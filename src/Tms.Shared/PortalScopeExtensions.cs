namespace Tms.Shared;

/// <summary>
/// The row-level scoping check Fig. 10 (§13.1) describes — repeated identically across
/// every endpoint a Supplier or Customer Portal contact can reach, so it lives in one
/// place rather than being reimplemented per controller. Internal staff
/// (SubcontractorId/ClientId both null) are never restricted by either check; a portal
/// contact is restricted to exactly the one Subcontractor/Client their own login is
/// scoped to — and, critically, a portal contact of the OTHER type is restricted too:
/// checking only "is my own field null" (an earlier, buggy version of this method)
/// treated a Client contact as unrestricted staff for every subcontractor-scoped check,
/// since their own SubcontractorId is null exactly like a staff member's — and
/// symmetrically for a Subcontractor contact against client-scoped checks. "Internal
/// staff" now means BOTH fields are null, not just the one this particular check cares
/// about.
/// </summary>
public static class PortalScopeExtensions
{
    public static bool CanAccessSubcontractor(this ITenantContext tenantContext, Guid subcontractorId) =>
        tenantContext.SubcontractorId is null
            ? tenantContext.ClientId is null
            : tenantContext.SubcontractorId == subcontractorId;

    public static bool CanAccessClient(this ITenantContext tenantContext, Guid clientId) =>
        tenantContext.ClientId is null
            ? tenantContext.SubcontractorId is null
            : tenantContext.ClientId == clientId;
}
