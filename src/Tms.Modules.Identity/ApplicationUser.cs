using Microsoft.AspNetCore.Identity;

namespace Tms.Modules.Identity;

public enum UserStatus
{
    Active,
    Deactivated
}

/// <summary>
/// Internal staff user (docs/architecture.html §07). Built on ASP.NET Core Identity;
/// company-scoped role assignments are layered on top via <see cref="UserCompanyRole"/>
/// rather than Identity's own global user-role table, since the same person can hold
/// different roles in different Companies — always within one Tenant (§4.1).
///
/// Status is deliberately our own field, not Identity's built-in lockout mechanism —
/// every other entity in this app (Vehicle, Driver, Client, ApiClient) is deactivated
/// the same explicit way (never a hard delete, §11.5), and AuthController's Login/
/// Refresh check this directly rather than relying on lockout, which SignInManager
/// enforces but the UserManager.CheckPasswordAsync call this app uses does not.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public UserStatus Status { get; set; } = UserStatus.Active;
}
