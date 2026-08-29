using Tms.Shared;

namespace Tms.Modules.Fleet;

/// <summary>
/// A named place used as a leg's origin or destination — no GPS/geocoding required
/// (docs/architecture.html §5.1): just a name scoped to a province and a country.
/// </summary>
public class Location : CompanyScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public Guid CountryId { get; set; }

    /// <summary>Never a hard delete (§11.5) — a Location referenced by a leg's history stays retrievable, just no longer selectable for a new one.</summary>
    public bool Active { get; set; } = true;
}
