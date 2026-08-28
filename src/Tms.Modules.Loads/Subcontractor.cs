using Tms.Shared;

namespace Tms.Modules.Loads;

public enum SubcontractorStatus
{
    Active,
    Deactivated
}

/// <summary>
/// Third-party carrier used for outsourced legs (docs/architecture.html §5.1, §10.2).
/// Currency is fixed once per subcontractor, not per transaction — every buy rate line
/// and subcontractor expense for this carrier (later phases of Tms.Modules.Billing) is
/// automatically denominated in it. LoadLeg.SubcontractorId already exists as a bare
/// Guid, seeded ahead of this entity; this is what makes it a real reference.
/// </summary>
public class Subcontractor : CompanyScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public string RegistrationNo { get; set; } = string.Empty;
    public Guid CurrencyId { get; set; }
    public DateOnly? InsuranceExpiry { get; set; }
    public string? BankingDetails { get; set; }
    public int PaymentTermsDays { get; set; } = 30;
    public SubcontractorStatus Status { get; set; } = SubcontractorStatus.Active;
}
