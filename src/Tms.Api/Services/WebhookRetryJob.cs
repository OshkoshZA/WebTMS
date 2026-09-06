using Microsoft.EntityFrameworkCore;
using Tms.Api.Auth;
using Tms.Infrastructure;
using Tms.Modules.Integration;

namespace Tms.Api.Services;

/// <summary>
/// The scheduled half of §11.3's retry story — WebhookRetryHostedService ticks this on an
/// interval, and each run sweeps every tenant's due Failed deliveries in one pass. There's
/// no single caller's JWT to resolve a tenant from here (nothing triggered this run), so it
/// uses the same "operates across every tenant, not on behalf of one" bypass
/// TmsDbContextFactory already uses for EF migrations: setting HttpTenantContext.
/// IsPlatformSupport on its own scope, rather than inventing a new mechanism.
/// </summary>
public class WebhookRetryJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookRetryJob> _logger;

    public WebhookRetryJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<WebhookRetryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var maxAttempts = _configuration.GetValue("BackgroundJobs:WebhookMaxAttempts", WebhookDeliveryService.RetryBackoff.Length);

        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpTenantContext>().IsPlatformSupport = true;

        var db = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var deliveryService = scope.ServiceProvider.GetRequiredService<WebhookDeliveryService>();

        var now = DateTimeOffset.UtcNow;
        var dueIds = await db.WebhookDeliveries
            .Where(d => d.Status == WebhookDeliveryStatus.Failed
                && d.AttemptCount < maxAttempts
                && d.NextAttemptAtUtc != null && d.NextAttemptAtUtc <= now)
            .Select(d => d.Id)
            .ToListAsync(ct);

        foreach (var id in dueIds)
        {
            try
            {
                await deliveryService.RetryAsync(id, ct);
            }
            catch (Exception ex)
            {
                // One row's failure (e.g. a transient DB hiccup on its own SaveChanges)
                // shouldn't stop the rest of this sweep — it's still Failed and due, so
                // the next tick picks it back up regardless.
                _logger.LogError(ex, "Webhook retry job failed to retry delivery {DeliveryId}", id);
            }
        }

        if (dueIds.Count > 0)
            _logger.LogInformation("Webhook retry job retried {Count} due delivery(ies)", dueIds.Count);
    }
}
