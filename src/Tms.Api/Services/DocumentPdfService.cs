using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Identity;
using Tms.Modules.Loads;

namespace Tms.Api.Services;

/// <summary>
/// Renders the three legal/billing documents §11.6 names (Invoice, CreditNote,
/// LoadConfirmation) to PDF bytes via QuestPDF — pure C#, no external process. Every
/// method here is called exactly once per document, at the moment it moves to Issued
/// (InvoicesController.Issue, CreditNotesController.Issue, LoadsController's own
/// EnsureLoadConfirmationAsync) — never re-rendered afterward, matching §11.6's own
/// "never re-rendered from current data" rule. <see cref="Company"/> is the shared
/// letterhead every one of these draws from (its own doc comment already says so) —
/// RenderDocument below is the one place that letterhead, the page setup, and the
/// footer are actually laid out; each public method here only ever builds its own
/// content column.
/// </summary>
public class DocumentPdfService
{
    public byte[] RenderInvoice(Invoice invoice, Client client, Company company, string currencyCode) =>
        RenderDocument(company, "TAX INVOICE", col =>
        {
            col.Spacing(8);
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Bill to").SemiBold();
                    c.Item().Text(client.Name);
                    c.Item().Text($"Reg. {client.RegistrationNo}");
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Text($"Invoice number: {invoice.InvoiceNumber}");
                    c.Item().Text($"Issue date: {invoice.IssueDate:yyyy-MM-dd}");
                    c.Item().Text($"Due date: {invoice.DueDate:yyyy-MM-dd}");
                });
            });

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1.5f);
                    columns.RelativeColumn(1.5f);
                });

                table.Header(header =>
                {
                    header.Cell().Text("Description").Bold();
                    header.Cell().AlignRight().Text("Qty").Bold();
                    header.Cell().AlignRight().Text("Rate").Bold();
                    header.Cell().AlignRight().Text("Amount").Bold();
                });

                foreach (var line in invoice.Lines)
                {
                    table.Cell().Text(line.Description);
                    table.Cell().AlignRight().Text(line.Quantity.ToString("0.##"));
                    table.Cell().AlignRight().Text($"{line.Rate:0.00} {currencyCode}");
                    table.Cell().AlignRight().Text($"{line.Amount:0.00} {currencyCode}");
                }
            });

            col.Item().AlignRight().Column(c =>
            {
                c.Item().Text($"Subtotal: {invoice.TotalExVat:0.00} {currencyCode}");
                c.Item().Text($"VAT: {invoice.VatAmount:0.00} {currencyCode}");
                c.Item().Text($"Total: {invoice.TotalIncVat:0.00} {currencyCode}").Bold();
            });

            col.Item().PaddingTop(20).Text($"Banking details: {company.BankingDetails}");
        });

    public byte[] RenderCreditNote(CreditNote creditNote, Client client, Company company, string currencyCode, string? originalInvoiceNumber) =>
        RenderDocument(company, "CREDIT NOTE", col =>
        {
            col.Spacing(8);
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("To").SemiBold();
                    c.Item().Text(client.Name);
                    c.Item().Text($"Reg. {client.RegistrationNo}");
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Text($"Credit note number: {creditNote.CreditNoteNumber}");
                    c.Item().Text($"Issue date: {creditNote.IssueDate:yyyy-MM-dd}");
                    if (originalInvoiceNumber is not null)
                        c.Item().Text($"Corrects invoice: {originalInvoiceNumber}");
                });
            });

            col.Item().Text($"Reason: {creditNote.Reason}");

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(1.5f);
                });

                table.Header(header =>
                {
                    header.Cell().Text("Description").Bold();
                    header.Cell().AlignRight().Text("Amount").Bold();
                });

                foreach (var line in creditNote.Lines)
                {
                    table.Cell().Text(line.Description);
                    table.Cell().AlignRight().Text($"{line.Amount:0.00} {currencyCode}");
                }
            });

            col.Item().AlignRight().Text($"Total: {creditNote.TotalAmount:0.00} {currencyCode}").Bold();
        });

    public byte[] RenderLoadConfirmation(
        LoadConfirmation confirmation, Subcontractor subcontractor, Company company,
        string originLocationName, string destinationLocationName,
        IReadOnlyList<(string CurrencyCode, decimal Amount)> buyTotals) =>
        RenderDocument(company, "LOAD CONFIRMATION", col =>
        {
            col.Spacing(8);
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Carrier").SemiBold();
                    c.Item().Text(subcontractor.Name);
                    c.Item().Text($"Reg. {subcontractor.RegistrationNo}");
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Text($"Document number: {confirmation.DocumentNumber}");
                    c.Item().Text($"Issued: {confirmation.IssuedDate:yyyy-MM-dd}");
                });
            });

            col.Item().Text($"Route: {originLocationName} → {destinationLocationName}");

            col.Item().Text("Agreed rate").SemiBold();
            if (buyTotals.Count == 0)
            {
                col.Item().Text("No commodity lines captured yet.");
            }
            else
            {
                foreach (var (currencyCode, amount) in buyTotals)
                    col.Item().Text($"{amount:0.00} {currencyCode}");
            }
        });

    /// <summary>The one place page size/margin/text style, the letterhead header, and the page-number footer are laid out — every document here shares all three, differing only in the title and the content column each public method builds.</summary>
    private static byte[] RenderDocument(Company company, string documentTitle, Action<ColumnDescriptor> buildContent)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(company.TradingName ?? company.LegalName).FontSize(16).Bold();
                    col.Item().Text($"Reg. {company.RegistrationNo} · VAT {company.VatNumber}");
                    col.Item().Text(company.PhysicalAddress);
                    col.Item().PaddingTop(10).Text(documentTitle).FontSize(14).Bold();
                });

                page.Content().PaddingTop(10).Column(buildContent);

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf();
    }
}
