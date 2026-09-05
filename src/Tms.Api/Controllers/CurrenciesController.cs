using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Shared;

namespace Tms.Api.Controllers;

/// <summary>
/// The shared, non-company-scoped currency reference list (docs/architecture.html §04,
/// §4.3) — Client.CurrencyId, ClientCurrency.CurrencyId, and every RateLine already
/// require one; this is what actually lets a caller discover a valid id, the same
/// "documented by everything that references it, but never independently listable"
/// gap LoadTypesController closed for LoadType. Read-only for the same reason: nothing
/// in this codebase creates or edits a Currency via the API.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/currencies")]
[Authorize]
public class CurrenciesController : ControllerBase
{
    private readonly TmsDbContext _db;

    public CurrenciesController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Currency>>> List(CancellationToken ct) =>
        Ok(await _db.Currencies.OrderBy(c => c.Code).ToListAsync(ct));
}
