using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>The shared, non-company-scoped unit-of-measure reference list (§5.5) — read-only, mirroring CurrenciesController/LoadTypesController's own "documented by everything that references it, but never independently listable" fix.</summary>
[Collection(StaffTestCollection.Name)]
public class UnitsOfMeasureTests
{
    private readonly StaffTestFixture _fx;

    public UnitsOfMeasureTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task List_returns_the_seeded_units_of_measure()
    {
        var unitsOfMeasure = await _fx.StaffClient.GetFromJsonAsync<List<UnitOfMeasureDto>>("/api/v1/units-of-measure");

        Assert.Contains(unitsOfMeasure!, u => u.Code == "PER_KM");
        Assert.Contains(unitsOfMeasure!, u => u.Code == "PER_TON");
        Assert.Contains(unitsOfMeasure!, u => u.Code == "PER_PALLET");
        Assert.Contains(unitsOfMeasure!, u => u.Code == "PER_LOAD");
        Assert.Contains(unitsOfMeasure!, u => u.Code == "PER_HOUR");
        Assert.Contains(unitsOfMeasure!, u => u.Code == "PER_LITRE");
    }

    private sealed record UnitOfMeasureDto(Guid Id, string Code, string Description);
}
