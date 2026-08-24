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
            new("company_id", companyId.ToString())
        };
        claims.AddRange(roleNames.Select(r => new Claim(ClaimTypes.Role, r)));
        // One claim per granted function (§07) — FunctionAuthorizationHandler checks these,
        // not role names, so a role can be renamed or restructured without touching any
        // [Authorize(Policy = "...")] attribute anywhere in the API.
        claims.AddRange(functionCodes.Distinct().Select(f => new Claim("function", f)));

        return BuildToken(claims);
    }

    /// <summary>
    /// OAuth2 client-credentials grant (§11.1) — no human user or role names, just
    /// the calling ApiClient's identity and whatever functions its ApiClientRole
    /// resolves to. TenantContextMiddleware reads "client_id" the same way it reads
    /// a user's NameIdentifier, into ICurrentUserAccessor.ApiClientId — so a write
    /// made under this token is attributed in the audit trail (§12) to the
    /// integration partner, not to a (non-existent) user.
    /// </summary>
    public IssuedToken IssueClientCredentialsToken(ApiClient client, IEnumerable<string> functionCodes)
    {
        var claims = new List<Claim>
        {
            new("client_id", client.ClientId),
            new("tenant_id", client.TenantId.ToString()),
            new("company_id", client.CompanyId.ToString())
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
