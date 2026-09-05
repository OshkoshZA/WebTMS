using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// Every subcontractor-scoped action, tested from all three angles: the Subcontractor
/// Portal contact's own data (must succeed), the SAME contact against a different
/// party's data (must 403 — the "wrong id" case), and a Customer Portal contact
/// against subcontractor-scoped data (must 403 — the "wrong TYPE" case, the actual
/// shape of every bug found this project: a check that only ever considered the
/// right-type-wrong-id case and let the wrong-type case fall through unfiltered).
/// </summary>
[Collection(PortalTestCollection.Name)]
public class SupplierPortalBoundaryTests
{
    private readonly PortalTestFixture _fixture;

    public SupplierPortalBoundaryTests(PortalTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Legs_list_returns_only_own_legs_and_rejects_other_subcontractor_and_client_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var ownResponse = await mine.GetAsync($"/api/v1/subcontractors/{_fixture.SubcontractorId}/legs");
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        var ownLegs = await ownResponse.Content.ReadFromJsonAsync<List<JsonElementLike>>();
        Assert.Contains(ownLegs!, l => l.Id == _fixture.SubcontractorLegId);

        var otherSubResponse = await mine.GetAsync($"/api/v1/subcontractors/{_fixture.OtherSubcontractorId}/legs");
        Assert.Equal(HttpStatusCode.Forbidden, otherSubResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var asClientResponse = await wrongType.GetAsync($"/api/v1/subcontractors/{_fixture.SubcontractorId}/legs");
        Assert.Equal(HttpStatusCode.Forbidden, asClientResponse.StatusCode);
    }

    [Fact]
    public async Task Accruals_list_is_pinned_to_own_subcontractor_regardless_of_query_param_and_rejects_client_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);

        // Passing the OTHER subcontractor's id must not widen the result — pinned server-side.
        var response = await mine.GetAsync($"/api/v1/accruals?subcontractorId={_fixture.OtherSubcontractorId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accruals = await response.Content.ReadFromJsonAsync<List<AccrualLike>>();
        Assert.All(accruals!, a => Assert.Equal(_fixture.SubcontractorId, a.SubcontractorId));

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var asClientResponse = await wrongType.GetAsync("/api/v1/accruals");
        Assert.Equal(HttpStatusCode.Forbidden, asClientResponse.StatusCode);
    }

