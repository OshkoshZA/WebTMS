using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Audit;
using Tms.Modules.Loads;
using Tms.Modules.Rating;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateLoadRequest(Guid ClientId, string ReferenceNo, Guid LoadTypeId, string? CreditOverrideReason = null);

public record AddLoadLegRequest(
    int SequenceNo,
    Guid OriginLocationId,
    Guid DestinationLocationId,
    LoadLegExecutionType ExecutionType,
    Guid CostCentreId,
    Guid? VehicleId,
    Guid? DriverId);

public record AllocateLoadLegRequest(Guid VehicleId, Guid DriverId);

public record AddCommodityLineRequest(
    Guid CommodityId,
    decimal Quantity,
    Guid UnitOfMeasureId,
    decimal SellRatePerUnit,
    string? CreditOverrideReason = null);

public record HoldLoadRequest(string Reason);

public record LoadTrackingResponse(Guid LoadId, LoadStatus Status, IReadOnlyList<LoadLeg> Legs, IReadOnlyList<LoadStatusHistory> History);

/// <summary>
/// Load capture, the leg-based status lifecycle, and the credit-limit hard stop
/// (docs/architecture.html §5.1, §5.2, §5.4). AR Outstanding is still zero — see
/// the TODO in CreditExposureService — so today's check is WIP-only; it becomes
/// exact the moment Tms.Modules.Billing exists, with no change needed here.
///
/// The lifecycle stops short of PodReceived/Invoiced/Closed: those depend on
/// Debrief and Billing (Phase 2/3), which don't exist yet. Booked → Allocated →
/// InTransit → Delivered, plus the On Hold and Cancelled exception branches, are
/// fully wired; the rest picks up once those modules land.
/// </summary>
[ApiController]
[Route("api/v1/loads")]
[Authorize]
public class LoadsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly CreditExposureService _creditExposure;

    public LoadsController(
        TmsDbContext db,
        ITenantContext tenantContext,
        ICurrentUserAccessor currentUser,
        CreditExposureService creditExposure)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _creditExposure = creditExposure;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Load>>> List(CancellationToken ct)
        => Ok(await _db.Loads.OrderByDescending(l => l.Id).ToListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<Load>> Create(CreateLoadRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId, ct);
        if (client is null) return NotFound($"Client {request.ClientId} was not found.");

        // A brand-new load carries no sell value yet, so the only thing worth
        // checking here is whether the client is *already* over limit from prior
        // loads — in which case starting another one is refused outright too.
        var creditCheck = await CheckCreditAsync(client, additionalAmount: 0m, request.CreditOverrideReason, ct);
        if (creditCheck is not null) return creditCheck;

        var load = new Load
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            ClientId = request.ClientId,
            ReferenceNo = request.ReferenceNo,
            LoadTypeId = request.LoadTypeId,
            Status = LoadStatus.Booked
        };

        _db.Loads.Add(load);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = load.Id }, load);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Load>> Get(Guid id, CancellationToken ct)
    {
        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        return load is null ? NotFound() : Ok(load);
    }

    [HttpGet("{id:guid}/tracking")]
    public async Task<ActionResult<LoadTrackingResponse>> Tracking(Guid id, CancellationToken ct)
    {
        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();

        var history = await _db.LoadStatusHistories
            .Where(h => h.LoadId == id)
            .OrderBy(h => h.ChangedAt)
            .ToListAsync(ct);

        return Ok(new LoadTrackingResponse(load.Id, load.Status, load.Legs, history));
    }

    [HttpPost("{id:guid}/legs")]
    public async Task<ActionResult<LoadLeg>> AddLeg(Guid id, AddLoadLegRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();

        var leg = new LoadLeg
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            LoadId = id,
            SequenceNo = request.SequenceNo,
            OriginLocationId = request.OriginLocationId,
            DestinationLocationId = request.DestinationLocationId,
            ExecutionType = request.ExecutionType,
            CostCentreId = request.CostCentreId,
            VehicleId = request.VehicleId,
            DriverId = request.DriverId,
            Status = request.VehicleId is not null && request.DriverId is not null
                ? LoadLegStatus.Allocated
                : LoadLegStatus.Planned
        };

        _db.LoadLegs.Add(leg);
        load.Legs.Add(leg);

        await RecomputeLoadStatusAsync(load, ct);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = load.Id }, leg);
    }

    /// <summary>Assigns a vehicle and driver to a leg that was created without one yet, moving it from Planned to Allocated.</summary>
    [HttpPost("{id:guid}/legs/{legId:guid}/allocate")]
    public async Task<IActionResult> AllocateLeg(Guid id, Guid legId, AllocateLoadLegRequest request, CancellationToken ct)
    {
        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();

        var leg = load.Legs.FirstOrDefault(l => l.Id == legId);
        if (leg is null) return NotFound($"Leg {legId} was not found on load {id}.");
        if (leg.Status != LoadLegStatus.Planned)
            return Conflict($"Leg is {leg.Status}; only a Planned leg can be allocated.");

        leg.VehicleId = request.VehicleId;
        leg.DriverId = request.DriverId;
        leg.Status = LoadLegStatus.Allocated;

        await RecomputeLoadStatusAsync(load, ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Marks a leg as under way. Requires the leg to already be Allocated (own fleet only, Phase 1 — §5.1).</summary>
    [HttpPost("{id:guid}/legs/{legId:guid}/start")]
    public async Task<IActionResult> StartLeg(Guid id, Guid legId, CancellationToken ct)
    {
        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();

        var leg = load.Legs.FirstOrDefault(l => l.Id == legId);
        if (leg is null) return NotFound($"Leg {legId} was not found on load {id}.");
        if (leg.Status != LoadLegStatus.Allocated)
            return Conflict($"Leg is {leg.Status}; only an Allocated leg can start.");
        if (load.Status == LoadStatus.OnHold)
            return Conflict("Load is On Hold; release it before starting any further legs.");

        leg.Status = LoadLegStatus.InTransit;

        await RecomputeLoadStatusAsync(load, ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Marks a leg as physically delivered — the load overall reaches Delivered once every leg has (§5.2).</summary>
    [HttpPost("{id:guid}/legs/{legId:guid}/deliver")]
    public async Task<IActionResult> DeliverLeg(Guid id, Guid legId, CancellationToken ct)
    {
        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();

        var leg = load.Legs.FirstOrDefault(l => l.Id == legId);
        if (leg is null) return NotFound($"Leg {legId} was not found on load {id}.");
        if (leg.Status != LoadLegStatus.InTransit)
            return Conflict($"Leg is {leg.Status}; only an In Transit leg can be delivered.");

        leg.Status = LoadLegStatus.Delivered;

        await RecomputeLoadStatusAsync(load, ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Pauses a load for a query or dispute — only while In Transit (§5.2 Fig. 3).</summary>
    [HttpPost("{id:guid}/hold")]
    public async Task<IActionResult> Hold(Guid id, HoldLoadRequest request, CancellationToken ct)
    {
        var load = await _db.Loads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();
        if (load.Status != LoadStatus.InTransit)
            return Conflict($"Load is {load.Status}; only a load In Transit can be put On Hold.");

        await TransitionLoadStatusAsync(load, LoadStatus.OnHold, request.Reason, ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Resumes a load that was On Hold, returning it to In Transit (§5.2 Fig. 3).</summary>
    [HttpPost("{id:guid}/release-hold")]
    public async Task<IActionResult> ReleaseHold(Guid id, CancellationToken ct)
    {
        var load = await _db.Loads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();
        if (load.Status != LoadStatus.OnHold)
            return Conflict($"Load is {load.Status}, not On Hold.");

        await TransitionLoadStatusAsync(load, LoadStatus.InTransit, reason: null, ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Cancels a load — only while it's still Booked or Allocated (§5.2 Fig. 3); once execution starts, it can no longer be cancelled outright.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var load = await _db.Loads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();
        if (load.Status != LoadStatus.Booked && load.Status != LoadStatus.Allocated)
            return Conflict($"Load is {load.Status}; only Booked or Allocated loads can be cancelled.");

        await TransitionLoadStatusAsync(load, LoadStatus.Cancelled, reason: null, ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Adds one commodity/quantity line to a leg and its sell rate line (docs/architecture.html
    /// §5.5, §08) — the point at which a load actually starts carrying sell value, and so the
    /// point at which the credit-limit hard stop (§5.4) has something real to check.
    /// </summary>
    [HttpPost("{id:guid}/legs/{legId:guid}/commodity-lines")]
    public async Task<ActionResult<CommodityLine>> AddCommodityLine(Guid id, Guid legId, AddCommodityLineRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var load = await _db.Loads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound($"Load {id} was not found.");

        var leg = await _db.LoadLegs.FirstOrDefaultAsync(l => l.Id == legId && l.LoadId == id, ct);
        if (leg is null) return NotFound($"Leg {legId} was not found on load {id}.");

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == load.ClientId, ct);
        if (client is null) return NotFound($"Client {load.ClientId} was not found.");

        var sellAmount = request.Quantity * request.SellRatePerUnit;

        var creditCheck = await CheckCreditAsync(client, sellAmount, request.CreditOverrideReason, ct);
        if (creditCheck is not null) return creditCheck;

        var commodityLine = new CommodityLine
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            LoadLegId = legId,
            CommodityId = request.CommodityId,
            Quantity = request.Quantity,
            UnitOfMeasureId = request.UnitOfMeasureId
        };
        _db.CommodityLines.Add(commodityLine);

        _db.Set<RateLine>().Add(new RateLine
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            SourceType = RateLineSourceType.CommodityLine,
            SourceId = commodityLine.Id,
            Direction = RateLineDirection.Sell,
            RatePerUnit = request.SellRatePerUnit,
            UnitOfMeasureId = request.UnitOfMeasureId,
            Quantity = request.Quantity,
            Amount = sellAmount
        });

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id }, commodityLine);
    }

    /// <summary>
    /// Recomputes Load.Status from its legs' individual statuses and, if it changed,
    /// records the transition (§5.2). Never touches On Hold or Cancelled — those are
    /// exception states only a manual action (Hold/ReleaseHold/Cancel) can enter or leave.
    /// </summary>
    private async Task RecomputeLoadStatusAsync(Load load, CancellationToken ct)
    {
        if (load.Status is LoadStatus.OnHold or LoadStatus.Cancelled)
            return;

        var legs = load.Legs;
        var next = legs.Count == 0
            ? LoadStatus.Booked
            : legs.All(l => l.Status == LoadLegStatus.Delivered) ? LoadStatus.Delivered
            : legs.Any(l => l.Status is LoadLegStatus.InTransit or LoadLegStatus.Delivered) ? LoadStatus.InTransit
            : legs.All(l => l.Status == LoadLegStatus.Allocated) ? LoadStatus.Allocated
            : LoadStatus.Booked;

        if (next != load.Status)
            await TransitionLoadStatusAsync(load, next, reason: null, ct);
    }

    private Task TransitionLoadStatusAsync(Load load, LoadStatus next, string? reason, CancellationToken ct)
    {
        _db.LoadStatusHistories.Add(new LoadStatusHistory
        {
            TenantId = load.TenantId,
            CompanyId = load.CompanyId,
            LoadId = load.Id,
            FromStatus = load.Status,
            ToStatus = next,
            ChangedByUserId = _currentUser.UserId ?? Guid.Empty,
            Reason = reason
        });
        load.Status = next;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs the §5.4 hard stop. Returns null when the action is allowed to proceed;
    /// otherwise the ActionResult to return directly. An override reason bypasses the
    /// stop and is written to the audit trail — but is only gated behind the Admin role
    /// for now. TODO (§07): gate this behind the real client.creditlimit.override
    /// function once function-based authorization policies exist; a role check is a
    /// stand-in, not the real thing.
    /// </summary>
    private async Task<ActionResult?> CheckCreditAsync(
        Tms.Modules.Loads.Client client,
        decimal additionalAmount,
        string? overrideReason,
        CancellationToken ct)
    {
        var status = await _creditExposure.GetStatusAsync(client, ct);
        if (!CreditExposureService.WouldBreach(status, additionalAmount))
            return null;

        if (!string.IsNullOrWhiteSpace(overrideReason))
        {
            if (!User.IsInRole("Admin"))
                return Forbid("Overriding the credit limit requires the client.creditlimit.override function.");

            _db.Set<AuditEntry>().Add(new AuditEntry
            {
                TenantId = client.TenantId,
                CompanyId = client.CompanyId,
                EntityType = nameof(Tms.Modules.Loads.Client),
                EntityId = client.Id.ToString(),
                Action = AuditAction.Override,
                ChangedByUserId = _currentUser.UserId,
                Reason = overrideReason,
                NewValueJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    status.CreditLimit,
                    status.TotalExposure,
                    additionalAmount,
                    Projected = status.TotalExposure + additionalAmount
                })
            });

            return null; // override accepted — allow the caller to proceed
        }

        return new ObjectResult(new ProblemDetails
        {
            Title = "Credit limit exceeded",
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = $"Client '{client.Name}' has {status.AvailableCredit:N2} available credit " +
                     $"(limit {status.CreditLimit:N2}, exposure {status.TotalExposure:N2}) " +
                     $"but this action would add {additionalAmount:N2}.",
            Extensions =
            {
                ["creditLimit"] = status.CreditLimit,
                ["arOutstanding"] = status.ArOutstanding,
                ["wip"] = status.Wip,
                ["totalExposure"] = status.TotalExposure,
                ["availableCredit"] = status.AvailableCredit,
                ["requestedAmount"] = additionalAmount
            }
        })
        { StatusCode = StatusCodes.Status422UnprocessableEntity };
    }
}
