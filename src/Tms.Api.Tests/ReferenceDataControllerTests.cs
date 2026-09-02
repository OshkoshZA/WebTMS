using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// The six master-data controllers that follow the standard CRUD convention (§11.5:
/// List/Get/Create/Update/Deactivate/Reactivate) but, unlike Client/Subcontractor/Vehicle,
/// had no direct test coverage at all — only touched incidentally through hardcoded
/// seeded ids other fixtures reference. One full round trip per resource, plus two
/// resource-specific business rules worth a named test: CostCentresController's
/// hierarchy cycle detection, and DriversController's refusal to let a generic Update
/// toggle Deactivated as a side effect.
/// </summary>
[Collection(StaffTestCollection.Name)]
public class ReferenceDataControllerTests
{
    private readonly StaffTestFixture _fx;

    public ReferenceDataControllerTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task Vehicle_full_crud_round_trip()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/vehicles", new
        {
            fleetNo = $"FL-{suffix}",
            registration = $"REG-{suffix}",
            type = 0
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var vehicle = await createResponse.Content.ReadFromJsonAsync<VehicleDto>();

        var list = await _fx.StaffClient.GetFromJsonAsync<List<VehicleDto>>("/api/v1/vehicles");
        Assert.Contains(list!, v => v.Id == vehicle!.Id);

        var got = await _fx.StaffClient.GetFromJsonAsync<VehicleDto>($"/api/v1/vehicles/{vehicle!.Id}");
        Assert.Equal(vehicle.FleetNo, got!.FleetNo);

        var updateResponse = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/vehicles/{vehicle.Id}", new
        {
            fleetNo = $"FL-{suffix}-updated",
            registration = vehicle.Registration,
            type = 0
        });
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);
        var updated = await _fx.StaffClient.GetFromJsonAsync<VehicleDto>($"/api/v1/vehicles/{vehicle.Id}");
        Assert.Equal($"FL-{suffix}-updated", updated!.FleetNo);

        (await _fx.StaffClient.PostAsync($"/api/v1/vehicles/{vehicle.Id}/deactivate", null)).EnsureSuccessStatusCode();
        var deactivated = await _fx.StaffClient.GetFromJsonAsync<VehicleDto>($"/api/v1/vehicles/{vehicle.Id}");
        Assert.Equal(1, deactivated!.Status); // Deactivated

        (await _fx.StaffClient.PostAsync($"/api/v1/vehicles/{vehicle.Id}/reactivate", null)).EnsureSuccessStatusCode();
        var reactivated = await _fx.StaffClient.GetFromJsonAsync<VehicleDto>($"/api/v1/vehicles/{vehicle.Id}");
        Assert.Equal(0, reactivated!.Status); // Active
    }

    [Fact]
    public async Task Driver_full_crud_round_trip()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/drivers", new
        {
            employeeNo = $"EMP-{suffix}",
            name = "Test Driver",
            licenceCode = "C1"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var driver = await createResponse.Content.ReadFromJsonAsync<DriverDto>();

        var list = await _fx.StaffClient.GetFromJsonAsync<List<DriverDto>>("/api/v1/drivers");
        Assert.Contains(list!, d => d.Id == driver!.Id);

        var got = await _fx.StaffClient.GetFromJsonAsync<DriverDto>($"/api/v1/drivers/{driver!.Id}");
        Assert.Equal(driver.Name, got!.Name);

        var updateResponse = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/drivers/{driver.Id}", new
        {
            name = "Updated Driver Name",
            licenceCode = "C1",
            status = 0 // Active — unchanged
        });
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);
        var updated = await _fx.StaffClient.GetFromJsonAsync<DriverDto>($"/api/v1/drivers/{driver.Id}");
        Assert.Equal("Updated Driver Name", updated!.Name);

        (await _fx.StaffClient.PostAsync($"/api/v1/drivers/{driver.Id}/deactivate", null)).EnsureSuccessStatusCode();
        var deactivated = await _fx.StaffClient.GetFromJsonAsync<DriverDto>($"/api/v1/drivers/{driver.Id}");
        Assert.Equal(2, deactivated!.Status); // Deactivated (Active=0, OnLeave=1, Deactivated=2)

        (await _fx.StaffClient.PostAsync($"/api/v1/drivers/{driver.Id}/reactivate", null)).EnsureSuccessStatusCode();
        var reactivated = await _fx.StaffClient.GetFromJsonAsync<DriverDto>($"/api/v1/drivers/{driver.Id}");
        Assert.Equal(0, reactivated!.Status); // Active
    }

    /// <summary>DriversController.Update refuses to let Deactivated be entered or left as a side effect of an otherwise-ordinary field edit — only the dedicated Deactivate/Reactivate actions may cross that boundary.</summary>
    [Fact]
    public async Task Driver_update_cannot_toggle_deactivated_status_as_a_side_effect()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/drivers", new
        {
            employeeNo = $"EMP-{suffix}",
            name = "Side Effect Test Driver",
            licenceCode = "C1"
        });
        createResponse.EnsureSuccessStatusCode();
        var driverId = (await createResponse.Content.ReadFromJsonAsync<DriverDto>())!.Id;

        var deactivateViaUpdate = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/drivers/{driverId}", new
        {
            name = "Side Effect Test Driver",
            licenceCode = "C1",
            status = 2 // Deactivated — not via the dedicated action
        });
        Assert.Equal(HttpStatusCode.Conflict, deactivateViaUpdate.StatusCode);

        (await _fx.StaffClient.PostAsync($"/api/v1/drivers/{driverId}/deactivate", null)).EnsureSuccessStatusCode();

        var reactivateViaUpdate = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/drivers/{driverId}", new
        {
            name = "Side Effect Test Driver",
            licenceCode = "C1",
            status = 0 // Active — not via the dedicated action
        });
        Assert.Equal(HttpStatusCode.Conflict, reactivateViaUpdate.StatusCode);
    }

    [Fact]
    public async Task Commodity_full_crud_round_trip()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/commodities", new
        {
            code = $"CMD-{suffix}",
            name = "Test Commodity",
            defaultUnitOfMeasureId = Guid.Parse(StaffTestFixture.UnitOfMeasureId),
            category = 0
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var commodity = await createResponse.Content.ReadFromJsonAsync<ActiveEntityDto>();

        var list = await _fx.StaffClient.GetFromJsonAsync<List<ActiveEntityDto>>("/api/v1/commodities");
        Assert.Contains(list!, c => c.Id == commodity!.Id);

        var updateResponse = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/commodities/{commodity!.Id}", new
        {
            code = $"CMD-{suffix}",
            name = "Updated Commodity Name",
            defaultUnitOfMeasureId = Guid.Parse(StaffTestFixture.UnitOfMeasureId),
            category = 0
        });
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        (await _fx.StaffClient.PostAsync($"/api/v1/commodities/{commodity.Id}/deactivate", null)).EnsureSuccessStatusCode();
        var deactivated = await _fx.StaffClient.GetFromJsonAsync<ActiveEntityDto>($"/api/v1/commodities/{commodity.Id}");
        Assert.False(deactivated!.Active);

        (await _fx.StaffClient.PostAsync($"/api/v1/commodities/{commodity.Id}/reactivate", null)).EnsureSuccessStatusCode();
        var reactivated = await _fx.StaffClient.GetFromJsonAsync<ActiveEntityDto>($"/api/v1/commodities/{commodity.Id}");
        Assert.True(reactivated!.Active);
    }

    [Fact]
    public async Task CostCentre_full_crud_round_trip()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/cost-centres", new { code = $"CC-{suffix}", name = "Test Cost Centre" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var costCentre = await createResponse.Content.ReadFromJsonAsync<ActiveEntityDto>();

        var list = await _fx.StaffClient.GetFromJsonAsync<List<ActiveEntityDto>>("/api/v1/cost-centres");
        Assert.Contains(list!, c => c.Id == costCentre!.Id);

        var updateResponse = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/cost-centres/{costCentre!.Id}",
            new { code = $"CC-{suffix}", name = "Updated Cost Centre Name", parentCostCentreId = (Guid?)null });
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        (await _fx.StaffClient.PostAsync($"/api/v1/cost-centres/{costCentre.Id}/deactivate", null)).EnsureSuccessStatusCode();
        var deactivated = await _fx.StaffClient.GetFromJsonAsync<ActiveEntityDto>($"/api/v1/cost-centres/{costCentre.Id}");
        Assert.False(deactivated!.Active);

        (await _fx.StaffClient.PostAsync($"/api/v1/cost-centres/{costCentre.Id}/reactivate", null)).EnsureSuccessStatusCode();
        var reactivated = await _fx.StaffClient.GetFromJsonAsync<ActiveEntityDto>($"/api/v1/cost-centres/{costCentre.Id}");
        Assert.True(reactivated!.Active);
    }

    /// <summary>CostCentresController.Update walks the full ancestry chain of a proposed parent, not just a direct self-reference, so a multi-level cycle (A's parent becomes B, B's parent is already A) is refused too.</summary>
    [Fact]
    public async Task CostCentre_update_refuses_both_direct_and_multi_level_hierarchy_cycles()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var aResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/cost-centres", new { code = $"CCA-{suffix}", name = "A" });
        aResponse.EnsureSuccessStatusCode();
        var aId = (await aResponse.Content.ReadFromJsonAsync<ActiveEntityDto>())!.Id;

        var selfParent = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/cost-centres/{aId}", new { code = $"CCA-{suffix}", name = "A", parentCostCentreId = aId });
        Assert.Equal(HttpStatusCode.BadRequest, selfParent.StatusCode);

        var bResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/cost-centres", new { code = $"CCB-{suffix}", name = "B", parentCostCentreId = aId });
        bResponse.EnsureSuccessStatusCode();
        var bId = (await bResponse.Content.ReadFromJsonAsync<ActiveEntityDto>())!.Id;

        // B's parent is A; making A's parent B would close a two-level cycle.
        var multiLevelCycle = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/cost-centres/{aId}", new { code = $"CCA-{suffix}", name = "A", parentCostCentreId = bId });
        Assert.Equal(HttpStatusCode.BadRequest, multiLevelCycle.StatusCode);
    }

    [Fact]
    public async Task ExpenseType_full_crud_round_trip()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/expense-types", new { code = $"ET-{suffix}", name = "Test Expense Type" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var expenseType = await createResponse.Content.ReadFromJsonAsync<ActiveEntityDto>();

        var list = await _fx.StaffClient.GetFromJsonAsync<List<ActiveEntityDto>>("/api/v1/expense-types");
        Assert.Contains(list!, e => e.Id == expenseType!.Id);

        var updateResponse = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/expense-types/{expenseType!.Id}", new { code = $"ET-{suffix}", name = "Updated Expense Type" });
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        (await _fx.StaffClient.PostAsync($"/api/v1/expense-types/{expenseType.Id}/deactivate", null)).EnsureSuccessStatusCode();
        var deactivated = await _fx.StaffClient.GetFromJsonAsync<ActiveEntityDto>($"/api/v1/expense-types/{expenseType.Id}");
        Assert.False(deactivated!.Active);

        (await _fx.StaffClient.PostAsync($"/api/v1/expense-types/{expenseType.Id}/reactivate", null)).EnsureSuccessStatusCode();
        var reactivated = await _fx.StaffClient.GetFromJsonAsync<ActiveEntityDto>($"/api/v1/expense-types/{expenseType.Id}");
        Assert.True(reactivated!.Active);
    }

    [Fact]
    public async Task Location_full_crud_round_trip()
    {
        // No CountriesController exists to discover a valid CountryId, so read it off an
        // already-seeded location — the same fixture id every other test's legs use.
        var seeded = await _fx.StaffClient.GetFromJsonAsync<LocationDto>($"/api/v1/locations/{StaffTestFixture.OriginLocationId}");
        var countryId = seeded!.CountryId;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/locations", new { name = $"Test Location {suffix}", province = "Gauteng", countryId });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var location = await createResponse.Content.ReadFromJsonAsync<LocationDto>();

        var list = await _fx.StaffClient.GetFromJsonAsync<List<LocationDto>>("/api/v1/locations");
        Assert.Contains(list!, l => l.Id == location!.Id);

        var updateResponse = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/locations/{location!.Id}", new { name = $"Updated Location {suffix}", province = "Western Cape", countryId });
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        (await _fx.StaffClient.PostAsync($"/api/v1/locations/{location.Id}/deactivate", null)).EnsureSuccessStatusCode();
        var deactivated = await _fx.StaffClient.GetFromJsonAsync<LocationDto>($"/api/v1/locations/{location.Id}");
        Assert.False(deactivated!.Active);

        (await _fx.StaffClient.PostAsync($"/api/v1/locations/{location.Id}/reactivate", null)).EnsureSuccessStatusCode();
        var reactivated = await _fx.StaffClient.GetFromJsonAsync<LocationDto>($"/api/v1/locations/{location.Id}");
        Assert.True(reactivated!.Active);
    }

    private sealed record VehicleDto(Guid Id, string FleetNo, string Registration, int Status);
    private sealed record DriverDto(Guid Id, string Name, int Status);
    private sealed record ActiveEntityDto(Guid Id, bool Active);
    private sealed record LocationDto(Guid Id, string Name, Guid CountryId, bool Active);
}
