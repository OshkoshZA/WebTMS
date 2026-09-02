using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// Boots the real API in-process (WebApplicationFactory&lt;Program&gt;, same
/// Program.cs, same DI, same real SQL Server database via Tms.Api's own user
/// secrets — the whole point is to exercise the actual authorization pipeline, not a
/// mock of it) and bootstraps two independent portal identities plus one "other
/// party" pair, so a test can assert both "my own access works" and "the other
/// party's data is off-limits" without hand-rolling fixtures per test. Reference-data
/// IDs below are the same seeded values used throughout this project's manual
/// verification (DevelopmentSeeder's demo tenant/company).
/// </summary>
public class PortalTestFixture : IAsyncLifetime
{
    private const string LoadTypeId = "6C48E708-7D45-4381-881D-16CC9E39ED24";
    private const string CostCentreId = "AAAAAAAA-0000-0000-0000-000000000003";
    private const string OriginLocationId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string DestinationLocationId = "aaaaaaaa-0000-0000-0000-000000000002";
    private const string CommodityId = "4cf021f4-50e1-4532-b7a4-627035eadef6";
    private const string UnitOfMeasureId = "a155c6f5-8dde-41f3-a54d-0ccdfd02d7cd";
    private const string CurrencyId = "2366a0f6-9b2d-41c0-9d73-2d38d0e45e8b";
    private const string AdminEmail = "admin@demo.local";
    private const string AdminPassword = "DemoAdmin#2026";
    private const string PortalPassword = "PortalTestPass#2026";

