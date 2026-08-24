namespace Tms.Shared;

/// <summary>
/// Shared reference data (docs/architecture.html §04, Fig. 2): Country, Currency, and
/// Unit of Measure are the same across every Tenant and Company — unlike Location,
/// Commodity, or Vehicle, which are each scoped to one Company.
/// </summary>
public class Country
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty; // ISO 3166-1 alpha-2, e.g. "ZA"
    public string Name { get; set; } = string.Empty;
}

public class Currency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty; // ISO 4217, e.g. "ZAR"
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
}

public class UnitOfMeasure
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty; // PER_KM, PER_TON, PER_PALLET, PER_LOAD, PER_HOUR, PER_LITRE
    public string Description { get; set; } = string.Empty;
}
