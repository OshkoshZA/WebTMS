namespace Tms.Modules.Integration;

/// <summary>
/// The fixed event-type vocabulary docs/architecture.html §11.3 defines. A partner
/// subscribes to one of these exact strings — WebhookSubscriptionsController.Create
/// rejects anything else, the same "typo in the request, not a new event type" guard as
/// any other fixed-vocabulary field in this codebase.
///
/// Only InvoiceIssued, CreditNoteIssued, and SubcontractorExpenseAvailableForExport are
/// actually published anywhere yet — exactly the three §11.4 says the (not yet built)
/// Xero adapter would react to. The rest are registerable today and will start firing
/// the moment each triggering action is wired up, with no further change needed here.
/// </summary>
public static class WebhookEventTypes
{
    public const string LoadStatusChanged = "load.status_changed";
    public const string LoadConfirmationIssued = "loadconfirmation.issued";
    public const string DebriefApproved = "debrief.approved";
    public const string InvoiceIssued = "invoice.issued";
    public const string CreditNoteIssued = "creditnote.issued";
    public const string SubcontractorAccrualRaised = "subcontractor_accrual.raised";
    public const string SubcontractorInvoiceReceived = "subcontractor_invoice.received";
    public const string SubcontractorExpenseAvailableForExport = "subcontractor_expense.available_for_export";
    public const string FinancialPeriodClosed = "financialperiod.closed";
    public const string ExceptionRaised = "exception.raised";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        LoadStatusChanged, LoadConfirmationIssued, DebriefApproved, InvoiceIssued, CreditNoteIssued,
        SubcontractorAccrualRaised, SubcontractorInvoiceReceived, SubcontractorExpenseAvailableForExport,
        FinancialPeriodClosed, ExceptionRaised
    };
}
