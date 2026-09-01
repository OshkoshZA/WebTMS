using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>Buy-side payables (§10.2) — accrual raise on allocation, matching/apportionment, and the two concurrency regressions found this project (Match's disjoint-accrual double-post, Dispute racing Match).</summary>
[Collection(StaffTestCollection.Name)]
public class SupplierInvoiceTests
{
    private readonly StaffTestFixture _fx;

    public SupplierInvoiceTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task Allocating_a_subcontracted_leg_with_a_buy_rate_raises_an_accrual_for_the_buy_amount()
    {
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        await _fx.CreateBookedLoadWithLegAsync(clientId, $"ACCR-{Guid.NewGuid():N}", subcontractorId: subcontractorId, buyRatePerUnit: 300);

        var accruals = await _fx.StaffClient.GetFromJsonAsync<List<AccrualLike>>($"/api/v1/accruals?subcontractorId={subcontractorId}");
        Assert.Single(accruals!);
        Assert.Equal(300m, accruals![0].EstimatedAmount);
        Assert.Equal(0, accruals[0].Status); // Accrued
    }

    private async Task<Guid> CreateSupplierInvoiceAsync(Guid subcontractorId, decimal amount)
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/supplier-invoices", new
        {
            subcontractorId,
            supplierInvoiceNumber = $"SI-{Guid.NewGuid():N}"[..15],
            invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            receivedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            amount
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdLike>())!.Id;
    }

    [Fact]
    public async Task Matching_at_the_exact_accrued_amount_nets_the_accrual_with_zero_variance()
    {
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        await _fx.CreateBookedLoadWithLegAsync(clientId, $"ACCR2-{Guid.NewGuid():N}", subcontractorId: subcontractorId, buyRatePerUnit: 300);
        var accrual = (await _fx.StaffClient.GetFromJsonAsync<List<AccrualLike>>($"/api/v1/accruals?subcontractorId={subcontractorId}"))!.Single();

        var invoiceId = await CreateSupplierInvoiceAsync(subcontractorId, 300);

        var matchResponse = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/supplier-invoices/{invoiceId}/match", new { accrualIds = new[] { accrual.Id } });
        matchResponse.EnsureSuccessStatusCode();
        var result = await matchResponse.Content.ReadFromJsonAsync<MatchResultLike>();

        Assert.Equal(0m, result!.VarianceAmount);
        var nettedAccrual = await _fx.StaffClient.GetFromJsonAsync<AccrualLike>($"/api/v1/accruals/{accrual.Id}");
        Assert.Equal(1, nettedAccrual!.Status); // Netted
    }

    /// <summary>Direct regression test for the fix in 2463fac: two concurrent Match calls against the SAME invoice with DISJOINT accrual sets used to both pass the in-memory status check and both apportion the invoice's full amount, double-posting the payable.</summary>
    [Fact]
    public async Task Concurrent_matches_with_disjoint_accruals_never_double_post_the_same_invoice()
    {
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        await _fx.CreateBookedLoadWithLegAsync(clientId, $"ACCR3A-{Guid.NewGuid():N}", subcontractorId: subcontractorId, buyRatePerUnit: 300);
        await _fx.CreateBookedLoadWithLegAsync(clientId, $"ACCR3B-{Guid.NewGuid():N}", subcontractorId: subcontractorId, buyRatePerUnit: 300);
        var accruals = await _fx.StaffClient.GetFromJsonAsync<List<AccrualLike>>($"/api/v1/accruals?subcontractorId={subcontractorId}&status=0");
        Assert.Equal(2, accruals!.Count);

        var invoiceId = await CreateSupplierInvoiceAsync(subcontractorId, 600);

        var (client1, client2) = _fx.GetRaceClients();

        var results = await Task.WhenAll(
            client1.PostAsJsonAsync($"/api/v1/supplier-invoices/{invoiceId}/match", new { accrualIds = new[] { accruals[0].Id } }),
            client2.PostAsJsonAsync($"/api/v1/supplier-invoices/{invoiceId}/match", new { accrualIds = new[] { accruals[1].Id } }));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);

        // The losing call's accrual must be untouched, still Accrued.
        var statuses = await Task.WhenAll(accruals.Select(a =>
            _fx.StaffClient.GetFromJsonAsync<AccrualLike>($"/api/v1/accruals/{a.Id}")));
        Assert.Single(statuses, a => a!.Status == 0); // one still Accrued
        Assert.Single(statuses, a => a!.Status == 1); // one Netted
    }

    /// <summary>Direct regression test for the fix in 2463fac: Dispute used to read Status == Received, lose a race to a concurrent Match that netted the accrual and committed first, then still blindly overwrite Status to Disputed.</summary>
    [Fact]
    public async Task Dispute_racing_a_match_never_wins_against_an_already_matched_invoice()
    {
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        await _fx.CreateBookedLoadWithLegAsync(clientId, $"ACCR4-{Guid.NewGuid():N}", subcontractorId: subcontractorId, buyRatePerUnit: 300);
        var accrual = (await _fx.StaffClient.GetFromJsonAsync<List<AccrualLike>>($"/api/v1/accruals?subcontractorId={subcontractorId}&status=0"))!.Single();
        var invoiceId = await CreateSupplierInvoiceAsync(subcontractorId, 300);

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/supplier-invoices/{invoiceId}/match", new { accrualIds = new[] { accrual.Id } }))
            .EnsureSuccessStatusCode();

        var disputeResponse = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/supplier-invoices/{invoiceId}/dispute", new { reason = "too late" });
        Assert.Equal(HttpStatusCode.Conflict, disputeResponse.StatusCode);

        var invoice = await _fx.StaffClient.GetFromJsonAsync<SupplierInvoiceLike>($"/api/v1/supplier-invoices/{invoiceId}");
        Assert.Equal(1, invoice!.Status); // still Matched, never flipped to Disputed
    }

    private sealed record IdLike(Guid Id);
    private sealed record AccrualLike(Guid Id, decimal EstimatedAmount, int Status);
    private sealed record MatchResultLike(decimal VarianceAmount);
    private sealed record SupplierInvoiceLike(Guid Id, int Status);
}
