using Tms.Shared;

namespace Tms.Modules.Billing;

public enum FinancialYearStatus
{
    Future,
    Open,
    Closed
}

/// <summary>
/// A Company's own financial calendar (docs/architecture.html §10.3) — need not follow
/// the calendar year (e.g. March-February). Divided into FinancialPeriods; Invoice,
/// CreditNote, and SubcontractorExpense (later phases of this module) all post against
/// a period, resolved automatically from date, never hand-picked.
///
/// The doc's own entity table lists only Open|Closed for a year (matching how a year
/// closes as a whole once its last period does), but a year can legitimately exist
/// before it's ever open — created ahead of time so the previous year's last period has
/// somewhere to roll into (§10.3: "closing and opening happen as one operation, so
/// there is never a gap"). Future fills that gap; nothing in the doc's behavior changes.
/// </summary>
public class FinancialYear : CompanyScopedEntity
{
    public string YearLabel { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public FinancialYearStatus Status { get; set; } = FinancialYearStatus.Future;

    public List<FinancialPeriod> Periods { get; set; } = new();
}

public enum FinancialPeriodStatus
{
    Future,
    Open,
    Closed
}

/// <summary>
/// One period within a FinancialYear (§10.3) — typically a calendar month, sometimes a
/// 13th stub/adjustment period. Exactly one period per Company is ever Open; closing one
/// opens the next as a single operation (FinancialPeriodsController.Close), so there is
/// never a gap where nothing is open for a new document to post into.
/// </summary>
public class FinancialPeriod : CompanyScopedEntity
{
    public Guid FinancialYearId { get; set; }
    public int PeriodNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public FinancialPeriodStatus Status { get; set; } = FinancialPeriodStatus.Future;
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedByUserId { get; set; }
}

/// <summary>
/// A Client's aged-debtors position at exactly one period close (§10.3) — a permanent,
/// period-stamped record, so an aged-debtors report for a past period is a lookup, not
/// a recalculation against today's data. Bucket rollover happens once per period close:
/// Current -> 30 -> 60 -> 90 -> 90+ (stays), written by FinancialPeriodsController.Close.
///
/// CurrentAmount is 0 for now: it should be the sum of invoices raised in the period
/// just closed, but Invoice doesn't exist yet (§10.1, a later phase of this same
/// module). Every other bucket still rolls forward correctly from the prior snapshot
/// regardless — exactly like CreditExposureService's AR Outstanding, which becomes real
/// the same way once Invoice lands, with no change needed here.
/// </summary>
public class DebtorsAgingSnapshot : CompanyScopedEntity
{
    public Guid ClientId { get; set; }
    public Guid FinancialPeriodId { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal Days30 { get; set; }
    public decimal Days60 { get; set; }
    public decimal Days90 { get; set; }
    public decimal Days90Plus { get; set; }
    public decimal TotalOutstanding { get; set; }
    public DateTimeOffset SnapshotDate { get; set; } = DateTimeOffset.UtcNow;
}
