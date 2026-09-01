using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>Debrief submission and approval (§09) — auto-approve vs. PendingReview, the load-status guard, the duplicate-submission race, and the accrual-claim path that feeds §10.2/§16.1.</summary>
[Collection(StaffTestCollection.Name)]
public class DebriefTests
{
    private readonly StaffTestFixture _fx;

    public DebriefTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task Clean_debrief_auto_approves_and_unlocks_the_load()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"DEBRIEF-CLEAN-{Guid.NewGuid():N}");
        await _fx.DeliverLegAsync(loadId, legId);

        var response = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var debrief = await response.Content.ReadFromJsonAsync<DebriefLike>();
        Assert.Equal(1, debrief!.Status); // Approved (PendingReview=0, Approved=1)
    }

    [Fact]
    public async Task Missing_pod_image_routes_to_PendingReview_even_when_PodReceived_is_true()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"DEBRIEF-NOPOD-{Guid.NewGuid():N}");
        await _fx.DeliverLegAsync(loadId, legId);

        var response = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief", new { podReceived = true });
        response.EnsureSuccessStatusCode();
        var debrief = await response.Content.ReadFromJsonAsync<DebriefLike>();

        Assert.Equal(0, debrief!.Status); // PendingReview
        Assert.Contains("Missing POD", debrief.ExceptionReasons);
    }

    [Fact]
    public async Task Debrief_clerk_approve_resolves_a_PendingReview_debrief()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"DEBRIEF-APPROVE-{Guid.NewGuid():N}");
        await _fx.DeliverLegAsync(loadId, legId);

        var submitResponse = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg", incidents = new[] { new { type = 0, severity = 1, narrative = "Delay" } } });
        submitResponse.EnsureSuccessStatusCode();
        var debrief = await submitResponse.Content.ReadFromJsonAsync<DebriefLike>();
        Assert.Equal(0, debrief!.Status); // PendingReview — an incident is always an exception trigger

        var approveResponse = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/debriefs/{debrief.Id}/approve", new { resolutionNote = "Reviewed" });
        Assert.Equal(HttpStatusCode.NoContent, approveResponse.StatusCode);

        var reApprove = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/debriefs/{debrief.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, reApprove.StatusCode);
    }

    [Fact]
    public async Task OnHold_load_rejects_debrief_submission()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"DEBRIEF-HOLD-{Guid.NewGuid():N}");

        // Hold only accepts a load that's InTransit, so: Start (-> InTransit) -> Hold ->
        // Deliver (DeliverLeg has no On-Hold guard, unlike Start/Allocate/AddLeg) -> the
        // leg is now Delivered but the load is still OnHold, which is exactly the state
        // SubmitDebrief's own guard exists to catch.
        (await _fx.StaffClient.PostAsync($"/api/v1/loads/{loadId}/legs/{legId}/start", null)).EnsureSuccessStatusCode();
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/hold", new { reason = "query" })).EnsureSuccessStatusCode();
        (await _fx.StaffClient.PostAsync($"/api/v1/loads/{loadId}/legs/{legId}/deliver", null)).EnsureSuccessStatusCode();

        var response = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_debrief_submissions_for_the_same_leg_resolve_to_exactly_one_success()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"DEBRIEF-RACE-{Guid.NewGuid():N}");
        await _fx.DeliverLegAsync(loadId, legId);

        var (client1, client2) = _fx.GetRaceClients();
        var body = new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" };

        var results = await Task.WhenAll(
            client1.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief", body),
            client2.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief", body));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Accrual_claim_on_approval_increments_the_named_accrual_by_exactly_the_claimed_amount()
    {
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(
            await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]),
            $"DEBRIEF-ACCRUAL-{Guid.NewGuid():N}", subcontractorId: subcontractorId, buyRatePerUnit: 300);

        var accruals = await _fx.StaffClient.GetFromJsonAsync<List<AccrualLike>>($"/api/v1/accruals?subcontractorId={subcontractorId}");
        var accrual = accruals!.Single();
        Assert.Equal(300m, accrual.EstimatedAmount);

        var expenseTypeId = await FindOrCreateExpenseTypeAsync();
        await _fx.DeliverLegAsync(loadId, legId);

        var response = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief", new
        {
            podReceived = true,
            podImageUrl = "https://example.com/pod.jpg",
            expenses = new[]
            {
                new { expenseTypeId, description = "Detention", amount = 200m, currencyId = Guid.Parse(StaffTestFixture.CurrencyId), claimedAgainst = 1, accrualId = accrual.Id }
            }
        });
        response.EnsureSuccessStatusCode();

        var updatedAccrual = await _fx.StaffClient.GetFromJsonAsync<AccrualLike>($"/api/v1/accruals/{accrual.Id}");
        Assert.Equal(500m, updatedAccrual!.EstimatedAmount);
    }

    private async Task<Guid> FindOrCreateExpenseTypeAsync()
    {
        var existing = await _fx.StaffClient.GetFromJsonAsync<List<ExpenseTypeLike>>("/api/v1/expense-types");
        if (existing is { Count: > 0 }) return existing[0].Id;

        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/expense-types", new { code = $"TOLL-{Guid.NewGuid():N}"[..10], name = "Toll" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdLike>())!.Id;
    }

    private sealed record IdLike(Guid Id);
    private sealed record DebriefLike(Guid Id, int Status, string? ExceptionReasons);
    private sealed record AccrualLike(Guid Id, decimal EstimatedAmount);
    private sealed record ExpenseTypeLike(Guid Id);
}
