using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Debrief;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateExpenseTypeRequest(string Code, string Name);
public record UpdateExpenseTypeRequest(string Code, string Name);

/// <summary>Company-level expense category reference data (§9.1) — LegsController.SubmitDebrief validates an ExpenseTypeId exists, so this is what actually lets one be created rather than only seeded. Follows the standard master-data CRUD convention (§11.5).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/expense-types")]
[Authorize]
public class ExpenseTypesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ExpenseTypesController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>Never part of either portal's documented scope, so any portal contact is Forbidden outright — matching every other master-data controller's own equivalent fix.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExpenseType>>> List(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        return Ok(await _db.ExpenseTypes.OrderBy(t => t.Code).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExpenseType>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var type = await _db.ExpenseTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        return type is null ? NotFound() : Ok(type);
    }

    [HttpPost]
    [Authorize(Policy = "expensetype.master.manage")]
    public async Task<ActionResult<ExpenseType>> Create(CreateExpenseTypeRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var type = new ExpenseType
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            Code = request.Code,
            Name = request.Name
        };

        _db.ExpenseTypes.Add(type);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = type.Id }, type);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "expensetype.master.manage")]
    public async Task<IActionResult> Update(Guid id, UpdateExpenseTypeRequest request, CancellationToken ct)
    {
        var type = await _db.ExpenseTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return NotFound();

        type.Code = request.Code;
        type.Name = request.Name;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "expensetype.master.manage")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var type = await _db.ExpenseTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return NotFound();

        type.Active = false; // never a hard delete — §11.5
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = "expensetype.master.manage")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        var type = await _db.ExpenseTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return NotFound();

        type.Active = true;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
