using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Audit;
using Tms.Modules.Billing;
using Tms.Modules.Debrief;
using Tms.Modules.Exceptions;
using Tms.Modules.Loads;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record ResolveExceptionRequest(string? ResolutionNotes = null);

public record ExceptionResponse(
    Guid Id, string Category, ExceptionSeverity Severity, string EntityType, Guid EntityId,
    ExceptionStatus Status, DateTimeOffset RaisedAt, Guid? AssignedToUserId, string Description,
    DateTimeOffset? ResolvedAt, string? ResolutionNotes);

/// <summary>
/// The read/resolve side of §16.1's shared exception mechanism — every dashboard (§16.2,
/// §16.3, §16.4) is meant to be a filtered, scoped query against this one table rather
/// than bespoke per-module logic; ExceptionService (src/Tms.Api/Services) is the write
/// side other controllers call into. Scoping here is company-wide only (internal
/// staff) — the ClientContact/SubcontractorContact scoped views §16.1 also describes
/// need the Customer/Supplier Portal identity types (§13), which don't exist yet.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/exceptions")]
[Authorize]
public class ExceptionsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IAuthorizationService _authorizationService;

    public ExceptionsController(
        TmsDbContext db, ICurrentUserAccessor currentUser, ITenantContext tenantContext, IAuthorizationService authorizationService)
    {
        _db = db;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// An ExceptionRecord names an EntityType/EntityId, not a Client/Subcontractor
    /// directly (§16.1's own polymorphic-reference design), so scoping one to a portal
    /// caller means resolving each of the three currently-wired sources (§16.1's Fig.
    /// 13) back to the party that owns it: "Client" is already the ClientId itself;
    /// "SupplierInvoice" carries its own SubcontractorId; "Debrief" needs a join through
    /// its LoadLeg to either the Load's ClientId or the leg's own SubcontractorId. The
    /// other three documented sources (accounting sync failure, compliance expiry, DSR
    /// deadline) aren't wired into ExceptionService.Raise anywhere yet, so there's
    /// nothing to resolve for them either way.
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>> OwnDebriefIdsForClientAsync(Guid clientId, CancellationToken ct) =>
        await _db.Set<Debrief>()
            .Join(_db.LoadLegs, d => d.LoadLegId, l => l.Id, (d, l) => new { d, l })
            .Join(_db.Loads, x => x.l.LoadId, load => load.Id, (x, load) => new { x.d, load })
            .Where(x => x.load.ClientId == clientId)
            .Select(x => x.d.Id)
            .ToListAsync(ct);

    private async Task<IReadOnlyCollection<Guid>> OwnDebriefIdsForSubcontractorAsync(Guid subcontractorId, CancellationToken ct) =>
        await _db.Set<Debrief>()
            .Join(_db.LoadLegs, d => d.LoadLegId, l => l.Id, (d, l) => new { d, l })
            .Where(x => x.l.SubcontractorId == subcontractorId)
            .Select(x => x.d.Id)
            .ToListAsync(ct);

    /// <summary>
    /// A Customer Portal contact's own view — exceptions tied to their own loads
    /// (Debrief) or raised against their own Client directly (CreditOverride). A
    /// Supplier Portal contact (ClientId null, same as staff) is explicitly Forbidden
    /// rather than silently falling through to the unrestricted staff branch below —
    /// the exact bug shape every other portal-scoping fix in this codebase has closed.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExceptionResponse>>> List(ExceptionStatus? status, CancellationToken ct)
    {
        var query = _db.Set<ExceptionRecord>().AsQueryable();

        if (_tenantContext.ClientId is Guid clientId)
        {
            var authResult = await _authorizationService.AuthorizeAsync(User, "portal.client.viewloads");
            if (!authResult.Succeeded) return Forbid();

            var debriefIds = await OwnDebriefIdsForClientAsync(clientId, ct);
            query = query.Where(e =>
                (e.EntityType == nameof(Tms.Modules.Loads.Client) && e.EntityId == clientId) ||
                (e.EntityType == nameof(Debrief) && debriefIds.Contains(e.EntityId)));
        }
        else if (_tenantContext.SubcontractorId is Guid subcontractorId)
        {
            var authResult = await _authorizationService.AuthorizeAsync(User, "portal.subcontractor.viewlegs");
            if (!authResult.Succeeded) return Forbid();

            var supplierInvoiceIds = await _db.SupplierInvoices
                .Where(si => si.SubcontractorId == subcontractorId)
                .Select(si => si.Id)
                .ToListAsync(ct);
            var debriefIds = await OwnDebriefIdsForSubcontractorAsync(subcontractorId, ct);
            query = query.Where(e =>
                (e.EntityType == nameof(SupplierInvoice) && supplierInvoiceIds.Contains(e.EntityId)) ||
                (e.EntityType == nameof(Debrief) && debriefIds.Contains(e.EntityId)));
        }
        // Neither scoping id set — internal staff — sees every exception in the
        // company, the same as before this scoping existed; TmsDbContext's global
        // query filters (§4.1) already keep this to the caller's own company.

        if (status is ExceptionStatus s) query = query.Where(e => e.Status == s);

        var records = await query.OrderByDescending(e => e.RaisedAt).ToListAsync(ct);
        return Ok(records.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExceptionResponse>> Get(Guid id, CancellationToken ct)
    {
        var record = await _db.Set<ExceptionRecord>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (record is null) return NotFound();

        if (_tenantContext.ClientId is Guid clientId)
        {
            var authResult = await _authorizationService.AuthorizeAsync(User, "portal.client.viewloads");
            if (!authResult.Succeeded) return Forbid();

            var visible = record.EntityType == nameof(Tms.Modules.Loads.Client)
                ? record.EntityId == clientId
                : record.EntityType == nameof(Debrief) && (await OwnDebriefIdsForClientAsync(clientId, ct)).Contains(record.EntityId);
            if (!visible) return Forbid();
        }
        else if (_tenantContext.SubcontractorId is Guid subcontractorId)
        {
            var authResult = await _authorizationService.AuthorizeAsync(User, "portal.subcontractor.viewlegs");
            if (!authResult.Succeeded) return Forbid();

            var visible = record.EntityType == nameof(SupplierInvoice)
                ? await _db.SupplierInvoices.AnyAsync(si => si.Id == record.EntityId && si.SubcontractorId == subcontractorId, ct)
                : record.EntityType == nameof(Debrief) && (await OwnDebriefIdsForSubcontractorAsync(subcontractorId, ct)).Contains(record.EntityId);
            if (!visible) return Forbid();
        }

        return Ok(ToResponse(record));
    }

    /// <summary>Claims an Open exception — records who's looking at it, without yet asserting it's actually fixed (that's Resolve).</summary>
    [HttpPost("{id:guid}/acknowledge")]
    [Authorize(Policy = "exception.manage")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct)
    {
        var record = await _db.Set<ExceptionRecord>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (record is null) return NotFound();
        if (record.Status != ExceptionStatus.Open)
            return Conflict($"Exception is {record.Status}; only an Open exception can be acknowledged.");

        record.Status = ExceptionStatus.Acknowledged;
        record.AssignedToUserId = _currentUser.UserId;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Resolves an exception with a note on how — allowed from Open directly, not just
    /// from Acknowledged, since requiring a separate claim step first adds no value
    /// when whatever it flagged is already fixed (e.g. a source that calls
    /// ExceptionService.ResolveByEntityAsync itself, like a Debrief being approved).
    /// </summary>
    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = "exception.manage")]
    public async Task<IActionResult> Resolve(Guid id, ResolveExceptionRequest request, CancellationToken ct)
    {
        var record = await _db.Set<ExceptionRecord>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (record is null) return NotFound();
        if (record.Status == ExceptionStatus.Resolved)
            return Conflict("This exception is already Resolved.");

        record.Status = ExceptionStatus.Resolved;
        record.ResolvedAt = DateTimeOffset.UtcNow;
        record.ResolutionNotes = request.ResolutionNotes;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static ExceptionResponse ToResponse(ExceptionRecord record) => new(
        record.Id, record.Category, record.Severity, record.EntityType, record.EntityId,
        record.Status, record.RaisedAt, record.AssignedToUserId, record.Description,
        record.ResolvedAt, record.ResolutionNotes);
}
