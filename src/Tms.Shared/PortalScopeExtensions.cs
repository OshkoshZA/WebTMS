namespace Tms.Shared;

/// <summary>
/// The row-level scoping check Fig. 10 (§13.1) describes — repeated identically across
/// every endpoint a Supplier Portal contact can reach, so it lives in one place rather
/// than being reimplemented per controller. Internal staff (SubcontractorId null) are
/// never restricted by this; a portal contact is restricted to exactly the one
/// Subcontractor their own login is scoped to.
/// </summary>
public static class PortalScopeExtensions
{
    public static bool CanAccessSubcontractor(this ITenantContext tenantContext, Guid subcontractorId) =>
        tenantContext.SubcontractorId is null || tenantContext.SubcontractorId == subcontractorId;
}
