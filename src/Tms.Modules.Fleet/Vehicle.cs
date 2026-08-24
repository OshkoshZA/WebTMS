using Tms.Shared;

namespace Tms.Modules.Fleet;

public enum VehicleType
{
    Horse,
    Trailer,
    Rigid
}

/// <summary>Company-owned truck or trailer (docs/architecture.html §5.1).</summary>
public class Vehicle : CompanyScopedEntity
{
    public string FleetNo { get; set; } = string.Empty;
    public string Registration { get; set; } = string.Empty;
    public VehicleType Type { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public DateOnly? LicenceExpiry { get; set; }
    public DateOnly? VehicleTestExpiry { get; set; }
}
