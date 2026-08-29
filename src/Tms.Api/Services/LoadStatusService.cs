using Tms.Infrastructure;
using Tms.Modules.Audit;
using Tms.Modules.Loads;

namespace Tms.Api.Services;

/// <summary>
/// Load.Status rollup from its legs' individual statuses (§5.2) — extracted out of
/// LoadsController so the Debrief-related actions (§09) can trigger the exact same
/// recompute when a leg reaches PodReceived, without duplicating the rollup logic or
/// risking it drifting out of sync between call sites.
/// </summary>
public class LoadStatusService
{
    private readonly TmsDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;

    public LoadStatusService(TmsDbContext db, ICurrentUserAccessor currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Recomputes Load.Status from its legs' individual statuses and, if it changed,
    /// records the transition. Never touches On Hold or Cancelled — those are
    /// exception states only a manual action (Hold/ReleaseHold/Cancel) can enter or
    /// leave.
    /// </summary>
    public async Task RecomputeAsync(Load load, CancellationToken ct)
    {
        if (load.Status is LoadStatus.OnHold or LoadStatus.Cancelled)
            return;

        var next = ComputeStatusFromLegs(load.Legs);
        if (next != load.Status)
            await TransitionAsync(load, next, reason: null, ct);
    }

    /// <summary>Pure leg-status rollup (§5.2) — shared by RecomputeAsync and LoadsController.ReleaseHold, which need the same answer but at different points relative to the On-Hold guard.</summary>
    public static LoadStatus ComputeStatusFromLegs(IReadOnlyCollection<LoadLeg> legs) =>
        legs.Count == 0
            ? LoadStatus.Booked
            : legs.All(l => l.Status == LoadLegStatus.PodReceived) ? LoadStatus.PodReceived
            : legs.All(l => l.Status is LoadLegStatus.Delivered or LoadLegStatus.PodReceived) ? LoadStatus.Delivered
            : legs.Any(l => l.Status is LoadLegStatus.InTransit or LoadLegStatus.Delivered or LoadLegStatus.PodReceived) ? LoadStatus.InTransit
            : legs.All(l => l.Status == LoadLegStatus.Allocated) ? LoadStatus.Allocated
            : LoadStatus.Booked;

    public Task TransitionAsync(Load load, LoadStatus next, string? reason, CancellationToken ct)
    {
        _db.LoadStatusHistories.Add(new LoadStatusHistory
        {
            TenantId = load.TenantId,
            CompanyId = load.CompanyId,
            LoadId = load.Id,
            FromStatus = load.Status,
            ToStatus = next,
            ChangedByUserId = _currentUser.UserId ?? Guid.Empty,
            Reason = reason
        });
        load.Status = next;
        return Task.CompletedTask;
    }
}
