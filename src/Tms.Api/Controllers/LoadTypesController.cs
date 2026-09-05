using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Loads;

namespace Tms.Api.Controllers;

/// <summary>
/// The shared, non-company-scoped load-type reference list (docs/architecture.html
/// §5.1: FTL, LTL, BULK, ...) that CreateLoadRequest.LoadTypeId already required —
/// this is what actually lets a caller discover a valid id, rather than needing to
/// already know one. Read-only, since nothing in this codebase creates/edits a
/// LoadType via the API (LoadType has no TenantId/CompanyId of its own to write one
/// against). Open to any authenticated caller, portal contacts included — unlike
/// Locations/CostCentres, the Customer Portal's own self-service booking (§13.2) needs
/// a valid LoadTypeId exactly as much as staff does.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/load-types")]
[Authorize]
public class LoadTypesController : ControllerBase
{
    private readonly TmsDbContext _db;

    public LoadTypesController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<LoadType>>> List(CancellationToken ct) =>
        Ok(await _db.LoadTypes.OrderBy(t => t.Code).ToListAsync(ct));
}
