using System.Security.Cryptography;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Identity;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateApiClientRequest(string Name, Guid RoleId, int? RateLimitPerMinute = null);

public record CreateApiClientResponse(Guid Id, string Name, string ClientId, string ClientSecret, int RateLimitPerMinute);

public record ApiClientResponse(
    Guid Id, string Name, string ClientId, ApiClientStatus Status, int RateLimitPerMinute, DateTimeOffset CreatedAt);

public record RotateSecretResponse(string ClientSecret);

public record UpdateRateLimitRequest(int RateLimitPerMinute);

/// <summary>
/// Provisions system-to-system integration partners for the OAuth2 client-credentials
/// grant (docs/architecture.html §11.1). Managing these is itself function-gated —
/// see integration.apiclient.manage — the same declarative mechanism every function
/// check in the API uses (§07), not a special case for this controller.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/api-clients")]
[Authorize]
public class ApiClientsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private static readonly PasswordHasher<ApiClient> SecretHasher = new();

    public ApiClientsController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApiClientResponse>>> List(CancellationToken ct)
        => Ok(await _db.ApiClients
            .OrderBy(c => c.Name)
            .Select(c => new ApiClientResponse(c.Id, c.Name, c.ClientId, c.Status, c.RateLimitPerMinute, c.CreatedAt))
            .ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiClientResponse>> Get(Guid id, CancellationToken ct)
    {
        var client = await _db.ApiClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        return client is null
            ? NotFound()
            : Ok(new ApiClientResponse(client.Id, client.Name, client.ClientId, client.Status, client.RateLimitPerMinute, client.CreatedAt));
    }

    /// <summary>Creates an integration partner and its first secret — the secret is returned once, here, and never again.</summary>
    [HttpPost]
    [Authorize(Policy = "integration.apiclient.manage")]
    public async Task<ActionResult<CreateApiClientResponse>> Create(CreateApiClientRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        if (!await _db.Roles.AnyAsync(r => r.Id == request.RoleId, ct))
            return NotFound($"Role {request.RoleId} was not found.");

        var client = new ApiClient
        {
            TenantId = _tenantContext.TenantId.Value,
            CompanyId = _tenantContext.CompanyId.Value,
            Name = request.Name,
            ClientId = Guid.NewGuid().ToString("N"),
            RateLimitPerMinute = request.RateLimitPerMinute is int rl and > 0 ? rl : 60
        };
        _db.ApiClients.Add(client);

        var plaintextSecret = GenerateSecret();
        _db.Set<ApiClientSecret>().Add(new ApiClientSecret
        {
            TenantId = client.TenantId,
            CompanyId = client.CompanyId,
            ApiClientId = client.Id,
            SecretHash = SecretHasher.HashPassword(client, plaintextSecret)
        });

        _db.Set<ApiClientRole>().Add(new ApiClientRole
        {
            ApiClientId = client.Id,
            CompanyId = client.CompanyId,
            RoleId = request.RoleId
        });

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = client.Id },
            new CreateApiClientResponse(client.Id, client.Name, client.ClientId, plaintextSecret, client.RateLimitPerMinute));
    }

    /// <summary>Adjusts a partner's rate limit (§11.1) — takes effect on their next token request, since the limit is embedded as a claim, not looked up per API call.</summary>
    [HttpPut("{id:guid}/rate-limit")]
    [Authorize(Policy = "integration.apiclient.manage")]
    public async Task<IActionResult> UpdateRateLimit(Guid id, UpdateRateLimitRequest request, CancellationToken ct)
    {
        if (request.RateLimitPerMinute <= 0)
            return BadRequest("RateLimitPerMinute must be positive.");

        var client = await _db.ApiClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return NotFound();

        client.RateLimitPerMinute = request.RateLimitPerMinute;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Adds a new active secret without revoking existing ones — an overlap window for rotating credentials without a hard cutover.</summary>
    [HttpPost("{id:guid}/secrets")]
    [Authorize(Policy = "integration.apiclient.manage")]
    public async Task<ActionResult<RotateSecretResponse>> RotateSecret(Guid id, CancellationToken ct)
    {
        var client = await _db.ApiClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return NotFound();

        var plaintextSecret = GenerateSecret();
        _db.Set<ApiClientSecret>().Add(new ApiClientSecret
        {
            TenantId = client.TenantId,
            CompanyId = client.CompanyId,
            ApiClientId = client.Id,
            SecretHash = SecretHasher.HashPassword(client, plaintextSecret)
        });

        await _db.SaveChangesAsync(ct);
        return Ok(new RotateSecretResponse(plaintextSecret));
    }

    /// <summary>Revokes an integration partner entirely — never a hard delete (§11.5); every token request it makes afterward is refused.</summary>
    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = "integration.apiclient.manage")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var client = await _db.ApiClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return NotFound();

        client.Status = ApiClientStatus.Revoked;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string GenerateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
