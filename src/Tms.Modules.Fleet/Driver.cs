using Tms.Shared;

namespace Tms.Modules.Fleet;

public enum DriverStatus
{
    Active,
    OnLeave,
    Deactivated
}

/// <summary>Company-employed driver (docs/architecture.html §5.1).</summary>
public class Driver : CompanyScopedEntity
{
    public string EmployeeNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LicenceCode { get; set; } = string.Empty;
    public DateOnly? LicenceExpiry { get; set; }
    public DateOnly? PdpExpiry { get; set; }
    public Guid? HomeCostCentreId { get; set; }
    public DriverStatus Status { get; set; } = DriverStatus.Active;
}
