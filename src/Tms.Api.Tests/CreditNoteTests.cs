using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>Sell-side credit notes (§10.1) — the sibling-line cap enforcement across a single request, its concurrent-race regression, and AR netting on Issue.</summary>
[Collection(StaffTestCollection.Name)]
public class CreditNoteTests
{
    private readonly StaffTestFixture _fx;

    public CreditNoteTests(StaffTestFixture fx) => _fx = fx;

    private async Task<(Guid ClientId, Guid InvoiceId, Guid InvoiceLineId, decimal LineAmount)> CreateIssuedInvoiceAsync(decimal sellRatePerUnit)
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"CN-{Guid.NewGuid():N}", sellRatePerUnit);
        await _fx.DeliverLegAsync(loadId, legId);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" })).EnsureSuccessStatusCode();

        var generateResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/invoices/generate", new { clientId });
        generateResponse.EnsureSuccessStatusCode();
        var invoice = await generateResponse.Content.ReadFromJsonAsync<InvoiceLike>();

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/invoices/{invoice!.Id}/issue", new { })).EnsureSuccessStatusCode();

        var line = invoice.Lines.Single();
        return (clientId, invoice.Id, line.Id, line.Amount);
    }

    [Fact]
    public async Task Crediting_more_than_the_invoice_lines_amount_across_sibling_lines_is_rejected()
    {
        var (clientId, invoiceId, lineId, lineAmount) = await CreateIssuedInvoiceAsync(500);
        var half = lineAmount / 2;

        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/credit-notes", new
        {
            clientId,
            originalInvoiceId = invoiceId,
            reason = "Test over-cap",
            lines = new[]
            {
                new { invoiceLineId = lineId, description = "Line A", amount = half + 50 },
                new { invoiceLineId = lineId, description = "Line B", amount = half + 50 }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_exact_cap_split_across_sibling_lines_succeeds()
    {
        var (clientId, invoiceId, lineId, lineAmount) = await CreateIssuedInvoiceAsync(500);
        var half = lineAmount / 2;

        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/credit-notes", new
        {
            clientId,
            originalInvoiceId = invoiceId,
            reason = "Test exact cap",
            lines = new[]
            {
                new { invoiceLineId = lineId, description = "Line A", amount = half },
                new { invoiceLineId = lineId, description = "Line B", amount = half }
            }
        });

        response.EnsureSuccessStatusCode();
        var creditNote = await response.Content.ReadFromJsonAsync<CreditNoteLike>();
        Assert.Equal(lineAmount, creditNote!.TotalAmount);
    }

    [Fact]
    public async Task Concurrent_creates_exceeding_the_cap_together_resolve_to_exactly_one_success()
    {
        var (clientId, invoiceId, lineId, lineAmount) = await CreateIssuedInvoiceAsync(1000);
        var sixtyPercent = lineAmount * 0.6m;

        var (client1, client2) = _fx.GetRaceClients();
        object Body() => new
        {
            clientId,
            originalInvoiceId = invoiceId,
            reason = "Test race",
            lines = new[] { new { invoiceLineId = lineId, description = "Line", amount = sixtyPercent } }
        };

        var results = await Task.WhenAll(
            client1.PostAsJsonAsync("/api/v1/credit-notes", Body()),
            client2.PostAsJsonAsync("/api/v1/credit-notes", Body()));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Issuing_a_credit_note_reduces_AR_outstanding_by_its_total()
    {
        var (clientId, invoiceId, lineId, lineAmount) = await CreateIssuedInvoiceAsync(500);

        var beforeStatus = await _fx.StaffClient.GetFromJsonAsync<CreditStatusLike>($"/api/v1/clients/{clientId}/credit-status");
        Assert.Equal(lineAmount, beforeStatus!.ArOutstanding);

        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/credit-notes", new
        {
            clientId,
            originalInvoiceId = invoiceId,
            reason = "Goodwill",
            lines = new[] { new { invoiceLineId = lineId, description = "Line", amount = 200m } }
        });
        createResponse.EnsureSuccessStatusCode();
        var creditNote = await createResponse.Content.ReadFromJsonAsync<CreditNoteLike>();

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/credit-notes/{creditNote!.Id}/issue", new { })).EnsureSuccessStatusCode();

        var afterStatus = await _fx.StaffClient.GetFromJsonAsync<CreditStatusLike>($"/api/v1/clients/{clientId}/credit-status");
        Assert.Equal(lineAmount - 200m, afterStatus!.ArOutstanding);
    }

    private sealed record InvoiceLike(Guid Id, List<InvoiceLineLike> Lines);
    private sealed record InvoiceLineLike(Guid Id, decimal Amount);
    private sealed record CreditNoteLike(Guid Id, decimal TotalAmount);
    private sealed record CreditStatusLike(decimal ArOutstanding);
}
