namespace Tms.Modules.Loads;

/// <summary>
/// Shared reference list classifying how a load is run — FTL, LTL, BULK, CONTAINER,
/// REEFER, ABNORMAL (docs/architecture.html §5.1). Not company-scoped.
/// </summary>
public class LoadType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