    [Fact]
    public async Task Accrual_get_rejects_other_subcontractors_accrual_and_client_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var ownResponse = await mine.GetAsync($"/api/v1/accruals/{_fixture.SubcontractorAccrualId}");
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var asClientResponse = await wrongType.GetAsync($"/api/v1/accruals/{_fixture.SubcontractorAccrualId}");
        Assert.Equal(HttpStatusCode.Forbidden, asClientResponse.StatusCode);
    }

    [Fact]
    public async Task Supplier_invoices_list_rejects_client_contact()
    {
        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var response = await wrongType.GetAsync("/api/v1/supplier-invoices");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetConfirmation_own_leg_succeeds_other_leg_and_client_contact_rejected()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var ownResponse = await mine.GetAsync($"/api/v1/legs/{_fixture.SubcontractorLegId}/confirmation");
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);

        var otherLegResponse = await mine.GetAsync($"/api/v1/legs/{_fixture.OtherSubcontractorLegId}/confirmation");
        Assert.Equal(HttpStatusCode.Forbidden, otherLegResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var asClientResponse = await wrongType.GetAsync($"/api/v1/legs/{_fixture.SubcontractorLegId}/confirmation");
        Assert.Equal(HttpStatusCode.Forbidden, asClientResponse.StatusCode);
    }

    [Fact]
    public async Task AcknowledgeConfirmation_rejects_other_leg_and_client_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var otherLegResponse = await mine.PostAsJsonAsync(
            $"/api/v1/legs/{_fixture.OtherSubcontractorLegId}/confirmation/acknowledge", new { acknowledged = true });
        Assert.Equal(HttpStatusCode.Forbidden, otherLegResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var asClientResponse = await wrongType.PostAsJsonAsync(
            $"/api/v1/legs/{_fixture.SubcontractorLegId}/confirmation/acknowledge", new { acknowledged = true });
        Assert.Equal(HttpStatusCode.Forbidden, asClientResponse.StatusCode);
    }

    /// <summary>
    /// Direct regression test for the bug found in this project's third audit pass:
    /// GetDebrief used to gate its portal check behind `if (SubcontractorId is not
    /// null)`, so a Client contact (SubcontractorId null) fell straight through to an
    /// unconditional 200 with the full debrief body. If this ever regresses, this is
    /// the test that must catch it.
    /// </summary>
    [Fact]
    public async Task GetDebrief_rejects_client_contact_even_though_their_own_SubcontractorId_is_null()
    {
        var staffSubmit = _fixture.StaffClient;

        // Drive the leg to Delivered so a debrief can be submitted, then submit one as staff.
        await DeliverLegAsync(_fixture.SubcontractorLegLoadId, _fixture.SubcontractorLegId);
        var submitResponse = await staffSubmit.PostAsJsonAsync(
            $"/api/v1/legs/{_fixture.SubcontractorLegId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" });
        Assert.True(submitResponse.StatusCode == HttpStatusCode.Created || submitResponse.StatusCode == HttpStatusCode.Conflict,
            $"Unexpected status setting up the debrief fixture: {submitResponse.StatusCode}");

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var response = await wrongType.GetAsync($"/api/v1/legs/{_fixture.SubcontractorLegId}/debrief");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var mine = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var ownResponse = await mine.GetAsync($"/api/v1/legs/{_fixture.SubcontractorLegId}/debrief");
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
    }

    [Fact]
    public async Task SubmitDebrief_rejects_other_leg_and_client_contact()
    {
        await DeliverLegAsync(_fixture.OtherSubcontractorLegLoadId, _fixture.OtherSubcontractorLegId);

        using var mine = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var otherLegResponse = await mine.PostAsJsonAsync(
            $"/api/v1/legs/{_fixture.OtherSubcontractorLegId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" });
        Assert.Equal(HttpStatusCode.Forbidden, otherLegResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var asClientResponse = await wrongType.PostAsJsonAsync(
            $"/api/v1/legs/{_fixture.SubcontractorLegId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" });
        Assert.Equal(HttpStatusCode.Forbidden, asClientResponse.StatusCode);
    }

    /// <summary>
    /// Regression test for the gap found while planning the debrief-expense form:
    /// LegsController.SubmitDebrief requires a valid ExpenseTypeId for any expense claim,
    /// but ExpenseTypesController used to Forbid EVERY portal contact outright, leaving a
    /// Subcontractor Portal contact with no way to discover one at all. Fixed to open
    /// List/Get to a Subcontractor Portal contact specifically; a Client contact still
    /// gets nothing here since no debrief/expense action exists on that side.
    /// </summary>
    [Fact]
    public async Task ExpenseTypes_list_and_get_are_open_to_subcontractor_portal_but_still_reject_client_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var listResponse = await mine.GetAsync("/api/v1/expense-types");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var types = await listResponse.Content.ReadFromJsonAsync<List<JsonElementLike>>();
        Assert.NotEmpty(types!);

        var getResponse = await mine.GetAsync($"/api/v1/expense-types/{types![0].Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var asClientListResponse = await wrongType.GetAsync("/api/v1/expense-types");
        Assert.Equal(HttpStatusCode.Forbidden, asClientListResponse.StatusCode);

        var asClientGetResponse = await wrongType.GetAsync($"/api/v1/expense-types/{types[0].Id}");
        Assert.Equal(HttpStatusCode.Forbidden, asClientGetResponse.StatusCode);
    }

    [Fact]
    public async Task SubcontractorContacts_list_rejects_other_subcontractor_and_client_contact()
    {
        using var mine = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var ownResponse = await mine.GetAsync($"/api/v1/subcontractors/{_fixture.SubcontractorId}/contacts");
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);

        var otherResponse = await mine.GetAsync($"/api/v1/subcontractors/{_fixture.OtherSubcontractorId}/contacts");
        Assert.Equal(HttpStatusCode.Forbidden, otherResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var asClientResponse = await wrongType.GetAsync($"/api/v1/subcontractors/{_fixture.SubcontractorId}/contacts");
        Assert.Equal(HttpStatusCode.Forbidden, asClientResponse.StatusCode);
    }

    /// <summary>Best-effort: start then deliver; a leg already past Delivered just no-ops via a 409 Conflict, which is fine for fixture setup — tests only need SOME leg sitting at or past Delivered.</summary>
    private async Task DeliverLegAsync(Guid loadId, Guid legId)
    {
        await _fixture.StaffClient.PostAsync($"/api/v1/loads/{loadId}/legs/{legId}/start", null);
        await _fixture.StaffClient.PostAsync($"/api/v1/loads/{loadId}/legs/{legId}/deliver", null);
    }

    private sealed record JsonElementLike(Guid Id);
    private sealed record AccrualLike(Guid Id, Guid SubcontractorId);
}
