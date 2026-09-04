using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Integration;

namespace Tms.Api.Services;

/// <summary>
/// The write side of §11.3's outbound events — every place elsewhere in this design that
/// triggers a documented event calls QueueAsync instead of talking to WebhookDelivery
/// directly. Unlike ExceptionService.Raise, this one does need to query (which
/// subscriptions actually want this event), so it's async — but it still never calls
/// SaveChangesAsync itself: the caller's own SaveChangesAsync persists the queued
/// deliveries in the same unit of work as the business change that triggered them, the
/// transactional-outbox half of the design (see WebhookDelivery's own doc comment).
/// </summary>
public class WebhookPublisher
{
    private readonly TmsDbContext _db;

    public WebhookPublisher(TmsDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns the ids of the WebhookDelivery rows just queued (empty if no Company has an Active subscription for this event) — the caller SaveChanges's them, then hands the same ids to WebhookDeliveryService to actually attempt.</summary>
    public async Task<IReadOnlyList<Guid>> QueueAsync(
        Guid tenantId, Guid companyId, string eventType, string entityType, Guid entityId, CancellationToken ct)
    {
        var subscriptions = await _db.WebhookSubscriptions
            .Where(s => s.CompanyId == companyId && s.EventType == eventType && s.Status == WebhookSubscriptionStatus.Active)
            .ToListAsync(ct);

        var ids = new List<Guid>(subscriptions.Count);
        var occurredAtUtc = DateTimeOffset.UtcNow;
        foreach (var subscription in subscriptions)
        {
            var delivery = new WebhookDelivery
            {
                TenantId = tenantId,
                CompanyId = companyId,
                SubscriptionId = subscription.Id,
                EventType = eventType,
                EntityType = entityType,
                EntityId = entityId.ToString(),
                OccurredAtUtc = occurredAtUtc
            };
            _db.WebhookDeliveries.Add(delivery);
            ids.Add(delivery.Id);
        }

        return ids;
    }
}
