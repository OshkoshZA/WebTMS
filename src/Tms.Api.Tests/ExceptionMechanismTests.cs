using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>The shared Exception mechanism (§16.1) — the three wired sources (Debrief, credit override, accrual variance) and the Acknowledge/Resolve lifecycle.</summary>
[Collection(StaffTestCollection.Name)]
public class ExceptionMechanismTests
{
    private readonly StaffTestFixture _fx;

    public ExceptionMechanismTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task A_debrief_exception_raises_an_ExceptionRecord_and_resolves_when_approved()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"EXC-DEBRIEF-{Guid.NewGuid():N}");
        await _fx.DeliverLegAsync(loadId, legId);

        var debriefResponse = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg", incidents = new[] { new { type = 0, severity = 1, narrative = "Delay" } } });
        debriefResponse.EnsureSuccessStatusCode();
        var debrief = await debriefResponse.Content.ReadFromJsonAsync<DebriefLike>();

        var openExceptions = await _fx.StaffClient.GetFromJsonAsync<List<ExceptionLike>>("/api/v1/exceptions?status=Open");
        var raised = Assert.Single(openExceptions!, e => e.EntityType == "Debrief" && e.EntityId == debrief!.Id);
        Assert.Equal("Debrief", raised.Category);

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/debriefs/{debrief!.Id}/approve", new { })).EnsureSuccessStatusCode();

        var afterApproval = await _fx.StaffClient.GetFromJsonAsync<ExceptionLike>($"/api/v1/exceptions/{raised.Id}");
        Assert.Equal(2, afterApproval!.Status); // Resolved (Open=0, Acknowledged=1, Resolved=2)
    }

    [Fact]
    public async Task A_credit_limit_override_raises_an_ExceptionRecord_for_the_client()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8], creditLimit: 10m);
        var loadId = await _fx.CreateLoadAsync(clientId, $"EXC-CREDIT-{Guid.NewGuid():N}");
        var legResponse = await _fx.AddOwnFleetLegAsync(loadId);
        legResponse.EnsureSuccessStatusCode();
        var legId = (await legResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;

        (await _fx.AddCommodityLineAsync(loadId, legId, sellRatePerUnit: 500, creditOverrideReason: "Test override")).EnsureSuccessStatusCode();

        var openExceptions = await _fx.StaffClient.GetFromJsonAsync<List<ExceptionLike>>("/api/v1/exceptions?status=Open");
        Assert.Contains(openExceptions!, e => e.Category == "CreditOverride" && e.EntityType == "Client" && e.EntityId == clientId);
    }

    [Fact]
    public async Task An_accrual_variance_on_match_raises_an_ExceptionRecord_for_the_supplier_invoice()
    {
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        await _fx.CreateBookedLoadWithLegAsync(clientId, $"EXC-VARIANCE-{Guid.NewGuid():N}", subcontractorId: subcontractorId, buyRatePerUnit: 300);
        var accrual = (await _fx.StaffClient.GetFromJsonAsync<List<AccrualLike>>($"/api/v1/accruals?subcontractorId={subcontractorId}&status=0"))!.Single();

        var invoiceResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/supplier-invoices", new
        {
            subcontractorId,
            supplierInvoiceNumber = $"SI-VAR-{Guid.NewGuid():N}"[..15],
            invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            receivedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            amount = 350 // R50 variance against the R300 accrual
        });
        invoiceResponse.EnsureSuccessStatusCode();
        var invoiceId = (await invoiceResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/supplier-invoices/{invoiceId}/match", new { accrualIds = new[] { accrual.Id } }))
            .EnsureSuccessStatusCode();

        var openExceptions = await _fx.StaffClient.GetFromJsonAsync<List<ExceptionLike>>("/api/v1/exceptions?status=Open");
        Assert.Contains(openExceptions!, e => e.Category == "AccrualVariance" && e.EntityType == "SupplierInvoice" && e.EntityId == invoiceId);
    }

    [Fact]
    public async Task Acknowledge_then_resolve_lifecycle_and_repeat_calls_are_rejected()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8], creditLimit: 10m);
        var loadId = await _fx.CreateLoadAsync(clientId, $"EXC-LIFECYCLE-{Guid.NewGuid():N}");
        var legResponse = await _fx.AddOwnFleetLegAsync(loadId);
        var legId = (await legResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;
        (await _fx.AddCommodityLineAsync(loadId, legId, sellRatePerUnit: 500, creditOverrideReason: "Test")).EnsureSuccessStatusCode();

        var openExceptions = await _fx.StaffClient.GetFromJsonAsync<List<ExceptionLike>>("/api/v1/exceptions?status=Open");
        var exceptionId = openExceptions!.Last(e => e.EntityId == clientId).Id;

        var ackResponse = await _fx.StaffClient.PostAsync($"/api/v1/exceptions/{exceptionId}/acknowledge", null);
        Assert.Equal(HttpStatusCode.NoContent, ackResponse.StatusCode);

        var reAck = await _fx.StaffClient.PostAsync($"/api/v1/exceptions/{exceptionId}/acknowledge", null);
        Assert.Equal(HttpStatusCode.Conflict, reAck.StatusCode);

        var resolveResponse = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/exceptions/{exceptionId}/resolve", new { resolutionNotes = "Done" });
        Assert.Equal(HttpStatusCode.NoContent, resolveResponse.StatusCode);

        var reResolve = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/exceptions/{exceptionId}/resolve", new { });
        Assert.Equal(HttpStatusCode.Conflict, reResolve.StatusCode);
    }

    private sealed record IdLike(Guid Id);
    private sealed record DebriefLike(Guid Id);
    private sealed record AccrualLike(Guid Id);
    private sealed record ExceptionLike(Guid Id, string Category, string EntityType, Guid EntityId, int Status);
}
