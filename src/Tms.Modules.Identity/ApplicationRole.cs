using Microsoft.AspNetCore.Identity;

namespace Tms.Modules.Identity;

/// <summary>
/// A named bundle of Functions (§07) — e.g. Dispatcher, Finance Clerk, Credit Controller.
/// Built on ASP.NET Core Identity's role type; the functions a role grants live in
/// <see cref="RoleFunction"/>, and which company a user holds the role in lives in
/// <see cref="UserCompanyRole"/>.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public Guid TenantId { get; set; }
}

/// <summary>
/// A discrete capability, e.g. "load.create", "rate.override", "debrief.approve",
/// "audit.view". Functions are registered by the API itself as endpoints ship —
/// never created by an end user (§07).
/// </summary>
public class Function
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>Join table: the functions a Role grants.</summary>
public class RoleFunction
{
    public Guid RoleId { get; set; }
    public Guid FunctionId { get; set; }
}

/// <summary>
/// A user's role assignment, scoped to one Company — the mechanism behind "the same
/// person can be a Dispatcher in one country and view-only in another" (§07).
/// </summary>
public class UserCompanyRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RoleId { get; set; }
}
