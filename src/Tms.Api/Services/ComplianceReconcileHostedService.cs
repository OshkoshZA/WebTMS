namespace Tms.Api.Services;

/// <summary>
/// The generic scheduling half of the §16.1 compliance-expiry sweep — mirrors
/// WebhookRetryHostedService exactly (a plain BackgroundService/PeriodicTimer around a
/// job class with its own callable RunOnceAsync). Registered only when
/// BackgroundJobs:Enabled is true (Program.cs); Tms.Api.Tests sets that false
/// (TestApiFactory) so no test run races a scheduled tick against rows a test is
/// asserting on.
/// </summary>
public class ComplianceReconcileHostedService : BackgroundService
{
    private readonly ComplianceReconcileJob _job;
    private readonly TimeSpan _interval;
    private readonly ILogger<ComplianceReconcileHostedService> _logger;

    public ComplianceReconcileHostedService(ComplianceReconcileJob job, IConfiguration configuration, ILogger<ComplianceReconcileHostedService> logger)
    {
        _job = job;
        _interval = TimeSpan.FromHours(configuration.GetValue("BackgroundJobs:ComplianceReconcileIntervalHours", 24));
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
                _logger.LogError(ex, "Compliance reconcile job tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
