using Tms.Shared;

namespace Tms.Modules.Privacy;

public enum DataCategory
{
    FinancialRecords,
    DriverPersonalData,
    PortalContactData,
    AuditTrail
}

/// <summary>
/// Per-company, per-category retention configuration (docs/architecture.html §14.2).
/// Laid down in Phase 1 so no retrospective backfill is needed once the full
/// DataSubjectRequest/erasure workflow (§14.3) lands in a later phase.
/// </summary>
public class RetentionPolicy : CompanyScopedEntity
{
    public DataCategory DataCategory { get; set; }
    public int RetentionPeriodYears { get; set; }
    public string LegalBasis { get; set; } = string.Empty;
    public bool AnonymizeAfterExpiry { get; set; } = true;
}
