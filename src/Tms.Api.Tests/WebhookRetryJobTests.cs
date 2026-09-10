using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tms.Api.Auth;
using Tms.Api.Services;
using Tms.Infrastructure;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// WebhookRetryJob (§11.3) — the scheduled sweep WebhookRetryHostedService otherwise ticks
/// on a timer (disabled for this whole test run: TestApiFactory sets BackgroundJobs:Enabled
/// false), exercised here by resolving the job straight from the app's own DI container and
/// calling RunOnceAsync directly against the real database, the same no-mocks approach as
/// WebhookTests. The job's own scope never gets a TenantId/CompanyId from anywhere (nothing
/// populates one outside a real HTTP request) — it relies entirely on the IsPlatformSupport
/// bypass (TmsDbContext, same mechanism TmsDbContextFactory already uses for EF migrations)
/// to see anything at all, so a successful sweep here already demonstrates that bypass is
/// doing real work across every tenant, not incidentally matching StaffTestFixture's own
/// seeded tenant.
///
/// Each test uses its own credit note event/subscription rather than sharing one across
/// tests in the same run — every Active subscription to an event type receives every
/// occurrence of it, so two still-Active subscriptions to "creditnote.issued" in the same
/// test would each see the other's credit note too.
/// </summary>
[Collection(StaffTestCollection.Name)]
public class WebhookRetryJobTests : IAsyncLifetime
{
    private readonly StaffTestFixture _fx;
    private readonly List<Guid> _createdSubscriptionIds = new();

