using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Fleet;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateDriverRequest(
    string EmployeeNo,
    string Name,
    string LicenceCode,
    DateOnly? LicenceExpiry,
    DateOnly? PdpExpiry,
    Guid? HomeCostCentreId);

public record UpdateDriverRequest(
    string Name,
    string LicenceCode,
    DateOnly? LicenceExpiry,
    DateOnly? PdpExpiry,
    Guid? HomeCostCentreId,
    DriverStatus Status);

/// <summary>Driver master data (docs/architecture.html §5.1) — follows the standard CRUD convention (§11.5).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/drivers")]
[Authorize]
public class DriversController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public DriversController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Driver>>> List(CancellationToken ct)
        => Ok(await _db.Drivers.OrderBy(d => d.Name).ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Driver>> Get(Guid id, CancellationToken ct)
    {
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == id, ct);
        return driver is null ? NotFound() : Ok(driver);
    }

    [HttpPost]
    public async Task<ActionResult<Driver>> Create(CreateDriverRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (request.HomeCostCentreId is Guid costCentreId && !await _db.CostCentres.AnyAsync(c => c.Id == costCentreId, ct))
            return NotFound($"Cost centre {costCentreId} was not found.");

        var driver = new Driver
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            EmployeeNo = request.EmployeeNo,
            Name = request.Name,
            LicenceCode = request.LicenceCode,
            LicenceExpiry = request.LicenceExpiry,
            PdpExpiry = request.PdpExpiry,
            HomeCostCentreId = request.HomeCostCentreId
        };

        _db.Drivers.Add(driver);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = driver.Id }, driver);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateDriverRequest request, CancellationToken ct)
    {
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (driver is null) return NotFound();

        if (request.HomeCostCentreId is Guid costCentreId && !await _db.CostCentres.AnyAsync(c => c.Id == costCentreId, ct))
            return NotFound($"Cost centre {costCentreId} was not found.");

        // Active <-> OnLeave is a routine, reversible operational change (unlike
        // VehicleStatus, which has no equivalent middle state), so it's fine to allow
        // here — but Deactivated is a one-way, §11.5 "never a hard delete" terminal
        // state and must only be reached through the dedicated Deactivate action below,
        // never as a side effect of an otherwise-ordinary field edit.
        if (request.Status == DriverStatus.Deactivated && driver.Status != DriverStatus.Deactivated)
            return Conflict("Deactivating a driver requires the dedicated deactivate action, not a general update.");

        // EmployeeNo is deliberately not editable here — it's the driver's stable identifier.
        driver.Name = request.Name;
        driver.LicenceCode = request.LicenceCode;
        driver.LicenceExpiry = request.LicenceExpiry;
        driver.PdpExpiry = request.PdpExpiry;
        driver.HomeCostCentreId = request.HomeCostCentreId;
        driver.Status = request.Status;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (driver is null) return NotFound();

        driver.Status = DriverStatus.Deactivated; // never a hard delete — §11.5
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
