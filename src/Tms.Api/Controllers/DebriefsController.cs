using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Audit;
using Tms.Modules.Debrief;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record ApproveDebriefRequest(string? ResolutionNote = null);

/// <summary>
/// The Debrief Clerk's side of §09 (Fig. 5) — submission itself lives on
/// LegsController (addressed by leg, since that's what a driver/clerk actually has in
/// hand), but resolving the queue of PendingReview debriefs is naturally addressed by
/// debrief id, not leg id.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/debriefs")]
[Authorize]
public class DebriefsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly DebriefApprovalService _debriefApproval;

    public DebriefsController(
        TmsDbContext db, ITenantContext tenantContext, ICurrentUserAccessor currentUser, DebriefApprovalService debriefApproval)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _debriefApproval = debriefApproval;
    }

    /// <summary>
    /// Lists debriefs, optionally filtered by status — PendingReview is the Debrief
    /// Clerk's working queue. Never part of either portal's documented scope — a
    /// portal contact's own debrief access is already correctly scoped-to-their-own-leg
    /// on LegsController.GetDebrief/SubmitDebrief; this unscoped-by-party route would
    /// otherwise leak every debrief in the company (POD, incidents, expense claims) to
    /// any portal contact, so it's Forbidden outright for either type.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DebriefResponse>>> List(DebriefStatus? status, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var query = _db.Set<Debrief>().Include(d => d.Incidents).Include(d => d.Expenses).AsQueryable();
        if (status is DebriefStatus s) query = query.Where(d => d.Status == s);

        var debriefs = await query.OrderByDescending(d => d.SubmittedAt).ToListAsync(ct);
        return Ok(debriefs.Select(LegsController.ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DebriefResponse>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var debrief = await _db.Set<Debrief>()
            .Include(d => d.Incidents)
            .Include(d => d.Expenses)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        return debrief is null ? NotFound() : Ok(LegsController.ToResponse(debrief));
    }

    /// <summary>
    /// A Debrief Clerk resolves a PendingReview debrief (§09, Fig. 5) — the same effect
    /// as an auto-approve: applies its SubcontractorAccrual-claimed expenses and locks
    /// the leg as PodReceived. The doc's "or escalates" branch isn't a separate status
    /// here — an escalation is a clerk decision to loop in someone else before calling
    /// this, not a distinct system state, since both paths converge on the same
    /// outcome (Fig. 5: both lead to "Leg locked as POD Received").
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "debrief.approve")]
    public async Task<IActionResult> Approve(Guid id, ApproveDebriefRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var debrief = await _db.Set<Debrief>().Include(d => d.Expenses).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (debrief is null) return NotFound();
        if (debrief.Status != DebriefStatus.PendingReview)
            return Conflict($"Debrief is {debrief.Status}; only a PendingReview debrief can be approved.");

        // Atomically claims this debrief — only succeeds if it's still PendingReview at
        // the instant of the UPDATE, closing a race where two concurrent Approve calls
        // for the same debrief both pass the in-memory check above before either
        // commits (unlike a freshly-submitted, not-yet-persisted debrief on the
        // auto-approve path, this one is already visible to any caller holding its id).
        var claimed = await _db.Set<Debrief>()
            .Where(d => d.Id == id && d.Status == DebriefStatus.PendingReview)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DebriefStatus.Approved), ct);

        if (claimed == 0)
            return Conflict("This debrief was already resolved by a concurrent request.");

        var leg = await _db.LoadLegs.FirstAsync(l => l.Id == debrief.LoadLegId, ct);
        var load = await _db.Loads.Include(l => l.Legs).FirstAsync(l => l.Id == leg.LoadId, ct);

        debrief.ResolutionNote = request.ResolutionNote;

        var error = await _debriefApproval.ApproveAsync(debrief, leg, load, resolvedByUserId: _currentUser.UserId, ct);
        if (error is not null) return Conflict(error);

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
