namespace Tms.Api.Services;

/// <summary>
/// The generic scheduling half of the §11.3 retry story — everything tenant/delivery-
/// specific lives in WebhookRetryJob so tests can call RunOnceAsync directly against a
/// real host without waiting on a timer. Registered only when BackgroundJobs:Enabled is
/// true (Program.cs); Tms.Api.Tests sets that false (TestApiFactory) so no test run ever
/// races this against the same rows a test is asserting on.
/// </summary>
public class WebhookRetryHostedService : BackgroundService
{
    private readonly WebhookRetryJob _job;
    private readonly TimeSpan _interval;
    private readonly ILogger<WebhookRetryHostedService> _logger;

    public WebhookRetryHostedService(WebhookRetryJob job, IConfiguration configuration, ILogger<WebhookRetryHostedService> logger)
    {
        _job = job;
        _interval = TimeSpan.FromSeconds(configuration.GetValue("BackgroundJobs:WebhookRetryIntervalSeconds", 60));
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                await _job.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // A tick failing outright (as opposed to one delivery failing inside
                // RunOnceAsync, which it already handles) must not kill this loop —
                // the next tick tries again.
                _logger.LogError(ex, "Webhook retry job tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
