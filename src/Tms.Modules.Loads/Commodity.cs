using Tms.Shared;

namespace Tms.Modules.Loads;

public enum CommodityCategory
{
    Fuel,
    BulkLiquid,
    DryBulk,
    BreakBulk,
    General
}

/// <summary>
/// A product the company moves — the master catalogue (docs/architecture.html §5.5).
/// A leg carries one or more <see cref="CommodityLine"/>s rather than a single price;
/// this is what lets a fuel tanker carry diesel and petrol on the same trip, each
/// rated independently.
/// </summary>
public class Commodity : CompanyScopedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid DefaultUnitOfMeasureId { get; set; }
    public CommodityCategory Category { get; set; } = CommodityCategory.General;
    public bool Active { get; set; } = true;
}
