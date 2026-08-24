using Tms.Shared;

namespace Tms.Modules.Loads;

public enum LoadLegExecutionType
{
    OwnFleet,
    Subcontracted
}

/// <summary>
/// A leg's own progress, independent of the other legs on the same load — what
/// Load.Status (§5.2) rolls up from. Not itself documented as a named entity in
/// docs/architecture.html, but needed to make "a load cannot reach Delivered until
/// every leg has been" (§5.2) true for a multi-leg load rather than approximate.
/// </summary>
public enum LoadLegStatus
{
    Planned,
    Allocated,
    InTransit,
    Delivered
}

/// <summary>
/// One movement within a load (docs/architecture.html §5.1) — a load may have several,
/// e.g. collection → hub → final mile. Carries one-or-more <see cref="CommodityLine"/>s
/// rather than a single price itself (§5.5); SubcontractorId is a bare id for now —
/// the Subcontractor entity itself lands with subcontracting in Phase 2 (§10.2).
/// </summary>
public class LoadLeg : CompanyScopedEntity
{
    public Guid LoadId { get; set; }
    public int SequenceNo { get; set; }
    public Guid OriginLocationId { get; set; }
    public Guid DestinationLocationId { get; set; }
    public LoadLegExecutionType ExecutionType { get; set; } = LoadLegExecutionType.OwnFleet;
    public LoadLegStatus Status { get; set; } = LoadLegStatus.Planned;
    public Guid CostCentreId { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? DriverId { get; set; }
    public Guid? SubcontractorId { get; set; } // Phase 2 — see §10.2

    public List<CommodityLine> CommodityLines { get; set; } = new();
}

/// <summary>Who/when/from/to for a Load's status transitions (§5.2) — the audited trail behind Load.Status.</summary>
public class LoadStatusHistory : CompanyScopedEntity
{
    public Guid LoadId { get; set; }
    public LoadStatus FromStatus { get; set; }
    public LoadStatus ToStatus { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid ChangedByUserId { get; set; }
    public string? Reason { get; set; }
}

/// <summary>One commodity, and its quantity, carried on a leg (docs/architecture.html §5.5).</summary>
public class CommodityLine : CompanyScopedEntity
{
    public Guid LoadLegId { get; set; }
    public Guid CommodityId { get; set; }
    public decimal Quantity { get; set; }
    public Guid UnitOfMeasureId { get; set; }
    public int SequenceNo { get; set; }
}
