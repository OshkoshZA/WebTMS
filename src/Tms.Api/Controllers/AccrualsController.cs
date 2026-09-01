using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Shared;

namespace Tms.Api.Controllers;

/// <summary>
/// Read-only view of SubcontractorAccrual (§10.2), raised automatically by
/// LoadsController — never created directly here. This is the working set an AP clerk
/// picks AccrualIds from for SupplierInvoicesController's Match action; List's
/// subcontractorId/status filters exist for exactly that ("what does this carrier still
/// have outstanding against them?") — and, since §13.1's Supplier Portal identity,
/// double as the same filters the portal's own "when will I get paid" view (§13.3)
/// uses, row-level scoped to the caller's own Subcontractor.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accruals")]
[Authorize]
public class AccrualsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuthorizationService _authorizationService;

    public AccrualsController(TmsDbContext db, ITenantContext tenantContext, IAuthorizationService authorizationService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _authorizationService = authorizationService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SubcontractorAccrual>>> List(
        Guid? subcontractorId, SubcontractorAccrualStatus? status, CancellationToken ct)
    {
        // A portal caller (§13.1) is pinned to their own Subcontractor regardless of
        // what subcontractorId they pass — never silently widened, and gated behind
        // the specific portal function the way every other portal action is. A Customer
        // Portal contact (SubcontractorId null, same as staff) is explicitly Forbidden
        // rather than silently falling through to the unfiltered staff branch below —
        // the bug an earlier version of this check had, which would have returned
        // every subcontractor's accruals to a client contact.
        if (_tenantContext.ClientId is not null) return Forbid();
        if (_tenantContext.SubcontractorId is Guid ownSubcontractorId)
        {
            var authResult = await _authorizationService.AuthorizeAsync(User, "portal.subcontractor.viewlegs");
            if (!authResult.Succeeded) return Forbid();
            subcontractorId = ownSubcontractorId;
        }

        var query = _db.Set<SubcontractorAccrual>().AsQueryable();
        if (subcontractorId is Guid s) query = query.Where(a => a.SubcontractorId == s);
        if (status is SubcontractorAccrualStatus st) query = query.Where(a => a.Status == st);

        return Ok(await query.OrderByDescending(a => a.AccrualDate).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SubcontractorAccrual>> Get(Guid id, CancellationToken ct)
    {
        var accrual = await _db.Set<SubcontractorAccrual>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (accrual is null) return NotFound();
        if (!_tenantContext.CanAccessSubcontractor(accrual.SubcontractorId)) return Forbid();

        return Ok(accrual);
    }
}
