using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>Roles, functions, and users (§07) — the reserved role name, the two cross-tenant IDOR regressions, and the portal-Role provisioning-hygiene checks found this project.</summary>
[Collection(StaffTestCollection.Name)]
public class IdentityTests
{
    private readonly StaffTestFixture _fx;

    public IdentityTests(StaffTestFixture fx) => _fx = fx;

    /// <summary>Direct regression test for the critical fix in 2463fac: "PlatformSupport" is a pure name match that bypasses every tenant/company query filter — RolesController.Create must reserve it.</summary>
    [Theory]
    [InlineData("PlatformSupport")]
    [InlineData("platformsupport")]
    [InlineData("PLATFORMSUPPORT")]
    public async Task Creating_a_role_named_PlatformSupport_is_rejected_case_insensitively(string name)
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/roles", new { Name = name });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_ordinary_role_name_still_succeeds()
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/roles", new { Name = $"Ordinary Role {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>Regression test for 2463fac's RolesController.RevokeFunction fix: it must load the role through the tenant-filtered set first, not go straight to an unscoped RoleFunction lookup — a nonexistent RoleId must 404, not silently no-op or 500.</summary>
    [Fact]
    public async Task RevokeFunction_against_a_nonexistent_role_404s_and_normal_grant_revoke_still_works()
    {
        var roleResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/roles", new { Name = $"Grant Test Role {Guid.NewGuid():N}" });
        roleResponse.EnsureSuccessStatusCode();
        var roleId = (await roleResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;
        var functionId = await _fx.FindFunctionIdAsync("client.creditlimit.override");

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/roles/{roleId}/functions", new { functionId })).EnsureSuccessStatusCode();
        var revokeResponse = await _fx.StaffClient.DeleteAsync($"/api/v1/roles/{roleId}/functions/{functionId}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        var revokeOnMissingRole = await _fx.StaffClient.DeleteAsync($"/api/v1/roles/{Guid.NewGuid()}/functions/{functionId}");
        Assert.Equal(HttpStatusCode.NotFound, revokeOnMissingRole.StatusCode);
    }

    /// <summary>Regression test for 2463fac's UsersController.RemoveCompanyRole fix: it must load the target user through the tenant-filtered set first — a nonexistent target user must 404.</summary>
    [Fact]
    public async Task RemoveCompanyRole_against_a_nonexistent_user_404s_and_normal_add_remove_still_works()
    {
        var userResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/users", new
        {
            email = $"identity-test-{Guid.NewGuid():N}@example.com",
            password = "TestPass#2026",
            displayName = "Identity Test User"
        });
        userResponse.EnsureSuccessStatusCode();
        var userId = (await userResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;

        var companyId = await GetCompanyIdAsync();
        var roleId = await _fx.FindRoleIdAsync("Admin");

        var addResponse = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/users/{userId}/company-roles", new { companyId, roleId });
        addResponse.EnsureSuccessStatusCode();
        var companyRoleId = (await addResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;

        var removeResponse = await _fx.StaffClient.DeleteAsync($"/api/v1/users/{userId}/company-roles/{companyRoleId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var removeOnMissingUser = await _fx.StaffClient.DeleteAsync($"/api/v1/users/{Guid.NewGuid()}/company-roles/{companyRoleId}");
        Assert.Equal(HttpStatusCode.NotFound, removeOnMissingUser.StatusCode);
    }

    /// <summary>Regression test for 46b90d3's RolesController.GrantFunction fix: a Role already assigned to a portal contact must never gain a non-portal function afterward.</summary>
    [Fact]
    public async Task GrantFunction_refuses_a_non_portal_function_on_a_role_already_held_by_a_portal_contact()
    {
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var roleResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/roles", new { Name = $"Portal Role {Guid.NewGuid():N}" });
        roleResponse.EnsureSuccessStatusCode();
        var roleId = (await roleResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;
        var viewlegsFn = await _fx.FindFunctionIdAsync("portal.subcontractor.viewlegs");
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/roles/{roleId}/functions", new { functionId = viewlegsFn })).EnsureSuccessStatusCode();

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/subcontractors/{subcontractorId}/contacts", new
        {
            email = $"identity-portal-{Guid.NewGuid():N}@example.com",
            password = "TestPass#2026",
            displayName = "Identity Portal Contact",
            roleId
        })).EnsureSuccessStatusCode();

        var internalFn = await _fx.FindFunctionIdAsync("finance.invoice.manage");
        var response = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/roles/{roleId}/functions", new { functionId = internalFn });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Regression test for 2463fac's contact-provisioning fix: a Role carrying any non-portal function must be rejected when assigned to a new Supplier Portal contact.</summary>
    [Fact]
    public async Task Creating_a_subcontractor_contact_with_a_non_portal_role_is_rejected()
    {
        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var adminRoleId = await _fx.FindRoleIdAsync("Admin");

        var response = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/subcontractors/{subcontractorId}/contacts", new
        {
            email = $"identity-badactor-{Guid.NewGuid():N}@example.com",
            password = "TestPass#2026",
            displayName = "Bad Actor",
            roleId = adminRoleId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Guid> GetCompanyIdAsync()
    {
        var companies = await _fx.StaffClient.GetFromJsonAsync<List<CompanyLike>>("/api/v1/companies");
        return companies!.First().Id;
    }

    private sealed record IdLike(Guid Id);
    private sealed record CompanyLike(Guid Id);
}
