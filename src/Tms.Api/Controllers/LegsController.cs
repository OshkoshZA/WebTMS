using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Debrief;
using Tms.Modules.Exceptions;
using Tms.Modules.Loads;
using Tms.Modules.Rating;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record AcknowledgeConfirmationRequest(bool Acknowledged, string? Reason = null);

public record LoadConfirmationResponse(
    Guid Id, Guid LoadLegId, Guid SubcontractorId, string DocumentNumber, DateTimeOffset IssuedDate,
    LoadConfirmationStatus Status, string? PdfUrl, string? DeclineReason);

public record SubmitDebriefIncidentRequest(IncidentType Type, IncidentSeverity Severity, string Narrative);

public record SubmitDebriefExpenseRequest(
    Guid ExpenseTypeId, string Description, decimal Amount, Guid CurrencyId,
    string? ReceiptImageUrl, ClaimedAgainst ClaimedAgainst, Guid? AccrualId = null);

public record SubmitDebriefRequest(
    decimal? OdometerStart, decimal? OdometerEnd, decimal? FuelLitres, decimal? FuelCost, decimal? DrivingHours,
    bool PodReceived, string? PodImageUrl,
    IReadOnlyList<SubmitDebriefIncidentRequest>? Incidents = null,
    IReadOnlyList<SubmitDebriefExpenseRequest>? Expenses = null);

public record DebriefIncidentResponse(Guid Id, IncidentType Type, IncidentSeverity Severity, string Narrative);

public record DebriefExpenseResponse(
    Guid Id, Guid ExpenseTypeId, string Description, decimal Amount, Guid CurrencyId,
    string? ReceiptImageUrl, ClaimedAgainst ClaimedAgainst, Guid? AccrualId);

public record DebriefResponse(
    Guid Id, Guid LoadLegId, Guid? DriverId, Guid? VehicleId,
    decimal? OdometerStart, decimal? OdometerEnd, decimal? FuelLitres, decimal? FuelCost, decimal? DrivingHours,
    bool PodReceived, string? PodImageUrl, DateTimeOffset SubmittedAt, DebriefStatus Status, string? ExceptionReasons,
    Guid? ResolvedByUserId, DateTimeOffset? ResolvedAt, string? ResolutionNote,
    IReadOnlyList<DebriefIncidentResponse> Incidents, IReadOnlyList<DebriefExpenseResponse> Expenses);

/// <summary>
/// Leg-level actions addressed independently of their parent Load (docs/architecture.html
/// §11.2 lists these as top-level /legs/{id}/... routes, distinct from /loads/{id}/legs/...,
/// which is where a leg is created/allocated). Covers Load Confirmation (§8.2) and, now,
/// debrief submission (§09) — POD upload proper is still a bare URL string, the same
/// "no upload infrastructure exists yet" gap as everywhere else a *Url field appears.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/legs")]
[Authorize]
public class LegsController : ControllerBase
{
    // No per-country/company driving-hours config exists yet — a placeholder, the same
    // "structurally correct now, real once it exists" pattern as VAT being 0 before a
    // rate table existed.
    private const decimal RegulatoryDrivingHoursLimit = 14m;

    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly DebriefApprovalService _debriefApproval;
    private readonly ExceptionService _exceptions;
    private readonly IAuthorizationService _authorizationService;

