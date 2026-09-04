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
/// There is deliberately no background retry worker (§19 Phase 3 gap, same reasoning as
/// §4.3's deferred FX auto-refresh) — a Failed row sits here for staff to replay via
/// WebhookDeliveriesController.Retry.
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
}
