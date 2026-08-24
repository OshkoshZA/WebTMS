namespace Tms.Shared;

/// <summary>
/// Base for every entity that lives inside exactly one Tenant — the absolute SaaS
/// isolation boundary (docs/architecture.html §4.1). TenantId is resolved once from
/// the caller's JWT at the top of the request pipeline and applied by a global EF Core
/// query filter (see Tms.Infrastructure.TmsDbContext) — it is never accepted from a
/// request body or query string.
/// </summary>
public abstract class TenantScopedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
}
