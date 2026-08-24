using Tms.Shared;

namespace Tms.Modules.Rating;

public enum RateLineSourceType
{
    CommodityLine,
    AncillaryServiceLine // Phase 2 — see docs/architecture.html §5.6
}

public enum RateLineDirection
{
    Sell,
    Buy
}

/// <summary>
/// A single priced line against one commodity line or ancillary service line — never
/// against the leg directly (docs/architecture.html §5.1, §08). One for sell,
/// zero-or-one for buy. Currency is not stored here; it is resolved from the Client
/// (sell) or Subcontractor (buy).
/// </summary>
public class RateLine : CompanyScopedEntity
{
    public RateLineSourceType SourceType { get; set; }
    public Guid SourceId { get; set; }
    public RateLineDirection Direction { get; set; }
    public decimal RatePerUnit { get; set; }
    public Guid UnitOfMeasureId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
}
