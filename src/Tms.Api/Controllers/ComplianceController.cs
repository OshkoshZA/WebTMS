using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Exceptions;
using Tms.Modules.Fleet;
using Tms.Shared;

namespace Tms.Api.Controllers;

/// <summary>
/// Wires vehicle/driver compliance expiry into the shared Exception mechanism
/// (docs/architecture.html §16.1, Fig. 13) — the third of that figure's six sources,
/// left unwired until now for the reason <c>Tms.Modules.Exceptions</c>'s own doc
/// comment gives: "compliance-expiry scanning has no scheduled job anywhere in this
/// codebase." That's still true — there is no Hangfire/Quartz-equivalent anywhere in
/// this design (the same gap §4.3's FX auto-refresh and §11.3's webhook retry/backoff
/// are already deferred behind) — so rather than inventing scheduler infrastructure
/// for this one feature, reconciliation runs on demand: <c>tms-app</c>'s own Compliance
/// screen (§5.1) calls this once per visit, alongside its existing GET /vehicles and
/// GET /drivers calls, so exceptions freshen on genuine human review activity rather
/// than a wall-clock schedule this codebase has nowhere to run.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/compliance")]
[Authorize]
public class ComplianceController : ControllerBase
{
    /// <summary>Matches ComplianceView.vue's own default "Soon" threshold — not configurable server-side, since no schema field for it exists anywhere (Company carries no such setting).</summary>
    private const int ExpiryWarningWindowDays = 30;
    private const string Category = "ComplianceExpiry";

    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ExceptionService _exceptions;

    public ComplianceController(TmsDbContext db, ITenantContext tenantContext, ExceptionService exceptions)
    {
        _db = db;
        _tenantContext = tenantContext;
        _exceptions = exceptions;
    }

    /// <summary>
    /// Scans every Active vehicle/OnLeave-or-Active driver's own expiry dates and
    /// raises/resolves one ComplianceExpiry exception per (entity, concern) pair — a
    /// vehicle's licence and vehicle-test dates, a driver's licence and PDP dates, each
    /// tracked independently via its own EntityType string
    /// ("VehicleLicence"/"VehicleTest"/"DriverLicence"/"DriverPdp") against the same
    /// EntityId, exactly the "new source plugs into dashboards that already exist"
    /// design Fig. 13 describes — no change needed to ExceptionsController or
    /// ExceptionService itself. Idempotent and safe to call repeatedly: an already-open
    /// exception for a (type, id) pair is never duplicated, and a date that's since
    /// moved outside the warning window (renewed) or whose vehicle/driver has been
    /// deactivated gets its existing exception auto-resolved. Deliberately does not
    /// re-raise at a different severity if the same exception is still open when a
    /// warning window is later crossed into actual expiry — a v1 simplification, not
    /// a full re-evaluation engine.
    /// </summary>
    [HttpPost("reconcile-exceptions")]
    [Authorize(Policy = "exception.manage")]
    public async Task<IActionResult> ReconcileExceptions(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(ExpiryWarningWindowDays);

        var activeVehicles = await _db.Vehicles.Where(v => v.Status == VehicleStatus.Active).ToListAsync(ct);
        foreach (var vehicle in activeVehicles)
        {
            await ReconcileOneAsync("VehicleLicence", vehicle.Id, vehicle.LicenceExpiry, $"Vehicle {vehicle.FleetNo} licence", today, cutoff, ct);
            await ReconcileOneAsync("VehicleTest", vehicle.Id, vehicle.VehicleTestExpiry, $"Vehicle {vehicle.FleetNo} vehicle test", today, cutoff, ct);
        }

        var activeDrivers = await _db.Drivers
            .Where(d => d.Status == DriverStatus.Active || d.Status == DriverStatus.OnLeave)
            .ToListAsync(ct);
        foreach (var driver in activeDrivers)
        {
            await ReconcileOneAsync("DriverLicence", driver.Id, driver.LicenceExpiry, $"Driver {driver.Name} licence", today, cutoff, ct);
            await ReconcileOneAsync("DriverPdp", driver.Id, driver.PdpExpiry, $"Driver {driver.Name} PDP", today, cutoff, ct);
        }

        // A deactivated unit is no longer dispatched, so its expiry no longer matters —
        // resolve anything still open for it. (Deliberately unconditional: cheap to
        // call ResolveByEntityAsync on an entity with nothing open, a no-op either way.)
        var deactivatedVehicleIds = await _db.Vehicles.Where(v => v.Status == VehicleStatus.Deactivated).Select(v => v.Id).ToListAsync(ct);
        foreach (var vehicleId in deactivatedVehicleIds)
        {
            await _exceptions.ResolveByEntityAsync("VehicleLicence", vehicleId, ct);
            await _exceptions.ResolveByEntityAsync("VehicleTest", vehicleId, ct);
        }

        var deactivatedDriverIds = await _db.Drivers.Where(d => d.Status == DriverStatus.Deactivated).Select(d => d.Id).ToListAsync(ct);
        foreach (var driverId in deactivatedDriverIds)
        {
            await _exceptions.ResolveByEntityAsync("DriverLicence", driverId, ct);
            await _exceptions.ResolveByEntityAsync("DriverPdp", driverId, ct);
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task ReconcileOneAsync(
        string entityType, Guid entityId, DateOnly? expiry, string label, DateOnly today, DateOnly cutoff, CancellationToken ct)
    {
        if (expiry is null || expiry.Value > cutoff)
        {
            // Not expiring soon (or not tracked at all) — resolve any exception this
            // concern previously raised (e.g. the date was since pushed out/renewed).
            await _exceptions.ResolveByEntityAsync(entityType, entityId, ct);
            return;
        }

        var alreadyOpen = await _db.ExceptionRecords
            .AnyAsync(e => e.EntityType == entityType && e.EntityId == entityId && e.Status != ExceptionStatus.Resolved, ct);
        if (alreadyOpen) return;

        var severity = expiry.Value < today ? ExceptionSeverity.Critical : ExceptionSeverity.Warning;
        var description = expiry.Value < today
            ? $"{label} expired {today.DayNumber - expiry.Value.DayNumber} day(s) ago ({expiry:yyyy-MM-dd})."
            : $"{label} expires in {expiry.Value.DayNumber - today.DayNumber} day(s) ({expiry:yyyy-MM-dd}).";

        _exceptions.Raise(_tenantContext.TenantId!.Value, _tenantContext.CompanyId!.Value, Category, severity, entityType, entityId, description);
    }
}
