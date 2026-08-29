using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Loads;
using Tms.Modules.Rating;

namespace Tms.Api.Services;

public record CreditStatus(
    Guid CurrencyId,
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
/// net of Issued CreditNotes against them — a Draft credit note doesn't reduce
/// exposure yet, the same "only committed documents count" rule Invoice itself
/// follows. WIP is the sell value of the client's
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
/// scoped per client (not per client+currency), so it's slightly more conservative
/// than strictly necessary for a client using two currencies at once, but still
/// correct — different clients never contend with each other either way.
///
/// A client's exposure is now tracked per currency, never blended (§4.3): AR and WIP
/// are summed only within one CurrencyId, and CreditLimit is resolved to whichever
/// currency the caller asks about — the client's own primary CreditLimit if it's the
/// primary currency, or a ClientCurrency row's own limit for an additional one. There
/// is deliberately no FX conversion anywhere in this class — exactly the design choice
/// that keeps this hard stop exact rather than an approximation of a moving rate.
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

    /// <summary>
    /// Resolves the CreditLimit that applies to one currency for this client (§4.3) —
    /// the client's own primary CreditLimit if currencyId is its primary CurrencyId, or
    /// a ClientCurrency row's own limit for an additional allowed currency. Null means
    /// this client isn't permitted to transact in that currency at all; callers use
    /// that to reject the request before ever reaching GetStatusAsync.
    /// </summary>
    public async Task<decimal?> ResolveCreditLimitAsync(Client client, Guid currencyId, CancellationToken ct)
    {
        if (currencyId == client.CurrencyId) return client.CreditLimit;

        var allowed = await _db.Set<ClientCurrency>()
            .FirstOrDefaultAsync(cc => cc.ClientId == client.Id && cc.CurrencyId == currencyId, ct);
        return allowed?.CreditLimit;
    }

    /// <summary>
    /// Exposure for one currency only (§4.3) — a client transacting in two currencies
    /// has two entirely separate exposure figures, never summed together, since doing
    /// that would require converting one into the other and reintroducing exactly the
    /// FX-timing risk this hard stop is designed to avoid. Throws if currencyId isn't
    /// one this client is actually permitted to use — callers (LoadsController) resolve
    /// and validate that first via ResolveCreditLimitAsync, so reaching here with an
    /// unresolvable currency indicates a caller bug, not a user-facing error case.
    /// </summary>
    public async Task<CreditStatus> GetStatusAsync(Client client, Guid currencyId, CancellationToken ct)
    {
        var creditLimit = await ResolveCreditLimitAsync(client, currencyId, ct)
            ?? throw new InvalidOperationException($"Client {client.Id} is not permitted to transact in currency {currencyId}.");

        var arOutstanding = await _db.Set<Invoice>()
            .Where(i => i.ClientId == client.Id && i.CurrencyId == currencyId
                && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartPaid))
            .SumAsync(i => i.TotalIncVat, ct);

        var issuedCreditNotes = await _db.Set<CreditNote>()
            .Where(cn => cn.ClientId == client.Id && cn.CurrencyId == currencyId && cn.Status == CreditNoteStatus.Issued)
            .SumAsync(cn => cn.TotalAmount, ct);

        arOutstanding -= issuedCreditNotes;

        var invoicedRateLineIds = _db.Set<InvoiceLine>().Select(l => l.RateLineSellId);

        var wip = await _db.Set<RateLine>()
            .Where(r => r.Direction == RateLineDirection.Sell && r.SourceType == RateLineSourceType.CommodityLine && r.CurrencyId == currencyId)
            .Where(r => !invoicedRateLineIds.Contains(r.Id))
            .Join(_db.Set<CommodityLine>(), r => r.SourceId, cl => cl.Id, (r, cl) => new { r, cl })
            .Join(_db.Set<LoadLeg>(), x => x.cl.LoadLegId, leg => leg.Id, (x, leg) => new { x.r, leg })
            .Join(_db.Set<Load>(), x => x.leg.LoadId, load => load.Id, (x, load) => new { x.r, load })
            .Where(x => x.load.ClientId == client.Id && WipStatuses.Contains(x.load.Status))
            .SumAsync(x => x.r.Amount, ct);

        var totalExposure = arOutstanding + wip;

        return new CreditStatus(
            CurrencyId: currencyId,
            CreditLimit: creditLimit,
            ArOutstanding: arOutstanding,
            Wip: wip,
            TotalExposure: totalExposure,
            AvailableCredit: creditLimit - totalExposure);
    }

    /// <summary>True if adding <paramref name="additionalAmount"/> would push Total Exposure above CreditLimit (§5.4's hard stop).</summary>
    public static bool WouldBreach(CreditStatus status, decimal additionalAmount) =>
        status.TotalExposure + additionalAmount > status.CreditLimit;
}
