using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// Every client-scoped action, tested from all three angles: the Customer Portal
/// contact's own data (must succeed), the SAME contact against a different party's
/// data (must 403), and a Supplier Portal contact against client-scoped data (must
/// 403 — the "wrong TYPE" case).
/// </summary>
[Collection(PortalTestCollection.Name)]
public class CustomerPortalBoundaryTests
{
    private readonly PortalTestFixture _fixture;

    public CustomerPortalBoundaryTests(PortalTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Loads_list_returns_only_own_loads_and_rejects_subcontractor_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var response = await mine.GetAsync("/api/v1/loads");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var loads = await response.Content.ReadFromJsonAsync<List<LoadLike>>();
        Assert.Contains(loads!, l => l.Id == _fixture.ClientLoadId);
        Assert.DoesNotContain(loads!, l => l.Id == _fixture.OtherClientLoadId);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var asSubResponse = await wrongType.GetAsync("/api/v1/loads");
        Assert.Equal(HttpStatusCode.Forbidden, asSubResponse.StatusCode);
    }

    [Fact]
    public async Task Load_get_and_tracking_reject_other_clients_load_and_subcontractor_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);

        var ownGet = await mine.GetAsync($"/api/v1/loads/{_fixture.ClientLoadId}");
        Assert.Equal(HttpStatusCode.OK, ownGet.StatusCode);
        var ownTracking = await mine.GetAsync($"/api/v1/loads/{_fixture.ClientLoadId}/tracking");
        Assert.Equal(HttpStatusCode.OK, ownTracking.StatusCode);

        var otherGet = await mine.GetAsync($"/api/v1/loads/{_fixture.OtherClientLoadId}");
        Assert.Equal(HttpStatusCode.Forbidden, otherGet.StatusCode);
        var otherTracking = await mine.GetAsync($"/api/v1/loads/{_fixture.OtherClientLoadId}/tracking");
        Assert.Equal(HttpStatusCode.Forbidden, otherTracking.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var asSubGet = await wrongType.GetAsync($"/api/v1/loads/{_fixture.ClientLoadId}");
        Assert.Equal(HttpStatusCode.Forbidden, asSubGet.StatusCode);
    }

    [Fact]
    public async Task SelfServiceBooking_succeeds_for_own_client_rejects_other_client_and_subcontractor_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);

        var ownBooking = await mine.PostAsJsonAsync("/api/v1/loads", new
        {
            clientId = _fixture.ClientId,
            referenceNo = $"SELF-SERVICE-{Guid.NewGuid():N}",
            loadTypeId = Guid.Parse("6C48E708-7D45-4381-881D-16CC9E39ED24")
        });
        Assert.Equal(HttpStatusCode.Created, ownBooking.StatusCode);

        var otherClientBooking = await mine.PostAsJsonAsync("/api/v1/loads", new
        {
            clientId = _fixture.OtherClientId,
            referenceNo = $"SELF-SERVICE-{Guid.NewGuid():N}",
            loadTypeId = Guid.Parse("6C48E708-7D45-4381-881D-16CC9E39ED24")
        });
        Assert.Equal(HttpStatusCode.Forbidden, otherClientBooking.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var asSubBooking = await wrongType.PostAsJsonAsync("/api/v1/loads", new
        {
            clientId = _fixture.ClientId,
            referenceNo = $"SELF-SERVICE-{Guid.NewGuid():N}",
            loadTypeId = Guid.Parse("6C48E708-7D45-4381-881D-16CC9E39ED24")
        });
        Assert.Equal(HttpStatusCode.Forbidden, asSubBooking.StatusCode);
    }

    [Fact]
    public async Task CreditStatus_rejects_other_client_and_subcontractor_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var ownResponse = await mine.GetAsync($"/api/v1/clients/{_fixture.ClientId}/credit-status");
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);

        var otherResponse = await mine.GetAsync($"/api/v1/clients/{_fixture.OtherClientId}/credit-status");
        Assert.Equal(HttpStatusCode.Forbidden, otherResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var asSubResponse = await wrongType.GetAsync($"/api/v1/clients/{_fixture.ClientId}/credit-status");
        Assert.Equal(HttpStatusCode.Forbidden, asSubResponse.StatusCode);
    }

    [Fact]
    public async Task Client_invoices_and_credit_notes_reject_other_client_and_subcontractor_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        Assert.Equal(HttpStatusCode.OK, (await mine.GetAsync($"/api/v1/clients/{_fixture.ClientId}/invoices")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mine.GetAsync($"/api/v1/clients/{_fixture.ClientId}/credit-notes")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await mine.GetAsync($"/api/v1/clients/{_fixture.OtherClientId}/invoices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.GetAsync($"/api/v1/clients/{_fixture.OtherClientId}/credit-notes")).StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongType.GetAsync($"/api/v1/clients/{_fixture.ClientId}/invoices")).StatusCode);
    }

    [Fact]
    public async Task Top_level_invoice_and_creditnote_lists_are_pinned_to_own_client_and_reject_subcontractor_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);

        // Passing the OTHER client's id must not widen the result — pinned server-side.
        var invoicesResponse = await mine.GetAsync($"/api/v1/invoices?clientId={_fixture.OtherClientId}");
        Assert.Equal(HttpStatusCode.OK, invoicesResponse.StatusCode);
        var invoices = await invoicesResponse.Content.ReadFromJsonAsync<List<ClientOwnedLike>>();
        Assert.All(invoices!, i => Assert.Equal(_fixture.ClientId, i.ClientId));

        var creditNotesResponse = await mine.GetAsync($"/api/v1/credit-notes?clientId={_fixture.OtherClientId}");
        Assert.Equal(HttpStatusCode.OK, creditNotesResponse.StatusCode);
        var creditNotes = await creditNotesResponse.Content.ReadFromJsonAsync<List<ClientOwnedLike>>();
        Assert.All(creditNotes!, c => Assert.Equal(_fixture.ClientId, c.ClientId));

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongType.GetAsync("/api/v1/invoices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongType.GetAsync("/api/v1/credit-notes")).StatusCode);
    }

    [Fact]
    public async Task ClientContacts_list_rejects_other_client_and_subcontractor_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var ownResponse = await mine.GetAsync($"/api/v1/clients/{_fixture.ClientId}/contacts");
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);

        var otherResponse = await mine.GetAsync($"/api/v1/clients/{_fixture.OtherClientId}/contacts");
        Assert.Equal(HttpStatusCode.Forbidden, otherResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var asSubResponse = await wrongType.GetAsync($"/api/v1/clients/{_fixture.ClientId}/contacts");
        Assert.Equal(HttpStatusCode.Forbidden, asSubResponse.StatusCode);
    }

    /// <summary>Regression test for the fix closing a real gap this pass found: a portal caller's invoice list previously had no status filter at all, so a Draft invoice — an internal working document nobody has actually issued yet — was fully visible to the client it belongs to.</summary>
    [Fact]
    public async Task Draft_invoice_is_hidden_from_the_portal_until_issued()
    {
        var invoiceId = await _fixture.GenerateDraftInvoiceForOwnClientAsync($"PORTAL-DRAFT-{Guid.NewGuid():N}");

        using var mine = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);

        var topLevelBefore = await mine.GetFromJsonAsync<List<IdLike>>("/api/v1/invoices");
        Assert.DoesNotContain(topLevelBefore!, i => i.Id == invoiceId);

        var clientScopedBefore = await mine.GetFromJsonAsync<List<IdLike>>($"/api/v1/clients/{_fixture.ClientId}/invoices");
        Assert.DoesNotContain(clientScopedBefore!, i => i.Id == invoiceId);

        var getBefore = await mine.GetAsync($"/api/v1/invoices/{invoiceId}");
        Assert.Equal(HttpStatusCode.Forbidden, getBefore.StatusCode);

        (await _fixture.StaffClient.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/issue", new { })).EnsureSuccessStatusCode();

        var topLevelAfter = await mine.GetFromJsonAsync<List<IdLike>>("/api/v1/invoices");
        Assert.Contains(topLevelAfter!, i => i.Id == invoiceId);

        var clientScopedAfter = await mine.GetFromJsonAsync<List<IdLike>>($"/api/v1/clients/{_fixture.ClientId}/invoices");
        Assert.Contains(clientScopedAfter!, i => i.Id == invoiceId);

        var getAfter = await mine.GetAsync($"/api/v1/invoices/{invoiceId}");
        Assert.Equal(HttpStatusCode.OK, getAfter.StatusCode);
    }

    /// <summary>Same fix, the credit-note side — a standalone Draft note needs no load/debrief setup, so this covers CreditNotesController's own equivalent filter directly.</summary>
    [Fact]
    public async Task Draft_credit_note_is_hidden_from_the_portal_until_issued()
    {
        var createResponse = await _fixture.StaffClient.PostAsJsonAsync("/api/v1/credit-notes", new
        {
            clientId = _fixture.ClientId,
            reason = "Portal Draft-visibility regression test",
            lines = new[] { new { description = "Goodwill adjustment", amount = 50m } }
        });
        createResponse.EnsureSuccessStatusCode();
        var creditNoteId = (await createResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;

        using var mine = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);

        var topLevelBefore = await mine.GetFromJsonAsync<List<IdLike>>("/api/v1/credit-notes");
        Assert.DoesNotContain(topLevelBefore!, c => c.Id == creditNoteId);

        var clientScopedBefore = await mine.GetFromJsonAsync<List<IdLike>>($"/api/v1/clients/{_fixture.ClientId}/credit-notes");
        Assert.DoesNotContain(clientScopedBefore!, c => c.Id == creditNoteId);

        (await _fixture.StaffClient.PostAsJsonAsync($"/api/v1/credit-notes/{creditNoteId}/issue", new { })).EnsureSuccessStatusCode();

        var topLevelAfter = await mine.GetFromJsonAsync<List<IdLike>>("/api/v1/credit-notes");
        Assert.Contains(topLevelAfter!, c => c.Id == creditNoteId);

        var clientScopedAfter = await mine.GetFromJsonAsync<List<IdLike>>($"/api/v1/clients/{_fixture.ClientId}/credit-notes");
        Assert.Contains(clientScopedAfter!, c => c.Id == creditNoteId);
    }

    private sealed record LoadLike(Guid Id);
    private sealed record ClientOwnedLike(Guid Id, Guid ClientId);
    private sealed record IdLike(Guid Id);
}
