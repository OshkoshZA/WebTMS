using Tms.Shared;

namespace Tms.Modules.Loads;

/// <summary>
/// One additional currency a Client is permitted to transact in, beyond its primary
/// Client.CurrencyId (docs/architecture.html §4.3) — each with its own CreditLimit,
/// since a currency this client is never billed in shouldn't share exposure with one
/// it is. A RateLine's CurrencyId is valid for this client only if it's the primary
/// or has a row here; ClientsController's Create/currency endpoints are the only way
/// rows are added, both gated by client.currency.change.
/// </summary>
public class ClientCurrency : CompanyScopedEntity
{
    public Guid ClientId { get; set; }
    public Guid CurrencyId { get; set; }
    public decimal CreditLimit { get; set; }
}

/// <summary>
/// One additional currency a Subcontractor is permitted to be paid in, beyond its
/// primary Subcontractor.CurrencyId (docs/architecture.html §4.3, §10.2) — no credit
/// limit involved here, unlike ClientCurrency, since we owe the subcontractor, not
/// the reverse.
/// </summary>
public class SubcontractorCurrency : CompanyScopedEntity
{
    public Guid SubcontractorId { get; set; }
    public Guid CurrencyId { get; set; }
}
