using Tms.Shared;

namespace Tms.Modules.Identity;

/// <summary>
/// A refresh token for the interactive-user login flow (docs/architecture.html
/// §11.1 — "short-lived with refresh tokens"). Only the SHA-256 hash of the
/// plaintext token is ever stored — it's high-entropy random data, not a
/// low-entropy password, so a fast deterministic hash is the right tool here
/// (it also lets the token be looked up directly by hash, which a salted
/// password hash deliberately does not allow).
///
/// FamilyId is shared across every rotation of one continuous refresh chain,
/// starting at login. Refreshing revokes the presented token and issues a new
/// one in the same family; presenting an already-revoked token again is treated
/// as reuse — a possible theft signal — and revokes the whole family, forcing
/// re-login (OAuth2 Security BCP's refresh token rotation/reuse-detection).
/// The client-credentials grant (§11.1) never issues one of these — a machine
/// client just requests a fresh access token with its client secret again.
/// </summary>
public class RefreshToken : CompanyScopedEntity
{
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}
