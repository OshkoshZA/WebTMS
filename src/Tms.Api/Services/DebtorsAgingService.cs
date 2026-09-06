using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Billing;
using Tms.Modules.Loads;

namespace Tms.Api.Services;

public record ClientAgingBuckets(decimal CurrentAmount, decimal Days30, decimal Days60, decimal Days90, decimal Days90Plus)
{
    public decimal TotalOutstanding => CurrentAmount + Days30 + Days60 + Days90 + Days90Plus;
}

public record CurrencyAgingBuckets(Guid CurrencyId, decimal CurrentAmount, decimal Days30, decimal Days60, decimal Days90, decimal Days90Plus)
{
    public decimal TotalOutstanding => CurrentAmount + Days30 + Days60 + Days90 + Days90Plus;
}

/// <summary>
/// Real aged-debtors buckets computed live from actual invoice data (docs/architecture.html
/// §10.3) — used both by FinancialPeriodsController.Close (writing a real
/// DebtorsAgingSnapshot instead of the previous roll-forward-only placeholder) and by a
/// live view (DashboardController, ClientsController) that doesn't wait for the next
/// period close. Current is not-yet-due (or due today); Days30/60/90 are 1-30/31-60/
/// 61-90 days overdue against the invoice's own DueDate as of the given date; Days90Plus
/// is beyond that — the same bucket boundaries the doc's own roll-forward table already
/// named, just computed from real data instead of shifted from whatever the prior
/// snapshot happened to hold.
/// </summary>
public class DebtorsAgingService
{
    private readonly TmsDbContext _db;

    public DebtorsAgingService(TmsDbContext db)
    {
        _db = db;
    }

    public async Task<ClientAgingBuckets> ComputeForClientAsync(Guid clientId, DateOnly asOf, CancellationToken ct)
    {
        var byClient = await ComputeForClientsAsync(new[] { clientId }, asOf, ct);
        return byClient.GetValueOrDefault(clientId) ?? new ClientAgingBuckets(0, 0, 0, 0, 0);
    }

    /// <summary>
    /// One bulk round trip regardless of how many clients are asked for, not one query
    /// per client — FinancialPeriodsController.Close calls this for every Client in the
    /// company at once, the same "thousands of clients" performance concern its own
    /// comment already documents for the snapshot write itself. Only counts a Client's
    /// invoices in its own primary CurrencyId — DebtorsAgingSnapshot has no CurrencyId
    /// column of its own (one row per Client per Period, predating any currency-aware
    /// design in this specific corner), so an invoice raised in one of a Client's
    /// *additional* allowed currencies (§4.3) doesn't appear here; DashboardController's
    /// own company-wide summary has no such limitation (see
    /// ComputeCompanyWideByCurrencyAsync below). A CreditNote correcting a specific
    /// Invoice nets against that invoice's own bucket; a standalone CreditNote (no
    /// OriginalInvoiceId) has no invoice of its own to net against, so it reduces that
    /// Client's Current bucket instead — matching how CreditExposureService's own AR
    /// Outstanding figure nets every Issued CreditNote against the total the same way,
    /// just bucketed here rather than left as one lump sum.
    /// </summary>
    public async Task<Dictionary<Guid, ClientAgingBuckets>> ComputeForClientsAsync(
        IReadOnlyCollection<Guid> clientIds, DateOnly asOf, CancellationToken ct)
    {
        var clientCurrencies = await _db.Clients
            .Where(c => clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.CurrencyId })
            .ToDictionaryAsync(c => c.Id, c => c.CurrencyId, ct);

