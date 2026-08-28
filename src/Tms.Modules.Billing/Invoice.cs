using Tms.Shared;

namespace Tms.Modules.Billing;

public enum InvoiceStatus
{
    Draft,
    Issued,
    PartPaid,
    Paid,
    Void
}

/// <summary>
/// A sell-side invoice raised per Client, in that client's fixed currency (§10.1) —
/// aggregating one line per approved commodity line, either scheduled or on demand.
/// Lifecycle: Draft -> Issued -> PartPaid/Paid; Void/cancel only while still Draft.
/// "Overdue" isn't a stored state — it's derived from DueDate (see InvoiceResponse).
///
/// Debrief approval (§09) doesn't exist yet (a later phase), so InvoicesController
/// generates from a client's Delivered loads instead of "Debrief Approved" — the
/// closest already-implemented proxy for "ready to bill." VAT is 0 for now: there's no
/// VAT-rate configuration in this codebase yet, so TotalIncVat == TotalExVat until that
/// lands, the same "structurally correct now, real once the rest exists" pattern as
/// CreditExposureService's AR Outstanding.
/// </summary>
public class Invoice : CompanyScopedEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public Guid FinancialPeriodId { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public decimal TotalExVat { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalIncVat { get; set; }
    public string? PdfUrl { get; set; }

    public List<InvoiceLine> Lines { get; set; } = new();
}

/// <summary>One commodity line's worth of sell value on an Invoice (§10.1) — RateLineSellId ties it back to the exact sell RateLine it was generated from, so it can never be billed twice.</summary>
public class InvoiceLine : CompanyScopedEntity
{
    public Guid InvoiceId { get; set; }
    public Guid RateLineSellId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public Guid UnitOfMeasureId { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}
