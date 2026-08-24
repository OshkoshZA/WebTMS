using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Loads;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateCostCentreRequest(string Code, string Name, Guid? ParentCostCentreId);
public record UpdateCostCentreRequest(string Code, string Name, Guid? ParentCostCentreId);

/// <summary>
/// Financial allocation units (docs/architecture.html §5.1, §06) — LoadsController.AddLeg
/// and DriversController both validate a CostCentreId exists, so this is what actually
/// lets one be created rather than only seeded.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/cost-centres")]
[Authorize]
public class CostCentresController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public CostCentresController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CostCentre>>> List(CancellationToken ct)
        => Ok(await _db.CostCentres.OrderBy(c => c.Code).ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CostCentre>> Get(Guid id, CancellationToken ct)
    {
        var costCentre = await _db.CostCentres.FirstOrDefaultAsync(c => c.Id == id, ct);
        return costCentre is null ? NotFound() : Ok(costCentre);
    }

    [HttpPost]
    public async Task<ActionResult<CostCentre>> Create(CreateCostCentreRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (request.ParentCostCentreId is Guid parentId && !await _db.CostCentres.AnyAsync(c => c.Id == parentId, ct))
            return NotFound($"Parent cost centre {parentId} was not found.");

        var costCentre = new CostCentre
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            Code = request.Code,
            Name = request.Name,
            ParentCostCentreId = request.ParentCostCentreId
        };

        _db.CostCentres.Add(costCentre);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = costCentre.Id }, costCentre);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCostCentreRequest request, CancellationToken ct)
    {
        var costCentre = await _db.CostCentres.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (costCentre is null) return NotFound();

        if (request.ParentCostCentreId == id)
            return BadRequest("A cost centre cannot be its own parent.");

        if (request.ParentCostCentreId is Guid parentId && !await _db.CostCentres.AnyAsync(c => c.Id == parentId, ct))
            return NotFound($"Parent cost centre {parentId} was not found.");

        costCentre.Code = request.Code;
        costCentre.Name = request.Name;
        costCentre.ParentCostCentreId = request.ParentCostCentreId;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var costCentre = await _db.CostCentres.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (costCentre is null) return NotFound();

        costCentre.Active = false; // never a hard delete — §11.5
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
