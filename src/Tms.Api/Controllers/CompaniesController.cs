using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Identity;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record UpdateCompanyRequest(
    string LegalName, string? TradingName, string RegistrationNo, string VatNumber,
    string PhysicalAddress, string PostalAddress, string BankingDetails,
    string InvoiceNumberPrefix, string? LogoUrl, bool InvoicingEnabled);

/// <summary>
/// A Tenant's own operating legal entities — the letterhead master data every invoice,
/// credit note, and load confirmation is built from (docs/architecture.html §5.1, §06,
/// §11.2). CountryId and CurrencyId are deliberately not editable here, the same
/// reasoning as a Client's or Subcontractor's own fixed currency (§04) — set once at
/// onboarding, not a plain field edit. A Company itself is created as part of tenant
/// onboarding, not through this API — only GET/PUT are exposed, matching §11.2.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies")]
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public CompaniesController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>Never part of either portal's documented scope, and this is the tenant's own letterhead master data (BankingDetails, VatNumber, RegistrationNo included) — any portal contact is Forbidden outright.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Company>>> List(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        return Ok(await _db.Companies.OrderBy(c => c.LegalName).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Company>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct);
        return company is null ? NotFound() : Ok(company);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "company.master.manage")]
    public async Task<IActionResult> Update(Guid id, UpdateCompanyRequest request, CancellationToken ct)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (company is null) return NotFound();

        company.LegalName = request.LegalName;
        company.TradingName = request.TradingName;
        company.RegistrationNo = request.RegistrationNo;
        company.VatNumber = request.VatNumber;
        company.PhysicalAddress = request.PhysicalAddress;
        company.PostalAddress = request.PostalAddress;
        company.BankingDetails = request.BankingDetails;
        company.InvoiceNumberPrefix = request.InvoiceNumberPrefix;
        company.LogoUrl = request.LogoUrl;
        company.InvoicingEnabled = request.InvoicingEnabled;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
