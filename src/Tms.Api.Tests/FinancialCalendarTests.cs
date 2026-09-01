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
    private sealed record SnapshotLike(Guid Id, Guid ClientId);
}
