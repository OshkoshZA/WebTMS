using Tms.Shared;

namespace Tms.Modules.Identity;

public enum ApiClientStatus
{
    Active,
    Revoked
}

/// <summary>
/// A system-to-system integration partner (docs/architecture.html §11.1, Fig. 2) —
/// authenticates via OAuth2 client-credentials, never interactively. ClientId is the
/// public identifier sent in the token request; the actual secret never lives here,
/// only its hash, in one-or-more <see cref="ApiClientSecret"/> rows (so a secret can
/// be rotated with an overlap window rather than an instant cutover).
/// </summary>
public class ApiClient : CompanyScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public ApiClientStatus Status { get; set; } = ApiClientStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Per-client rate limit (docs/architecture.html §11.1 — "per-client rate
    /// limits"). Embedded as a claim in every access token this client is issued
    /// (§11.1), so the rate limiter can partition and size the limit per caller
    /// without a database round-trip on every request.
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 60;
}

/// <summary>One hashed secret for an ApiClient. Never store the plaintext value — it's shown to the caller exactly once, at issuance.</summary>
public class ApiClientSecret : CompanyScopedEntity
{
    public Guid ApiClientId { get; set; }
    public string SecretHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// An ApiClient's role assignment — the machine-client equivalent of UserCompanyRole
/// (§07). Functions are resolved the same way for both: Role → RoleFunction → Function.
/// </summary>
public class ApiClientRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApiClientId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RoleId { get; set; }
}
