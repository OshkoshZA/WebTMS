using System.Data;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Audit;
using Tms.Modules.Billing;
using Tms.Modules.Exceptions;
using Tms.Modules.Fleet;
using Tms.Modules.Loads;
using Tms.Modules.Rating;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateLoadRequest(
    Guid ClientId, string ReferenceNo, Guid LoadTypeId, string? CreditOverrideReason = null,
    DateTimeOffset? PickupWindowStart = null, DateTimeOffset? PickupWindowEnd = null,
    DateTimeOffset? DeliveryWindowStart = null, DateTimeOffset? DeliveryWindowEnd = null);

public record AddLoadLegRequest(
    int SequenceNo,
    Guid OriginLocationId,
    Guid DestinationLocationId,
    LoadLegExecutionType ExecutionType,
    Guid CostCentreId,
    Guid? VehicleId,
    Guid? DriverId,
    Guid? SubcontractorId);

public record AllocateLoadLegRequest(Guid? VehicleId, Guid? DriverId, Guid? SubcontractorId);

public record AddCommodityLineRequest(
    Guid CommodityId,
    decimal Quantity,
    Guid UnitOfMeasureId,
    decimal SellRatePerUnit,
    decimal? BuyRatePerUnit = null,
    Guid? SellCurrencyId = null,
    Guid? BuyCurrencyId = null,
    string? CreditOverrideReason = null);

public record HoldLoadRequest(string Reason);

public record LoadTrackingResponse(Guid LoadId, LoadStatus Status, IReadOnlyList<LoadLeg> Legs, IReadOnlyList<LoadStatusHistory> History);

public record LoadLegMarginResponse(
    Guid LegId, Guid? SellCurrencyId, decimal SellTotal, Guid? BuyCurrencyId, decimal BuyTotal,
    decimal? ExchangeRateUsed, decimal? ConvertedBuyTotal, decimal? Margin, string? Note);

public record LoadMarginResponse(Guid LoadId, IReadOnlyList<LoadLegMarginResponse> Legs);

