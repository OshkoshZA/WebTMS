using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Audit;
using Tms.Modules.Billing;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record DebtorsAgingSnapshotResponse(
    Guid Id, Guid ClientId, decimal CurrentAmount, decimal Days30, decimal Days60, decimal Days90, decimal Days90Plus,
    decimal TotalOutstanding, DateTimeOffset SnapshotDate);

/// <summary>
/// A FinancialPeriod's lifecycle (docs/architecture.html §10.3) — Future -> Open ->
/// Closed, one-directional like every other approval boundary in this design. Creating
/// years/periods is FinancialYearsController's concern; this is purely the close action
/// and its debtors-aging sub-resource.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/financial-periods")]
[Authorize]
public class FinancialPeriodsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ITenantContext _tenantContext;

    public FinancialPeriodsController(TmsDbContext db, ICurrentUserAccessor currentUser, ITenantContext tenantContext)
    {
        _db = db;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
    }

    /// <summary>Never part of either portal's documented scope — the Company's own financial-calendar structure, so any portal contact is Forbidden outright, same as DebtorsAging below.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<FinancialPeriodResponse>>> List([FromQuery] Guid? financialYearId, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var query = _db.FinancialPeriods.AsQueryable();
        if (financialYearId is Guid yearId) query = query.Where(p => p.FinancialYearId == yearId);

        var periods = await query.OrderBy(p => p.StartDate).ToListAsync(ct);
        return Ok(periods.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FinancialPeriodResponse>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var period = await _db.FinancialPeriods.FirstOrDefaultAsync(p => p.Id == id, ct);
        return period is null ? NotFound() : Ok(ToResponse(period));
    }

    /// <summary>
    /// Closes an Open period and opens the next one as a single operation (§10.3), so
    /// there is never a gap with nothing open to post into. Also writes every Client's
    /// DebtorsAgingSnapshot for this period, rolling their prior snapshot's buckets
    /// forward one step. The pre-close checklist described in §10.3 (draft invoices,
    /// unmatched accruals, open exceptions) is deferred until Invoice/SubcontractorAccrual/
    /// Exception exist to actually check — nothing to gate on yet.
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = "finance.period.close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        // Unlike every other mutating action in this codebase, this controller never
        // checked Tenant/Company were actually resolved before touching data — the
        // global query filter degrades to tenant-only scoping when CompanyId is null
        // (§4.1), which would let Close act on a different company's period in the
        // same tenant. Matches the guard every sibling controller already has.
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var period = await _db.FinancialPeriods.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (period is null) return NotFound();
        if (period.Status != FinancialPeriodStatus.Open)
            return Conflict($"Period is {period.Status}; only an Open period can be closed.");

        var nextPeriod = await ResolveNextPeriodAsync(period, ct);
        if (nextPeriod is null)
        {
            return Conflict(
                "No Future period follows this one. Create the next FinancialYear (POST /financial-years) before closing the last period of this one.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Atomically claims this period — only succeeds if it's still Open at the
        // instant of the UPDATE. Without this, two concurrent Close calls for the same
        // period both pass the in-memory Status check above and each write a full,
        // duplicate set of DebtorsAgingSnapshot rows below — no unique index protects
        // that table, so a plain double-insert goes undetected rather than throwing.
        // FinancialPeriodOneOpenPerCompanyIndex alone doesn't catch this: it only
        // guards two *different* periods becoming Open at once, not the same period
        // being closed twice. Deliberately not mirrored onto the tracked `period`
        // entity below — it still carries the stale (unset) ClosedAt/ClosedByUserId in
        // memory, and marking it Modified would make SaveChangesAsync overwrite the
        // values this claim just wrote with those stale nulls.
        var claimed = await _db.FinancialPeriods
            .Where(p => p.Id == id && p.Status == FinancialPeriodStatus.Open)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, FinancialPeriodStatus.Closed)
                .SetProperty(p => p.ClosedAt, DateTimeOffset.UtcNow)
                .SetProperty(p => p.ClosedByUserId, _currentUser.UserId), ct);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(ct);
            return Conflict("This period was already closed by a concurrent request.");
        }

        nextPeriod.Status = FinancialPeriodStatus.Open;

        if (period.FinancialYearId != nextPeriod.FinancialYearId)
        {
            // The period just closed was the last one in its year — the year closes
            // with it, and the year the next period belongs to opens in the same
            // one-directional move.
            var year = await _db.FinancialYears.FirstAsync(y => y.Id == period.FinancialYearId, ct);
            var nextYear = await _db.FinancialYears.FirstAsync(y => y.Id == nextPeriod.FinancialYearId, ct);
            year.Status = FinancialYearStatus.Closed;
            nextYear.Status = FinancialYearStatus.Open;
        }

        await WriteDebtorsAgingSnapshotsAsync(period, ct);

        try
        {
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // FinancialPeriodOneOpenPerCompanyIndex catches a race between two
            // concurrent Close (or Close/Create) calls that both read "no other Open
            // period" before either committed — turns a real double-open into a clean
            // 409 instead of a raw 500.
            await transaction.RollbackAsync(ct);
            return Conflict("Another period was already opened by a concurrent request.");
        }

        return NoContent();
    }

    /// <summary>Never part of either portal's documented scope — returns every Client's aged-debt buckets in one call with no per-client filter, so any portal contact is Forbidden outright (a Customer Portal contact's own AR view is ClientsController.CreditStatus instead).</summary>
    [HttpGet("{id:guid}/debtors-aging")]
    public async Task<ActionResult<IEnumerable<DebtorsAgingSnapshotResponse>>> DebtorsAging(Guid id, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();
        if (!await _db.FinancialPeriods.AnyAsync(p => p.Id == id, ct))
            return NotFound();

        var snapshots = await _db.DebtorsAgingSnapshots
            .Where(s => s.FinancialPeriodId == id)
            .OrderBy(s => s.ClientId)
            .Select(s => new DebtorsAgingSnapshotResponse(
                s.Id, s.ClientId, s.CurrentAmount, s.Days30, s.Days60, s.Days90, s.Days90Plus, s.TotalOutstanding, s.SnapshotDate))
            .ToListAsync(ct);

        return Ok(snapshots);
    }

    /// <summary>
    /// The next period in sequence — the same year's next PeriodNumber, or period 1 of
    /// the FinancialYear whose StartDate picks up exactly where this one's EndDate
    /// leaves off, if this was the last period of its year. Requires true contiguity
    /// (StartDate == EndDate + 1 day), not just "chronologically closest" — otherwise a
    /// year created out of order, or with a gap before it, could get skipped into
    /// straight past however many years should have come between (confirmed live: an
    /// unrelated year starting years later was picked up as "next" before this fix).
    /// </summary>
    private async Task<FinancialPeriod?> ResolveNextPeriodAsync(FinancialPeriod period, CancellationToken ct)
    {
        var withinYear = await _db.FinancialPeriods.FirstOrDefaultAsync(
            p => p.FinancialYearId == period.FinancialYearId && p.PeriodNumber == period.PeriodNumber + 1, ct);
        if (withinYear is not null) return withinYear;

        var expectedNextStart = period.EndDate.AddDays(1);
        var nextYear = await _db.FinancialYears
            .FirstOrDefaultAsync(y => y.CompanyId == period.CompanyId && y.StartDate == expectedNextStart, ct);
        if (nextYear is null) return null;

        return await _db.FinancialPeriods.FirstOrDefaultAsync(p => p.FinancialYearId == nextYear.Id && p.PeriodNumber == 1, ct);
    }

    /// <summary>Rolls every Client's aged-debtors bucket forward one step (§10.3: Current->30->60->90->90+, 90+ stays).</summary>
    private async Task WriteDebtorsAgingSnapshotsAsync(FinancialPeriod closingPeriod, CancellationToken ct)
    {
        var clientIds = await _db.Clients.Select(c => c.Id).ToListAsync(ct);

        foreach (var clientId in clientIds)
        {
            var prior = await _db.DebtorsAgingSnapshots
                .Where(s => s.ClientId == clientId)
                .OrderByDescending(s => s.SnapshotDate)
                .FirstOrDefaultAsync(ct);

            // TODO (§10.1): "new Current" is the sum of invoices raised in the period
            // just closed — 0 until Invoice exists. See the class doc comment on
            // DebtorsAgingSnapshot.
            var snapshot = new DebtorsAgingSnapshot
            {
                TenantId = closingPeriod.TenantId,
                CompanyId = closingPeriod.CompanyId,
                ClientId = clientId,
                FinancialPeriodId = closingPeriod.Id,
                CurrentAmount = 0m,
                Days30 = prior?.CurrentAmount ?? 0m,
                Days60 = prior?.Days30 ?? 0m,
                Days90 = prior?.Days60 ?? 0m,
                Days90Plus = (prior?.Days90 ?? 0m) + (prior?.Days90Plus ?? 0m)
            };
            snapshot.TotalOutstanding = snapshot.CurrentAmount + snapshot.Days30 + snapshot.Days60 + snapshot.Days90 + snapshot.Days90Plus;

            _db.DebtorsAgingSnapshots.Add(snapshot);
        }
    }

    private static FinancialPeriodResponse ToResponse(FinancialPeriod p) =>
        new(p.Id, p.PeriodNumber, p.Name, p.StartDate, p.EndDate, p.Status, p.ClosedAt);
}
