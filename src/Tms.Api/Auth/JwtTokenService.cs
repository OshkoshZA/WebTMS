using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Tms.Modules.Identity;

namespace Tms.Api.Auth;

public record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues the JWT that carries the TenantId/CompanyId/function claims
/// TenantContextMiddleware and FunctionAuthorizationHandler read (docs/architecture.html
/// §4.1, §07, §11.1) — for an interactive user login, and for the OAuth2
/// client-credentials grant an integration partner uses instead. Both paths produce
/// the same kind of bearer token; only which claims are present differs. Access
/// tokens are deliberately short-lived; refresh-token issuance/rotation is a
/// follow-up, not built here yet.
/// </summary>
public class JwtTokenService
{
    // Interactive users don't have a per-account configured limit (only ApiClients
    // do, §11.1) — this is a single shared baseline, generous relative to a typical
    // machine client's default, since a human driving the UI naturally makes many
    // more small requests than one integration batch job does.
    private const int DefaultUserRateLimitPerMinute = 300;

    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IssuedToken IssueAccessToken(
        ApplicationUser user,
        Guid companyId,
        IEnumerable<string> roleNames,
        IEnumerable<string> functionCodes)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("tenant_id", user.TenantId.ToString()),
            new("company_id", companyId.ToString()),
            // Configurable so Tms.Api.Tests can raise it — the suite now runs 100+ tests
            // through one shared, pre-authenticated staff session and comfortably clears
            // 300 requests within a single fixed window, a volume no real interactive
            // user producing a naturally-paced click stream ever would.
            new("rate_limit", _configuration.GetValue("RateLimiting:DefaultUserPermitLimit", DefaultUserRateLimitPerMinute).ToString())
        };
        claims.AddRange(roleNames.Select(r => new Claim(ClaimTypes.Role, r)));
        // One claim per granted function (§07) — FunctionAuthorizationHandler checks these,
        // not role names, so a role can be renamed or restructured without touching any
        // [Authorize(Policy = "...")] attribute anywhere in the API.
        claims.AddRange(functionCodes.Distinct().Select(f => new Claim("function", f)));

        // A Supplier or Customer Portal contact (§13.1) — the row-level scoping claim
        // TenantContextMiddleware reads into ITenantContext.SubcontractorId/ClientId.
        if (user.SubcontractorId is Guid subcontractorId)
            claims.Add(new Claim("subcontractor_id", subcontractorId.ToString()));
        if (user.ClientId is Guid clientId)
            claims.Add(new Claim("portal_client_id", clientId.ToString()));

        return BuildToken(claims);
    }

    /// <summary>
    /// OAuth2 client-credentials grant (§11.1) — no human user or role names, just
    /// the calling ApiClient's identity and whatever functions its ApiClientRole
    /// resolves to. TenantContextMiddleware reads "client_id" the same way it reads
    /// a user's NameIdentifier, into ICurrentUserAccessor.ApiClientId — so a write
    /// made under this token is attributed in the audit trail (§12) to the
    /// integration partner, not to a (non-existent) user. The client's own
    /// RateLimitPerMinute travels with it as a claim too, so the rate limiter
    /// (Program.cs) never needs a database round-trip to size the limit.
    /// </summary>
    public IssuedToken IssueClientCredentialsToken(ApiClient client, IEnumerable<string> functionCodes)
    {
        var claims = new List<Claim>
        {
            new("client_id", client.ClientId),
            new("tenant_id", client.TenantId.ToString()),
            new("company_id", client.CompanyId.ToString()),
            new("rate_limit", client.RateLimitPerMinute.ToString())
        };
        claims.AddRange(functionCodes.Distinct().Select(f => new Claim("function", f)));

        return BuildToken(claims);
    }

    private IssuedToken BuildToken(List<Claim> claims)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"] ?? string.Empty));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
