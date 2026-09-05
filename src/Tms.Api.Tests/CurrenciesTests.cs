using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>The shared, non-company-scoped currency reference list (§04, §4.3) — read-only.</summary>
[Collection(StaffTestCollection.Name)]
public class CurrenciesTests
{
    private readonly StaffTestFixture _fx;

    public CurrenciesTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task List_returns_the_seeded_currencies()
    {
        var currencies = await _fx.StaffClient.GetFromJsonAsync<List<CurrencyDto>>("/api/v1/currencies");

        Assert.Contains(currencies!, c => c.Code == "ZAR");
        Assert.Contains(currencies!, c => c.Code == "USD");
    }

    private sealed record CurrencyDto(Guid Id, string Code, string Name, string Symbol);
}
