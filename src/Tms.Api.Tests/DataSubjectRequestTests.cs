using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// The data subject rights workflow (§14.3, Fig. 11) — logging, fulfilling, rejecting,
/// and exporting DataSubjectRequests. Scope for this pass: Access/Portability export the
/// subject's own core record only; Rectification is tracked here but corrected via the
/// subject's own existing Update endpoint; Erasure anonymizes identity fields and is
/// refused unless the subject is already Deactivated. Historical AuditEntry snapshots
/// are a deliberately deferred, separate piece of work.
/// </summary>
[Collection(StaffTestCollection.Name)]
public class DataSubjectRequestTests
{
    private readonly StaffTestFixture _fx;

    public DataSubjectRequestTests(StaffTestFixture fx) => _fx = fx;

    private async Task<Guid> CreateDriverAsync(string suffix)
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/drivers", new
        {
            employeeNo = $"DSR-{suffix}",
            name = "DSR Test Driver",
            licenceCode = "C1"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    [Fact]
    public async Task Create_rejects_a_subject_id_that_does_not_exist()
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/data-subject-requests",
            new { subjectType = 0, subjectId = Guid.NewGuid(), requestType = 0 }); // Driver, Access
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Access_request_is_fulfilled_then_exported_and_tracks_a_thirty_day_due_date()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var driverId = await CreateDriverAsync(suffix);

        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/data-subject-requests",
            new { subjectType = 0, subjectId = driverId, requestType = 0 }); // Driver, Access
        createResponse.EnsureSuccessStatusCode();
        var dsr = await createResponse.Content.ReadFromJsonAsync<DsrDto>();

        Assert.True((dsr!.ReceivedAt.AddDays(30) - dsr.DueDate).Duration() < TimeSpan.FromSeconds(1));

        (await _fx.StaffClient.PostAsync($"/api/v1/data-subject-requests/{dsr.Id}/fulfill", null)).EnsureSuccessStatusCode();

        var exportResponse = await _fx.StaffClient.GetAsync($"/api/v1/data-subject-requests/{dsr.Id}/export");
        exportResponse.EnsureSuccessStatusCode();
        var exported = await exportResponse.Content.ReadFromJsonAsync<DriverExportDto>();
        Assert.Equal("DSR Test Driver", exported!.Name);
    }

    [Fact]
    public async Task Export_refuses_a_request_that_is_not_yet_fulfilled()
    {
        var driverId = await CreateDriverAsync(Guid.NewGuid().ToString("N")[..8]);
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/data-subject-requests",
            new { subjectType = 0, subjectId = driverId, requestType = 0 });
        createResponse.EnsureSuccessStatusCode();
        var dsr = await createResponse.Content.ReadFromJsonAsync<DsrDto>();

        var response = await _fx.StaffClient.GetAsync($"/api/v1/data-subject-requests/{dsr!.Id}/export");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Rejecting_a_request_records_the_reason_and_refuses_a_second_resolution()
    {
        var driverId = await CreateDriverAsync(Guid.NewGuid().ToString("N")[..8]);
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/data-subject-requests",
            new { subjectType = 0, subjectId = driverId, requestType = 1 }); // Rectification
        createResponse.EnsureSuccessStatusCode();
        var dsr = await createResponse.Content.ReadFromJsonAsync<DsrDto>();

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/data-subject-requests/{dsr!.Id}/reject", new { rejectionReason = "Duplicate request" }))
            .EnsureSuccessStatusCode();

        var afterReject = await _fx.StaffClient.GetFromJsonAsync<DsrDto>($"/api/v1/data-subject-requests/{dsr.Id}");
        Assert.Equal(3, afterReject!.Status); // Rejected (Received=0, InProgress=1, Fulfilled=2, Rejected=3)
        Assert.Equal("Duplicate request", afterReject.RejectionReason);

        var secondReject = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/data-subject-requests/{dsr.Id}/reject", new { rejectionReason = "Too late" });
        Assert.Equal(HttpStatusCode.Conflict, secondReject.StatusCode);
    }

    [Fact]
    public async Task Erasure_is_refused_while_the_driver_is_still_active_and_succeeds_once_deactivated()
    {
        var driverId = await CreateDriverAsync(Guid.NewGuid().ToString("N")[..8]);
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/data-subject-requests",
            new { subjectType = 0, subjectId = driverId, requestType = 2 }); // Erasure
        createResponse.EnsureSuccessStatusCode();
        var dsr = await createResponse.Content.ReadFromJsonAsync<DsrDto>();

        var refused = await _fx.StaffClient.PostAsync($"/api/v1/data-subject-requests/{dsr!.Id}/fulfill", null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        (await _fx.StaffClient.PostAsync($"/api/v1/drivers/{driverId}/deactivate", null)).EnsureSuccessStatusCode();

        (await _fx.StaffClient.PostAsync($"/api/v1/data-subject-requests/{dsr.Id}/fulfill", null)).EnsureSuccessStatusCode();

        var driver = await _fx.StaffClient.GetFromJsonAsync<DriverExportDto>($"/api/v1/drivers/{driverId}");
        Assert.StartsWith("Erased Driver", driver!.Name);

        // Already Fulfilled — a second fulfill (or reject) must not silently re-run the erasure.
        var secondFulfill = await _fx.StaffClient.PostAsync($"/api/v1/data-subject-requests/{dsr.Id}/fulfill", null);
        Assert.Equal(HttpStatusCode.Conflict, secondFulfill.StatusCode);
    }

    [Fact]
    public async Task Erasure_of_a_client_contact_anonymizes_email_and_display_name_once_deactivated()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clientId = await _fx.CreateClientAsync(suffix);

        var roleResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/roles", new { name = $"DSR Portal Role {suffix}" });
        roleResponse.EnsureSuccessStatusCode();
        var roleId = (await roleResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;
        var functionId = await _fx.FindFunctionIdAsync("portal.client.viewloads");
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/roles/{roleId}/functions", new { functionId })).EnsureSuccessStatusCode();

        var contactResponse = await _fx.StaffClient.PostAsJsonAsync($"/api/v1/clients/{clientId}/contacts", new
        {
            email = $"dsr-{suffix}@example.com",
            password = "DsrTestPass#2026",
            displayName = "DSR Test Contact",
            roleId
        });
        contactResponse.EnsureSuccessStatusCode();
        var contactId = (await contactResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;

        (await _fx.StaffClient.PostAsync($"/api/v1/clients/{clientId}/contacts/{contactId}/deactivate", null)).EnsureSuccessStatusCode();

        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/data-subject-requests",
            new { subjectType = 1, subjectId = contactId, requestType = 2 }); // ClientContact, Erasure
        createResponse.EnsureSuccessStatusCode();
        var dsr = await createResponse.Content.ReadFromJsonAsync<DsrDto>();

        (await _fx.StaffClient.PostAsync($"/api/v1/data-subject-requests/{dsr!.Id}/fulfill", null)).EnsureSuccessStatusCode();

        var contacts = await _fx.StaffClient.GetFromJsonAsync<List<ClientContactDto>>($"/api/v1/clients/{clientId}/contacts");
        var contact = contacts!.Single(c => c.Id == contactId);
        Assert.Equal("Erased User", contact.DisplayName);
        Assert.StartsWith("erased-", contact.Email);
        Assert.EndsWith("@erased.local", contact.Email);
    }

    private sealed record IdDto(Guid Id);
    private sealed record DsrDto(Guid Id, int Status, DateTimeOffset ReceivedAt, DateTimeOffset DueDate, string? RejectionReason);
    private sealed record DriverExportDto(string Name);
    private sealed record ClientContactDto(Guid Id, string Email, string DisplayName);
}
