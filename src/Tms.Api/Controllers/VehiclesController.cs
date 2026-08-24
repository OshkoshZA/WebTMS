using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Fleet;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateVehicleRequest(
    string FleetNo,
    string Registration,
    VehicleType Type,
    string? Make,
    string? Model,
    DateOnly? LicenceExpiry,
    DateOnly? VehicleTestExpiry);

public record UpdateVehicleRequest(
    string FleetNo,
    string Registration,
    VehicleType Type,
    string? Make,
    string? Model,
    DateOnly? LicenceExpiry,
    DateOnly? VehicleTestExpiry);

/// <summary>Vehicle master data (docs/architecture.html §5.1) — follows the standard CRUD convention (§11.5).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/vehicles")]
[Authorize]
public class VehiclesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public VehiclesController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Vehicle>>> List(CancellationToken ct)
        => Ok(await _db.Vehicles.OrderBy(v => v.FleetNo).ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Vehicle>> Get(Guid id, CancellationToken ct)
    {
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, ct);
        return vehicle is null ? NotFound() : Ok(vehicle);
    }

    [HttpPost]
    public async Task<ActionResult<Vehicle>> Create(CreateVehicleRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var vehicle = new Vehicle
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            FleetNo = request.FleetNo,
            Registration = request.Registration,
            Type = request.Type,
            Make = request.Make,
            Model = request.Model,
            LicenceExpiry = request.LicenceExpiry,
            VehicleTestExpiry = request.VehicleTestExpiry
        };

        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = vehicle.Id }, vehicle);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateVehicleRequest request, CancellationToken ct)
    {
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vehicle is null) return NotFound();

        vehicle.FleetNo = request.FleetNo;
        vehicle.Registration = request.Registration;
        vehicle.Type = request.Type;
        vehicle.Make = request.Make;
        vehicle.Model = request.Model;
        vehicle.LicenceExpiry = request.LicenceExpiry;
        vehicle.VehicleTestExpiry = request.VehicleTestExpiry;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vehicle is null) return NotFound();

        vehicle.Status = VehicleStatus.Deactivated; // never a hard delete — §11.5
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
