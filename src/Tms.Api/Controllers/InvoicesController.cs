using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Integration;
using Tms.Modules.Loads;
using Tms.Modules.Rating;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record GenerateInvoiceRequest(Guid ClientId, Guid? CurrencyId = null, DateOnly? IssueDate = null);
public record IssueInvoiceRequest(DateOnly? IssueDate = null);

public record InvoiceLineResponse(Guid Id, Guid RateLineSellId, string Description, decimal Quantity, Guid UnitOfMeasureId, decimal Rate, decimal Amount);

public record InvoiceResponse(
    Guid Id, string InvoiceNumber, Guid ClientId, Guid CurrencyId, Guid FinancialPeriodId, DateOnly IssueDate, DateOnly DueDate,
    InvoiceStatus Status, decimal TotalExVat, decimal VatAmount, decimal TotalIncVat, bool IsOverdue,
    IReadOnlyList<InvoiceLineResponse> Lines);

/// <summary>
/// Sell-side invoicing (docs/architecture.html §10.1) — one line per approved commodity
/// line, in the Client's fixed currency, posted against the Company's current open
/// FinancialPeriod (§10.3). Generate aggregates a client's unbilled sell RateLines from
/// PodReceived loads (§5.2, §09) — a load only reaches that status once every leg's
/// Debrief is Approved, the real "ready to bill" gate the doc describes, no longer the
/// Delivered stand-in used before Debrief existed. "Overdue" isn't a stored status;
/// it's derived from DueDate, same as the doc describes.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/invoices")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly WebhookPublisher _webhookPublisher;
    private readonly WebhookDeliveryService _webhookDelivery;

    public InvoicesController(
        TmsDbContext db, ITenantContext tenantContext, IAuthorizationService authorizationService,
        WebhookPublisher webhookPublisher, WebhookDeliveryService webhookDelivery)
    {
        _db = db;
        _tenantContext = tenantContext;
        _authorizationService = authorizationService;
        _webhookPublisher = webhookPublisher;
        _webhookDelivery = webhookDelivery;
    }

    /// <summary>Also the Customer Portal's own invoice list (§13.1, §13.2) — a portal caller is pinned to their own Client's invoices regardless of what clientId they pass.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<InvoiceResponse>>> List(Guid? clientId, CancellationToken ct)
    {
        // A Supplier Portal contact (ClientId null, same as staff) is explicitly
        // Forbidden — the bug an earlier version of this check had would have returned
        // every client's invoices to a subcontractor contact.
        if (_tenantContext.SubcontractorId is not null) return Forbid();

        var isPortalCaller = _tenantContext.ClientId is not null;
        if (_tenantContext.ClientId is Guid ownClientId)
        {
            var authResult = await _authorizationService.AuthorizeAsync(User, "portal.client.viewinvoices");
            if (!authResult.Succeeded) return Forbid();
            clientId = ownClientId;
        }

        var query = _db.Invoices.Include(i => i.Lines).AsQueryable();
        if (clientId is Guid c) query = query.Where(i => i.ClientId == c);
        // A Draft invoice is still an internal working document — nothing has been
        // issued to the client yet, so a portal caller never sees it; staff still see
        // every status, including Draft, since they're the ones who'd act on it.
        if (isPortalCaller) query = query.Where(i => i.Status != InvoiceStatus.Draft);

        var invoices = await query.OrderByDescending(i => i.IssueDate).ToListAsync(ct);
        return Ok(invoices.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceResponse>> Get(Guid id, CancellationToken ct)
    {
        var invoice = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is not null && !_tenantContext.CanAccessClient(invoice.ClientId)) return Forbid();
        // Same Draft restriction as List — a portal caller who already had this id
        // (e.g. from an earlier issued state) still can't reach it while it's Draft.
        if (invoice is not null && _tenantContext.ClientId is not null && invoice.Status == InvoiceStatus.Draft) return Forbid();

        return invoice is null ? NotFound() : Ok(ToResponse(invoice));
    }

    /// <summary>Aggregates a client's unbilled sell lines into a new Draft invoice — one InvoiceLine per CommodityLine, never blended (§10.1: "a multi-product delivery produces one invoice with a line per product").</summary>
    [HttpPost("generate")]
    [Authorize(Policy = "finance.invoice.manage")]
    public async Task<ActionResult<InvoiceResponse>> Generate(GenerateInvoiceRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId, ct);
        if (client is null) return NotFound($"Client {request.ClientId} was not found.");

        // A client permitted to transact in more than one currency (§4.3) can have
        // unbilled sell lines in each at once — never blended onto one invoice, since
        // a single document can't carry two currencies. CurrencyId picks which one
        // this call generates; omitted, it defaults to the client's primary.
        var currencyId = request.CurrencyId ?? client.CurrencyId;

        var openPeriod = await _db.FinancialPeriods.FirstOrDefaultAsync(
            p => p.CompanyId == _tenantContext.CompanyId && p.Status == FinancialPeriodStatus.Open, ct);
        if (openPeriod is null)
            return Conflict("No open financial period for this company — create one first (POST /api/v1/financial-years).");

        var alreadyInvoicedRateLineIds = _db.InvoiceLines.Select(l => l.RateLineSellId);

        var candidates = await _db.Set<RateLine>()
            .Where(r => r.Direction == RateLineDirection.Sell && r.SourceType == RateLineSourceType.CommodityLine && r.CurrencyId == currencyId)
            .Where(r => !alreadyInvoicedRateLineIds.Contains(r.Id))
            .Join(_db.Set<CommodityLine>(), r => r.SourceId, cl => cl.Id, (r, cl) => new { r, cl })
            .Join(_db.Set<LoadLeg>(), x => x.cl.LoadLegId, leg => leg.Id, (x, leg) => new { x.r, x.cl, leg })
            .Join(_db.Loads, x => x.leg.LoadId, load => load.Id, (x, load) => new { x.r, x.cl, load })
            .Where(x => x.load.ClientId == request.ClientId && x.load.Status == LoadStatus.PodReceived)
            .Select(x => new { x.r, x.cl })
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return Conflict("No unbilled, PodReceived sell lines found for this client in that currency.");

        var commodityIds = candidates.Select(x => x.cl.CommodityId).Distinct().ToList();
        var commodityNames = await _db.Commodities
            .Where(c => commodityIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var issueDate = request.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var invoice = new Invoice
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            InvoiceNumber = await NextInvoiceNumberAsync(_tenantContext.CompanyId.Value, ct),
            ClientId = client.Id,
            CurrencyId = currencyId,
            FinancialPeriodId = openPeriod.Id,
            IssueDate = issueDate,
            DueDate = issueDate.AddDays(client.PaymentTermsDays)
        };

        foreach (var x in candidates)
        {
            invoice.Lines.Add(new InvoiceLine
            {
                TenantId = invoice.TenantId,
                CompanyId = invoice.CompanyId,
                RateLineSellId = x.r.Id,
                Description = commodityNames.GetValueOrDefault(x.cl.CommodityId, "Commodity"),
                Quantity = x.cl.Quantity,
                UnitOfMeasureId = x.cl.UnitOfMeasureId,
                Rate = x.r.RatePerUnit,
                Amount = x.r.Amount
            });
        }

        invoice.TotalExVat = invoice.Lines.Sum(l => l.Amount);
        invoice.VatAmount = 0m; // TODO: no VAT-rate configuration exists in this codebase yet
        invoice.TotalIncVat = invoice.TotalExVat + invoice.VatAmount;

        _db.Invoices.Add(invoice);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // InvoiceLineRateLineSellIndex catches a race between two concurrent
            // Generate calls that both read the same sell RateLine as unbilled before
            // either committed — turns a real double-bill into a clean 409 instead of
            // a raw 500, same pattern as the composite name/number indexes elsewhere.
            return Conflict("One or more of these sell lines were already invoiced by a concurrent request.");
        }

        return CreatedAtAction(nameof(Get), new { id = invoice.Id }, ToResponse(invoice));
    }

    /// <summary>Draft -> Issued. DueDate is recomputed from the issue date given here (or today), so a Draft that sat around doesn't silently issue with a stale due date.</summary>
    [HttpPost("{id:guid}/issue")]
    [Authorize(Policy = "finance.invoice.manage")]
    public async Task<IActionResult> Issue(Guid id, IssueInvoiceRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status != InvoiceStatus.Draft)
            return Conflict($"Invoice is {invoice.Status}; only a Draft invoice can be issued.");

        var client = await _db.Clients.FirstAsync(c => c.Id == invoice.ClientId, ct);
        invoice.IssueDate = request.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        invoice.DueDate = invoice.IssueDate.AddDays(client.PaymentTermsDays);
        invoice.Status = InvoiceStatus.Issued;

        var deliveryIds = await _webhookPublisher.QueueAsync(
            _tenantContext.TenantId.Value, _tenantContext.CompanyId.Value,
            WebhookEventTypes.InvoiceIssued, nameof(Invoice), invoice.Id, ct);

        await _db.SaveChangesAsync(ct);
        await _webhookDelivery.DeliverAsync(deliveryIds, ct);
        return NoContent();
    }

    /// <summary>Draft -> Void — the only cancellation path (§10.1), and only while still Draft.</summary>
    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = "finance.invoice.manage")]
    public async Task<IActionResult> Void(Guid id, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status != InvoiceStatus.Draft)
            return Conflict($"Invoice is {invoice.Status}; only a Draft invoice can be voided.");

        invoice.Status = InvoiceStatus.Void;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Sequential per Company from a row count — same accepted concurrency caveat as CreditExposureService: two simultaneous Generate calls could race to the same number.</summary>
    private async Task<string> NextInvoiceNumberAsync(Guid companyId, CancellationToken ct)
    {
        var company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct);
        var count = await _db.Invoices.CountAsync(i => i.CompanyId == companyId, ct);
        return $"{company.InvoiceNumberPrefix}{count + 1:D6}";
    }

    internal static InvoiceResponse ToResponse(Invoice invoice) => new(
        invoice.Id, invoice.InvoiceNumber, invoice.ClientId, invoice.CurrencyId, invoice.FinancialPeriodId, invoice.IssueDate, invoice.DueDate,
        invoice.Status, invoice.TotalExVat, invoice.VatAmount, invoice.TotalIncVat,
        IsOverdue: invoice.Status is InvoiceStatus.Issued or InvoiceStatus.PartPaid && invoice.DueDate < DateOnly.FromDateTime(DateTime.UtcNow),
        invoice.Lines.Select(l => new InvoiceLineResponse(l.Id, l.RateLineSellId, l.Description, l.Quantity, l.UnitOfMeasureId, l.Rate, l.Amount)).ToList());
}
