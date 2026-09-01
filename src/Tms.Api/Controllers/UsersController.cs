using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Audit;
using Tms.Modules.Identity;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateUserRequest(string Email, string Password, string DisplayName, Guid? InitialCompanyId = null, Guid? InitialRoleId = null);
public record UpdateUserRequest(string DisplayName);
public record AddCompanyRoleRequest(Guid CompanyId, Guid RoleId);

public record UserCompanyRoleResponse(Guid Id, Guid CompanyId, Guid RoleId, string RoleName);
public record UserResponse(Guid Id, string Email, string DisplayName, UserStatus Status, IReadOnlyList<UserCompanyRoleResponse> CompanyRoles);

/// <summary>
/// Internal user management (docs/architecture.html §07, §11.2) — creating a user here
/// is the only way one gets onto the platform (there is no public self-registration
/// endpoint, per AuthController). Follows the standard CRUD convention (§11.5): list /
/// get / create / update / deactivate / reactivate, never a hard delete, so a
/// deactivated user's history stays attributed. Company/role assignments are managed
/// as their own sub-resource rather than folded into Update, since a user can hold
/// more than one — the same reason ApiClient secrets get their own endpoint.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserAccessor _currentUser;

    public UsersController(
        TmsDbContext db, UserManager<ApplicationUser> userManager, ITenantContext tenantContext, ICurrentUserAccessor currentUser)
    {
        _db = db;
        _userManager = userManager;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    /// <summary>Never part of either portal's documented scope — exposes every internal user (and every other party's portal contacts) with their email/display name/company-role assignments, so any portal contact is Forbidden outright.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponse>>> List(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var users = await _db.Users.OrderBy(u => u.Email).ToListAsync(ct);
        var companyRolesByUser = await GetCompanyRolesByUserAsync(users.Select(u => u.Id), ct);

        return Ok(users.Select(u => ToResponse(u, companyRolesByUser)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        var companyRolesByUser = await GetCompanyRolesByUserAsync(new[] { id }, ct);
        return Ok(ToResponse(user, companyRolesByUser));
    }

    /// <summary>Creates an internal user and, if InitialCompanyId/InitialRoleId are both given, their first company/role assignment in one call.</summary>
    [HttpPost]
    [Authorize(Policy = "identity.user.manage")]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            return Unauthorized("Request is missing a resolved Tenant context.");

        if (request.InitialCompanyId is null != request.InitialRoleId is null)
            return BadRequest("InitialCompanyId and InitialRoleId must both be given, or both omitted.");

        if (request.InitialCompanyId is Guid companyId)
        {
            if (!await _db.Companies.AnyAsync(c => c.Id == companyId, ct))
                return NotFound($"Company {companyId} was not found.");
            if (!await _db.Roles.AnyAsync(r => r.Id == request.InitialRoleId, ct))
                return NotFound($"Role {request.InitialRoleId} was not found.");
        }

        var user = new ApplicationUser
        {
            TenantId = _tenantContext.TenantId.Value,
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName
        };

        IdentityResult createResult;
        try
        {
            createResult = await _userManager.CreateAsync(user, request.Password);
        }
        catch (DbUpdateException)
        {
            // UserManager's own duplicate-email check runs against the tenant-scoped
            // query filter and would normally have already caught this — this only
            // fires if two requests for the same email in the same Tenant race each
            // other past that check. The composite (TenantId, NormalizedUserName)
            // unique index (§07) still catches it at the database; this just turns
            // that into a clean response instead of a raw 500 with a stack trace.
            return Conflict("A user with that email already exists.");
        }

        if (!createResult.Succeeded)
        {
            return BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });
        }

        if (request.InitialCompanyId is Guid initialCompanyId)
        {
            _db.UserCompanyRoles.Add(new UserCompanyRole
            {
                UserId = user.Id,
                CompanyId = initialCompanyId,
                RoleId = request.InitialRoleId!.Value
            });
            await _db.SaveChangesAsync(ct);
        }

        var companyRolesByUser = await GetCompanyRolesByUserAsync(new[] { user.Id }, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, ToResponse(user, companyRolesByUser));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "identity.user.manage")]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        // Email is deliberately not editable here — it's the user's login identifier,
        // and changing it is an identity operation (re-confirmation, etc.) this app
        // doesn't support yet, not a plain field edit.
        user.DisplayName = request.DisplayName;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "identity.user.manage")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        // Deactivating requires identity.user.manage, which the target account would
        // lose the moment this succeeds — so a caller deactivating themselves could
        // strand the tenant with no one left who can call Reactivate. Have another
        // admin do it instead.
        if (id == _currentUser.UserId)
            return Conflict("You cannot deactivate your own account — have another user with identity.user.manage do it.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.Status = UserStatus.Deactivated; // never a hard delete — §11.5; checked by Login/Refresh
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Reverses a Deactivate — the only path back to Active, mirroring how Deactivate is the only path out of it.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = "identity.user.manage")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.Status = UserStatus.Active;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Grants a user a role in a company (§07) — a user can hold several of these at once, one per Company.</summary>
    [HttpPost("{id:guid}/company-roles")]
    [Authorize(Policy = "identity.user.manage")]
    public async Task<ActionResult<UserCompanyRoleResponse>> AddCompanyRole(Guid id, AddCompanyRoleRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct);
        if (company is null) return NotFound($"Company {request.CompanyId} was not found.");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, ct);
        if (role is null) return NotFound($"Role {request.RoleId} was not found.");

        // A Portal contact (§13.1) getting a stray company/role assignment here would
        // let their next login resolve CompanyId to a company their Subcontractor/
        // Client has nothing to do with, carrying that company's full function set
        // alongside their still-attached portal scoping claim — the assignment must be
        // confined to the Company that actually owns their Subcontractor/Client, and
        // the Role to the matching portal.* functions only, same as contact creation
        // itself enforces (SubcontractorContactsController/ClientContactsController).
        if (user.SubcontractorId is Guid ownSubcontractorId)
        {
            var subcontractor = await _db.Subcontractors.FirstAsync(s => s.Id == ownSubcontractorId, ct);
            if (subcontractor.CompanyId != request.CompanyId)
                return BadRequest("A Supplier Portal contact can only be assigned a role in the Company that owns their Subcontractor.");

            // Materialized before .Any() — StartsWith(string, StringComparison) has no
            // SQL translation, so this must run as an in-memory check, not part of the
            // query itself (confirmed live: the query-translated form throws a 500).
            var functionCodes = await _db.RoleFunctions
                .Where(rf => rf.RoleId == request.RoleId)
                .Join(_db.Functions, rf => rf.FunctionId, f => f.Id, (rf, f) => f.Code)
                .ToListAsync(ct);
            if (functionCodes.Any(code => !code.StartsWith("portal.subcontractor.", StringComparison.Ordinal)))
                return BadRequest("A Supplier Portal contact's Role may only grant portal.subcontractor.* functions.");
        }
        else if (user.ClientId is Guid ownClientId)
        {
            var client = await _db.Clients.FirstAsync(c => c.Id == ownClientId, ct);
            if (client.CompanyId != request.CompanyId)
                return BadRequest("A Customer Portal contact can only be assigned a role in the Company that owns their Client.");

            var functionCodes = await _db.RoleFunctions
                .Where(rf => rf.RoleId == request.RoleId)
                .Join(_db.Functions, rf => rf.FunctionId, f => f.Id, (rf, f) => f.Code)
                .ToListAsync(ct);
            if (functionCodes.Any(code => !code.StartsWith("portal.client.", StringComparison.Ordinal)))
                return BadRequest("A Customer Portal contact's Role may only grant portal.client.* functions.");
        }

        var alreadyAssigned = await _db.UserCompanyRoles.AnyAsync(
            ucr => ucr.UserId == id && ucr.CompanyId == request.CompanyId && ucr.RoleId == request.RoleId, ct);
        if (alreadyAssigned)
            return Conflict("This user already holds that role in that company.");

        var assignment = new UserCompanyRole { UserId = id, CompanyId = request.CompanyId, RoleId = request.RoleId };
        _db.UserCompanyRoles.Add(assignment);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id }, new UserCompanyRoleResponse(assignment.Id, assignment.CompanyId, assignment.RoleId, role.Name!));
    }

    /// <summary>Revokes one company/role assignment — the user keeps any others they hold.</summary>
    [HttpDelete("{id:guid}/company-roles/{companyRoleId:guid}")]
    [Authorize(Policy = "identity.user.manage")]
    public async Task<IActionResult> RemoveCompanyRole(Guid id, Guid companyRoleId, CancellationToken ct)
    {
        // UserCompanyRole carries no TenantId of its own and isn't query-filtered —
        // unlike every other mutating action in this controller (Update, Deactivate,
        // Reactivate, AddCompanyRole), this used to look the assignment up directly by
        // UserId, so a caller holding identity.user.manage in their own tenant could
        // remove a role assignment from a user belonging to a *different* tenant,
        // given that tenant's UserId + UserCompanyRole.Id. Loading the user first
        // through the tenant-filtered _db.Users set, the same way AddCompanyRole
        // already does, closes that gap.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        var assignment = await _db.UserCompanyRoles.FirstOrDefaultAsync(ucr => ucr.Id == companyRoleId && ucr.UserId == id, ct);
        if (assignment is null) return NotFound();

        // Removing an assignment that carries identity.user.manage needs a
        // last-holder check, the same reasoning as Deactivate's self-guard above and
        // RolesController.RevokeFunction's equivalent check on identity.role.manage:
        // even if the role itself still grants the function, this specific user might
        // be the only one actually holding a role that grants it here — losing it
        // would strand the tenant with no one able to manage users at all, including
        // reversing this very removal.
        var roleGrantsUserManage = await _db.RoleFunctions
            .Join(_db.Functions, rf => rf.FunctionId, f => f.Id, (rf, f) => new { rf.RoleId, f.Code })
            .AnyAsync(x => x.RoleId == assignment.RoleId && x.Code == "identity.user.manage", ct);

        if (roleGrantsUserManage)
        {
            var anotherHolderRemains = await _db.UserCompanyRoles
                .Where(ucr => ucr.Id != companyRoleId)
                .Join(_db.RoleFunctions, ucr => ucr.RoleId, rf => rf.RoleId, (ucr, rf) => new { ucr.UserId, rf.FunctionId })
                .Join(_db.Functions, x => x.FunctionId, f => f.Id, (x, f) => new { x.UserId, f.Code })
                .Where(x => x.Code == "identity.user.manage")
                .Join(_db.Users, x => x.UserId, u => u.Id, (x, u) => u.Status)
                .AnyAsync(status => status != UserStatus.Deactivated, ct);

            if (!anotherHolderRemains)
                return Conflict("Removing this would leave no active user in the tenant able to manage users — assign identity.user.manage to another user first.");
        }

        _db.UserCompanyRoles.Remove(assignment);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ILookup<Guid, UserCompanyRoleResponse>> GetCompanyRolesByUserAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.ToList();
        var rows = await _db.UserCompanyRoles
            .Where(ucr => ids.Contains(ucr.UserId))
            .Join(_db.Roles, ucr => ucr.RoleId, r => r.Id, (ucr, r) => new { ucr.UserId, ucr.Id, ucr.CompanyId, ucr.RoleId, RoleName = r.Name })
            .ToListAsync(ct);

        return rows.ToLookup(r => r.UserId, r => new UserCompanyRoleResponse(r.Id, r.CompanyId, r.RoleId, r.RoleName!));
    }

    private static UserResponse ToResponse(ApplicationUser user, ILookup<Guid, UserCompanyRoleResponse> companyRolesByUser) =>
        new(user.Id, user.Email!, user.DisplayName, user.Status, companyRolesByUser[user.Id].ToList());
}
