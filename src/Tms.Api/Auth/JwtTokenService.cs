using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Tms.Modules.Identity;

namespace Tms.Api.Auth;

public record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues the JWT that carries the TenantId/CompanyId claims TenantContextMiddleware
/// later reads (docs/architecture.html §4.1, §11.1). Access tokens are deliberately
/// short-lived; refresh-token issuance/rotation is a follow-up, not built here yet.
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
        var jwtSection = _configuration.GetSection("Jwt");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"] ?? string.Empty));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

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

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
