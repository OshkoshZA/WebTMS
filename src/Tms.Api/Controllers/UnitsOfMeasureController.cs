using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Shared;

namespace Tms.Api.Controllers;

/// <summary>
/// The shared, non-company-scoped unit-of-measure reference list (docs/architecture.html
/// §5.5) — every CommodityLine and Commodity.DefaultUnitOfMeasureId already require one,
/// but nothing previously let a caller discover a valid id independently of a chosen
/// Commodity's own default, the same "documented by everything that references it, but
/// never independently listable" gap CurrenciesController/LoadTypesController closed for
/// their own reference tables. Unlike those two, this follows CommoditiesController's own
/// convention instead — Forbidden outright to any portal caller — since a UnitOfMeasure
/// only ever matters for capturing a commodity line, an internal-staff-only action
/// neither portal's documented scope ever reaches. Read-only for the same reason nothing
/// else here creates/edits: no controller creates or edits a UnitOfMeasure via the API.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/units-of-measure")]
[Authorize]
public class UnitsOfMeasureController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public UnitsOfMeasureController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UnitOfMeasure>>> List(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        return Ok(await _db.UnitsOfMeasure.OrderBy(u => u.Code).ToListAsync(ct));
    }
}
