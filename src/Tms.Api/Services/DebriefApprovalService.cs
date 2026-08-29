using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Debrief;
using Tms.Modules.Loads;

namespace Tms.Api.Services;

/// <summary>
/// Approving a Debrief (§09, Fig. 5) — shared by LegsController.SubmitDebrief's
/// auto-approve path and DebriefsController.Approve's clerk-resolved path, since both
/// have exactly the same effect: apply every SubcontractorAccrual-claimed expense line
/// to its named accrual ("Debrief-approved extras... adjust the open accrual's
/// estimate", §10.2), lock the leg as PodReceived, and recompute the load's own
/// rollup status (§5.2).
/// </summary>
public class DebriefApprovalService
{
    private readonly TmsDbContext _db;
    private readonly LoadStatusService _loadStatus;

    public DebriefApprovalService(TmsDbContext db, LoadStatusService loadStatus)
    {
        _db = db;
        _loadStatus = loadStatus;
    }

    /// <summary>Returns null on success, or an error message the caller should return as a Conflict — e.g. an accrual this debrief claimed against got matched to a supplier invoice by an unrelated request while it sat PendingReview.</summary>
    public async Task<string?> ApproveAsync(
        Debrief debrief, LoadLeg leg, Load load, Guid? resolvedByUserId, CancellationToken ct)
    {
        var claims = debrief.Expenses
            .Where(e => e.ClaimedAgainst == ClaimedAgainst.SubcontractorAccrual && e.AccrualId is not null)
            .ToList();

        // Validate every claim before mutating any accrual, so a mid-way failure never
        // leaves some accruals adjusted and others not.
        var accruals = new List<SubcontractorAccrual>();
        foreach (var expense in claims)
        {
            var accrual = await _db.Set<SubcontractorAccrual>().FirstOrDefaultAsync(a => a.Id == expense.AccrualId, ct);
            if (accrual is null) return $"Accrual {expense.AccrualId} no longer exists.";
            if (accrual.Status != SubcontractorAccrualStatus.Accrued)
                return $"Accrual {expense.AccrualId} was already matched to a supplier invoice while this debrief was pending — resolve the variance manually instead.";

            accruals.Add(accrual);
        }

        for (var i = 0; i < claims.Count; i++)
            accruals[i].EstimatedAmount += claims[i].Amount;

        debrief.Status = DebriefStatus.Approved;
        debrief.ResolvedByUserId = resolvedByUserId;
        debrief.ResolvedAt = DateTimeOffset.UtcNow;

        leg.Status = LoadLegStatus.PodReceived;
        await _loadStatus.RecomputeAsync(load, ct);

        return null;
    }
}
