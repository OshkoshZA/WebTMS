using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>Financial calendar (§10.3) — period close, its debtors-aging rollover, and the concurrent double-close regression found this project.</summary>
[Collection(StaffTestCollection.Name)]
public class FinancialCalendarTests
{
    private readonly StaffTestFixture _fx;

    public FinancialCalendarTests(StaffTestFixture fx) => _fx = fx;

    private async Task<Guid> GetOpenPeriodIdAsync()
    {
        var periods = await _fx.StaffClient.GetFromJsonAsync<List<PeriodLike>>("/api/v1/financial-periods");
        return periods!.Single(p => p.Status == 1).Id; // Open
    }

    private async Task<DateOnly> GetPeriodEndDateAsync(Guid periodId) =>
        (await _fx.StaffClient.GetFromJsonAsync<PeriodDetailLike>($"/api/v1/financial-periods/{periodId}"))!.EndDate;

    [Fact]
    public async Task Closing_the_open_period_writes_exactly_one_debtors_aging_snapshot_per_client()
    {
        await _fx.EnsureFutureFinancialPeriodExistsAsync();
        var periodId = await GetOpenPeriodIdAsync();

        var closeResponse = await _fx.StaffClient.PostAsync($"/api/v1/financial-periods/{periodId}/close", null);
        Assert.Equal(HttpStatusCode.NoContent, closeResponse.StatusCode);

        var snapshots = await _fx.StaffClient.GetFromJsonAsync<List<SnapshotLike>>($"/api/v1/financial-periods/{periodId}/debtors-aging");
        var duplicateClientIds = snapshots!.GroupBy(s => s.ClientId).Where(g => g.Count() > 1).ToList();
        Assert.Empty(duplicateClientIds);

        var periodsAfter = await _fx.StaffClient.GetFromJsonAsync<List<PeriodLike>>("/api/v1/financial-periods");
        Assert.Single(periodsAfter!, p => p.Status == 1); // exactly one Open period, still
    }

    /// <summary>
    /// Direct regression test for the bug WriteDebtorsAgingSnapshotsAsync used to carry:
    /// "new Current" was hardcoded to 0 forever (a pre-Invoice placeholder never
    /// revisited once Invoice existed), so every snapshot ever written understated a
    /// client's real position. An invoice due exactly 45 days before the closing
    /// period's own EndDate lands 31-60 days overdue as of that close — Days60, not
    /// Current and not zero.
    /// </summary>
    [Fact]
    public async Task Closing_a_period_writes_a_real_bucket_from_an_actual_overdue_invoice_not_a_permanent_zero()
    {
        await _fx.EnsureFutureFinancialPeriodExistsAsync();
        var periodId = await GetOpenPeriodIdAsync();
        var periodEndDate = await GetPeriodEndDateAsync(periodId);

        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"FINAGE-{Guid.NewGuid():N}", sellRatePerUnit: 350m);
        await _fx.DeliverLegAsync(loadId, legId);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" })).EnsureSuccessStatusCode();
        var generateResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/invoices/generate", new { clientId });
        generateResponse.EnsureSuccessStatusCode();
        var invoiceId = (await generateResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        // PaymentTermsDays is fixed at 30 (CreateClientAsync), so IssueDate = periodEndDate
        // - 75 gives DueDate = periodEndDate - 45 — 45 days overdue as of the close.
        var issueDate = periodEndDate.AddDays(-75);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/issue", new { issueDate })).EnsureSuccessStatusCode();

        var closeResponse = await _fx.StaffClient.PostAsync($"/api/v1/financial-periods/{periodId}/close", null);
        Assert.Equal(HttpStatusCode.NoContent, closeResponse.StatusCode);

        var snapshots = await _fx.StaffClient.GetFromJsonAsync<List<AgingSnapshotDetailLike>>($"/api/v1/financial-periods/{periodId}/debtors-aging");
        var snapshot = snapshots!.Single(s => s.ClientId == clientId);

        Assert.Equal(350m, snapshot.Days60);
        Assert.Equal(0m, snapshot.CurrentAmount);
        Assert.Equal(0m, snapshot.Days30);
        Assert.Equal(0m, snapshot.Days90);
        Assert.Equal(0m, snapshot.Days90Plus);
        Assert.Equal(350m, snapshot.TotalOutstanding);
    }

    /// <summary>Direct regression test for the fix in f541b9e: two concurrent Close calls for the same period both used to pass the in-memory Status check and each write a full, duplicate set of DebtorsAgingSnapshot rows.</summary>
    [Fact]
    public async Task Concurrent_close_calls_for_the_same_period_resolve_to_exactly_one_success()
    {
        await _fx.EnsureFutureFinancialPeriodExistsAsync();
        var periodId = await GetOpenPeriodIdAsync();

        var (client1, client2) = _fx.GetRaceClients();

        var results = await Task.WhenAll(
            client1.PostAsync($"/api/v1/financial-periods/{periodId}/close", null),
            client2.PostAsync($"/api/v1/financial-periods/{periodId}/close", null));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);

        var snapshots = await _fx.StaffClient.GetFromJsonAsync<List<SnapshotLike>>($"/api/v1/financial-periods/{periodId}/debtors-aging");
        var duplicateClientIds = snapshots!.GroupBy(s => s.ClientId).Where(g => g.Count() > 1).ToList();
        Assert.Empty(duplicateClientIds);
    }

    private sealed record PeriodLike(Guid Id, int Status);
    private sealed record PeriodDetailLike(Guid Id, DateOnly EndDate);
    private sealed record SnapshotLike(Guid Id, Guid ClientId);
    private sealed record AgingSnapshotDetailLike(
        Guid Id, Guid ClientId, decimal CurrentAmount, decimal Days30, decimal Days60, decimal Days90, decimal Days90Plus, decimal TotalOutstanding);
    private sealed record IdDto(Guid Id);
}
