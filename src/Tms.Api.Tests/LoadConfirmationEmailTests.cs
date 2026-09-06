using System.Net.Http.Json;
using MimeKit;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// "Issued as a PDF — emailed to the subcontractor" (§8.2, §11.6) — LoadsController's
/// own EnsureLoadConfirmationAsync/SendLoadConfirmationEmailAsync, exercised against a
/// real local SMTP server (EmailTestFixture's own SmtpTestReceiver) rather than a
/// mocked IEmailSender, the same no-mocks approach as every other integration test in
/// this suite. The send is awaited inline before the triggering HTTP call returns, so
/// no polling is needed — by the time that call completes, the message is already in
/// Receiver.Messages or it never will be.
/// </summary>
[Collection(EmailTestCollection.Name)]
public class LoadConfirmationEmailTests
{
    private readonly EmailTestFixture _fx;

    public LoadConfirmationEmailTests(EmailTestFixture fx) => _fx = fx;

    [Fact]
    public async Task Allocating_a_subcontracted_leg_emails_the_subcontractors_active_portal_contact_with_the_pdf_attached()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var subcontractorId = await _fx.CreateSubcontractorAsync(suffix);
        var roleId = await _fx.CreatePortalRoleAsync(suffix);
        var contactEmail = $"email-test-{suffix}@example.test";
        await _fx.CreateSubcontractorContactAsync(subcontractorId, contactEmail, roleId);

        await _fx.CreateSubcontractedLegAsync(subcontractorId, $"EMAIL-{suffix}");

        var message = Assert.Single(_fx.Receiver.Messages, m => m.To.Mailboxes.Any(a => a.Address == contactEmail));
        Assert.Contains("Load Confirmation", message.Subject);
        var attachment = Assert.IsType<MimePart>(Assert.Single(message.Attachments));
        Assert.EndsWith(".pdf", attachment.FileName ?? "");
        Assert.True(attachment.Content!.Stream.Length > 100, "Attached PDF is suspiciously small.");
    }

    [Fact]
    public async Task A_deactivated_portal_contact_is_never_emailed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var subcontractorId = await _fx.CreateSubcontractorAsync(suffix);
        var roleId = await _fx.CreatePortalRoleAsync(suffix);
        var contactEmail = $"email-test-deactivated-{suffix}@example.test";
        await _fx.CreateSubcontractorContactAsync(subcontractorId, contactEmail, roleId);

        var listResponse = await _fx.StaffClient.GetFromJsonAsync<List<ContactLike>>($"/api/v1/subcontractors/{subcontractorId}/contacts");
        var contact = listResponse!.Single(c => c.Email == contactEmail);
        (await _fx.StaffClient.PostAsync($"/api/v1/subcontractors/{subcontractorId}/contacts/{contact.Id}/deactivate", null)).EnsureSuccessStatusCode();

        await _fx.CreateSubcontractedLegAsync(subcontractorId, $"EMAILDEACT-{suffix}");

        Assert.DoesNotContain(_fx.Receiver.Messages, m => m.To.Mailboxes.Any(a => a.Address == contactEmail));
    }

    private sealed record ContactLike(Guid Id, string Email);
}
