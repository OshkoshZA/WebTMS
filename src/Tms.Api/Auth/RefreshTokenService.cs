using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Identity;

namespace Tms.Api.Auth;

/// <summary>
/// Issues and validates refresh tokens (docs/architecture.html §11.1). Callers are
/// responsible for calling SaveChanges — this only builds/queries entities, the same
/// division of responsibility as the rest of the codebase's services.
/// </summary>
public class RefreshTokenService
{
    private const int RefreshTokenDays = 7;

    private readonly TmsDbContext _db;

    public RefreshTokenService(TmsDbContext db)
    {
        _db = db;
    }

    /// <summary>Builds (and stages, via Add) a new refresh token. Pass a fresh Guid as familyId for a new login, or the existing family's id when rotating.</summary>
    public (string PlaintextToken, RefreshToken Record) Issue(Guid tenantId, Guid companyId, Guid userId, Guid familyId)
    {
        var plaintext = GenerateToken();
        var record = new RefreshToken
        {
            TenantId = tenantId,
            CompanyId = companyId,
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(plaintext),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(RefreshTokenDays)
        };
        _db.Set<RefreshToken>().Add(record);
        return (plaintext, record);
    }

    /// <summary>
    /// Looks up a refresh token by its plaintext value. Uses IgnoreQueryFilters —
    /// like the OAuth2 client-credentials lookup (§11.1) — because which tenant a
    /// presented token belongs to is exactly what this call is resolving; there is
    /// no ambient tenant context yet for this request.
    /// </summary>
    public Task<RefreshToken?> FindByPlaintextAsync(string plaintextToken, CancellationToken ct)
    {
        var hash = Hash(plaintextToken);
        return _db.Set<RefreshToken>().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    }

    /// <summary>Revokes every still-active token in a rotation family — the reuse-detection response.</summary>
    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var tokens = await _db.Set<RefreshToken>().IgnoreQueryFilters()
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
            token.RevokedAt = DateTimeOffset.UtcNow;
    }

    private static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string input) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