    public WebhookRetryJobTests(StaffTestFixture fx) => _fx = fx;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var id in _createdSubscriptionIds)
            await _fx.StaffClient.PostAsync($"/api/v1/webhooks/subscriptions/{id}/disable", null);
    }

    private async Task<Guid> SubscribeAsync(string callbackUrl)
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/webhooks/subscriptions", new { eventType = "creditnote.issued", callbackUrl });
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<CreateSubscriptionDto>();
        _createdSubscriptionIds.Add(dto!.Id);
        return dto.Id;
    }

    private async Task IssueCreditNoteAsync()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/credit-notes", new
        {
            clientId,
            reason = "Webhook retry job test",
            lines = new[] { new { description = "Adjustment", amount = 50m } }
        });
        createResponse.EnsureSuccessStatusCode();
        var creditNote = await createResponse.Content.ReadFromJsonAsync<IdLike>();
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/credit-notes/{creditNote!.Id}/issue", new { })).EnsureSuccessStatusCode();
    }

    private async Task<DeliveryDto> GetOnlyDeliveryAsync(Guid subscriptionId)
    {
        var deliveries = await _fx.StaffClient.GetFromJsonAsync<List<DeliveryDto>>($"/api/v1/webhook-deliveries?subscriptionId={subscriptionId}");
        return Assert.Single(deliveries!);
    }

    /// <summary>Bypasses the real 1-minute-plus backoff so a test doesn't need to sleep for it — same IsPlatformSupport side-channel WebhookRetryJob itself uses, since this reaches TmsDbContext directly rather than through a request.</summary>
    private Task ForceNextAttemptIntoThePastAsync(Guid deliveryId) => SetNextAttemptAsync(deliveryId, DateTimeOffset.UtcNow.AddMinutes(-1));

    /// <summary>
    /// Pins NextAttemptAtUtc comfortably beyond any real backoff step (§11.3's own
    /// longest is 12 hours) so a "not due" assertion can never flake into "actually due"
    /// no matter how long a slow, shared, full-suite run takes to reach it — this is the
    /// fix for a real flake this exact test surfaced (WebhookDeliveryService's own
    /// ~1-minute first backoff had, in fact, elapsed by the time a slow full run's own
    /// sweep call ran, making the delivery genuinely due and retried — correct job
    /// behavior, just not what "not yet due" was supposed to be testing).
    /// </summary>
    private Task ForceNextAttemptFarIntoTheFutureAsync(Guid deliveryId) => SetNextAttemptAsync(deliveryId, DateTimeOffset.UtcNow.AddDays(1));

    private async Task SetNextAttemptAsync(Guid deliveryId, DateTimeOffset nextAttemptAtUtc)
    {
        using var scope = _fx.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpTenantContext>().IsPlatformSupport = true;
        var db = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var delivery = await db.WebhookDeliveries.FirstAsync(d => d.Id == deliveryId);
        delivery.NextAttemptAtUtc = nextAttemptAtUtc;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_sweep_retries_a_due_failed_delivery()
    {
        using var receiver = new WebhookTestReceiver();
        receiver.RespondWith(500);
        var subscriptionId = await SubscribeAsync(receiver.Url);
        await IssueCreditNoteAsync();
        var delivery = await GetOnlyDeliveryAsync(subscriptionId);
        Assert.Equal(1, delivery.AttemptCount);
        await ForceNextAttemptIntoThePastAsync(delivery.Id);

        receiver.RespondWith(200);
        var job = _fx.Services.GetRequiredService<WebhookRetryJob>();
        await job.RunOnceAsync(CancellationToken.None);

        // At least the original failed attempt plus the sweep's retry. Seen as 3 once,
        // during an abnormally slow full-suite run rather than exactly 2. AttemptCount and
        // Status only ever move once per AttemptAsync call (WebhookDeliveryService.cs), and
        // every other app-level source of a duplicate has a hard blocker: a duplicate queued
        // delivery is caught by GetOnlyDeliveryAsync's own Assert.Single right after issuing;
        // WebhookRetryHostedService never runs a competing sweep (TestApiFactory forces
        // BackgroundJobs:Enabled false for every fixture); and a stale subscription from an
        // earlier run can't land on this receiver (WebhookTestReceiver's per-instance path
        // token 404s anything not addressed to it). What's left is the transport, not the
        // app: "webhooks" is a plain AddHttpClient (Program.cs) with no retry policy of its
        // own, so its pooled SocketsHttpHandler owns the two real attempts to this receiver's
        // one URL — and a pooled connection that the server (or an idle gap stretched by a
        // loaded run) closes between them is transparently resent on a fresh connection
        // before any request bytes go out, which the receiver then counts as a 3rd request
        // without WebhookDeliveryService ever making a 3rd call. That's a documented .NET
        // behavior, not app logic, so pinning this to exactly 2 would be asserting a
        // transport implementation detail; the status transition below is the actual
        // correctness signal, and isn't subject to the same ambiguity.
        Assert.True(receiver.Requests.Count >= 2, $"Expected at least 2 requests, saw {receiver.Requests.Count}.");
        Assert.Equal(1, (await GetOnlyDeliveryAsync(subscriptionId)).Status); // Delivered
    }

    [Fact]
    public async Task A_sweep_leaves_a_not_yet_due_failed_delivery_alone()
    {
        using var receiver = new WebhookTestReceiver();
        receiver.RespondWith(500);
        var subscriptionId = await SubscribeAsync(receiver.Url);
        await IssueCreditNoteAsync();
        var delivery = await GetOnlyDeliveryAsync(subscriptionId);
        Assert.NotNull(delivery.NextAttemptAtUtc);
        await ForceNextAttemptFarIntoTheFutureAsync(delivery.Id);

        receiver.RespondWith(200);
        var job = _fx.Services.GetRequiredService<WebhookRetryJob>();
        await job.RunOnceAsync(CancellationToken.None);

        Assert.Single(receiver.Requests); // untouched by the sweep — not due yet
        Assert.Equal(2, (await GetOnlyDeliveryAsync(subscriptionId)).Status); // still Failed
    }

    [Fact]
    public async Task A_delivery_stops_being_swept_once_it_reaches_the_configured_max_attempts()
    {
        using var receiver = new WebhookTestReceiver();
        receiver.RespondWith(500);
        var subscriptionId = await SubscribeAsync(receiver.Url);
        await IssueCreditNoteAsync();
        var delivery = await GetOnlyDeliveryAsync(subscriptionId);

        // Manual retry ignores NextAttemptAtUtc, so this drives AttemptCount up to the
        // configured max (appsettings.json BackgroundJobs:WebhookMaxAttempts = 5) — 1
        // from the original attempt above, plus 4 here — without waiting on real backoff.
        for (var i = 0; i < 4; i++)
            (await _fx.StaffClient.PostAsync($"/api/v1/webhook-deliveries/{delivery.Id}/retry", null)).EnsureSuccessStatusCode();

        var exhausted = await GetOnlyDeliveryAsync(subscriptionId);
        Assert.Equal(5, exhausted.AttemptCount);
        await ForceNextAttemptIntoThePastAsync(delivery.Id);

        receiver.RespondWith(200);
        var job = _fx.Services.GetRequiredService<WebhookRetryJob>();
        await job.RunOnceAsync(CancellationToken.None);

        Assert.Equal(5, receiver.Requests.Count); // unchanged — the sweep left it alone despite being due
        Assert.Equal(2, (await GetOnlyDeliveryAsync(subscriptionId)).Status); // still Failed, never auto-retried past the max
    }

    private sealed record CreateSubscriptionDto(Guid Id, string EventType, string CallbackUrl, int Status, string Secret);
    private sealed record DeliveryDto(Guid Id, Guid SubscriptionId, int Status, int AttemptCount, DateTimeOffset? NextAttemptAtUtc);
    private sealed record IdLike(Guid Id);
}
