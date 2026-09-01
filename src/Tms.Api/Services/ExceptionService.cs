using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Exceptions;

namespace Tms.Api.Services;

/// <summary>
/// The write side of §16.1's shared mechanism — every place elsewhere in this design
/// that already flags something for a human calls Raise instead of inventing its own
/// alerting, and ResolveByEntityAsync when whatever it flagged is no longer live (e.g.
/// a Debrief's exception, resolved the moment DebriefApprovalService approves it).
/// Neither method calls SaveChangesAsync itself — like DebriefApprovalService's own
/// entity mutations, the caller's own SaveChangesAsync persists these alongside
/// whatever else that request is already doing in the same unit of work.
/// </summary>
public class ExceptionService
{
    private readonly TmsDbContext _db;

    public ExceptionService(TmsDbContext db)
    {
        _db = db;
    }

    public void Raise(
        Guid tenantId, Guid companyId, string category, ExceptionSeverity severity,
        string entityType, Guid entityId, string description)
    {
        _db.Set<ExceptionRecord>().Add(new ExceptionRecord
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Category = category,
            Severity = severity,
            EntityType = entityType,
            EntityId = entityId,
            Description = description
        });
    }

    public async Task ResolveByEntityAsync(string entityType, Guid entityId, CancellationToken ct)
    {
        var openOrAcknowledged = await _db.Set<ExceptionRecord>()
            .Where(e => e.EntityType == entityType && e.EntityId == entityId && e.Status != ExceptionStatus.Resolved)
            .ToListAsync(ct);

        foreach (var record in openOrAcknowledged)
        {
            record.Status = ExceptionStatus.Resolved;
            record.ResolvedAt = DateTimeOffset.UtcNow;
        }
    }
}
