using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// The internal dashboard's company-wide KPI aggregates (docs/architecture.html §16.2)
/// — DashboardController's margin/credit-exposure/payables summaries. Portal-Forbidden
/// coverage for these routes lives in MasterDataAndAdminBoundaryTests instead, alongside
/// every other "staff only, no exceptions" endpoint of the same shape. Assertions here
/// are all before/after deltas, never an absolute total — this shared dev database
/// accumulates real data across every test class in the collection (and, locally,
/// across every manual verification pass this project has ever run), so only a delta
/// against a value this test itself creates is something the test can actually own.
/// </summary>
[Collection(StaffTestCollection.Name)]
public class DashboardTests
{
    private static readonly Guid UsdCurrencyId = Guid.Parse("983cc062-2b8a-41d4-9209-a4b05f6dcc1d");
    private static readonly Guid ZarCurrencyId = Guid.Parse(StaffTestFixture.CurrencyId);

    private readonly StaffTestFixture _fx;

    public DashboardTests(StaffTestFixture fx) => _fx = fx;

    private Task<MarginSummaryDto> GetMarginSummaryAsync() =>
        _fx.StaffClient.GetFromJsonAsync<MarginSummaryDto>("/api/v1/dashboard/margin-summary")!;

    private Task<CreditExposureSummaryDto> GetCreditExposureSummaryAsync() =>
        _fx.StaffClient.GetFromJsonAsync<CreditExposureSummaryDto>("/api/v1/dashboard/credit-exposure-summary")!;

    private Task<PayablesSummaryDto> GetPayablesSummaryAsync() =>
        _fx.StaffClient.GetFromJsonAsync<PayablesSummaryDto>("/api/v1/dashboard/payables-summary")!;

    private decimal ExposureFor(CreditExposureSummaryDto summary, Guid currencyId, Func<CurrencyExposureDto, decimal> select) =>
        summary.ByCurrency.FirstOrDefault(c => c.CurrencyId == currencyId) is { } row ? select(row) : 0m;

    [Fact]
    public async Task Margin_summary_reports_the_reporting_currency_and_reflects_this_companys_own_currency()
    {
        var summary = await GetMarginSummaryAsync();
        Assert.Equal(ZarCurrencyId, summary.ReportingCurrencyId);
    }

    [Fact]
    public async Task An_own_fleet_sell_only_line_adds_its_full_amount_to_sell_total_and_margin()
    {
        var before = await GetMarginSummaryAsync();

        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        await _fx.CreateBookedLoadWithLegAsync(clientId, $"DASHM-{Guid.NewGuid():N}", sellRatePerUnit: 777m);

        var after = await GetMarginSummaryAsync();

        Assert.Equal(before.SellTotal + 777m, after.SellTotal);
        Assert.Equal(before.BuyTotal, after.BuyTotal); // own-fleet: no buy RateLine at all
        Assert.Equal(before.Margin + 777m, after.Margin);
    }

    [Fact]
    public async Task A_cross_currency_buy_line_converts_using_the_latest_captured_rate_into_the_reporting_currency()
    {
        // A far-future effective date so this is unambiguously the "most recent
        // captured rate" MarginSummary resolves to, regardless of what any other test
        // in this collection has already captured for the same USD->ZAR pair at an
        // earlier (2026) date.
        var effectiveDate = new DateOnly(2030, 1, 1);
        const decimal rate = 12.5m;
        (await _fx.StaffClient.PostAsJsonAsync("/api/v1/exchange-rates",
            new { fromCurrencyId = UsdCurrencyId, toCurrencyId = ZarCurrencyId, effectiveDate, rate }))
            .EnsureSuccessStatusCode();

        var before = await GetMarginSummaryAsync();

        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/subcontractors/{subcontractorId}/currencies", new { currencyId = UsdCurrencyId }))
            .EnsureSuccessStatusCode();
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var loadId = await _fx.CreateLoadAsync(clientId, $"DASHMX-{Guid.NewGuid():N}");
        var legResponse = await _fx.AddSubcontractedLegAsync(loadId, subcontractorId);
        legResponse.EnsureSuccessStatusCode();
        var legId = (await legResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;
        // StaffTestFixture's own AddCommodityLineAsync/CreateBookedLoadWithLegAsync helpers
        // have no way to express a buyCurrencyId of their own — they always default to the
        // subcontractor's primary currency (ZAR) — so a genuine cross-currency buy line has
        // to be posted directly, the same workaround ExchangeRateAndMarginTests already uses.
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs/{legId}/commodity-lines", new
        {
            commodityId = Guid.Parse(StaffTestFixture.CommodityId),
            quantity = 1,
            unitOfMeasureId = Guid.Parse(StaffTestFixture.UnitOfMeasureId),
            sellRatePerUnit = 1000m,
            sellCurrencyId = ZarCurrencyId,
            buyRatePerUnit = 40m,
            buyCurrencyId = UsdCurrencyId,
        })).EnsureSuccessStatusCode();

