using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;

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

    public FunctionsController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FunctionResponse>>> List(CancellationToken ct)
        => Ok(await _db.Functions
            .OrderBy(f => f.Code)
            .Select(f => new FunctionResponse(f.Id, f.Code, f.Description))
            .ToListAsync(ct));
}
