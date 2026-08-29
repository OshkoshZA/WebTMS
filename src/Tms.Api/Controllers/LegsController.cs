using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Debrief;
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

    public LegsController(TmsDbContext db, ITenantContext tenantContext, DebriefApprovalService debriefApproval)
    {
        _db = db;
        _tenantContext = tenantContext;
        _debriefApproval = debriefApproval;
    }

    /// <summary>Retrieves a leg's Load Confirmation (§8.2) — PdfUrl is null for now, since there's no PDF-rendering infrastructure in this codebase yet.</summary>
    [HttpGet("{id:guid}/confirmation")]
    public async Task<ActionResult<LoadConfirmationResponse>> GetConfirmation(Guid id, CancellationToken ct)
    {
        var confirmation = await _db.LoadConfirmations.FirstOrDefaultAsync(lc => lc.LoadLegId == id, ct);
        return confirmation is null ? NotFound() : Ok(ToResponse(confirmation));
    }

    /// <summary>
    /// Records the subcontractor's response to an Issued confirmation (§8.2) — stands
    /// in for the Supplier Portal, which doesn't exist yet: an internal user captures
    /// what the carrier communicated (call, email), rather than the carrier submitting
    /// it themselves.
    /// </summary>
    [HttpPost("{id:guid}/confirmation/acknowledge")]
    public async Task<IActionResult> AcknowledgeConfirmation(Guid id, AcknowledgeConfirmationRequest request, CancellationToken ct)
    {
        var confirmation = await _db.LoadConfirmations.FirstOrDefaultAsync(lc => lc.LoadLegId == id, ct);
        if (confirmation is null) return NotFound($"No load confirmation exists for leg {id}.");
        if (confirmation.Status != LoadConfirmationStatus.Issued)
            return Conflict($"Confirmation is {confirmation.Status}; only an Issued confirmation can be acknowledged or declined.");

        confirmation.Status = request.Acknowledged ? LoadConfirmationStatus.Acknowledged : LoadConfirmationStatus.Declined;
        confirmation.DeclineReason = request.Acknowledged ? null : request.Reason;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/debrief")]
    public async Task<ActionResult<DebriefResponse>> GetDebrief(Guid id, CancellationToken ct)
    {
        var debrief = await _db.Set<Debrief>()
            .Include(d => d.Incidents)
            .Include(d => d.Expenses)
            .FirstOrDefaultAsync(d => d.LoadLegId == id, ct);
        return debrief is null ? NotFound() : Ok(ToResponse(debrief));
    }

    /// <summary>
    /// Submits a leg's debrief (§09, Fig. 5) — also used by driver mobile web, or by an
    /// internal user standing in for a subcontracted leg's carrier (the Supplier Portal,
    /// §13.3, doesn't exist yet). Everything is captured in one atomic call — odometer,
    /// fuel, POD, incidents, and expense lines — rather than a partial submission
    /// followed by separate calls to attach each one; a debrief with no exceptions
    /// auto-approves immediately, so there'd be no window left to add anything
    /// afterward otherwise. Auto-approves if nothing about it is exceptional; otherwise
    /// sits PendingReview for a Debrief Clerk (DebriefsController.Approve) to resolve.
    /// </summary>
    [HttpPost("{id:guid}/debrief")]
    public async Task<ActionResult<DebriefResponse>> SubmitDebrief(Guid id, SubmitDebriefRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var leg = await _db.LoadLegs.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (leg is null) return NotFound();
        if (leg.Status != LoadLegStatus.Delivered)
            return Conflict($"Leg is {leg.Status}; only a Delivered leg can be debriefed.");
        if (await _db.Set<Debrief>().AnyAsync(d => d.LoadLegId == id, ct))
            return Conflict("This leg has already been debriefed.");

        var load = await _db.Loads.Include(l => l.Legs).FirstAsync(l => l.Id == leg.LoadId, ct);

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
        if (!debrief.PodReceived) reasons.Add("Missing POD");
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
        }

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetDebrief), new { id }, ToResponse(debrief));
    }

    private static LoadConfirmationResponse ToResponse(LoadConfirmation confirmation) => new(
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
