using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Privacy;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record RetentionPolicyRequest(DataCategory DataCategory, int RetentionPeriodYears, string LegalBasis, bool AnonymizeAfterExpiry);
public record RetentionPolicyResponse(Guid Id, DataCategory DataCategory, int RetentionPeriodYears, string LegalBasis, bool AnonymizeAfterExpiry);

/// <summary>
/// A Company's per-category retention configuration (docs/architecture.html §14.2) —
/// the RetentionPolicy entity was laid down in Phase 1 with no controller over it until
/// now. Addressed by an explicit CompanyId, the same convention as CompaniesController
/// itself, since a Tenant with several Companies may configure each one differently
/// (e.g. different Countries carry different statutory record-keeping periods).
///
/// PUT replaces the Company's entire policy set in one call, the same "set membership"
/// shape as PUT /roles/{id}/functions — a category left out of the payload has its
/// existing policy removed, since a partial PUT that could only ever add entries would
/// leave no way to retract one. There's no DELETE of an individual policy for that
/// reason; retracting one is just leaving it out of the next PUT.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:guid}/retention-policies")]
[Authorize]
public class RetentionPoliciesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public RetentionPoliciesController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    /// <summary>Never part of either portal's documented scope — the Company's own compliance configuration, so any portal contact is Forbidden outright, matching CompaniesController's own equivalent actions.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RetentionPolicyResponse>>> List(Guid companyId, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();

        if (!await _db.Companies.AnyAsync(c => c.Id == companyId, ct))
            return NotFound($"No Company with id {companyId} was found.");

        var policies = await _db.RetentionPolicies
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.DataCategory)
            .ToListAsync(ct);

        return Ok(policies.Select(ToResponse));
    }

    /// <summary>Replaces the Company's entire policy set: upserts every category in the payload, and removes any existing policy for a category the payload leaves out.</summary>
    [HttpPut]
    [Authorize(Policy = "privacy.retentionpolicy.manage")]
    public async Task<ActionResult<IEnumerable<RetentionPolicyResponse>>> Set(Guid companyId, IEnumerable<RetentionPolicyRequest> request, CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();
        if (_tenantContext.TenantId is null)
            return Unauthorized("Request is missing a resolved Tenant context.");

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return NotFound($"No Company with id {companyId} was found.");

        var incoming = request.ToList();
        var duplicateCategory = incoming.GroupBy(r => r.DataCategory).FirstOrDefault(g => g.Count() > 1);
        if (duplicateCategory is not null)
            return BadRequest($"DataCategory {duplicateCategory.Key} was submitted more than once.");
        if (incoming.Any(r => r.RetentionPeriodYears <= 0))
            return BadRequest("RetentionPeriodYears must be greater than zero.");
        if (incoming.Any(r => string.IsNullOrWhiteSpace(r.LegalBasis)))
            return BadRequest("LegalBasis is required.");

        var existing = await _db.RetentionPolicies.Where(p => p.CompanyId == companyId).ToListAsync(ct);
        var incomingCategories = incoming.Select(r => r.DataCategory).ToHashSet();

        foreach (var stale in existing.Where(p => !incomingCategories.Contains(p.DataCategory)))
            _db.RetentionPolicies.Remove(stale);

        foreach (var item in incoming)
        {
            var policy = existing.FirstOrDefault(p => p.DataCategory == item.DataCategory);
            if (policy is null)
            {
                policy = new RetentionPolicy { TenantId = _tenantContext.TenantId.Value, CompanyId = companyId, DataCategory = item.DataCategory };
                _db.RetentionPolicies.Add(policy);
            }

            policy.RetentionPeriodYears = item.RetentionPeriodYears;
            policy.LegalBasis = item.LegalBasis;
            policy.AnonymizeAfterExpiry = item.AnonymizeAfterExpiry;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // RetentionPolicyIndex catches a race between two concurrent Set calls for
            // the same Company — a clean 409 rather than a raw 500.
            return Conflict("This Company's retention policies were updated concurrently by another request.");
        }

        var result = await _db.RetentionPolicies
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.DataCategory)
            .ToListAsync(ct);
        return Ok(result.Select(ToResponse));
    }

    private static RetentionPolicyResponse ToResponse(RetentionPolicy p) => new(
        p.Id, p.DataCategory, p.RetentionPeriodYears, p.LegalBasis, p.AnonymizeAfterExpiry);
}
