using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>The shared, non-company-scoped load-type reference list (§5.1) — read-only, and open to any authenticated caller since the Customer Portal's own booking flow needs it too.</summary>
[Collection(StaffTestCollection.Name)]
public class LoadTypesTests
{
    private readonly StaffTestFixture _fx;

    public LoadTypesTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task List_returns_the_seeded_load_types()
    {
        var loadTypes = await _fx.StaffClient.GetFromJsonAsync<List<LoadTypeDto>>("/api/v1/load-types");

        Assert.Contains(loadTypes!, t => t.Code == "FTL");
        Assert.Contains(loadTypes!, t => t.Code == "LTL");
        Assert.Contains(loadTypes!, t => t.Code == "BULK");
    }

    private sealed record LoadTypeDto(Guid Id, string Code, string Description);
}
