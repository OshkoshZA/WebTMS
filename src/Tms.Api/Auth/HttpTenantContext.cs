using Tms.Modules.Audit;
using Tms.Shared;

namespace Tms.Api.Auth;

/// <summary>
/// Resolves the request's Tenant/Company/user identity from JWT claims
/// (docs/architecture.html §4.1, §11.1) — the single place this happens, so
/// TmsDbContext's global query filters and the audit interceptor always see
/// the same values. Populated by <see cref="TenantContextMiddleware"/> once
/// per request, before any controller or DbContext code runs.
/// </summary>
public class HttpTenantContext : ITenantContext, ICurrentUserAccessor
{
    public Guid? TenantId { get; set; }
    public Guid? CompanyId { get; set; }
    public bool IsPlatformSupport { get; set; }
    public Guid? UserId { get; set; }
    public string? ApiClientId { get; set; }
}
