using Tms.Shared;

namespace Tms.Modules.Exceptions;

public enum ExceptionSeverity
{
    Info,
    Warning,
    Critical
}

public enum ExceptionStatus
{
    Open,
    Acknowledged,
    Resolved
}

/// <summary>
/// The shared cross-module attention mechanism (docs/architecture.html §16.1) — rather
/// than each module inventing its own alerting, every place elsewhere in this design
/// that already flags something for a human writes one of these, so a dashboard is a
/// filtered, scoped query against one table, never bespoke per-module logic. Named
/// ExceptionRecord rather than the doc's own "Exception", to avoid colliding with
/// System.Exception throughout the rest of the codebase.
/// </summary>
public class ExceptionRecord : CompanyScopedEntity
{
    public string Category { get; set; } = string.Empty;
    public ExceptionSeverity Severity { get; set; }

    /// <summary>The kind of record this exception is tied to, e.g. "Debrief", "Client", "SupplierInvoice" — paired with EntityId as a lightweight polymorphic reference rather than a foreign key per possible source, since new sources plug in over time (§16.1's own Fig. 13).</summary>
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }

    public ExceptionStatus Status { get; set; } = ExceptionStatus.Open;
    public DateTimeOffset RaisedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? AssignedToUserId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
}
