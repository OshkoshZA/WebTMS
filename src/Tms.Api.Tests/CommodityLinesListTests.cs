using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// LoadsController.ListCommodityLines — added because AddCommodityLine returns only the
/// bare CommodityLine it just created (no rate fields — those live on separate RateLine
/// rows), and GET /loads/{id} never nests a leg's own CommodityLines either, so there
/// was previously no way at all to see what's already on a leg after a page reload.
/// </summary>
[Collection(StaffTestCollection.Name)]
public class CommodityLinesListTests
{
    private readonly StaffTestFixture _fx;

    public CommodityLinesListTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task An_own_fleet_legs_line_reports_only_its_sell_side()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"CLL-OWN-{Guid.NewGuid():N}", sellRatePerUnit: 500);

        var lines = await _fx.StaffClient.GetFromJsonAsync<List<CommodityLineDto>>(
            $"/api/v1/loads/{loadId}/legs/{legId}/commodity-lines");
        var line = Assert.Single(lines!);

        Assert.Equal(Guid.Parse(StaffTestFixture.CommodityId), line.CommodityId);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal(Guid.Parse(StaffTestFixture.CurrencyId), line.SellCurrencyId);
        Assert.Equal(500m, line.SellRatePerUnit);
        Assert.Equal(500m, line.SellAmount);
        Assert.Null(line.BuyCurrencyId);
        Assert.Null(line.BuyRatePerUnit);
        Assert.Null(line.BuyAmount);
    }

    [Fact]
    public async Task A_subcontracted_legs_line_reports_both_sides_and_multiple_lines_keep_their_own_rates()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clientId = await _fx.CreateClientAsync(suffix);
        var subcontractorId = await _fx.CreateSubcontractorAsync(suffix);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(
            clientId, $"CLL-SUB-{suffix}", sellRatePerUnit: 500, subcontractorId: subcontractorId, buyRatePerUnit: 300);

        (await _fx.AddCommodityLineAsync(loadId, legId, sellRatePerUnit: 200, buyRatePerUnit: 120)).EnsureSuccessStatusCode();

        var lines = await _fx.StaffClient.GetFromJsonAsync<List<CommodityLineDto>>(
            $"/api/v1/loads/{loadId}/legs/{legId}/commodity-lines");
        Assert.Equal(2, lines!.Count);

        var first = lines.Single(l => l.SellRatePerUnit == 500m);
        Assert.Equal(300m, first.BuyRatePerUnit);
        Assert.Equal(300m, first.BuyAmount);

        var second = lines.Single(l => l.SellRatePerUnit == 200m);
        Assert.Equal(120m, second.BuyRatePerUnit);
        Assert.Equal(120m, second.BuyAmount);
    }

    [Fact]
    public async Task Unknown_leg_404s()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var loadId = await _fx.CreateLoadAsync(clientId, $"CLL-404-{Guid.NewGuid():N}");

        var response = await _fx.StaffClient.GetAsync($"/api/v1/loads/{loadId}/legs/{Guid.NewGuid()}/commodity-lines");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CommodityLineDto(
        Guid Id, Guid LoadLegId, Guid CommodityId, decimal Quantity, Guid UnitOfMeasureId, int SequenceNo,
        Guid SellCurrencyId, decimal SellRatePerUnit, decimal SellAmount,
        Guid? BuyCurrencyId, decimal? BuyRatePerUnit, decimal? BuyAmount);
}
