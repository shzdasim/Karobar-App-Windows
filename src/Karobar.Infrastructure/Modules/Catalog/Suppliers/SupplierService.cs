using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Karobar.Application.Interfaces;
using Karobar.Domain.Entities;
using Karobar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Services;

public sealed class SupplierService : ISupplierService
{
    public const int PreviewInvalidLimit = 20;
    public const int PreviewValidLimit = 10;

    private static readonly string[] NameHeaderAliases =
    [
        "name", "supplier", "supplier_name", "company", "company_name"
    ];

    private static readonly string[] AddressHeaderAliases =
    [
        "address", "addr", "supplier_address", "location"
    ];

    private static readonly string[] PhoneHeaderAliases =
    [
        "phone", "mobile", "contact", "phone_number", "tel"
    ];

    private static readonly string[] OpeningBalanceHeaderAliases =
    [
        "opening_balance", "opening", "balance"
    ];

    private static readonly Regex NumericRegex = new(@"^[+-]?\d+(\.\d+)?$", RegexOptions.Compiled);
    private static readonly Regex BeforeMonthsFilterRegex = new(@"^before_(\d+)$", RegexOptions.Compiled);

    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;

    public SupplierService(IDbContextFactory<KarobarDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<SupplierDto>> GetAllAsync(
        string? searchQuery = null,
        string? policyFilter = "all",
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        string qName = searchQuery?.Trim() ?? string.Empty;
        string filter = string.IsNullOrWhiteSpace(policyFilter) ? "all" : policyFilter.Trim();

        var query = db.Suppliers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(qName))
        {
            query = query.Where(s =>
                EF.Functions.Like(s.Name, $"%{qName}%") ||
                (s.Phone != null && EF.Functions.Like(s.Phone, $"%{qName}%")) ||
                (s.Address != null && EF.Functions.Like(s.Address, $"%{qName}%")));
        }

        if (!string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (filter is "before_expiry" or "accepts_expired" or "non_returnable")
            {
                query = query.Where(s => s.ExpiryReturnPolicy == filter);
            }
            else
            {
                var match = BeforeMonthsFilterRegex.Match(filter);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int months))
                {
                    query = query.Where(s => s.ExpiryReturnPolicy == "before_expiry" && s.ExpiryReturnMonths == months);
                }
            }
        }

        var suppliers = await query
            .OrderByDescending(s => s.Id)
            .ToListAsync(cancellationToken);

        if (suppliers.Count == 0)
            return [];

        var supplierIds = suppliers.Select(s => s.Id).ToList();

        var productCounts = await db.Products.AsNoTracking()
            .Where(p => p.SupplierId.HasValue && supplierIds.Contains(p.SupplierId.Value))
            .GroupBy(p => p.SupplierId!.Value)
            .Select(g => new { SupplierId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Count, cancellationToken);

        // Compute net payable per supplier from supplier_ledgers
        var ledgerBalances = await db.SupplierLedgers.AsNoTracking()
            .Where(sl => supplierIds.Contains(sl.SupplierId))
            .GroupBy(sl => sl.SupplierId)
            .Select(g => new
            {
                SupplierId = g.Key,
                NetBalance = g.Sum(x => x.InvoiceTotal - x.TotalPaid - x.DebitedAmount)
            })
            .ToDictionaryAsync(x => x.SupplierId, x => x.NetBalance, cancellationToken);

        // Evaluate active batches for products belonging to these suppliers
        var activeBatchRows = await (
            from b in db.Batches.AsNoTracking()
            join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
            join br in db.Brands.AsNoTracking() on p.BrandId equals (long?)br.Id into brJoin
            from br in brJoin.DefaultIfEmpty()
            where b.Quantity > 0 && b.ExpiryDate != null && p.SupplierId.HasValue && supplierIds.Contains(p.SupplierId.Value)
            select new
            {
                SupplierId = p.SupplierId!.Value,
                b.Quantity,
                b.ExpiryDate,
                BrandPolicy = br != null ? br.ExpiryReturnPolicy : null,
                BrandMonths = br != null ? (int?)br.ExpiryReturnMonths : null,
                BrandDays = br != null ? (int?)br.ExpiryReturnDays : null
            }).ToListAsync(cancellationToken);

        var supplierMap = suppliers.ToDictionary(s => s.Id);
        var intimationStats = new Dictionary<long, (int DueCount, int DueUnits)>();

        foreach (var row in activeBatchRows)
        {
            if (!supplierMap.TryGetValue(row.SupplierId, out var sup))
                continue;

            string effPolicy = !string.IsNullOrWhiteSpace(sup.ExpiryReturnPolicy)
                ? sup.ExpiryReturnPolicy
                : (!string.IsNullOrWhiteSpace(row.BrandPolicy) ? row.BrandPolicy! : "before_expiry");
            int effMonths = !string.IsNullOrWhiteSpace(sup.ExpiryReturnPolicy)
                ? (sup.ExpiryReturnMonths ?? 3)
                : (row.BrandMonths ?? 3);
            int effDays = !string.IsNullOrWhiteSpace(sup.ExpiryReturnPolicy)
                ? (sup.ExpiryReturnDays ?? 90)
                : (row.BrandDays ?? 90);

            var eval = BrandService.EvaluateBatchIntimation(row.ExpiryDate, effPolicy, effMonths, effDays);
            if (eval.IsIntimationDue)
            {
                intimationStats.TryGetValue(row.SupplierId, out var cur);
                intimationStats[row.SupplierId] = (cur.DueCount + 1, cur.DueUnits + row.Quantity);
            }
        }

        var result = new List<SupplierDto>(suppliers.Count);
        foreach (var s in suppliers)
        {
            productCounts.TryGetValue(s.Id, out int prodCount);
            intimationStats.TryGetValue(s.Id, out var stats);
            ledgerBalances.TryGetValue(s.Id, out decimal netBalance);

            result.Add(MapToDto(s, prodCount, stats.DueCount, stats.DueUnits, Math.Max(0m, netBalance), null));
        }

        return result;
    }

    public async Task<List<SupplierDto>> SearchAsync(
        string? query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        string q = query?.Trim() ?? string.Empty;
        int safeLimit = Math.Clamp(limit, 1, 100);

        var dbQuery = db.Suppliers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(q))
        {
            dbQuery = dbQuery.Where(s =>
                EF.Functions.Like(s.Name, $"%{q}%") ||
                (s.Phone != null && EF.Functions.Like(s.Phone, $"%{q}%")));
        }

        var items = await dbQuery
            .OrderBy(s => s.Name)
            .Take(safeLimit)
            .ToListAsync(cancellationToken);

        return items.Select(s => MapToDto(s, 0, 0, 0, 0m, null)).ToList();
    }

    public async Task<SupplierDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var supplier = await db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (supplier is null)
            return null;

        int prodCount = await db.Products.AsNoTracking()
            .CountAsync(p => p.SupplierId == id, cancellationToken);

        decimal netBalance = await db.SupplierLedgers.AsNoTracking()
            .Where(sl => sl.SupplierId == id)
            .SumAsync(sl => (decimal?)(sl.InvoiceTotal - sl.TotalPaid - sl.DebitedAmount), cancellationToken) ?? 0m;

        var rawBatches = await (
            from b in db.Batches.AsNoTracking()
            join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
            join br in db.Brands.AsNoTracking() on p.BrandId equals (long?)br.Id into brJoin
            from br in brJoin.DefaultIfEmpty()
            where b.Quantity > 0 && b.ExpiryDate != null && p.SupplierId == id
            orderby b.ExpiryDate ascending
            select new
            {
                BatchId = b.Id,
                b.BatchNumber,
                b.ExpiryDate,
                b.Quantity,
                ProductId = p.Id,
                ProductName = p.Name,
                ProductCode = p.ProductCode,
                PackSize = p.PackSize,
                BrandName = br != null ? br.Name : string.Empty
            })
            .Take(150)
            .ToListAsync(cancellationToken);

        var batchDtos = new List<SupplierBatchIntimationDto>(rawBatches.Count);
        int dueCount = 0;
        int dueUnits = 0;

        foreach (var r in rawBatches)
        {
            var eval = BrandService.EvaluateBatchIntimation(
                r.ExpiryDate,
                supplier.ExpiryReturnPolicy,
                supplier.ExpiryReturnMonths ?? 3,
                supplier.ExpiryReturnDays ?? 90);

            if (eval.IsIntimationDue)
            {
                dueCount++;
                dueUnits += r.Quantity;
            }

            batchDtos.Add(new SupplierBatchIntimationDto
            {
                BatchId = r.BatchId,
                BatchNumber = r.BatchNumber ?? string.Empty,
                ExpiryDate = r.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                Quantity = r.Quantity,
                ProductId = r.ProductId,
                ProductName = r.ProductName,
                ProductCode = r.ProductCode ?? string.Empty,
                PackSize = r.PackSize,
                BrandName = r.BrandName ?? string.Empty,
                DaysRemaining = eval.DaysRemaining,
                ReturnDeadlineDate = eval.ReturnDeadlineDate,
                DaysUntilReturnDeadline = eval.DaysUntilReturnDeadline,
                IntimationStatus = eval.IntimationStatus,
                IsIntimationDue = eval.IsIntimationDue
            });
        }

        return MapToDto(supplier, prodCount, dueCount, dueUnits, Math.Max(0m, netBalance), batchDtos);
    }

    public async Task<SupplierCreditSummaryDto> GetCreditSummaryAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var supplier = await db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Supplier not found.");

        decimal netBalance = await db.SupplierLedgers.AsNoTracking()
            .Where(sl => sl.SupplierId == id)
            .SumAsync(sl => (decimal?)(sl.InvoiceTotal - sl.TotalPaid - sl.DebitedAmount), cancellationToken) ?? 0m;

        decimal currentBalance = Math.Max(0m, Math.Round(netBalance, 2));
        decimal? creditLimit = supplier.CreditLimit.HasValue ? Math.Round(supplier.CreditLimit.Value, 2) : null;
        decimal? availableCredit = creditLimit.HasValue && creditLimit.Value > 0m
            ? Math.Max(0m, Math.Round(creditLimit.Value - currentBalance, 2))
            : null;
        bool isExceeded = creditLimit.HasValue && creditLimit.Value > 0m && currentBalance > creditLimit.Value;

        var today = DateTime.Today;
        var overdueInvoices = await db.PurchaseInvoices.AsNoTracking()
            .Where(pi =>
                pi.SupplierId == id &&
                pi.Status != "reversed" &&
                pi.DueDate != null &&
                pi.DueDate < today &&
                pi.TotalAmount > pi.TotalPaid)
            .Select(pi => new
            {
                Remaining = pi.TotalAmount - pi.TotalPaid,
                pi.DueDate
            })
            .ToListAsync(cancellationToken);

        decimal totalOverdue = Math.Round(overdueInvoices.Sum(x => Math.Max(0m, x.Remaining)), 2);
        DateTime? oldestDue = overdueInvoices
            .Where(x => x.DueDate.HasValue)
            .Select(x => x.DueDate!.Value)
            .OrderBy(d => d)
            .FirstOrDefault();

        return new SupplierCreditSummaryDto
        {
            SupplierId = supplier.Id,
            Name = supplier.Name,
            Phone = supplier.Phone,
            CreditLimit = creditLimit,
            CreditDays = supplier.CreditDays,
            NetBalance = Math.Round(netBalance, 2),
            CurrentBalance = currentBalance,
            AvailableCredit = availableCredit,
            IsLimitExceeded = isExceeded,
            TotalOverdue = totalOverdue,
            OverdueInvoicesCount = overdueInvoices.Count,
            OldestDueDate = oldestDue.HasValue && overdueInvoices.Count > 0
                ? oldestDue.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : null
        };
    }

    public async Task<SupplierDto> CreateAsync(
        SupplierUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Supplier name is required.");
        if (name.Length > 255)
            throw new InvalidOperationException("Supplier name must not exceed 255 characters.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        bool exists = await db.Suppliers
            .AnyAsync(s => s.Name.ToLower() == name.ToLower(), cancellationToken);
        if (exists)
            throw new InvalidOperationException($"A supplier named '{name}' already exists.");

        var normalized = BrandService.NormalizeExpiryPolicy(
            request.ExpiryReturnPolicy,
            request.ExpiryReturnMonths,
            request.ExpiryReturnDays,
            request.ExpiryReturnNotes);

        string taxStatus = NormalizeTaxStatus(request.TaxStatus);
        var now = DateTime.UtcNow;

        var entity = new Supplier
        {
            Name = name,
            Address = NullIfWhiteSpace(request.Address),
            Phone = NullIfWhiteSpace(request.Phone),
            Email = NullIfWhiteSpace(request.Email),
            TaxNumber = NullIfWhiteSpace(request.TaxNumber),
            TaxStatus = taxStatus,
            CreditLimit = request.CreditLimit.HasValue && request.CreditLimit.Value >= 0m
                ? Math.Round(request.CreditLimit.Value, 2)
                : null,
            CreditDays = request.CreditDays.HasValue && request.CreditDays.Value >= 0
                ? request.CreditDays.Value
                : 30,
            ExpiryReturnPolicy = normalized.Policy,
            ExpiryReturnMonths = normalized.Months,
            ExpiryReturnDays = normalized.Days,
            ExpiryReturnNotes = normalized.Notes,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Suppliers.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        decimal openingBalance = request.OpeningBalance.HasValue && request.OpeningBalance.Value > 0m
            ? Math.Round(request.OpeningBalance.Value, 2)
            : 0m;

        if (openingBalance > 0m)
        {
            db.SupplierLedgers.Add(new SupplierLedger
            {
                SupplierId = entity.Id,
                PurchaseInvoiceId = null,
                EntryType = "manual",
                EntryDate = DateTime.Today,
                PostedNumber = $"OB-{entity.Id:D5}",
                InvoiceTotal = openingBalance,
                TotalPaid = 0m,
                CreditRemaining = openingBalance,
                DebitedAmount = 0m,
                TotalBalance = openingBalance,
                Description = "Opening Balance",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return MapToDto(entity, 0, 0, 0, openingBalance, []);
    }

    public async Task<SupplierDto> UpdateAsync(
        long id,
        SupplierUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Supplier name is required.");
        if (name.Length > 255)
            throw new InvalidOperationException("Supplier name must not exceed 255 characters.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Supplier not found.");

        bool duplicate = await db.Suppliers
            .AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), cancellationToken);
        if (duplicate)
            throw new InvalidOperationException($"A supplier named '{name}' already exists.");

        var normalized = BrandService.NormalizeExpiryPolicy(
            request.ExpiryReturnPolicy,
            request.ExpiryReturnMonths,
            request.ExpiryReturnDays,
            request.ExpiryReturnNotes);

        entity.Name = name;
        entity.Address = NullIfWhiteSpace(request.Address);
        entity.Phone = NullIfWhiteSpace(request.Phone);
        entity.Email = NullIfWhiteSpace(request.Email);
        entity.TaxNumber = NullIfWhiteSpace(request.TaxNumber);
        entity.TaxStatus = NormalizeTaxStatus(request.TaxStatus);
        entity.CreditLimit = request.CreditLimit.HasValue && request.CreditLimit.Value >= 0m
            ? Math.Round(request.CreditLimit.Value, 2)
            : null;
        entity.CreditDays = request.CreditDays.HasValue && request.CreditDays.Value >= 0
            ? request.CreditDays.Value
            : 30;
        entity.ExpiryReturnPolicy = normalized.Policy;
        entity.ExpiryReturnMonths = normalized.Months;
        entity.ExpiryReturnDays = normalized.Days;
        entity.ExpiryReturnNotes = normalized.Notes;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken) ?? MapToDto(entity, 0, 0, 0, 0m, []);
    }

    public async Task DeleteAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Supplier not found.");

        int productsCount = await db.Products.CountAsync(p => p.SupplierId == id, cancellationToken);
        if (productsCount > 0)
            throw new InvalidOperationException("Cannot delete: supplier is used by one or more products.");

        db.Suppliers.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ExportCsvAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidOperationException("Please provide a valid export file path.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var items = await db.Suppliers.AsNoTracking()
            .OrderByDescending(s => s.Id)
            .Select(s => new { s.Name, s.Address, s.Phone })
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("name,address,phone");

        foreach (var item in items)
        {
            sb.Append(EscapeCsvField(item.Name));
            sb.Append(',');
            sb.Append(EscapeCsvField(item.Address ?? string.Empty));
            sb.Append(',');
            sb.AppendLine(EscapeCsvField(item.Phone ?? string.Empty));
        }

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    public async Task SaveTemplateCsvAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidOperationException("Please provide a valid file path.");

        var sb = new StringBuilder();
        sb.AppendLine("name,address,phone,opening_balance");
        sb.AppendLine("ABC Pharma,Plot 12 Industrial Area,0300-1234567,50000");
        sb.AppendLine("MediCare Distributors,Main Boulevard Gulberg,0321-7654321,0");
        sb.AppendLine("Global Health Supplies,,0333-9876543,15000");

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    public async Task<SupplierCsvValidationResult> ValidateCsvAsync(
        string filePath,
        string delimiter = ",",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new InvalidOperationException("Please choose a valid CSV file first.");

        await Task.Yield();

        var parsed = ParseAndValidateCsvFile(filePath, delimiter);
        if (!parsed.Ok)
        {
            return new SupplierCsvValidationResult
            {
                Ok = false,
                Message = parsed.Message,
                UsedDelimiter = parsed.UsedDelimiter
            };
        }

        var invalidSamples = new List<SupplierCsvSampleRow>();
        var validSamples = new List<SupplierCsvSampleRow>();
        int validCount = 0;
        int invalidCount = 0;

        foreach (var row in parsed.Rows)
        {
            if (row.Errors.Count > 0)
            {
                invalidCount++;
                if (invalidSamples.Count < PreviewInvalidLimit)
                    invalidSamples.Add(row);
            }
            else
            {
                validCount++;
                if (validSamples.Count < PreviewValidLimit)
                    validSamples.Add(row);
            }
        }

        return new SupplierCsvValidationResult
        {
            Ok = true,
            UsedDelimiter = parsed.UsedDelimiter,
            Total = parsed.Rows.Count,
            Valid = validCount,
            Invalid = invalidCount,
            InvalidSamples = invalidSamples,
            ValidSamples = validSamples
        };
    }

    public async Task<SupplierCsvCommitStats> CommitCsvAsync(
        string filePath,
        bool insertValidOnly = true,
        string delimiter = ",",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new InvalidOperationException("Please choose a valid CSV file first.");

        var parsed = ParseAndValidateCsvFile(filePath, delimiter);
        if (!parsed.Ok)
            throw new InvalidOperationException(parsed.Message);

        int total = parsed.Rows.Count;
        var validRows = parsed.Rows.Where(r => r.Errors.Count == 0).ToList();
        int validCount = validRows.Count;
        int invalidCount = total - validCount;

        if (invalidCount > 0 && !insertValidOnly)
            throw new InvalidOperationException("Import aborted due to invalid rows in CSV.");

        if (validCount == 0)
        {
            return new SupplierCsvCommitStats
            {
                Total = total,
                Valid = 0,
                Invalid = invalidCount,
                Committed = 0,
                NewCount = 0,
                ExistingCount = 0
            };
        }

        // Deduplicate by supplier name (last row wins)
        var uniqueByLower = new Dictionary<string, SupplierCsvSampleRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in validRows)
        {
            uniqueByLower[r.Name.ToLowerInvariant()] = r;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existingSuppliers = await db.Suppliers.ToListAsync(cancellationToken);
        var existingMap = existingSuppliers
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        int newCount = 0;
        int existingCount = 0;
        var pendingOpeningBalances = new List<(Supplier Entity, decimal Balance)>();

        foreach (var row in uniqueByLower.Values)
        {
            if (existingMap.TryGetValue(row.Name, out var existingEntity))
            {
                existingCount++;
                existingEntity.Address = NullIfWhiteSpace(row.Address);
                existingEntity.Phone = NullIfWhiteSpace(row.Phone);
                existingEntity.UpdatedAt = now;
            }
            else
            {
                newCount++;
                var newSupplier = new Supplier
                {
                    Name = row.Name,
                    Address = NullIfWhiteSpace(row.Address),
                    Phone = NullIfWhiteSpace(row.Phone),
                    TaxStatus = "unregistered",
                    CreditDays = 30,
                    ExpiryReturnPolicy = "before_expiry",
                    ExpiryReturnMonths = 3,
                    ExpiryReturnDays = 90,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Suppliers.Add(newSupplier);
                if (row.OpeningBalance > 0m)
                {
                    pendingOpeningBalances.Add((newSupplier, Math.Round(row.OpeningBalance, 2)));
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (pendingOpeningBalances.Count > 0)
        {
            foreach (var (supEntity, balance) in pendingOpeningBalances)
            {
                db.SupplierLedgers.Add(new SupplierLedger
                {
                    SupplierId = supEntity.Id,
                    PurchaseInvoiceId = null,
                    EntryType = "manual",
                    EntryDate = DateTime.Today,
                    PostedNumber = $"OB-{supEntity.Id:D5}",
                    InvoiceTotal = balance,
                    TotalPaid = 0m,
                    CreditRemaining = balance,
                    DebitedAmount = 0m,
                    TotalBalance = balance,
                    Description = "Opening Balance (Imported)",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        return new SupplierCsvCommitStats
        {
            Total = total,
            Valid = validCount,
            Invalid = invalidCount,
            Committed = uniqueByLower.Count,
            NewCount = newCount,
            ExistingCount = existingCount
        };
    }

    private static SupplierDto MapToDto(
        Supplier s,
        int productsCount,
        int intimationDueCount,
        int intimationDueUnits,
        decimal currentPayable,
        List<SupplierBatchIntimationDto>? batches)
    {
        string policy = string.IsNullOrWhiteSpace(s.ExpiryReturnPolicy) ? "before_expiry" : s.ExpiryReturnPolicy;
        int months = s.ExpiryReturnMonths ?? 3;
        int days = s.ExpiryReturnDays ?? 90;

        return new SupplierDto
        {
            Id = s.Id,
            Name = s.Name,
            Address = s.Address,
            Phone = s.Phone,
            Email = s.Email,
            TaxNumber = s.TaxNumber,
            TaxStatus = string.IsNullOrWhiteSpace(s.TaxStatus) ? "unregistered" : s.TaxStatus!,
            CreditLimit = s.CreditLimit,
            CreditDays = s.CreditDays,
            ExpiryReturnPolicy = policy,
            ExpiryReturnMonths = months,
            ExpiryReturnDays = days,
            ExpiryReturnNotes = s.ExpiryReturnNotes,
            ExpiryPolicyLabel = BrandService.FormatExpiryPolicyLabel(policy, months, days),
            ProductsCount = productsCount,
            IntimationDueCount = intimationDueCount,
            IntimationDueUnits = intimationDueUnits,
            CurrentPayable = Math.Round(currentPayable, 2),
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
            IntimationBatches = batches ?? []
        };
    }

    private static string NormalizeTaxStatus(string? rawStatus)
    {
        string status = string.IsNullOrWhiteSpace(rawStatus) ? "unregistered" : rawStatus.Trim().ToLowerInvariant();
        return status is "registered" or "exempt" or "unregistered" ? status : "unregistered";
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (bool Ok, string Message, string UsedDelimiter, List<SupplierCsvSampleRow> Rows) ParseAndValidateCsvFile(
        string filePath,
        string requestedDelimiter)
    {
        var lines = File.ReadAllLines(filePath);
        int headerLineIdx = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var cleaned = StripBom(lines[i]);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                headerLineIdx = i;
                lines[i] = cleaned;
                break;
            }
        }

        if (headerLineIdx < 0)
            return (false, "CSV is empty or missing a header row.", ",", []);

        char delimChar = DetectDelimiter(lines[headerLineIdx], requestedDelimiter);
        string usedDelimDisplay = delimChar == '\t' ? "\\t" : delimChar.ToString();

        var rawHeaderCols = ParseCsvLine(lines[headerLineIdx], delimChar);
        var normalizedHeaders = rawHeaderCols.Select(NormalizeHeader).ToList();

        int nameIdx = FindHeaderIndex(normalizedHeaders, NameHeaderAliases);
        int addrIdx = FindHeaderIndex(normalizedHeaders, AddressHeaderAliases);
        int phoneIdx = FindHeaderIndex(normalizedHeaders, PhoneHeaderAliases);
        int obIdx = FindHeaderIndex(normalizedHeaders, OpeningBalanceHeaderAliases);

        if (nameIdx < 0)
        {
            return (
                false,
                "Missing required column: \"name\" (accepted aliases: name, supplier, supplier_name, company, company_name).",
                usedDelimDisplay,
                []);
        }

        var rows = new List<SupplierCsvSampleRow>();
        var seenLower = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int rowNum = 1;

        for (int i = headerLineIdx + 1; i < lines.Length; i++)
        {
            rowNum++;
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var cols = ParseCsvLine(line, delimChar);
            if (cols.All(string.IsNullOrWhiteSpace))
                continue;

            string rawName = nameIdx < cols.Count ? cols[nameIdx] : string.Empty;
            string rawAddr = addrIdx >= 0 && addrIdx < cols.Count ? cols[addrIdx] : string.Empty;
            string rawPhone = phoneIdx >= 0 && phoneIdx < cols.Count ? cols[phoneIdx] : string.Empty;
            string rawOb = obIdx >= 0 && obIdx < cols.Count ? cols[obIdx] : string.Empty;

            string name = CollapseSpaces(rawName);
            string address = CollapseSpaces(rawAddr);
            string phone = CollapseSpaces(rawPhone);

            decimal openingBalance = 0m;
            if (!string.IsNullOrWhiteSpace(rawOb))
            {
                string cleanedOb = rawOb.Replace(",", string.Empty).Trim();
                decimal.TryParse(cleanedOb, NumberStyles.Any, CultureInfo.InvariantCulture, out openingBalance);
            }

            var errors = new List<string>();
            var warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("Name is required.");
            }
            else
            {
                if (name.Length > 255)
                    errors.Add("Name exceeds 255 characters.");
                if (NumericRegex.IsMatch(name))
                    warnings.Add("Name looks numeric-only; verify this is intended.");
            }

            if (!string.IsNullOrWhiteSpace(address) && address.Length > 255)
                errors.Add("Address exceeds 255 characters.");

            if (!string.IsNullOrWhiteSpace(phone) && phone.Length > 255)
                errors.Add("Phone exceeds 255 characters.");

            if (openingBalance < 0m)
                errors.Add("Opening balance cannot be negative.");

            if (!string.IsNullOrWhiteSpace(name))
            {
                string key = name.ToLowerInvariant();
                if (seenLower.TryGetValue(key, out int firstRow))
                {
                    warnings.Add($"Duplicate in file (first seen at row {firstRow}) — last occurrence will be used.");
                }
                else
                {
                    seenLower[key] = rowNum;
                }
            }

            rows.Add(new SupplierCsvSampleRow
            {
                Row = rowNum,
                Name = name,
                Address = address,
                Phone = phone,
                OpeningBalance = Math.Max(0m, openingBalance),
                Errors = errors,
                Warnings = warnings
            });
        }

        return (true, string.Empty, usedDelimDisplay, rows);
    }

    private static char DetectDelimiter(string headerLine, string requestedDelimiter)
    {
        if (!string.IsNullOrEmpty(requestedDelimiter) && requestedDelimiter != "auto")
        {
            if (requestedDelimiter is "\\t" or "\t" or "tab")
                return '\t';
            return requestedDelimiter[0];
        }

        char[] candidates = [',', ';', '\t', '|'];
        char best = ',';
        int bestCount = -1;

        foreach (char c in candidates)
        {
            var parts = ParseCsvLine(headerLine, c);
            if (parts.Count > bestCount)
            {
                bestCount = parts.Count;
                best = c;
            }
        }

        return best;
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private static string StripBom(string value)
        => string.IsNullOrEmpty(value) ? string.Empty : value.TrimStart('\uFEFF');

    private static string CollapseSpaces(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        string s = StripBom(value).Trim();
        return Regex.Replace(s, @"\s+", " ");
    }

    private static string NormalizeHeader(string header)
    {
        string s = CollapseSpaces(header).ToLowerInvariant();
        return s.Replace("-", "_").Replace(" ", "_");
    }

    private static int FindHeaderIndex(List<string> headers, string[] aliases)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            if (aliases.Contains(headers[i], StringComparer.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }
}
