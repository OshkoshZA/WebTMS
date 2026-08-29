using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Loads;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateSubcontractorRequest(
    string Name, string RegistrationNo, Guid CurrencyId, DateOnly? InsuranceExpiry, string? BankingDetails, int PaymentTermsDays);

public record UpdateSubcontractorRequest(
    string Name, string RegistrationNo, DateOnly? InsuranceExpiry, string? BankingDetails, int PaymentTermsDays);

public record AddSubcontractorCurrencyRequest(Guid CurrencyId);

/// <summary>
/// Subcontractor (third-party carrier) master data (docs/architecture.html §5.1, §10.2).
/// Follows the standard master-data CRUD convention (§11.5): list / get / create /
/// update / deactivate — never a hard delete, since a Subcontractor underpins buy-rate
/// and accrual/payables history once the rest of Tms.Modules.Billing's buy side lands.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/subcontractors")]
[Authorize]
public class SubcontractorsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public SubcontractorsController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Subcontractor>>> List(CancellationToken ct)
        => Ok(await _db.Subcontractors.OrderBy(s => s.Name).ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Subcontractor>> Get(Guid id, CancellationToken ct)
    {
        var subcontractor = await _db.Subcontractors.FirstOrDefaultAsync(s => s.Id == id, ct);
        return subcontractor is null ? NotFound() : Ok(subcontractor);
    }

    [HttpPost]
    [Authorize(Policy = "subcontractor.master.manage")]
    public async Task<ActionResult<Subcontractor>> Create(CreateSubcontractorRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (!await _db.Currencies.AnyAsync(c => c.Id == request.CurrencyId, ct))
            return NotFound($"Currency {request.CurrencyId} was not found.");

        var subcontractor = new Subcontractor
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            Name = request.Name,
            RegistrationNo = request.RegistrationNo,
            CurrencyId = request.CurrencyId,
            InsuranceExpiry = request.InsuranceExpiry,
            BankingDetails = request.BankingDetails,
            PaymentTermsDays = request.PaymentTermsDays
        };

        _db.Subcontractors.Add(subcontractor);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = subcontractor.Id }, subcontractor);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "subcontractor.master.manage")]
    public async Task<IActionResult> Update(Guid id, UpdateSubcontractorRequest request, CancellationToken ct)
    {
        var subcontractor = await _db.Subcontractors.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subcontractor is null) return NotFound();

        // Currency is deliberately not editable here — same reasoning as Client: a real
        // currency change is its own function-gated action, not a plain field edit.
        subcontractor.Name = request.Name;
        subcontractor.RegistrationNo = request.RegistrationNo;
        subcontractor.InsuranceExpiry = request.InsuranceExpiry;
        subcontractor.BankingDetails = request.BankingDetails;
        subcontractor.PaymentTermsDays = request.PaymentTermsDays;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "subcontractor.master.manage")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var subcontractor = await _db.Subcontractors.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subcontractor is null) return NotFound();

        subcontractor.Status = SubcontractorStatus.Deactivated; // never a hard delete — §11.5
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Currencies this subcontractor is permitted to be paid in, beyond its primary — its own CurrencyId is always implicitly allowed and isn't listed here (docs/architecture.html §4.3).</summary>
    [HttpGet("{id:guid}/currencies")]
    public async Task<ActionResult<IEnumerable<SubcontractorCurrency>>> Currencies(Guid id, CancellationToken ct)
    {
        if (!await _db.Subcontractors.AnyAsync(s => s.Id == id, ct)) return NotFound();

        return Ok(await _db.Set<SubcontractorCurrency>().Where(sc => sc.SubcontractorId == id).ToListAsync(ct));
    }

    /// <summary>Grants this subcontractor an additional currency to be paid in (§4.3) — the primary CurrencyId set at Create is always allowed and never needs a row here. No credit limit involved, unlike ClientsController's equivalent — we owe the subcontractor, not the reverse.</summary>
    [HttpPost("{id:guid}/currencies")]
    [Authorize(Policy = "subcontractor.master.manage")]
    public async Task<ActionResult<SubcontractorCurrency>> AddCurrency(Guid id, AddSubcontractorCurrencyRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var subcontractor = await _db.Subcontractors.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subcontractor is null) return NotFound();

        if (!await _db.Currencies.AnyAsync(c => c.Id == request.CurrencyId, ct))
            return NotFound($"Currency {request.CurrencyId} was not found.");

        if (request.CurrencyId == subcontractor.CurrencyId)
            return Conflict("That is already this subcontractor's primary currency.");
        if (await _db.Set<SubcontractorCurrency>().AnyAsync(sc => sc.SubcontractorId == id && sc.CurrencyId == request.CurrencyId, ct))
            return Conflict("This subcontractor is already permitted to be paid in that currency.");

        var subcontractorCurrency = new SubcontractorCurrency
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            SubcontractorId = id,
            CurrencyId = request.CurrencyId
        };
        _db.Set<SubcontractorCurrency>().Add(subcontractorCurrency);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Currencies), new { id }, subcontractorCurrency);
    }
}
