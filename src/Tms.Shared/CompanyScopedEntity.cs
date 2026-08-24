namespace Tms.Shared;

/// <summary>
/// Base for entities scoped to one Company within a Tenant (docs/architecture.html §4.3).
/// Both TenantId and CompanyId are enforced by the same global query filter, so a query
/// can never accidentally cross a company, let alone a tenant.
/// </summary>
public abstract class CompanyScopedEntity : TenantScopedEntity
{
    public Guid CompanyId { get; set; }
}
