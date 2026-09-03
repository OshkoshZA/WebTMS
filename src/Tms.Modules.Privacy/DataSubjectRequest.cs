using Tms.Shared;

namespace Tms.Modules.Privacy;

public enum DsrSubjectType
{
    Driver,
    ClientContact,
    SubcontractorContact,
    User
}

public enum DsrRequestType
{
    Access,
    Rectification,
    Erasure,
    Portability
}

public enum DsrStatus
{
    Received,
    InProgress,
    Fulfilled,
    Rejected
}

/// <summary>
/// A logged data subject request (docs/architecture.html §14.3, Fig. 11) — Tenant-scoped
/// rather than Company-scoped, since the Tenant itself is the Data Controller (§14),
/// and a request's subject (a Driver, or an ApplicationUser backing an internal User/
/// ClientContact/SubcontractorContact — there's no separate contact entity, see
/// ApplicationUser's own class doc) can only ever belong to one Tenant anyway.
/// </summary>
public class DataSubjectRequest : TenantScopedEntity
{
    public DsrSubjectType SubjectType { get; set; }
    public Guid SubjectId { get; set; }
    public DsrRequestType RequestType { get; set; }
    public DsrStatus Status { get; set; } = DsrStatus.Received;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset DueDate { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public string? RejectionReason { get; set; }
    public Guid HandledByUserId { get; set; }
}
