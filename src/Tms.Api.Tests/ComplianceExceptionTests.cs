using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// Vehicle/driver compliance expiry wired into the shared Exception mechanism (§16.1)
/// via ComplianceController.ReconcileExceptions — the third of Fig. 13's six sources.
/// Exercises the on-demand path; ComplianceReconcileJobTests covers the scheduled sweep
/// (§11.3), both sharing the same ComplianceReconciliationService underneath.
/// </summary>
[Collection(StaffTestCollection.Name)]
public class ComplianceExceptionTests
{
    private readonly StaffTestFixture _fx;

    public ComplianceExceptionTests(StaffTestFixture fx) => _fx = fx;

    private async Task<Guid> CreateVehicleAsync(string suffix, DateOnly? licenceExpiry = null, DateOnly? vehicleTestExpiry = null)
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/vehicles", new
        {
            fleetNo = $"CMPV-{suffix}",
            registration = $"CMPREG{suffix}",
            type = 0,
            licenceExpiry,
            vehicleTestExpiry
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private async Task<Guid> CreateDriverAsync(string suffix, DateOnly? licenceExpiry = null, DateOnly? pdpExpiry = null)
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/drivers", new
        {
            employeeNo = $"CMPD-{suffix}",
            name = $"Compliance Test Driver {suffix}",
            licenceCode = "C1",
            licenceExpiry,
            pdpExpiry
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private async Task ReconcileAsync() =>
        (await _fx.StaffClient.PostAsync("/api/v1/compliance/reconcile-exceptions", null)).EnsureSuccessStatusCode();

    private async Task<List<ExceptionLike>> GetOpenExceptionsAsync(string entityType, Guid entityId) =>
        (await _fx.StaffClient.GetFromJsonAsync<List<ExceptionLike>>("/api/v1/exceptions?status=0"))!
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .ToList();

    [Fact]
    public async Task An_already_expired_vehicle_licence_raises_a_critical_exception()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var vehicleId = await CreateVehicleAsync(suffix, licenceExpiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5));

        await ReconcileAsync();

        var exceptions = await GetOpenExceptionsAsync("VehicleLicence", vehicleId);
        var exception = Assert.Single(exceptions);
        Assert.Equal("ComplianceExpiry", exception.Category);
        Assert.Equal(2, exception.Severity); // Critical
    }

    [Fact]
    public async Task A_vehicle_test_expiring_within_the_warning_window_raises_a_warning_exception()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var vehicleId = await CreateVehicleAsync(suffix, vehicleTestExpiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10));

        await ReconcileAsync();

        var exceptions = await GetOpenExceptionsAsync("VehicleTest", vehicleId);
        var exception = Assert.Single(exceptions);
        Assert.Equal(1, exception.Severity); // Warning
    }

    [Fact]
    public async Task An_expiry_well_outside_the_warning_window_raises_nothing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var vehicleId = await CreateVehicleAsync(suffix, licenceExpiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60));

        await ReconcileAsync();

        Assert.Empty(await GetOpenExceptionsAsync("VehicleLicence", vehicleId));
    }

    [Fact]
    public async Task Reconciling_twice_never_duplicates_the_same_open_exception()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var vehicleId = await CreateVehicleAsync(suffix, licenceExpiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        await ReconcileAsync();
        await ReconcileAsync();

        Assert.Single(await GetOpenExceptionsAsync("VehicleLicence", vehicleId));
    }

    [Fact]
    public async Task Renewing_the_licence_past_the_warning_window_auto_resolves_the_open_exception()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var vehicleId = await CreateVehicleAsync(suffix, licenceExpiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        await ReconcileAsync();
        Assert.Single(await GetOpenExceptionsAsync("VehicleLicence", vehicleId));

        var updateResponse = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/vehicles/{vehicleId}", new
        {
            fleetNo = $"CMPV-{suffix}",
            registration = $"CMPREG{suffix}",
            type = 0,
            licenceExpiry = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(365),
            vehicleTestExpiry = (DateOnly?)null
        });
        updateResponse.EnsureSuccessStatusCode();

        await ReconcileAsync();

        Assert.Empty(await GetOpenExceptionsAsync("VehicleLicence", vehicleId));
    }

    [Fact]
    public async Task Deactivating_a_vehicle_auto_resolves_its_open_compliance_exception()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var vehicleId = await CreateVehicleAsync(suffix, licenceExpiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        await ReconcileAsync();
        Assert.Single(await GetOpenExceptionsAsync("VehicleLicence", vehicleId));

        (await _fx.StaffClient.PostAsync($"/api/v1/vehicles/{vehicleId}/deactivate", null)).EnsureSuccessStatusCode();
        await ReconcileAsync();

        Assert.Empty(await GetOpenExceptionsAsync("VehicleLicence", vehicleId));
    }

    [Fact]
    public async Task A_drivers_expired_pdp_raises_its_own_exception_independent_of_licence()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var driverId = await CreateDriverAsync(suffix, pdpExpiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3));

        await ReconcileAsync();

        Assert.Single(await GetOpenExceptionsAsync("DriverPdp", driverId));
        Assert.Empty(await GetOpenExceptionsAsync("DriverLicence", driverId)); // licence wasn't set — independent concern
    }

    [Fact]
    public async Task Reconcile_succeeds_for_staff()
    {
        // The portal-Forbidden half of this check lives in
        // MasterDataAndAdminBoundaryTests (PortalTestFixture holds the portal tokens
        // this fixture doesn't) — this just confirms the staff-allowed side.
        var response = await _fx.StaffClient.PostAsync("/api/v1/compliance/reconcile-exceptions", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed record IdDto(Guid Id);
    private sealed record ExceptionLike(Guid Id, string Category, int Severity, string EntityType, Guid EntityId, int Status);
}
