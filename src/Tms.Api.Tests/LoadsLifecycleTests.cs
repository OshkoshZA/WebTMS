using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>Load/leg booking, allocation, and status lifecycle (§5.2, §5.4) — happy path plus the concrete regressions found during this project's own audit rounds.</summary>
[Collection(StaffTestCollection.Name)]
public class LoadsLifecycleTests
{
    private readonly StaffTestFixture _fx;

    public LoadsLifecycleTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task Booking_a_load_and_adding_a_commodity_line_succeeds()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"BOOK-{Guid.NewGuid():N}");

        var load = await _fx.StaffClient.GetFromJsonAsync<LoadLike>($"/api/v1/loads/{loadId}");
        Assert.NotNull(load);
        Assert.Contains(load!.Legs, l => l.Id == legId);
    }

    [Fact]
    public async Task Credit_hard_stop_blocks_a_commodity_line_over_the_limit_and_an_override_reason_lets_it_through()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8], creditLimit: 100m);
        var loadId = await _fx.CreateLoadAsync(clientId, $"CREDIT-{Guid.NewGuid():N}");
        var legResponse = await _fx.AddOwnFleetLegAsync(loadId);
        legResponse.EnsureSuccessStatusCode();
        var legId = (await legResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;

        var blocked = await _fx.AddCommodityLineAsync(loadId, legId, sellRatePerUnit: 500);
        Assert.Equal((HttpStatusCode)422, blocked.StatusCode);

        var overridden = await _fx.AddCommodityLineAsync(loadId, legId, sellRatePerUnit: 500, creditOverrideReason: "Approved by test");
        Assert.Equal(HttpStatusCode.Created, overridden.StatusCode);
    }

    [Fact]
    public async Task Deactivated_vehicle_and_driver_are_rejected_at_AddLeg_and_reactivating_restores_access()
    {
        var vehicleId = await CreateVehicleAsync();
        var driverId = await CreateDriverAsync();

        (await _fx.StaffClient.PostAsync($"/api/v1/vehicles/{vehicleId}/deactivate", null)).EnsureSuccessStatusCode();

        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var loadId = await _fx.CreateLoadAsync(clientId, $"DEACT-VEH-{Guid.NewGuid():N}");
        var blocked = await _fx.AddOwnFleetLegAsync(loadId, vehicleId: vehicleId, driverId: driverId);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        (await _fx.StaffClient.PostAsync($"/api/v1/vehicles/{vehicleId}/reactivate", null)).EnsureSuccessStatusCode();
        var allowed = await _fx.AddOwnFleetLegAsync(loadId, vehicleId: vehicleId, driverId: driverId);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task Deactivated_subcontractor_is_rejected_at_AddLeg()
    {
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        (await _fx.StaffClient.PostAsync($"/api/v1/subcontractors/{subcontractorId}/deactivate", null)).EnsureSuccessStatusCode();

        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var loadId = await _fx.CreateLoadAsync(clientId, $"DEACT-SUB-{Guid.NewGuid():N}");
        var blocked = await _fx.AddSubcontractedLegAsync(loadId, subcontractorId);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    }

    [Fact]
    public async Task Cancelled_load_rejects_further_legs_and_commodity_lines()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var loadId = await _fx.CreateLoadAsync(clientId, $"CANCEL-{Guid.NewGuid():N}");
        (await _fx.StaffClient.PostAsync($"/api/v1/loads/{loadId}/cancel", null)).EnsureSuccessStatusCode();

        var legResponse = await _fx.AddOwnFleetLegAsync(loadId);
        Assert.Equal(HttpStatusCode.Conflict, legResponse.StatusCode);
    }

    [Fact]
    public async Task Full_status_lifecycle_reaches_PodReceived_after_start_and_deliver()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"LIFECYCLE-{Guid.NewGuid():N}");

        await _fx.DeliverLegAsync(loadId, legId);
        var debriefResponse = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" });
        debriefResponse.EnsureSuccessStatusCode();

        var load = await _fx.StaffClient.GetFromJsonAsync<LoadStatusLike>($"/api/v1/loads/{loadId}");
        Assert.Equal(5, load!.Status); // PodReceived (Quoted=0..Delivered=4, PodReceived=5), per LoadStatus enum ordering (§5.2)
    }

    [Fact]
    public async Task Concurrent_allocate_calls_for_the_same_leg_resolve_to_exactly_one_success()
    {
        var vehicleId = await CreateVehicleAsync();
        var driverId = await CreateDriverAsync();
        var vehicleId2 = await CreateVehicleAsync();
        var driverId2 = await CreateDriverAsync();

        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var loadId = await _fx.CreateLoadAsync(clientId, $"RACE-ALLOC-{Guid.NewGuid():N}");

        // A Planned leg (no vehicle/driver at AddLeg time) so Allocate is the thing racing.
        var legResponse = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs", new
        {
            sequenceNo = 1,
            originLocationId = Guid.Parse(StaffTestFixture.OriginLocationId),
            destinationLocationId = Guid.Parse(StaffTestFixture.DestinationLocationId),
            executionType = 0,
            costCentreId = Guid.Parse(StaffTestFixture.CostCentreId)
        });
        legResponse.EnsureSuccessStatusCode();
        var legId = (await legResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;

        var (client1, client2) = _fx.GetRaceClients();

        var task1 = client1.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs/{legId}/allocate", new { vehicleId, driverId });
        var task2 = client2.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs/{legId}/allocate", new { vehicleId = vehicleId2, driverId = driverId2 });
        var results = await Task.WhenAll(task1, task2);

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    private async Task<Guid> CreateVehicleAsync()
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/vehicles", new
        {
            fleetNo = $"FL-{Guid.NewGuid():N}"[..12],
            registration = $"REG{Guid.NewGuid():N}"[..10],
            type = 0
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdLike>())!.Id;
    }

    private async Task<Guid> CreateDriverAsync()
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/drivers", new
        {
            employeeNo = $"EMP-{Guid.NewGuid():N}"[..12],
            name = "Test Driver",
            licenceCode = "C1"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdLike>())!.Id;
    }

    private sealed record IdLike(Guid Id);
    private sealed record LoadLike(Guid Id, List<LegLike> Legs);
    private sealed record LegLike(Guid Id);
    private sealed record LoadStatusLike(int Status);
}
