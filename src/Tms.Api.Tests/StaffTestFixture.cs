using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// Boots the real API in-process (same WebApplicationFactory approach as
/// PortalTestFixture — real DI, real SQL Server database, no mocks) and exposes a
/// single staff-authenticated HttpClient plus builder methods for the fixture data
/// every business-logic test needs: a Client, a Subcontractor, a Load, a leg (own-fleet
/// or subcontracted), a commodity line. Every builder takes a unique suffix so
/// concurrent test classes never collide on names/reference numbers, and reference-data
/// IDs are the same seeded values used throughout this project's manual verification
/// (DevelopmentSeeder's demo tenant/company).
/// </summary>
public class StaffTestFixture : IAsyncLifetime
{
    public const string LoadTypeId = "6C48E708-7D45-4381-881D-16CC9E39ED24";
    public const string CostCentreId = "AAAAAAAA-0000-0000-0000-000000000003";
    public const string OriginLocationId = "aaaaaaaa-0000-0000-0000-000000000001";
    public const string DestinationLocationId = "aaaaaaaa-0000-0000-0000-000000000002";
    public const string CommodityId = "4cf021f4-50e1-4532-b7a4-627035eadef6";
    public const string UnitOfMeasureId = "a155c6f5-8dde-41f3-a54d-0ccdfd02d7cd";
    public const string CurrencyId = "2366a0f6-9b2d-41c0-9d73-2d38d0e45e8b";
    private const string AdminEmail = "admin@demo.local";
    private const string AdminPassword = "DemoAdmin#2026";

    private readonly WebApplicationFactory<Program> _factory = new();

    public HttpClient StaffClient { get; private set; } = null!;

    /// <summary>
    /// Two pre-authenticated staff clients, logged in ONCE during InitializeAsync
    /// rather than per test — /auth/login carries a 10-request/minute, per-IP rate
    /// limit (Program.cs), and with ~7 concurrency-race tests each wanting two
    /// independent sessions, logging in fresh per test blew straight through it the
    /// first time this suite ran in full. Reusing the same pair across every race test
    /// is safe: tests in the same xUnit collection run sequentially, never overlapping.
    /// </summary>
    private HttpClient _raceClient1 = null!;
    private HttpClient _raceClient2 = null!;

    public async Task InitializeAsync()
    {
        StaffClient = _factory.CreateClient();
        var response = await StaffClient.PostAsJsonAsync("/api/v1/auth/login", new { email = AdminEmail, password = AdminPassword });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        StaffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        _raceClient1 = await LoginNewClientAsync();
        _raceClient2 = await LoginNewClientAsync();
    }

