using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
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
/// AR Outstanding (issued/part-paid invoice balances) is hardcoded to zero for now —
/// Tms.Modules.Billing (§10.1) doesn't exist yet, so there is nothing to sum. WIP is
/// real: the sell value of every one of the client's loads that hasn't reached
/// Invoiced yet, computed directly from CommodityLine sell RateLines, which already
/// exist in Phase 1. Swap in the real AR figure here — and nowhere else — once
/// Billing lands; every caller of this service stays correct automatically.
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
        // TODO (§10.1): once Invoice exists, sum Issued/PartPaid balances for this
        // client here, net of credit notes. Zero is correct only until then.
        const decimal arOutstanding = 0m;

        var wip = await _db.Set<RateLine>()
            .Where(r => r.Direction == RateLineDirection.Sell && r.SourceType == RateLineSourceType.CommodityLine)
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
