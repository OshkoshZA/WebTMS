using Tms.Shared;

namespace Tms.Modules.Identity;

/// <summary>
/// One of a Tenant's own operating legal entities — one per registered business per
/// country (docs/architecture.html §5.1). This record is itself the letterhead every
/// invoice, credit note, and load confirmation is built from.
/// </summary>
public class Company : TenantScopedEntity
{
    public string LegalName { get; set; } = string.Empty;
    public string? TradingName { get; set; }
    public string RegistrationNo { get; set; } = string.Empty;
    public string VatNumber { get; set; } = string.Empty;
    public string PhysicalAddress { get; set; } = string.Empty;
    public string PostalAddress { get; set; } = string.Empty;
    public string BankingDetails { get; set; } = string.Empty;
    public string InvoiceNumberPrefix { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Load-tracking-only mode switch (§10): when false, the sell-side invoicing
    /// pipeline never runs for this company's loads, while subcontractor accrual
    /// tracking (buy side) is entirely unaffected.
    /// </summary>
    public bool InvoicingEnabled { get; set; } = true;

    public Guid CountryId { get; set; }

    /// <summary>Reporting currency (§04) — distinct from a Client's or Subcontractor's own fixed currency.</summary>
    public Guid CurrencyId { get; set; }
}