        var after = await GetMarginSummaryAsync();

        Assert.Equal(before.SellTotal + 1000m, after.SellTotal);
        // 40 USD converted at the just-captured 12.5 rate = 500 ZAR added to BuyTotal.
        Assert.Equal(before.BuyTotal + 500m, after.BuyTotal);
        Assert.Equal(before.Margin + 500m, after.Margin); // 1000 sell - 500 converted buy
    }

    [Fact]
    public async Task An_issued_invoice_moves_its_amount_from_wip_to_ar_with_no_net_change_in_total_exposure()
    {
        var before = await GetCreditExposureSummaryAsync();

        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"DASHC-{Guid.NewGuid():N}", sellRatePerUnit: 900m);

        // Booked and not yet invoiced: this 900 sits in WIP, same as
        // An_unbilled_booked_load_adds_to_wip_and_total_exposure below.
        var afterBooking = await GetCreditExposureSummaryAsync();
        Assert.Equal(
            ExposureFor(before, ZarCurrencyId, c => c.TotalWip) + 900m,
            ExposureFor(afterBooking, ZarCurrencyId, c => c.TotalWip));

        await _fx.DeliverLegAsync(loadId, legId);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" })).EnsureSuccessStatusCode();
        var generateResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/invoices/generate", new { clientId });
        generateResponse.EnsureSuccessStatusCode();
        var invoiceId = (await generateResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        // Generate alone (still Draft) already excludes the line from WIP — the same
        // "referenced by any InvoiceLine, regardless of the invoice's own status" rule
        // CreditExposureService's own WIP query follows — but a Draft invoice doesn't
        // count as AR yet either, so the 900 is briefly invisible to both buckets.
        var afterGenerate = await GetCreditExposureSummaryAsync();
        Assert.Equal(
            ExposureFor(before, ZarCurrencyId, c => c.TotalWip),
            ExposureFor(afterGenerate, ZarCurrencyId, c => c.TotalWip));
        Assert.Equal(
            ExposureFor(before, ZarCurrencyId, c => c.TotalArOutstanding),
            ExposureFor(afterGenerate, ZarCurrencyId, c => c.TotalArOutstanding));

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/issue", new { })).EnsureSuccessStatusCode();

        var afterIssue = await GetCreditExposureSummaryAsync();

        // Issued: the 900 now shows as AR instead, with WIP still excluding it — the
        // WIP-to-AR handoff carries the exposure across with no double-counting, so
        // TotalExposure matches the Booked snapshot exactly even though its two
        // components have fully swapped which one holds the 900.
        Assert.Equal(
            ExposureFor(before, ZarCurrencyId, c => c.TotalArOutstanding) + 900m,
            ExposureFor(afterIssue, ZarCurrencyId, c => c.TotalArOutstanding));
        Assert.Equal(
            ExposureFor(before, ZarCurrencyId, c => c.TotalWip),
            ExposureFor(afterIssue, ZarCurrencyId, c => c.TotalWip));
        Assert.Equal(
            ExposureFor(afterBooking, ZarCurrencyId, c => c.TotalExposure),
            ExposureFor(afterIssue, ZarCurrencyId, c => c.TotalExposure));
    }

    [Fact]
    public async Task An_unbilled_booked_load_adds_to_wip_and_total_exposure()
    {
        var before = await GetCreditExposureSummaryAsync();

        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        await _fx.CreateBookedLoadWithLegAsync(clientId, $"DASHW-{Guid.NewGuid():N}", sellRatePerUnit: 650m);
        // Deliberately left Booked and never invoiced — this is exactly WIP.

        var after = await GetCreditExposureSummaryAsync();

        Assert.Equal(
            ExposureFor(before, ZarCurrencyId, c => c.TotalWip) + 650m,
            ExposureFor(after, ZarCurrencyId, c => c.TotalWip));
        Assert.Equal(
            ExposureFor(before, ZarCurrencyId, c => c.TotalExposure) + 650m,
            ExposureFor(after, ZarCurrencyId, c => c.TotalExposure));
    }

    [Fact]
    public async Task A_new_clients_credit_limit_is_summed_into_its_primary_currencys_total()
    {
        var before = await GetCreditExposureSummaryAsync();

        await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8], creditLimit: 250_000m);

        var after = await GetCreditExposureSummaryAsync();

        Assert.Equal(
            ExposureFor(before, ZarCurrencyId, c => c.TotalCreditLimit) + 250_000m,
            ExposureFor(after, ZarCurrencyId, c => c.TotalCreditLimit));
    }

    [Fact]
    public async Task Allocating_then_matching_a_subcontracted_leg_moves_the_count_from_accrued_to_available_to_export()
    {
        var before = await GetPayablesSummaryAsync();

        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        await _fx.CreateBookedLoadWithLegAsync(
            clientId, $"DASHP-{Guid.NewGuid():N}", subcontractorId: subcontractorId, buyRatePerUnit: 300m);

        var afterAccrual = await GetPayablesSummaryAsync();
        Assert.Equal(before.Accrued + 1, afterAccrual.Accrued);
        Assert.Equal(before.AvailableToExport, afterAccrual.AvailableToExport);

        var accrual = (await _fx.StaffClient.GetFromJsonAsync<List<AccrualDto>>(
            $"/api/v1/accruals?subcontractorId={subcontractorId}&status=0"))!.Single();

        var invoiceResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/supplier-invoices", new
        {
            subcontractorId,
            supplierInvoiceNumber = $"SI-{Guid.NewGuid():N}"[..15],
            invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            receivedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            amount = 300m
        });
        invoiceResponse.EnsureSuccessStatusCode();
        var invoiceId = (await invoiceResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/supplier-invoices/{invoiceId}/match", new { accrualIds = new[] { accrual.Id } }))
            .EnsureSuccessStatusCode();

        var afterMatch = await GetPayablesSummaryAsync();
        Assert.Equal(before.Accrued, afterMatch.Accrued); // back down: the one we added is now Netted
        Assert.Equal(before.AvailableToExport + 1, afterMatch.AvailableToExport);
    }

    private sealed record IdDto(Guid Id);
    private sealed record AccrualDto(Guid Id, decimal EstimatedAmount, int Status);
    private sealed record UnconvertedAmountDto(Guid CurrencyId, string Side, decimal Amount);
    private sealed record MarginSummaryDto(Guid ReportingCurrencyId, decimal SellTotal, decimal BuyTotal, decimal Margin, List<UnconvertedAmountDto> Unconverted);
    private sealed record CurrencyExposureDto(Guid CurrencyId, decimal TotalCreditLimit, decimal TotalArOutstanding, decimal TotalWip, decimal TotalExposure);
    private sealed record CreditExposureSummaryDto(List<CurrencyExposureDto> ByCurrency);
    private sealed record PayablesSummaryDto(int Accrued, int AvailableToExport, int Exported, int Paid);
}
