using Microsoft.AspNetCore.Mvc;

namespace Tms.Api.Controllers;

/// <summary>
/// The one tail every "return a document's own rendered PDF" action shares (§11.6) —
/// InvoicesController.GetPdf, CreditNotesController.GetPdf, and
/// LegsController.GetConfirmationPdf each fetch their own entity and apply their own
/// tenant/portal access check first (genuinely different per entity, so left alone),
/// but all three end the same way: a document that hasn't been rendered yet (still
/// Draft, or issued before PDF rendering existed) 404s, otherwise the bytes go out as
/// the response.
/// </summary>
public static class PdfResponseExtensions
{
    public static IActionResult PdfFileOrNotFound(this ControllerBase controller, byte[]? pdfContent, string fileName) =>
        pdfContent is null ? controller.NotFound() : controller.File(pdfContent, "application/pdf", fileName);
}
