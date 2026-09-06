using Tms.Shared;

namespace Tms.Modules.Integration;

public enum WebhookDeliveryStatus
{
    Pending,
    Delivered,
    Failed
}

/// <summary>
/// One attempted (or about-to-be-attempted) delivery of one event to one
/// WebhookSubscription (docs/architecture.html §11.3) — the transactional-outbox half of
/// the design: WebhookPublisher.QueueAsync adds these in the same unit of work as the
/// business change that triggered them (so an event is never lost to a crash between
/// "the invoice was issued" and "a delivery row exists for it"), and the actual HTTP
/// attempt happens afterward, never inside that same SaveChanges.
///
/// A Failed row is also picked up automatically by WebhookRetryJob (§11.3), which sweeps
/// every tenant on a schedule and backs off between attempts using AttemptCount/
/// NextAttemptAtUtc below — WebhookDeliveriesController.Retry remains for staff to force
/// an out-of-schedule attempt regardless of NextAttemptAtUtc.
/// </summary>
public class WebhookDelivery : CompanyScopedEntity
{
    public Guid SubscriptionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;
    public DateTimeOffset? AttemptedAtUtc { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ErrorDetail { get; set; }

    /// <summary>How many attempts (manual or automatic) this delivery has had. WebhookRetryJob stops sweeping it up once this reaches BackgroundJobOptions.WebhookMaxAttempts — it still shows as Failed, just no longer auto-retried.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Set after a failed attempt to the next time WebhookRetryJob should retry it (an increasing backoff, not a fixed interval). Null for a row that's never failed, or that's exhausted its attempts.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
}
