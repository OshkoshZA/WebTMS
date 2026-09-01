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
    private readonly ExceptionService _exceptions;

    public DebriefApprovalService(TmsDbContext db, LoadStatusService loadStatus, ExceptionService exceptions)
    {
        _db = db;
        _loadStatus = loadStatus;
        _exceptions = exceptions;
    }

    /// <summary>Returns null on success, or an error message the caller should return as a Conflict — e.g. an accrual this debrief claimed against got matched to a supplier invoice by an unrelated request while it sat PendingReview.</summary>
    public async Task<string?> ApproveAsync(
        Debrief debrief, LoadLeg leg, Load load, Guid? resolvedByUserId, CancellationToken ct)
    {
        var claims = debrief.Expenses
            .Where(e => e.ClaimedAgainst == ClaimedAgainst.SubcontractorAccrual && e.AccrualId is not null)
            .ToList();

        if (claims.Count > 0)
        {
            // Each claim is applied via a single atomic, server-side increment with the
            // Accrued check baked into its own WHERE clause — not a read-then-write, so
            // there's no window for a concurrent SupplierInvoicesController.Match on the
            // same accrual to net it out from under this update: either this UPDATE runs
            // first and Match's own Accrued check then correctly fails it, or Match runs
            // first and this UPDATE affects 0 rows, caught below. A prior version read
            // Status then wrote EstimatedAmount as two separate steps — closing exactly
            // the class of race this codebase's own audit sweep went looking for
            // elsewhere (SupplierInvoicesController.Match's own accrual-vs-accrual race).
            // Wrapped in one transaction so a mid-way failure on a multi-accrual debrief
            // never leaves some accruals adjusted and others not.
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            foreach (var expense in claims)
            {
                var claimed = await _db.Set<SubcontractorAccrual>()
                    .Where(a => a.Id == expense.AccrualId && a.Status == SubcontractorAccrualStatus.Accrued)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.EstimatedAmount, a => a.EstimatedAmount + expense.Amount), ct);

                if (claimed == 0)
                {
                    await transaction.RollbackAsync(ct);
                    return $"Accrual {expense.AccrualId} was already matched to a supplier invoice while this debrief was pending — resolve the variance manually instead.";
                }
            }

            await transaction.CommitAsync(ct);
        }

        debrief.Status = DebriefStatus.Approved;
        debrief.ResolvedByUserId = resolvedByUserId;
        debrief.ResolvedAt = DateTimeOffset.UtcNow;

        leg.Status = LoadLegStatus.PodReceived;
        await _loadStatus.RecomputeAsync(load, ct);

        // Closes out §16.1's shared Exception, if this debrief ever raised one (it
        // didn't, on the auto-approve path — nothing to resolve there, and this is a
        // no-op).
        await _exceptions.ResolveByEntityAsync(nameof(Debrief), debrief.Id, ct);

        return null;
    }
}
