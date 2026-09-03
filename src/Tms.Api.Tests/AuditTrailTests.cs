using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>The audit.view-gated viewer/export API (§12.3) — capture itself (AuditSaveChangesInterceptor) already ran under every other test in this suite; these are the first tests of the read side built on top of it.</summary>
[Collection(StaffTestCollection.Name)]
public class AuditTrailTests
{
    private readonly StaffTestFixture _fx;

    public AuditTrailTests(StaffTestFixture fx) => _fx = fx;

    [Fact]
    public async Task Creating_a_client_produces_a_queryable_create_entry()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);

        var entries = await _fx.StaffClient.GetFromJsonAsync<List<AuditEntryDto>>(
            $"/api/v1/audit-entries?entityType=Client&entityId={clientId}");

        var createEntry = Assert.Single(entries!);
        Assert.Equal(0, createEntry.Action); // Create=0 (Create, Update, Delete, StatusChange, Approve, Override)
        Assert.NotNull(createEntry.ChangedByUserId);
        Assert.Contains(clientId.ToString(), createEntry.NewValueJson);
    }

    [Fact]
    public async Task Updating_a_vehicle_produces_a_second_entry_distinct_from_its_create()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/vehicles", new
        {
            fleetNo = $"AUD-{suffix}",
            registration = $"AUDREG{suffix}",
            type = 0
        });
        createResponse.EnsureSuccessStatusCode();
        var vehicleId = (await createResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        (await _fx.StaffClient.PutAsJsonAsync($"/api/v1/vehicles/{vehicleId}", new
        {
            fleetNo = $"AUD-{suffix}-updated",
            registration = $"AUDREG{suffix}",
            type = 0
        })).EnsureSuccessStatusCode();

        var entries = await _fx.StaffClient.GetFromJsonAsync<List<AuditEntryDto>>(
            $"/api/v1/audit-entries?entityType=Vehicle&entityId={vehicleId}");

        Assert.Equal(2, entries!.Count);
        Assert.Single(entries, e => e.Action == 0); // Create
        Assert.Single(entries, e => e.Action == 1); // Update
    }

    [Fact]
    public async Task Action_filter_narrows_to_only_that_action_type()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/cost-centres", new { code = $"AUDCC-{suffix}", name = "Audit Filter Test" });
        createResponse.EnsureSuccessStatusCode();
        var costCentreId = (await createResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var createOnly = await _fx.StaffClient.GetFromJsonAsync<List<AuditEntryDto>>(
            $"/api/v1/audit-entries?entityType=CostCentre&entityId={costCentreId}&action=0");
        Assert.Single(createOnly!);

        var updateOnly = await _fx.StaffClient.GetFromJsonAsync<List<AuditEntryDto>>(
            $"/api/v1/audit-entries?entityType=CostCentre&entityId={costCentreId}&action=1");
        Assert.Empty(updateOnly!);
    }

    [Fact]
    public async Task Export_returns_csv_with_a_matching_row_and_no_row_cap()
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);

        var response = await _fx.StaffClient.GetAsync($"/api/v1/audit-entries/export?entityType=Client&entityId={clientId}");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Id,CompanyId,EntityType,EntityId,Action,ChangedByUserId,ChangedByApiClientId,ChangedAtUtc,OldValueJson,NewValueJson,Reason", lines[0].TrimEnd('\r'));
        Assert.Single(lines.Skip(1), line => line.Contains(clientId.ToString()));
    }

    /// <summary>Regression-shaped: a caller holding some other function entirely, but not audit.view, must not be able to browse the audit trail — the same "granted function scopes the token" proof AuthAndApiClientsTests already establishes for a different function.</summary>
    [Fact]
    public async Task A_caller_without_audit_view_is_forbidden()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/roles", new { name = $"No Audit View Role {suffix}" });
        roleResponse.EnsureSuccessStatusCode();
        var roleId = (await roleResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var functionId = await _fx.FindFunctionIdAsync("vehicle.master.manage");
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/roles/{roleId}/functions", new { functionId })).EnsureSuccessStatusCode();

        var clientResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/api-clients", new { name = $"No Audit View Client {suffix}", roleId });
        clientResponse.EnsureSuccessStatusCode();
        var created = await clientResponse.Content.ReadFromJsonAsync<CreateApiClientResponseDto>();

        var tokenClient = _fx.CreateAnonymousClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = created!.ClientId,
            ["client_secret"] = created.ClientSecret
        });
        var tokenResponse = await tokenClient.PostAsync("/api/v1/auth/token", form);
        tokenResponse.EnsureSuccessStatusCode();
        var accessToken = (await tokenResponse.Content.ReadFromJsonAsync<TokenResponseDto>())!.AccessToken;

        var scopedClient = _fx.CreateAnonymousClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await scopedClient.GetAsync("/api/v1/audit-entries");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record AuditEntryDto(Guid Id, string EntityType, string EntityId, int Action, Guid? ChangedByUserId, string NewValueJson);
    private sealed record IdDto(Guid Id);
    private sealed record CreateApiClientResponseDto(string ClientId, string ClientSecret);
    private sealed record TokenResponseDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken);
}
