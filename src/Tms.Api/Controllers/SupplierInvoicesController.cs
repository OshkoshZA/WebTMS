using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Loads;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateSupplierInvoiceRequest(Guid SubcontractorId, string SupplierInvoiceNumber, DateOnly InvoiceDate, DateOnly ReceivedDate, decimal Amount, Guid? CurrencyId = null);
public record MatchSupplierInvoiceRequest(IReadOnlyList<Guid> AccrualIds);
public record DisputeSupplierInvoiceRequest(string Reason);

public record SubcontractorExpenseResponse(
    Guid Id, Guid RateLineBuyId, Guid AccrualId, Guid FinancialPeriodId,
    decimal Amount, SubcontractorExpenseStatus Status, DateTimeOffset FinalizedDate);

public record SupplierInvoiceResponse(
    Guid Id, Guid SubcontractorId, Guid CurrencyId, string SupplierInvoiceNumber, DateOnly InvoiceDate, DateOnly ReceivedDate,
    decimal Amount, SupplierInvoiceStatus Status, string? DisputeReason,
    IReadOnlyList<SubcontractorExpenseResponse> Expenses);

public record MatchSupplierInvoiceResponse(SupplierInvoiceResponse Invoice, decimal VarianceAmount);

/// <summary>
/// Buy-side payables (docs/architecture.html §10.2) — the subcontractor's own invoice,
/// captured when it arrives and matched against the SubcontractorAccrual(s) that
/// LoadsController already raised at leg allocation. Matching is the only thing that
/// ever creates a SubcontractorExpense; nothing here creates one directly, and nothing
/// ever edits one afterward — a correction is always a new SupplierInvoice/match, the
/// same one-directional posting rule as everything else in §10.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/supplier-invoices")]
[Authorize]
public class SupplierInvoicesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public SupplierInvoicesController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SupplierInvoiceResponse>>> List(CancellationToken ct)
    {
        var invoices = await _db.SupplierInvoices.Include(si => si.Expenses).OrderByDescending(si => si.ReceivedDate).ToListAsync(ct);
        return Ok(invoices.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupplierInvoiceResponse>> Get(Guid id, CancellationToken ct)
    {
        var invoice = await _db.SupplierInvoices.Include(si => si.Expenses).FirstOrDefaultAsync(si => si.Id == id, ct);
        return invoice is null ? NotFound() : Ok(ToResponse(invoice));
    }

    /// <summary>
    /// Captures a subcontractor's own invoice as Received. SupplierInvoiceNumber is the
    /// carrier's own numbering, never generated here — so a duplicate capture of the
    /// same (Subcontractor, SupplierInvoiceNumber) pair for this Company is rejected
    /// outright rather than silently accepted twice.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "finance.subcontractorinvoice.process")]
    public async Task<ActionResult<SupplierInvoiceResponse>> Create(CreateSupplierInvoiceRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var subcontractor = await _db.Subcontractors.FirstOrDefaultAsync(s => s.Id == request.SubcontractorId, ct);
        if (subcontractor is null) return NotFound($"Subcontractor {request.SubcontractorId} was not found.");

        var currencyId = request.CurrencyId ?? subcontractor.CurrencyId;
        if (!await IsSubcontractorCurrencyAllowedAsync(subcontractor, currencyId, ct))
            return BadRequest($"Subcontractor is not permitted to transact in currency {currencyId} — add it via POST /subcontractors/{{id}}/currencies first.");

        if (await _db.SupplierInvoices.AnyAsync(
            si => si.SubcontractorId == request.SubcontractorId && si.SupplierInvoiceNumber == request.SupplierInvoiceNumber, ct))
            return Conflict($"Supplier invoice {request.SupplierInvoiceNumber} has already been captured for this subcontractor.");

        var invoice = new SupplierInvoice
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            SubcontractorId = request.SubcontractorId,
            CurrencyId = currencyId,
            SupplierInvoiceNumber = request.SupplierInvoiceNumber,
            InvoiceDate = request.InvoiceDate,
            ReceivedDate = request.ReceivedDate,
            Amount = request.Amount
        };
        _db.SupplierInvoices.Add(invoice);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // SupplierInvoiceNumberIndex (Company, Subcontractor, SupplierInvoiceNumber)
            // catches a race between two Create calls for the same invoice that both
            // passed the AnyAsync check above — turns that into a clean 409 instead of
            // a raw 500, same pattern as RolesController/UsersController.
            return Conflict($"Supplier invoice {request.SupplierInvoiceNumber} has already been captured for this subcontractor.");
        }

        return CreatedAtAction(nameof(Get), new { id = invoice.Id }, ToResponse(invoice));
    }

    /// <summary>
    /// Matches a Received SupplierInvoice against one or more of its subcontractor's
    /// open Accrued accruals (§10.2) — one-to-one for a single-leg invoice, one-to-many
    /// where a carrier bills several legs on one invoice. Atomically nets off every
    /// matched accrual and finalizes a SubcontractorExpense for each, apportioned from
    /// the invoice's actual Amount (not the accruals' estimates) so the total expensed
    /// always equals exactly what was invoiced. Any variance between the accrual
    /// estimate and the actual invoice is real and returned for review — it never
    /// blocks the match, same as the "flag then net anyway" path in the doc's own
    /// diagram (Fig. 7).
    /// </summary>
    [HttpPost("{id:guid}/match")]
    [Authorize(Policy = "finance.subcontractorinvoice.process")]
    public async Task<ActionResult<MatchSupplierInvoiceResponse>> Match(Guid id, MatchSupplierInvoiceRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (request.AccrualIds is null || request.AccrualIds.Count == 0)
            return BadRequest("At least one AccrualId is required.");

        var invoice = await _db.SupplierInvoices.Include(si => si.Expenses).FirstOrDefaultAsync(si => si.Id == id, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status != SupplierInvoiceStatus.Received)
            return Conflict($"Supplier invoice is {invoice.Status}; only a Received invoice can be matched.");

        var accrualIds = request.AccrualIds.Distinct().ToList();
        var accruals = await _db.Set<SubcontractorAccrual>().Where(a => accrualIds.Contains(a.Id)).ToListAsync(ct);
        if (accruals.Count != accrualIds.Count)
            return NotFound("One or more accruals were not found.");
        if (accruals.Any(a => a.SubcontractorId != invoice.SubcontractorId))
            return BadRequest("Every accrual must belong to the same subcontractor as the invoice.");
        if (accruals.Any(a => a.CurrencyId != invoice.CurrencyId))
            return BadRequest("Every accrual must be in the same currency as the invoice.");
        if (accruals.Any(a => a.Status != SubcontractorAccrualStatus.Accrued))
            return Conflict("Every accrual must still be Accrued — one has already been matched.");

        var openPeriod = await _db.FinancialPeriods.FirstOrDefaultAsync(
            p => p.CompanyId == _tenantContext.CompanyId && p.Status == FinancialPeriodStatus.Open, ct);
        if (openPeriod is null)
            return Conflict("No open financial period for this company — create one first (POST /api/v1/financial-years).");

        var totalEstimated = accruals.Sum(a => a.EstimatedAmount);
        var varianceAmount = invoice.Amount - totalEstimated;

        // Largest-remainder apportionment, in whole cents: floor each accrual's raw
        // share, then hand out the few cents left over to whichever lines had the
        // largest fractional remainder. Always sums to exactly invoice.Amount and
        // never produces a negative line — unlike the previous "last line absorbs the
        // rounding" approach, which a large variance spread across many accruals
        // could drive negative (each line's rounding can be off by up to half a cent;
        // over enough lines that adds up to real money).
        var totalCents = (long)Math.Round(invoice.Amount * 100m, 0, MidpointRounding.AwayFromZero);
        var hasWeight = totalEstimated != 0m;
        var centsShare = new long[accruals.Count];
        var remainders = new decimal[accruals.Count];
        var flooredSum = 0L;

        for (var i = 0; i < accruals.Count; i++)
        {
            var weight = hasWeight ? accruals[i].EstimatedAmount / totalEstimated : 1m / accruals.Count;
            var rawCents = totalCents * weight;
            var flooredCents = (long)Math.Floor(rawCents);
            centsShare[i] = flooredCents;
            remainders[i] = rawCents - flooredCents;
            flooredSum += flooredCents;
        }

        foreach (var index in Enumerable.Range(0, accruals.Count)
            .OrderByDescending(i => remainders[i])
            .Take((int)(totalCents - flooredSum)))
        {
            centsShare[index] += 1;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Atomically claims this invoice — only succeeds if it's still Received at the
        // instant of the UPDATE. Without this, two concurrent Match calls against the
        // same invoice but with *disjoint* AccrualIds (e.g. two AP clerks each matching
        // a different subset of legs) would both pass the in-memory Status check above
        // and both apportion the invoice's *full* Amount, double-posting the payable —
        // SubcontractorExpenseAccrualIndex only protects a shared accrual, not this
        // per-invoice case. Same pattern as DebriefApprovalService's accrual claims.
        var claimed = await _db.SupplierInvoices
            .Where(si => si.Id == id && si.Status == SupplierInvoiceStatus.Received)
            .ExecuteUpdateAsync(s => s.SetProperty(si => si.Status, SupplierInvoiceStatus.Matched), ct);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(ct);
            return Conflict("This supplier invoice was already resolved by a concurrent request.");
        }
        invoice.Status = SupplierInvoiceStatus.Matched; // keep the tracked entity in sync for the response DTO

        for (var i = 0; i < accruals.Count; i++)
        {
            var accrual = accruals[i];
            var expenseAmount = centsShare[i] / 100m;

            accrual.Status = SubcontractorAccrualStatus.Netted;

            // invoice was loaded (not newly Add()-ed), so it's already tracked as
            // Unchanged/Modified — appending only to its Expenses collection navigation
            // left EF unable to tell these were new rows, and it generated UPDATE
            // statements against nonexistent SubcontractorExpense rows instead of
            // INSERTs (a real 0-rows-affected DbUpdateConcurrencyException, caught live
            // testing this). Adding straight to the DbSet forces the correct Added state.
            _db.Set<SubcontractorExpense>().Add(new SubcontractorExpense
            {
                TenantId = _tenantContext.TenantId.Value,
                CompanyId = _tenantContext.CompanyId.Value,
                SubcontractorId = invoice.SubcontractorId,
                CurrencyId = invoice.CurrencyId,
                RateLineBuyId = accrual.RateLineBuyId,
                AccrualId = accrual.Id,
                SupplierInvoiceId = invoice.Id,
                FinancialPeriodId = openPeriod.Id,
                Amount = expenseAmount,
                Status = SubcontractorExpenseStatus.AvailableToExport
            });
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // SubcontractorExpenseAccrualIndex catches a race between two concurrent
            // Match calls that both read the same accrual as still Accrued before
            // either committed — turns a real double-net into a clean 409 instead of
            // a raw 500, same pattern as SupplierInvoiceNumberIndex above.
            await transaction.RollbackAsync(ct);
            return Conflict("One or more of these accruals were already matched by a concurrent request.");
        }

        // Reload so the response reflects the SubcontractorExpense rows just inserted —
        // invoice.Expenses (populated only by the collection-navigation Add we removed
        // above) would otherwise come back empty even though the insert succeeded.
        await _db.Entry(invoice).Collection(si => si.Expenses).LoadAsync(ct);

        return Ok(new MatchSupplierInvoiceResponse(ToResponse(invoice), varianceAmount));
    }

    /// <summary>Flags a Received invoice as contested rather than matching it — e.g. the amount or the legs it claims don't check out. Only reachable from Received; a Matched invoice is corrected by a new SupplierInvoice, never edited in place.</summary>
    [HttpPost("{id:guid}/dispute")]
    [Authorize(Policy = "finance.subcontractorinvoice.process")]
    public async Task<IActionResult> Dispute(Guid id, DisputeSupplierInvoiceRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var invoice = await _db.SupplierInvoices.FirstOrDefaultAsync(si => si.Id == id, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status != SupplierInvoiceStatus.Received)
            return Conflict($"Supplier invoice is {invoice.Status}; only a Received invoice can be disputed.");

        // Atomically claims this invoice — closes a race against a concurrent Match
        // call on the same invoice: without this, Dispute could read Status ==
        // Received, lose the race to a Match that nets the accruals and commits first,
        // then still blindly overwrite Status to Disputed with no error — leaving an
        // invoice marked Disputed even though its accruals are already Netted and its
        // expenses already posted. Same claim pattern as Match itself.
        var claimed = await _db.SupplierInvoices
            .Where(si => si.Id == id && si.Status == SupplierInvoiceStatus.Received)
            .ExecuteUpdateAsync(s => s
                .SetProperty(si => si.Status, SupplierInvoiceStatus.Disputed)
                .SetProperty(si => si.DisputeReason, request.Reason), ct);

        if (claimed == 0)
            return Conflict("This supplier invoice was already resolved by a concurrent request.");

        return NoContent();
    }

    /// <summary>Whether a Subcontractor is permitted to transact in a given currency (§4.3) — its own primary CurrencyId, or an explicit SubcontractorCurrency allow-list row.</summary>
    private async Task<bool> IsSubcontractorCurrencyAllowedAsync(Subcontractor subcontractor, Guid currencyId, CancellationToken ct) =>
        currencyId == subcontractor.CurrencyId
        || await _db.Set<SubcontractorCurrency>().AnyAsync(sc => sc.SubcontractorId == subcontractor.Id && sc.CurrencyId == currencyId, ct);

    private static SupplierInvoiceResponse ToResponse(SupplierInvoice invoice) => new(
        invoice.Id, invoice.SubcontractorId, invoice.CurrencyId, invoice.SupplierInvoiceNumber, invoice.InvoiceDate, invoice.ReceivedDate,
        invoice.Amount, invoice.Status, invoice.DisputeReason,
        invoice.Expenses.Select(e => new SubcontractorExpenseResponse(
            e.Id, e.RateLineBuyId, e.AccrualId, e.FinancialPeriodId, e.Amount, e.Status, e.FinalizedDate)).ToList());
}
