using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Shared;

namespace Tms.Api.Controllers;

/// <summary>
/// The shared, non-company-scoped country reference list (docs/architecture.html §04,
/// Fig. 2) — LocationsController.Create/Update already require a CountryId, but nothing
/// previously let a caller discover a valid id at all, the same "documented by
/// everything that references it, but never independently listable" gap
/// UnitsOfMeasureController closed for its own reference table. Forbidden to any portal
/// caller like Locations itself, since a Country only ever matters for capturing a
/// Location — an internal-staff-only action neither portal's documented scope reaches.
/// Read-only for the same reason nothing else here creates/edits: no controller creates
/// or edits a Country via the API.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/countries")]
[Authorize]
public class CountriesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public CountriesController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Country>>> List(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        return Ok(await _db.Countries.OrderBy(c => c.Name).ToListAsync(ct));
    }
}
