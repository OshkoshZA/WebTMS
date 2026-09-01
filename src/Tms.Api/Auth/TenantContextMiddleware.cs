using System.Security.Claims;

namespace Tms.Api.Auth;

/// <summary>
/// Reads the TenantId/CompanyId/PlatformSupport claims off the authenticated JWT and
/// populates the request-scoped <see cref="HttpTenantContext"/> before anything else
/// runs — the "resolved once from the caller's JWT at the top of the request pipeline"
/// step described in docs/architecture.html §4.1. Runs after authentication, before
/// authorization and controller execution.
/// </summary>
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, HttpTenantContext tenantContext)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            if (Guid.TryParse(user.FindFirstValue("tenant_id"), out var tenantId))
                tenantContext.TenantId = tenantId;

            if (Guid.TryParse(user.FindFirstValue("company_id"), out var companyId))
                tenantContext.CompanyId = companyId;

            if (Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                tenantContext.UserId = userId;

            tenantContext.ApiClientId = user.FindFirstValue("client_id");
            tenantContext.IsPlatformSupport = user.IsInRole("PlatformSupport");

            // Distinct claim name from "client_id" above (§13.1) — that one identifies
            // an ApiClient (system-to-system auth), this one a Supplier Portal
            // contact's own Subcontractor. Different concepts that happen to share the
            // word "client" in this domain — a Client (customer) vs. an ApiClient
            // (integration credential) vs. a Subcontractor (carrier) portal contact.
            if (Guid.TryParse(user.FindFirstValue("subcontractor_id"), out var subcontractorId))
                tenantContext.SubcontractorId = subcontractorId;

            // "portal_client_id", not "client_id" — that name is already the ApiClient
            // claim above, and its value is a string identifier, not this Client's Guid
            // primary key; a Customer Portal contact's own scoping claim (§13.1).
            if (Guid.TryParse(user.FindFirstValue("portal_client_id"), out var portalClientId))
                tenantContext.ClientId = portalClientId;
        }

        await _next(context);
    }
}
