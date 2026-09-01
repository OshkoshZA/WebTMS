using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>Master-data deactivate/reactivate round-trips and the two AddCurrency race regressions found this project.</summary>
[Collection(StaffTestCollection.Name)]
public class MasterDataRoundTripTests
{
    private readonly StaffTestFixture _fx;

    public MasterDataRoundTripTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task Client_deactivate_then_reactivate_round_trips()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);

        (await _fx.StaffClient.PostAsync($"/api/v1/clients/{clientId}/deactivate", null)).EnsureSuccessStatusCode();
        var deactivated = await _fx.StaffClient.GetFromJsonAsync<StatusLike>($"/api/v1/clients/{clientId}");
        Assert.Equal(1, deactivated!.Status); // Deactivated

        (await _fx.StaffClient.PostAsync($"/api/v1/clients/{clientId}/reactivate", null)).EnsureSuccessStatusCode();
        var reactivated = await _fx.StaffClient.GetFromJsonAsync<StatusLike>($"/api/v1/clients/{clientId}");
        Assert.Equal(0, reactivated!.Status); // Active
    }

    [Fact]
    public async Task Subcontractor_deactivate_then_reactivate_round_trips()
    {
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);

        (await _fx.StaffClient.PostAsync($"/api/v1/subcontractors/{subcontractorId}/deactivate", null)).EnsureSuccessStatusCode();
        var deactivated = await _fx.StaffClient.GetFromJsonAsync<StatusLike>($"/api/v1/subcontractors/{subcontractorId}");
        Assert.Equal(1, deactivated!.Status);

        (await _fx.StaffClient.PostAsync($"/api/v1/subcontractors/{subcontractorId}/reactivate", null)).EnsureSuccessStatusCode();
        var reactivated = await _fx.StaffClient.GetFromJsonAsync<StatusLike>($"/api/v1/subcontractors/{subcontractorId}");
        Assert.Equal(0, reactivated!.Status);
    }

    /// <summary>Direct regression test for the fix in 2463fac: two concurrent AddCurrency calls for the same (Client, Currency) pair used to race past the pre-check and surface as a raw 500 instead of a clean 409.</summary>
    [Fact]
    public async Task Concurrent_client_AddCurrency_for_the_same_pair_resolves_to_exactly_one_success()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var otherCurrencyId = FindAnotherCurrencyId();

        var (client1, client2) = _fx.GetRaceClients();
        object Body() => new { currencyId = otherCurrencyId, creditLimit = 1000m };

        var results = await Task.WhenAll(
            client1.PostAsJsonAsync($"/api/v1/clients/{clientId}/currencies", Body()),
            client2.PostAsJsonAsync($"/api/v1/clients/{clientId}/currencies", Body()));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    /// <summary>The symmetric regression for SubcontractorsController.AddCurrency.</summary>
    [Fact]
    public async Task Concurrent_subcontractor_AddCurrency_for_the_same_pair_resolves_to_exactly_one_success()
    {
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var otherCurrencyId = FindAnotherCurrencyId();

        var (client1, client2) = _fx.GetRaceClients();
        object Body() => new { currencyId = otherCurrencyId };

        var results = await Task.WhenAll(
            client1.PostAsJsonAsync($"/api/v1/subcontractors/{subcontractorId}/currencies", Body()),
            client2.PostAsJsonAsync($"/api/v1/subcontractors/{subcontractorId}/currencies", Body()));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    /// <summary>
    /// No CurrenciesController exists to discover reference-data ids through the API,
    /// so this is the same seeded USD currency id used throughout this project's own
    /// manual verification — anything other than StaffTestFixture.CurrencyId (the ZAR
    /// primary every fixture Client/Subcontractor is created with) works here, since
    /// AddCurrency only ever rejects re-adding the primary.
    /// </summary>
    private static Guid FindAnotherCurrencyId() => Guid.Parse("983cc062-2b8a-41d4-9209-a4b05f6dcc1d");

    private sealed record StatusLike(int Status);
}
