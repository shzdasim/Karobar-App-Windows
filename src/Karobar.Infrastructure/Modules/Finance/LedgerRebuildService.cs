using Karobar.Application.Interfaces;
using Karobar.Domain.Entities;
using Karobar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Services;

/// <summary>
/// Chronological Running-Balance Ledger Rebuild Engine matching CustomerLedgerController & SupplierLedgerController.
/// Subtracts PaymentAllocations from credit invoices to prevent double-counting when Payment Vouchers are present.
/// </summary>
public sealed class LedgerRebuildService : ILedgerRebuildService
{
    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;

    public LedgerRebuildService(IDbContextFactory<KarobarDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<int> RebuildAllCustomerLedgersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var customerIds = await db.Customers.Select(c => c.Id).ToListAsync(cancellationToken);
        var totalEntries = 0;

        foreach (var customerId in customerIds)
        {
            var entries = await db.CustomerLedgers
                .Where(l => l.CustomerId == customerId)
                .OrderBy(l => l.EntryDate)
                .ThenBy(l => l.Id)
                .ToListAsync(cancellationToken);

            decimal runningBalance = 0m;
            foreach (var entry in entries)
            {
                entry.PreviousBalance = runningBalance;

                if (entry.EntryType == "invoice")
                {
                    var billRemain = Math.Max(0m, entry.InvoiceTotal - entry.TotalReceived - entry.DeductedAmount);
                    entry.BalanceRemaining = Math.Round(billRemain, 2);
                    runningBalance = Math.Round(runningBalance + billRemain, 2);
                }
                else if (entry.EntryType == "payment")
                {
                    runningBalance = Math.Round(runningBalance - entry.CreditedAmount, 2);
                }
                else
                {
                    var delta = entry.InvoiceTotal - entry.TotalReceived - entry.CreditedAmount;
                    runningBalance = Math.Round(runningBalance + delta, 2);
                }

                entry.TotalBalance = runningBalance;
                totalEntries++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return totalEntries;
    }

    public async Task<int> RebuildAllSupplierLedgersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var supplierIds = await db.Suppliers.Select(s => s.Id).ToListAsync(cancellationToken);
        var totalEntries = 0;

        foreach (var supplierId in supplierIds)
        {
            var entries = await db.SupplierLedgers
                .Where(l => l.SupplierId == supplierId)
                .OrderBy(l => l.EntryDate)
                .ThenBy(l => l.Id)
                .ToListAsync(cancellationToken);

            decimal runningBalance = 0m;
            foreach (var entry in entries)
            {
                entry.PreviousBalance = runningBalance;

                if (entry.EntryType == "invoice")
                {
                    var billRemain = Math.Max(0m, entry.InvoiceTotal - entry.TotalPaid - entry.DebitedAmount);
                    entry.CreditRemaining = Math.Round(billRemain, 2);
                    runningBalance = Math.Round(runningBalance + billRemain, 2);
                }
                else if (entry.EntryType == "payment")
                {
                    runningBalance = Math.Round(runningBalance - entry.DebitedAmount - entry.TotalPaid, 2);
                }
                else
                {
                    var delta = entry.InvoiceTotal - entry.TotalPaid - entry.DebitedAmount;
                    runningBalance = Math.Round(runningBalance + delta, 2);
                }

                entry.TotalBalance = runningBalance;
                totalEntries++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return totalEntries;
    }
}
