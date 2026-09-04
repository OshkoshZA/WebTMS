using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>A Company's per-category retention configuration (§14.2) — RetentionPoliciesController.</summary>
[Collection(StaffTestCollection.Name)]
public class RetentionPolicyTests
{
    private readonly StaffTestFixture _fx;

    public RetentionPolicyTests(StaffTestFixture fx) => _fx = fx;

    private async Task<Guid> GetCompanyIdAsync()
    {
        var companies = await _fx.StaffClient.GetFromJsonAsync<List<CompanyDto>>("/api/v1/companies");
        return companies!.First().Id;
    }

    [Fact]
    public async Task Setting_a_policy_set_and_reading_it_back_round_trips()
    {
        var companyId = await GetCompanyIdAsync();

        var setResponse = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/companies/{companyId}/retention-policies", new[]
        {
            new { dataCategory = 1, retentionPeriodYears = 5, legalBasis = "Labour law", anonymizeAfterExpiry = true }, // DriverPersonalData
            new { dataCategory = 3, retentionPeriodYears = 7, legalBasis = "Same as underlying record", anonymizeAfterExpiry = true } // AuditTrail
        });
        setResponse.EnsureSuccessStatusCode();

        var getResponse = await _fx.StaffClient.GetFromJsonAsync<List<RetentionPolicyDto>>($"/api/v1/companies/{companyId}/retention-policies");
        Assert.Equal(2, getResponse!.Count);
        Assert.Contains(getResponse, p => p.DataCategory == 1 && p.RetentionPeriodYears == 5 && p.LegalBasis == "Labour law");
        Assert.Contains(getResponse, p => p.DataCategory == 3 && p.RetentionPeriodYears == 7);
    }

    [Fact]
    public async Task A_second_set_replaces_the_first_dropping_omitted_categories_and_updating_the_rest()
    {
        var companyId = await GetCompanyIdAsync();

        (await _fx.StaffClient.PutAsJsonAsync($"/api/v1/companies/{companyId}/retention-policies", new[]
        {
            new { dataCategory = 0, retentionPeriodYears = 5, legalBasis = "Tax law", anonymizeAfterExpiry = false }, // FinancialRecords
            new { dataCategory = 2, retentionPeriodYears = 1, legalBasis = "Commercial relationship", anonymizeAfterExpiry = true } // PortalContactData
        })).EnsureSuccessStatusCode();

        var secondSet = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/companies/{companyId}/retention-policies", new[]
        {
            new { dataCategory = 0, retentionPeriodYears = 10, legalBasis = "Tax law, revised", anonymizeAfterExpiry = false } // FinancialRecords only
        });
        secondSet.EnsureSuccessStatusCode();

        var afterSecondSet = await _fx.StaffClient.GetFromJsonAsync<List<RetentionPolicyDto>>($"/api/v1/companies/{companyId}/retention-policies");
        var financial = Assert.Single(afterSecondSet!);
        Assert.Equal(10, financial.RetentionPeriodYears);
        Assert.Equal("Tax law, revised", financial.LegalBasis);
    }

    [Fact]
    public async Task A_zero_or_negative_retention_period_is_rejected()
    {
        var companyId = await GetCompanyIdAsync();

        var response = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/companies/{companyId}/retention-policies", new[]
        {
            new { dataCategory = 1, retentionPeriodYears = 0, legalBasis = "Labour law", anonymizeAfterExpiry = true }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submitting_the_same_category_twice_is_rejected()
    {
        var companyId = await GetCompanyIdAsync();

        var response = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/companies/{companyId}/retention-policies", new[]
        {
            new { dataCategory = 1, retentionPeriodYears = 5, legalBasis = "Labour law", anonymizeAfterExpiry = true },
            new { dataCategory = 1, retentionPeriodYears = 6, legalBasis = "Labour law, again", anonymizeAfterExpiry = true }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Setting_policies_for_a_nonexistent_company_404s()
    {
        var response = await _fx.StaffClient.PutAsJsonAsync($"/api/v1/companies/{Guid.NewGuid()}/retention-policies", new[]
        {
            new { dataCategory = 1, retentionPeriodYears = 5, legalBasis = "Labour law", anonymizeAfterExpiry = true }
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>List/Get stays open to any authenticated staff, the same read/write split as every other master-data resource (§11.5) — only Set is gated.</summary>
    [Fact]
    public async Task A_caller_without_the_manage_function_can_still_read_but_not_set()
    {
        var companyId = await GetCompanyIdAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var roleResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/roles", new { name = $"No Retention Manage Role {suffix}" });
        roleResponse.EnsureSuccessStatusCode();
        var roleId = (await roleResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;
        var functionId = await _fx.FindFunctionIdAsync("vehicle.master.manage");
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/roles/{roleId}/functions", new { functionId })).EnsureSuccessStatusCode();

        var clientResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/api-clients", new { name = $"No Retention Manage Client {suffix}", roleId });
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

        var getResponse = await scopedClient.GetAsync($"/api/v1/companies/{companyId}/retention-policies");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var setResponse = await scopedClient.PutAsJsonAsync($"/api/v1/companies/{companyId}/retention-policies", new[]
        {
            new { dataCategory = 1, retentionPeriodYears = 5, legalBasis = "Labour law", anonymizeAfterExpiry = true }
        });
        Assert.Equal(HttpStatusCode.Forbidden, setResponse.StatusCode);
    }

    private sealed record CompanyDto(Guid Id);
    private sealed record RetentionPolicyDto(Guid Id, int DataCategory, int RetentionPeriodYears, string LegalBasis, bool AnonymizeAfterExpiry);
    private sealed record IdDto(Guid Id);
    private sealed record CreateApiClientResponseDto(string ClientId, string ClientSecret);
    private sealed record TokenResponseDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken);
}
