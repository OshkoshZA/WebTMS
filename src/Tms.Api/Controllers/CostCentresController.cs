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
    [Authorize(Policy = "costcentre.master.manage")]
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
    [Authorize(Policy = "costcentre.master.manage")]
    public async Task<IActionResult> Update(Guid id, UpdateCostCentreRequest request, CancellationToken ct)
    {
        var costCentre = await _db.CostCentres.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (costCentre is null) return NotFound();

        if (request.ParentCostCentreId is Guid parentId)
        {
            if (parentId == id)
                return BadRequest("A cost centre cannot be its own parent.");

            var parent = await _db.CostCentres.FirstOrDefaultAsync(c => c.Id == parentId, ct);
            if (parent is null) return NotFound($"Parent cost centre {parentId} was not found.");

            // Walks the ancestry chain from the proposed parent upward — a direct
            // self-reference is the shallow case above, but nothing stopped a
            // multi-level cycle (A's parent is B, B's parent becomes A) before, which
            // would spin a rollup report (§06) forever.
            var visited = new HashSet<Guid> { id };
            var current = parent;
            while (current.ParentCostCentreId is Guid ancestorId)
            {
                if (ancestorId == id)
                    return BadRequest("That would create a cycle in the cost centre hierarchy.");
                if (!visited.Add(ancestorId))
                    break; // an already-corrupt cycle elsewhere — not this call's to fix
                current = await _db.CostCentres.FirstOrDefaultAsync(c => c.Id == ancestorId, ct);
                if (current is null) break;
            }
        }

        costCentre.Code = request.Code;
        costCentre.Name = request.Name;
        costCentre.ParentCostCentreId = request.ParentCostCentreId;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "costcentre.master.manage")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var costCentre = await _db.CostCentres.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (costCentre is null) return NotFound();

        costCentre.Active = false; // never a hard delete — §11.5
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = "costcentre.master.manage")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        var costCentre = await _db.CostCentres.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (costCentre is null) return NotFound();

        costCentre.Active = true;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
