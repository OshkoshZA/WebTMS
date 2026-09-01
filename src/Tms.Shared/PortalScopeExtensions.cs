namespace Tms.Shared;

/// <summary>
/// The row-level scoping check Fig. 10 (§13.1) describes — repeated identically across
/// every endpoint a Supplier or Customer Portal contact can reach, so it lives in one
/// place rather than being reimplemented per controller. Internal staff
/// (SubcontractorId/ClientId both null) are never restricted by either check; a portal
/// contact is restricted to exactly the one Subcontractor/Client their own login is
/// scoped to.
/// </summary>
public static class PortalScopeExtensions
{
    public static bool CanAccessSubcontractor(this ITenantContext tenantContext, Guid subcontractorId) =>
        tenantContext.SubcontractorId is null || tenantContext.SubcontractorId == subcontractorId;

    public static bool CanAccessClient(this ITenantContext tenantContext, Guid clientId) =>
        tenantContext.ClientId is null || tenantContext.ClientId == clientId;
}
