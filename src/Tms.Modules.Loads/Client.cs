using Tms.Shared;

namespace Tms.Modules.Loads;

public enum ClientStatus
{
    Active,
    Deactivated
}

/// <summary>
/// Customer the company contracts with and bills (docs/architecture.html §5.1, §5.4).
/// Currency is fixed once per client, not per transaction — every invoice and sell
/// rate line for this client is automatically denominated in it.
/// </summary>
public class Client : CompanyScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public string RegistrationNo { get; set; } = string.Empty;
    public Guid CurrencyId { get; set; }
    public decimal CreditLimit { get; set; }
    public int PaymentTermsDays { get; set; } = 30;
    public Guid? DefaultCostCentreId { get; set; }
    public ClientStatus Status { get; set; } = ClientStatus.Active;
}
