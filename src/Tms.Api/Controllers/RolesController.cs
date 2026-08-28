using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Identity;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateRoleRequest(string Name);
public record GrantFunctionRequest(Guid FunctionId);

public record FunctionResponse(Guid Id, string Code, string Description);
public record RoleResponse(Guid Id, string Name, IReadOnlyList<FunctionResponse> Functions);

/// <summary>
/// Role management (docs/architecture.html §07, §11.2) — a Role is a named bundle of
/// Functions; a user holds one or more, scoped per company, via UsersController's
/// company-roles sub-resource. Functions themselves are read-only here (see
/// FunctionsController) — they're registered by the API itself as endpoints ship,
/// never user-created — so the only thing this controller manages is which of the
/// existing functions a role grants. No delete/rename: the doc's endpoint table only
/// calls for list/create, and removing a role out from under users who hold it is a
/// bigger operation than this pass covers.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/roles")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ITenantContext _tenantContext;

    public RolesController(TmsDbContext db, RoleManager<ApplicationRole> roleManager, ITenantContext tenantContext)
    {
        _db = db;
        _roleManager = roleManager;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoleResponse>>> List(CancellationToken ct)
    {
        var roles = await _db.Roles.OrderBy(r => r.Name).ToListAsync(ct);
        var functionsByRole = await GetFunctionsByRoleAsync(roles.Select(r => r.Id), ct);

        return Ok(roles.Select(r => ToResponse(r, functionsByRole)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RoleResponse>> Get(Guid id, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();

        var functionsByRole = await GetFunctionsByRoleAsync(new[] { id }, ct);
        return Ok(ToResponse(role, functionsByRole));
    }

    [HttpPost]
    [Authorize(Policy = "identity.role.manage")]
    public async Task<ActionResult<RoleResponse>> Create(CreateRoleRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            return Unauthorized("Request is missing a resolved Tenant context.");

        var role = new ApplicationRole { Name = request.Name, TenantId = _tenantContext.TenantId.Value };

        IdentityResult createResult;
        try
        {
            createResult = await _roleManager.CreateAsync(role);
        }
        catch (DbUpdateException)
        {
            // RoleManager's own duplicate-name check runs against the tenant-scoped
            // query filter and would normally have already caught this — this only
            // fires if two requests for the same name in the same Tenant race each
            // other past that check. The composite (TenantId, NormalizedName) unique
            // index (§07) still catches it at the database; this just turns that into
            // a clean response instead of a raw 500 with a stack trace.
            return Conflict("A role with that name already exists.");
        }

        if (!createResult.Succeeded)
        {
            return BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });
        }

        return CreatedAtAction(nameof(Get), new { id = role.Id }, ToResponse(role, await GetFunctionsByRoleAsync(new[] { role.Id }, ct)));
    }

    /// <summary>Grants a role one more function — a role starts with none, so this is what actually makes a freshly-created role useful.</summary>
    [HttpPost("{id:guid}/functions")]
    [Authorize(Policy = "identity.role.manage")]
    public async Task<IActionResult> GrantFunction(Guid id, GrantFunctionRequest request, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();

        var function = await _db.Functions.FirstOrDefaultAsync(f => f.Id == request.FunctionId, ct);
        if (function is null) return NotFound($"Function {request.FunctionId} was not found.");

        var alreadyGranted = await _db.RoleFunctions.AnyAsync(rf => rf.RoleId == id && rf.FunctionId == request.FunctionId, ct);
        if (alreadyGranted)
            return Conflict("This role already has that function.");

        _db.RoleFunctions.Add(new RoleFunction { RoleId = id, FunctionId = request.FunctionId });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Revokes one function from a role — anyone holding the role loses that capability on their next login/refresh (functions are baked into the JWT, §11.1).</summary>
    [HttpDelete("{id:guid}/functions/{functionId:guid}")]
    [Authorize(Policy = "identity.role.manage")]
    public async Task<IActionResult> RevokeFunction(Guid id, Guid functionId, CancellationToken ct)
    {
        var grant = await _db.RoleFunctions.FirstOrDefaultAsync(rf => rf.RoleId == id && rf.FunctionId == functionId, ct);
        if (grant is null) return NotFound();

        _db.RoleFunctions.Remove(grant);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ILookup<Guid, FunctionResponse>> GetFunctionsByRoleAsync(IEnumerable<Guid> roleIds, CancellationToken ct)
    {
        var ids = roleIds.ToList();
        var rows = await _db.RoleFunctions
            .Where(rf => ids.Contains(rf.RoleId))
            .Join(_db.Functions, rf => rf.FunctionId, f => f.Id, (rf, f) => new { rf.RoleId, f.Id, f.Code, f.Description })
            .ToListAsync(ct);

        return rows.ToLookup(r => r.RoleId, r => new FunctionResponse(r.Id, r.Code, r.Description));
    }

    private static RoleResponse ToResponse(ApplicationRole role, ILookup<Guid, FunctionResponse> functionsByRole) =>
        new(role.Id, role.Name!, functionsByRole[role.Id].ToList());
}
