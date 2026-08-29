using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateFinancialYearRequest(string YearLabel, DateOnly StartDate, DateOnly EndDate, int PeriodCount = 12);

public record FinancialPeriodResponse(
    Guid Id, int PeriodNumber, string Name, DateOnly StartDate, DateOnly EndDate, FinancialPeriodStatus Status, DateTimeOffset? ClosedAt);

public record FinancialYearResponse(
    Guid Id, string YearLabel, DateOnly StartDate, DateOnly EndDate, FinancialYearStatus Status, IReadOnlyList<FinancialPeriodResponse> Periods);

/// <summary>
/// A Company's financial calendar (docs/architecture.html §10.3) — a FinancialYear
/// divided into FinancialPeriods that Invoice/CreditNote/SubcontractorExpense (later
/// phases of this module) post against. Only Create lives here; a period's lifecycle
/// (Open -> Closed) is FinancialPeriodsController's concern.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/financial-years")]
[Authorize]
public class FinancialYearsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public FinancialYearsController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FinancialYearResponse>>> List(CancellationToken ct)
    {
        var years = await _db.FinancialYears.Include(y => y.Periods).OrderBy(y => y.StartDate).ToListAsync(ct);
        return Ok(years.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FinancialYearResponse>> Get(Guid id, CancellationToken ct)
    {
        var year = await _db.FinancialYears.Include(y => y.Periods).FirstOrDefaultAsync(y => y.Id == id, ct);
        return year is null ? NotFound() : Ok(ToResponse(year));
    }

    /// <summary>
    /// Creates a year and divides it into PeriodCount calendar-month periods (the last
    /// absorbing any remainder so it always ends exactly on EndDate). Opens immediately
    /// only if the Company has no other Open period right now (§10.3: exactly one is
    /// ever Open) — otherwise it sits Future until FinancialPeriodsController.Close
    /// rolls into its first period, which is exactly why creating next year ahead of
    /// time is supported at all: closing the current year's last period would otherwise
    /// have nowhere to roll into.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "finance.calendar.manage")]
    public async Task<ActionResult<FinancialYearResponse>> Create(CreateFinancialYearRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (request.PeriodCount is < 1 or > 13)
            return BadRequest("PeriodCount must be between 1 and 13.");
        if (request.EndDate <= request.StartDate)
            return BadRequest("EndDate must be after StartDate.");

        // Keeps the calendar gap-free at creation time, not just at close time
        // (FinancialPeriodsController.Close's rollover requires the same contiguity) —
        // a year starting anywhere but immediately after the latest one would otherwise
        // leave the in-between dates with no period for anything to post into.
        var latestYear = await _db.FinancialYears
            .Where(y => y.CompanyId == _tenantContext.CompanyId)
            .OrderByDescending(y => y.EndDate)
            .FirstOrDefaultAsync(ct);
        if (latestYear is not null && request.StartDate != latestYear.EndDate.AddDays(1))
        {
            return BadRequest(
                $"StartDate must be {latestYear.EndDate.AddDays(1):yyyy-MM-dd} — the day after the latest FinancialYear ({latestYear.YearLabel}) ends — to keep the calendar contiguous.");
        }

        var hasOpenPeriod = await _db.FinancialPeriods.AnyAsync(
            p => p.CompanyId == _tenantContext.CompanyId && p.Status == FinancialPeriodStatus.Open, ct);

        var year = new FinancialYear
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            YearLabel = request.YearLabel,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = hasOpenPeriod ? FinancialYearStatus.Future : FinancialYearStatus.Open
        };

        var periodStart = request.StartDate;
        for (var i = 1; i <= request.PeriodCount; i++)
        {
            var periodEnd = i == request.PeriodCount ? request.EndDate : periodStart.AddMonths(1).AddDays(-1);

            year.Periods.Add(new FinancialPeriod
            {
                TenantId = year.TenantId,
                CompanyId = year.CompanyId,
                PeriodNumber = i,
                Name = $"{request.YearLabel} P{i}",
                StartDate = periodStart,
                EndDate = periodEnd,
                Status = !hasOpenPeriod && i == 1 ? FinancialPeriodStatus.Open : FinancialPeriodStatus.Future
            });

            periodStart = periodEnd.AddDays(1);
        }

        _db.FinancialYears.Add(year);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // FinancialPeriodOneOpenPerCompanyIndex catches a race between two
            // concurrent Create calls that both read "no Open period yet" before
            // either committed — turns a real double-open into a clean 409 instead of
            // a raw 500.
            return Conflict("Another financial year/period was already opened for this company by a concurrent request.");
        }

        return CreatedAtAction(nameof(Get), new { id = year.Id }, ToResponse(year));
    }

    private static FinancialYearResponse ToResponse(FinancialYear year) => new(
        year.Id, year.YearLabel, year.StartDate, year.EndDate, year.Status,
        year.Periods.OrderBy(p => p.PeriodNumber)
            .Select(p => new FinancialPeriodResponse(p.Id, p.PeriodNumber, p.Name, p.StartDate, p.EndDate, p.Status, p.ClosedAt))
            .ToList());
}
