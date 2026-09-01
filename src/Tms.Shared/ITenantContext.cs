namespace Tms.Shared;

/// <summary>
/// Resolved once per request from the caller's JWT (docs/architecture.html §4.1) —
/// never from a request body or query string. Consumed by TmsDbContext's global
/// query filters, which are the application-layer half of the defense-in-depth
/// tenant isolation described in §4.1 (SQL Server Row-Level Security is the second,
/// independent layer and is applied in a deployment script, not here).
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? CompanyId { get; }

    /// <summary>
    /// True only for the narrow, break-glass Platform Support role (§4.1, §07) —
    /// never true for a customer's own user, and every use is captured by the
    /// audit trail (§12).
    /// </summary>
    bool IsPlatformSupport { get; }

    /// <summary>
    /// Set only when the caller is a Supplier Portal contact (§13.1) — the row-level
    /// scoping claim Fig. 10 describes, restricting them to their own Subcontractor's
    /// legs, confirmations, and accruals. Null for every other kind of caller
    /// (internal staff, an ApiClient), the same way IsPlatformSupport is false for them.
    /// </summary>
    Guid? SubcontractorId { get; }

    /// <summary>The Customer Portal's equivalent of <see cref="SubcontractorId"/> (§13.1) — restricting a ClientContact to their own Client's loads, invoices, and credit notes.</summary>
    Guid? ClientId { get; }
}
