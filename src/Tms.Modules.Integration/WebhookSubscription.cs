using Tms.Shared;

namespace Tms.Modules.Integration;

public enum WebhookSubscriptionStatus
{
    Active,
    Disabled
}

/// <summary>
/// A partner's registered callback for one event type (docs/architecture.html §11.2/
/// §11.3). Secret is a server-generated 256-bit value, shown once in the Create response
/// and used to HMAC-SHA256-sign every delivery so the partner can verify a payload
/// actually came from this platform. No CallbackUrl edit — an owner who needs a new URL
/// registers a fresh subscription and disables the old one, the same "never mutate the
/// thing a signature was issued for" reasoning as an ApiClient's own secret.
/// </summary>
public class WebhookSubscription : CompanyScopedEntity
{
    public string EventType { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public WebhookSubscriptionStatus Status { get; set; } = WebhookSubscriptionStatus.Active;
}
