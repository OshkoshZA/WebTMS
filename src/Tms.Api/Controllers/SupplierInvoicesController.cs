using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Loads;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateSupplierInvoiceRequest(Guid SubcontractorId, string SupplierInvoiceNumber, DateOnly InvoiceDate, DateOnly ReceivedDate, decimal Amount, Guid? CurrencyId = null);
public record MatchSupplierInvoiceRequest(IReadOnlyList<Guid> AccrualIds);
public record DisputeSupplierInvoiceRequest(string Reason);

public record SubcontractorExpenseResponse(
    Guid Id, Guid RateLineBuyId, Guid AccrualId, Guid FinancialPeriodId,
    decimal Amount, SubcontractorExpenseStatus Status, DateTimeOffset FinalizedDate);

public record SupplierInvoiceResponse(
    Guid Id, Guid SubcontractorId, Guid CurrencyId, string SupplierInvoiceNumber, DateOnly InvoiceDate, DateOnly ReceivedDate,
    decimal Amount, SupplierInvoiceStatus Status, string? DisputeReason,
    IReadOnlyList<SubcontractorExpenseResponse> Expenses);

public record MatchSupplierInvoiceResponse(SupplierInvoiceResponse Invoice, decimal VarianceAmount);

/// <summary>
/// Buy-side payables (docs/architecture.html §10.2) — the subcontractor's own invoice,
/// captured when it arrives and matched against the SubcontractorAccrual(s) that
/// LoadsController already raised at leg allocation. Matching is the only thing that
/// ever creates a SubcontractorExpense; nothing here creates one directly, and nothing
/// ever edits one afterward — a correction is always a new SupplierInvoice/match, the
/// same one-directional posting rule as everything else in §10.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/supplier-invoices")]
[Authorize]
public class SupplierInvoicesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public SupplierInvoicesController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SupplierInvoiceResponse>>> List(CancellationToken ct)
    {
        var invoices = await _db.SupplierInvoices.Include(si => si.Expenses).OrderByDescending(si => si.ReceivedDate).ToListAsync(ct);
        return Ok(invoices.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupplierInvoiceResponse>> Get(Guid id, CancellationToken ct)
    {
        var invoice = await _db.SupplierInvoices.Include(si => si.Expenses).FirstOrDefaultAsync(si => si.Id == id, ct);
        return invoice is null ? NotFound() : Ok(ToResponse(invoice));
    }

    /// <summary>
    /// Captures a subcontractor's own invoice as Received. SupplierInvoiceNumber is the
    /// carrier's own numbering, never generated here — so a duplicate capture of the
    /// same (Subcontractor, SupplierInvoiceNumber) pair for this Company is rejected
    /// outright rather than silently accepted twice.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "finance.subcontractorinvoice.process")]
    public async Task<ActionResult<SupplierInvoiceResponse>> Create(CreateSupplierInvoiceRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var subcontractor = await _db.Subcontractors.FirstOrDefaultAsync(s => s.Id == request.SubcontractorId, ct);
        if (subcontractor is null) return NotFound($"Subcontractor {request.SubcontractorId} was not found.");

        var currencyId = request.CurrencyId ?? subcontractor.CurrencyId;
        if (!await IsSubcontractorCurrencyAllowedAsync(subcontractor, currencyId, ct))
            return BadRequest($"Subcontractor is not permitted to transact in currency {currencyId} — add it via POST /subcontractors/{{id}}/currencies first.");

        if (await _db.SupplierInvoices.AnyAsync(
            si => si.SubcontractorId == request.SubcontractorId && si.SupplierInvoiceNumber == request.SupplierInvoiceNumber, ct))
            return Conflict($"Supplier invoice {request.SupplierInvoiceNumber} has already been captured for this subcontractor.");

        var invoice = new SupplierInvoice
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            SubcontractorId = request.SubcontractorId,
            CurrencyId = currencyId,
            SupplierInvoiceNumber = request.SupplierInvoiceNumber,
            InvoiceDate = request.InvoiceDate,
            ReceivedDate = request.ReceivedDate,
            Amount = request.Amount
        };
        _db.SupplierInvoices.Add(invoice);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // SupplierInvoiceNumberIndex (Company, Subcontractor, SupplierInvoiceNumber)
            // catches a race between two Create calls for the same invoice that both
            // passed the AnyAsync check above — turns that into a clean 409 instead of
            // a raw 500, same pattern as RolesController/UsersController.
            return Conflict($"Supplier invoice {request.SupplierInvoiceNumber} has already been captured for this subcontractor.");
        }

        return CreatedAtAction(nameof(Get), new { id = invoice.Id }, ToResponse(invoice));
    }

    /// <summary>
    /// Matches a Received SupplierInvoice against one or more of its subcontractor's
    /// open Accrued accruals (§10.2) — one-to-one for a single-leg invoice, one-to-many
    /// where a carrier bills several legs on one invoice. Atomically nets off every
    /// matched accrual and finalizes a SubcontractorExpense for each, apportioned from
    /// the invoice's actual Amount (not the accruals' estimates) so the total expensed
    /// always equals exactly what was invoiced. Any variance between the accrual
    /// estimate and the actual invoice is real and returned for review — it never
    /// blocks the match, same as the "flag then net anyway" path in the doc's own
    /// diagram (Fig. 7).
    /// </summary>
    [HttpPost("{id:guid}/match")]
    [Authorize(Policy = "finance.subcontractorinvoice.process")]
    public async Task<ActionResult<MatchSupplierInvoiceResponse>> Match(Guid id, MatchSupplierInvoiceRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (request.AccrualIds is null || request.AccrualIds.Count == 0)
            return BadRequest("At least one AccrualId is required.");

        var invoice = await _db.SupplierInvoices.Include(si => si.Expenses).FirstOrDefaultAsync(si => si.Id == id, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status != SupplierInvoiceStatus.Received)
            return Conflict($"Supplier invoice is {invoice.Status}; only a Received invoice can be matched.");

        var accrualIds = request.AccrualIds.Distinct().ToList();
        var accruals = await _db.Set<SubcontractorAccrual>().Where(a => accrualIds.Contains(a.Id)).ToListAsync(ct);
        if (accruals.Count != accrualIds.Count)
            return NotFound("One or more accruals were not found.");
        if (accruals.Any(a => a.SubcontractorId != invoice.SubcontractorId))
            return BadRequest("Every accrual must belong to the same subcontractor as the invoice.");
        if (accruals.Any(a => a.CurrencyId != invoice.CurrencyId))
            return BadRequest("Every accrual must be in the same currency as the invoice.");
        if (accruals.Any(a => a.Status != SubcontractorAccrualStatus.Accrued))
            return Conflict("Every accrual must still be Accrued — one has already been matched.");

        var openPeriod = await _db.FinancialPeriods.FirstOrDefaultAsync(
            p => p.CompanyId == _tenantContext.CompanyId && p.Status == FinancialPeriodStatus.Open, ct);
        if (openPeriod is null)
            return Conflict("No open financial period for this company — create one first (POST /api/v1/financial-years).");

        var totalEstimated = accruals.Sum(a => a.EstimatedAmount);
        var varianceAmount = invoice.Amount - totalEstimated;

        var allocated = 0m;
        for (var i = 0; i < accruals.Count; i++)
        {
            var accrual = accruals[i];

            // Apportion the invoice's actual Amount by each accrual's share of the
            // total estimate; the last line absorbs whatever's left so the expensed
            // total always lands on invoice.Amount exactly, never a rounding-short cent.
            var expenseAmount = i == accruals.Count - 1
                ? invoice.Amount - allocated
                : totalEstimated == 0m ? 0m : Math.Round(invoice.Amount * (accrual.EstimatedAmount / totalEstimated), 2);
            allocated += expenseAmount;

            accrual.Status = SubcontractorAccrualStatus.Netted;

            // invoice was loaded (not newly Add()-ed), so it's already tracked as
            // Unchanged/Modified — appending only to its Expenses collection navigation
            // left EF unable to tell these were new rows, and it generated UPDATE
            // statements against nonexistent SubcontractorExpense rows instead of
            // INSERTs (a real 0-rows-affected DbUpdateConcurrencyException, caught live
            // testing this). Adding straight to the DbSet forces the correct Added state.
            _db.Set<SubcontractorExpense>().Add(new SubcontractorExpense
            {
                TenantId = _tenantContext.TenantId.Value,
                CompanyId = _tenantContext.CompanyId.Value,
                SubcontractorId = invoice.SubcontractorId,
                CurrencyId = invoice.CurrencyId,
                RateLineBuyId = accrual.RateLineBuyId,
                AccrualId = accrual.Id,
                SupplierInvoiceId = invoice.Id,
                FinancialPeriodId = openPeriod.Id,
                Amount = expenseAmount,
                Status = SubcontractorExpenseStatus.AvailableToExport
            });
        }

        invoice.Status = SupplierInvoiceStatus.Matched;

        await _db.SaveChangesAsync(ct);

        // Reload so the response reflects the SubcontractorExpense rows just inserted —
        // invoice.Expenses (populated only by the collection-navigation Add we removed
        // above) would otherwise come back empty even though the insert succeeded.
        await _db.Entry(invoice).Collection(si => si.Expenses).LoadAsync(ct);

        return Ok(new MatchSupplierInvoiceResponse(ToResponse(invoice), varianceAmount));
    }

    /// <summary>Flags a Received invoice as contested rather than matching it — e.g. the amount or the legs it claims don't check out. Only reachable from Received; a Matched invoice is corrected by a new SupplierInvoice, never edited in place.</summary>
    [HttpPost("{id:guid}/dispute")]
    [Authorize(Policy = "finance.subcontractorinvoice.process")]
    public async Task<IActionResult> Dispute(Guid id, DisputeSupplierInvoiceRequest request, CancellationToken ct)
    {
        var invoice = await _db.SupplierInvoices.FirstOrDefaultAsync(si => si.Id == id, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status != SupplierInvoiceStatus.Received)
            return Conflict($"Supplier invoice is {invoice.Status}; only a Received invoice can be disputed.");

        invoice.Status = SupplierInvoiceStatus.Disputed;
        invoice.DisputeReason = request.Reason;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Whether a Subcontractor is permitted to transact in a given currency (§4.3) — its own primary CurrencyId, or an explicit SubcontractorCurrency allow-list row.</summary>
    private async Task<bool> IsSubcontractorCurrencyAllowedAsync(Subcontractor subcontractor, Guid currencyId, CancellationToken ct) =>
        currencyId == subcontractor.CurrencyId
        || await _db.Set<SubcontractorCurrency>().AnyAsync(sc => sc.SubcontractorId == subcontractor.Id && sc.CurrencyId == currencyId, ct);

    private static SupplierInvoiceResponse ToResponse(SupplierInvoice invoice) => new(
        invoice.Id, invoice.SubcontractorId, invoice.CurrencyId, invoice.SupplierInvoiceNumber, invoice.InvoiceDate, invoice.ReceivedDate,
        invoice.Amount, invoice.Status, invoice.DisputeReason,
        invoice.Expenses.Select(e => new SubcontractorExpenseResponse(
            e.Id, e.RateLineBuyId, e.AccrualId, e.FinancialPeriodId, e.Amount, e.Status, e.FinalizedDate)).ToList());
}
