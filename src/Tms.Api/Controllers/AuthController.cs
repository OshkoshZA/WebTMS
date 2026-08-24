using Asp.Versioning;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Auth;
using Tms.Infrastructure;
using Tms.Modules.Identity;

namespace Tms.Api.Controllers;

public record LoginRequest(string Email, string Password, Guid? CompanyId);
public record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid TenantId,
    Guid CompanyId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Functions);

public record RefreshRequest(string RefreshToken);
public record RefreshResponse(
    string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>
/// Interactive-user login (docs/architecture.html §11.1) — issues the JWT that
/// TenantContextMiddleware later resolves TenantId/CompanyId from, plus a refresh
/// token so that access token can stay short-lived. There is no public
/// self-registration endpoint here: internal users are provisioned by an admin
/// (§07), so this controller only ever authenticates an existing account.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TmsDbContext _db;
    private readonly JwtTokenService _tokenService;
    private readonly RefreshTokenService _refreshTokens;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        TmsDbContext db,
        JwtTokenService tokenService,
        RefreshTokenService refreshTokens)
    {
        _userManager = userManager;
        _db = db;
        _tokenService = tokenService;
        _refreshTokens = refreshTokens;
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
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

        var (roleNames, functionCodes) = await ResolveRolesAndFunctionsAsync(user.Id, assignment.CompanyId, ct);

        var accessToken = _tokenService.IssueAccessToken(user, assignment.CompanyId, roleNames, functionCodes);

        // A fresh login starts a brand new rotation family (§11.1) — every refresh
        // from here on stays inside it until it's revoked (logout, expiry, or a
        // reuse-detection trip).
        var (refreshPlaintext, refreshRecord) =
            _refreshTokens.Issue(user.TenantId, assignment.CompanyId, user.Id, Guid.NewGuid());

        await _db.SaveChangesAsync(ct);

        return Ok(new LoginResponse(
            accessToken.AccessToken, accessToken.ExpiresAt,
            refreshPlaintext, refreshRecord.ExpiresAt,
            user.TenantId, assignment.CompanyId, roleNames, functionCodes));
    }

    /// <summary>
    /// Exchanges a refresh token for a new access token and a new refresh token
    /// (§11.1) — rotation, not reuse: the presented token is revoked in the same
    /// call. Presenting a token that's already been revoked (i.e. already used, or
    /// already logged out) is treated as possible theft and revokes the entire
    /// rotation family, forcing a fresh login.
    /// </summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<RefreshResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var existing = await _refreshTokens.FindByPlaintextAsync(request.RefreshToken, ct);
        if (existing is null)
            return Unauthorized(new { error = "invalid_grant" });

        if (existing.RevokedAt is not null)
        {
            await _refreshTokens.RevokeFamilyAsync(existing.FamilyId, ct);
            await _db.SaveChangesAsync(ct);
            return Unauthorized(new { error = "invalid_grant", detail = "Refresh token reuse detected; session revoked." });
        }

        if (existing.ExpiresAt < DateTimeOffset.UtcNow)
            return Unauthorized(new { error = "invalid_grant" });

        var user = await _userManager.FindByIdAsync(existing.UserId.ToString());
        if (user is null)
            return Unauthorized(new { error = "invalid_grant" });

        existing.RevokedAt = DateTimeOffset.UtcNow; // this one is now spent — rotation, not reuse

        var (roleNames, functionCodes) = await ResolveRolesAndFunctionsAsync(user.Id, existing.CompanyId, ct);
        var accessToken = _tokenService.IssueAccessToken(user, existing.CompanyId, roleNames, functionCodes);
        var (refreshPlaintext, refreshRecord) =
            _refreshTokens.Issue(existing.TenantId, existing.CompanyId, user.Id, existing.FamilyId);

        await _db.SaveChangesAsync(ct);

        return Ok(new RefreshResponse(
            accessToken.AccessToken, accessToken.ExpiresAt, refreshPlaintext, refreshRecord.ExpiresAt));
    }

    /// <summary>Revokes a refresh token — always 204, whether or not the token was valid, so this can't be used to probe for valid tokens.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        var existing = await _refreshTokens.FindByPlaintextAsync(request.RefreshToken, ct);
        if (existing is not null && existing.RevokedAt is null)
        {
            existing.RevokedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    /// <summary>
    /// OAuth2 client-credentials grant (docs/architecture.html §11.1) — the
    /// system-to-system counterpart to <see cref="Login"/>. Standard RFC 6749 §4.4
    /// shape: form-encoded request, "access_token"/"token_type"/"expires_in" response,
    /// an "error" body on failure — so any off-the-shelf OAuth2 client library can
    /// call this without knowing anything TMS-specific. No refresh token here: a
    /// machine client just requests a new access token with its client secret again.
    /// </summary>
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    [EnableRateLimiting("auth")]
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

    /// <summary>Role → RoleFunction → Function (§07) for one user in one company — shared by Login and Refresh so they can never drift apart.</summary>
    private async Task<(IReadOnlyList<string> RoleNames, IReadOnlyList<string> FunctionCodes)> ResolveRolesAndFunctionsAsync(
        Guid userId, Guid companyId, CancellationToken ct)
    {
        var roleIds = await _db.UserCompanyRoles
            .Where(ucr => ucr.UserId == userId && ucr.CompanyId == companyId)
            .Select(ucr => ucr.RoleId)
            .ToListAsync(ct);

        var roleNames = await _db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name!)
            .ToListAsync(ct);

        var functionCodes = await _db.RoleFunctions
            .Where(rf => roleIds.Contains(rf.RoleId))
            .Join(_db.Functions, rf => rf.FunctionId, f => f.Id, (rf, f) => f.Code)
            .Distinct()
            .ToListAsync(ct);

        return (roleNames, functionCodes);
    }
}
