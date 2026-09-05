using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>The shared, non-company-scoped country reference list (§04) — read-only, mirroring UnitsOfMeasureController/CurrenciesController/LoadTypesController's own "documented by everything that references it, but never independently listable" fix.</summary>
[Collection(StaffTestCollection.Name)]
public class CountriesTests
{
    private readonly StaffTestFixture _fx;

    public CountriesTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task List_returns_the_seeded_country()
    {
        var countries = await _fx.StaffClient.GetFromJsonAsync<List<CountryDto>>("/api/v1/countries");

        Assert.Contains(countries!, c => c.Code == "ZA");
    }

    private sealed record CountryDto(Guid Id, string Code, string Name);
}
