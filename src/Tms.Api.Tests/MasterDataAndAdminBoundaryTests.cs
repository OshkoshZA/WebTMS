using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// Every action fixed with the outright "any portal caller is Forbidden, this isn't
/// part of either portal's documented scope" pattern (2463fac, f541b9e, 46b90d3) —
/// data-driven so a newly-added endpoint of the same shape is one row away from being
/// covered, rather than requiring a whole new hand-written test method.
/// </summary>
[Collection(PortalTestCollection.Name)]
public class MasterDataAndAdminBoundaryTests
{
    private readonly PortalTestFixture _fixture;

    public MasterDataAndAdminBoundaryTests(PortalTestFixture fixture) => _fixture = fixture;

    public static IEnumerable<object[]> BlockedForAnyPortalCallerRoutes => new[]
    {
        new object[] { "/api/v1/clients" },
        new object[] { "/api/v1/subcontractors" },
        new object[] { "/api/v1/companies" },
        new object[] { "/api/v1/users" },
        new object[] { "/api/v1/roles" },
        new object[] { "/api/v1/functions" },
        new object[] { "/api/v1/api-clients" },
        new object[] { "/api/v1/debriefs" },
        new object[] { "/api/v1/financial-periods" },
        new object[] { "/api/v1/financial-years" },
        new object[] { "/api/v1/commodities" },
        new object[] { "/api/v1/cost-centres" },
        new object[] { "/api/v1/drivers" },
        new object[] { "/api/v1/locations" },
        new object[] { "/api/v1/vehicles" },
        new object[] { "/api/v1/units-of-measure" },
        new object[] { "/api/v1/countries" },
    };

    // Blocked for a Client Portal contact but NOT for a Subcontractor Portal contact —
    // ExpenseTypesController.List/Get was opened to the latter (see
    // SupplierPortalBoundaryTests.ExpenseTypes_list_and_get_are_open_to_subcontractor_portal_but_still_reject_client_contact)
    // so it doesn't belong in BlockedForAnyPortalCallerRoutes above any more.
    public static IEnumerable<object[]> BlockedForClientContactOnlyRoutes => new[]
    {
        new object[] { "/api/v1/expense-types" },
    };

    [Theory]
    [MemberData(nameof(BlockedForAnyPortalCallerRoutes))]
    public async Task Route_rejects_subcontractor_contact(string route)
    {
        using var client = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BlockedForAnyPortalCallerRoutes))]
    [MemberData(nameof(BlockedForClientContactOnlyRoutes))]
    public async Task Route_rejects_client_contact(string route)
    {
        using var client = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BlockedForAnyPortalCallerRoutes))]
    public async Task Route_remains_accessible_to_staff(string route)
    {
        var response = await _fixture.StaffClient.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DebtorsAging_rejects_both_portal_types_and_remains_accessible_to_staff()
    {
        var periodsResponse = await _fixture.StaffClient.GetAsync("/api/v1/financial-periods");
        periodsResponse.EnsureSuccessStatusCode();
        var periods = await periodsResponse.Content.ReadFromJsonAsync<List<PeriodLike>>();
        var anyPeriod = periods!.First();

        using var subClient = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await subClient.GetAsync($"/api/v1/financial-periods/{anyPeriod.Id}/debtors-aging")).StatusCode);

        using var clientClient = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await clientClient.GetAsync($"/api/v1/financial-periods/{anyPeriod.Id}/debtors-aging")).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await _fixture.StaffClient.GetAsync($"/api/v1/financial-periods/{anyPeriod.Id}/debtors-aging")).StatusCode);
    }

    private sealed record PeriodLike(Guid Id);
}
