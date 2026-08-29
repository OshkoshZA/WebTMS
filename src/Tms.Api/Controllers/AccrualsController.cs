using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Billing;

namespace Tms.Api.Controllers;

/// <summary>
/// Read-only view of SubcontractorAccrual (§10.2), raised automatically by
/// LoadsController — never created directly here. This is the working set an AP clerk
/// picks AccrualIds from for SupplierInvoicesController's Match action; List's
/// subcontractorId/status filters exist for exactly that ("what does this carrier still
/// have outstanding against them?").
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accruals")]
[Authorize]
public class AccrualsController : ControllerBase
{
    private readonly TmsDbContext _db;

    public AccrualsController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SubcontractorAccrual>>> List(
        Guid? subcontractorId, SubcontractorAccrualStatus? status, CancellationToken ct)
    {
        var query = _db.Set<SubcontractorAccrual>().AsQueryable();
        if (subcontractorId is Guid s) query = query.Where(a => a.SubcontractorId == s);
        if (status is SubcontractorAccrualStatus st) query = query.Where(a => a.Status == st);

        return Ok(await query.OrderByDescending(a => a.AccrualDate).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SubcontractorAccrual>> Get(Guid id, CancellationToken ct)
    {
        var accrual = await _db.Set<SubcontractorAccrual>().FirstOrDefaultAsync(a => a.Id == id, ct);
        return accrual is null ? NotFound() : Ok(accrual);
    }
}
