using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Loads;
using Tms.Modules.Rating;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record UnconvertedMarginAmount(Guid CurrencyId, string Side, decimal Amount);

public record MarginSummaryResponse(
    Guid ReportingCurrencyId, decimal SellTotal, decimal BuyTotal, decimal Margin,
    IReadOnlyList<UnconvertedMarginAmount> Unconverted);

public record CurrencyExposureTotal(
    Guid CurrencyId, decimal TotalCreditLimit, decimal TotalArOutstanding, decimal TotalWip, decimal TotalExposure);

public record CreditExposureSummaryResponse(IReadOnlyList<CurrencyExposureTotal> ByCurrency);

public record PayablesSummaryResponse(int Accrued, int AvailableToExport, int Exported, int Paid);

public record AgedDebtorsSummaryResponse(IReadOnlyList<CurrencyAgingBuckets> ByCurrency);

public record OnTimeDeliverySummaryResponse(int OnTimeCount, int LateCount, decimal? OnTimeRatePercent);

/// <summary>
/// Company-wide KPI aggregates for the internal dashboard's own "known, bounded gap"
/// tiles (docs/architecture.html §16.2) — a read surface over entities already defined
/// elsewhere, never a parallel reporting database, same framing as every other
/// dashboard in this design (Fig. 13). Every endpoint here is staff-only; none of
/// these figures are ever part of either portal's own scoped-down dashboard
/// (§16.3/§16.4), which deliberately never exposes internal margin/credit data.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private static readonly LoadStatus[] WipStatuses =
    {
        LoadStatus.Booked,
        LoadStatus.Allocated,
        LoadStatus.InTransit,
        LoadStatus.Delivered,
        LoadStatus.OnHold
    };

    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly DebtorsAgingService _aging;

    public DashboardController(TmsDbContext db, ITenantContext tenantContext, DebtorsAgingService aging)
    {
        _db = db;
        _tenantContext = tenantContext;
        _aging = aging;
    }

    /// <summary>
    /// Sell/buy margin summed across every commodity-line RateLine company-wide,
    /// converted to the caller's own Company.CurrencyId (§04) using the most recently
    /// captured exchange rate for each source currency — a live snapshot figure, not
    /// the per-transaction historical rate GET /loads/{id}/margin anchors to a load's
    /// own PickupWindowStart, since repeating that per-leg lookup company-wide would
    /// mean one query per leg rather than one per distinct currency. A currency with
    /// no captured rate to the reporting currency is excluded from the totals and
    /// listed in Unconverted instead of silently mis-stating the aggregate.
    /// </summary>
    [HttpGet("margin-summary")]
    public async Task<ActionResult<MarginSummaryResponse>> MarginSummary(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();
        if (_tenantContext.CompanyId is null) return Unauthorized("Request is missing a resolved Company context.");

        var company = await _db.Companies.FirstAsync(c => c.Id == _tenantContext.CompanyId, ct);
        var reportingCurrencyId = company.CurrencyId;

        var sellByCurrency = await _db.RateLines
            .Where(r => r.SourceType == RateLineSourceType.CommodityLine && r.Direction == RateLineDirection.Sell)
            .GroupBy(r => r.CurrencyId)
            .Select(g => new { CurrencyId = g.Key, Total = g.Sum(r => r.Amount) })
            .ToListAsync(ct);

        var buyByCurrency = await _db.RateLines
            .Where(r => r.SourceType == RateLineSourceType.CommodityLine && r.Direction == RateLineDirection.Buy)
            .GroupBy(r => r.CurrencyId)
            .Select(g => new { CurrencyId = g.Key, Total = g.Sum(r => r.Amount) })
            .ToListAsync(ct);

        async Task<decimal?> ConvertAsync(Guid currencyId, decimal amount)
        {
            if (currencyId == reportingCurrencyId) return amount;
            var rate = await _db.ExchangeRates
                .Where(e => e.FromCurrencyId == currencyId && e.ToCurrencyId == reportingCurrencyId)
                .OrderByDescending(e => e.EffectiveDate)
                .FirstOrDefaultAsync(ct);
            return rate is null ? null : amount * rate.Rate;
        }

        var unconverted = new List<UnconvertedMarginAmount>();
        decimal sellTotal = 0, buyTotal = 0;

        foreach (var bucket in sellByCurrency)
        {
            var converted = await ConvertAsync(bucket.CurrencyId, bucket.Total);
            if (converted is null) unconverted.Add(new UnconvertedMarginAmount(bucket.CurrencyId, "Sell", bucket.Total));
            else sellTotal += converted.Value;
        }
        foreach (var bucket in buyByCurrency)
        {
            var converted = await ConvertAsync(bucket.CurrencyId, bucket.Total);
            if (converted is null) unconverted.Add(new UnconvertedMarginAmount(bucket.CurrencyId, "Buy", bucket.Total));
            else buyTotal += converted.Value;
        }

        return Ok(new MarginSummaryResponse(reportingCurrencyId, sellTotal, buyTotal, sellTotal - buyTotal, unconverted));
    }

    /// <summary>
    /// Credit exposure (§5.4) summed across every Client, grouped by currency — never
    /// blended across currencies, the same rule CreditExposureService applies per
    /// client. Reimplemented in bulk here (grouped queries) rather than looping
    /// CreditExposureService once per client, which would be one query per client
    /// company-wide.
    /// </summary>
    [HttpGet("credit-exposure-summary")]
    public async Task<ActionResult<CreditExposureSummaryResponse>> CreditExposureSummary(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var primaryLimits = await _db.Clients
            .GroupBy(c => c.CurrencyId)
            .Select(g => new { CurrencyId = g.Key, Total = g.Sum(c => c.CreditLimit) })
            .ToListAsync(ct);

        var additionalLimits = await _db.ClientCurrencies
            .GroupBy(cc => cc.CurrencyId)
            .Select(g => new { CurrencyId = g.Key, Total = g.Sum(cc => cc.CreditLimit) })
            .ToListAsync(ct);

        var arByCurrency = await _db.Invoices
            .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartPaid)
            .GroupBy(i => i.CurrencyId)
            .Select(g => new { CurrencyId = g.Key, Total = g.Sum(i => i.TotalIncVat) })
            .ToListAsync(ct);

        var creditedByCurrency = await _db.CreditNotes
            .Where(cn => cn.Status == CreditNoteStatus.Issued)
            .GroupBy(cn => cn.CurrencyId)
            .Select(g => new { CurrencyId = g.Key, Total = g.Sum(cn => cn.TotalAmount) })
            .ToListAsync(ct);

        var invoicedRateLineIds = _db.InvoiceLines.Select(l => l.RateLineSellId);
        var wipByCurrency = await _db.RateLines
            .Where(r => r.Direction == RateLineDirection.Sell && r.SourceType == RateLineSourceType.CommodityLine)
            .Where(r => !invoicedRateLineIds.Contains(r.Id))
            .Join(_db.CommodityLines, r => r.SourceId, cl => cl.Id, (r, cl) => new { r, cl.LoadLegId })
            .Join(_db.LoadLegs, x => x.LoadLegId, leg => leg.Id, (x, leg) => new { x.r, leg.LoadId })
            .Join(_db.Loads, x => x.LoadId, load => load.Id, (x, load) => new { x.r, load.Status })
            .Where(x => WipStatuses.Contains(x.Status))
            .GroupBy(x => x.r.CurrencyId)
            .Select(g => new { CurrencyId = g.Key, Total = g.Sum(x => x.r.Amount) })
            .ToListAsync(ct);

        var currencyIds = primaryLimits.Select(x => x.CurrencyId)
            .Concat(additionalLimits.Select(x => x.CurrencyId))
            .Concat(arByCurrency.Select(x => x.CurrencyId))
            .Concat(wipByCurrency.Select(x => x.CurrencyId))
            .Distinct();

        var byCurrency = currencyIds.Select(currencyId =>
        {
            var creditLimit = (primaryLimits.FirstOrDefault(x => x.CurrencyId == currencyId)?.Total ?? 0)
                + (additionalLimits.FirstOrDefault(x => x.CurrencyId == currencyId)?.Total ?? 0);
            var ar = (arByCurrency.FirstOrDefault(x => x.CurrencyId == currencyId)?.Total ?? 0)
                - (creditedByCurrency.FirstOrDefault(x => x.CurrencyId == currencyId)?.Total ?? 0);
            var wip = wipByCurrency.FirstOrDefault(x => x.CurrencyId == currencyId)?.Total ?? 0;
            return new CurrencyExposureTotal(currencyId, creditLimit, ar, wip, ar + wip);
        }).ToList();

        return Ok(new CreditExposureSummaryResponse(byCurrency));
    }

    /// <summary>
    /// Subcontractor payables pipeline counts (§10.2) — the same Accrued/
    /// AvailableToExport/Exported/Paid tally the Supplier Portal's own dashboard
    /// (§16.4) already computes client-side over one subcontractor's own small lists;
    /// company-wide that would mean fetching every accrual/expense just to count them,
    /// so this counts server-side instead.
    /// </summary>
    [HttpGet("payables-summary")]
    public async Task<ActionResult<PayablesSummaryResponse>> PayablesSummary(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var accrued = await _db.SubcontractorAccruals.CountAsync(a => a.Status == SubcontractorAccrualStatus.Accrued, ct);
        var availableToExport = await _db.SubcontractorExpenses.CountAsync(e => e.Status == SubcontractorExpenseStatus.AvailableToExport, ct);
        var exported = await _db.SubcontractorExpenses.CountAsync(e => e.Status == SubcontractorExpenseStatus.Exported, ct);
        var paid = await _db.SubcontractorExpenses.CountAsync(e => e.Status == SubcontractorExpenseStatus.Paid, ct);

        return Ok(new PayablesSummaryResponse(accrued, availableToExport, exported, paid));
    }

    /// <summary>
    /// Aged debtors, computed live as of today rather than read from whatever the last
    /// period close happened to snapshot (§10.3, §16.2) — grouped by currency, never
    /// blended (§4.3), and with none of ClientsController.LiveAging's own primary-
    /// currency-only limitation (see DebtorsAgingService's own doc comment).
    /// </summary>
    [HttpGet("aged-debtors-summary")]
    public async Task<ActionResult<AgedDebtorsSummaryResponse>> AgedDebtorsSummary(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var buckets = await _aging.ComputeCompanyWideByCurrencyAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct);
        return Ok(new AgedDebtorsSummaryResponse(buckets));
    }

    /// <summary>
    /// The share of Delivered loads that reached Delivered at or before their own
    /// promised DeliveryWindowEnd (§16.2) — both halves of "promised vs. actual" already
    /// existed in the schema (Load.DeliveryWindowEnd, and LoadStatusHistory's own
    /// ChangedAt for the transition to Delivered), just never connected to each other. A
    /// load with no DeliveryWindowEnd set can't be judged either way and is excluded
    /// from both the counts and the rate entirely, rather than counted as on-time by
    /// default or penalized for a promise that was never made. OnTimeRatePercent is null
    /// (not 0) when there's nothing ratable yet, so the frontend can tell "no data" apart
    /// from "a real 0%".
    /// </summary>
    [HttpGet("on-time-delivery-summary")]
    public async Task<ActionResult<OnTimeDeliverySummaryResponse>> OnTimeDeliverySummary(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var deliveredAtByLoadId = await _db.LoadStatusHistories
            .Where(h => h.ToStatus == LoadStatus.Delivered)
            .GroupBy(h => h.LoadId)
            .Select(g => new { LoadId = g.Key, DeliveredAt = g.Min(h => h.ChangedAt) })
            .ToDictionaryAsync(x => x.LoadId, x => x.DeliveredAt, ct);

        var deliveredLoadIds = deliveredAtByLoadId.Keys.ToList();
        var ratableLoads = await _db.Loads
            .Where(l => l.DeliveryWindowEnd != null && deliveredLoadIds.Contains(l.Id))
            .Select(l => new { l.Id, l.DeliveryWindowEnd })
            .ToListAsync(ct);

        var onTimeCount = 0;
        var lateCount = 0;
        foreach (var load in ratableLoads)
        {
            if (deliveredAtByLoadId[load.Id] <= load.DeliveryWindowEnd!.Value) onTimeCount++;
            else lateCount++;
        }

        var total = onTimeCount + lateCount;
        var ratePercent = total == 0 ? (decimal?)null : Math.Round(100m * onTimeCount / total, 1);

        return Ok(new OnTimeDeliverySummaryResponse(onTimeCount, lateCount, ratePercent));
    }
}