        var invoices = await _db.Invoices
            .Where(i => clientIds.Contains(i.ClientId) && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartPaid))
            .Select(i => new { i.Id, i.ClientId, i.CurrencyId, i.DueDate, i.TotalIncVat })
            .ToListAsync(ct);

        var creditsByInvoiceId = await _db.CreditNotes
            .Where(cn => clientIds.Contains(cn.ClientId) && cn.Status == CreditNoteStatus.Issued && cn.OriginalInvoiceId != null)
            .GroupBy(cn => cn.OriginalInvoiceId!.Value)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(cn => cn.TotalAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Total, ct);

        var standaloneCreditsByClient = await _db.CreditNotes
            .Where(cn => clientIds.Contains(cn.ClientId) && cn.Status == CreditNoteStatus.Issued && cn.OriginalInvoiceId == null)
            .GroupBy(cn => cn.ClientId)
            .Select(g => new { ClientId = g.Key, Total = g.Sum(cn => cn.TotalAmount) })
            .ToDictionaryAsync(x => x.ClientId, x => x.Total, ct);

        var totals = clientIds.ToDictionary(id => id, _ => (Current: 0m, D30: 0m, D60: 0m, D90: 0m, D90Plus: 0m));

        foreach (var invoice in invoices)
        {
            if (!clientCurrencies.TryGetValue(invoice.ClientId, out var primaryCurrencyId) || invoice.CurrencyId != primaryCurrencyId)
                continue;

            var net = invoice.TotalIncVat - creditsByInvoiceId.GetValueOrDefault(invoice.Id);
            var daysOverdue = asOf.DayNumber - invoice.DueDate.DayNumber;
            var bucket = totals[invoice.ClientId];
            if (daysOverdue <= 0) bucket.Current += net;
            else if (daysOverdue <= 30) bucket.D30 += net;
            else if (daysOverdue <= 60) bucket.D60 += net;
            else if (daysOverdue <= 90) bucket.D90 += net;
            else bucket.D90Plus += net;
            totals[invoice.ClientId] = bucket;
        }

        foreach (var (clientId, standaloneCredit) in standaloneCreditsByClient)
        {
            var bucket = totals[clientId];
            bucket.Current -= standaloneCredit;
            totals[clientId] = bucket;
        }

        return totals.ToDictionary(x => x.Key, x => new ClientAgingBuckets(x.Value.Current, x.Value.D30, x.Value.D60, x.Value.D90, x.Value.D90Plus));
    }

    /// <summary>
    /// Company-wide buckets grouped by currency, never blended across one (§4.3) — every
    /// Issued/PartPaid invoice counts here regardless of whether its currency is a
    /// Client's primary or one of its additional allowed ones, unlike
    /// ComputeForClientsAsync's own primary-currency-only limitation, since there's no
    /// per-client snapshot row shape to stay compatible with here. Standalone CreditNotes
    /// reduce the Current bucket for their own CurrencyId the same way.
    /// </summary>
    public async Task<IReadOnlyList<CurrencyAgingBuckets>> ComputeCompanyWideByCurrencyAsync(DateOnly asOf, CancellationToken ct)
    {
        var invoices = await _db.Invoices
            .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartPaid)
            .Select(i => new { i.Id, i.CurrencyId, i.DueDate, i.TotalIncVat })
            .ToListAsync(ct);

        var creditsByInvoiceId = await _db.CreditNotes
            .Where(cn => cn.Status == CreditNoteStatus.Issued && cn.OriginalInvoiceId != null)
            .GroupBy(cn => cn.OriginalInvoiceId!.Value)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(cn => cn.TotalAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Total, ct);

        var standaloneCreditsByCurrency = await _db.CreditNotes
            .Where(cn => cn.Status == CreditNoteStatus.Issued && cn.OriginalInvoiceId == null)
            .GroupBy(cn => cn.CurrencyId)
            .Select(g => new { CurrencyId = g.Key, Total = g.Sum(cn => cn.TotalAmount) })
            .ToDictionaryAsync(x => x.CurrencyId, x => x.Total, ct);

        var totals = new Dictionary<Guid, (decimal Current, decimal D30, decimal D60, decimal D90, decimal D90Plus)>();

        foreach (var invoice in invoices)
        {
            var net = invoice.TotalIncVat - creditsByInvoiceId.GetValueOrDefault(invoice.Id);
            var daysOverdue = asOf.DayNumber - invoice.DueDate.DayNumber;
            var bucket = totals.GetValueOrDefault(invoice.CurrencyId);
            if (daysOverdue <= 0) bucket.Current += net;
            else if (daysOverdue <= 30) bucket.D30 += net;
            else if (daysOverdue <= 60) bucket.D60 += net;
            else if (daysOverdue <= 90) bucket.D90 += net;
            else bucket.D90Plus += net;
            totals[invoice.CurrencyId] = bucket;
        }

        foreach (var (currencyId, standaloneCredit) in standaloneCreditsByCurrency)
        {
            var bucket = totals.GetValueOrDefault(currencyId);
            bucket.Current -= standaloneCredit;
            totals[currencyId] = bucket;
        }

        return totals
            .Select(x => new CurrencyAgingBuckets(x.Key, x.Value.Current, x.Value.D30, x.Value.D60, x.Value.D90, x.Value.D90Plus))
            .ToList();
    }
}
