using Microsoft.AspNetCore.Identity;

namespace Tms.Modules.Identity;

public enum UserStatus
{
    Active,
    Deactivated
}

/// <summary>
/// Any login identity in this system (docs/architecture.html §07, §13.1) — internal
/// staff, and, when <see cref="SubcontractorId"/> or <see cref="ClientId"/> is set, a
/// Supplier or Customer Portal contact (the doc's own "SubcontractorContact"/
/// "ClientContact" — reusing this table rather than a second parallel Identity user
/// type, since the login/JWT/RoleFunction machinery is identical either way; only the
/// JWT ends up carrying an extra scoping claim, §13.1 Fig. 10). Built on ASP.NET Core
/// Identity; company-scoped role assignments are layered on top via
/// <see cref="UserCompanyRole"/> rather than Identity's own global user-role table,
/// since the same person can hold different roles in different Companies — always
/// within one Tenant (§4.1) — and a portal contact's one UserCompanyRole is scoped to
/// the Company that owns their Subcontractor/Client.
///
/// Status is deliberately our own field, not Identity's built-in lockout mechanism —
/// every other entity in this app (Vehicle, Driver, Client, ApiClient) is deactivated
/// the same explicit way (never a hard delete, §11.5), and AuthController's Login/
/// Refresh check this directly rather than relying on lockout, which SignInManager
/// enforces but the UserManager.CheckPasswordAsync call this app uses does not. The
/// doc's own three-state Invited|Active|Disabled for a portal contact collapses to
/// this same two-state enum — there's no email-invite infrastructure anywhere in this
/// codebase yet to make "Invited" mean anything distinct from "Active".
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>Set only for a Supplier Portal contact — null for internal staff. Mutually exclusive with ClientId and with normal internal-staff usage; nothing about this identity model prevents more than one being set, but nothing in this codebase ever does.</summary>
    public Guid? SubcontractorId { get; set; }

    /// <summary>Set only for a Customer Portal contact — null for internal staff. Mutually exclusive with SubcontractorId and with normal internal-staff usage.</summary>
    public Guid? ClientId { get; set; }
}
