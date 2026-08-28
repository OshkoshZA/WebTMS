using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Loads;

namespace Tms.Api.Controllers;

public record AcknowledgeConfirmationRequest(bool Acknowledged, string? Reason = null);

public record LoadConfirmationResponse(
    Guid Id, Guid LoadLegId, Guid SubcontractorId, string DocumentNumber, DateTimeOffset IssuedDate,
    LoadConfirmationStatus Status, string? PdfUrl, string? DeclineReason);

/// <summary>
/// Leg-level actions addressed independently of their parent Load (docs/architecture.html
/// §11.2 lists these as top-level /legs/{id}/... routes, distinct from /loads/{id}/legs/...,
/// which is where a leg is created/allocated). Only the Load Confirmation piece of this
/// (§8.2) exists so far; debrief and POD (§09, also documented as /legs/{id}/... routes)
/// land with the Debrief module.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/legs")]
[Authorize]
public class LegsController : ControllerBase
{
    private readonly TmsDbContext _db;

    public LegsController(TmsDbContext db)
    {
        _db = db;
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

    private static LoadConfirmationResponse ToResponse(LoadConfirmation confirmation) => new(
        confirmation.Id, confirmation.LoadLegId, confirmation.SubcontractorId, confirmation.DocumentNumber,
        confirmation.IssuedDate, confirmation.Status, confirmation.PdfUrl, confirmation.DeclineReason);
}
