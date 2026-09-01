using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Identity;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateClientContactRequest(string Email, string Password, string DisplayName, Guid RoleId);
public record ClientContactResponse(Guid Id, string Email, string DisplayName, UserStatus Status);

/// <summary>
/// Customer Portal contacts (docs/architecture.html §13.1) — the doc's own
/// "ClientContact" entity, the exact mirror of SubcontractorContactsController for the
/// buy/sell side: backed by the same ApplicationUser table (see its class doc
/// comment), a contact's one UserCompanyRole scoped to the Company that owns their
/// Client, holding a Role that should carry only portal.client.* functions.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clients/{clientId:guid}/contacts")]
[Authorize]
public class ClientContactsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITenantContext _tenantContext;

    public ClientContactsController(TmsDbContext db, UserManager<ApplicationUser> userManager, ITenantContext tenantContext)
    {
        _db = db;
        _userManager = userManager;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClientContactResponse>>> List(Guid clientId, CancellationToken ct)
    {
        if (!_tenantContext.CanAccessClient(clientId)) return Forbid();
        if (!await _db.Clients.AnyAsync(c => c.Id == clientId, ct)) return NotFound();

        var contacts = await _db.Users
            .Where(u => u.ClientId == clientId)
            .OrderBy(u => u.Email)
            .ToListAsync(ct);

        return Ok(contacts.Select(ToResponse));
    }

    [HttpPost]
    [Authorize(Policy = "client.contact.manage")]
    public async Task<ActionResult<ClientContactResponse>> Create(Guid clientId, CreateClientContactRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            return Unauthorized("Request is missing a resolved Tenant context.");

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (client is null) return NotFound($"Client {clientId} was not found.");

        if (!await _db.Roles.AnyAsync(r => r.Id == request.RoleId, ct))
            return NotFound($"Role {request.RoleId} was not found.");

        var user = new ApplicationUser
        {
            TenantId = _tenantContext.TenantId.Value,
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName,
            ClientId = clientId
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
            CompanyId = client.CompanyId,
            RoleId = request.RoleId
        });
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { clientId }, ToResponse(user));
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "client.contact.manage")]
    public async Task<IActionResult> Deactivate(Guid clientId, Guid id, CancellationToken ct)
    {
        var contact = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.ClientId == clientId, ct);
        if (contact is null) return NotFound();

        contact.Status = UserStatus.Deactivated; // never a hard delete — §11.5; checked by Login/Refresh
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Reverses a Deactivate — the only path back to Active, mirroring how Deactivate is the only path out of it.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = "client.contact.manage")]
    public async Task<IActionResult> Reactivate(Guid clientId, Guid id, CancellationToken ct)
    {
        var contact = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.ClientId == clientId, ct);
        if (contact is null) return NotFound();

        contact.Status = UserStatus.Active;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static ClientContactResponse ToResponse(ApplicationUser user) =>
        new(user.Id, user.Email ?? string.Empty, user.DisplayName, user.Status);
}
