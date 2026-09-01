using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Shared;

namespace Tms.Api.Controllers;

/// <summary>
/// Read-only Function catalog (docs/architecture.html §07, §11.2) — functions are
/// registered by the API itself as endpoints ship, never user-created, so there is no
/// Create/Update/Delete here. Used by RolesController's grant endpoint to look up
/// valid FunctionIds.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/functions")]
[Authorize]
public class FunctionsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public FunctionsController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>Never part of either portal's documented scope — internal capability names/descriptions aren't meant for an external contact, so any portal contact is Forbidden outright.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<FunctionResponse>>> List(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        return Ok(await _db.Functions
            .OrderBy(f => f.Code)
            .Select(f => new FunctionResponse(f.Id, f.Code, f.Description))
            .ToListAsync(ct));
    }
}
