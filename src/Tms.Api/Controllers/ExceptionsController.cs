using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Audit;
using Tms.Modules.Exceptions;
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

    public ExceptionsController(TmsDbContext db, ICurrentUserAccessor currentUser, ITenantContext tenantContext)
    {
        _db = db;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Never part of either portal's documented scope — the ClientContact/
    /// SubcontractorContact scoped exception views §16.1 itself describes need row-level
    /// scoping this table doesn't have (an ExceptionRecord names an EntityType/EntityId,
    /// not a Client/Subcontractor directly), so rather than build that out speculatively,
    /// any portal contact is Forbidden outright — the company-wide internal dashboard
    /// view stays internal-staff-only until that scoping is actually built.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExceptionResponse>>> List(ExceptionStatus? status, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        // No explicit tenant/company filtering here — TmsDbContext's global query
        // filters (§4.1) already scope this to the caller's own company, the same
        // convention every other List action in this codebase follows.
        var query = _db.Set<ExceptionRecord>().AsQueryable();
        if (status is ExceptionStatus s) query = query.Where(e => e.Status == s);

        var records = await query.OrderByDescending(e => e.RaisedAt).ToListAsync(ct);
        return Ok(records.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExceptionResponse>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var record = await _db.Set<ExceptionRecord>().FirstOrDefaultAsync(e => e.Id == id, ct);
        return record is null ? NotFound() : Ok(ToResponse(record));
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
