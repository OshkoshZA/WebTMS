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
public record AddClientCurrencyRequest(Guid CurrencyId, decimal CreditLimit);
public record UpdateClientCurrencyRequest(decimal CreditLimit);

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

    /// <summary>Available credit, WIP, and AR outstanding for a client, in one currency (docs/architecture.html §4.3, §5.4, §11.2) — defaults to the client's primary currency; pass currencyId for one of its additional allowed currencies instead.</summary>
    [HttpGet("{id:guid}/credit-status")]
    public async Task<ActionResult<CreditStatus>> CreditStatus(Guid id, Guid? currencyId, CancellationToken ct)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return NotFound();

        var resolvedCurrencyId = currencyId ?? client.CurrencyId;
        if (await _creditExposure.ResolveCreditLimitAsync(client, resolvedCurrencyId, ct) is null)
            return BadRequest($"Client is not permitted to transact in currency {resolvedCurrencyId}.");

        return Ok(await _creditExposure.GetStatusAsync(client, resolvedCurrencyId, ct));
    }

    /// <summary>Currencies this client is permitted to transact in, beyond its primary — its own CurrencyId is always implicitly allowed and isn't listed here (docs/architecture.html §4.3).</summary>
    [HttpGet("{id:guid}/currencies")]
    public async Task<ActionResult<IEnumerable<ClientCurrency>>> Currencies(Guid id, CancellationToken ct)
    {
        if (!await _db.Clients.AnyAsync(c => c.Id == id, ct)) return NotFound();

        return Ok(await _db.Set<ClientCurrency>().Where(cc => cc.ClientId == id).ToListAsync(ct));
    }

    /// <summary>Grants this client an additional currency to transact in, with its own CreditLimit (§4.3) — the primary CurrencyId set at Create is always allowed and never needs a row here.</summary>
    [HttpPost("{id:guid}/currencies")]
    [Authorize(Policy = "client.currency.change")]
    public async Task<ActionResult<ClientCurrency>> AddCurrency(Guid id, AddClientCurrencyRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return NotFound();

        if (!await _db.Currencies.AnyAsync(c => c.Id == request.CurrencyId, ct))
            return NotFound($"Currency {request.CurrencyId} was not found.");

        if (request.CurrencyId == client.CurrencyId)
            return Conflict("That is already this client's primary currency.");
        if (await _db.Set<ClientCurrency>().AnyAsync(cc => cc.ClientId == id && cc.CurrencyId == request.CurrencyId, ct))
            return Conflict("This client is already permitted to transact in that currency.");

        var clientCurrency = new ClientCurrency
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            ClientId = id,
            CurrencyId = request.CurrencyId,
            CreditLimit = request.CreditLimit
        };
        _db.Set<ClientCurrency>().Add(clientCurrency);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // ClientCurrencyIndex catches a race between two concurrent AddCurrency
            // calls for the same (Client, Currency) pair that both passed the AnyAsync
            // check above — turns that into a clean 409 instead of a raw 500, same
            // pattern as SupplierInvoicesController.Create.
            return Conflict("This client is already permitted to transact in that currency.");
        }

        return CreatedAtAction(nameof(Currencies), new { id }, clientCurrency);
    }

    /// <summary>Updates the CreditLimit for one of this client's additional currencies — the primary currency's limit is still edited through the plain Update action instead.</summary>
    [HttpPut("{id:guid}/currencies/{currencyId:guid}")]
    [Authorize(Policy = "client.currency.change")]
    public async Task<IActionResult> UpdateCurrency(Guid id, Guid currencyId, UpdateClientCurrencyRequest request, CancellationToken ct)
    {
        var clientCurrency = await _db.Set<ClientCurrency>().FirstOrDefaultAsync(cc => cc.ClientId == id && cc.CurrencyId == currencyId, ct);
        if (clientCurrency is null) return NotFound();

        clientCurrency.CreditLimit = request.CreditLimit;
        await _db.SaveChangesAsync(ct);
        return NoContent();
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

    /// <summary>Reverses a Deactivate — the only path back to Active, mirroring how Deactivate is the only path out of it (the same pattern CommoditiesController/CostCentresController/DriversController/VehiclesController/LocationsController already follow; LoadsController.Create's own "reactivate it before booking a new load" error message assumes this exists).</summary>
    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = "client.master.manage")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return NotFound();

        client.Status = ClientStatus.Active;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Invoices raised against a client (docs/architecture.html §11.2).</summary>
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

    /// <summary>Credit notes raised against a client (docs/architecture.html §10.1, §11.2) — invoice-correcting and standalone alike.</summary>
    [HttpGet("{id:guid}/credit-notes")]
    public async Task<ActionResult<IEnumerable<CreditNoteResponse>>> CreditNotes(Guid id, CancellationToken ct)
    {
        if (!await _db.Clients.AnyAsync(c => c.Id == id, ct)) return NotFound();

        var creditNotes = await _db.Set<CreditNote>()
            .Include(cn => cn.Lines)
            .Where(cn => cn.ClientId == id)
            .OrderByDescending(cn => cn.IssueDate)
            .ToListAsync(ct);

        return Ok(creditNotes.Select(CreditNotesController.ToResponse));
    }
}
