using Tms.Shared;

namespace Tms.Modules.Loads;

/// <summary>Financial allocation unit (docs/architecture.html §5.1, §06).</summary>
public class CostCentre : CompanyScopedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? ParentCostCentreId { get; set; }
    public bool Active { get; set; } = true;
}
