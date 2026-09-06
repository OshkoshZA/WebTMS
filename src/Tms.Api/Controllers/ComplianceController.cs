using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tms.Api.Services;
using Tms.Shared;

namespace Tms.Api.Controllers;

/// <summary>
/// Wires vehicle/driver compliance expiry into the shared Exception mechanism
/// (docs/architecture.html §16.1, Fig. 13) — the third of that figure's six sources.
/// The actual scan lives in ComplianceReconciliationService, shared with
/// ComplianceReconcileJob's own scheduled sweep (§11.3) — this action stays the
/// on-demand path: tms-app's own Compliance screen (§5.1) calls it once per visit, so
/// exceptions also freshen immediately on genuine human review activity rather than
/// only on the job's own schedule.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/compliance")]
[Authorize]
public class ComplianceController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly ComplianceReconciliationService _reconciliation;

    public ComplianceController(ITenantContext tenantContext, ComplianceReconciliationService reconciliation)
    {
        _tenantContext = tenantContext;
        _reconciliation = reconciliation;
    }

    /// <summary>See ComplianceReconciliationService.ReconcileAsync for the actual scan/raise/resolve logic — this action only applies the request-specific caller guards before delegating to it.</summary>
    [HttpPost("reconcile-exceptions")]
    [Authorize(Policy = "exception.manage")]
    public async Task<IActionResult> ReconcileExceptions(CancellationToken ct)
    {
        if (_tenantContext.SubcontractorId is not null || _tenantContext.ClientId is not null) return Forbid();
        if (_tenantContext.TenantId is null || _tenantContext.CompanyId is null)
            return Unauthorized("Request is missing a resolved Tenant/Company context.");

        await _reconciliation.ReconcileAsync(ct);
        return NoContent();
    }
}
