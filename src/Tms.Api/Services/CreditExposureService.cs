using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Loads;
using Tms.Modules.Rating;

namespace Tms.Api.Services;

public record CreditStatus(
    decimal CreditLimit,
    decimal ArOutstanding,
    decimal Wip,
    decimal TotalExposure,
    decimal AvailableCredit);

/// <summary>
/// Implements the credit control formula from docs/architecture.html §5.4:
/// Total Exposure = AR Outstanding + WIP; Available Credit = CreditLimit − Total Exposure.
///
/// AR Outstanding is now real: Issued/PartPaid Invoice balances for the client (§10.1),
/// net of credit notes — CreditNote doesn't exist yet (a later phase of Billing), so
/// there's nothing to net against until it does. WIP is the sell value of the client's
/// not-yet-invoiced loads — a CommodityLine's sell RateLine is excluded the moment it's
/// referenced by an InvoiceLine, so a load moves from WIP to AR one line at a time as
/// each of its commodity lines gets billed, not all-or-nothing at the load level.
///
/// KNOWN LIMITATION (docs/architecture.html §5.4): this reads exposure with no lock
/// and no elevated isolation, so it is not atomic with the SaveChangesAsync the caller
/// runs afterward. Two genuinely concurrent writes against the same client can each
/// read exposure before the other commits and both pass the hard-stop check, together
/// exceeding CreditLimit. Accepted as a Phase 1 gap — closing it means row-locking or
/// a serializable transaction around the check-and-save, at the cost of added lock
/// contention on every load/commodity-line write.
/// </summary>
public class CreditExposureService
{
    private static readonly LoadStatus[] WipStatuses =
    {
        LoadStatus.Booked,
        LoadStatus.Allocated,
        LoadStatus.InTransit,
        LoadStatus.Delivered,
        LoadStatus.PodReceived,
        LoadStatus.OnHold
    };

    private readonly TmsDbContext _db;

    public CreditExposureService(TmsDbContext db)
    {
        _db = db;
    }

    public async Task<CreditStatus> GetStatusAsync(Client client, CancellationToken ct)
    {
        var arOutstanding = await _db.Set<Invoice>()
            .Where(i => i.ClientId == client.Id && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartPaid))
            .SumAsync(i => i.TotalIncVat, ct);

        var invoicedRateLineIds = _db.Set<InvoiceLine>().Select(l => l.RateLineSellId);

        var wip = await _db.Set<RateLine>()
            .Where(r => r.Direction == RateLineDirection.Sell && r.SourceType == RateLineSourceType.CommodityLine)
            .Where(r => !invoicedRateLineIds.Contains(r.Id))
            .Join(_db.Set<CommodityLine>(), r => r.SourceId, cl => cl.Id, (r, cl) => new { r, cl })
            .Join(_db.Set<LoadLeg>(), x => x.cl.LoadLegId, leg => leg.Id, (x, leg) => new { x.r, leg })
            .Join(_db.Set<Load>(), x => x.leg.LoadId, load => load.Id, (x, load) => new { x.r, load })
            .Where(x => x.load.ClientId == client.Id && WipStatuses.Contains(x.load.Status))
            .SumAsync(x => x.r.Amount, ct);

        var totalExposure = arOutstanding + wip;

        return new CreditStatus(
            CreditLimit: client.CreditLimit,
            ArOutstanding: arOutstanding,
            Wip: wip,
            TotalExposure: totalExposure,
            AvailableCredit: client.CreditLimit - totalExposure);
    }

    /// <summary>True if adding <paramref name="additionalAmount"/> would push Total Exposure above CreditLimit (§5.4's hard stop).</summary>
    public static bool WouldBreach(CreditStatus status, decimal additionalAmount) =>
        status.TotalExposure + additionalAmount > status.CreditLimit;
}
