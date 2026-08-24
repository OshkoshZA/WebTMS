namespace Tms.Modules.Identity;

public enum TenantStatus
{
    Active,
    Suspended,
    Cancelled
}

/// <summary>
/// An independent customer of the SaaS platform — the outer, absolute isolation
/// boundary (docs/architecture.html §4.1). Owns one or more Companies.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string PlanTier { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
