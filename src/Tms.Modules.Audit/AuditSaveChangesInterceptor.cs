using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tms.Modules.Identity;
using Tms.Shared;

namespace Tms.Modules.Audit;

/// <summary>
/// Central capture point for the audit trail (docs/architecture.html §12, Fig. 9).
/// Every DbContext.SaveChanges(Async) call across every module goes through this —
/// coverage doesn't depend on a module remembering to "add auditing" for itself.
/// AuditEntry rows are written in the same SaveChanges call as the change they
/// describe, so the two can never drift apart.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserAccessor _currentUser;

    public AuditSaveChangesInterceptor(ICurrentUserAccessor currentUser)
    {
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null) return;

        var entries = new List<AuditEntry>();

        // Materialized to a list before iterating: ResolveTenantId can call context.Find,
        // which may attach a newly-loaded entity to this same ChangeTracker mid-loop —
        // enumerating the live tracker directly would then throw "collection modified".
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            // The audit store itself is append-only — never audit changes to AuditEntry.
            if (entry.Entity is AuditEntry) continue;

            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Create,
                EntityState.Modified => AuditAction.Update,
                EntityState.Deleted => AuditAction.Delete,
                _ => (AuditAction?)null
            };
            if (action is null) continue;

            entries.Add(new AuditEntry
            {
                TenantId = ResolveTenantId(entry, context),
                CompanyId = ResolveCompanyId(entry),
                EntityType = entry.Entity.GetType().Name,
                EntityId = ResolveEntityId(entry),
                Action = action.Value,
                ChangedByUserId = _currentUser.UserId,
                ChangedByApiClientId = _currentUser.ApiClientId,
                OldValueJson = action == AuditAction.Update || action == AuditAction.Delete
                    ? SerializeOriginalValues(entry)
                    : null,
                NewValueJson = action == AuditAction.Create || action == AuditAction.Update
                    ? SerializeCurrentValues(entry)
                    : null
            });
        }

        foreach (var e in entries)
        {
            context.Set<AuditEntry>().Add(e);
        }
    }

    /// <summary>
    /// TenantScopedEntity covers most of the model, but ApplicationUser/ApplicationRole
    /// carry TenantId directly without deriving from it (they extend IdentityUser/
    /// IdentityRole instead), and UserCompanyRole/RoleFunction/ApiClientRole are plain
    /// join tables with no TenantId of their own at all — their tenant only exists on
    /// whichever parent they reference. context.Find checks this same ChangeTracker
    /// first (so a parent created in the very same SaveChanges call, e.g. a new
    /// ApiClient and its ApiClientRole, resolves with no extra query) and falls back to
    /// a real lookup otherwise.
    /// </summary>
    private static Guid ResolveTenantId(EntityEntry entry, DbContext context) => entry.Entity switch
    {
        TenantScopedEntity tenantScoped => tenantScoped.TenantId,
        ApplicationUser user => user.TenantId,
        ApplicationRole role => role.TenantId,
        UserCompanyRole ucr => context.Find<Company>(ucr.CompanyId)?.TenantId ?? Guid.Empty,
        RoleFunction rf => context.Find<ApplicationRole>(rf.RoleId)?.TenantId ?? Guid.Empty,
        ApiClientRole acr => context.Find<ApiClient>(acr.ApiClientId)?.TenantId ?? Guid.Empty,
        _ => Guid.Empty
    };

    private static Guid? ResolveCompanyId(EntityEntry entry) => entry.Entity switch
    {
        CompanyScopedEntity companyScoped => companyScoped.CompanyId,
        UserCompanyRole ucr => ucr.CompanyId,
        ApiClientRole acr => acr.CompanyId,
        _ => null
    };

    private static string ResolveEntityId(EntityEntry entry)
    {
        var idProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
        return idProperty?.CurrentValue?.ToString() ?? string.Empty;
    }

    private static string SerializeCurrentValues(EntityEntry entry) =>
        JsonSerializer.Serialize(entry.CurrentValues.ToObject());

    private static string SerializeOriginalValues(EntityEntry entry) =>
        JsonSerializer.Serialize(entry.OriginalValues.ToObject());
}

/// <summary>
/// Resolved once per request from the caller's JWT (interactive user or system-to-system
/// API client) — implemented in Tms.Api so this module never depends on ASP.NET Core.
/// </summary>
public interface ICurrentUserAccessor
{
    Guid? UserId { get; }
    string? ApiClientId { get; }
}
