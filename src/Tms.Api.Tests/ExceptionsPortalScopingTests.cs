using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// ExceptionsController.List/Get used to Forbid every portal caller outright; it now
/// resolves each of the three currently-wired ExceptionRecord sources (§16.1's Fig. 13
/// — "Client" for a credit override, "SupplierInvoice" for an accrual variance,
/// "Debrief" via a join through LoadLeg/Load) back to whichever Client or Subcontractor
/// actually owns it. One test per source, each from the three angles this project's
/// own audits settled on: the caller's own exception is visible, a different party's
/// same-type exception is not (even by direct id), and the wrong portal TYPE entirely
/// is rejected too.
/// </summary>
[Collection(PortalTestCollection.Name)]
public class ExceptionsPortalScopingTests
{
    private readonly PortalTestFixture _fixture;

    public ExceptionsPortalScopingTests(PortalTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Debrief_exception_resolves_to_the_owning_client_not_others_not_a_subcontractor_contact()
    {
        var mineDebriefId = await RaiseDebriefExceptionOnOwnFleetLegAsync(_fixture.ClientLoadId);
        var otherDebriefId = await RaiseDebriefExceptionOnOwnFleetLegAsync(_fixture.OtherClientLoadId);

        var mineExceptionId = await FindExceptionIdAsync("Debrief", mineDebriefId);
        var otherExceptionId = await FindExceptionIdAsync("Debrief", otherDebriefId);

        using var mine = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var listed = await mine.GetFromJsonAsync<List<ExceptionLike>>("/api/v1/exceptions?status=Open");
        Assert.Contains(listed!, e => e.Id == mineExceptionId);
        Assert.DoesNotContain(listed!, e => e.Id == otherExceptionId);

        var ownGetResponse = await mine.GetAsync($"/api/v1/exceptions/{mineExceptionId}");
        Assert.Equal(HttpStatusCode.OK, ownGetResponse.StatusCode);

        var otherGetResponse = await mine.GetAsync($"/api/v1/exceptions/{otherExceptionId}");
        Assert.Equal(HttpStatusCode.Forbidden, otherGetResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var wrongTypeResponse = await wrongType.GetAsync($"/api/v1/exceptions/{mineExceptionId}");
        Assert.Equal(HttpStatusCode.Forbidden, wrongTypeResponse.StatusCode);
    }

    [Fact]
    public async Task Debrief_exception_resolves_to_the_owning_subcontractor_not_others_not_a_client_contact()
    {
        var mineDebriefId = await RaiseDebriefExceptionOnSubcontractedLegAsync(_fixture.SubcontractorLegLoadId, _fixture.SubcontractorLegId);
        var otherDebriefId = await RaiseDebriefExceptionOnSubcontractedLegAsync(_fixture.OtherSubcontractorLegLoadId, _fixture.OtherSubcontractorLegId);

        var mineExceptionId = await FindExceptionIdAsync("Debrief", mineDebriefId);
        var otherExceptionId = await FindExceptionIdAsync("Debrief", otherDebriefId);

        using var mine = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var listed = await mine.GetFromJsonAsync<List<ExceptionLike>>("/api/v1/exceptions?status=Open");
        Assert.Contains(listed!, e => e.Id == mineExceptionId);
        Assert.DoesNotContain(listed!, e => e.Id == otherExceptionId);

        var ownGetResponse = await mine.GetAsync($"/api/v1/exceptions/{mineExceptionId}");
        Assert.Equal(HttpStatusCode.OK, ownGetResponse.StatusCode);

        var otherGetResponse = await mine.GetAsync($"/api/v1/exceptions/{otherExceptionId}");
        Assert.Equal(HttpStatusCode.Forbidden, otherGetResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var wrongTypeResponse = await wrongType.GetAsync($"/api/v1/exceptions/{mineExceptionId}");
        Assert.Equal(HttpStatusCode.Forbidden, wrongTypeResponse.StatusCode);
    }

    [Fact]
    public async Task AccrualVariance_exception_resolves_to_the_owning_subcontractor_not_others_not_a_client_contact()
    {
        var mineInvoiceId = await RaiseAccrualVarianceAsync(_fixture.SubcontractorId, _fixture.SubcontractorAccrualId);

        var otherAccrualId = (await _fixture.StaffClient
            .GetFromJsonAsync<List<AccrualLike>>($"/api/v1/accruals?subcontractorId={_fixture.OtherSubcontractorId}&status=0"))!
            .Select(a => a.Id).First();
        var otherInvoiceId = await RaiseAccrualVarianceAsync(_fixture.OtherSubcontractorId, otherAccrualId);

        var mineExceptionId = await FindExceptionIdAsync("SupplierInvoice", mineInvoiceId);
        var otherExceptionId = await FindExceptionIdAsync("SupplierInvoice", otherInvoiceId);

        using var mine = _fixture.CreateAuthenticatedClient(_fixture.SubcontractorToken);
        var listed = await mine.GetFromJsonAsync<List<ExceptionLike>>("/api/v1/exceptions?status=Open");
        Assert.Contains(listed!, e => e.Id == mineExceptionId);
        Assert.DoesNotContain(listed!, e => e.Id == otherExceptionId);

        var ownGetResponse = await mine.GetAsync($"/api/v1/exceptions/{mineExceptionId}");
        Assert.Equal(HttpStatusCode.OK, ownGetResponse.StatusCode);

        var otherGetResponse = await mine.GetAsync($"/api/v1/exceptions/{otherExceptionId}");
        Assert.Equal(HttpStatusCode.Forbidden, otherGetResponse.StatusCode);

        using var wrongType = _fixture.CreateAuthenticatedClient(_fixture.ClientToken);
        var wrongTypeResponse = await wrongType.GetAsync($"/api/v1/exceptions/{mineExceptionId}");
        Assert.Equal(HttpStatusCode.Forbidden, wrongTypeResponse.StatusCode);
    }

    /// <summary>Adds an own-fleet leg to an existing load, progresses it to Delivered, then submits a POD-less debrief — the simplest of §09's five triggers, guaranteed to raise a Debrief exception. Returns the debrief id (the exception's own EntityId).</summary>
    private async Task<Guid> RaiseDebriefExceptionOnOwnFleetLegAsync(Guid loadId)
    {
        var legResponse = await _fixture.StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs", new
        {
            sequenceNo = 1,
            originLocationId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            destinationLocationId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
            executionType = 0, // OwnFleet
            costCentreId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
            vehicleId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            driverId = Guid.Parse("a05273b3-36b7-454a-9029-7b09a3068db0")
        });
        legResponse.EnsureSuccessStatusCode();
        var legId = (await legResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;

        return await DeliverAndSubmitPodLessDebriefAsync(loadId, legId);
    }

    /// <summary>Same as above, but for a leg that already exists and is allocated to a subcontractor (start/deliver only — no leg/commodity setup, the fixture's own subcontracted legs already carry a commodity line).</summary>
    private async Task<Guid> RaiseDebriefExceptionOnSubcontractedLegAsync(Guid loadId, Guid legId) =>
        await DeliverAndSubmitPodLessDebriefAsync(loadId, legId);

    private async Task<Guid> DeliverAndSubmitPodLessDebriefAsync(Guid loadId, Guid legId)
    {
        await _fixture.StaffClient.PostAsync($"/api/v1/loads/{loadId}/legs/{legId}/start", null);
        await _fixture.StaffClient.PostAsync($"/api/v1/loads/{loadId}/legs/{legId}/deliver", null);

        var debriefResponse = await _fixture.StaffClient.PostAsJsonAsync(
            $"/api/v1/legs/{legId}/debrief", new { podReceived = false });
        debriefResponse.EnsureSuccessStatusCode();
        return (await debriefResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;
    }

    /// <summary>Raises an AccrualVariance exception by matching a SupplierInvoice whose amount deliberately differs from the accrual's own estimate — the exact recipe ExceptionMechanismTests already uses at the staff level. Returns the invoice id (the exception's own EntityId).</summary>
    private async Task<Guid> RaiseAccrualVarianceAsync(Guid subcontractorId, Guid accrualId)
    {
        var invoiceResponse = await _fixture.StaffClient.PostAsJsonAsync("/api/v1/supplier-invoices", new
        {
            subcontractorId,
            supplierInvoiceNumber = $"SI-XSCOPE-{Guid.NewGuid():N}"[..18],
            invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            receivedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            amount = 999999m // deliberately far off any accrual's own estimate
        });
        invoiceResponse.EnsureSuccessStatusCode();
        var invoiceId = (await invoiceResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;

        (await _fixture.StaffClient.PostAsJsonAsync($"/api/v1/supplier-invoices/{invoiceId}/match", new { accrualIds = new[] { accrualId } }))
            .EnsureSuccessStatusCode();

        return invoiceId;
    }

    private async Task<Guid> FindExceptionIdAsync(string entityType, Guid entityId)
    {
        var exceptions = await _fixture.StaffClient.GetFromJsonAsync<List<ExceptionLike>>("/api/v1/exceptions?status=Open");
        return exceptions!.Single(e => e.EntityType == entityType && e.EntityId == entityId).Id;
    }

    private sealed record IdLike(Guid Id);
    private sealed record AccrualLike(Guid Id);
    private sealed record ExceptionLike(Guid Id, string Category, string EntityType, Guid EntityId, int Status);
}
