using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Integration;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record WebhookDeliveryResponse(
    Guid Id, Guid SubscriptionId, string EventType, string EntityType, string EntityId, DateTimeOffset OccurredAtUtc,
    WebhookDeliveryStatus Status, DateTimeOffset? AttemptedAtUtc, int? ResponseStatusCode, string? ErrorDetail);

/// <summary>
/// The read/replay side of §11.3's delivery tracking — mirrors §11.2's documented
/// AccountingSyncRecord shape (GET .../sync-records?status=Failed) for the same
/// "failed pushes awaiting review/retry" need, applied to webhooks instead.
/// There is no manual Create here — WebhookPublisher is the only writer of new rows;
/// this controller only reads them and, via Retry, re-attempts one.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webhook-deliveries")]
[Authorize]
public class WebhookDeliveriesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly WebhookDeliveryService _deliveryService;

    public WebhookDeliveriesController(TmsDbContext db, ITenantContext tenantContext, WebhookDeliveryService deliveryService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _deliveryService = deliveryService;
    }

    /// <summary>Never part of either portal's documented scope — external-partner integration management, so any portal contact is Forbidden outright.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WebhookDeliveryResponse>>> List(Guid? subscriptionId, WebhookDeliveryStatus? status, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var query = _db.WebhookDeliveries.AsQueryable();
        if (subscriptionId is Guid sid) query = query.Where(d => d.SubscriptionId == sid);
        if (status is WebhookDeliveryStatus st) query = query.Where(d => d.Status == st);

        var deliveries = await query.OrderByDescending(d => d.OccurredAtUtc).ToListAsync(ct);
        return Ok(deliveries.Select(ToResponse));
    }

    /// <summary>Re-attempts one delivery regardless of its current Status — an explicit, deliberate staff action, not something anything else does automatically (§11.3: no background retry worker exists).</summary>
    [HttpPost("{id:guid}/retry")]
    [Authorize(Policy = "integration.webhook.manage")]
    public async Task<ActionResult<WebhookDeliveryResponse>> Retry(Guid id, CancellationToken ct)
    {
        var succeeded = await _deliveryService.RetryAsync(id, ct);
        if (!succeeded) return NotFound();

        var delivery = await _db.WebhookDeliveries.FirstAsync(d => d.Id == id, ct);
        return Ok(ToResponse(delivery));
    }

    private static WebhookDeliveryResponse ToResponse(WebhookDelivery d) => new(
        d.Id, d.SubscriptionId, d.EventType, d.EntityType, d.EntityId, d.OccurredAtUtc,
        d.Status, d.AttemptedAtUtc, d.ResponseStatusCode, d.ErrorDetail);
}
