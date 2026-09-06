using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// PDF rendering for the three §11.6 legal/billing documents (Invoice, CreditNote,
/// LoadConfirmation) via QuestPDF (§10.1/§8.2/§11.6) — rendered exactly once, at Issue/
/// Allocate time, and retrieved via each document's own GET .../pdf action. Asserts on
/// the actual returned bytes (the "%PDF-" magic header every valid PDF starts with),
/// not just a 200 — a wrong content type or empty body would pass a status-code-only
/// check but produce a file nothing could actually open.
/// </summary>
[Collection(StaffTestCollection.Name)]
public class DocumentPdfTests
{
    private readonly StaffTestFixture _fx;

    public DocumentPdfTests(StaffTestFixture fx) => _fx = fx;

    private static void AssertLooksLikeAPdf(HttpResponseMessage response, byte[] body)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.True(body.Length > 100, "PDF body is suspiciously small.");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(body, 0, 5));
    }

    [Fact]
    public async Task Issuing_an_invoice_renders_a_retrievable_pdf_and_a_draft_ones_pdf_404s()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"PDF-{Guid.NewGuid():N}", sellRatePerUnit: 500);
        await _fx.DeliverLegAsync(loadId, legId);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" })).EnsureSuccessStatusCode();

        var generateResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/invoices/generate", new { clientId });
        generateResponse.EnsureSuccessStatusCode();
        var invoice = await generateResponse.Content.ReadFromJsonAsync<InvoiceLike>();

        // Draft — nothing rendered yet.
        var draftPdfResponse = await _fx.StaffClient.GetAsync($"/api/v1/invoices/{invoice!.Id}/pdf");
        Assert.Equal(HttpStatusCode.NotFound, draftPdfResponse.StatusCode);

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/issue", new { })).EnsureSuccessStatusCode();

        var issued = await _fx.StaffClient.GetFromJsonAsync<InvoiceLike>($"/api/v1/invoices/{invoice.Id}");
        Assert.NotNull(issued!.PdfUrl);

        var pdfResponse = await _fx.StaffClient.GetAsync($"/api/v1/invoices/{invoice.Id}/pdf");
        AssertLooksLikeAPdf(pdfResponse, await pdfResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Issuing_a_standalone_credit_note_renders_a_retrievable_pdf()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/credit-notes", new
        {
            clientId,
            reason = "PDF rendering test",
            lines = new[] { new { description = "Goodwill adjustment", amount = 75m } }
        });
        createResponse.EnsureSuccessStatusCode();
        var creditNote = await createResponse.Content.ReadFromJsonAsync<IdLike>();

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/credit-notes/{creditNote!.Id}/issue", new { })).EnsureSuccessStatusCode();

        var pdfResponse = await _fx.StaffClient.GetAsync($"/api/v1/credit-notes/{creditNote.Id}/pdf");
        AssertLooksLikeAPdf(pdfResponse, await pdfResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_credit_note_correcting_an_invoice_renders_a_pdf_that_names_the_original_invoice_number()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"PDFCN-{Guid.NewGuid():N}", sellRatePerUnit: 200);
        await _fx.DeliverLegAsync(loadId, legId);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" })).EnsureSuccessStatusCode();

        var generateResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/invoices/generate", new { clientId });
        generateResponse.EnsureSuccessStatusCode();
        var invoice = await generateResponse.Content.ReadFromJsonAsync<InvoiceLike>();
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/invoices/{invoice!.Id}/issue", new { })).EnsureSuccessStatusCode();

        var invoiceDetail = await _fx.StaffClient.GetFromJsonAsync<InvoiceDetailLike>($"/api/v1/invoices/{invoice.Id}");
        var invoiceLineId = invoiceDetail!.Lines[0].Id;

        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/credit-notes", new
        {
            clientId,
            originalInvoiceId = invoice.Id,
            reason = "Rate dispute",
            lines = new[] { new { invoiceLineId, description = "Partial credit", amount = 50m } }
        });
        createResponse.EnsureSuccessStatusCode();
        var creditNote = await createResponse.Content.ReadFromJsonAsync<IdLike>();
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/credit-notes/{creditNote!.Id}/issue", new { })).EnsureSuccessStatusCode();

        var pdfResponse = await _fx.StaffClient.GetAsync($"/api/v1/credit-notes/{creditNote.Id}/pdf");
        var body = await pdfResponse.Content.ReadAsByteArrayAsync();
        AssertLooksLikeAPdf(pdfResponse, body);
    }

    [Fact]
    public async Task Allocating_a_subcontracted_leg_renders_a_retrievable_load_confirmation_pdf()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var (_, legId) = await _fx.CreateBookedLoadWithLegAsync(
            clientId, $"PDFLC-{Guid.NewGuid():N}", subcontractorId: subcontractorId, buyRatePerUnit: 300);

        var confirmation = await _fx.StaffClient.GetFromJsonAsync<ConfirmationLike>($"/api/v1/legs/{legId}/confirmation");
        Assert.NotNull(confirmation!.PdfUrl);

        var pdfResponse = await _fx.StaffClient.GetAsync($"/api/v1/legs/{legId}/confirmation/pdf");
        AssertLooksLikeAPdf(pdfResponse, await pdfResponse.Content.ReadAsByteArrayAsync());
    }

    private sealed record InvoiceLike(Guid Id, int Status, decimal TotalExVat, string? PdfUrl);
    private sealed record InvoiceLineLike(Guid Id);
    private sealed record InvoiceDetailLike(Guid Id, IReadOnlyList<InvoiceLineLike> Lines);
    private sealed record IdLike(Guid Id);
    private sealed record ConfirmationLike(Guid Id, string? PdfUrl);
}
