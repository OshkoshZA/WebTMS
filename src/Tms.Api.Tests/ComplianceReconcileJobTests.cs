using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tms.Api.Services;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// ComplianceReconcileJob (§16.1/§11.3) — the scheduled sweep ComplianceReconcileHostedService
/// otherwise ticks on a timer (disabled for this whole test run: TestApiFactory sets
/// BackgroundJobs:Enabled false), exercised here by resolving the job straight from the
/// app's own DI container and calling RunOnceAsync directly against the real database,
/// the same approach as WebhookRetryJobTests. Per-entity reconciliation logic itself
/// (expiry windows, auto-resolve on renewal/deactivation, no duplicate raises) is already
/// covered by ComplianceExceptionTests against the on-demand endpoint — both paths now
/// share the exact same ComplianceReconciliationService, so this only needs to prove the
/// job's own part: discovering the right company and reconciling it correctly from a
/// scope that starts with no ambient tenant identity of its own (nothing populates one
/// outside a real HTTP request), the same proof WebhookRetryJobTests gives for its own
/// IsPlatformSupport bypass.
/// </summary>
[Collection(StaffTestCollection.Name)]
public class ComplianceReconcileJobTests
{
    private readonly StaffTestFixture _fx;

    public ComplianceReconcileJobTests(StaffTestFixture fx) => _fx = fx;

    private async Task<Guid> CreateVehicleAsync(string suffix, DateOnly licenceExpiry)
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/vehicles", new
        {
            fleetNo = $"CMPJOBV-{suffix}",
            registration = $"CMPJOBREG{suffix}",
            type = 0,
            licenceExpiry
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private async Task<List<ExceptionLike>> GetOpenExceptionsAsync(string entityType, Guid entityId) =>
        (await _fx.StaffClient.GetFromJsonAsync<List<ExceptionLike>>("/api/v1/exceptions?status=0"))!
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .ToList();

    [Fact]
    public async Task A_scheduled_sweep_raises_an_exception_for_the_fixtures_own_company()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var vehicleId = await CreateVehicleAsync(suffix, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2));

        var job = _fx.Services.GetRequiredService<ComplianceReconcileJob>();
        await job.RunOnceAsync(CancellationToken.None);

        var exception = Assert.Single(await GetOpenExceptionsAsync("VehicleLicence", vehicleId));
        Assert.Equal(2, exception.Severity); // Critical
    }

    private sealed record IdDto(Guid Id);
    private sealed record ExceptionLike(Guid Id, string Category, int Severity, string EntityType, Guid EntityId, int Status);
}
