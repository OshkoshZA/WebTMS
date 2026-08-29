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
/// zero-or-one for buy. CurrencyId defaults to the Client's (sell) or Subcontractor's
/// (buy) primary currency, but a party can be allowed additional currencies (§4.3 —
/// ClientCurrency/SubcontractorCurrency), so it's stored explicitly here rather than
/// always re-derived from the party.
/// </summary>
public class RateLine : CompanyScopedEntity
{
    public RateLineSourceType SourceType { get; set; }
    public Guid SourceId { get; set; }
    public RateLineDirection Direction { get; set; }
    public Guid CurrencyId { get; set; }
    public decimal RatePerUnit { get; set; }
    public Guid UnitOfMeasureId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
}
