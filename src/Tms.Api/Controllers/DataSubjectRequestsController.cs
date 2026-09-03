using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Audit;
using Tms.Modules.Fleet;
using Tms.Modules.Identity;
using Tms.Modules.Privacy;
using Tms.Shared;

namespace Tms.Api.Controllers;

public record CreateDataSubjectRequestRequest(DsrSubjectType SubjectType, Guid SubjectId, DsrRequestType RequestType);
public record RejectDataSubjectRequestRequest(string RejectionReason);

public record DataSubjectRequestResponse(
    Guid Id, DsrSubjectType SubjectType, Guid SubjectId, DsrRequestType RequestType, DsrStatus Status,
    DateTimeOffset ReceivedAt, DateTimeOffset DueDate, DateTimeOffset? FulfilledAt, string? RejectionReason, Guid HandledByUserId);

/// <summary>
/// The data subject rights workflow (docs/architecture.html §14.3, Fig. 11) — logging,
/// tracking, and fulfilling Access/Rectification/Erasure/Portability requests against a
/// Driver, or an ApplicationUser backing an internal User/ClientContact/
/// SubcontractorContact (there's no separate contact entity, see ApplicationUser's own
/// class doc). Gated behind privacy.dsr.manage end to end — unlike ExceptionsController,
/// there's no open-to-any-staff read side, since this data implicates real people and
/// carries genuine legal/compliance weight.
///
/// Scope deliberately bounded for this pass: Access/Portability export the subject's own
/// core record only, not a deep trace across every table that might reference them;
/// Rectification is logged and tracked here but the actual correction happens through
/// each subject type's own existing Update endpoint; Erasure anonymizes identity fields
/// (Name, or Email/UserName/DisplayName) but never touches historical AuditEntry
/// snapshots that captured the pre-erasure values — a real, separate piece of work of
/// its own. Erasure is refused unless the subject is already Deactivated: anonymizing an
/// active login's credentials would lock out someone still supposed to be using the
/// system.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/data-subject-requests")]
[Authorize(Policy = "privacy.dsr.manage")]
public class DataSubjectRequestsController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ITenantContext _tenantContext;

    public DataSubjectRequestsController(
        TmsDbContext db, UserManager<ApplicationUser> userManager, ICurrentUserAccessor currentUser, ITenantContext tenantContext)
    {
        _db = db;
        _userManager = userManager;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DataSubjectRequestResponse>>> List(DsrStatus? status, CancellationToken ct)
    {
        var query = _db.DataSubjectRequests.AsQueryable();
        if (status is DsrStatus s) query = query.Where(r => r.Status == s);

        var requests = await query.OrderBy(r => r.DueDate).ToListAsync(ct);
        return Ok(requests.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DataSubjectRequestResponse>> Get(Guid id, CancellationToken ct)
    {
        var request = await _db.DataSubjectRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        return request is null ? NotFound() : Ok(ToResponse(request));
    }

    /// <summary>Due one month out (GDPR's stricter standard; POPIA's own "as soon as reasonably possible" has no fixed deadline to compute from) — regardless of request type, so nothing is ever silently missed.</summary>
    [HttpPost]
    public async Task<ActionResult<DataSubjectRequestResponse>> Create(CreateDataSubjectRequestRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            return Unauthorized("Request is missing a resolved Tenant context.");

        if (!await SubjectExistsAsync(request.SubjectType, request.SubjectId, ct))
            return NotFound($"No {request.SubjectType} with id {request.SubjectId} was found.");

        var now = DateTimeOffset.UtcNow;
        var dsr = new DataSubjectRequest
        {
            TenantId = _tenantContext.TenantId.Value,
            SubjectType = request.SubjectType,
            SubjectId = request.SubjectId,
            RequestType = request.RequestType,
            Status = DsrStatus.Received,
            ReceivedAt = now,
            DueDate = now.AddDays(30),
            HandledByUserId = _currentUser.UserId ?? Guid.Empty
        };

        _db.DataSubjectRequests.Add(dsr);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = dsr.Id }, ToResponse(dsr));
    }

    /// <summary>
    /// Access/Portability/Rectification: a pure status transition — the actual "compile
    /// and return" (Access/Portability) is Export below, live and repeatable; the actual
    /// correction (Rectification) already happened through the subject's own Update
    /// endpoint before this is called. Erasure is the one request type with a real side
    /// effect: it anonymizes the subject in this same call, gated on them already being
    /// Deactivated.
    /// </summary>
    [HttpPost("{id:guid}/fulfill")]
    public async Task<IActionResult> Fulfill(Guid id, CancellationToken ct)
    {
        var request = await _db.DataSubjectRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return NotFound();
        if (request.Status is DsrStatus.Fulfilled or DsrStatus.Rejected)
            return Conflict($"This request is already {request.Status}.");

        if (request.RequestType == DsrRequestType.Erasure)
        {
            var erasureResult = await EraseSubjectAsync(request.SubjectType, request.SubjectId, ct);
            if (erasureResult is not null) return erasureResult;
        }

        request.Status = DsrStatus.Fulfilled;
        request.FulfilledAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, RejectDataSubjectRequestRequest request, CancellationToken ct)
    {
        var dsr = await _db.DataSubjectRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (dsr is null) return NotFound();
        if (dsr.Status is DsrStatus.Fulfilled or DsrStatus.Rejected)
            return Conflict($"This request is already {dsr.Status}.");

        dsr.Status = DsrStatus.Rejected;
        dsr.RejectionReason = request.RejectionReason;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// A live, recomputed snapshot (§11.6: a data export, never archived) — repeatable
    /// any time after Fulfill, not a one-shot side effect of it. Access/Portability only;
    /// Rectification/Erasure have nothing meaningful to "export."
    /// </summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var request = await _db.DataSubjectRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return NotFound();
        if (request.RequestType is not (DsrRequestType.Access or DsrRequestType.Portability))
            return Conflict("Only an Access or Portability request can be exported.");
        if (request.Status != DsrStatus.Fulfilled)
            return Conflict("This request must be Fulfilled before it can be exported.");

        if (request.SubjectType == DsrSubjectType.Driver)
        {
            var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == request.SubjectId, ct);
            if (driver is null) return NotFound("The subject record no longer exists.");
            return Ok(new
            {
                driver.Id, driver.EmployeeNo, driver.Name, driver.LicenceCode,
                driver.LicenceExpiry, driver.PdpExpiry, driver.Status
            });
        }

        var user = await _userManager.FindByIdAsync(request.SubjectId.ToString());
        if (user is null) return NotFound("The subject record no longer exists.");
        return Ok(new
        {
            user.Id, user.Email, user.UserName, user.DisplayName, user.Status,
            user.ClientId, user.SubcontractorId
        });
    }

    private async Task<bool> SubjectExistsAsync(DsrSubjectType subjectType, Guid subjectId, CancellationToken ct) => subjectType switch
    {
        DsrSubjectType.Driver => await _db.Drivers.AnyAsync(d => d.Id == subjectId, ct),
        DsrSubjectType.ClientContact => await _db.Users.AnyAsync(u => u.Id == subjectId && u.ClientId != null, ct),
        DsrSubjectType.SubcontractorContact => await _db.Users.AnyAsync(u => u.Id == subjectId && u.SubcontractorId != null, ct),
        DsrSubjectType.User => await _db.Users.AnyAsync(u => u.Id == subjectId && u.ClientId == null && u.SubcontractorId == null, ct),
        _ => false
    };

    /// <summary>Returns null on success, or an ActionResult to short-circuit Fulfill with (the Deactivated guard failing).</summary>
    private async Task<IActionResult?> EraseSubjectAsync(DsrSubjectType subjectType, Guid subjectId, CancellationToken ct)
    {
        if (subjectType == DsrSubjectType.Driver)
        {
            var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == subjectId, ct);
            if (driver is null) return NotFound("The subject record no longer exists.");
            if (driver.Status != DriverStatus.Deactivated)
                return Conflict("This Driver must be deactivated before an erasure request can be fulfilled.");

            driver.Name = $"Erased Driver {driver.Id.ToString("N")[..8]}";
            await _db.SaveChangesAsync(ct);
            return null;
        }

        var user = await _userManager.FindByIdAsync(subjectId.ToString());
        if (user is null) return NotFound("The subject record no longer exists.");
        if (user.Status != UserStatus.Deactivated)
            return Conflict("This user must be deactivated before an erasure request can be fulfilled.");

        var anonymizedHandle = $"erased-{user.Id.ToString("N")[..8]}@erased.local";
        await _userManager.SetEmailAsync(user, anonymizedHandle);
        await _userManager.SetUserNameAsync(user, anonymizedHandle);
        user.DisplayName = "Erased User";
        await _db.SaveChangesAsync(ct);
        return null;
    }

    private static DataSubjectRequestResponse ToResponse(DataSubjectRequest r) => new(
        r.Id, r.SubjectType, r.SubjectId, r.RequestType, r.Status,
        r.ReceivedAt, r.DueDate, r.FulfilledAt, r.RejectionReason, r.HandledByUserId);
}
