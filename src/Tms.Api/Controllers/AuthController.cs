using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Auth;
using Tms.Infrastructure;
using Tms.Modules.Identity;

namespace Tms.Api.Controllers;

public record LoginRequest(string Email, string Password, Guid? CompanyId);
public record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid TenantId,
    Guid CompanyId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Functions);

/// <summary>
/// Interactive-user login (docs/architecture.html §11.1) — issues the JWT that
/// TenantContextMiddleware later resolves TenantId/CompanyId from. There is no
/// public self-registration endpoint here: internal users are provisioned by an
/// admin (§07), so this controller only ever authenticates an existing account.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TmsDbContext _db;
    private readonly JwtTokenService _tokenService;

    public AuthController(UserManager<ApplicationUser> userManager, TmsDbContext db, JwtTokenService tokenService)
    {
        _userManager = userManager;
        _db = db;
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized("Invalid email or password.");

        // A user's company assignments (§07) — which Company(ies) they hold a role in.
        var assignments = await _db.UserCompanyRoles
            .Where(ucr => ucr.UserId == user.Id)
            .ToListAsync(ct);

        if (assignments.Count == 0)
            return Forbid(); // authenticated, but not yet assigned to any Company

        var assignment = request.CompanyId is Guid requestedCompanyId
            ? assignments.FirstOrDefault(a => a.CompanyId == requestedCompanyId)
            : assignments.First(); // no company specified — default to the first assignment

        if (assignment is null)
            return Forbid(); // authenticated, but not assigned to the requested Company

        var roleIds = await _db.UserCompanyRoles
            .Where(ucr => ucr.UserId == user.Id && ucr.CompanyId == assignment.CompanyId)
            .Select(ucr => ucr.RoleId)
            .ToListAsync(ct);

        var roleNames = await _db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name!)
            .ToListAsync(ct);

        // Role → RoleFunction → Function (§07) — resolved once, at login, into flat
        // claims; FunctionAuthorizationHandler never has to walk this chain itself.
        var functionCodes = await _db.RoleFunctions
            .Where(rf => roleIds.Contains(rf.RoleId))
            .Join(_db.Functions, rf => rf.FunctionId, f => f.Id, (rf, f) => f.Code)
            .Distinct()
            .ToListAsync(ct);

        var token = _tokenService.IssueAccessToken(user, assignment.CompanyId, roleNames, functionCodes);

        return Ok(new LoginResponse(
            token.AccessToken, token.ExpiresAt, user.TenantId, assignment.CompanyId, roleNames, functionCodes));
    }

    /// <summary>
    /// OAuth2 client-credentials grant (docs/architecture.html §11.1) — the
    /// system-to-system counterpart to <see cref="Login"/>. Standard RFC 6749 §4.4
    /// shape: form-encoded request, "access_token"/"token_type"/"expires_in" response,
    /// an "error" body on failure — so any off-the-shelf OAuth2 client library can
    /// call this without knowing anything TMS-specific.
    /// </summary>
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token(
        [FromForm(Name = "grant_type")] string? grantType,
        [FromForm(Name = "client_id")] string? clientId,
        [FromForm(Name = "client_secret")] string? clientSecret,
        CancellationToken ct)
    {
        if (grantType != "client_credentials")
            return BadRequest(new { error = "unsupported_grant_type" });
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return BadRequest(new { error = "invalid_request" });

        // No tenant is known yet at this point — that's exactly what ClientId
        // resolves — so this is one of the few places IgnoreQueryFilters (§4.1) is
        // the correct call rather than a bypass to be suspicious of.
        var client = await _db.Set<ApiClient>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ClientId == clientId, ct);
        if (client is null || client.Status != ApiClientStatus.Active)
            return Unauthorized(new { error = "invalid_client" });

        var secrets = await _db.Set<ApiClientSecret>().IgnoreQueryFilters()
            .Where(s => s.ApiClientId == client.Id && s.RevokedAt == null)
            .ToListAsync(ct);

        var hasher = new PasswordHasher<ApiClient>();
        var verified = secrets.Any(s =>
            hasher.VerifyHashedPassword(client, s.SecretHash, clientSecret) != PasswordVerificationResult.Failed);
        if (!verified)
            return Unauthorized(new { error = "invalid_client" });

        var roleIds = await _db.Set<ApiClientRole>().IgnoreQueryFilters()
            .Where(r => r.ApiClientId == client.Id)
            .Select(r => r.RoleId)
            .ToListAsync(ct);

        var functionCodes = await _db.RoleFunctions
            .Where(rf => roleIds.Contains(rf.RoleId))
            .Join(_db.Functions, rf => rf.FunctionId, f => f.Id, (rf, f) => f.Code)
            .Distinct()
            .ToListAsync(ct);

        var token = _tokenService.IssueClientCredentialsToken(client, functionCodes);

        return Ok(new
        {
            access_token = token.AccessToken,
            token_type = "Bearer",
            expires_in = (int)(token.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds
        });
    }
}
