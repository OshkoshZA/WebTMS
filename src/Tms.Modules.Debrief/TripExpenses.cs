using Tms.Shared;

namespace Tms.Modules.Debrief;

public enum ClaimedAgainst
{
    Company,
    SubcontractorAccrual
}

/// <summary>Company-level expense category reference data (§9.1) — Toll, Overnight, TruckStop, Weighbridge, Parking, Subsistence, Other are typical, but each company maintains its own list, the same master-data convention as Commodity/CostCentre (§11.5).</summary>
public class ExpenseType : CompanyScopedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
}

/// <summary>
/// One incidental trip cost captured on a Debrief (§9.1) — a debrief carries as many of
/// these as actually happened. For an own-fleet leg (ClaimedAgainst = Company) this is
/// just recorded data — no reimbursement/petty-cash ledger exists in this codebase yet
/// to post it to. For a subcontracted leg (ClaimedAgainst = SubcontractorAccrual),
/// AccrualId names exactly which of the leg's accruals this adjusts — a leg can carry
/// more than one (e.g. transport plus an outsourced escort, §5.6), so "the leg's
/// accrual" is never assumed. The adjustment itself only happens once the owning
/// Debrief is Approved (auto or by a clerk) — matching the doc's own wording,
/// "Debrief-approved extras... adjust the open accrual's estimate" (§10.2).
/// </summary>
public class DebriefExpense : CompanyScopedEntity
{
    public Guid DebriefId { get; set; }
    public Guid ExpenseTypeId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid CurrencyId { get; set; }
    public string? ReceiptImageUrl { get; set; }
    public ClaimedAgainst ClaimedAgainst { get; set; }
    public Guid? AccrualId { get; set; }
}
