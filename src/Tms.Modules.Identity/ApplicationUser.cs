using Microsoft.AspNetCore.Identity;

namespace Tms.Modules.Identity;

/// <summary>
/// Internal staff user (docs/architecture.html §07). Built on ASP.NET Core Identity;
/// company-scoped role assignments are layered on top via <see cref="UserCompanyRole"/>
/// rather than Identity's own global user-role table, since the same person can hold
/// different roles in different Companies — always within one Tenant (§4.1).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
