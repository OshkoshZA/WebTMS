using System.Security.Cryptography;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Integration;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateWebhookSubscriptionRequest(string EventType, string CallbackUrl);
public record WebhookSubscriptionResponse(Guid Id, string EventType, string CallbackUrl, WebhookSubscriptionStatus Status);
public record CreateWebhookSubscriptionResponse(Guid Id, string EventType, string CallbackUrl, WebhookSubscriptionStatus Status, string Secret);

/// <summary>
/// Partner webhook registration (docs/architecture.html §11.2/§11.3) — Secret is
/// returned once, here, and never again, the same convention as ApiClientsController's
/// own ClientSecret. Unlike ApiClientSecret, it's stored encrypted rather than hashed
/// (see TmsDbContext's Tms.WebhookSecret.v1 protector) because it has to be recoverable
/// in plaintext to sign every future delivery, not just verified once at login.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webhooks/subscriptions")]
[Authorize]
public class WebhookSubscriptionsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public WebhookSubscriptionsController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>Never part of either portal's documented scope — external-partner integration management, so any portal contact is Forbidden outright.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WebhookSubscriptionResponse>>> List(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var subscriptions = await _db.WebhookSubscriptions.OrderBy(s => s.EventType).ToListAsync(ct);
        return Ok(subscriptions.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WebhookSubscriptionResponse>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var subscription = await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        return subscription is null ? NotFound() : Ok(ToResponse(subscription));
    }

    [HttpPost]
    [Authorize(Policy = "integration.webhook.manage")]
    public async Task<ActionResult<CreateWebhookSubscriptionResponse>> Create(CreateWebhookSubscriptionRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (!WebhookEventTypes.All.Contains(request.EventType))
            return BadRequest($"'{request.EventType}' is not a recognised event type.");
        if (!Uri.TryCreate(request.CallbackUrl, UriKind.Absolute, out var callbackUri) || callbackUri.Scheme is not ("http" or "https"))
            return BadRequest("CallbackUrl must be an absolute http or https URL.");

        var plaintextSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var subscription = new WebhookSubscription
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            EventType = request.EventType,
            CallbackUrl = request.CallbackUrl,
            Secret = plaintextSecret
        };

        _db.WebhookSubscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = subscription.Id },
            new CreateWebhookSubscriptionResponse(subscription.Id, subscription.EventType, subscription.CallbackUrl, subscription.Status, plaintextSecret));
    }

    /// <summary>Never a hard delete (§11.5) — a disabled subscription stops receiving new deliveries, but its delivery history stays intact.</summary>
    [HttpPost("{id:guid}/disable")]
    [Authorize(Policy = "integration.webhook.manage")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        var subscription = await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subscription is null) return NotFound();

        subscription.Status = WebhookSubscriptionStatus.Disabled;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static WebhookSubscriptionResponse ToResponse(WebhookSubscription s) => new(s.Id, s.EventType, s.CallbackUrl, s.Status);
}
