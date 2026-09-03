using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>Exchange-rate capture/override (§4.3, §11.2) and the buy→sell converted margin it feeds (LoadsController.Margin) — the manual-capture half of the Rating exchange-rate engine; the automated daily-refresh background job §4.3 also describes is out of scope.</summary>
[Collection(StaffTestCollection.Name)]
public class ExchangeRateAndMarginTests
{
    private readonly StaffTestFixture _fx;

    // The same seeded USD id MasterDataRoundTripTests uses as "any currency other than
    // the ZAR primary every fixture Client/Subcontractor is created with" — see that
    // file's own FindAnotherCurrencyId for why this is a literal rather than discovered
    // via an API call (no CurrenciesController exists).
    private static readonly Guid UsdCurrencyId = Guid.Parse("983cc062-2b8a-41d4-9209-a4b05f6dcc1d");

    public ExchangeRateAndMarginTests(StaffTestFixture fx) => _fx = fx;

    private async Task<Guid> CaptureRateAsync(Guid from, Guid to, DateOnly effectiveDate, decimal rate)
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/exchange-rates",
            new { fromCurrencyId = from, toCurrencyId = to, effectiveDate, rate });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExchangeRateDto>())!.Id;
    }

    [Fact]
    public async Task Capturing_a_rate_makes_it_retrievable_on_or_after_its_effective_date()
    {
        var effectiveDate = new DateOnly(2026, 1, 1);
        await CaptureRateAsync(Guid.Parse(StaffTestFixture.CurrencyId), UsdCurrencyId, effectiveDate, 0.055m);

        var laterQuery = await _fx.StaffClient.GetFromJsonAsync<ExchangeRateDto>(
            $"/api/v1/exchange-rates?from={StaffTestFixture.CurrencyId}&to={UsdCurrencyId}&date=2026-06-15");
        Assert.Equal(0.055m, laterQuery!.Rate);
        Assert.Equal(effectiveDate, laterQuery.EffectiveDate);
    }

    [Fact]
    public async Task A_second_capture_for_the_same_pair_and_date_overrides_rather_than_duplicates()
    {
        var effectiveDate = new DateOnly(2026, 2, 1);
        var firstId = await CaptureRateAsync(UsdCurrencyId, Guid.Parse(StaffTestFixture.CurrencyId), effectiveDate, 18.0m);
        var secondId = await CaptureRateAsync(UsdCurrencyId, Guid.Parse(StaffTestFixture.CurrencyId), effectiveDate, 18.5m);

        Assert.Equal(firstId, secondId); // same row, updated — not a second one

        var current = await _fx.StaffClient.GetFromJsonAsync<ExchangeRateDto>(
            $"/api/v1/exchange-rates?from={UsdCurrencyId}&to={StaffTestFixture.CurrencyId}&date=2026-02-01");
        Assert.Equal(18.5m, current!.Rate);
    }

    [Fact]
    public async Task Get_404s_when_no_rate_was_ever_captured_for_that_pair()
    {
        var neverCapturedFrom = Guid.NewGuid();
        var response = await _fx.StaffClient.GetAsync(
            $"/api/v1/exchange-rates?from={neverCapturedFrom}&to={StaffTestFixture.CurrencyId}&date=2026-01-01");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Capture_rejects_a_currency_paired_with_itself_and_a_non_positive_rate()
    {
        var same = Guid.Parse(StaffTestFixture.CurrencyId);
        var selfPair = await _fx.StaffClient.PostAsJsonAsync("/api/v1/exchange-rates",
            new { fromCurrencyId = same, toCurrencyId = same, effectiveDate = new DateOnly(2026, 1, 1), rate = 1m });
        Assert.Equal(HttpStatusCode.BadRequest, selfPair.StatusCode);

        var zeroRate = await _fx.StaffClient.PostAsJsonAsync("/api/v1/exchange-rates",
            new { fromCurrencyId = same, toCurrencyId = UsdCurrencyId, effectiveDate = new DateOnly(2026, 1, 1), rate = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, zeroRate.StatusCode);
    }

    [Fact]
    public async Task Margin_for_an_own_fleet_leg_is_just_the_sell_total_with_no_conversion()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, _) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"MRG-{Guid.NewGuid():N}", sellRatePerUnit: 500);

        var margin = await _fx.StaffClient.GetFromJsonAsync<LoadMarginDto>($"/api/v1/loads/{loadId}/margin");
        var leg = Assert.Single(margin!.Legs);

        Assert.Equal(500m, leg.SellTotal);
        Assert.Null(leg.BuyCurrencyId);
        Assert.Equal(500m, leg.Margin);
        Assert.Null(leg.Note);
    }

    [Fact]
    public async Task Margin_for_a_cross_currency_leg_converts_the_buy_side_using_the_rate_effective_on_pickup_date()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clientId = await _fx.CreateClientAsync(suffix);
        var subcontractorId = await _fx.CreateSubcontractorAsync(suffix);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/subcontractors/{subcontractorId}/currencies", new { currencyId = UsdCurrencyId }))
            .EnsureSuccessStatusCode();

        var pickupDate = new DateOnly(2026, 3, 15);
        await CaptureRateAsync(UsdCurrencyId, Guid.Parse(StaffTestFixture.CurrencyId), pickupDate.AddDays(-5), 10m);

        var loadId = await _fx.CreateLoadAsync(clientId, $"MRGX-{suffix}",
            pickupWindowStart: new DateTimeOffset(pickupDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var legResponse = await _fx.AddSubcontractedLegAsync(loadId, subcontractorId);
        legResponse.EnsureSuccessStatusCode();
        var legId = (await legResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs/{legId}/commodity-lines", new
        {
            commodityId = Guid.Parse(StaffTestFixture.CommodityId),
            quantity = 1,
            unitOfMeasureId = Guid.Parse(StaffTestFixture.UnitOfMeasureId),
            sellRatePerUnit = 500m,
            sellCurrencyId = Guid.Parse(StaffTestFixture.CurrencyId),
            buyRatePerUnit = 100m,
            buyCurrencyId = UsdCurrencyId
        })).EnsureSuccessStatusCode();

        var margin = await _fx.StaffClient.GetFromJsonAsync<LoadMarginDto>($"/api/v1/loads/{loadId}/margin");
        var leg = Assert.Single(margin!.Legs);

        Assert.Equal(500m, leg.SellTotal);
        Assert.Equal(100m, leg.BuyTotal);
        Assert.Equal(10m, leg.ExchangeRateUsed);
        Assert.Equal(1000m, leg.ConvertedBuyTotal); // 100 USD * 10 = 1000 ZAR
        Assert.Equal(-500m, leg.Margin); // 500 - 1000
        Assert.Null(leg.Note);
    }

    [Fact]
    public async Task Margin_without_a_pickup_date_or_a_captured_rate_reports_a_note_instead_of_guessing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clientId = await _fx.CreateClientAsync(suffix);
        var subcontractorId = await _fx.CreateSubcontractorAsync(suffix);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/subcontractors/{subcontractorId}/currencies", new { currencyId = UsdCurrencyId }))
            .EnsureSuccessStatusCode();

        // No pickupWindowStart at all this time.
        var loadId = await _fx.CreateLoadAsync(clientId, $"MRGN-{suffix}");
        var legResponse = await _fx.AddSubcontractedLegAsync(loadId, subcontractorId);
        legResponse.EnsureSuccessStatusCode();
        var legId = (await legResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs/{legId}/commodity-lines", new
        {
            commodityId = Guid.Parse(StaffTestFixture.CommodityId),
            quantity = 1,
            unitOfMeasureId = Guid.Parse(StaffTestFixture.UnitOfMeasureId),
            sellRatePerUnit = 500m,
            sellCurrencyId = Guid.Parse(StaffTestFixture.CurrencyId),
            buyRatePerUnit = 100m,
            buyCurrencyId = UsdCurrencyId
        })).EnsureSuccessStatusCode();

        var margin = await _fx.StaffClient.GetFromJsonAsync<LoadMarginDto>($"/api/v1/loads/{loadId}/margin");
        var leg = Assert.Single(margin!.Legs);

        Assert.Null(leg.Margin);
        Assert.Null(leg.ConvertedBuyTotal);
        Assert.Contains("PickupWindowStart", leg.Note);
    }

    private sealed record ExchangeRateDto(Guid Id, DateOnly EffectiveDate, decimal Rate);
    private sealed record IdDto(Guid Id);
    private sealed record LoadLegMarginDto(
        Guid LegId, Guid? SellCurrencyId, decimal SellTotal, Guid? BuyCurrencyId, decimal BuyTotal,
        decimal? ExchangeRateUsed, decimal? ConvertedBuyTotal, decimal? Margin, string? Note);
    private sealed record LoadMarginDto(Guid LoadId, List<LoadLegMarginDto> Legs);
}