/// <summary>
/// Load capture, the leg-based status lifecycle, and the credit-limit hard stop
/// (docs/architecture.html §5.1, §5.2, §5.4).
///
/// PodReceived is reached only once every leg's Debrief is Approved (§09) — a leg
/// never sets that status directly, only LegsController.SubmitDebrief/
/// DebriefsController.Approve do, via LoadStatusService's shared rollup. The
/// lifecycle still stops short of Invoiced/Closed: those depend on tracking whether
/// every commodity line for a load has actually been invoiced/expensed, which
/// doesn't exist yet.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/loads")]
[Authorize]
public class LoadsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly CreditExposureService _creditExposure;
    private readonly LoadStatusService _loadStatus;
    private readonly IAuthorizationService _authorizationService;
    private readonly ExceptionService _exceptions;

    public LoadsController(
        TmsDbContext db,
        ITenantContext tenantContext,
        ICurrentUserAccessor currentUser,
        CreditExposureService creditExposure,
        LoadStatusService loadStatus,
        IAuthorizationService authorizationService,
        ExceptionService exceptions)
    {
        _db = db;
        _tenantContext = tenantContext;
        _authorizationService = authorizationService;
        _currentUser = currentUser;
        _creditExposure = creditExposure;
        _loadStatus = loadStatus;
        _exceptions = exceptions;
    }

    /// <summary>
    /// The row-level scoping and function check every Customer Portal action needs
    /// (§13.1) — internal staff (neither ClientId nor SubcontractorId set) are entirely
    /// unaffected. A portal contact must both own the load's client (delegating to
    /// ITenantContext.CanAccessClient, which — unlike an earlier, buggy version of this
    /// method — correctly Forbids a Supplier Portal contact too, not just a Customer
    /// Portal contact for a different Client) and hold the specific portal.client.*
    /// function for the action they're attempting.
    /// </summary>
    private async Task<ActionResult?> CheckPortalClientAccessAsync(Guid loadClientId, string requiredFunction)
    {
        if (_tenantContext.ClientId is null && _tenantContext.SubcontractorId is null) return null;
        if (!_tenantContext.CanAccessClient(loadClientId)) return Forbid();

        var authResult = await _authorizationService.AuthorizeAsync(User, requiredFunction);
        return authResult.Succeeded ? null : Forbid();
    }

    /// <summary>
    /// AddLeg/AllocateLeg/StartLeg/DeliverLeg/Hold/ReleaseHold/Cancel/AddCommodityLine
    /// carry no function policy at all — before Portal contacts existed, the class-level
    /// [Authorize] (any authenticated internal staff) was an adequate gate on its own.
    /// Now that an authenticated caller can also be an external Subcontractor/Client
    /// portal contact, and none of these actions are part of the documented portal
    /// feature (§13.2's own scope is limited to booking a load's header via Create; §13.3
    /// names only leg confirmation/debrief actions, both on LegsController), every one of
    /// them explicitly Forbids any portal caller outright rather than leaving them
    /// reachable by whatever narrow portal.* functions a contact happens to hold.
    /// </summary>
    private ActionResult? BlockPortalCallers() =>
        _tenantContext.ClientId is not null || _tenantContext.SubcontractorId is not null ? Forbid() : null;

    /// <summary>Also the Customer Portal's own load list (§13.2) — a portal caller sees only their own Client's loads regardless of what the rest of this query would otherwise return. A Supplier Portal contact (ClientId null, same as staff) is explicitly Forbidden rather than silently falling through to the unfiltered staff branch — the bug an earlier version of this check had.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Load>>> List(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null) return Forbid();

        var query = _db.Loads.AsQueryable();
        if (_tenantContext.ClientId is Guid ownClientId)
        {
            var authResult = await _authorizationService.AuthorizeAsync(User, "portal.client.viewloads");
            if (!authResult.Succeeded) return Forbid();
            query = query.Where(l => l.ClientId == ownClientId);
        }

        return Ok(await query.OrderByDescending(l => l.Id).ToListAsync(ct));
    }

    /// <summary>Also the Customer Portal's own self-service booking (§13.2, gated by portal.client.createload) — subject to the same credit hard stop as every other channel below, no special exemption. A portal caller can only ever book for their own Client, regardless of what ClientId they pass.</summary>
    [HttpPost]
    public async Task<ActionResult<Load>> Create(CreateLoadRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var portalCheck = await CheckPortalClientAccessAsync(request.ClientId, "portal.client.createload");
        if (portalCheck is not null) return portalCheck;

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId, ct);
        if (client is null) return NotFound($"Client {request.ClientId} was not found.");
        if (client.Status == ClientStatus.Deactivated)
            return Conflict($"Client '{client.Name}' is deactivated; reactivate it before booking a new load.");

        if (!await _db.LoadTypes.AnyAsync(lt => lt.Id == request.LoadTypeId, ct))
            return NotFound($"Load type {request.LoadTypeId} was not found.");

        // Holds an exclusive, per-client SQL Server application lock for the rest of
        // this method (§5.4) — a concurrent Create/AddCommodityLine for the same
        // client blocks here until this transaction commits or rolls back, so the
        // credit check below can never race another one reading stale exposure.
        await using var creditLock = await _creditExposure.BeginCreditLockAsync(_tenantContext.TenantId.Value, client.Id, ct);

        // A brand-new load carries no sell value yet, so the only thing worth
        // checking here is whether the client is *already* over limit from prior
        // loads — in which case starting another one is refused outright too. This
        // preliminary check always looks at the client's primary currency (§4.3):
        // which currency a load will actually use isn't known until its first
        // commodity line is added (AddCommodityLine), where the real, currency-scoped
        // check happens.
        var creditCheck = await CheckCreditAsync(client, additionalAmount: 0m, request.CreditOverrideReason, client.CurrencyId, ct);
        if (creditCheck is not null) return creditCheck; // creditLock disposed uncommitted -> rolled back

        var load = new Load
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            ClientId = request.ClientId,
            ReferenceNo = request.ReferenceNo,
            LoadTypeId = request.LoadTypeId,
            Status = LoadStatus.Booked,
            PickupWindowStart = request.PickupWindowStart,
            PickupWindowEnd = request.PickupWindowEnd,
            DeliveryWindowStart = request.DeliveryWindowStart,
            DeliveryWindowEnd = request.DeliveryWindowEnd
        };

        _db.Loads.Add(load);
        await _db.SaveChangesAsync(ct);
        await creditLock.CommitAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = load.Id }, load);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Load>> Get(Guid id, CancellationToken ct)
    {
        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();

        var portalCheck = await CheckPortalClientAccessAsync(load.ClientId, "portal.client.viewloads");
        if (portalCheck is not null) return portalCheck;

        return Ok(load);
    }

    /// <summary>
    /// Buy→sell converted margin per leg (§4.3) — never part of either portal's
    /// documented scope (a Subcontractor's own portal view deliberately shows accrual
    /// status "without exposing internal margin data," §16.4), so any portal caller is
    /// Forbidden outright, unlike Get/Tracking above which scope a Client contact to
    /// their own load instead of blocking them entirely.
    /// </summary>
    [HttpGet("{id:guid}/margin")]
    public async Task<ActionResult<LoadMarginResponse>> Margin(Guid id, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var load = await _db.Loads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();

        var legs = await _db.LoadLegs.Where(l => l.LoadId == id).ToListAsync(ct);
        var legIds = legs.Select(l => l.Id).ToList();

        var rateLinesByLeg = await _db.RateLines
            .Where(r => r.SourceType == RateLineSourceType.CommodityLine)
            .Join(_db.Set<CommodityLine>(), r => r.SourceId, cl => cl.Id, (r, cl) => new { r, cl.LoadLegId })
            .Where(x => legIds.Contains(x.LoadLegId))
            .ToListAsync(ct);

        var pickupDate = load.PickupWindowStart is DateTimeOffset pws ? DateOnly.FromDateTime(pws.UtcDateTime) : (DateOnly?)null;

        var legResponses = new List<LoadLegMarginResponse>();
        foreach (var leg in legs)
        {
            var sellLines = rateLinesByLeg.Where(x => x.LoadLegId == leg.Id && x.r.Direction == RateLineDirection.Sell).Select(x => x.r).ToList();
            var buyLines = rateLinesByLeg.Where(x => x.LoadLegId == leg.Id && x.r.Direction == RateLineDirection.Buy).Select(x => x.r).ToList();

            var sellCurrencyId = sellLines.FirstOrDefault()?.CurrencyId;
            var sellTotal = sellLines.Sum(r => r.Amount);
            var buyCurrencyId = buyLines.FirstOrDefault()?.CurrencyId;
            var buyTotal = buyLines.Sum(r => r.Amount);

            decimal? exchangeRateUsed = null;
            decimal? convertedBuyTotal;
            decimal? margin;
            string? note = null;

            if (buyCurrencyId is null)
            {
                // Own-fleet leg, or no buy RateLine captured yet — nothing to convert.
                convertedBuyTotal = 0m;
                margin = sellTotal;
            }
            else if (sellCurrencyId is null || buyCurrencyId == sellCurrencyId)
            {
                // No sell line yet, or both sides already in the same currency — no
                // exchange rate needed either way.
                convertedBuyTotal = buyTotal;
                margin = sellTotal - buyTotal;
            }
            else if (pickupDate is null)
            {
                convertedBuyTotal = null;
                margin = null;
                note = "Load has no PickupWindowStart to anchor an exchange rate lookup.";
            }
            else
            {
                var rate = await _db.ExchangeRates
                    .Where(e => e.FromCurrencyId == buyCurrencyId && e.ToCurrencyId == sellCurrencyId && e.EffectiveDate <= pickupDate)
                    .OrderByDescending(e => e.EffectiveDate)
                    .FirstOrDefaultAsync(ct);

                if (rate is null)
                {
                    convertedBuyTotal = null;
                    margin = null;
                    note = $"No captured exchange rate for this currency pair on or before {pickupDate:yyyy-MM-dd}.";
                }
                else
                {
                    exchangeRateUsed = rate.Rate;
                    convertedBuyTotal = buyTotal * rate.Rate;
                    margin = sellTotal - convertedBuyTotal;
                }
            }

            legResponses.Add(new LoadLegMarginResponse(
                leg.Id, sellCurrencyId, sellTotal, buyCurrencyId, buyTotal, exchangeRateUsed, convertedBuyTotal, margin, note));
        }

        return Ok(new LoadMarginResponse(load.Id, legResponses));
    }

    /// <summary>Also the Customer Portal's own load tracking view (§13.2), following the same status lifecycle (§5.2) shown to internal staff.</summary>
    [HttpGet("{id:guid}/tracking")]
    public async Task<ActionResult<LoadTrackingResponse>> Tracking(Guid id, CancellationToken ct)
    {
        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();

        var portalCheck = await CheckPortalClientAccessAsync(load.ClientId, "portal.client.viewloads");
        if (portalCheck is not null) return portalCheck;

        var history = await _db.LoadStatusHistories
            .Where(h => h.LoadId == id)
            .OrderBy(h => h.ChangedAt)
            .ToListAsync(ct);

        return Ok(new LoadTrackingResponse(load.Id, load.Status, load.Legs, history));
    }

    [HttpPost("{id:guid}/legs")]
    public async Task<ActionResult<LoadLeg>> AddLeg(Guid id, AddLoadLegRequest request, CancellationToken ct)
    {
        if (BlockPortalCallers() is ActionResult portalBlock) return portalBlock;

        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();
        if (load.Status is LoadStatus.OnHold or LoadStatus.Cancelled)
            return Conflict($"Load is {load.Status}; no further legs can be added.");

        if (!await _db.Locations.AnyAsync(l => l.Id == request.OriginLocationId, ct))
            return NotFound($"Location {request.OriginLocationId} (origin) was not found.");
        if (!await _db.Locations.AnyAsync(l => l.Id == request.DestinationLocationId, ct))
            return NotFound($"Location {request.DestinationLocationId} (destination) was not found.");
        if (!await _db.CostCentres.AnyAsync(c => c.Id == request.CostCentreId, ct))
            return NotFound($"Cost centre {request.CostCentreId} was not found.");

        // A leg is resourced one way or the other, never both (§5.1/§8.2) — own fleet
        // by Vehicle+Driver, subcontracted by Subcontractor — so the field that
        // doesn't match ExecutionType is rejected outright rather than silently ignored.
        if (request.ExecutionType == LoadLegExecutionType.Subcontracted && (request.VehicleId is not null || request.DriverId is not null))
            return BadRequest("A Subcontracted leg cannot also carry a VehicleId/DriverId.");
        if (request.ExecutionType == LoadLegExecutionType.OwnFleet && request.SubcontractorId is not null)
            return BadRequest("An OwnFleet leg cannot also carry a SubcontractorId.");

        if (request.VehicleId is Guid vehicleId)
        {
            var requestedVehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId, ct);
            if (requestedVehicle is null) return NotFound($"Vehicle {vehicleId} was not found.");
            if (requestedVehicle.Status == VehicleStatus.Deactivated)
                return Conflict($"Vehicle '{requestedVehicle.FleetNo}' is deactivated; it cannot be allocated to a new leg.");
        }
        if (request.DriverId is Guid driverId)
        {
            var requestedDriver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == driverId, ct);
            if (requestedDriver is null) return NotFound($"Driver {driverId} was not found.");
            if (requestedDriver.Status == DriverStatus.Deactivated)
                return Conflict($"Driver '{requestedDriver.Name}' is deactivated; it cannot be allocated to a new leg.");
        }
        if (request.SubcontractorId is Guid subcontractorId)
        {
            var requestedSubcontractor = await _db.Subcontractors.FirstOrDefaultAsync(s => s.Id == subcontractorId, ct);
            if (requestedSubcontractor is null) return NotFound($"Subcontractor {subcontractorId} was not found.");
            if (requestedSubcontractor.Status == SubcontractorStatus.Deactivated)
                return Conflict($"Subcontractor '{requestedSubcontractor.Name}' is deactivated; it cannot be allocated to a new leg.");
        }

        // Reaching Allocated (§5.2, §8.2) means different things depending on how the
        // leg is resourced: Vehicle+Driver for OwnFleet, a Subcontractor assignment for
        // Subcontracted — not both.
        var isAllocated = request.ExecutionType == LoadLegExecutionType.Subcontracted
            ? request.SubcontractorId is not null
            : request.VehicleId is not null && request.DriverId is not null;

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
            SubcontractorId = request.SubcontractorId,
            Status = isAllocated ? LoadLegStatus.Allocated : LoadLegStatus.Planned
        };

        _db.LoadLegs.Add(leg);
        load.Legs.Add(leg);

        await _loadStatus.RecomputeAsync(load, ct);
        await EnsureLoadConfirmationAsync(leg, ct);
        await EnsureAccrualsForLegAsync(leg, ct);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = load.Id }, leg);
    }

    /// <summary>Assigns a leg's resource — Vehicle+Driver for OwnFleet, a Subcontractor for Subcontracted (§5.2, §8.2) — moving it from Planned to Allocated.</summary>
    [HttpPost("{id:guid}/legs/{legId:guid}/allocate")]
    public async Task<IActionResult> AllocateLeg(Guid id, Guid legId, AllocateLoadLegRequest request, CancellationToken ct)
    {
        if (BlockPortalCallers() is ActionResult portalBlock) return portalBlock;

        // Acquired before the very first read of this leg's row (§5.2, §8.2, §10.2):
        // two concurrent Allocate calls for the same Planned leg could otherwise both
        // pass every check below before either commits — a duplicate LoadConfirmation
        // or SubcontractorAccrual, or the leg's own resource assignment landing on
        // whichever request happened to commit last. Fetching load/leg only after the
        // lock is held (rather than before, like CreditExposureService's lock) matters
        // here specifically because the racy state being read — leg.Status — lives on
        // the row itself, not a separately-recomputed aggregate.
        await using var legLock = await BeginLegLockAsync(legId, ct);

        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();

        var leg = load.Legs.FirstOrDefault(l => l.Id == legId);
        if (leg is null) return NotFound($"Leg {legId} was not found on load {id}.");
        if (leg.Status != LoadLegStatus.Planned)
            return Conflict($"Leg is {leg.Status}; only a Planned leg can be allocated.");
        if (load.Status is LoadStatus.OnHold or LoadStatus.Cancelled)
            return Conflict($"Load is {load.Status}; no further legs can be allocated.");

        if (leg.ExecutionType == LoadLegExecutionType.Subcontracted)
        {
            if (request.SubcontractorId is null || request.VehicleId is not null || request.DriverId is not null)
                return BadRequest("A Subcontracted leg is allocated with SubcontractorId only.");

            var subcontractor = await _db.Subcontractors.FirstOrDefaultAsync(s => s.Id == request.SubcontractorId, ct);
            if (subcontractor is null) return NotFound($"Subcontractor {request.SubcontractorId} was not found.");
            if (subcontractor.Status == SubcontractorStatus.Deactivated)
                return Conflict($"Subcontractor '{subcontractor.Name}' is deactivated; it cannot be allocated to a new leg.");

            // A commodity line's buy rate can be added before this leg has a
            // subcontractor assigned (no allow-list to check against yet — see
            // AddCommodityLine), so any currency it landed on was only provisional.
            // Now that the actual subcontractor is known, make sure it really is
            // permitted to be paid in every currency already sitting on this leg.
            var existingBuyCurrencyIds = await _db.Set<RateLine>()
                .Where(r => r.Direction == RateLineDirection.Buy && r.SourceType == RateLineSourceType.CommodityLine)
                .Join(_db.Set<CommodityLine>(), r => r.SourceId, cl => cl.Id, (r, cl) => new { r, cl })
                .Where(x => x.cl.LoadLegId == leg.Id)
                .Select(x => x.r.CurrencyId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var currencyId in existingBuyCurrencyIds)
            {
                if (!await IsSubcontractorCurrencyAllowedAsync(subcontractor, currencyId, ct))
                    return Conflict($"Subcontractor is not permitted to transact in currency {currencyId}, but a buy rate already on this leg uses it.");
            }

            leg.SubcontractorId = request.SubcontractorId;
        }
        else
        {
            if (request.VehicleId is null || request.DriverId is null || request.SubcontractorId is not null)
                return BadRequest("An OwnFleet leg is allocated with VehicleId and DriverId only.");

            var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == request.VehicleId, ct);
            if (vehicle is null) return NotFound($"Vehicle {request.VehicleId} was not found.");
            if (vehicle.Status == VehicleStatus.Deactivated)
                return Conflict($"Vehicle '{vehicle.FleetNo}' is deactivated; it cannot be allocated to a new leg.");

            var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == request.DriverId, ct);
            if (driver is null) return NotFound($"Driver {request.DriverId} was not found.");
            if (driver.Status == DriverStatus.Deactivated)
                return Conflict($"Driver '{driver.Name}' is deactivated; it cannot be allocated to a new leg.");

            leg.VehicleId = request.VehicleId;
            leg.DriverId = request.DriverId;
        }

        leg.Status = LoadLegStatus.Allocated;

        await _loadStatus.RecomputeAsync(load, ct);
        await EnsureLoadConfirmationAsync(leg, ct);
        await EnsureAccrualsForLegAsync(leg, ct);
        await _db.SaveChangesAsync(ct);
        await legLock.CommitAsync(ct);
        return NoContent();
    }

    /// <summary>Marks a leg as under way. Requires the leg to already be Allocated (§5.1) — the same transition for an OwnFleet or a Subcontracted leg, only how Allocated was reached differs.</summary>
    [HttpPost("{id:guid}/legs/{legId:guid}/start")]
    public async Task<IActionResult> StartLeg(Guid id, Guid legId, CancellationToken ct)
    {
        if (BlockPortalCallers() is ActionResult portalBlock) return portalBlock;

        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();

        var leg = load.Legs.FirstOrDefault(l => l.Id == legId);
        if (leg is null) return NotFound($"Leg {legId} was not found on load {id}.");
        if (leg.Status != LoadLegStatus.Allocated)
            return Conflict($"Leg is {leg.Status}; only an Allocated leg can start.");
        if (load.Status is LoadStatus.OnHold or LoadStatus.Cancelled)
            return Conflict($"Load is {load.Status}; no further legs can start.");

        leg.Status = LoadLegStatus.InTransit;

        await _loadStatus.RecomputeAsync(load, ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Marks a leg as physically delivered — the load overall reaches Delivered once every leg has (§5.2).</summary>
    [HttpPost("{id:guid}/legs/{legId:guid}/deliver")]
    public async Task<IActionResult> DeliverLeg(Guid id, Guid legId, CancellationToken ct)
    {
        if (BlockPortalCallers() is ActionResult portalBlock) return portalBlock;

        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();

        var leg = load.Legs.FirstOrDefault(l => l.Id == legId);
        if (leg is null) return NotFound($"Leg {legId} was not found on load {id}.");
        if (leg.Status != LoadLegStatus.InTransit)
            return Conflict($"Leg is {leg.Status}; only an In Transit leg can be delivered.");

        leg.Status = LoadLegStatus.Delivered;

        await _loadStatus.RecomputeAsync(load, ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Pauses a load for a query or dispute — only while In Transit (§5.2 Fig. 3).</summary>
    [HttpPost("{id:guid}/hold")]
    public async Task<IActionResult> Hold(Guid id, HoldLoadRequest request, CancellationToken ct)
    {
        if (BlockPortalCallers() is ActionResult portalBlock) return portalBlock;

        var load = await _db.Loads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();
        if (load.Status != LoadStatus.InTransit)
            return Conflict($"Load is {load.Status}; only a load In Transit can be put On Hold.");

        await _loadStatus.TransitionAsync(load, LoadStatus.OnHold, request.Reason, ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Resumes a load that was On Hold (§5.2 Fig. 3) — recomputed from its legs' actual
    /// state rather than assumed back to In Transit, since a leg can still be marked
    /// Delivered while the load is held (DeliverLeg has no On-Hold guard, unlike
    /// StartLeg/AllocateLeg/AddLeg): if every leg finished while paused, releasing the
    /// hold must land on Delivered, not silently revert to In Transit.
    /// </summary>
    [HttpPost("{id:guid}/release-hold")]
    public async Task<IActionResult> ReleaseHold(Guid id, CancellationToken ct)
    {
        if (BlockPortalCallers() is ActionResult portalBlock) return portalBlock;

        var load = await _db.Loads.Include(l => l.Legs).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();
        if (load.Status != LoadStatus.OnHold)
            return Conflict($"Load is {load.Status}, not On Hold.");

        var next = LoadStatusService.ComputeStatusFromLegs(load.Legs);
        await _loadStatus.TransitionAsync(load, next, reason: null, ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Cancels a load — only while it's still Booked or Allocated (§5.2 Fig. 3); once execution starts, it can no longer be cancelled outright.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (BlockPortalCallers() is ActionResult portalBlock) return portalBlock;

        var load = await _db.Loads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound();
        if (load.Status != LoadStatus.Booked && load.Status != LoadStatus.Allocated)
            return Conflict($"Load is {load.Status}; only Booked or Allocated loads can be cancelled.");

        await _loadStatus.TransitionAsync(load, LoadStatus.Cancelled, reason: null, ct);
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
        if (BlockPortalCallers() is ActionResult portalBlock) return portalBlock;

        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var load = await _db.Loads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (load is null) return NotFound($"Load {id} was not found.");
        if (load.Status is LoadStatus.OnHold or LoadStatus.Cancelled)
            return Conflict($"Load is {load.Status}; no further commodity lines can be added.");

        var leg = await _db.LoadLegs.FirstOrDefaultAsync(l => l.Id == legId && l.LoadId == id, ct);
        if (leg is null) return NotFound($"Leg {legId} was not found on load {id}.");

        if (!await _db.Commodities.AnyAsync(c => c.Id == request.CommodityId, ct))
            return NotFound($"Commodity {request.CommodityId} was not found.");

        if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Id == request.UnitOfMeasureId, ct))
            return NotFound($"Unit of measure {request.UnitOfMeasureId} was not found.");

        // A subcontracted leg's cost is rate-line-based, so every line needs its own
        // buy rate to feed the accrual it raises (§8.1, §10.2); an own-fleet leg's buy
        // cost is a standard per-vehicle-class cost-per-km/hour figure instead (§8.1) —
        // no such cost table exists in this codebase yet, so there's nothing for a
        // BuyRatePerUnit to mean there, and it's rejected rather than silently ignored.
        if (leg.ExecutionType == LoadLegExecutionType.Subcontracted && request.BuyRatePerUnit is null)
            return BadRequest("A commodity line on a Subcontracted leg must include a BuyRatePerUnit.");
        if (leg.ExecutionType == LoadLegExecutionType.OwnFleet && request.BuyRatePerUnit is not null)
            return BadRequest("An OwnFleet leg's commodity line cannot carry a BuyRatePerUnit.");

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == load.ClientId, ct);
        if (client is null) return NotFound($"Client {load.ClientId} was not found.");

        var sellCurrencyId = request.SellCurrencyId ?? client.CurrencyId;
        var sellCreditLimit = await _creditExposure.ResolveCreditLimitAsync(client, sellCurrencyId, ct);
        if (sellCreditLimit is null)
            return BadRequest($"Client is not permitted to transact in currency {sellCurrencyId} — add it via POST /clients/{{id}}/currencies first.");

        // A buy rate's currency can only be validated against the leg's actual
        // subcontractor once one is assigned (leg.SubcontractorId is set exactly when
        // the leg is Allocated — see AddLeg/AllocateLeg). Before that, there's no
        // allow-list to check against yet, so BuyCurrencyId must be given explicitly;
        // AllocateLeg re-validates it once the subcontractor becomes known.
        Guid? buyCurrencyId = null;
        if (request.BuyRatePerUnit is not null)
        {
            if (leg.SubcontractorId is Guid legSubcontractorId)
            {
                var subcontractor = await _db.Subcontractors.FirstAsync(s => s.Id == legSubcontractorId, ct);
                buyCurrencyId = request.BuyCurrencyId ?? subcontractor.CurrencyId;
                if (!await IsSubcontractorCurrencyAllowedAsync(subcontractor, buyCurrencyId.Value, ct))
                    return BadRequest($"Subcontractor is not permitted to transact in currency {buyCurrencyId} — add it via POST /subcontractors/{{id}}/currencies first.");
            }
            else if (request.BuyCurrencyId is Guid explicitBuyCurrencyId)
            {
                if (!await _db.Currencies.AnyAsync(c => c.Id == explicitBuyCurrencyId, ct))
                    return NotFound($"Currency {explicitBuyCurrencyId} was not found.");
                buyCurrencyId = explicitBuyCurrencyId;
            }
            else
            {
                return BadRequest("BuyCurrencyId is required when adding a buy rate to a leg that isn't allocated to a subcontractor yet.");
            }
        }

        var sellAmount = request.Quantity * request.SellRatePerUnit;

        // See Create's identical use of this lock (§5.4) — same per-client resource,
        // so the two endpoints serialize against each other too, not just themselves.
        await using var creditLock = await _creditExposure.BeginCreditLockAsync(_tenantContext.TenantId.Value, client.Id, ct);

        var creditCheck = await CheckCreditAsync(client, sellAmount, request.CreditOverrideReason, sellCurrencyId, ct);
        if (creditCheck is not null) return creditCheck; // creditLock disposed uncommitted -> rolled back

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
            CurrencyId = sellCurrencyId,
            RatePerUnit = request.SellRatePerUnit,
            UnitOfMeasureId = request.UnitOfMeasureId,
            Quantity = request.Quantity,
            Amount = sellAmount
        });

        if (request.BuyRatePerUnit is decimal buyRatePerUnit)
        {
            var buyRateLine = new RateLine
            {
                TenantId = _tenantContext.TenantId.Value,
                CompanyId = _tenantContext.CompanyId.Value,
                SourceType = RateLineSourceType.CommodityLine,
                SourceId = commodityLine.Id,
                Direction = RateLineDirection.Buy,
                CurrencyId = buyCurrencyId!.Value,
                RatePerUnit = buyRatePerUnit,
                UnitOfMeasureId = request.UnitOfMeasureId,
                Quantity = request.Quantity,
                Amount = request.Quantity * buyRatePerUnit
            };
            _db.Set<RateLine>().Add(buyRateLine);

            // Covers the case where this leg was already Allocated before this line was
            // added; if it wasn't yet, EnsureAccrualsForLegAsync picks this line up later
            // from AllocateLeg instead (both are idempotent per RateLineBuyId).
            await EnsureAccrualAsync(leg, buyRateLine, ct);
        }

        await _db.SaveChangesAsync(ct);
        await creditLock.CommitAsync(ct);

        return CreatedAtAction(nameof(Get), new { id }, commodityLine);
    }

    /// <summary>
    /// Issues a LoadConfirmation the moment a Subcontracted leg reaches Allocated
    /// (§8.2) — never created directly, and never twice for the same leg. Sequential
    /// DocumentNumber per company carries the same accepted concurrency caveat as
    /// InvoicesController's NextInvoiceNumberAsync.
    /// </summary>
    private async Task EnsureLoadConfirmationAsync(LoadLeg leg, CancellationToken ct)
    {
        if (leg.ExecutionType != LoadLegExecutionType.Subcontracted || leg.SubcontractorId is null) return;
        if (await _db.Set<LoadConfirmation>().AnyAsync(lc => lc.LoadLegId == leg.Id, ct)) return;

        var count = await _db.Set<LoadConfirmation>().CountAsync(lc => lc.CompanyId == leg.CompanyId, ct);
        _db.Set<LoadConfirmation>().Add(new LoadConfirmation
        {
            TenantId = leg.TenantId,
            CompanyId = leg.CompanyId,
            LoadLegId = leg.Id,
            SubcontractorId = leg.SubcontractorId.Value,
            DocumentNumber = $"LC{count + 1:D6}"
        });
    }

    /// <summary>
    /// Raises a SubcontractorAccrual for every buy RateLine already on this leg's
    /// commodity lines that doesn't have one yet (§10.2) — the bulk counterpart to
    /// EnsureAccrualAsync, for the moment a leg *becomes* Allocated (AddLeg/AllocateLeg),
    /// when its commodity lines (and their buy rates) may already have been added
    /// beforehand. A no-op the instant a leg is created, since it has no commodity
    /// lines yet; real once AddCommodityLine has run first.
    /// </summary>
    private async Task EnsureAccrualsForLegAsync(LoadLeg leg, CancellationToken ct)
    {
        if (leg.ExecutionType != LoadLegExecutionType.Subcontracted || leg.SubcontractorId is null) return;
        if (leg.Status != LoadLegStatus.Allocated) return;

        var buyRateLines = await _db.Set<RateLine>()
            .Where(r => r.Direction == RateLineDirection.Buy && r.SourceType == RateLineSourceType.CommodityLine)
            .Join(_db.Set<CommodityLine>(), r => r.SourceId, cl => cl.Id, (r, cl) => new { r, cl })
            .Where(x => x.cl.LoadLegId == leg.Id)
            .Select(x => x.r)
            .ToListAsync(ct);

        foreach (var buyRateLine in buyRateLines)
            await EnsureAccrualAsync(leg, buyRateLine, ct);
    }

    /// <summary>
    /// Raises the SubcontractorAccrual for one buy RateLine the moment its leg is both
    /// Subcontracted and Allocated (§5.2, §10.2) — the company's buy-side liability is
    /// recognised here, at commitment, not weeks later when the subcontractor's own
    /// invoice arrives (that's SupplierInvoice matching, a separate later step). Never
    /// raised twice for the same RateLineBuyId.
    /// </summary>
    private async Task EnsureAccrualAsync(LoadLeg leg, RateLine buyRateLine, CancellationToken ct)
    {
        if (leg.ExecutionType != LoadLegExecutionType.Subcontracted || leg.SubcontractorId is null) return;
        if (leg.Status != LoadLegStatus.Allocated) return;
        if (await _db.Set<SubcontractorAccrual>().AnyAsync(a => a.RateLineBuyId == buyRateLine.Id, ct)) return;

        _db.Set<SubcontractorAccrual>().Add(new SubcontractorAccrual
        {
            TenantId = leg.TenantId,
            CompanyId = leg.CompanyId,
            RateLineBuyId = buyRateLine.Id,
            SubcontractorId = leg.SubcontractorId.Value,
            CurrencyId = buyRateLine.CurrencyId,
            AccrualDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EstimatedAmount = buyRateLine.Amount
        });
    }

    /// <summary>Whether a Subcontractor is permitted to transact in a given currency (§4.3) — its own primary CurrencyId, or an explicit SubcontractorCurrency allow-list row.</summary>
    private async Task<bool> IsSubcontractorCurrencyAllowedAsync(Tms.Modules.Loads.Subcontractor subcontractor, Guid currencyId, CancellationToken ct) =>
        currencyId == subcontractor.CurrencyId
        || await _db.Set<SubcontractorCurrency>().AnyAsync(sc => sc.SubcontractorId == subcontractor.Id && sc.CurrencyId == currencyId, ct);

    /// <summary>
    /// Holds an exclusive, transaction-scoped SQL Server application lock on one leg —
    /// the same sp_getapplock mechanism as CreditExposureService.BeginCreditLockAsync
    /// (§5.4), just keyed on a leg instead of a client. AllocateLeg is the only caller:
    /// it acquires this before its very first read of the leg's row, so a second
    /// concurrent Allocate call for the same leg blocks here until the first commits
    /// or rolls back, then sees whatever state that left behind — never a race where
    /// both read Planned before either writes Allocated.
    /// </summary>
    private async Task<IDbContextTransaction> BeginLegLockAsync(Guid legId, CancellationToken ct)
    {
        var transaction = await _db.Database.BeginTransactionAsync(ct);

        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.Transaction = transaction.GetDbTransaction();

        void AddParam(string name, object value, ParameterDirection direction = ParameterDirection.Input)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            parameter.Direction = direction;
            command.Parameters.Add(parameter);
        }

        AddParam("@Resource", $"leg:{legId}");
        AddParam("@LockMode", "Exclusive");
        AddParam("@LockOwner", "Transaction");
        AddParam("@LockTimeout", 10000);
        AddParam("@ReturnValue", 0, ParameterDirection.ReturnValue);

        await command.ExecuteNonQueryAsync(ct);

        var lockResult = (int)command.Parameters["@ReturnValue"].Value!;
        if (lockResult < 0)
        {
            await transaction.RollbackAsync(ct);
            throw new InvalidOperationException(
                $"Could not acquire the leg lock for {legId} (sp_getapplock returned {lockResult}).");
        }

        return transaction;
    }

    /// <summary>
    /// Runs the §5.4 hard stop, scoped to one currency (§4.3) — the caller has already
    /// resolved and validated currencyId against the client's allowed set before ever
    /// reaching here. Returns null when the action is allowed to proceed; otherwise the
    /// ActionResult to return directly. An override reason is only honoured for a
    /// caller whose JWT carries the client.creditlimit.override function claim (§07) —
    /// resolved from their role at login, checked here via the same policy mechanism
    /// any endpoint could use — and is written to the audit trail either way.
    ///
    /// The check-and-save this participates in is made atomic per client by the
    /// caller's BeginCreditLockAsync (§5.4) — see CreditExposureService's doc comment.
    /// </summary>
    private async Task<ActionResult?> CheckCreditAsync(
        Tms.Modules.Loads.Client client,
        decimal additionalAmount,
        string? overrideReason,
        Guid currencyId,
        CancellationToken ct)
    {
        var status = await _creditExposure.GetStatusAsync(client, currencyId, ct);
        if (!CreditExposureService.WouldBreach(status, additionalAmount))
            return null;

        if (!string.IsNullOrWhiteSpace(overrideReason))
        {
            var authResult = await _authorizationService.AuthorizeAsync(User, "client.creditlimit.override");
            if (!authResult.Succeeded)
            {
                return new ObjectResult(new ProblemDetails
                {
                    Title = "Missing function",
                    Status = StatusCodes.Status403Forbidden,
                    Detail = "Overriding the credit limit requires the client.creditlimit.override function."
                })
                { StatusCode = StatusCodes.Status403Forbidden };
            }

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
                    status.CurrencyId,
                    status.CreditLimit,
                    status.TotalExposure,
                    additionalAmount,
                    Projected = status.TotalExposure + additionalAmount
                })
            });

            // Feeds §16.1's shared dashboard mechanism (Fig. 13's "Credit hard-stop /
            // override" source) — a bypassed hard stop is exactly the kind of thing a
            // dashboard should be able to surface without bespoke per-module logic.
            _exceptions.Raise(
                client.TenantId, client.CompanyId, "CreditOverride", ExceptionSeverity.Warning,
                nameof(Tms.Modules.Loads.Client), client.Id,
                $"Credit limit overridden for {additionalAmount:N2} — reason: {overrideReason}");

            return null; // override accepted — allow the caller to proceed
        }

        var currency = await _db.Currencies.FirstAsync(c => c.Id == currencyId, ct);

        return new ObjectResult(new ProblemDetails
        {
            Title = "Credit limit exceeded",
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = $"Client '{client.Name}' has {status.AvailableCredit:N2} {currency.Code} available credit " +
                     $"(limit {status.CreditLimit:N2}, exposure {status.TotalExposure:N2}) " +
                     $"but this action would add {additionalAmount:N2}.",
            Extensions =
            {
                ["currencyId"] = status.CurrencyId,
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
