using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// Partner webhook subscriptions and delivery (§11.2/§11.3) — WebhookTestReceiver stands
/// in for a partner's callback with a real local HTTP listener, so these assert on the
/// actual signed request sent, not a mocked stand-in for it. Only the three events this
/// pass wires up (invoice.issued, creditnote.issued, subcontractor_expense.available_for_export)
/// are covered end to end; the rest are registerable but not yet published anywhere.
/// </summary>
[Collection(StaffTestCollection.Name)]
public class WebhookTests
{
    private readonly StaffTestFixture _fx;

    public WebhookTests(StaffTestFixture fx) => _fx = fx;

    private async Task<(Guid Id, string Secret)> SubscribeAsync(string eventType, string callbackUrl)
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/webhooks/subscriptions", new { eventType, callbackUrl });
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<CreateSubscriptionDto>();
        return (dto!.Id, dto.Secret);
    }

    private async Task<Guid> IssuePodReceivedInvoiceAsync(decimal sellRatePerUnit = 500)
    {
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var (loadId, legId) = await _fx.CreateBookedLoadWithLegAsync(clientId, $"WH-{Guid.NewGuid():N}", sellRatePerUnit);
        await _fx.DeliverLegAsync(loadId, legId);
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/legs/{legId}/debrief",
            new { podReceived = true, podImageUrl = "https://example.com/pod.jpg" })).EnsureSuccessStatusCode();

        var generateResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/invoices/generate", new { clientId });
        generateResponse.EnsureSuccessStatusCode();
        var invoice = await generateResponse.Content.ReadFromJsonAsync<InvoiceLike>();

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/invoices/{invoice!.Id}/issue", new { })).EnsureSuccessStatusCode();
        return invoice.Id;
    }

    private static string ComputeSignature(string secret, string body) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    [Fact]
    public async Task Issuing_an_invoice_delivers_a_correctly_signed_payload_to_the_subscribed_callback()
    {
        using var receiver = new WebhookTestReceiver();
        var (subscriptionId, secret) = await SubscribeAsync("invoice.issued", receiver.Url);

        var invoiceId = await IssuePodReceivedInvoiceAsync();

        var received = Assert.Single(receiver.Requests);
        Assert.Contains($"\"entityId\":\"{invoiceId}\"", received.Body);
        Assert.Contains("\"eventType\":\"invoice.issued\"", received.Body);
        Assert.Equal(ComputeSignature(secret, received.Body), received.Signature);

        var deliveries = await _fx.StaffClient.GetFromJsonAsync<List<DeliveryDto>>($"/api/v1/webhook-deliveries?subscriptionId={subscriptionId}");
        var delivery = Assert.Single(deliveries!);
        Assert.Equal(1, delivery.Status); // Delivered (Pending=0, Delivered=1, Failed=2)
        Assert.Equal(200, delivery.ResponseStatusCode);
    }

    [Fact]
    public async Task A_subscription_for_a_different_event_type_receives_nothing()
    {
        using var receiver = new WebhookTestReceiver();
        await SubscribeAsync("creditnote.issued", receiver.Url);

        await IssuePodReceivedInvoiceAsync();

        Assert.Empty(receiver.Requests);
    }

    [Fact]
    public async Task A_disabled_subscription_receives_nothing_and_queues_no_delivery()
    {
        using var receiver = new WebhookTestReceiver();
        var (subscriptionId, _) = await SubscribeAsync("invoice.issued", receiver.Url);
        (await _fx.StaffClient.PostAsync($"/api/v1/webhooks/subscriptions/{subscriptionId}/disable", null)).EnsureSuccessStatusCode();

        await IssuePodReceivedInvoiceAsync();

        Assert.Empty(receiver.Requests);
        var deliveries = await _fx.StaffClient.GetFromJsonAsync<List<DeliveryDto>>($"/api/v1/webhook-deliveries?subscriptionId={subscriptionId}");
        Assert.Empty(deliveries!);
    }

    [Fact]
    public async Task A_failed_delivery_is_recorded_and_a_later_retry_succeeds()
    {
        using var receiver = new WebhookTestReceiver();
        receiver.RespondWith(500);
        var (subscriptionId, _) = await SubscribeAsync("invoice.issued", receiver.Url);

        await IssuePodReceivedInvoiceAsync();

        var deliveries = await _fx.StaffClient.GetFromJsonAsync<List<DeliveryDto>>($"/api/v1/webhook-deliveries?subscriptionId={subscriptionId}&status=Failed");
        var delivery = Assert.Single(deliveries!);
        Assert.Equal(2, delivery.Status); // Failed
        Assert.Equal(500, delivery.ResponseStatusCode);

        receiver.RespondWith(200);
        var retryResponse = await _fx.StaffClient.PostAsync($"/api/v1/webhook-deliveries/{delivery.Id}/retry", null);
        retryResponse.EnsureSuccessStatusCode();
        var retried = await retryResponse.Content.ReadFromJsonAsync<DeliveryDto>();

        Assert.Equal(1, retried!.Status); // Delivered
        Assert.Equal(2, receiver.Requests.Count); // the original attempt plus the retry
    }

    [Fact]
    public async Task Issuing_a_standalone_credit_note_delivers_creditnote_issued()
    {
        using var receiver = new WebhookTestReceiver();
        await SubscribeAsync("creditnote.issued", receiver.Url);

        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        var createResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/credit-notes", new
        {
            clientId,
            reason = "Webhook wiring test",
            lines = new[] { new { description = "Goodwill adjustment", amount = 100m } }
        });
        createResponse.EnsureSuccessStatusCode();
        var creditNote = await createResponse.Content.ReadFromJsonAsync<IdLike>();

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/credit-notes/{creditNote!.Id}/issue", new { })).EnsureSuccessStatusCode();

        var received = Assert.Single(receiver.Requests);
        Assert.Contains($"\"entityId\":\"{creditNote.Id}\"", received.Body);
        Assert.Contains("\"eventType\":\"creditnote.issued\"", received.Body);
    }

    [Fact]
    public async Task Matching_a_supplier_invoice_delivers_subcontractor_expense_available_for_export()
    {
        using var receiver = new WebhookTestReceiver();
        await SubscribeAsync("subcontractor_expense.available_for_export", receiver.Url);

        var subcontractorId = await _fx.CreateSubcontractorAsync(Guid.NewGuid().ToString("N")[..8]);
        var clientId = await _fx.CreateClientAsync(Guid.NewGuid().ToString("N")[..8]);
        await _fx.CreateBookedLoadWithLegAsync(clientId, $"WHACCR-{Guid.NewGuid():N}", subcontractorId: subcontractorId, buyRatePerUnit: 300);
        var accrual = (await _fx.StaffClient.GetFromJsonAsync<List<AccrualLike>>($"/api/v1/accruals?subcontractorId={subcontractorId}"))!.Single();

        var invoiceResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/supplier-invoices", new
        {
            subcontractorId,
            supplierInvoiceNumber = $"WH-{Guid.NewGuid():N}"[..15],
            invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            receivedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            amount = 300
        });
        invoiceResponse.EnsureSuccessStatusCode();
        var supplierInvoiceId = (await invoiceResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;

        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/supplier-invoices/{supplierInvoiceId}/match",
            new { accrualIds = new[] { accrual.Id } })).EnsureSuccessStatusCode();

        var received = Assert.Single(receiver.Requests);
        Assert.Contains("\"eventType\":\"subcontractor_expense.available_for_export\"", received.Body);
    }

    [Fact]
    public async Task An_unrecognised_event_type_is_rejected()
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/webhooks/subscriptions",
            new { eventType = "not.a.real.event", callbackUrl = "https://example.com/hook" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_callback_url_is_rejected()
    {
        var response = await _fx.StaffClient.PostAsJsonAsync("/api/v1/webhooks/subscriptions",
            new { eventType = "invoice.issued", callbackUrl = "not-a-url" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>List/Get stay open to any authenticated staff, the same read/write split as every other master-data resource (§11.5) — only Create/Disable are gated.</summary>
    [Fact]
    public async Task A_caller_without_the_manage_function_can_list_but_not_create_or_disable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/roles", new { name = $"No Webhook Manage Role {suffix}" });
        roleResponse.EnsureSuccessStatusCode();
        var roleId = (await roleResponse.Content.ReadFromJsonAsync<IdLike>())!.Id;
        var functionId = await _fx.FindFunctionIdAsync("vehicle.master.manage");
        (await _fx.StaffClient.PostAsJsonAsync($"/api/v1/roles/{roleId}/functions", new { functionId })).EnsureSuccessStatusCode();

        var clientResponse = await _fx.StaffClient.PostAsJsonAsync("/api/v1/api-clients", new { name = $"No Webhook Manage Client {suffix}", roleId });
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

        var listResponse = await scopedClient.GetAsync("/api/v1/webhooks/subscriptions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var createResponse = await scopedClient.PostAsJsonAsync("/api/v1/webhooks/subscriptions",
            new { eventType = "invoice.issued", callbackUrl = "https://example.com/hook" });
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    private sealed record CreateSubscriptionDto(Guid Id, string EventType, string CallbackUrl, int Status, string Secret);
    private sealed record DeliveryDto(Guid Id, Guid SubscriptionId, string EventType, int Status, int? ResponseStatusCode);
    private sealed record InvoiceLike(Guid Id, int Status, decimal TotalExVat);
    private sealed record AccrualLike(Guid Id, decimal EstimatedAmount, int Status);
    private sealed record IdLike(Guid Id);
    private sealed record CreateApiClientResponseDto(string ClientId, string ClientSecret);
    private sealed record TokenResponseDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken);
}
