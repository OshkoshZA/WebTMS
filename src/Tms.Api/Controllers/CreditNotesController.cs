using System.Data;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Integration;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateCreditNoteLineRequest(Guid? InvoiceLineId, string Description, decimal Amount);

public record CreateCreditNoteRequest(
    Guid ClientId, Guid? OriginalInvoiceId, string Reason, Guid? CurrencyId,
    IReadOnlyList<CreateCreditNoteLineRequest> Lines, DateOnly? IssueDate = null);

public record IssueCreditNoteRequest(DateOnly? IssueDate = null);

public record CreditNoteLineResponse(Guid Id, Guid? InvoiceLineId, string Description, decimal Amount);

public record CreditNoteResponse(
    Guid Id, string CreditNoteNumber, Guid ClientId, Guid? OriginalInvoiceId, Guid CurrencyId, Guid FinancialPeriodId,
    string Reason, DateOnly IssueDate, CreditNoteStatus Status, decimal TotalAmount, string? PdfUrl,
    IReadOnlyList<CreditNoteLineResponse> Lines);

/// <summary>
/// Sell-side adjustments (docs/architecture.html §10.1) — either correcting one or
/// more lines of an already-Issued invoice, or a standalone goodwill/ad-hoc note.
/// Every write here requires finance.creditnote.approve, since it reduces recognised
/// revenue; List/Get stay open, the same read/write split as Invoice.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/credit-notes")]
[Authorize]
public class CreditNotesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly CreditExposureService _creditExposure;
    private readonly IAuthorizationService _authorizationService;
    private readonly WebhookPublisher _webhookPublisher;
    private readonly WebhookDeliveryService _webhookDelivery;

    public CreditNotesController(
        TmsDbContext db, ITenantContext tenantContext, CreditExposureService creditExposure, IAuthorizationService authorizationService,
        WebhookPublisher webhookPublisher, WebhookDeliveryService webhookDelivery)
    {
        _db = db;
        _tenantContext = tenantContext;
        _creditExposure = creditExposure;
        _authorizationService = authorizationService;
        _webhookPublisher = webhookPublisher;
        _webhookDelivery = webhookDelivery;
    }

    /// <summary>Also the Customer Portal's own credit note list (§13.1, §13.2) — a portal caller is pinned to their own Client's credit notes regardless of what clientId they pass.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CreditNoteResponse>>> List(Guid? clientId, CancellationToken ct)
    {
        // A Supplier Portal contact (ClientId null, same as staff) is explicitly
        // Forbidden — the bug an earlier version of this check had would have returned
        // every client's credit notes to a subcontractor contact.
        if (_tenantContext.SubcontractorId is not null) return Forbid();

        var isPortalCaller = _tenantContext.ClientId is not null;
        if (_tenantContext.ClientId is Guid ownClientId)
        {
            var authResult = await _authorizationService.AuthorizeAsync(User, "portal.client.viewinvoices");
            if (!authResult.Succeeded) return Forbid();
            clientId = ownClientId;
        }

        var query = _db.Set<CreditNote>().Include(cn => cn.Lines).AsQueryable();
        if (clientId is Guid c) query = query.Where(cn => cn.ClientId == c);
        // Same reasoning as InvoicesController.List — a Draft credit note is still an
        // internal working document, so a portal caller never sees it; staff do.
        if (isPortalCaller) query = query.Where(cn => cn.Status != CreditNoteStatus.Draft);

        var creditNotes = await query.OrderByDescending(cn => cn.IssueDate).ToListAsync(ct);
        return Ok(creditNotes.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CreditNoteResponse>> Get(Guid id, CancellationToken ct)
    {
        var creditNote = await _db.Set<CreditNote>().Include(cn => cn.Lines).FirstOrDefaultAsync(cn => cn.Id == id, ct);
        if (creditNote is not null && !_tenantContext.CanAccessClient(creditNote.ClientId)) return Forbid();
        if (creditNote is not null && _tenantContext.ClientId is not null && creditNote.Status == CreditNoteStatus.Draft) return Forbid();

        return creditNote is null ? NotFound() : Ok(ToResponse(creditNote));
    }

    /// <summary>
    /// Creates a Draft credit note — either every line names an InvoiceLineId on
    /// OriginalInvoiceId (correcting it), or none do (a standalone note); the two never
    /// mix. Correcting an invoice fixes CurrencyId to the invoice's own; a standalone
    /// note picks one of the Client's allowed currencies (§4.3), defaulting to its
    /// primary. A line's Amount is capped at its InvoiceLine's own Amount minus
    /// whatever's already been credited against it by any non-Void credit note —
    /// checked under a per-invoice SQL application lock so two concurrent credit notes
    /// against the same invoice can't both pass that check before either commits.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "finance.creditnote.approve")]
    public async Task<ActionResult<CreditNoteResponse>> Create(CreateCreditNoteRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (request.Lines is null || request.Lines.Count == 0)
            return BadRequest("At least one line is required.");
        if (request.Lines.Any(l => l.Amount <= 0))
            return BadRequest("Each line's Amount must be positive.");

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId, ct);
        if (client is null) return NotFound($"Client {request.ClientId} was not found.");

        Invoice? invoice = null;
        Guid currencyId;

        if (request.OriginalInvoiceId is Guid originalInvoiceId)
        {
            invoice = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == originalInvoiceId, ct);
            if (invoice is null) return NotFound($"Invoice {originalInvoiceId} was not found.");
            if (invoice.ClientId != request.ClientId)
                return BadRequest("That invoice does not belong to this client.");
            if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Void)
                return Conflict($"Invoice is {invoice.Status}; only an Issued, Part Paid, or Paid invoice can be credited.");
            if (request.CurrencyId is Guid explicitCurrencyId && explicitCurrencyId != invoice.CurrencyId)
                return BadRequest("CurrencyId must match the invoice's own currency when correcting an invoice.");
            if (request.Lines.Any(l => l.InvoiceLineId is null))
                return BadRequest("Every line must reference an InvoiceLineId when correcting an invoice.");

            currencyId = invoice.CurrencyId;
        }
        else
        {
            if (request.Lines.Any(l => l.InvoiceLineId is not null))
                return BadRequest("A standalone credit note's lines cannot reference an InvoiceLineId.");

            currencyId = request.CurrencyId ?? client.CurrencyId;
            if (await _creditExposure.ResolveCreditLimitAsync(client, currencyId, ct) is null)
                return BadRequest($"Client is not permitted to transact in currency {currencyId}.");
        }

        var openPeriod = await _db.FinancialPeriods.FirstOrDefaultAsync(
            p => p.CompanyId == _tenantContext.CompanyId && p.Status == FinancialPeriodStatus.Open, ct);
        if (openPeriod is null)
            return Conflict("No open financial period for this company — create one first (POST /api/v1/financial-years).");

        // Only an invoice-correcting note needs the lock: a standalone note has no
        // InvoiceLine cap to race over. Held from here through SaveChanges so a second
        // concurrent Create against the same invoice blocks until this one commits or
        // rolls back — disposing it uncommitted (any early return below) rolls back.
        await using var invoiceLock = invoice is not null
            ? await BeginInvoiceLockAsync(invoice.Id, ct)
            : null;

        if (invoice is not null)
        {
            // Tracks each InvoiceLineId's running claim across this request's own
            // Lines — the per-invoice lock above only guards against a *different*
            // transaction crediting the same line concurrently; without this, two
            // lines in the same request both referencing the same InvoiceLineId each
            // independently query "already credited" against the DB (still zero for
            // both, since neither has been saved yet) and both pass, over-crediting
            // the line by the sum of both once actually saved.
            var pendingByInvoiceLineId = new Dictionary<Guid, decimal>();

            foreach (var lineRequest in request.Lines)
            {
                var invoiceLine = invoice.Lines.FirstOrDefault(l => l.Id == lineRequest.InvoiceLineId);
                if (invoiceLine is null)
                    return NotFound($"Invoice line {lineRequest.InvoiceLineId} was not found on invoice {invoice.Id}.");

                var alreadyCredited = await _db.Set<CreditNoteLine>()
                    .Where(cnl => cnl.InvoiceLineId == invoiceLine.Id)
                    .Join(_db.Set<CreditNote>(), cnl => cnl.CreditNoteId, cn => cn.Id, (cnl, cn) => new { cnl.Amount, cn.Status })
                    .Where(x => x.Status != CreditNoteStatus.Void)
                    .SumAsync(x => x.Amount, ct);

                var pendingFromThisRequest = pendingByInvoiceLineId.GetValueOrDefault(invoiceLine.Id);

                if (alreadyCredited + pendingFromThisRequest + lineRequest.Amount > invoiceLine.Amount)
                {
                    return Conflict(
                        $"Crediting {lineRequest.Amount:N2} against invoice line {invoiceLine.Id} would exceed its original " +
                        $"amount of {invoiceLine.Amount:N2} (already credited: {alreadyCredited + pendingFromThisRequest:N2}).");
                }

                pendingByInvoiceLineId[invoiceLine.Id] = pendingFromThisRequest + lineRequest.Amount;
            }
        }

        var issueDate = request.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var creditNote = new CreditNote
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            CreditNoteNumber = await NextCreditNoteNumberAsync(_tenantContext.CompanyId.Value, ct),
            ClientId = client.Id,
            OriginalInvoiceId = request.OriginalInvoiceId,
            CurrencyId = currencyId,
            FinancialPeriodId = openPeriod.Id,
            Reason = request.Reason,
            IssueDate = issueDate
        };

        foreach (var lineRequest in request.Lines)
        {
            creditNote.Lines.Add(new CreditNoteLine
            {
                TenantId = creditNote.TenantId,
                CompanyId = creditNote.CompanyId,
                InvoiceLineId = lineRequest.InvoiceLineId,
                Description = lineRequest.Description,
                Amount = lineRequest.Amount
            });
        }

        creditNote.TotalAmount = creditNote.Lines.Sum(l => l.Amount);

        _db.Set<CreditNote>().Add(creditNote);
        await _db.SaveChangesAsync(ct);
        if (invoiceLock is not null) await invoiceLock.CommitAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = creditNote.Id }, ToResponse(creditNote));
    }

    /// <summary>Draft -> Issued. DueDate-style recompute doesn't apply here (a credit note has no due date), but IssueDate is still refreshed from what's given (or today), the same reasoning as Invoice's Issue.</summary>
    [HttpPost("{id:guid}/issue")]
    [Authorize(Policy = "finance.creditnote.approve")]
    public async Task<IActionResult> Issue(Guid id, IssueCreditNoteRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var creditNote = await _db.Set<CreditNote>().FirstOrDefaultAsync(cn => cn.Id == id, ct);
        if (creditNote is null) return NotFound();
        if (creditNote.Status != CreditNoteStatus.Draft)
            return Conflict($"Credit note is {creditNote.Status}; only a Draft credit note can be issued.");

        creditNote.IssueDate = request.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        creditNote.Status = CreditNoteStatus.Issued;

        var deliveryIds = await _webhookPublisher.QueueAsync(
            _tenantContext.TenantId.Value, _tenantContext.CompanyId.Value,
            WebhookEventTypes.CreditNoteIssued, nameof(CreditNote), creditNote.Id, ct);

        await _db.SaveChangesAsync(ct);
        await _webhookDelivery.DeliverAsync(deliveryIds, ct);
        return NoContent();
    }

    /// <summary>Draft -> Void — the only cancellation path (§10.1), and only while still Draft.</summary>
    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = "finance.creditnote.approve")]
    public async Task<IActionResult> Void(Guid id, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var creditNote = await _db.Set<CreditNote>().FirstOrDefaultAsync(cn => cn.Id == id, ct);
        if (creditNote is null) return NotFound();
        if (creditNote.Status != CreditNoteStatus.Draft)
            return Conflict($"Credit note is {creditNote.Status}; only a Draft credit note can be voided.");

        creditNote.Status = CreditNoteStatus.Void;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Sequential per Company from a row count — same accepted concurrency caveat as InvoicesController.NextInvoiceNumberAsync.</summary>
    private async Task<string> NextCreditNoteNumberAsync(Guid companyId, CancellationToken ct)
    {
        var count = await _db.Set<CreditNote>().CountAsync(cn => cn.CompanyId == companyId, ct);
        return $"CN{count + 1:D6}";
    }

    /// <summary>Holds an exclusive, transaction-scoped SQL Server application lock on one invoice — same sp_getapplock mechanism as LoadsController's per-leg lock, just keyed on an invoice instead of a leg.</summary>
    private async Task<IDbContextTransaction> BeginInvoiceLockAsync(Guid invoiceId, CancellationToken ct)
    {
        var transaction = await _db.Database.BeginTransactionAsync(ct);

        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.Transaction = transaction.GetDbTransaction();

        void AddParam(string name, object value, ParameterDirection direction = ParameterDirection.Input)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            parameter.Direction = direction;
            command.Parameters.Add(parameter);
        }

        AddParam("@Resource", $"invoice:{invoiceId}");
        AddParam("@LockMode", "Exclusive");
        AddParam("@LockOwner", "Transaction");
        AddParam("@LockTimeout", 10000);
        AddParam("@ReturnValue", 0, ParameterDirection.ReturnValue);

        await command.ExecuteNonQueryAsync(ct);

        var lockResult = (int)command.Parameters["@ReturnValue"].Value!;
        if (lockResult < 0)
        {
            await transaction.RollbackAsync(ct);
            throw new InvalidOperationException(
                $"Could not acquire the invoice lock for {invoiceId} (sp_getapplock returned {lockResult}).");
        }

        return transaction;
    }

    internal static CreditNoteResponse ToResponse(CreditNote creditNote) => new(
        creditNote.Id, creditNote.CreditNoteNumber, creditNote.ClientId, creditNote.OriginalInvoiceId,
        creditNote.CurrencyId, creditNote.FinancialPeriodId, creditNote.Reason, creditNote.IssueDate,
        creditNote.Status, creditNote.TotalAmount, creditNote.PdfUrl,
        creditNote.Lines.Select(l => new CreditNoteLineResponse(l.Id, l.InvoiceLineId, l.Description, l.Amount)).ToList());
}
