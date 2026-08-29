using Tms.Shared;

namespace Tms.Modules.Billing;

public enum CreditNoteStatus
{
    Draft,
    Issued,
    Void
}

/// <summary>
/// A sell-side adjustment against a Client (§10.1) — either correcting one or more
/// lines of an already-Issued Invoice (rate disputes, short-delivery, damage claims),
/// or a standalone goodwill/ad-hoc adjustment with no OriginalInvoiceId. Either way it
/// requires finance.creditnote.approve, since it reduces recognised revenue. Lifecycle
/// mirrors Invoice's own: Draft -> Issued -> Void, with Void/cancel only permitted
/// while still Draft. CurrencyId is fixed to the Invoice's own currency when
/// correcting one; for a standalone note it's one of the Client's allowed currencies
/// (§4.3), same validation as everywhere else a currency is chosen. Not in the doc's
/// own field list, but §10.3 says every CreditNote records the period it falls into —
/// FinancialPeriodId is added for exactly that, the same "the entity table is
/// representative, not exhaustive" gap already true of §11.2.
/// </summary>
public class CreditNote : CompanyScopedEntity
{
    public string CreditNoteNumber { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public Guid? OriginalInvoiceId { get; set; }
    public Guid CurrencyId { get; set; }
    public Guid FinancialPeriodId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public CreditNoteStatus Status { get; set; } = CreditNoteStatus.Draft;
    public decimal TotalAmount { get; set; }
    public string? PdfUrl { get; set; }

    public List<CreditNoteLine> Lines { get; set; } = new();
}

/// <summary>
/// One corrected amount on a CreditNote (§10.1) — InvoiceLineId ties it back to the
/// exact InvoiceLine it corrects when the credit note has an OriginalInvoiceId; null
/// for a standalone note's free-form lines. A line's Amount is capped at its
/// InvoiceLine's own Amount minus whatever's already been credited against it across
/// every non-Void CreditNote — enforced by CreditNotesController.Create, which holds a
/// per-invoice SQL application lock while checking it, the same reasoning as every
/// other check-and-save race closed elsewhere in this codebase.
/// </summary>
public class CreditNoteLine : CompanyScopedEntity
{
    public Guid CreditNoteId { get; set; }
    public Guid? InvoiceLineId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
