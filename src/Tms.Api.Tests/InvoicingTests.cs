using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>Sell-side invoicing (§10.1) — the PodReceived gate, the double-bill unique-index race, and Issue/Void.</summary>
[Collection(StaffTestCollection.Name)]
public class InvoicingTests
{
    private readonly StaffTestFixture _fx;

    public InvoicingTests(StaffTestFixture fx) => _fx = fx;

    private async Task<Guid> CreatePodReceivedLoadClientAsync(decimal sellRatePerUnit = 500)
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"INV-{Guid.NewGuid():N}", sellRatePerUnit);
        await _fx.DeliverLegAsync(loadId, legId);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" })).EnsureSuccessStatusCode();
        return clientId;
    }

    [Fact]
    public async Task Generate_creates_a_draft_invoice_with_the_correct_total_for_a_PodReceived_load()
    {
        var clientId = await CreatePodReceivedLoadClientAsync(sellRatePerUnit: 500);

        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/invoices/generate", new { clientId });
        response.EnsureSuccessStatusCode();
        var invoice = await response.Content.ReadFromJsonAsync<InvoiceLike>();

        Assert.Equal(0, invoice!.Status); // Draft
        Assert.Equal(500m, invoice.TotalExVat);
    }

    [Fact]
    public async Task Concurrent_generate_calls_never_double_bill_the_same_sell_line()
    {
        var clientId = await CreatePodReceivedLoadClientAsync(sellRatePerUnit: 500);

        var (client1, client2) = _fx.GetRaceClients();
        var body = new { clientId };

        var results = await Task.WhenAll(
            client1.PostAsJsonAsync("/api/v1/invoices/generate", body),
            client2.PostAsJsonAsync("/api/v1/invoices/generate", body));

        var totalBilled = 0m;
        foreach (var r in results)
        {
            if (r.StatusCode != HttpStatusCode.Created) continue;
            var invoice = await r.Content.ReadFromJsonAsync<InvoiceLike>();
            totalBilled += invoice!.TotalExVat;
        }

        // Whether both calls succeed (each picking up a disjoint, empty-after-the-fact
        // set) or one 409s, the same R500 line must never appear on two invoices.
        Assert.Equal(500m, totalBilled);
    }

    [Fact]
    public async Task Issue_then_void_lifecycle()
    {
        var clientId = await CreatePodReceivedLoadClientAsync();
        var generateResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/invoices/generate", new { clientId });
        generateResponse.EnsureSuccessStatusCode();
        var invoice = await generateResponse.Content.ReadFromJsonAsync<InvoiceLike>();

        var issueResponse = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/invoices/{invoice!.Id}/issue", new { });
        Assert.Equal(HttpStatusCode.NoContent, issueResponse.StatusCode);

        // Void is Draft-only, per §10.1 — an Issued invoice can no longer be voided directly.
        var voidResponse = await _fx.StaffClient.PostAsync($"/api/v1/invoices/{invoice.Id}/void", null);
        Assert.Equal(HttpStatusCode.Conflict, voidResponse.StatusCode);
    }

    private sealed record InvoiceLike(Guid Id, int Status, decimal TotalExVat);
}