    private async Task<HttpClient> LoginNewClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = AdminEmail, password = AdminPassword });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Two independent, already-authenticated staff sessions for a concurrency-race test to fire requests from — pulled from the pool rather than logging in fresh.</summary>
    public (HttpClient First, HttpClient Second) GetRaceClients() => (_raceClient1, _raceClient2);

    public async Task<Guid> CreateClientAsync(string suffix, decimal creditLimit = 1_000_000m)
    {
        var response = await StaffClient.PostAsJsonAsync("/api/v1/clients", new
        {
            name = $"Test Client {suffix}",
            registrationNo = $"REG-{suffix}",
            currencyId = Guid.Parse(CurrencyId),
            creditLimit,
            paymentTermsDays = 30
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    public async Task<Guid> CreateSubcontractorAsync(string suffix)
    {
        var response = await StaffClient.PostAsJsonAsync("/api/v1/subcontractors", new
        {
            name = $"Test Sub {suffix}",
            registrationNo = $"REG-{suffix}",
            currencyId = Guid.Parse(CurrencyId),
            paymentTermsDays = 30
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    public async Task<Guid> CreateLoadAsync(Guid clientId, string referenceNo, string? creditOverrideReason = null)
    {
        var response = await StaffClient.PostAsJsonAsync("/api/v1/loads", new
        {
            clientId,
            referenceNo,
            loadTypeId = Guid.Parse(LoadTypeId),
            creditOverrideReason
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    /// <summary>An own-fleet leg using the seeded demo vehicle/driver — the same fixture id pair used throughout this project's manual verification.</summary>
    public async Task<HttpResponseMessage> AddOwnFleetLegAsync(Guid loadId, int sequenceNo = 1, Guid? vehicleId = null, Guid? driverId = null) =>
        await StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs", new
        {
            sequenceNo,
            originLocationId = Guid.Parse(OriginLocationId),
            destinationLocationId = Guid.Parse(DestinationLocationId),
            executionType = 0, // OwnFleet
            costCentreId = Guid.Parse(CostCentreId),
            vehicleId = vehicleId ?? Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            driverId = driverId ?? Guid.Parse("a05273b3-36b7-454a-9029-7b09a3068db0")
        });

    public async Task<HttpResponseMessage> AddSubcontractedLegAsync(Guid loadId, Guid subcontractorId, int sequenceNo = 1) =>
        await StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs", new
        {
            sequenceNo,
            originLocationId = Guid.Parse(OriginLocationId),
            destinationLocationId = Guid.Parse(DestinationLocationId),
            executionType = 1, // Subcontracted
            costCentreId = Guid.Parse(CostCentreId),
            subcontractorId
        });

    public async Task<HttpResponseMessage> AddCommodityLineAsync(
        Guid loadId, Guid legId, decimal sellRatePerUnit, decimal? buyRatePerUnit = null, string? creditOverrideReason = null) =>
        await StaffClient.PostAsJsonAsync($"/api/v1/loads/{loadId}/legs/{legId}/commodity-lines", new
        {
            commodityId = Guid.Parse(CommodityId),
            quantity = 1,
            unitOfMeasureId = Guid.Parse(UnitOfMeasureId),
            sellRatePerUnit,
            buyRatePerUnit,
            creditOverrideReason
        });

    /// <summary>Books a load, adds one leg (own-fleet unless a subcontractorId is given), and adds one commodity line — the common case most tests just need a working leg for.</summary>
    public async Task<(Guid LoadId, Guid LegId)> CreateBookedLoadWithLegAsync(
        Guid clientId, string referenceNo, decimal sellRatePerUnit = 500, Guid? subcontractorId = null, decimal? buyRatePerUnit = null)
    {
        var loadId = await CreateLoadAsync(clientId, referenceNo);
        var legResponse = subcontractorId is Guid subId
            ? await AddSubcontractedLegAsync(loadId, subId)
            : await AddOwnFleetLegAsync(loadId);
        legResponse.EnsureSuccessStatusCode();
        var legId = (await legResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var commodityResponse = await AddCommodityLineAsync(loadId, legId, sellRatePerUnit, buyRatePerUnit);
        commodityResponse.EnsureSuccessStatusCode();

        return (loadId, legId);
    }

    public async Task DeliverLegAsync(Guid loadId, Guid legId)
    {
        (await StaffClient.PostAsync($"/api/v1/loads/{loadId}/legs/{legId}/start", null)).EnsureSuccessStatusCode();
        (await StaffClient.PostAsync($"/api/v1/loads/{loadId}/legs/{legId}/deliver", null)).EnsureSuccessStatusCode();
    }

    public async Task<Guid> FindFunctionIdAsync(string code)
    {
        var functions = await StaffClient.GetFromJsonAsync<List<FunctionDto>>("/api/v1/functions");
        var match = functions!.FirstOrDefault(f => f.Code == code);
        if (match is null) throw new InvalidOperationException($"Seeded function '{code}' was not found — has DevelopmentSeeder changed?");
        return match.Id;
    }

    /// <summary>
    /// Closing the Open period consumes a real, finite resource (the financial
    /// calendar), so a test suite that runs repeatedly against the same database
    /// would eventually exhaust the pre-seeded future periods. This keeps the
    /// calendar self-sustaining: if nothing picks up immediately where the latest
    /// known period leaves off, create a fresh FinancialYear starting the day after.
    /// </summary>
    public async Task EnsureFutureFinancialPeriodExistsAsync()
    {
        var years = (await StaffClient.GetFromJsonAsync<List<FinancialYearDto>>("/api/v1/financial-years"))!;
        var latestEnd = years.Max(y => y.EndDate);
        var hasImmediateFollowOn = years.Any(y => y.StartDate == latestEnd.AddDays(1));
        if (hasImmediateFollowOn) return;

        var start = latestEnd.AddDays(1);
        var response = await StaffClient.PostAsJsonAsync("/api/v1/financial-years", new
        {
            yearLabel = $"Test FY {start.Year}-{start.AddYears(1).Year}",
            startDate = start,
            endDate = start.AddYears(1).AddDays(-1),
            periodCount = 12
        });
        response.EnsureSuccessStatusCode();
    }

    public async Task<Guid> FindRoleIdAsync(string name)
    {
        var roles = await StaffClient.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var match = roles!.FirstOrDefault(r => r.Name == name);
        if (match is null) throw new InvalidOperationException($"Role '{name}' was not found.");
        return match.Id;
    }

    private sealed record LoginResponseDto(string AccessToken);
    private sealed record IdDto(Guid Id);
    private sealed record FunctionDto(Guid Id, string Code, string Description);
    private sealed record RoleDto(Guid Id, string Name);
    private sealed record FinancialYearDto(Guid Id, DateOnly StartDate, DateOnly EndDate);
}

[CollectionDefinition(Name)]
public class StaffTestCollection : ICollectionFixture<StaffTestFixture>
{
    public const string Name = "Staff business-logic tests";
}
