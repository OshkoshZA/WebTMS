using Tms.Shared;

namespace Tms.Modules.Loads;

/// <summary>
/// Load status lifecycle (docs/architecture.html §5.2). A load's status is the
/// rollup of its legs — see LoadStatusHistory for the audited transition trail.
/// </summary>
public enum LoadStatus
{
    Quoted,
    Booked,
    Allocated,
    InTransit,
    Delivered,
    PodReceived,
    Invoiced,
    OnHold,
    Cancelled,
    Closed
}

/// <summary>Customer's transport request (docs/architecture.html §5.1).</summary>
public class Load : CompanyScopedEntity
{
    public Guid ClientId { get; set; }
    public string ReferenceNo { get; set; } = string.Empty;
    public Guid LoadTypeId { get; set; }
    public LoadStatus Status { get; set; } = LoadStatus.Quoted;
    public DateTimeOffset? PickupWindowStart { get; set; }
    public DateTimeOffset? PickupWindowEnd { get; set; }
    public DateTimeOffset? DeliveryWindowStart { get; set; }
    public DateTimeOffset? DeliveryWindowEnd { get; set; }

    public List<LoadLeg> Legs { get; set; } = new();
}
