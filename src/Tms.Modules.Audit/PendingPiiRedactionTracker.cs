namespace Tms.Modules.Audit;

/// <summary>
/// A request-scoped signal from a caller upstream of DbContext.SaveChanges (currently
/// only DataSubjectRequestsController's Erasure fulfillment) telling
/// AuditSaveChangesInterceptor that a specific property of a specific entity is about to
/// be overwritten with anonymized data — so the one AuditEntry that same SaveChanges call
/// generates never captures the pre-erasure value in its OldValueJson.
///
/// This is deliberately capture-time only: AuditEntry stays strictly append-only (§12) —
/// nothing here ever edits a row already persisted, and every prior AuditEntry still
/// carries whatever it always carried. Registered scoped, matching the request-scoped
/// DbContext it exists alongside, so a marked entry never survives past the request that
/// queued it.
/// </summary>
public class PendingPiiRedactionTracker
{
    private readonly HashSet<(string EntityType, string EntityId, string PropertyName)> _pending = new();

    public void MarkForRedaction(string entityType, string entityId, string propertyName) =>
        _pending.Add((entityType, entityId, propertyName));

    public bool ShouldRedact(string entityType, string entityId, string propertyName) =>
        _pending.Contains((entityType, entityId, propertyName));
}
