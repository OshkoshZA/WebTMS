using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Integration;

namespace Tms.Api.Services;

/// <summary>
/// The read/attempt side of §11.3's outbound events — takes WebhookDelivery rows
/// WebhookPublisher already queued and persisted, and makes one synchronous, signed HTTP
/// attempt at each. DeliverAsync is called inline, right after the triggering request's
/// own SaveChangesAsync commits; WebhookRetryJob calls RetryAsync again later on a
/// schedule for anything that came back Failed, and WebhookDeliveriesController.Retry
/// lets staff force an extra attempt outside that schedule.
/// </summary>
public class WebhookDeliveryService
{
    /// <summary>Backoff between automatic retry attempts, indexed by (AttemptCount - 1) and clamped to the last entry once AttemptCount exceeds its length — WebhookRetryJob stops sweeping a delivery up at all once AttemptCount reaches this array's length (see BackgroundJobOptions.WebhookMaxAttempts).</summary>
    public static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(12)
    ];

    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly TmsDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public WebhookDeliveryService(TmsDbContext db, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Attempts every given (still-Pending, typically) delivery and persists the outcome. Safe to call with an empty list. Never throws on an individual delivery's own failure — that failure IS the recorded outcome.</summary>
    public async Task DeliverAsync(IReadOnlyCollection<Guid> deliveryIds, CancellationToken ct)
    {
        if (deliveryIds.Count == 0) return;

        var deliveries = await _db.WebhookDeliveries.Where(d => deliveryIds.Contains(d.Id)).ToListAsync(ct);
        var subscriptionIds = deliveries.Select(d => d.SubscriptionId).Distinct().ToList();
        var subscriptions = await _db.WebhookSubscriptions
            .Where(s => subscriptionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        foreach (var delivery in deliveries)
        {
            if (!subscriptions.TryGetValue(delivery.SubscriptionId, out var subscription))
                continue; // the subscription was removed between Queue and Deliver — nothing left to send to.

            await AttemptAsync(delivery, subscription, ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>The manual-retry path (WebhookDeliveriesController.Retry) — re-attempts one delivery regardless of its current Status, since a Delivered row is never re-sent by anything else and retrying one is an explicit, deliberate staff action.</summary>
    public async Task<bool> RetryAsync(Guid deliveryId, CancellationToken ct)
    {
        var delivery = await _db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery is null) return false;

        var subscription = await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == delivery.SubscriptionId, ct);
        if (subscription is null) return false;

        await AttemptAsync(delivery, subscription, ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task AttemptAsync(WebhookDelivery delivery, WebhookSubscription subscription, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            EventType = delivery.EventType,
            EntityType = delivery.EntityType,
            EntityId = delivery.EntityId,
            CompanyId = delivery.CompanyId,
            OccurredAtUtc = delivery.OccurredAtUtc
        }, PayloadOptions);
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(subscription.Secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        delivery.AttemptedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.CallbackUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Tms-Signature", $"sha256={signature}");

            var client = _httpClientFactory.CreateClient("webhooks");
            using var response = await client.SendAsync(request, ct);

            delivery.ResponseStatusCode = (int)response.StatusCode;
            delivery.Status = response.IsSuccessStatusCode ? WebhookDeliveryStatus.Delivered : WebhookDeliveryStatus.Failed;
            delivery.ErrorDetail = response.IsSuccessStatusCode ? null : $"Callback responded {(int)response.StatusCode} {response.StatusCode}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            delivery.ResponseStatusCode = null;
            delivery.ErrorDetail = ex.Message;
        }

        if (delivery.Status == WebhookDeliveryStatus.Delivered)
        {
            delivery.AttemptCount = 0;
            delivery.NextAttemptAtUtc = null;
        }
        else
        {
            delivery.AttemptCount++;
            var backoff = RetryBackoff[Math.Min(delivery.AttemptCount, RetryBackoff.Length) - 1];
            delivery.NextAttemptAtUtc = DateTimeOffset.UtcNow + backoff;
        }
    }
}
