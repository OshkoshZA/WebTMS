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

    private sealed record LoadLike(Guid Id);
    private sealed record ClientOwnedLike(Guid Id, Guid ClientId);
}
