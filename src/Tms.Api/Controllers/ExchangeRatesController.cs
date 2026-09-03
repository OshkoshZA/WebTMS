using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Rating;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CaptureExchangeRateRequest(Guid FromCurrencyId, Guid ToCurrencyId, DateOnly EffectiveDate, decimal Rate);

public record ExchangeRateResponse(Guid Id, Guid FromCurrencyId, Guid ToCurrencyId, DateOnly EffectiveDate, decimal Rate);

/// <summary>
/// Manual capture/override of currency-pair rates (docs/architecture.html §4.3, §11.2) —
/// the automated daily-refresh background job §4.3 also describes is out of scope here;
/// this is the "missing or disputed" manual path finance falls back to either way.
/// LoadsController.Margin is the consumer: it reads from this table to convert a leg's
/// buy amount into its sell currency.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/exchange-rates")]
[Authorize]
public class ExchangeRatesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ExchangeRatesController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// The rate "effective on" a date is the most recently captured one on or before
    /// it — rates aren't captured for every single day, so an exact-date match would
    /// make this unusable for any date finance hasn't specifically touched.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ExchangeRateResponse>> Get(Guid from, Guid to, DateOnly date, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var rate = await _db.ExchangeRates
            .Where(e => e.FromCurrencyId == from && e.ToCurrencyId == to && e.EffectiveDate <= date)
            .OrderByDescending(e => e.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        return rate is null
            ? NotFound($"No captured rate for that currency pair on or before {date:yyyy-MM-dd}.")
            : Ok(ToResponse(rate));
    }

    /// <summary>
    /// Captures a rate, or overrides the one already captured for this exact
    /// (pair, date) — never a second conflicting row for it (ExchangeRateIndex,
    /// TmsDbContext), matching how a provider correction or a finance dispute both
    /// mean "this is the right number for that day," not "add another candidate."
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "finance.exchangerate.manage")]
    public async Task<ActionResult<ExchangeRateResponse>> Capture(CaptureExchangeRateRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (request.FromCurrencyId == request.ToCurrencyId)
            return BadRequest("FromCurrencyId and ToCurrencyId must differ.");
        if (request.Rate <= 0)
            return BadRequest("Rate must be positive.");

        if (!await _db.Currencies.AnyAsync(c => c.Id == request.FromCurrencyId, ct))
            return NotFound($"Currency {request.FromCurrencyId} was not found.");
        if (!await _db.Currencies.AnyAsync(c => c.Id == request.ToCurrencyId, ct))
            return NotFound($"Currency {request.ToCurrencyId} was not found.");

        var existing = await _db.ExchangeRates.FirstOrDefaultAsync(e =>
            e.FromCurrencyId == request.FromCurrencyId && e.ToCurrencyId == request.ToCurrencyId &&
            e.EffectiveDate == request.EffectiveDate, ct);

        if (existing is not null)
        {
            existing.Rate = request.Rate;
            await _db.SaveChangesAsync(ct);
            return Ok(ToResponse(existing));
        }

        var rate = new ExchangeRate
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            FromCurrencyId = request.FromCurrencyId,
            ToCurrencyId = request.ToCurrencyId,
            EffectiveDate = request.EffectiveDate,
            Rate = request.Rate
        };

        _db.ExchangeRates.Add(rate);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // ExchangeRateIndex catches a race between two concurrent Capture calls for
            // the same brand-new (pair, date) that both read "no existing row" before
            // either committed.
            return Conflict("A rate for that currency pair and date was captured by a concurrent request — retry to update it instead.");
        }

        return CreatedAtAction(nameof(Get), new { from = rate.FromCurrencyId, to = rate.ToCurrencyId, date = rate.EffectiveDate }, ToResponse(rate));
    }

    private static ExchangeRateResponse ToResponse(ExchangeRate e) => new(e.Id, e.FromCurrencyId, e.ToCurrencyId, e.EffectiveDate, e.Rate);
}
