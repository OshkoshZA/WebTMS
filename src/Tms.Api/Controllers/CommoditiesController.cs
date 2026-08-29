using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Loads;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateCommodityRequest(string Code, string Name, Guid DefaultUnitOfMeasureId, CommodityCategory Category);
public record UpdateCommodityRequest(string Code, string Name, Guid DefaultUnitOfMeasureId, CommodityCategory Category);

/// <summary>
/// The product master catalogue (docs/architecture.html §5.5, §11.2) — LoadsController.AddCommodityLine
/// validates a CommodityId exists, so this is what actually lets one be created rather than only seeded.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/commodities")]
[Authorize]
public class CommoditiesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public CommoditiesController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Commodity>>> List(CancellationToken ct)
        => Ok(await _db.Commodities.OrderBy(c => c.Code).ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Commodity>> Get(Guid id, CancellationToken ct)
    {
        var commodity = await _db.Commodities.FirstOrDefaultAsync(c => c.Id == id, ct);
        return commodity is null ? NotFound() : Ok(commodity);
    }

    [HttpPost]
    [Authorize(Policy = "commodity.master.manage")]
    public async Task<ActionResult<Commodity>> Create(CreateCommodityRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Id == request.DefaultUnitOfMeasureId, ct))
            return NotFound($"Unit of measure {request.DefaultUnitOfMeasureId} was not found.");

        var commodity = new Commodity
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            Code = request.Code,
            Name = request.Name,
            DefaultUnitOfMeasureId = request.DefaultUnitOfMeasureId,
            Category = request.Category
        };

        _db.Commodities.Add(commodity);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = commodity.Id }, commodity);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "commodity.master.manage")]
    public async Task<IActionResult> Update(Guid id, UpdateCommodityRequest request, CancellationToken ct)
    {
        var commodity = await _db.Commodities.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (commodity is null) return NotFound();

        if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Id == request.DefaultUnitOfMeasureId, ct))
            return NotFound($"Unit of measure {request.DefaultUnitOfMeasureId} was not found.");

        commodity.Code = request.Code;
        commodity.Name = request.Name;
        commodity.DefaultUnitOfMeasureId = request.DefaultUnitOfMeasureId;
        commodity.Category = request.Category;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "commodity.master.manage")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var commodity = await _db.Commodities.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (commodity is null) return NotFound();

        commodity.Active = false; // never a hard delete — §11.5
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = "commodity.master.manage")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        var commodity = await _db.Commodities.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (commodity is null) return NotFound();

        commodity.Active = true;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
