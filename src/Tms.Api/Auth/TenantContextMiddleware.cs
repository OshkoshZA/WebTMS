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
        }

        await _next(context);
    }
}
