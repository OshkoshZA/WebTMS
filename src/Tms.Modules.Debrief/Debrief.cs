using Tms.Shared;

namespace Tms.Modules.Debrief;

public enum DebriefStatus
{
    PendingReview,
    Approved
}

public enum IncidentType
{
    Delay,
    Damage,
    Breakdown
}

public enum IncidentSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// Post-trip reconciliation for one leg (docs/architecture.html §09) — the gate a leg
/// must clear before it can be billed to the client or paid to a subcontractor (Fig. 5).
/// Submitted once per leg, only once it's Delivered; auto-approved immediately if
/// nothing about it is exceptional, otherwise held PendingReview for a Debrief Clerk
/// (gated by debrief.approve) to resolve. Either path ends the same way: the leg
/// locks as PodReceived and, if it was Subcontracted, any
/// SubcontractorAccrual its expense lines claimed against gets adjusted (§9.1, §10.2).
///
/// Two of the doc's five exception triggers can't actually be checked yet and are
/// deliberately not faked: "odometer distance deviates &gt;10% from planned route
/// distance" has no planned-distance figure stored anywhere on LoadLeg, and "fuel
/// consumption outside the vehicle's expected range" has no per-vehicle-class
/// consumption reference table. The other three — missing POD, any logged incident,
/// driving hours over the regulatory limit — are real. RegulatoryDrivingHoursLimit is
/// a placeholder constant (no per-country/company config exists yet), the same
/// "structurally correct now, real once it exists" pattern as VAT being 0 before a
/// rate table existed.
/// </summary>
public class Debrief : CompanyScopedEntity
{
    public Guid LoadLegId { get; set; }
    public Guid? DriverId { get; set; }
    public Guid? VehicleId { get; set; }
    public decimal? OdometerStart { get; set; }
    public decimal? OdometerEnd { get; set; }
    public decimal? FuelLitres { get; set; }

    // Not in the doc's own Debrief field list, but §09's prose explicitly says "fuel
    // litres and cost" — capturing the cost alongside the litres is obviously worth
    // keeping, the same reasoning as LoadConfirmation.DeclineReason.
    public decimal? FuelCost { get; set; }

    public decimal? DrivingHours { get; set; }
    public bool PodReceived { get; set; }

    // Not in the doc's own field list either — same "no upload infrastructure exists
    // yet, just a URL" treatment as Company.LogoUrl/Invoice.PdfUrl.
    public string? PodImageUrl { get; set; }

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DebriefStatus Status { get; set; }

    // A human-readable, comma-joined summary of why this needed review (e.g. "Missing
    // POD, 1 incident(s) logged") — not a structured table of its own; nothing else in
    // this codebase queries or filters on individual exception reasons, only shows them.
    public string? ExceptionReasons { get; set; }

    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }

    public List<DebriefIncident> Incidents { get; set; } = new();
    public List<DebriefExpense> Expenses { get; set; } = new();
}

/// <summary>One delay/damage/breakdown logged against a Debrief (§09) — any incident at all, regardless of severity, routes its Debrief to PendingReview.</summary>
public class DebriefIncident : CompanyScopedEntity
{
    public Guid DebriefId { get; set; }
    public IncidentType Type { get; set; }
    public IncidentSeverity Severity { get; set; }
    public string Narrative { get; set; } = string.Empty;
}
