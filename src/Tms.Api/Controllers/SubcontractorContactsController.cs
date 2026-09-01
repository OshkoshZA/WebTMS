using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Identity;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateSubcontractorContactRequest(string Email, string Password, string DisplayName, Guid RoleId);
public record SubcontractorContactResponse(Guid Id, string Email, string DisplayName, UserStatus Status);

/// <summary>
/// Supplier Portal contacts (docs/architecture.html §13.1) — the doc's own
/// "SubcontractorContact" entity, backed by the same ApplicationUser table internal
/// staff use rather than a second parallel Identity user type (see the class doc
/// comment on ApplicationUser). Reuses the internal Role/Function model exactly as
/// §13.1 describes: a contact's one UserCompanyRole is scoped to the Company that
/// owns their Subcontractor, and should be granted a Role carrying only portal.* — a
/// company's own admin is the one provisioning it, so nothing here forces that; there's
/// no validation preventing an internal Role from being granted to a portal contact
/// by mistake, matching how RolesController.Create doesn't reserved-name-check
/// portal roles for the internal side either.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/subcontractors/{subcontractorId:guid}/contacts")]
[Authorize]
public class SubcontractorContactsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITenantContext _tenantContext;

    public SubcontractorContactsController(TmsDbContext db, UserManager<ApplicationUser> userManager, ITenantContext tenantContext)
    {
        _db = db;
        _userManager = userManager;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SubcontractorContactResponse>>> List(Guid subcontractorId, CancellationToken ct)
    {
        if (!_tenantContext.CanAccessSubcontractor(subcontractorId)) return Forbid();
        if (!await _db.Subcontractors.AnyAsync(s => s.Id == subcontractorId, ct)) return NotFound();

        var contacts = await _db.Users
            .Where(u => u.SubcontractorId == subcontractorId)
            .OrderBy(u => u.Email)
            .ToListAsync(ct);

        return Ok(contacts.Select(ToResponse));
    }

    [HttpPost]
    [Authorize(Policy = "subcontractor.contact.manage")]
    public async Task<ActionResult<SubcontractorContactResponse>> Create(Guid subcontractorId, CreateSubcontractorContactRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            return Unauthorized("Request is missing a resolved Tenant context.");
        if (!_tenantContext.CanAccessSubcontractor(subcontractorId)) return Forbid();

        var subcontractor = await _db.Subcontractors.FirstOrDefaultAsync(s => s.Id == subcontractorId, ct);
        if (subcontractor is null) return NotFound($"Subcontractor {subcontractorId} was not found.");

        var roleFunctionCodes = await _db.RoleFunctions
            .Where(rf => rf.RoleId == request.RoleId)
            .Join(_db.Functions, rf => rf.FunctionId, f => f.Id, (rf, f) => f.Code)
            .ToListAsync(ct);
        if (roleFunctionCodes.Count == 0 && !await _db.Roles.AnyAsync(r => r.Id == request.RoleId, ct))
            return NotFound($"Role {request.RoleId} was not found.");

        // A Supplier Portal contact must only ever be handed a Role restricted to
        // portal.subcontractor.* — the doc's own trust boundary (§13.1: a portal
        // contact's Role "should be granted... only portal.*"), previously unenforced.
        // Handing an external contact a Role that also carries any internal function
        // (identity.user.manage, finance.invoice.manage, etc.) would give them a fully
        // working internal-staff session for this Company, JWT scoping claim
        // notwithstanding — most endpoints check the function claim alone.
        if (roleFunctionCodes.Any(code => !code.StartsWith("portal.subcontractor.", StringComparison.Ordinal)))
            return BadRequest("A Supplier Portal contact's Role may only grant portal.subcontractor.* functions.");

        var user = new ApplicationUser
        {
            TenantId = _tenantContext.TenantId.Value,
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName,
            SubcontractorId = subcontractorId
        };

        IdentityResult createResult;
        try
        {
            createResult = await _userManager.CreateAsync(user, request.Password);
        }
        catch (DbUpdateException)
        {
            // Same race UsersController.Create already documents and closes — the
            // (TenantId, NormalizedUserName) unique index catching two concurrent
            // Create calls for the same email that both passed UserManager's own
            // duplicate check.
            return Conflict("A user with that email already exists.");
        }

        if (!createResult.Succeeded)
            return BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });

        _db.UserCompanyRoles.Add(new UserCompanyRole
        {
            UserId = user.Id,
            CompanyId = subcontractor.CompanyId,
            RoleId = request.RoleId
        });
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { subcontractorId }, ToResponse(user));
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "subcontractor.contact.manage")]
    public async Task<IActionResult> Deactivate(Guid subcontractorId, Guid id, CancellationToken ct)
    {
        if (!_tenantContext.CanAccessSubcontractor(subcontractorId)) return Forbid();

        var contact = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.SubcontractorId == subcontractorId, ct);
        if (contact is null) return NotFound();

        contact.Status = UserStatus.Deactivated; // never a hard delete — §11.5; checked by Login/Refresh
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Reverses a Deactivate — the only path back to Active, mirroring how Deactivate is the only path out of it.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = "subcontractor.contact.manage")]
    public async Task<IActionResult> Reactivate(Guid subcontractorId, Guid id, CancellationToken ct)
    {
        if (!_tenantContext.CanAccessSubcontractor(subcontractorId)) return Forbid();

        var contact = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.SubcontractorId == subcontractorId, ct);
        if (contact is null) return NotFound();

        contact.Status = UserStatus.Active;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static SubcontractorContactResponse ToResponse(ApplicationUser user) =>
        new(user.Id, user.Email ?? string.Empty, user.DisplayName, user.Status);
}
