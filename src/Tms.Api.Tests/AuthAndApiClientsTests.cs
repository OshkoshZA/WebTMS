using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>AuthController beyond the login path (§11.1) — refresh rotation, reuse detection, logout — plus the OAuth2 client-credentials grant and ApiClientsController's own lifecycle (§11.1/§11.2), none of which the rest of the suite happens to exercise (every other fixture only ever logs in as staff and never refreshes or provisions an integration partner).</summary>
[Collection(StaffTestCollection.Name)]
public class AuthAndApiClientsTests
{
    private readonly StaffTestFixture _fx;

    public AuthAndApiClientsTests(StaffTestFixture fx) => _fx = fx;

    private static async Task<LoginResponseDto> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = StaffTestFixture.AdminEmail, password = StaffTestFixture.AdminPassword });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponseDto>())!;
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_the_presented_one_cannot_be_reused()
    {
        var client = _fx.CreateAnonymousClient();
        var login = await LoginAsync(client);

        var refreshed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken });
        refreshed.EnsureSuccessStatusCode();
        var rotated = await refreshed.Content.ReadFromJsonAsync<RefreshResponseDto>();
        Assert.NotEqual(login.RefreshToken, rotated!.RefreshToken);

        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
    }

    /// <summary>Regression for the family-revocation rule (§11.1): presenting an already-rotated token doesn't just fail itself — it kills every other token descended from the same original login, so a stolen-and-replayed token can't be used to quietly ride alongside the legitimate session.</summary>
    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_family_including_the_legitimate_successor()
    {
        var client = _fx.CreateAnonymousClient();
        var login = await LoginAsync(client);

        var first = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken });
        first.EnsureSuccessStatusCode();
        var legitimateSuccessor = await first.Content.ReadFromJsonAsync<RefreshResponseDto>();

        // Replay the original, already-rotated token — reuse detected.
        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // The legitimate successor from `first` must be dead too — the whole family was revoked, not just the reused token.
        var afterFamilyRevoke = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = legitimateSuccessor!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterFamilyRevoke.StatusCode);
    }

    [Fact]
    public async Task Concurrent_refresh_calls_against_the_same_token_produce_exactly_one_success()
    {
        var client = _fx.CreateAnonymousClient();
        var login = await LoginAsync(client);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken })));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Logout_revokes_the_token_and_is_always_204_even_for_garbage_input()
    {
        var client = _fx.CreateAnonymousClient();
        var login = await LoginAsync(client);

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);

        // Never reveals whether a token was real — logout against nonsense is still 204.
        var garbage = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = "not-a-real-token" });
        Assert.Equal(HttpStatusCode.NoContent, garbage.StatusCode);
    }

    private async Task<(string ClientId, string ClientSecret)> CreateApiClientAsync(string suffix, string functionCode)
    {
        var roleResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/roles", new { name = $"ApiClient Test Role {suffix}" });
        roleResponse.EnsureSuccessStatusCode();
        var roleId = (await roleResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var functionId = await _fx.FindFunctionIdAsync(functionCode);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/roles/{roleId}/functions", new { functionId })).EnsureSuccessStatusCode();

        var clientResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/api-clients",
            new { name = $"Test Integration Partner {suffix}", roleId });
        clientResponse.EnsureSuccessStatusCode();
        var created = await clientResponse.Content.ReadFromJsonAsync<CreateApiClientResponseDto>();
        return (created!.ClientId, created.ClientSecret);
    }

    private async Task<HttpResponseMessage> RequestTokenAsync(string clientId, string clientSecret)
    {
        var client = _fx.CreateAnonymousClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        });
        return await client.PostAsync("/api/v1/auth/token", form);
    }

    [Fact]
    public async Task Client_credentials_grant_issues_a_token_scoped_to_only_the_role_it_was_created_with()
    {
        var (clientId, clientSecret) = await CreateApiClientAsync(Guid.NewGuid().ToString("N")[..8], "vehicle.master.manage");

        var tokenResponse = await RequestTokenAsync(clientId, clientSecret);
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponseDto>();
        Assert.Equal("Bearer", token!.TokenType);

        var apiClient = _fx.CreateAnonymousClient();
        apiClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);

        // Granted function: succeeds.
        var allowed = await apiClient.PostAsJsonAsync("/api/v1/vehicles", new
        {
            fleetNo = $"FL-{Guid.NewGuid():N}"[..12],
            registration = $"REG{Guid.NewGuid():N}"[..10],
            type = 0
        });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);

        // Ungranted function: refused, even though it's an otherwise-identical master-data action.
        var refused = await apiClient.PostAsJsonAsync("/api/v1/drivers", new
        {
            employeeNo = $"EMP-{Guid.NewGuid():N}"[..12],
            name = "Should Not Be Created",
            licenceCode = "C1"
        });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Wrong_secret_and_unknown_client_id_are_both_refused()
    {
        var (clientId, clientSecret) = await CreateApiClientAsync(Guid.NewGuid().ToString("N")[..8], "vehicle.master.manage");

        var wrongSecret = await RequestTokenAsync(clientId, clientSecret + "x");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);

        var unknownClient = await RequestTokenAsync($"nonexistent-{Guid.NewGuid():N}", clientSecret);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownClient.StatusCode);
    }

    [Fact]
    public async Task Rotating_a_secret_adds_a_new_working_one_without_revoking_the_old_one()
    {
        var (clientId, originalSecret) = await CreateApiClientAsync(Guid.NewGuid().ToString("N")[..8], "vehicle.master.manage");

        var clientResponse = await _fx.StaffClient.GetAsync($"/api/v1/api-clients");
        clientResponse.EnsureSuccessStatusCode();
        var clients = await clientResponse.Content.ReadFromJsonAsync<List<ApiClientDto>>();
        var apiClientId = clients!.Single(c => c.ClientId == clientId).Id;

        var rotateResponse = await _fx.StaffClient.PostAsync($"/api/v1/api-clients/{apiClientId}/secrets", null);
        rotateResponse.EnsureSuccessStatusCode();
        var newSecret = (await rotateResponse.Content.ReadFromJsonAsync<RotateSecretResponseDto>())!.ClientSecret;

        var withOldSecret = await RequestTokenAsync(clientId, originalSecret);
        Assert.Equal(HttpStatusCode.OK, withOldSecret.StatusCode);

        var withNewSecret = await RequestTokenAsync(clientId, newSecret);
        Assert.Equal(HttpStatusCode.OK, withNewSecret.StatusCode);
    }

    [Fact]
    public async Task Revoking_an_api_client_stops_it_from_getting_new_tokens()
    {
        var (clientId, clientSecret) = await CreateApiClientAsync(Guid.NewGuid().ToString("N")[..8], "vehicle.master.manage");

        var clientResponse = await _fx.StaffClient.GetAsync($"/api/v1/api-clients");
        clientResponse.EnsureSuccessStatusCode();
        var clients = await clientResponse.Content.ReadFromJsonAsync<List<ApiClientDto>>();
        var apiClientId = clients!.Single(c => c.ClientId == clientId).Id;

        (await _fx.StaffClient.PostAsync($"/api/v1/api-clients/{apiClientId}/revoke", null)).EnsureSuccessStatusCode();

        var afterRevoke = await RequestTokenAsync(clientId, clientSecret);
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task UpdateRateLimit_rejects_a_non_positive_value_and_accepts_a_valid_one()
    {
        var (clientId, _) = await CreateApiClientAsync(Guid.NewGuid().ToString("N")[..8], "vehicle.master.manage");
        var clientResponse = await _fx.StaffClient.GetAsync($"/api/v1/api-clients");
        clientResponse.EnsureSuccessStatusCode();
        var clients = await clientResponse.Content.ReadFromJsonAsync<List<ApiClientDto>>();
        var apiClientId = clients!.Single(c => c.ClientId == clientId).Id;

        var rejected = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/api-clients/{apiClientId}/rate-limit", new { rateLimitPerMinute = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var accepted = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/api-clients/{apiClientId}/rate-limit", new { rateLimitPerMinute = 120 });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    public async Task Functions_list_is_read_only_reference_data_containing_the_seeded_catalog()
    {
        var response = await _fx.StaffClient.GetFromJsonAsync<List<FunctionListDto>>("/api/v1/functions");
        Assert.NotEmpty(response!);
        Assert.Contains(response!, f => f.Code == "vehicle.master.manage");
    }

    private sealed record LoginResponseDto(string AccessToken, string RefreshToken);
    private sealed record RefreshResponseDto(string AccessToken, string RefreshToken);
    private sealed record IdDto(Guid Id);
    private sealed record CreateApiClientResponseDto(Guid Id, string Name, string ClientId, string ClientSecret);
    private sealed record RotateSecretResponseDto(string ClientSecret);
    private sealed record ApiClientDto(Guid Id, string Name, string ClientId);
    private sealed record TokenResponseDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType);
    private sealed record FunctionListDto(Guid Id, string Code, string Description);
}
