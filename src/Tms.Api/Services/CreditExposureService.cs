using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Loads;
using Tms.Modules.Rating;

namespace Tms.Api.Services;

public record CreditStatus(
    decimal CreditLimit,
    decimal ArOutstanding,
    decimal Wip,
    decimal TotalExposure,
    decimal AvailableCredit);

/// <summary>
/// Implements the credit control formula from docs/architecture.html §5.4:
/// Total Exposure = AR Outstanding + WIP; Available Credit = CreditLimit − Total Exposure.
///
/// AR Outstanding is now real: Issued/PartPaid Invoice balances for the client (§10.1),
/// net of credit notes — CreditNote doesn't exist yet (a later phase of Billing), so
/// there's nothing to net against until it does. WIP is the sell value of the client's
/// not-yet-invoiced loads — a CommodityLine's sell RateLine is excluded the moment it's
/// referenced by an InvoiceLine, so a load moves from WIP to AR one line at a time as
/// each of its commodity lines gets billed, not all-or-nothing at the load level.
///
/// Closing the concurrency gap this class used to carry (docs/architecture.html §5.4):
/// a caller now wraps its check-and-save in BeginCreditLockAsync, which takes a
/// transaction-scoped SQL Server application lock per client before GetStatusAsync
/// ever runs. A second concurrent write against the *same* client blocks until the
/// first transaction commits or rolls back, so it always reads exposure that already
/// reflects whatever the first one did — the two race-condition requests from the
/// known limitation now serialize instead of both reading stale exposure. The lock is
/// scoped per client, so different clients never contend with each other.
/// </summary>
public class CreditExposureService
{
    private static readonly LoadStatus[] WipStatuses =
    {
        LoadStatus.Booked,
        LoadStatus.Allocated,
        LoadStatus.InTransit,
        LoadStatus.Delivered,
        LoadStatus.PodReceived,
        LoadStatus.OnHold
    };

    private readonly TmsDbContext _db;

    public CreditExposureService(TmsDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Begins a transaction and acquires an exclusive, transaction-scoped SQL Server
    /// application lock for this client (docs/architecture.html §5.4) — call this
    /// before GetStatusAsync/WouldBreach and hold it open through whatever the caller
    /// then adds and saves, so the whole check-and-save is atomic per client. A second
    /// caller for the same client blocks here (up to 10s) until the first commits or
    /// rolls back. @LockOwner = 'Transaction' means the lock releases automatically
    /// when the returned transaction is committed, rolled back, or disposed without
    /// either — the caller never needs to release it explicitly.
    /// </summary>
    public async Task<IDbContextTransaction> BeginCreditLockAsync(Guid tenantId, Guid clientId, CancellationToken ct)
    {
        var transaction = await _db.Database.BeginTransactionAsync(ct);

        // sp_getapplock's return code comes back as a stored-procedure RETURN value,
        // not a result set — SqlQueryRaw/FromSql can't express that (and can't compose
        // a multi-statement batch with a LINQ operator like SingleAsync at all), so
        // this goes through plain ADO.NET on the same connection/transaction instead.
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.Transaction = transaction.GetDbTransaction();

        void AddParam(string name, object value, ParameterDirection direction = ParameterDirection.Input)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            parameter.Direction = direction;
            command.Parameters.Add(parameter);
        }

        AddParam("@Resource", $"credit:{tenantId}:{clientId}");
        AddParam("@LockMode", "Exclusive");
        AddParam("@LockOwner", "Transaction");
        AddParam("@LockTimeout", 10000);
        AddParam("@ReturnValue", 0, ParameterDirection.ReturnValue);

        await command.ExecuteNonQueryAsync(ct);

        var lockResult = (int)command.Parameters["@ReturnValue"].Value!;
        if (lockResult < 0)
        {
            await transaction.RollbackAsync(ct);
            throw new InvalidOperationException(
                $"Could not acquire the credit lock for client {clientId} (sp_getapplock returned {lockResult}).");
        }

        return transaction;
    }

    public async Task<CreditStatus> GetStatusAsync(Client client, CancellationToken ct)
    {
        var arOutstanding = await _db.Set<Invoice>()
            .Where(i => i.ClientId == client.Id && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartPaid))
            .SumAsync(i => i.TotalIncVat, ct);

        var invoicedRateLineIds = _db.Set<InvoiceLine>().Select(l => l.RateLineSellId);

        var wip = await _db.Set<RateLine>()
            .Where(r => r.Direction == RateLineDirection.Sell && r.SourceType == RateLineSourceType.CommodityLine)
            .Where(r => !invoicedRateLineIds.Contains(r.Id))
            .Join(_db.Set<CommodityLine>(), r => r.SourceId, cl => cl.Id, (r, cl) => new { r, cl })
            .Join(_db.Set<LoadLeg>(), x => x.cl.LoadLegId, leg => leg.Id, (x, leg) => new { x.r, leg })
            .Join(_db.Set<Load>(), x => x.leg.LoadId, load => load.Id, (x, load) => new { x.r, load })
            .Where(x => x.load.ClientId == client.Id && WipStatuses.Contains(x.load.Status))
            .SumAsync(x => x.r.Amount, ct);

        var totalExposure = arOutstanding + wip;

        return new CreditStatus(
            CreditLimit: client.CreditLimit,
            ArOutstanding: arOutstanding,
            Wip: wip,
            TotalExposure: totalExposure,
            AvailableCredit: client.CreditLimit - totalExposure);
    }

    /// <summary>True if adding <paramref name="additionalAmount"/> would push Total Exposure above CreditLimit (§5.4's hard stop).</summary>
    public static bool WouldBreach(CreditStatus status, decimal additionalAmount) =>
        status.TotalExposure + additionalAmount > status.CreditLimit;
}