    public LegsController(
        TmsDbContext db, ITenantContext tenantContext, DebriefApprovalService debriefApproval,
        ExceptionService exceptions, IAuthorizationService authorizationService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _debriefApproval = debriefApproval;
        _exceptions = exceptions;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// The row-level scoping and function check every Supplier Portal action needs
    /// (§13.1) — internal staff (neither SubcontractorId nor ClientId set) are entirely
    /// unaffected, preserving this endpoint's existing behavior for them. A portal
    /// contact must both own the leg's subcontractor (delegating to
    /// ITenantContext.CanAccessSubcontractor, which — unlike an earlier, buggy version
    /// of this method — correctly Forbids a Customer Portal contact too, not just a
    /// Supplier Portal contact for a different Subcontractor) and hold the specific
    /// portal.* function for the action they're attempting. A null legSubcontractorId
    /// (an OwnFleet leg) is never accessible to any portal caller.
    /// </summary>
    private async Task<ActionResult?> CheckPortalAccessAsync(Guid? legSubcontractorId, string requiredFunction)
    {
        if (_tenantContext.SubcontractorId is null && _tenantContext.ClientId is null) return null;
        if (legSubcontractorId is null || !_tenantContext.CanAccessSubcontractor(legSubcontractorId.Value)) return Forbid();

        var authResult = await _authorizationService.AuthorizeAsync(User, requiredFunction);
        return authResult.Succeeded ? null : Forbid();
    }

    /// <summary>Retrieves a leg's Load Confirmation (§8.2) — PdfUrl is null for now, since there's no PDF-rendering infrastructure in this codebase yet. Row-level scoped for a Supplier Portal caller (§13.1), same as every other portal action.</summary>
    [HttpGet("{id:guid}/confirmation")]
    public async Task<ActionResult<LoadConfirmationResponse>> GetConfirmation(Guid id, CancellationToken ct)
    {
        var confirmation = await _db.LoadConfirmations.FirstOrDefaultAsync(lc => lc.LoadLegId == id, ct);
        if (confirmation is null) return NotFound();

        var portalCheck = await CheckPortalAccessAsync(confirmation.SubcontractorId, "portal.subcontractor.viewlegs");
        if (portalCheck is not null) return portalCheck;

        return Ok(ToResponse(confirmation));
    }

    /// <summary>
    /// Records the subcontractor's response to an Issued confirmation (§8.2) — either
    /// the Supplier Portal's own portal.subcontractor.acknowledgeconfirmation action
    /// (§13.3), or an internal user standing in for a carrier who called or emailed
    /// instead.
    /// </summary>
    [HttpPost("{id:guid}/confirmation/acknowledge")]
    public async Task<IActionResult> AcknowledgeConfirmation(Guid id, AcknowledgeConfirmationRequest request, CancellationToken ct)
    {
        var confirmation = await _db.LoadConfirmations.FirstOrDefaultAsync(lc => lc.LoadLegId == id, ct);
        if (confirmation is null) return NotFound($"No load confirmation exists for leg {id}.");

        var portalCheck = await CheckPortalAccessAsync(confirmation.SubcontractorId, "portal.subcontractor.acknowledgeconfirmation");
        if (portalCheck is not null) return portalCheck;

        if (confirmation.Status != LoadConfirmationStatus.Issued)
            return Conflict($"Confirmation is {confirmation.Status}; only an Issued confirmation can be acknowledged or declined.");

        confirmation.Status = request.Acknowledged ? LoadConfirmationStatus.Acknowledged : LoadConfirmationStatus.Declined;
        confirmation.DeclineReason = request.Acknowledged ? null : request.Reason;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Row-level scoped for a Supplier Portal caller (§13.1), same as every other portal action — a debrief carries no SubcontractorId of its own, so this resolves it from the leg.</summary>
    [HttpGet("{id:guid}/debrief")]
    public async Task<ActionResult<DebriefResponse>> GetDebrief(Guid id, CancellationToken ct)
    {
        var debrief = await _db.Set<Debrief>()
            .Include(d => d.Incidents)
            .Include(d => d.Expenses)
            .FirstOrDefaultAsync(d => d.LoadLegId == id, ct);
        if (debrief is null) return NotFound();

        if (_tenantContext.SubcontractorId is not null)
        {
            var legSubcontractorId = await _db.LoadLegs.Where(l => l.Id == id).Select(l => l.SubcontractorId).FirstOrDefaultAsync(ct);
            var portalCheck = await CheckPortalAccessAsync(legSubcontractorId, "portal.subcontractor.viewlegs");
            if (portalCheck is not null) return portalCheck;
        }

        return Ok(ToResponse(debrief));
    }

    /// <summary>
    /// Submits a leg's debrief (§09, Fig. 5) — also used by driver mobile web, by the
    /// Supplier Portal's own portal.subcontractor.uploadpod action for a subcontracted
    /// leg (§13.3 — the doc names it narrower than what this endpoint actually covers,
    /// since no separate "just POD" endpoint exists; a carrier's detention claims etc.
    /// go through the same Expenses list as everything else described in §09/§9.1), or
    /// by an internal user standing in for a carrier who called or emailed instead.
    /// Everything is captured in one atomic call — odometer, fuel, POD, incidents, and
    /// expense lines — rather than a partial submission followed by separate calls to
    /// attach each one; a debrief with no exceptions auto-approves immediately, so
    /// there'd be no window left to add anything afterward otherwise. Auto-approves if
    /// nothing about it is exceptional; otherwise sits PendingReview for a Debrief
    /// Clerk (DebriefsController.Approve) to resolve.
    /// </summary>
    [HttpPost("{id:guid}/debrief")]
    public async Task<ActionResult<DebriefResponse>> SubmitDebrief(Guid id, SubmitDebriefRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var leg = await _db.LoadLegs.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (leg is null) return NotFound();

        var portalCheck = await CheckPortalAccessAsync(leg.SubcontractorId, "portal.subcontractor.uploadpod");
        if (portalCheck is not null) return portalCheck;

        if (leg.Status != LoadLegStatus.Delivered)
            return Conflict($"Leg is {leg.Status}; only a Delivered leg can be debriefed.");
        if (await _db.Set<Debrief>().AnyAsync(d => d.LoadLegId == id, ct))
            return Conflict("This leg has already been debriefed.");

        var load = await _db.Loads.Include(l => l.Legs).FirstAsync(l => l.Id == leg.LoadId, ct);
        if (load.Status is LoadStatus.OnHold or LoadStatus.Cancelled)
            return Conflict($"Load is {load.Status}; this leg cannot be debriefed until it's released.");

        var incidentRequests = request.Incidents ?? Array.Empty<SubmitDebriefIncidentRequest>();
        var expenseRequests = request.Expenses ?? Array.Empty<SubmitDebriefExpenseRequest>();

        foreach (var expenseRequest in expenseRequests)
        {
            if (!await _db.ExpenseTypes.AnyAsync(t => t.Id == expenseRequest.ExpenseTypeId, ct))
                return NotFound($"Expense type {expenseRequest.ExpenseTypeId} was not found.");
            if (!await _db.Currencies.AnyAsync(c => c.Id == expenseRequest.CurrencyId, ct))
                return NotFound($"Currency {expenseRequest.CurrencyId} was not found.");

            if (expenseRequest.ClaimedAgainst == ClaimedAgainst.SubcontractorAccrual)
            {
                if (leg.ExecutionType != LoadLegExecutionType.Subcontracted)
                    return BadRequest("Only a Subcontracted leg's expenses can be claimed against a SubcontractorAccrual.");
                if (expenseRequest.AccrualId is null)
                    return BadRequest("AccrualId is required when ClaimedAgainst is SubcontractorAccrual.");

                var accrual = await _db.Set<Tms.Modules.Billing.SubcontractorAccrual>()
                    .FirstOrDefaultAsync(a => a.Id == expenseRequest.AccrualId, ct);
                if (accrual is null) return NotFound($"Accrual {expenseRequest.AccrualId} was not found.");
                if (accrual.Status != Tms.Modules.Billing.SubcontractorAccrualStatus.Accrued)
                    return Conflict($"Accrual {expenseRequest.AccrualId} has already been matched — it can no longer be adjusted.");

                var belongsToLeg = await _db.Set<RateLine>()
                    .Where(r => r.Id == accrual.RateLineBuyId)
                    .Join(_db.Set<CommodityLine>(), r => r.SourceId, cl => cl.Id, (r, cl) => cl.LoadLegId)
                    .AnyAsync(legId => legId == leg.Id, ct);
                if (!belongsToLeg) return BadRequest($"Accrual {expenseRequest.AccrualId} does not belong to this leg.");
            }
            else if (expenseRequest.AccrualId is not null)
            {
                return BadRequest("AccrualId is only valid when ClaimedAgainst is SubcontractorAccrual.");
            }
        }

        var debrief = new Debrief
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            LoadLegId = id,
            DriverId = leg.DriverId,
            VehicleId = leg.VehicleId,
            OdometerStart = request.OdometerStart,
            OdometerEnd = request.OdometerEnd,
            FuelLitres = request.FuelLitres,
            FuelCost = request.FuelCost,
            DrivingHours = request.DrivingHours,
            PodReceived = request.PodReceived,
            PodImageUrl = request.PodImageUrl
        };

        foreach (var incidentRequest in incidentRequests)
        {
            debrief.Incidents.Add(new DebriefIncident
            {
                TenantId = debrief.TenantId,
                CompanyId = debrief.CompanyId,
                Type = incidentRequest.Type,
                Severity = incidentRequest.Severity,
                Narrative = incidentRequest.Narrative
            });
        }

        foreach (var expenseRequest in expenseRequests)
        {
            debrief.Expenses.Add(new DebriefExpense
            {
                TenantId = debrief.TenantId,
                CompanyId = debrief.CompanyId,
                ExpenseTypeId = expenseRequest.ExpenseTypeId,
                Description = expenseRequest.Description,
                Amount = expenseRequest.Amount,
                CurrencyId = expenseRequest.CurrencyId,
                ReceiptImageUrl = expenseRequest.ReceiptImageUrl,
                ClaimedAgainst = expenseRequest.ClaimedAgainst,
                AccrualId = expenseRequest.AccrualId
            });
        }

        _db.Set<Debrief>().Add(debrief);

        // §09's five documented exception triggers: two of them — odometer distance
        // deviating >10% from a "planned route distance," and fuel consumption outside
        // a vehicle-class "expected range" — can't be checked at all, since neither
        // figure is stored anywhere in this codebase. Flagging that honestly rather
        // than fabricating a threshold; see the class doc comment.
        var reasons = new List<string>();
        if (!debrief.PodReceived || string.IsNullOrWhiteSpace(debrief.PodImageUrl)) reasons.Add("Missing POD");
        if (debrief.Incidents.Count > 0) reasons.Add($"{debrief.Incidents.Count} incident(s) logged");
        if (debrief.DrivingHours is decimal hours && hours > RegulatoryDrivingHoursLimit)
            reasons.Add($"Driving hours ({hours:0.#}) exceed the regulatory limit ({RegulatoryDrivingHoursLimit:0.#})");

        if (reasons.Count == 0)
        {
            var error = await _debriefApproval.ApproveAsync(debrief, leg, load, resolvedByUserId: null, ct);
            if (error is not null) return Conflict(error);
        }
        else
        {
            debrief.Status = DebriefStatus.PendingReview;
            debrief.ExceptionReasons = string.Join(", ", reasons);

            // Feeds §16.1's shared dashboard mechanism — resolved by
            // DebriefApprovalService.ApproveAsync the moment a Debrief Clerk (or the
            // auto-approve path, on some later resubmission model) approves it.
            _exceptions.Raise(
                debrief.TenantId, debrief.CompanyId, "Debrief", ExceptionSeverity.Warning,
                nameof(Debrief), debrief.Id, debrief.ExceptionReasons);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // DebriefLoadLegIndex catches a race between two concurrent Submit calls
            // for the same leg that both passed the AnyAsync check above before either
            // committed — turns that into a clean 409 instead of a raw 500, same
            // pattern as SupplierInvoicesController.Create.
            return Conflict("This leg has already been debriefed.");
        }

        return CreatedAtAction(nameof(GetDebrief), new { id }, ToResponse(debrief));
    }

    internal static LoadConfirmationResponse ToResponse(LoadConfirmation confirmation) => new(
        confirmation.Id, confirmation.LoadLegId, confirmation.SubcontractorId, confirmation.DocumentNumber,
        confirmation.IssuedDate, confirmation.Status, confirmation.PdfUrl, confirmation.DeclineReason);

    internal static DebriefResponse ToResponse(Debrief debrief) => new(
        debrief.Id, debrief.LoadLegId, debrief.DriverId, debrief.VehicleId,
        debrief.OdometerStart, debrief.OdometerEnd, debrief.FuelLitres, debrief.FuelCost, debrief.DrivingHours,
        debrief.PodReceived, debrief.PodImageUrl, debrief.SubmittedAt, debrief.Status, debrief.ExceptionReasons,
        debrief.ResolvedByUserId, debrief.ResolvedAt, debrief.ResolutionNote,
        debrief.Incidents.Select(i => new DebriefIncidentResponse(i.Id, i.Type, i.Severity, i.Narrative)).ToList(),
        debrief.Expenses.Select(e => new DebriefExpenseResponse(
            e.Id, e.ExpenseTypeId, e.Description, e.Amount, e.CurrencyId, e.ReceiptImageUrl, e.ClaimedAgainst, e.AccrualId)).ToList());
}