    private readonly WebApplicationFactory<Program> _factory = TestApiFactory.Create();
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];

    public HttpClient StaffClient { get; private set; } = null!;

    /// <summary>A Subcontractor Portal contact's own token, and the fixture data it owns.</summary>
    public string SubcontractorToken { get; private set; } = "";
    public Guid SubcontractorId { get; private set; }
    public Guid SubcontractorLegId { get; private set; }
    public Guid SubcontractorLegLoadId { get; private set; }
    public Guid SubcontractorAccrualId { get; private set; }

    /// <summary>A Customer Portal contact's own token, and the fixture data it owns.</summary>
    public string ClientToken { get; private set; } = "";
    public Guid ClientId { get; private set; }
    public Guid ClientLoadId { get; private set; }

    /// <summary>A second, unrelated party of each type — every "must NOT see this" assertion targets these ids.</summary>
    public Guid OtherSubcontractorId { get; private set; }
    public Guid OtherSubcontractorLegId { get; private set; }
    public Guid OtherSubcontractorLegLoadId { get; private set; }
    public Guid OtherClientId { get; private set; }
    public Guid OtherClientLoadId { get; private set; }

    public async Task InitializeAsync()
    {
        StaffClient = _factory.CreateClient();
        var staffToken = await LoginAsync(StaffClient, AdminEmail, AdminPassword);
        StaffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        var subcontractorViewlegsFn = await FindFunctionIdAsync("portal.subcontractor.viewlegs");
        var subcontractorAckFn = await FindFunctionIdAsync("portal.subcontractor.acknowledgeconfirmation");
        var subcontractorPodFn = await FindFunctionIdAsync("portal.subcontractor.uploadpod");
        var clientViewloadsFn = await FindFunctionIdAsync("portal.client.viewloads");
        var clientViewinvoicesFn = await FindFunctionIdAsync("portal.client.viewinvoices");
        var clientCreateloadFn = await FindFunctionIdAsync("portal.client.createload");

        var supplierPortalRoleId = await CreateRoleAsync($"Test Supplier Portal {_runId}",
            subcontractorViewlegsFn, subcontractorAckFn, subcontractorPodFn);
        var customerPortalRoleId = await CreateRoleAsync($"Test Customer Portal {_runId}",
            clientViewloadsFn, clientViewinvoicesFn, clientCreateloadFn);

        // The "mine" party pair.
        SubcontractorId = await CreateSubcontractorAsync($"Test Sub {_runId}");
        (SubcontractorLegId, SubcontractorLegLoadId, SubcontractorAccrualId) = await CreateSubcontractedLegAsync(SubcontractorId, $"PORTAL-TEST-SUB-{_runId}");
        var subcontractorContactEmail = $"sub-contact-{_runId}@portal-test.example";
        await CreateSubcontractorContactAsync(SubcontractorId, subcontractorContactEmail, supplierPortalRoleId);
        SubcontractorToken = await LoginAsync(_factory.CreateClient(), subcontractorContactEmail, PortalPassword);

        ClientId = await CreateClientAsync($"Test Client {_runId}");
        ClientLoadId = await CreateLoadAsync(ClientId, $"PORTAL-TEST-CLIENT-{_runId}");
        var clientContactEmail = $"client-contact-{_runId}@portal-test.example";
        await CreateClientContactAsync(ClientId, clientContactEmail, customerPortalRoleId);
        ClientToken = await LoginAsync(_factory.CreateClient(), clientContactEmail, PortalPassword);

        // The "other party" pair — every cross-party test targets these instead.
        OtherSubcontractorId = await CreateSubcontractorAsync($"Test Other Sub {_runId}");
        (OtherSubcontractorLegId, OtherSubcontractorLegLoadId, _) = await CreateSubcontractedLegAsync(OtherSubcontractorId, $"PORTAL-TEST-OTHERSUB-{_runId}");

        OtherClientId = await CreateClientAsync($"Test Other Client {_runId}");
        OtherClientLoadId = await CreateLoadAsync(OtherClientId, $"PORTAL-TEST-OTHERCLIENT-{_runId}");
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>A fresh HttpClient carrying the given bearer token — tests share the fixture's app instance but never a client, so response headers/state never leak between tests.</summary>
    public HttpClient CreateAuthenticatedClient(string bearerToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return body!.AccessToken;
    }

    private async Task<Guid> FindFunctionIdAsync(string code)
    {
        var functions = await StaffClient.GetFromJsonAsync<List<FunctionDto>>("/api/v1/functions");
        var match = functions!.FirstOrDefault(f => f.Code == code);
        if (match is null) throw new InvalidOperationException($"Seeded function '{code}' was not found — has DevelopmentSeeder changed?");
        return match.Id;
    }

    private async Task<Guid> CreateRoleAsync(string name, params Guid[] functionIds)
    {
        var roleResponse = await StaffClient.PostAsJsonAsync("/api/v1/roles", new { Name = name });
        roleResponse.EnsureSuccessStatusCode();
        var role = await roleResponse.Content.ReadFromJsonAsync<IdDto>();

        foreach (var functionId in functionIds)
        {
            var grantResponse = await StaffClient.PostAsJsonAsync($"/api/v1/roles/{role!.Id}/functions", new { functionId });
            grantResponse.EnsureSuccessStatusCode();
        }

        return role!.Id;
    }

    private async Task<Guid> CreateSubcontractorAsync(string name)
    {
        var response = await StaffClient.PostAsJsonAsync("/api/v1/subcontractors", new
        {
            name,
            registrationNo = $"REG-{_runId}-{name.GetHashCode():X}",
            currencyId = Guid.Parse(CurrencyId),
            paymentTermsDays = 30
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<IdDto>();
        return body!.Id;
    }

    private async Task<Guid> CreateClientAsync(string name)
    {
        var response = await StaffClient.PostAsJsonAsync("/api/v1/clients", new
        {
            name,
            registrationNo = $"REG-{_runId}-{name.GetHashCode():X}",
            currencyId = Guid.Parse(CurrencyId),
            creditLimit = 1_000_000m,
            paymentTermsDays = 30
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<IdDto>();
        return body!.Id;
    }

    private async Task<Guid> CreateLoadAsync(Guid clientId, string referenceNo)
    {
        var response = await StaffClient.PostAsJsonAsync("/api/v1/loads", new
        {
            clientId,
            referenceNo,
            loadTypeId = Guid.Parse(LoadTypeId)
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<IdDto>();
        return body!.Id;
    }

    /// <summary>A subcontracted leg with a buy rate (raising an accrual) on a fresh load for the given subcontractor — self-contained, doesn't reuse ClientId, since a leg only needs *some* load to hang off.</summary>
    private async Task<(Guid LegId, Guid LoadId, Guid AccrualId)> CreateSubcontractedLegAsync(Guid subcontractorId, string referenceNo)
    {
        var carrierClientId = await CreateClientAsync($"Carrier-Load-Client {referenceNo}");
        var loadId = await CreateLoadAsync(carrierClientId, referenceNo);

        var legResponse = await StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs", new
        {
            sequenceNo = 1,
            originLocationId = Guid.Parse(OriginLocationId),
            destinationLocationId = Guid.Parse(DestinationLocationId),
            executionType = 1, // Subcontracted
            costCentreId = Guid.Parse(CostCentreId),
            subcontractorId
        });
        legResponse.EnsureSuccessStatusCode();
        var leg = await legResponse.Content.ReadFromJsonAsync<IdDto>();

        var commodityResponse = await StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs/{leg!.Id}/commodity-lines", new
        {
            commodityId = Guid.Parse(CommodityId),
            quantity = 1,
            unitOfMeasureId = Guid.Parse(UnitOfMeasureId),
            sellRatePerUnit = 500,
            buyRatePerUnit = 300
        });
        commodityResponse.EnsureSuccessStatusCode();

        var accruals = await StaffClient.GetFromJsonAsync<List<AccrualDto>>($"/api/v1/accruals?subcontractorId={subcontractorId}");
        var accrual = accruals!.OrderByDescending(a => a.AccrualDate).First();

        return (leg.Id, loadId, accrual.Id);
    }

    private async Task CreateSubcontractorContactAsync(Guid subcontractorId, string email, Guid roleId)
    {
        var response = await StaffClient.PostAsJsonAsync($"/api/v1/subcontractors/{subcontractorId}/contacts", new
        {
            email,
            password = PortalPassword,
            displayName = "Portal Test Subcontractor Contact",
            roleId
        });
        response.EnsureSuccessStatusCode();
    }

    private async Task CreateClientContactAsync(Guid clientId, string email, Guid roleId)
    {
        var response = await StaffClient.PostAsJsonAsync($"/api/v1/clients/{clientId}/contacts", new
        {
            email,
            password = PortalPassword,
            displayName = "Portal Test Client Contact",
            roleId
        });
        response.EnsureSuccessStatusCode();
    }

    private sealed record LoginResponseDto(string AccessToken);
    private sealed record IdDto(Guid Id);
    private sealed record FunctionDto(Guid Id, string Code, string Description);
    private sealed record AccrualDto(Guid Id, Guid SubcontractorId, DateOnly AccrualDate);
}

[CollectionDefinition(Name)]
public class PortalTestCollection : ICollectionFixture<PortalTestFixture>
{
    public const string Name = "Portal boundary tests";
}
