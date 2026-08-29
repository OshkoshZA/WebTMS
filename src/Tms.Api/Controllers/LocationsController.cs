using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Fleet;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateLocationRequest(string Name, string Province, Guid CountryId);
public record UpdateLocationRequest(string Name, string Province, Guid CountryId);

/// <summary>
/// Named locations used as a leg's origin/destination (docs/architecture.html §5.1,
/// §11.2) — no GPS/geocoding, just a name scoped to a province and country. Loads
/// legs reference these by id (LoadsController.AddLeg validates the id exists), so
/// this is what actually lets a new location be created rather than only seeded.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/locations")]
[Authorize]
public class LocationsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public LocationsController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Location>>> List(CancellationToken ct)
        => Ok(await _db.Locations.OrderBy(l => l.Name).ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Location>> Get(Guid id, CancellationToken ct)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);
        return location is null ? NotFound() : Ok(location);
    }

    [HttpPost]
    [Authorize(Policy = "location.master.manage")]
    public async Task<ActionResult<Location>> Create(CreateLocationRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (!await _db.Countries.AnyAsync(c => c.Id == request.CountryId, ct))
            return NotFound($"Country {request.CountryId} was not found.");

        var location = new Location
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            Name = request.Name,
            Province = request.Province,
            CountryId = request.CountryId
        };

        _db.Locations.Add(location);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = location.Id }, location);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "location.master.manage")]
    public async Task<IActionResult> Update(Guid id, UpdateLocationRequest request, CancellationToken ct)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (location is null) return NotFound();

        if (!await _db.Countries.AnyAsync(c => c.Id == request.CountryId, ct))
            return NotFound($"Country {request.CountryId} was not found.");

        location.Name = request.Name;
        location.Province = request.Province;
        location.CountryId = request.CountryId;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "location.master.manage")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (location is null) return NotFound();

        location.Active = false; // never a hard delete — §11.5
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = "location.master.manage")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (location is null) return NotFound();

        location.Active = true;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
