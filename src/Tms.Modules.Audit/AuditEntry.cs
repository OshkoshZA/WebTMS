using Tms.Shared;

namespace Tms.Modules.Audit;

public enum AuditAction
{
    Create,
    Update,
    Delete,
    StatusChange,
    Approve,
    Override
}

/// <summary>
/// A single audited change (docs/architecture.html §12) — written by
/// <see cref="AuditSaveChangesInterceptor"/> for every mutating SaveChanges call,
/// so coverage doesn't depend on each module remembering to log itself. Append-only:
/// nothing in this codebase issues an UPDATE or DELETE against this table.
/// </summary>
public class AuditEntry : TenantScopedEntity
{
    public Guid? CompanyId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public AuditAction Action { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public string? ChangedByApiClientId { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public string? Reason { get; set; }
}
