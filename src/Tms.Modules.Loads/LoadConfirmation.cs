using Tms.Shared;

namespace Tms.Modules.Loads;

public enum LoadConfirmationStatus
{
    Issued,
    Acknowledged,
    Declined
}

/// <summary>
/// The carrier's written instruction to run a leg, and formal proof of the agreed rate
/// (docs/architecture.html §8.2) — generated automatically by LoadsController the
/// moment a Subcontracted leg reaches Allocated, never created directly. PdfUrl is null
/// for now: there's no PDF-rendering infrastructure anywhere in this codebase yet, the
/// same "structurally correct now, real once it exists" gap as Invoice's PdfUrl.
/// DeclineReason isn't in the doc's own field list, but capturing why a subcontractor
/// declined is obviously worth keeping once they can (matches FinancialPeriod's
/// ClosedAt/ClosedByUserId, added for the same reason).
/// </summary>
public class LoadConfirmation : CompanyScopedEntity
{
    public Guid LoadLegId { get; set; }
    public Guid SubcontractorId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public DateTimeOffset IssuedDate { get; set; } = DateTimeOffset.UtcNow;
    public LoadConfirmationStatus Status { get; set; } = LoadConfirmationStatus.Issued;
    public string? PdfUrl { get; set; }
    public string? DeclineReason { get; set; }
}
