using System.Globalization;
using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Audit;

namespace Tms.Api.Controllers;

public record AuditEntryResponse(
    Guid Id, Guid? CompanyId, string EntityType, string EntityId, AuditAction Action,
    Guid? ChangedByUserId, string? ChangedByApiClientId, DateTimeOffset ChangedAtUtc,
    string? OldValueJson, string? NewValueJson, string? Reason);

/// <summary>
/// The audit.view-gated viewer/export half of Fig. 9 (docs/architecture.html §12) —
/// capture itself (AuditSaveChangesInterceptor) needed no controller at all, since it
/// runs underneath every module's own SaveChanges call. This is a read-only query
/// surface over the append-only AuditEntry store: no create/update/delete here, and
/// none of the usual master-data List/Get response shaping — every column is exposed,
/// since redacting anything here would defeat the point of an audit trail.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/audit-entries")]
[Authorize(Policy = "audit.view")]
public class AuditEntriesController : ControllerBase
{
    // Interactive browsing (List) is capped — nobody reviews thousands of rows in a UI —
    // but the export endpoint deliberately has no cap: §12.3 promises "the full trail is
    // exportable on demand for an external audit or regulatory inquiry," and a compliance
    // export that silently truncated would be worse than not having one at all.
    private const int DefaultTake = 100;
    private const int MaxTake = 500;

    private readonly TmsDbContext _db;

    public AuditEntriesController(TmsDbContext db)
    {
        _db = db;
    }

    /// <summary>Filtered, capped, newest-first — for reviewing recent history, not for a compliance export (see Export below).</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditEntryResponse>>> List(
        string? entityType, string? entityId, Guid? companyId, Guid? changedByUserId,
        AuditAction? action, DateTimeOffset? from, DateTimeOffset? to, int take = DefaultTake, CancellationToken ct = default)
    {
        var query = BuildFilteredQuery(entityType, entityId, companyId, changedByUserId, action, from, to);
        var capped = Math.Clamp(take, 1, MaxTake);

        var entries = await query
            .OrderByDescending(e => e.ChangedAtUtc)
            .Take(capped)
            .ToListAsync(ct);

        return Ok(entries.Select(ToResponse));
    }

    /// <summary>
    /// A live-recomputed CSV of every entry matching the same filters as List (§11.6:
    /// a data export, never archived, always reflecting current data) — no row cap,
    /// unlike List, since a partial compliance export would be worse than none.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        string? entityType, string? entityId, Guid? companyId, Guid? changedByUserId,
        AuditAction? action, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var query = BuildFilteredQuery(entityType, entityId, companyId, changedByUserId, action, from, to);
        var entries = await query.OrderByDescending(e => e.ChangedAtUtc).ToListAsync(ct);

        var csv = new StringBuilder();
        csv.AppendLine("Id,CompanyId,EntityType,EntityId,Action,ChangedByUserId,ChangedByApiClientId,ChangedAtUtc,OldValueJson,NewValueJson,Reason");
        foreach (var e in entries)
        {
            csv.AppendLine(string.Join(",",
                CsvField(e.Id.ToString()), CsvField(e.CompanyId?.ToString()), CsvField(e.EntityType), CsvField(e.EntityId),
                CsvField(e.Action.ToString()), CsvField(e.ChangedByUserId?.ToString()), CsvField(e.ChangedByApiClientId),
                CsvField(e.ChangedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                CsvField(e.OldValueJson), CsvField(e.NewValueJson), CsvField(e.Reason)));
        }

        var fileName = $"audit-trail-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.csv";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
    }

    private IQueryable<AuditEntry> BuildFilteredQuery(
        string? entityType, string? entityId, Guid? companyId, Guid? changedByUserId,
        AuditAction? action, DateTimeOffset? from, DateTimeOffset? to)
    {
        // No explicit tenant filtering — TmsDbContext's global query filters (§4.1)
        // already scope AuditEntry (a TenantScopedEntity) to the caller's own tenant,
        // the same convention every other query in this codebase relies on.
        var query = _db.AuditEntries.AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(e => e.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(e => e.EntityId == entityId);
        if (companyId is Guid cid) query = query.Where(e => e.CompanyId == cid);
        if (changedByUserId is Guid uid) query = query.Where(e => e.ChangedByUserId == uid);
        if (action is AuditAction a) query = query.Where(e => e.Action == a);
        if (from is DateTimeOffset f) query = query.Where(e => e.ChangedAtUtc >= f);
        if (to is DateTimeOffset t) query = query.Where(e => e.ChangedAtUtc <= t);

        return query;
    }

    private static string CsvField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuoting ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static AuditEntryResponse ToResponse(AuditEntry e) => new(
        e.Id, e.CompanyId, e.EntityType, e.EntityId, e.Action,
        e.ChangedByUserId, e.ChangedByApiClientId, e.ChangedAtUtc,
        e.OldValueJson, e.NewValueJson, e.Reason);
}
