using Microsoft.EntityFrameworkCore;
using Tms.Api.Auth;
using Tms.Infrastructure;

namespace Tms.Api.Services;

/// <summary>
/// The scheduled half of §16.1's compliance-expiry source (Fig. 13) — previously
/// on-demand only ("no background job framework exists anywhere in this codebase"),
/// now also swept on a schedule via §11.3's job infrastructure, so an expiry gets
/// flagged even if nobody happens to visit the Compliance screen that day.
/// ComplianceReconciliationService.ReconcileAsync only ever reconciles the single
/// Tenant/Company its own ITenantContext resolves to — a request gets that from the
/// caller's JWT, but nothing resolves one for a job with no request of its own, so
/// this lists every Company (via the same IsPlatformSupport bypass WebhookRetryJob
/// uses to see across tenants) and, for each one, opens a fresh scope with that
/// Company's own TenantId/CompanyId set directly on HttpTenantContext — normal
/// single-company scoping, not a bypass, since the point here is to reconcile each
/// company on its own terms exactly as its own staff's on-demand call would.
/// </summary>
public class ComplianceReconcileJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ComplianceReconcileJob> _logger;

    public ComplianceReconcileJob(IServiceScopeFactory scopeFactory, ILogger<ComplianceReconcileJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var companies = await ListCompaniesAsync(ct);

        foreach (var (tenantId, companyId) in companies)
        {
            using var scope = _scopeFactory.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<HttpTenantContext>();
            tenantContext.TenantId = tenantId;
            tenantContext.CompanyId = companyId;

            var reconciliation = scope.ServiceProvider.GetRequiredService<ComplianceReconciliationService>();
            try
            {
                await reconciliation.ReconcileAsync(ct);
            }
            catch (Exception ex)
            {
                // One company's failure shouldn't stop the rest of this sweep — it's
                // picked up again next run regardless.
                _logger.LogError(ex, "Compliance reconcile job failed for company {CompanyId}", companyId);
            }
        }
    }

    private async Task<List<(Guid TenantId, Guid CompanyId)>> ListCompaniesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpTenantContext>().IsPlatformSupport = true;
        var db = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var rows = await db.Companies.Select(c => new { c.TenantId, c.Id }).ToListAsync(ct);
        return rows.Select(r => (r.TenantId, r.Id)).ToList();
    }
}
