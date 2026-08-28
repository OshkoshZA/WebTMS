using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Services;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Loads;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateClientRequest(string Name, string RegistrationNo, Guid CurrencyId, decimal CreditLimit, int PaymentTermsDays);
public record UpdateClientRequest(string Name, string RegistrationNo, decimal CreditLimit, int PaymentTermsDays);

/// <summary>
/// Client master data (docs/architecture.html §5.1). Follows the standard
/// master-data CRUD convention from §11.5: list / get / create / update /
/// deactivate — never a hard delete, since a Client underpins invoice and
/// credit history once Tms.Modules.Billing lands.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clients")]
[Authorize]
public class ClientsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly CreditExposureService _creditExposure;

    public ClientsController(TmsDbContext db, ITenantContext tenantContext, CreditExposureService creditExposure)
    {
        _db = db;
        _tenantContext = tenantContext;
        _creditExposure = creditExposure;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Client>>> List(CancellationToken ct)
    {
        // No explicit tenant/company filtering here — TmsDbContext's global query
        // filters (§4.1) already scope this to the caller's own data.
        return Ok(await _db.Clients.OrderBy(c => c.Name).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Client>> Get(Guid id, CancellationToken ct)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        return client is null ? NotFound() : Ok(client);
    }

    [HttpPost]
    [Authorize(Policy = "client.master.manage")]
    public async Task<ActionResult<Client>> Create(CreateClientRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var client = new Client
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            Name = request.Name,
            RegistrationNo = request.RegistrationNo,
            CurrencyId = request.CurrencyId,
            CreditLimit = request.CreditLimit,
            PaymentTermsDays = request.PaymentTermsDays
        };

        _db.Clients.Add(client);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = client.Id }, client);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "client.master.manage")]
    public async Task<IActionResult> Update(Guid id, UpdateClientRequest request, CancellationToken ct)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return NotFound();

        // Currency is deliberately not editable here — a real currency change is its
        // own function-gated action (client.currency.change, §04), not a plain field edit.
        client.Name = request.Name;
        client.RegistrationNo = request.RegistrationNo;
        client.CreditLimit = request.CreditLimit;
        client.PaymentTermsDays = request.PaymentTermsDays;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Available credit, WIP, and AR outstanding for a client (docs/architecture.html §5.4, §11.2).</summary>
    [HttpGet("{id:guid}/credit-status")]
    public async Task<ActionResult<CreditStatus>> CreditStatus(Guid id, CancellationToken ct)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return NotFound();

        return Ok(await _creditExposure.GetStatusAsync(client, ct));
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "client.master.manage")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return NotFound();

        client.Status = ClientStatus.Deactivated; // never a hard delete — §11.5
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Invoices raised against a client (docs/architecture.html §11.2) — credit-note detail isn't included yet, since CreditNote doesn't exist (§10.1, a later phase).</summary>
    [HttpGet("{id:guid}/invoices")]
    public async Task<ActionResult<IEnumerable<InvoiceResponse>>> Invoices(Guid id, CancellationToken ct)
    {
        if (!await _db.Clients.AnyAsync(c => c.Id == id, ct)) return NotFound();

        var invoices = await _db.Invoices
            .Include(i => i.Lines)
            .Where(i => i.ClientId == id)
            .OrderByDescending(i => i.IssueDate)
            .ToListAsync(ct);

        return Ok(invoices.Select(InvoicesController.ToResponse));
    }
}
