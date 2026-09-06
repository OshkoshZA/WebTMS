using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// Boots the real API (WebApplicationFactory&lt;Program&gt;, real DI, real SQL Server —
/// no mocks) with Email:SmtpHost/SmtpPort pointed at a real local SmtpTestReceiver
/// rather than the usual TestApiFactory.Create() with no args — the receiver's port is
/// only known once it's actually bound, so it can't be one of the fixed overrides
/// TestApiFactory applies for every other fixture. A dedicated fixture (rather than
/// reusing StaffTestFixture) because of that one difference; everything else here
/// mirrors PortalTestFixture's own builder-method style.
/// </summary>
public class EmailTestFixture : IAsyncLifetime
{
    private const string CostCentreId = "AAAAAAAA-0000-0000-0000-000000000003";
    private const string OriginLocationId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string DestinationLocationId = "aaaaaaaa-0000-0000-0000-000000000002";
    private const string CommodityId = "4cf021f4-50e1-4532-b7a4-627035eadef6";
    private const string UnitOfMeasureId = "a155c6f5-8dde-41f3-a54d-0ccdfd02d7cd";
    private const string CurrencyId = "2366a0f6-9b2d-41c0-9d73-2d38d0e45e8b";
    private const string LoadTypeId = "6C48E708-7D45-4381-881D-16CC9E39ED24";
    private const string AdminEmail = "admin@demo.local";
    private const string AdminPassword = "DemoAdmin#2026";
    private const string PortalPassword = "EmailTestPortalPass#2026";

    private WebApplicationFactory<Program>? _factory;

    public SmtpTestReceiver Receiver { get; private set; } = null!;
    public HttpClient StaffClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Receiver = new SmtpTestReceiver();
        _factory = TestApiFactory.Create(new Dictionary<string, string?>
        {
            ["Email:SmtpHost"] = "127.0.0.1",
            ["Email:SmtpPort"] = Receiver.Port.ToString(),
        });

        StaffClient = _factory.CreateClient();
        var response = await StaffClient.PostAsJsonAsync("/api/v1/auth/login", new { email = AdminEmail, password = AdminPassword });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        StaffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    public Task DisposeAsync()
    {
        Receiver.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    public async Task<Guid> CreateSubcontractorAsync(string suffix)
    {
        var response = await StaffClient.PostAsJsonAsync("/api/v1/subcontractors", new
        {
            name = $"Email Test Sub {suffix}",
            registrationNo = $"EMAILREG{suffix}",
            currencyId = Guid.Parse(CurrencyId),
            paymentTermsDays = 30
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    /// <summary>A Role holding only portal.subcontractor.viewlegs — the minimum a Supplier Portal contact needs to exist at all, matching PortalTestFixture's own pattern.</summary>
    public async Task<Guid> CreatePortalRoleAsync(string suffix)
    {
        var roleResponse = await StaffClient.PostAsJsonAsync("/api/v1/roles", new { name = $"Email Test Portal Role {suffix}" });
        roleResponse.EnsureSuccessStatusCode();
        var roleId = (await roleResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var functions = await StaffClient.GetFromJsonAsync<List<FunctionDto>>("/api/v1/functions");
        var functionId = functions!.First(f => f.Code == "portal.subcontractor.viewlegs").Id;
        (await StaffClient.PostAsJsonAsync($"/api/v1/roles/{roleId}/functions", new { functionId })).EnsureSuccessStatusCode();

        return roleId;
    }

    public async Task CreateSubcontractorContactAsync(Guid subcontractorId, string email, Guid roleId)
    {
        var response = await StaffClient.PostAsJsonAsync($"/api/v1/subcontractors/{subcontractorId}/contacts", new
        {
            email,
            password = PortalPassword,
            displayName = "Email Test Portal Contact",
            roleId
        });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>A fresh load with one Subcontracted leg (with a buy rate, so a real amount appears in the confirmation) for the given subcontractor — creating the leg is itself what triggers the confirmation email.</summary>
    public async Task<Guid> CreateSubcontractedLegAsync(Guid subcontractorId, string referenceNo)
    {
        var carrierClientId = await CreateOwnerClientAsync(referenceNo);
        var loadResponse = await StaffClient.PostAsJsonAsync("/api/v1/loads", new
        {
            clientId = carrierClientId,
            referenceNo,
            loadTypeId = Guid.Parse(LoadTypeId)
        });
        loadResponse.EnsureSuccessStatusCode();
        var loadId = (await loadResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

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
        var legId = (await legResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        (await StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs/{legId}/commodity-lines", new
        {
            commodityId = Guid.Parse(CommodityId),
            quantity = 1,
            unitOfMeasureId = Guid.Parse(UnitOfMeasureId),
            sellRatePerUnit = 500,
            buyRatePerUnit = 300
        })).EnsureSuccessStatusCode();

        return legId;
    }

    private async Task<Guid> CreateOwnerClientAsync(string suffix)
    {
        var response = await StaffClient.PostAsJsonAsync("/api/v1/clients", new
        {
            name = $"Email Test Owner Client {suffix}",
            registrationNo = $"EMAILOWNER{suffix}",
            currencyId = Guid.Parse(CurrencyId),
            creditLimit = 1_000_000m,
            paymentTermsDays = 30
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private sealed record LoginResponseDto(string AccessToken);
    private sealed record IdDto(Guid Id);
    private sealed record FunctionDto(Guid Id, string Code, string Description);
}

[CollectionDefinition(Name)]
public class EmailTestCollection : ICollectionFixture<EmailTestFixture>
{
    public const string Name = "Email tests";
}
