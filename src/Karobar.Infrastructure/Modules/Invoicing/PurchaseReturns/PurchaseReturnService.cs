using System.Text.Json;
using Karobar.Application.Interfaces;
using Karobar.Domain.Entities;
using Karobar.Domain.Formulas;
using Karobar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Services;

public sealed class PurchaseReturnService : IPurchaseReturnService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;
    private readonly IAuthService _authService;

    public PurchaseReturnService(
        IDbContextFactory<KarobarDbContext> dbFactory,
        IAuthService authService)
    {
        _dbFactory = dbFactory;
        _authService = authService;
    }

    // =========================================================================
    // 1. DIRECTORY, BOOTSTRAP & LOOKUPS
    // =========================================================================

    public async Task<PurchaseReturnDirectoryPageDto> GetDirectoryPageAsync(
        PurchaseReturnDirectoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 10, 200);

        var query = db.PurchaseReturns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.PostedNumber))
        {
            var pTerm = filter.PostedNumber.Trim().ToLowerInvariant();
            query = query.Where(r => r.PostedNumber.ToLower().Contains(pTerm));
        }

        if (filter.SupplierId is > 0)
        {
            var supId = filter.SupplierId.Value;
            query = query.Where(r => r.SupplierId == supId);
        }
        else if (!string.IsNullOrWhiteSpace(filter.SupplierSearch))
        {
            var sTerm = filter.SupplierSearch.Trim().ToLowerInvariant();
            var matchingSupplierIds = await db.Suppliers.AsNoTracking()
                .Where(s => s.Name.ToLower().Contains(sTerm))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            query = query.Where(r => matchingSupplierIds.Contains(r.SupplierId));
        }

        if (!string.IsNullOrWhiteSpace(filter.SettlementMode) && filter.SettlementMode != "all")
        {
            var mode = filter.SettlementMode.Trim().ToLowerInvariant();
            query = query.Where(r => r.SettlementMode != null && r.SettlementMode.ToLower() == mode);
        }

        if (filter.DateFrom.HasValue)
        {
            var fromDate = filter.DateFrom.Value.Date;
            query = query.Where(r => r.Date >= fromDate);
        }

        if (filter.DateTo.HasValue)
        {
            var toDate = filter.DateTo.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(r => r.Date <= toDate);
        }

        var allMatching = await query
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);

        var totalCount = allMatching.Count;
        var totalGross = allMatching.Sum(r => r.GrossTotal);
        var totalNet = allMatching.Sum(r => r.Total);
        var totalCashRefund = allMatching
            .Where(r => string.Equals(r.SettlementMode ?? "cash_refund", "cash_refund", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Total);
        var totalLedgerCredit = allMatching
            .Where(r => !string.Equals(r.SettlementMode ?? "cash_refund", "cash_refund", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Total);

        var pageSlice = allMatching
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var supplierIds = pageSlice.Select(r => r.SupplierId).Distinct().ToList();
        var supplierMap = supplierIds.Count > 0
            ? await db.Suppliers.AsNoTracking()
                .Where(s => supplierIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken)
            : [];

        var invoiceIds = pageSlice
            .Where(r => r.PurchaseInvoiceId.HasValue)
            .Select(r => r.PurchaseInvoiceId!.Value)
            .Distinct()
            .ToList();

        var invoiceMap = invoiceIds.Count > 0
            ? await db.PurchaseInvoices.AsNoTracking()
                .Where(pi => invoiceIds.Contains(pi.Id))
                .ToDictionaryAsync(pi => pi.Id, pi => (pi.PostedNumber, pi.InvoiceNumber), cancellationToken)
            : [];

        var accountIds = pageSlice
            .Where(r => r.AccountId.HasValue)
            .Select(r => r.AccountId!.Value)
            .Distinct()
            .ToList();

        var accountMap = accountIds.Count > 0
            ? await db.Accounts.AsNoTracking()
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken)
            : [];

        var returnIds = pageSlice.Select(r => r.Id).ToList();
        var itemStats = returnIds.Count > 0
            ? await db.PurchaseReturnItems.AsNoTracking()
                .Where(i => returnIds.Contains(i.PurchaseReturnId))
                .GroupBy(i => i.PurchaseReturnId)
                .Select(g => new
                {
                    ReturnId = g.Key,
                    Count = g.Count(),
                    Units = g.Sum(x => x.ReturnUnitQuantity)
                })
                .ToDictionaryAsync(x => x.ReturnId, cancellationToken)
            : [];

        var rows = pageSlice.Select((r, idx) =>
        {
            string? invPosted = null;
            string? invNo = null;
            if (r.PurchaseInvoiceId.HasValue && invoiceMap.TryGetValue(r.PurchaseInvoiceId.Value, out var invTuple))
            {
                invPosted = invTuple.PostedNumber;
                invNo = invTuple.InvoiceNumber;
            }

            var hasStats = itemStats.TryGetValue(r.Id, out var st);

            return new PurchaseReturnDirectoryRowDto
            {
                RowIndex = ((page - 1) * pageSize) + idx + 1,
                Id = r.Id,
                PostedNumber = r.PostedNumber,
                Date = r.Date,
                SupplierId = r.SupplierId,
                SupplierName = supplierMap.TryGetValue(r.SupplierId, out var sName) ? sName : $"Supplier #{r.SupplierId}",
                PurchaseInvoiceId = r.PurchaseInvoiceId,
                PurchaseInvoicePostedNumber = invPosted,
                PurchaseInvoiceNumber = invNo,
                SettlementMode = NormalizeSettlementMode(r.SettlementMode),
                PaymentMethod = r.PaymentMethod ?? "cash",
                AccountId = r.AccountId,
                AccountName = r.AccountId.HasValue && accountMap.TryGetValue(r.AccountId.Value, out var accName) ? accName : null,
                GrossTotal = r.GrossTotal,
                DiscountPercentage = r.DiscountPercentage,
                DiscountAmount = r.DiscountAmount,
                TaxPercentage = r.TaxPercentage,
                TaxAmount = r.TaxAmount,
                Total = r.Total,
                CreditedAmount = r.CreditedAmount,
                ItemsCount = hasStats ? st!.Count : 0,
                TotalReturnUnits = hasStats ? st!.Units : 0,
                Remarks = r.Remarks,
                CreatedAt = r.CreatedAt
            };
        }).ToList();

        return new PurchaseReturnDirectoryPageDto
        {
            Items = rows,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalReturnsCount = totalCount,
            TotalGrossAmount = Math.Round(totalGross, 2),
            TotalNetAmount = Math.Round(totalNet, 2),
            TotalCashRefundAmount = Math.Round(totalCashRefund, 2),
            TotalLedgerCreditAmount = Math.Round(totalLedgerCredit, 2)
        };
    }

    public async Task<PurchaseReturnFormBootstrapDto> GetFormBootstrapAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var nextCode = await ComputeNextPostedNumberInternalAsync(db, cancellationToken);
        var suppliers = await BuildSupplierLookupsAsync(db, cancellationToken);
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Name)
            .Select(a => new PurchaseAccountLookupDto
            {
                Id = a.Id,
                Name = a.Name,
                Code = a.Code,
                AccountType = a.AccountType,
                CurrentBalance = a.CurrentBalance,
                IsDefault = a.IsDefault,
                SyncWithCashDrawer = a.SyncWithCashDrawer
            })
            .ToListAsync(cancellationToken);

        var defaultCashAcc = accounts.FirstOrDefault(a => a.AccountType == "cash_drawer" || a.SyncWithCashDrawer)
            ?? accounts.FirstOrDefault(a => a.IsDefault)
            ?? accounts.FirstOrDefault();

        var openSession = await db.CashDrawerSessions.AsNoTracking()
            .Where(s => s.Status == "open")
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        return new PurchaseReturnFormBootstrapDto
        {
            NextPostedNumberPreview = nextCode,
            Suppliers = suppliers,
            Accounts = accounts,
            DefaultCashAccountId = defaultCashAcc?.Id,
            HasOpenCashDrawerSession = openSession != null,
            ActiveDrawerSessionOpenedAt = openSession?.OpenedAt,
            CurrencySymbol = setting?.CurrencySymbol ?? "Rs.",
            StoreName = setting?.StoreName ?? "Karobar App",
            StoreAddress = setting?.Address ?? string.Empty,
            StorePhone = setting?.PhoneNumber ?? string.Empty
        };
    }

    public async Task<string> GenerateNextPostedNumberAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await ComputeNextPostedNumberInternalAsync(db, cancellationToken);
    }

    public async Task<PurchaseReturnDetailDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var pr = await db.PurchaseReturns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (pr == null)
            return null;

        var supplier = await db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == pr.SupplierId, cancellationToken);

        PurchaseInvoice? linkedInv = null;
        if (pr.PurchaseInvoiceId.HasValue)
        {
            var invId = pr.PurchaseInvoiceId.Value;
            linkedInv = await db.PurchaseInvoices.AsNoTracking()
                .FirstOrDefaultAsync(pi => pi.Id == invId, cancellationToken);
        }

        Account? account = null;
        if (pr.AccountId.HasValue)
        {
            var accId = pr.AccountId.Value;
            account = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == accId, cancellationToken);
        }

        var items = await db.PurchaseReturnItems.AsNoTracking()
            .Where(i => i.PurchaseReturnId == pr.Id)
            .OrderBy(i => i.Id)
            .ToListAsync(cancellationToken);

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var products = productIds.Count > 0
            ? await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken)
            : [];

        // Compute current available units for each line item (including this return's own units when editing)
        var invoiceReturnables = pr.PurchaseInvoiceId.HasValue
            ? await GetInvoiceReturnableItemsInternalAsync(db, pr.PurchaseInvoiceId.Value, pr.Id, cancellationToken)
            : [];

        var lineDtos = new List<PurchaseReturnLineItemDto>(items.Count);
        for (var idx = 0; idx < items.Count; idx++)
        {
            var it = items[idx];
            products.TryGetValue(it.ProductId, out var prod);
            var cleanBatch = (it.Batch ?? string.Empty).Trim();

            int availUnits;
            if (pr.PurchaseInvoiceId.HasValue)
            {
                var matchInv = invoiceReturnables.FirstOrDefault(x =>
                    x.ProductId == it.ProductId &&
                    string.Equals((x.Batch ?? string.Empty).Trim(), cleanBatch, StringComparison.OrdinalIgnoreCase));
                availUnits = matchInv?.ReturnableUnits
                    ?? invoiceReturnables.Where(x => x.ProductId == it.ProductId).Sum(x => x.ReturnableUnits);
            }
            else
            {
                var batchOpts = await GetProductBatchesInternalAsync(db, it.ProductId, null, pr.Id, cancellationToken);
                if (string.IsNullOrEmpty(cleanBatch))
                {
                    var unb = batchOpts.FirstOrDefault(b => b.IsUnbatched);
                    availUnits = unb?.AvailableUnits ?? ((prod?.Quantity ?? 0) + it.ReturnUnitQuantity);
                }
                else
                {
                    var bMatch = batchOpts.FirstOrDefault(b =>
                        !b.IsUnbatched &&
                        string.Equals(b.BatchNumber.Trim(), cleanBatch, StringComparison.OrdinalIgnoreCase));
                    availUnits = bMatch?.AvailableUnits ?? it.ReturnUnitQuantity;
                }
            }

            lineDtos.Add(new PurchaseReturnLineItemDto
            {
                Id = it.Id,
                RowNumber = idx + 1,
                ProductId = it.ProductId,
                ProductCode = prod?.ProductCode ?? string.Empty,
                ProductName = prod?.Name ?? $"Product #{it.ProductId}",
                Batch = cleanBatch,
                Expiry = it.Expiry,
                PackSize = it.PackSize > 0 ? it.PackSize : (prod?.PackSize > 0 ? prod.PackSize : 1),
                PackPurchasedQuantity = it.PackPurchasedQuantity,
                AvailableUnits = availUnits,
                ReturnPackQuantity = it.ReturnPackQuantity,
                ReturnUnitQuantity = it.ReturnUnitQuantity,
                PackPurchasePrice = it.PackPurchasePrice,
                UnitPurchasePrice = it.UnitPurchasePrice,
                ItemDiscountPercentage = it.ItemDiscountPercentage,
                SubTotal = it.SubTotal
            });
        }

        var voucherCode = $"DN-{pr.PostedNumber}";
        var hasVoucher = await db.Payments.AsNoTracking()
            .AnyAsync(p => p.VoucherNumber == voucherCode, cancellationToken);

        var latestLedger = await db.SupplierLedgers.AsNoTracking()
            .Where(l => l.SupplierId == pr.SupplierId)
            .OrderByDescending(l => l.EntryDate)
            .ThenByDescending(l => l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new PurchaseReturnDetailDto
        {
            Id = pr.Id,
            PostedNumber = pr.PostedNumber,
            Date = pr.Date,
            SupplierId = pr.SupplierId,
            SupplierName = supplier?.Name ?? $"Supplier #{pr.SupplierId}",
            SupplierPhone = supplier?.Phone,
            SupplierAddress = supplier?.Address,
            PurchaseInvoiceId = pr.PurchaseInvoiceId,
            PurchaseInvoicePostedNumber = linkedInv?.PostedNumber,
            PurchaseInvoiceNumber = linkedInv?.InvoiceNumber,
            PurchaseInvoiceType = linkedInv?.InvoiceType,
            Remarks = pr.Remarks,
            GrossTotal = pr.GrossTotal,
            DiscountPercentage = pr.DiscountPercentage,
            DiscountAmount = pr.DiscountAmount,
            TaxPercentage = pr.TaxPercentage,
            TaxAmount = pr.TaxAmount,
            Total = pr.Total,
            SettlementMode = NormalizeSettlementMode(pr.SettlementMode),
            PaymentMethod = pr.PaymentMethod ?? "cash",
            AccountId = pr.AccountId,
            AccountName = account?.Name,
            CreditedAmount = pr.CreditedAmount,
            VoucherNumber = hasVoucher ? voucherCode : null,
            SupplierBalanceAfter = latestLedger?.TotalBalance ?? 0m,
            CreatedAt = pr.CreatedAt,
            UpdatedAt = pr.UpdatedAt,
            Items = lineDtos
        };
    }

    public async Task<List<PurchaseReturnInvoiceLookupDto>> SearchSupplierInvoicesAsync(
        long? supplierId = null,
        string? search = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var safeLimit = Math.Clamp(limit, 1, 200);
        var query = db.PurchaseInvoices.AsNoTracking()
            .Where(pi => pi.Status == null || pi.Status != "reversed");

        if (supplierId is > 0)
        {
            var supId = supplierId.Value;
            query = query.Where(pi => pi.SupplierId == supId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            var matchingSupplierIds = await db.Suppliers.AsNoTracking()
                .Where(s => s.Name.ToLower().Contains(term))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            query = query.Where(pi =>
                pi.PostedNumber.ToLower().Contains(term) ||
                (pi.InvoiceNumber != null && pi.InvoiceNumber.ToLower().Contains(term)) ||
                matchingSupplierIds.Contains(pi.SupplierId));
        }

        var invoices = await query
            .OrderByDescending(pi => pi.PostedDate)
            .ThenByDescending(pi => pi.Id)
            .Take(safeLimit)
            .ToListAsync(cancellationToken);

        var supIds = invoices.Select(i => i.SupplierId).Distinct().ToList();
        var supMap = supIds.Count > 0
            ? await db.Suppliers.AsNoTracking()
                .Where(s => supIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken)
            : [];

        var invIds = invoices.Select(i => i.Id).ToList();
        var itemCounts = invIds.Count > 0
            ? await db.PurchaseInvoiceItems.AsNoTracking()
                .Where(ii => invIds.Contains(ii.PurchaseInvoiceId))
                .GroupBy(ii => ii.PurchaseInvoiceId)
                .Select(g => new { InvoiceId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.Count, cancellationToken)
            : [];

        return invoices.Select(inv =>
        {
            var isCredit = string.Equals(inv.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
            var paid = !isCredit && inv.TotalPaid <= 0m ? inv.TotalAmount : inv.TotalPaid;
            var remaining = isCredit ? Math.Max(0m, Math.Round(inv.TotalAmount - inv.TotalPaid, 2)) : 0m;

            return new PurchaseReturnInvoiceLookupDto
            {
                Id = inv.Id,
                PostedNumber = inv.PostedNumber,
                InvoiceNumber = inv.InvoiceNumber ?? string.Empty,
                PostedDate = inv.PostedDate,
                SupplierId = inv.SupplierId,
                SupplierName = supMap.TryGetValue(inv.SupplierId, out var sName) ? sName : $"Supplier #{inv.SupplierId}",
                InvoiceType = string.IsNullOrWhiteSpace(inv.InvoiceType) ? "debit" : inv.InvoiceType,
                TotalAmount = inv.TotalAmount,
                TotalPaid = paid,
                RemainingAmount = remaining,
                Remarks = inv.Remarks,
                ItemsCount = itemCounts.TryGetValue(inv.Id, out var ic) ? ic : 0
            };
        }).ToList();
    }

    public async Task<List<PurchaseReturnInvoiceItemOptionDto>> GetInvoiceReturnableItemsAsync(
        long purchaseInvoiceId,
        long? excludePurchaseReturnId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await GetInvoiceReturnableItemsInternalAsync(db, purchaseInvoiceId, excludePurchaseReturnId, cancellationToken);
    }

    public async Task<List<PurchaseReturnProductSearchDto>> SearchProductsForReturnAsync(
        string? search = null,
        long? supplierId = null,
        long? purchaseInvoiceId = null,
        long? excludePurchaseReturnId = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var safeLimit = Math.Clamp(limit, 1, 300);

        // If linked to a specific Purchase Invoice, restrict product list to products on that invoice!
        if (purchaseInvoiceId is > 0)
        {
            var invItems = await GetInvoiceReturnableItemsInternalAsync(
                db,
                purchaseInvoiceId.Value,
                excludePurchaseReturnId,
                cancellationToken);

            var invProductIds = invItems.Select(x => x.ProductId).Distinct().ToList();
            if (invProductIds.Count == 0)
                return [];

            var invProductsQuery = db.Products.AsNoTracking()
                .Where(p => invProductIds.Contains(p.Id));

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLowerInvariant();
                invProductsQuery = invProductsQuery.Where(p =>
                    (p.Name != null && p.Name.ToLower().Contains(term)) ||
                    (p.ProductCode != null && p.ProductCode.ToLower().Contains(term)) ||
                    (p.Barcode != null && p.Barcode.ToLower().Contains(term)) ||
                    (p.Formulation != null && p.Formulation.ToLower().Contains(term)));
            }

            var invProducts = await invProductsQuery
                .OrderBy(p => p.Name)
                .ThenBy(p => p.Id)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);

            var returnableByPid = invItems
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.ReturnableUnits));

            var priceByPid = invItems
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.First());

            return await BuildProductSearchDtosAsync(db, invProducts, returnableByPid, priceByPid, cancellationToken);
        }

        // Open Return mode: search catalog products (StartsWith on Name or exact/contains on Code/Barcode)
        List<Product> products;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var termLower = term.ToLowerInvariant();
            products = await db.Products.AsNoTracking()
                .Where(p =>
                    (p.Name != null && p.Name.ToLower().StartsWith(termLower)) ||
                    (p.ProductCode != null && p.ProductCode.ToLower().Contains(termLower)) ||
                    (p.Barcode != null && p.Barcode.ToLower() == termLower))
                .OrderBy(p => p.Name)
                .ThenBy(p => p.Id)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);
        }
        else
        {
            products = await db.Products.AsNoTracking()
                .OrderBy(p => p.Name)
                .ThenBy(p => p.Id)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);
        }

        return await BuildProductSearchDtosAsync(db, products, null, null, cancellationToken);
    }

    public async Task<List<PurchaseReturnBatchOptionDto>> GetProductBatchesForReturnAsync(
        long productId,
        long? purchaseInvoiceId = null,
        long? excludePurchaseReturnId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await GetProductBatchesInternalAsync(db, productId, purchaseInvoiceId, excludePurchaseReturnId, cancellationToken);
    }

    public async Task<PurchaseReturnPreShiftCheckResult> CheckPreShiftCashEditImpactAsync(
        long returnId,
        string? newSettlementMode,
        string? newPaymentMethod,
        decimal newTotal,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var orig = await db.PurchaseReturns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);
        if (orig == null)
            return new PurchaseReturnPreShiftCheckResult { RequiresPrompt = false };

        var openSession = await db.CashDrawerSessions.AsNoTracking()
            .Where(s => s.Status == "open")
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (openSession == null)
            return new PurchaseReturnPreShiftCheckResult { RequiresPrompt = false };

        var origTime = orig.CreatedAt ?? orig.Date;
        if (origTime >= openSession.OpenedAt)
            return new PurchaseReturnPreShiftCheckResult { RequiresPrompt = false };

        var origMode = NormalizeSettlementMode(orig.SettlementMode);
        var origMethod = NormalizePaymentMethod(orig.PaymentMethod);
        var origIsCashDrawer = origMode == "cash_refund" && origMethod == "cash";

        var nextMode = NormalizeSettlementMode(newSettlementMode);
        var nextMethod = NormalizePaymentMethod(newPaymentMethod);
        var nextIsCashDrawer = nextMode == "cash_refund" && nextMethod == "cash";

        var oldCash = origIsCashDrawer ? Math.Round(orig.Total, 2) : 0m;
        var newCash = nextIsCashDrawer ? Math.Round(newTotal, 2) : 0m;
        var delta = Math.Round(newCash - oldCash, 2);

        if (Math.Abs(delta) <= 0.009m)
            return new PurchaseReturnPreShiftCheckResult { RequiresPrompt = false };

        return new PurchaseReturnPreShiftCheckResult
        {
            RequiresPrompt = true,
            OldCashAmount = oldCash,
            NewCashAmount = newCash,
            CashDelta = delta,
            ReturnCreatedAt = origTime,
            ShiftOpenedAt = openSession.OpenedAt
        };
    }

    // =========================================================================
    // 2. CREATE, UPDATE & DELETE (FULL ACID + ENTITLEMENT + SETTLEMENT)
    // =========================================================================

    public async Task<PurchaseReturnOperationResult> CreateAsync(
        PurchaseReturnUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var validation = await ValidateAndCalculatePurchaseReturnAsync(
                db,
                request,
                existingReturnId: null,
                cancellationToken);

            if (!validation.IsValid)
                return validation.FailureResult!;

            var now = DateTime.UtcNow;
            var postedNumber = await ComputeNextPostedNumberInternalAsync(db, cancellationToken);

            var pr = new PurchaseReturn
            {
                SupplierId = validation.Supplier!.Id,
                PurchaseInvoiceId = validation.LinkedInvoice?.Id,
                PostedNumber = postedNumber,
                Date = request.Date.Date,
                Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
                GrossTotal = validation.Footer.GrossTotal,
                DiscountPercentage = validation.Footer.DiscountPercentage,
                DiscountAmount = validation.Footer.DiscountAmount,
                TaxPercentage = validation.Footer.TaxPercentage,
                TaxAmount = validation.Footer.TaxAmount,
                Total = validation.Footer.Total,
                SettlementMode = validation.ResolvedSettlementMode,
                PaymentMethod = validation.ResolvedPaymentMethod,
                AccountId = request.AccountId,
                CreditedAmount = validation.ResolvedSettlementMode == "cash_refund" ? 0m : validation.Footer.Total,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.PurchaseReturns.Add(pr);
            await db.SaveChangesAsync(cancellationToken);

            // Persist return items & deduct stock from Batches and Products
            var savedItems = new List<PurchaseReturnItem>(validation.NormalizedLines.Count);
            foreach (var line in validation.NormalizedLines)
            {
                var itemEntity = new PurchaseReturnItem
                {
                    PurchaseReturnId = pr.Id,
                    ProductId = line.Product.Id,
                    Batch = string.IsNullOrWhiteSpace(line.CleanBatch) ? null : line.CleanBatch,
                    Expiry = line.Expiry,
                    PackSize = line.Calc.PackSize,
                    PackPurchasedQuantity = line.Calc.PackPurchasedQuantity,
                    ReturnPackQuantity = line.Calc.ReturnPackQuantity,
                    ReturnUnitQuantity = line.Calc.ReturnUnitQuantity,
                    PackPurchasePrice = Math.Round(line.Calc.PackPurchasePrice, 2, MidpointRounding.AwayFromZero),
                    UnitPurchasePrice = Math.Round(line.Calc.UnitPurchasePrice, 4, MidpointRounding.AwayFromZero),
                    ItemDiscountPercentage = line.Calc.ItemDiscountPercentage,
                    SubTotal = line.Calc.SubTotal,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.PurchaseReturnItems.Add(itemEntity);
                savedItems.Add(itemEntity);

                await ApplyStockDeltaSmartAsync(
                    db,
                    line.Product,
                    line.CleanBatch,
                    line.Expiry,
                    deltaUnits: -line.Calc.ReturnUnitQuantity,
                    now,
                    cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);

            // Sync financial settlement (cash_refund vs credit_note / invoice_offset)
            await SyncPurchaseReturnSettlementAsync(
                db,
                pr,
                validation.Supplier,
                validation.LinkedInvoice,
                validation.ResolvedSettlementMode,
                validation.ResolvedPaymentMethod,
                request.AccountId,
                oldReturnSnapshot: null,
                updateDrawerIfPreShift: true,
                effectiveActorId,
                now,
                cancellationToken);

            // Record DocumentHistory
            var afterSnapshot = BuildAuditSnapshotJson(pr, validation.Supplier.Name, savedItems);
            db.DocumentHistories.Add(new DocumentHistory
            {
                DocumentType = "purchase_return",
                DocumentId = pr.Id,
                PostedNumber = pr.PostedNumber,
                Action = "created",
                UserId = effectiveActorId,
                AfterSnapshot = afterSnapshot,
                ChangesSummary = $"Created Purchase Return {pr.PostedNumber} ({validation.Supplier.Name}) — Total: {pr.Total:N2} [{pr.SettlementMode}]",
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new PurchaseReturnOperationResult
            {
                Succeeded = true,
                ReturnId = pr.Id,
                PostedNumber = pr.PostedNumber,
                Message = $"Purchase return {pr.PostedNumber} created successfully."
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return PurchaseReturnOperationResult.Fail($"Failed to create purchase return: {ex.Message}");
        }
    }

    public async Task<PurchaseReturnOperationResult> UpdateAsync(
        long id,
        PurchaseReturnUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var pr = await db.PurchaseReturns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (pr == null)
                return PurchaseReturnOperationResult.Fail($"Purchase return #{id} was not found.");

            var oldItems = await db.PurchaseReturnItems
                .Where(i => i.PurchaseReturnId == pr.Id)
                .OrderBy(i => i.Id)
                .ToListAsync(cancellationToken);

            var oldSupplier = await db.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == pr.SupplierId, cancellationToken);
            var beforeSnapshot = BuildAuditSnapshotJson(pr, oldSupplier?.Name ?? string.Empty, oldItems);

            var oldSnapshotMeta = new OldReturnSettlementSnapshot
            {
                OldSupplierId = pr.SupplierId,
                OldPurchaseInvoiceId = pr.PurchaseInvoiceId,
                OldTotal = pr.Total,
                OldSettlementMode = NormalizeSettlementMode(pr.SettlementMode),
                OldPaymentMethod = NormalizePaymentMethod(pr.PaymentMethod),
                OldAccountId = pr.AccountId,
                CreatedAt = pr.CreatedAt ?? pr.Date
            };

            var validation = await ValidateAndCalculatePurchaseReturnAsync(
                db,
                request,
                existingReturnId: pr.Id,
                cancellationToken);

            if (!validation.IsValid)
                return validation.FailureResult!;

            var now = DateTime.UtcNow;

            // 1. Revert stock from previous return items
            var oldProductIds = oldItems.Select(i => i.ProductId).Distinct().ToList();
            var trackedOldProducts = await db.Products
                .Where(p => oldProductIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            foreach (var oldItem in oldItems)
            {
                if (oldItem.ReturnUnitQuantity <= 0)
                    continue;

                if (trackedOldProducts.TryGetValue(oldItem.ProductId, out var oldProd))
                {
                    await ApplyStockDeltaSmartAsync(
                        db,
                        oldProd,
                        oldItem.Batch,
                        oldItem.Expiry,
                        deltaUnits: +oldItem.ReturnUnitQuantity,
                        now,
                        cancellationToken);
                }
            }

            db.PurchaseReturnItems.RemoveRange(oldItems);
            await db.SaveChangesAsync(cancellationToken);

            // 2. Update PurchaseReturn header
            pr.SupplierId = validation.Supplier!.Id;
            pr.PurchaseInvoiceId = validation.LinkedInvoice?.Id;
            pr.Date = request.Date.Date;
            pr.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
            pr.GrossTotal = validation.Footer.GrossTotal;
            pr.DiscountPercentage = validation.Footer.DiscountPercentage;
            pr.DiscountAmount = validation.Footer.DiscountAmount;
            pr.TaxPercentage = validation.Footer.TaxPercentage;
            pr.TaxAmount = validation.Footer.TaxAmount;
            pr.Total = validation.Footer.Total;
            pr.SettlementMode = validation.ResolvedSettlementMode;
            pr.PaymentMethod = validation.ResolvedPaymentMethod;
            pr.AccountId = request.AccountId;
            pr.CreditedAmount = validation.ResolvedSettlementMode == "cash_refund" ? 0m : validation.Footer.Total;
            pr.UpdatedAt = now;

            // 3. Insert new items & deduct stock
            var savedItems = new List<PurchaseReturnItem>(validation.NormalizedLines.Count);
            foreach (var line in validation.NormalizedLines)
            {
                var itemEntity = new PurchaseReturnItem
                {
                    PurchaseReturnId = pr.Id,
                    ProductId = line.Product.Id,
                    Batch = string.IsNullOrWhiteSpace(line.CleanBatch) ? null : line.CleanBatch,
                    Expiry = line.Expiry,
                    PackSize = line.Calc.PackSize,
                    PackPurchasedQuantity = line.Calc.PackPurchasedQuantity,
                    ReturnPackQuantity = line.Calc.ReturnPackQuantity,
                    ReturnUnitQuantity = line.Calc.ReturnUnitQuantity,
                    PackPurchasePrice = Math.Round(line.Calc.PackPurchasePrice, 2, MidpointRounding.AwayFromZero),
                    UnitPurchasePrice = Math.Round(line.Calc.UnitPurchasePrice, 4, MidpointRounding.AwayFromZero),
                    ItemDiscountPercentage = line.Calc.ItemDiscountPercentage,
                    SubTotal = line.Calc.SubTotal,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.PurchaseReturnItems.Add(itemEntity);
                savedItems.Add(itemEntity);

                await ApplyStockDeltaSmartAsync(
                    db,
                    line.Product,
                    line.CleanBatch,
                    line.Expiry,
                    deltaUnits: -line.Calc.ReturnUnitQuantity,
                    now,
                    cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);

            // 4. Re-sync financial settlement & supplier ledgers
            await SyncPurchaseReturnSettlementAsync(
                db,
                pr,
                validation.Supplier,
                validation.LinkedInvoice,
                validation.ResolvedSettlementMode,
                validation.ResolvedPaymentMethod,
                request.AccountId,
                oldReturnSnapshot: oldSnapshotMeta,
                updateDrawerIfPreShift: request.UpdateDrawerIfPreShift,
                effectiveActorId,
                now,
                cancellationToken);

            // 5. Record DocumentHistory
            var afterSnapshot = BuildAuditSnapshotJson(pr, validation.Supplier.Name, savedItems);
            db.DocumentHistories.Add(new DocumentHistory
            {
                DocumentType = "purchase_return",
                DocumentId = pr.Id,
                PostedNumber = pr.PostedNumber,
                Action = "updated",
                UserId = effectiveActorId,
                BeforeSnapshot = beforeSnapshot,
                AfterSnapshot = afterSnapshot,
                ChangesSummary = $"Updated Purchase Return {pr.PostedNumber} — Total: {oldSnapshotMeta.OldTotal:N2} → {pr.Total:N2} [{pr.SettlementMode}]",
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new PurchaseReturnOperationResult
            {
                Succeeded = true,
                ReturnId = pr.Id,
                PostedNumber = pr.PostedNumber,
                Message = $"Purchase return {pr.PostedNumber} updated successfully."
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return PurchaseReturnOperationResult.Fail($"Failed to update purchase return: {ex.Message}");
        }
    }

    public async Task<PurchaseReturnOperationResult> DeleteAsync(
        long id,
        string userPassword,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;
        if (effectiveActorId is > 0)
        {
            var passwordOk = await _authService.VerifyPasswordAsync(
                effectiveActorId.Value,
                userPassword ?? string.Empty,
                cancellationToken);
            if (!passwordOk)
                return PurchaseReturnOperationResult.Fail("Incorrect password. Deletion was not authorized.");
        }
        else if (string.IsNullOrWhiteSpace(userPassword))
        {
            return PurchaseReturnOperationResult.Fail("Password confirmation is required to delete a purchase return.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var pr = await db.PurchaseReturns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (pr == null)
                return PurchaseReturnOperationResult.Fail($"Purchase return #{id} was not found.");

            var items = await db.PurchaseReturnItems
                .Where(i => i.PurchaseReturnId == pr.Id)
                .ToListAsync(cancellationToken);

            var supplier = await db.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == pr.SupplierId, cancellationToken);
            var beforeSnapshot = BuildAuditSnapshotJson(pr, supplier?.Name ?? string.Empty, items);

            var now = DateTime.UtcNow;
            var postedNo = pr.PostedNumber;

            // 1. Restore stock to Batches & Products
            var productIds = items.Select(i => i.ProductId).Distinct().ToList();
            var trackedProducts = await db.Products
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            foreach (var item in items)
            {
                if (item.ReturnUnitQuantity <= 0)
                    continue;

                if (trackedProducts.TryGetValue(item.ProductId, out var prod))
                {
                    await ApplyStockDeltaSmartAsync(
                        db,
                        prod,
                        item.Batch,
                        item.Expiry,
                        deltaUnits: +item.ReturnUnitQuantity,
                        now,
                        cancellationToken);
                }
            }

            // 2. Unwind financial & ledger settlement (AccountTransaction, CashDrawer, DN- Payment, Allocations, SupplierLedger)
            await RemovePurchaseReturnSettlementAsync(
                db,
                pr,
                supplier?.Name ?? string.Empty,
                effectiveActorId,
                now,
                cancellationToken);

            // 3. Delete items & return header
            db.PurchaseReturnItems.RemoveRange(items);
            db.PurchaseReturns.Remove(pr);

            db.DocumentHistories.Add(new DocumentHistory
            {
                DocumentType = "purchase_return",
                DocumentId = id,
                PostedNumber = postedNo,
                Action = "deleted",
                UserId = effectiveActorId,
                BeforeSnapshot = beforeSnapshot,
                ChangesSummary = $"Deleted Purchase Return {postedNo} (Total: {pr.Total:N2})",
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new PurchaseReturnOperationResult
            {
                Succeeded = true,
                ReturnId = id,
                PostedNumber = postedNo,
                Message = $"Purchase return {postedNo} deleted and stock restored."
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return PurchaseReturnOperationResult.Fail($"Failed to delete purchase return: {ex.Message}");
        }
    }

    public async Task<List<DocumentHistoryEntryDto>> GetDocumentHistoryAsync(
        long returnId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var logs = await db.DocumentHistories.AsNoTracking()
            .Where(h => h.DocumentType == "purchase_return" && h.DocumentId == returnId)
            .OrderByDescending(h => h.CreatedAt)
            .ThenByDescending(h => h.Id)
            .ToListAsync(cancellationToken);

        var userIds = logs.Where(l => l.UserId.HasValue).Select(l => l.UserId!.Value).Distinct().ToList();
        var userMap = userIds.Count > 0
            ? await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken)
            : [];

        return logs.Select(l => new DocumentHistoryEntryDto
        {
            Id = l.Id,
            Action = l.Action,
            PostedNumber = l.PostedNumber ?? string.Empty,
            UserName = l.UserId.HasValue && userMap.TryGetValue(l.UserId.Value, out var uName) ? uName : "System",
            Reason = l.Reason,
            ChangesSummary = l.ChangesSummary ?? string.Empty,
            CreatedAt = l.CreatedAt ?? DateTime.UtcNow
        }).ToList();
    }

    // =========================================================================
    // 3. INTERNAL HELPERS: ENTITLEMENT, STOCK, SETTLEMENT & LEDGER SYNC
    // =========================================================================

    private sealed class NormalizedReturnLineInternal
    {
        public required Product Product { get; init; }
        public required string CleanBatch { get; init; }
        public required DateTime? Expiry { get; init; }
        public required bool IsUnbatched { get; init; }
        public required PurchaseReturnLineCalcModel Calc { get; init; }
    }

    private sealed class ValidatedPurchaseReturnInternal
    {
        public bool IsValid { get; init; }
        public PurchaseReturnOperationResult? FailureResult { get; init; }
        public Supplier? Supplier { get; init; }
        public PurchaseInvoice? LinkedInvoice { get; init; }
        public string ResolvedSettlementMode { get; init; } = "cash_refund";
        public string ResolvedPaymentMethod { get; init; } = "cash";
        public List<NormalizedReturnLineInternal> NormalizedLines { get; init; } = [];
        public PurchaseReturnFooterCalcModel Footer { get; init; } = new();
    }

    private sealed class OldReturnSettlementSnapshot
    {
        public long OldSupplierId { get; init; }
        public long? OldPurchaseInvoiceId { get; init; }
        public decimal OldTotal { get; init; }
        public string OldSettlementMode { get; init; } = "cash_refund";
        public string OldPaymentMethod { get; init; } = "cash";
        public long? OldAccountId { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    private static string NormalizeSettlementMode(string? raw, PurchaseInvoice? linkedInvoice = null)
    {
        var clean = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (clean == "ledger_credit")
            return "credit_note";

        if (clean is "cash_refund" or "credit_note" or "invoice_offset")
            return clean;

        if (linkedInvoice != null && string.Equals(linkedInvoice.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase))
            return "credit_note";

        return "cash_refund";
    }

    private static string NormalizePaymentMethod(string? raw)
    {
        var clean = (raw ?? "cash").Trim().ToLowerInvariant();
        return clean switch
        {
            "cash" or "bank_transfer" or "card" or "mobile_wallet" or "cheque" or "other" => clean,
            _ => "cash"
        };
    }

    private static async Task<string> ComputeNextPostedNumberInternalAsync(
        KarobarDbContext db,
        CancellationToken cancellationToken)
    {
        var existingCodes = await db.PurchaseReturns.AsNoTracking()
            .Where(r => r.PostedNumber.StartsWith("PRRET-"))
            .Select(r => r.PostedNumber)
            .ToListAsync(cancellationToken);

        var maxNum = 0;
        foreach (var code in existingCodes)
        {
            var suffix = code["PRRET-".Length..].Trim();
            if (int.TryParse(suffix, out var n) && n > maxNum)
                maxNum = n;
        }

        return $"PRRET-{(maxNum + 1):D4}";
    }

    private static async Task<List<PurchaseSupplierLookupDto>> BuildSupplierLookupsAsync(
        KarobarDbContext db,
        CancellationToken cancellationToken)
    {
        var suppliers = await db.Suppliers.AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var latestBalances = await db.SupplierLedgers.AsNoTracking()
            .GroupBy(l => l.SupplierId)
            .Select(g => new
            {
                SupplierId = g.Key,
                LatestEntry = g.OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.Id).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.SupplierId, x => x.LatestEntry?.TotalBalance ?? 0m, cancellationToken);

        return suppliers.Select(s => new PurchaseSupplierLookupDto
        {
            Id = s.Id,
            Name = s.Name,
            Phone = s.Phone ?? string.Empty,
            Address = s.Address ?? string.Empty,
            CreditLimit = s.CreditLimit ?? 0m,
            CreditDays = s.CreditDays ?? 30,
            CurrentPayable = latestBalances.TryGetValue(s.Id, out var bal) ? bal : 0m,
            ExpiryReturnPolicy = s.ExpiryReturnPolicy ?? "before_expiry",
            ExpiryReturnMonths = s.ExpiryReturnMonths ?? 3,
            ExpiryReturnDays = s.ExpiryReturnDays ?? 90
        }).ToList();
    }

    private static async Task<List<PurchaseReturnInvoiceItemOptionDto>> GetInvoiceReturnableItemsInternalAsync(
        KarobarDbContext db,
        long purchaseInvoiceId,
        long? excludePurchaseReturnId,
        CancellationToken cancellationToken)
    {
        var invItems = await db.PurchaseInvoiceItems.AsNoTracking()
            .Where(ii => ii.PurchaseInvoiceId == purchaseInvoiceId)
            .OrderBy(ii => ii.LineOrder)
            .ThenBy(ii => ii.Id)
            .ToListAsync(cancellationToken);

        if (invItems.Count == 0)
            return [];

        var productIds = invItems.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var batches = await db.Batches.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId))
            .ToListAsync(cancellationToken);

        // Sum already-returned units across other PurchaseReturns for this same PurchaseInvoice
        var otherReturnsQuery = db.PurchaseReturns.AsNoTracking()
            .Where(r => r.PurchaseInvoiceId == purchaseInvoiceId);

        if (excludePurchaseReturnId is > 0)
        {
            var exId = excludePurchaseReturnId.Value;
            otherReturnsQuery = otherReturnsQuery.Where(r => r.Id != exId);
        }

        var otherReturnIds = await otherReturnsQuery.Select(r => r.Id).ToListAsync(cancellationToken);
        var otherReturnItems = otherReturnIds.Count > 0
            ? await db.PurchaseReturnItems.AsNoTracking()
                .Where(ri => otherReturnIds.Contains(ri.PurchaseReturnId))
                .ToListAsync(cancellationToken)
            : [];

        // Also if editing an existing return, load its own items to restore physical stock availability
        var editedReturnItems = excludePurchaseReturnId is > 0
            ? await db.PurchaseReturnItems.AsNoTracking()
                .Where(ri => ri.PurchaseReturnId == excludePurchaseReturnId.Value)
                .ToListAsync(cancellationToken)
            : [];

        // Group purchased units by (ProductId, BatchKey, ExpiryKey) matching ReturnEntitlement.php
        static string NormBatch(string? b) => (b ?? string.Empty).Trim().ToUpperInvariant();
        static string NormExp(DateTime? e) => e.HasValue ? e.Value.Date.ToString("yyyy-MM-dd") : string.Empty;

        var returnedByExactKey = new Dictionary<(long Pid, string Batch, string Exp), int>();
        var returnedByProductBatchOnly = new Dictionary<(long Pid, string Batch), int>();
        var returnedUnbatchedByProduct = new Dictionary<long, int>();

        foreach (var ri in otherReturnItems)
        {
            var qty = Math.Max(0, ri.ReturnUnitQuantity);
            if (qty <= 0) continue;

            var bKey = NormBatch(ri.Batch);
            var eKey = NormExp(ri.Expiry);
            if (!string.IsNullOrEmpty(bKey))
            {
                var exactKey = (ri.ProductId, bKey, eKey);
                returnedByExactKey[exactKey] = returnedByExactKey.GetValueOrDefault(exactKey) + qty;

                var pbKey = (ri.ProductId, bKey);
                returnedByProductBatchOnly[pbKey] = returnedByProductBatchOnly.GetValueOrDefault(pbKey) + qty;
            }
            else
            {
                returnedUnbatchedByProduct[ri.ProductId] = returnedUnbatchedByProduct.GetValueOrDefault(ri.ProductId) + qty;
            }
        }

        // Aggregate invoice lines by (ProductId, Batch, Expiry)
        var groupedLines = invItems
            .GroupBy(ii => new
            {
                ii.ProductId,
                BatchKey = NormBatch(ii.Batch),
                ExpKey = NormExp(ii.Expiry)
            })
            .ToList();

        var results = new List<PurchaseReturnInvoiceItemOptionDto>(groupedLines.Count);

        foreach (var grp in groupedLines)
        {
            var first = grp.First();
            products.TryGetValue(first.ProductId, out var prod);

            var purchasedUnits = grp.Sum(ii => ii.Quantity > 0 ? ii.Quantity : (ii.UnitQuantity + ii.UnitBonus));
            var packPurchased = grp.Sum(ii => ii.PackQuantity);

            int alreadyRet;
            if (!string.IsNullOrEmpty(grp.Key.BatchKey))
            {
                if (!returnedByExactKey.TryGetValue((grp.Key.ProductId, grp.Key.BatchKey, grp.Key.ExpKey), out alreadyRet))
                {
                    returnedByProductBatchOnly.TryGetValue((grp.Key.ProductId, grp.Key.BatchKey), out alreadyRet);
                }
            }
            else
            {
                returnedUnbatchedByProduct.TryGetValue(grp.Key.ProductId, out alreadyRet);
            }

            // Also account for unbatched returns against a single-batch invoice line (ReturnEntitlement fallback)
            if (!string.IsNullOrEmpty(grp.Key.BatchKey) &&
                alreadyRet == 0 &&
                groupedLines.Count(g => g.Key.ProductId == grp.Key.ProductId) == 1 &&
                returnedUnbatchedByProduct.TryGetValue(grp.Key.ProductId, out var fallbackRet))
            {
                alreadyRet = fallbackRet;
            }

            var returnableUnits = Math.Max(0, purchasedUnits - alreadyRet);

            // Physical stock on hand (including restored units from edited return)
            var restoredProdUnits = editedReturnItems
                .Where(x => x.ProductId == first.ProductId)
                .Sum(x => x.ReturnUnitQuantity);
            var physicalProdUnits = Math.Max(0, (prod?.Quantity ?? 0) + restoredProdUnits);

            int physicalLineUnits;
            if (!string.IsNullOrEmpty(grp.Key.BatchKey))
            {
                var matchingBatches = batches.Where(b =>
                    b.ProductId == first.ProductId &&
                    string.Equals(NormBatch(b.BatchNumber), grp.Key.BatchKey, StringComparison.OrdinalIgnoreCase)).ToList();

                var restoredBatchUnits = editedReturnItems
                    .Where(x => x.ProductId == first.ProductId && string.Equals(NormBatch(x.Batch), grp.Key.BatchKey, StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.ReturnUnitQuantity);

                physicalLineUnits = matchingBatches.Count > 0
                    ? Math.Max(0, matchingBatches.Sum(b => Math.Max(0, b.Quantity)) + restoredBatchUnits)
                    : physicalProdUnits;
            }
            else
            {
                physicalLineUnits = physicalProdUnits;
            }

            results.Add(new PurchaseReturnInvoiceItemOptionDto
            {
                InvoiceItemId = first.Id,
                ProductId = first.ProductId,
                ProductCode = prod?.ProductCode ?? string.Empty,
                ProductName = prod?.Name ?? $"Product #{first.ProductId}",
                Batch = string.IsNullOrWhiteSpace(first.Batch) ? string.Empty : first.Batch.Trim(),
                Expiry = first.Expiry,
                PackSize = first.PackSize > 0 ? first.PackSize : (prod?.PackSize > 0 ? prod.PackSize : 1),
                PackPurchasedQuantity = packPurchased,
                UnitPurchasedQuantity = grp.Sum(ii => ii.UnitQuantity),
                UnitBonus = grp.Sum(ii => ii.UnitBonus),
                TotalPurchasedUnits = purchasedUnits,
                AlreadyReturnedUnits = alreadyRet,
                ReturnableUnits = returnableUnits,
                PhysicalAvailableUnits = physicalLineUnits,
                EffectiveAvailableUnits = Math.Min(returnableUnits, physicalLineUnits),
                PackPurchasePrice = first.PackPurchasePrice,
                UnitPurchasePrice = first.UnitPurchasePrice,
                ItemDiscountPercentage = first.ItemDiscountPercentage
            });
        }

        return results;
    }

    private static async Task<List<PurchaseReturnProductSearchDto>> BuildProductSearchDtosAsync(
        KarobarDbContext db,
        List<Product> products,
        Dictionary<long, int>? returnableByProduct,
        Dictionary<long, PurchaseReturnInvoiceItemOptionDto>? invoicePriceByProduct,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0)
            return [];

        var productIds = products.Select(p => p.Id).ToList();
        var catIds = products.Where(p => p.CategoryId.HasValue).Select(p => p.CategoryId!.Value).Distinct().ToList();
        var brandIds = products.Where(p => p.BrandId.HasValue).Select(p => p.BrandId!.Value).Distinct().ToList();

        var catMap = catIds.Count > 0
            ? await db.Categories.AsNoTracking()
                .Where(c => catIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            : [];

        var brandMap = brandIds.Count > 0
            ? await db.Brands.AsNoTracking()
                .Where(b => brandIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken)
            : [];

        var batchCounts = await db.Batches.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId) && b.Quantity > 0)
            .GroupBy(b => b.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Count, cancellationToken);

        return products.Select(p =>
        {
            var packSize = p.PackSize > 0 ? p.PackSize : 1;
            var packCost = p.PackPurchasePrice > 0m ? p.PackPurchasePrice : Math.Round(p.UnitPurchasePrice * packSize, 2);
            var unitCost = p.UnitPurchasePrice > 0m ? p.UnitPurchasePrice : (packSize > 0 ? Math.Round(packCost / packSize, 4) : 0m);

            if (invoicePriceByProduct != null && invoicePriceByProduct.TryGetValue(p.Id, out var invOpt))
            {
                packSize = invOpt.PackSize > 0 ? invOpt.PackSize : packSize;
                if (invOpt.PackPurchasePrice > 0m) packCost = invOpt.PackPurchasePrice;
                if (invOpt.UnitPurchasePrice > 0m) unitCost = invOpt.UnitPurchasePrice;
            }

            int? retUnits = null;
            if (returnableByProduct != null && returnableByProduct.TryGetValue(p.Id, out var rVal))
            {
                retUnits = rVal;
            }

            return new PurchaseReturnProductSearchDto
            {
                Id = p.Id,
                ProductCode = p.ProductCode,
                Barcode = p.Barcode,
                Name = p.Name,
                GenericName = p.Formulation,
                Formulation = p.Formulation,
                CategoryName = p.CategoryId.HasValue && catMap.TryGetValue(p.CategoryId.Value, out var cName) ? cName : "Uncategorized",
                BrandName = p.BrandId.HasValue && brandMap.TryGetValue(p.BrandId.Value, out var bName) ? bName : "—",
                Rack = p.Rack,
                PackSize = packSize,
                QuantityOnHand = p.Quantity,
                ReturnableUnitsOnInvoice = retUnits,
                PackPurchasePrice = packCost,
                UnitPurchasePrice = unitCost,
                PackSalePrice = p.PackSalePrice,
                UnitSalePrice = p.UnitSalePrice,
                AvgPrice = p.AvgPrice,
                BatchesCount = batchCounts.TryGetValue(p.Id, out var bc) ? bc : 0
            };
        }).ToList();
    }

    private static async Task<List<PurchaseReturnBatchOptionDto>> GetProductBatchesInternalAsync(
        KarobarDbContext db,
        long productId,
        long? purchaseInvoiceId,
        long? excludePurchaseReturnId,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product == null)
            return [];

        var packSize = product.PackSize > 0 ? product.PackSize : 1;
        var defaultPackCost = product.PackPurchasePrice > 0m
            ? product.PackPurchasePrice
            : Math.Round(product.UnitPurchasePrice * packSize, 2);
        var defaultUnitCost = product.UnitPurchasePrice > 0m
            ? product.UnitPurchasePrice
            : Math.Round(defaultPackCost / packSize, 4);

        // 1. INVOICE-LINKED MODE: Return batch options from the linked Purchase Invoice
        if (purchaseInvoiceId is > 0)
        {
            var invItems = await GetInvoiceReturnableItemsInternalAsync(
                db,
                purchaseInvoiceId.Value,
                excludePurchaseReturnId,
                cancellationToken);

            var forProduct = invItems.Where(i => i.ProductId == productId).ToList();
            return forProduct.Select(i => new PurchaseReturnBatchOptionDto
            {
                BatchId = null,
                ProductId = productId,
                BatchNumber = i.IsUnbatched ? string.Empty : (i.Batch ?? string.Empty),
                ExpiryDate = i.Expiry,
                IsUnbatched = i.IsUnbatched,
                PackSize = i.PackSize > 0 ? i.PackSize : packSize,
                PhysicalAvailableUnits = i.PhysicalAvailableUnits,
                InvoiceReturnableUnits = i.ReturnableUnits,
                AvailableUnits = i.ReturnableUnits,
                PackPurchasedQuantity = i.PackPurchasedQuantity,
                PackPurchasePrice = i.PackPurchasePrice > 0m ? i.PackPurchasePrice : defaultPackCost,
                UnitPurchasePrice = i.UnitPurchasePrice > 0m ? i.UnitPurchasePrice : defaultUnitCost,
                ItemDiscountPercentage = i.ItemDiscountPercentage
            }).ToList();
        }

        // 2. OPEN RETURN MODE: Return physical batches + [Unassigned Stock]
        var batches = await db.Batches.AsNoTracking()
            .Where(b => b.ProductId == productId)
            .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
            .ThenBy(b => b.Id)
            .ToListAsync(cancellationToken);

        var editedItems = excludePurchaseReturnId is > 0
            ? await db.PurchaseReturnItems.AsNoTracking()
                .Where(ri => ri.PurchaseReturnId == excludePurchaseReturnId.Value && ri.ProductId == productId)
                .ToListAsync(cancellationToken)
            : [];

        var restoredTotalForProduct = editedItems.Sum(x => x.ReturnUnitQuantity);
        var effectiveProductStock = Math.Max(0, product.Quantity + restoredTotalForProduct);

        var options = new List<PurchaseReturnBatchOptionDto>();
        var totalAssignedStock = 0;

        foreach (var b in batches.Where(x => !string.IsNullOrWhiteSpace(x.BatchNumber)))
        {
            var bNo = b.BatchNumber!.Trim();
            var restoredForBatch = editedItems
                .Where(x => string.Equals((x.Batch ?? string.Empty).Trim(), bNo, StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.ReturnUnitQuantity);

            var avail = Math.Max(0, b.Quantity + restoredForBatch);
            totalAssignedStock += avail;

            if (avail > 0 || restoredForBatch > 0)
            {
                var bPackSize = b.PackSize is > 0 ? b.PackSize.Value : packSize;
                var bPackCost = b.PackPurchasePrice is > 0m ? b.PackPurchasePrice.Value : defaultPackCost;
                var bUnitCost = b.UnitPurchasePrice is > 0m ? b.UnitPurchasePrice.Value : defaultUnitCost;

                options.Add(new PurchaseReturnBatchOptionDto
                {
                    BatchId = b.Id,
                    ProductId = productId,
                    BatchNumber = bNo,
                    ExpiryDate = b.ExpiryDate,
                    IsUnbatched = false,
                    PackSize = bPackSize,
                    PhysicalAvailableUnits = avail,
                    AvailableUnits = avail,
                    PackPurchasedQuantity = 0m,
                    PackPurchasePrice = bPackCost,
                    UnitPurchasePrice = bUnitCost,
                    ItemDiscountPercentage = 0m
                });
            }
        }

        var unassignedUnits = Math.Max(0, effectiveProductStock - totalAssignedStock);
        var hadEditedUnbatched = editedItems.Any(x => string.IsNullOrWhiteSpace(x.Batch));

        if (unassignedUnits > 0 || options.Count == 0 || hadEditedUnbatched)
        {
            options.Add(new PurchaseReturnBatchOptionDto
            {
                BatchId = null,
                ProductId = productId,
                BatchNumber = string.Empty,
                ExpiryDate = null,
                IsUnbatched = true,
                PackSize = packSize,
                PhysicalAvailableUnits = options.Count == 0 ? effectiveProductStock : unassignedUnits,
                AvailableUnits = options.Count == 0 ? effectiveProductStock : unassignedUnits,
                PackPurchasedQuantity = 0m,
                PackPurchasePrice = defaultPackCost,
                UnitPurchasePrice = defaultUnitCost,
                ItemDiscountPercentage = 0m
            });
        }

        return options;
    }

    private static async Task<ValidatedPurchaseReturnInternal> ValidateAndCalculatePurchaseReturnAsync(
        KarobarDbContext db,
        PurchaseReturnUpsertRequest request,
        long? existingReturnId,
        CancellationToken cancellationToken)
    {
        if (request.SupplierId <= 0)
        {
            return new ValidatedPurchaseReturnInternal
            {
                IsValid = false,
                FailureResult = PurchaseReturnOperationResult.Fail("Please select a supplier.", fieldName: "supplier_id")
            };
        }

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken);
        if (supplier == null)
        {
            return new ValidatedPurchaseReturnInternal
            {
                IsValid = false,
                FailureResult = PurchaseReturnOperationResult.Fail($"Supplier #{request.SupplierId} was not found.", fieldName: "supplier_id")
            };
        }

        PurchaseInvoice? linkedInvoice = null;
        if (request.PurchaseInvoiceId is > 0)
        {
            var invId = request.PurchaseInvoiceId.Value;
            linkedInvoice = await db.PurchaseInvoices.FirstOrDefaultAsync(pi => pi.Id == invId, cancellationToken);
            if (linkedInvoice == null)
            {
                return new ValidatedPurchaseReturnInternal
                {
                    IsValid = false,
                    FailureResult = PurchaseReturnOperationResult.Fail($"Linked Purchase Invoice #{invId} was not found.", fieldName: "purchase_invoice_id")
                };
            }

            if (string.Equals(linkedInvoice.Status, "reversed", StringComparison.OrdinalIgnoreCase))
            {
                return new ValidatedPurchaseReturnInternal
                {
                    IsValid = false,
                    FailureResult = PurchaseReturnOperationResult.Fail($"Purchase Invoice {linkedInvoice.PostedNumber} has been reversed and cannot be returned against.", fieldName: "purchase_invoice_id")
                };
            }

            if (linkedInvoice.SupplierId != supplier.Id)
            {
                return new ValidatedPurchaseReturnInternal
                {
                    IsValid = false,
                    FailureResult = PurchaseReturnOperationResult.Fail("Selected purchase invoice does not belong to the selected supplier.", fieldName: "purchase_invoice_id")
                };
            }
        }

        var activeInputs = request.Items
            .Select((item, idx) => (Item: item, Index: idx))
            .Where(x => x.Item.ProductId > 0)
            .ToList();

        if (activeInputs.Count == 0)
        {
            return new ValidatedPurchaseReturnInternal
            {
                IsValid = false,
                FailureResult = PurchaseReturnOperationResult.Fail("Add at least one product to the purchase return.")
            };
        }

        var productIds = activeInputs.Select(x => x.Item.ProductId).Distinct().ToList();
        var trackedProducts = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // Load old return items if updating so we can add their quantities back to physical stock capacity
        var oldReturnItems = existingReturnId is > 0
            ? await db.PurchaseReturnItems.AsNoTracking()
                .Where(ri => ri.PurchaseReturnId == existingReturnId.Value)
                .ToListAsync(cancellationToken)
            : [];

        // Check duplicate (ProductId, BatchKey) rows
        var seenRowKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var normalizedLines = new List<NormalizedReturnLineInternal>(activeInputs.Count);

        foreach (var (input, rowIdx) in activeInputs)
        {
            if (!trackedProducts.TryGetValue(input.ProductId, out var prod))
            {
                return FailLine(rowIdx, "product_id", $"Row {rowIdx + 1}: Product #{input.ProductId} was not found.");
            }

            var cleanBatch = input.IsUnbatched ? string.Empty : (input.Batch ?? string.Empty).Trim();
            if (cleanBatch is "[Unassigned Stock]" or "[Unbatched Stock]" or "__unbatched__")
                cleanBatch = string.Empty;

            var rowKey = $"{prod.Id}::{(string.IsNullOrEmpty(cleanBatch) ? "__unbatched__" : cleanBatch)}";
            if (seenRowKeys.TryGetValue(rowKey, out var firstRowIdx))
            {
                var dupMsg = string.IsNullOrEmpty(cleanBatch)
                    ? $"Unassigned stock for '{prod.Name}' is selected on both row {firstRowIdx + 1} and row {rowIdx + 1}."
                    : $"Duplicate batch '{cleanBatch}' for '{prod.Name}' on rows {firstRowIdx + 1} and {rowIdx + 1}.";
                return FailLine(rowIdx, "batch", dupMsg);
            }
            seenRowKeys[rowKey] = rowIdx;

            var packSize = input.PackSize > 0 ? input.PackSize : (prod.PackSize > 0 ? prod.PackSize : 1);
            var calc = new PurchaseReturnLineCalcModel
            {
                PackSize = packSize,
                PackPurchasedQuantity = Math.Max(0m, input.PackPurchasedQuantity),
                ReturnPackQuantity = Math.Max(0m, input.ReturnPackQuantity),
                ReturnUnitQuantity = Math.Max(0, input.ReturnUnitQuantity),
                PackPurchasePrice = Math.Max(0m, input.PackPurchasePrice),
                UnitPurchasePrice = Math.Max(0m, input.UnitPurchasePrice),
                ItemDiscountPercentage = Math.Clamp(input.ItemDiscountPercentage, 0m, 100m)
            };

            var changedTrigger = calc.ReturnUnitQuantity > 0
                ? "return_unit_quantity"
                : (calc.ReturnPackQuantity > 0m ? "return_pack_quantity" : null);

            InvoiceCalculators.RecalcPurchaseReturnItem(calc, changedTrigger);

            if (calc.ReturnUnitQuantity <= 0)
            {
                return FailLine(rowIdx, "return_pack_quantity", $"Row {rowIdx + 1}: Enter a return quantity greater than zero for '{prod.Name}'.");
            }

            normalizedLines.Add(new NormalizedReturnLineInternal
            {
                Product = prod,
                CleanBatch = cleanBatch,
                Expiry = string.IsNullOrEmpty(cleanBatch) ? null : input.Expiry?.Date,
                IsUnbatched = string.IsNullOrEmpty(cleanBatch),
                Calc = calc
            });
        }

        // Validate against Invoice ReturnEntitlement if linked to a Purchase Invoice
        if (linkedInvoice != null)
        {
            var entitlementOptions = await GetInvoiceReturnableItemsInternalAsync(
                db,
                linkedInvoice.Id,
                existingReturnId,
                cancellationToken);

            var entitlementByProduct = entitlementOptions
                .GroupBy(e => e.ProductId)
                .ToDictionary(g => g.Key, g => g.ToList());

            for (var i = 0; i < normalizedLines.Count; i++)
            {
                var line = normalizedLines[i];
                if (!entitlementByProduct.TryGetValue(line.Product.Id, out var prodEntitlements) || prodEntitlements.Count == 0)
                {
                    return FailLine(
                        i,
                        "product_id",
                        $"Row {i + 1}: Product '{line.Product.Name}' does not exist on Purchase Invoice {linkedInvoice.PostedNumber}.");
                }

                int maxReturnable;
                if (!string.IsNullOrEmpty(line.CleanBatch))
                {
                    var batchEnt = prodEntitlements.FirstOrDefault(e =>
                        string.Equals((e.Batch ?? string.Empty).Trim(), line.CleanBatch, StringComparison.OrdinalIgnoreCase));
                    if (batchEnt == null)
                    {
                        return FailLine(
                            i,
                            "batch",
                            $"Row {i + 1}: Batch '{line.CleanBatch}' for '{line.Product.Name}' was not purchased on invoice {linkedInvoice.PostedNumber}.");
                    }
                    maxReturnable = batchEnt.ReturnableUnits;
                }
                else
                {
                    var unbatchedEnt = prodEntitlements.FirstOrDefault(e => e.IsUnbatched);
                    maxReturnable = unbatchedEnt?.ReturnableUnits ?? prodEntitlements.Sum(e => e.ReturnableUnits);
                }

                if (line.Calc.ReturnUnitQuantity > maxReturnable)
                {
                    return FailLine(
                        i,
                        "return_unit_quantity",
                        $"Row {i + 1}: Return quantity ({line.Calc.ReturnUnitQuantity}) exceeds remaining returnable quantity ({maxReturnable}) on invoice {linkedInvoice.PostedNumber}.");
                }
            }
        }

        // Validate against Physical Stock on Hand (Product & Batch level)
        foreach (var prodGroup in normalizedLines.GroupBy(l => l.Product.Id))
        {
            var prod = prodGroup.First().Product;
            var restoredProdUnits = oldReturnItems
                .Where(ri => ri.ProductId == prod.Id)
                .Sum(ri => ri.ReturnUnitQuantity);
            var availableProductStock = Math.Max(0, prod.Quantity + restoredProdUnits);
            var requestedProductUnits = prodGroup.Sum(l => l.Calc.ReturnUnitQuantity);

            if (requestedProductUnits > availableProductStock)
            {
                return FailLine(
                    0,
                    "return_unit_quantity",
                    $"Insufficient physical stock for '{prod.Name}': Available {availableProductStock} units, trying to return {requestedProductUnits} units.");
            }
        }

        for (var i = 0; i < normalizedLines.Count; i++)
        {
            var line = normalizedLines[i];
            if (!string.IsNullOrEmpty(line.CleanBatch))
            {
                var matchingBatches = await db.Batches.AsNoTracking()
                    .Where(b => b.ProductId == line.Product.Id && b.BatchNumber != null && b.BatchNumber.ToLower() == line.CleanBatch.ToLower())
                    .ToListAsync(cancellationToken);

                var restoredBatchUnits = oldReturnItems
                    .Where(ri => ri.ProductId == line.Product.Id && string.Equals((ri.Batch ?? string.Empty).Trim(), line.CleanBatch, StringComparison.OrdinalIgnoreCase))
                    .Sum(ri => ri.ReturnUnitQuantity);

                if (matchingBatches.Count > 0)
                {
                    var batchStock = Math.Max(0, matchingBatches.Sum(b => Math.Max(0, b.Quantity)) + restoredBatchUnits);
                    if (line.Calc.ReturnUnitQuantity > batchStock)
                    {
                        return FailLine(
                            i,
                            "return_unit_quantity",
                            $"Row {i + 1}: Batch '{line.CleanBatch}' for '{line.Product.Name}' only has {batchStock} available units in stock (requested {line.Calc.ReturnUnitQuantity}).");
                    }
                }
            }
        }

        // Calculate Footer
        var footer = new PurchaseReturnFooterCalcModel
        {
            DiscountPercentage = Math.Clamp(request.DiscountPercentage, 0m, 100m),
            DiscountAmount = Math.Max(0m, request.DiscountAmount),
            TaxPercentage = Math.Max(0m, request.TaxPercentage),
            TaxAmount = Math.Max(0m, request.TaxAmount)
        };

        var discTrigger = request.DiscountPercentage > 0m
            ? "discount_percentage"
            : (request.DiscountAmount > 0m ? "discount_amount" : null);
        InvoiceCalculators.RecalcPurchaseReturnFooter(normalizedLines.Select(l => l.Calc), footer, discTrigger);

        var taxTrigger = request.TaxPercentage > 0m
            ? "tax_percentage"
            : (request.TaxAmount > 0m ? "tax_amount" : null);
        if (taxTrigger != null)
        {
            InvoiceCalculators.RecalcPurchaseReturnFooter(normalizedLines.Select(l => l.Calc), footer, taxTrigger);
        }

        var resolvedSettlement = NormalizeSettlementMode(request.SettlementMode, linkedInvoice);
        var resolvedMethod = NormalizePaymentMethod(request.PaymentMethod);

        return new ValidatedPurchaseReturnInternal
        {
            IsValid = true,
            Supplier = supplier,
            LinkedInvoice = linkedInvoice,
            ResolvedSettlementMode = resolvedSettlement,
            ResolvedPaymentMethod = resolvedMethod,
            NormalizedLines = normalizedLines,
            Footer = footer
        };

        static ValidatedPurchaseReturnInternal FailLine(int idx, string field, string msg) => new()
        {
            IsValid = false,
            FailureResult = PurchaseReturnOperationResult.Fail(msg, rowIndex: idx, fieldName: field)
        };
    }

    /// <summary>
    /// Exact port of PurchaseReturnController::applyStockDeltaSmart.
    /// When deltaUnits &lt; 0 (deducting returned stock): deducts from matching Batch first, then Product.Quantity.
    /// When deltaUnits &gt; 0 (restoring stock on edit/delete): restores to matching Batch and Product.Quantity.
    /// </summary>
    private static async Task ApplyStockDeltaSmartAsync(
        KarobarDbContext db,
        Product product,
        string? batchNumber,
        DateTime? expiry,
        int deltaUnits,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (deltaUnits == 0)
            return;

        var cleanBatch = (batchNumber ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(cleanBatch))
        {
            var candidateBatches = await db.Batches
                .Where(b => b.ProductId == product.Id && b.BatchNumber != null && b.BatchNumber.ToLower() == cleanBatch.ToLower())
                .OrderBy(b => b.Id)
                .ToListAsync(cancellationToken);

            Batch? targetBatch = null;
            if (expiry.HasValue)
            {
                var expDate = expiry.Value.Date;
                targetBatch = candidateBatches.FirstOrDefault(b => b.ExpiryDate.HasValue && b.ExpiryDate.Value.Date == expDate);
            }
            targetBatch ??= candidateBatches.FirstOrDefault();

            if (deltaUnits < 0)
            {
                if (targetBatch != null)
                {
                    targetBatch.Quantity = Math.Max(0, targetBatch.Quantity + deltaUnits);
                    targetBatch.UpdatedAt = now;
                }
            }
            else if (deltaUnits > 0)
            {
                if (targetBatch != null)
                {
                    targetBatch.Quantity = Math.Max(0, targetBatch.Quantity + deltaUnits);
                    targetBatch.UpdatedAt = now;
                }
                else
                {
                    db.Batches.Add(new Batch
                    {
                        ProductId = product.Id,
                        BatchNumber = cleanBatch,
                        ExpiryDate = expiry?.Date,
                        Quantity = deltaUnits,
                        PackSize = product.PackSize > 0 ? product.PackSize : 1,
                        StorageLocationId = product.StorageLocationId,
                        Rack = product.Rack,
                        PackPurchasePrice = product.PackPurchasePrice,
                        UnitPurchasePrice = product.UnitPurchasePrice,
                        PackSalePrice = product.PackSalePrice,
                        UnitSalePrice = product.UnitSalePrice,
                        WholeSalePackPrice = product.WholeSalePackPrice,
                        WholeSaleUnitPrice = product.WholeSaleUnitPrice,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }
        }

        product.Quantity = Math.Max(0, product.Quantity + deltaUnits);
        if (product.Quantity == 0 && deltaUnits < 0)
        {
            product.AvgPrice = 0m;
            product.Margin = 0m;
            product.WholeSaleMargin = 0m;
        }
        product.UpdatedAt = now;
    }

    /// <summary>
    /// Exact port of ReturnSettlementService::syncPurchaseReturnSettlement.
    /// Supports:
    /// 1. 'cash_refund': Deposits refund into Account (AccountTransaction Direction='in') & CashDrawerTransaction, removes any DN- voucher & SupplierLedger entry.
    /// 2. 'credit_note' (Debit Note DN-) & 'invoice_offset': Creates/updates Payment ('DN-' + PostedNumber) & SupplierLedger row (DebitedAmount = Total) and optionally auto-allocates against linked PurchaseInvoice.
    /// </summary>
    private static async Task SyncPurchaseReturnSettlementAsync(
        KarobarDbContext db,
        PurchaseReturn pr,
        Supplier supplier,
        PurchaseInvoice? linkedInvoice,
        string mode,
        string paymentMethod,
        long? explicitAccountId,
        OldReturnSettlementSnapshot? oldReturnSnapshot,
        bool updateDrawerIfPreShift,
        long? actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var total = Math.Max(0m, Math.Round(pr.Total, 2));

        // 1. Unwind previous AccountTransaction for this return (if any)
        await UnwindAccountTransactionsForPurchaseReturnAsync(db, pr.Id, now, cancellationToken);

        // 2. Unwind previous DN- Payment voucher & allocations (if any)
        var affectedInvoiceIds = await UnwindPurchaseReturnDebitNotePaymentAsync(db, pr, now, cancellationToken);

        if (mode == "cash_refund")
        {
            // Delete any SupplierLedger entry for this return
            var existingLedgers = await db.SupplierLedgers
                .Where(l => l.PurchaseReturnId == pr.Id)
                .ToListAsync(cancellationToken);
            if (existingLedgers.Count > 0)
            {
                db.SupplierLedgers.RemoveRange(existingLedgers);
            }

            var account = await ResolveOrEnsurePaymentAccountAsync(db, paymentMethod, explicitAccountId, now, cancellationToken);
            pr.SettlementMode = "cash_refund";
            pr.PaymentMethod = paymentMethod;
            pr.AccountId = account.Id;
            pr.CreditedAmount = 0m;
            pr.UpdatedAt = now;

            if (total > 0m)
            {
                account.CurrentBalance = Math.Round(account.CurrentBalance + total, 2);
                account.UpdatedAt = now;

                db.AccountTransactions.Add(new AccountTransaction
                {
                    AccountId = account.Id,
                    TransactionDate = pr.Date,
                    Direction = "in",
                    Amount = total,
                    BalanceAfter = account.CurrentBalance,
                    Category = "purchase_return_refund",
                    PaymentMethod = paymentMethod,
                    ReferenceType = "purchase_return",
                    ReferenceId = pr.Id,
                    ReferenceNumber = pr.PostedNumber,
                    CounterpartyName = supplier.Name,
                    Description = $"Supplier refund for purchase return #{pr.PostedNumber}",
                    CreatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            await SyncCashDrawerForPurchaseReturnAsync(
                db,
                pr,
                oldReturnSnapshot,
                newIsCashDrawer: paymentMethod == "cash" || account.SyncWithCashDrawer || account.AccountType == "cash_drawer",
                newCashAmount: total,
                updateDrawerIfPreShift,
                actorUserId,
                now,
                cancellationToken);
        }
        else
        {
            // 'credit_note' or 'invoice_offset' (Supplier Debit Note DN-)
            pr.SettlementMode = mode;
            pr.CreditedAmount = total;
            pr.AccountId = null;
            pr.UpdatedAt = now;

            // If edited from cash_refund -> credit_note, unwind cash drawer delta
            if (oldReturnSnapshot != null)
            {
                await SyncCashDrawerForPurchaseReturnAsync(
                    db,
                    pr,
                    oldReturnSnapshot,
                    newIsCashDrawer: false,
                    newCashAmount: 0m,
                    updateDrawerIfPreShift,
                    actorUserId,
                    now,
                    cancellationToken);
            }

            Payment? dnPayment = null;
            if (total > 0m)
            {
                var voucherNo = $"DN-{pr.PostedNumber}";
                dnPayment = new Payment
                {
                    VoucherNumber = voucherNo,
                    PartyType = "supplier",
                    SupplierId = pr.SupplierId,
                    PaymentDate = pr.Date,
                    Amount = total,
                    AllocatedAmount = 0m,
                    UnappliedAmount = total,
                    PaymentMethod = "credit_note",
                    AccountId = null,
                    ReferenceNumber = pr.PostedNumber,
                    Status = "posted",
                    Notes = $"Debit note from purchase return #{pr.PostedNumber}",
                    CreatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Payments.Add(dnPayment);
                await db.SaveChangesAsync(cancellationToken);

                // If invoice_offset (or credit_note linked to an unpaid credit invoice when requested), auto-allocate
                if (mode == "invoice_offset" && linkedInvoice != null)
                {
                    var invUnpaid = Math.Max(0m, Math.Round(linkedInvoice.TotalAmount - linkedInvoice.TotalPaid, 2));
                    var autoApply = Math.Min(total, invUnpaid);
                    if (autoApply > 0m)
                    {
                        db.PaymentAllocations.Add(new PaymentAllocation
                        {
                            PaymentId = dnPayment.Id,
                            InvoiceType = "purchase_invoice",
                            InvoiceId = linkedInvoice.Id,
                            AllocatedAmount = autoApply,
                            CreatedAt = now,
                            UpdatedAt = now
                        });

                        linkedInvoice.TotalPaid = Math.Round(linkedInvoice.TotalPaid + autoApply, 2);
                        linkedInvoice.UpdatedAt = now;

                        dnPayment.AllocatedAmount = autoApply;
                        dnPayment.UnappliedAmount = Math.Max(0m, Math.Round(total - autoApply, 2));
                        dnPayment.UpdatedAt = now;

                        affectedInvoiceIds.Add(linkedInvoice.Id);
                    }
                }
            }

            // Upsert SupplierLedger row for this PurchaseReturn
            var ledgerRow = await db.SupplierLedgers
                .FirstOrDefaultAsync(l => l.PurchaseReturnId == pr.Id, cancellationToken);

            var desc = $"{(mode == "invoice_offset" ? "Invoice Offset" : "Debit Note")} - Purchase Return #{pr.PostedNumber}";
            if (!string.IsNullOrWhiteSpace(pr.Remarks))
                desc += $" ({pr.Remarks})";

            if (ledgerRow == null)
            {
                ledgerRow = new SupplierLedger
                {
                    SupplierId = pr.SupplierId,
                    PurchaseReturnId = pr.Id,
                    PurchaseInvoiceId = pr.PurchaseInvoiceId,
                    PaymentId = dnPayment?.Id,
                    EntryType = "payment",
                    PostedNumber = pr.PostedNumber,
                    InvoiceNumber = linkedInvoice?.InvoiceNumber,
                    EntryDate = pr.Date,
                    PaymentReference = $"DN-{pr.PostedNumber}",
                    Description = desc,
                    InvoiceTotal = 0m,
                    TotalPaid = 0m,
                    DebitedAmount = total,
                    CreditRemaining = 0m,
                    CreatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.SupplierLedgers.Add(ledgerRow);
            }
            else
            {
                ledgerRow.SupplierId = pr.SupplierId;
                ledgerRow.PurchaseInvoiceId = pr.PurchaseInvoiceId;
                ledgerRow.PaymentId = dnPayment?.Id;
                ledgerRow.EntryType = "payment";
                ledgerRow.PostedNumber = pr.PostedNumber;
                ledgerRow.InvoiceNumber = linkedInvoice?.InvoiceNumber;
                ledgerRow.EntryDate = pr.Date;
                ledgerRow.PaymentReference = $"DN-{pr.PostedNumber}";
                ledgerRow.Description = desc;
                ledgerRow.InvoiceTotal = 0m;
                ledgerRow.TotalPaid = 0m;
                ledgerRow.DebitedAmount = total;
                ledgerRow.CreditRemaining = 0m;
                ledgerRow.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Sync any PurchaseInvoice ledger rows whose allocations changed
        foreach (var invId in affectedInvoiceIds.Distinct())
        {
            await SyncInvoiceLedgerRowAfterAllocationChangeAsync(db, invId, now, cancellationToken);
        }

        // Rebuild supplier running balance(s)
        await RebuildSingleSupplierLedgerAsync(db, pr.SupplierId, cancellationToken);
        if (oldReturnSnapshot != null && oldReturnSnapshot.OldSupplierId != pr.SupplierId)
        {
            await RebuildSingleSupplierLedgerAsync(db, oldReturnSnapshot.OldSupplierId, cancellationToken);
        }
    }

    private static async Task RemovePurchaseReturnSettlementAsync(
        KarobarDbContext db,
        PurchaseReturn pr,
        string supplierName,
        long? actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await UnwindAccountTransactionsForPurchaseReturnAsync(db, pr.Id, now, cancellationToken);

        var affectedInvoiceIds = await UnwindPurchaseReturnDebitNotePaymentAsync(db, pr, now, cancellationToken);

        var ledgers = await db.SupplierLedgers
            .Where(l => l.PurchaseReturnId == pr.Id)
            .ToListAsync(cancellationToken);
        if (ledgers.Count > 0)
        {
            db.SupplierLedgers.RemoveRange(ledgers);
        }

        // Reverse cash drawer inflow if this return had a cash_refund in the active session
        var oldMode = NormalizeSettlementMode(pr.SettlementMode);
        var oldMethod = NormalizePaymentMethod(pr.PaymentMethod);
        if (oldMode == "cash_refund" && oldMethod == "cash" && pr.Total > 0m)
        {
            var openSession = await db.CashDrawerSessions
                .Where(s => s.Status == "open")
                .OrderByDescending(s => s.OpenedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (openSession != null)
            {
                openSession.ExpectedClosingBalance = Math.Round(openSession.ExpectedClosingBalance - pr.Total, 2);
                openSession.UpdatedAt = now;

                db.CashDrawerTransactions.Add(new CashDrawerTransaction
                {
                    CashDrawerSessionId = openSession.Id,
                    Type = "cash_out",
                    Amount = Math.Round(pr.Total, 2),
                    ReferenceType = "purchase_return",
                    ReferenceId = pr.Id,
                    Description = $"Purchase Return Deleted ({pr.PostedNumber} - {supplierName})",
                    CreatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var invId in affectedInvoiceIds.Distinct())
        {
            await SyncInvoiceLedgerRowAfterAllocationChangeAsync(db, invId, now, cancellationToken);
        }

        await RebuildSingleSupplierLedgerAsync(db, pr.SupplierId, cancellationToken);
    }

    private static async Task UnwindAccountTransactionsForPurchaseReturnAsync(
        KarobarDbContext db,
        long purchaseReturnId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var txns = await db.AccountTransactions
            .Where(t => t.ReferenceType == "purchase_return" && t.ReferenceId == purchaseReturnId)
            .ToListAsync(cancellationToken);

        if (txns.Count == 0)
            return;

        foreach (var txn in txns)
        {
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == txn.AccountId, cancellationToken);
            if (acc != null)
            {
                if (txn.Direction == "in")
                    acc.CurrentBalance = Math.Round(acc.CurrentBalance - txn.Amount, 2);
                else
                    acc.CurrentBalance = Math.Round(acc.CurrentBalance + txn.Amount, 2);
                acc.UpdatedAt = now;
            }
        }

        db.AccountTransactions.RemoveRange(txns);
    }

    private static async Task<List<long>> UnwindPurchaseReturnDebitNotePaymentAsync(
        KarobarDbContext db,
        PurchaseReturn pr,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var affectedInvoiceIds = new List<long>();
        var voucherCode = $"DN-{pr.PostedNumber}";

        var existingPayments = await db.Payments
            .Where(p => p.VoucherNumber == voucherCode)
            .ToListAsync(cancellationToken);

        if (existingPayments.Count == 0)
            return affectedInvoiceIds;

        var paymentIds = existingPayments.Select(p => p.Id).ToList();
        var allocations = await db.PaymentAllocations
            .Where(pa => paymentIds.Contains(pa.PaymentId) && pa.InvoiceType == "purchase_invoice")
            .ToListAsync(cancellationToken);

        foreach (var alloc in allocations)
        {
            var inv = await db.PurchaseInvoices.FirstOrDefaultAsync(pi => pi.Id == alloc.InvoiceId, cancellationToken);
            if (inv != null)
            {
                inv.TotalPaid = Math.Max(0m, Math.Round(inv.TotalPaid - alloc.AllocatedAmount, 2));
                inv.UpdatedAt = now;
                affectedInvoiceIds.Add(inv.Id);
            }
        }

        if (allocations.Count > 0)
        {
            db.PaymentAllocations.RemoveRange(allocations);
        }

        // Null out PaymentId on any SupplierLedger row before deleting the Payment
        var linkedLedgerRows = await db.SupplierLedgers
            .Where(sl => sl.PaymentId.HasValue && paymentIds.Contains(sl.PaymentId.Value))
            .ToListAsync(cancellationToken);
        foreach (var lr in linkedLedgerRows)
        {
            lr.PaymentId = null;
        }

        db.Payments.RemoveRange(existingPayments);
        await db.SaveChangesAsync(cancellationToken);

        return affectedInvoiceIds;
    }

    private static async Task SyncInvoiceLedgerRowAfterAllocationChangeAsync(
        KarobarDbContext db,
        long invoiceId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var inv = await db.PurchaseInvoices.AsNoTracking()
            .FirstOrDefaultAsync(pi => pi.Id == invoiceId, cancellationToken);
        if (inv == null || !string.Equals(inv.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase))
            return;

        var allocatedSum = await db.PaymentAllocations.AsNoTracking()
            .Where(pa => pa.InvoiceType == "purchase_invoice" && pa.InvoiceId == invoiceId)
            .SumAsync(pa => (decimal?)pa.AllocatedAmount, cancellationToken) ?? 0m;

        var ledgerRow = await db.SupplierLedgers
            .FirstOrDefaultAsync(sl => sl.PurchaseInvoiceId == invoiceId, cancellationToken);
        if (ledgerRow != null)
        {
            var directPaid = Math.Max(0m, Math.Round(inv.TotalPaid - allocatedSum, 2));
            var directPaidOnInv = Math.Min(directPaid, inv.TotalAmount);
            ledgerRow.TotalPaid = directPaidOnInv;
            ledgerRow.DebitedAmount = Math.Round(allocatedSum, 2);
            ledgerRow.CreditRemaining = Math.Max(0m, Math.Round(inv.TotalAmount - directPaidOnInv - allocatedSum, 2));
            ledgerRow.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SyncCashDrawerForPurchaseReturnAsync(
        KarobarDbContext db,
        PurchaseReturn pr,
        OldReturnSettlementSnapshot? oldSnapshot,
        bool newIsCashDrawer,
        decimal newCashAmount,
        bool updateDrawerIfPreShift,
        long? actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var openSession = await db.CashDrawerSessions
            .Where(s => s.Status == "open")
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (openSession == null)
            return;

        if (oldSnapshot == null)
        {
            // CREATE
            if (newIsCashDrawer && newCashAmount > 0m)
            {
                openSession.ExpectedClosingBalance = Math.Round(openSession.ExpectedClosingBalance + newCashAmount, 2);
                openSession.UpdatedAt = now;

                db.CashDrawerTransactions.Add(new CashDrawerTransaction
                {
                    CashDrawerSessionId = openSession.Id,
                    Type = "cash_in",
                    Amount = newCashAmount,
                    ReferenceType = "purchase_return",
                    ReferenceId = pr.Id,
                    Description = $"Purchase Return Refund #{pr.PostedNumber}",
                    CreatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            return;
        }

        // UPDATE
        var isPreShift = oldSnapshot.CreatedAt < openSession.OpenedAt;
        if (isPreShift && !updateDrawerIfPreShift)
            return;

        var oldIsCashDrawer = oldSnapshot.OldSettlementMode == "cash_refund" && oldSnapshot.OldPaymentMethod == "cash";
        var oldCash = oldIsCashDrawer ? Math.Round(oldSnapshot.OldTotal, 2) : 0m;
        var nextCash = newIsCashDrawer ? Math.Round(newCashAmount, 2) : 0m;
        var delta = Math.Round(nextCash - oldCash, 2);

        if (Math.Abs(delta) <= 0.009m)
            return;

        openSession.ExpectedClosingBalance = Math.Round(openSession.ExpectedClosingBalance + delta, 2);
        openSession.UpdatedAt = now;

        db.CashDrawerTransactions.Add(new CashDrawerTransaction
        {
            CashDrawerSessionId = openSession.Id,
            Type = delta > 0m ? "cash_in" : "cash_out",
            Amount = Math.Abs(delta),
            ReferenceType = "purchase_return",
            ReferenceId = pr.Id,
            Description = $"Purchase Return Edited ({pr.PostedNumber}): {oldCash:N2} → {nextCash:N2}",
            CreatedBy = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static async Task<Account> ResolveOrEnsurePaymentAccountAsync(
        KarobarDbContext db,
        string paymentMethod,
        long? explicitAccountId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (explicitAccountId is > 0)
        {
            var accId = explicitAccountId.Value;
            var explicitAcc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accId && a.IsActive, cancellationToken);
            if (explicitAcc != null)
                return explicitAcc;
        }

        var expectedTypes = paymentMethod switch
        {
            "cash" => new[] { "cash_drawer", "cash" },
            "bank_transfer" or "card" or "cheque" => new[] { "bank" },
            "mobile_wallet" => new[] { "wallet", "mobile_wallet" },
            _ => new[] { "cash_drawer", "cash", "bank" }
        };

        var matched = await db.Accounts
            .Where(a => a.IsActive && expectedTypes.Contains(a.AccountType))
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (matched != null)
            return matched;

        var fallback = await db.Accounts
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (fallback != null)
            return fallback;

        var created = new Account
        {
            Name = "Cash in Hand (POS Drawer)",
            Code = "CASH-DRAWER",
            AccountType = "cash_drawer",
            ProviderKey = "cash_drawer",
            Currency = "PKR",
            IsDefault = true,
            IsActive = true,
            SyncWithCashDrawer = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Accounts.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    private static async Task RebuildSingleSupplierLedgerAsync(
        KarobarDbContext db,
        long supplierId,
        CancellationToken cancellationToken)
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
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string BuildAuditSnapshotJson(
        PurchaseReturn pr,
        string supplierName,
        IReadOnlyCollection<PurchaseReturnItem> items)
    {
        var payload = new
        {
            pr.Id,
            pr.PostedNumber,
            Date = pr.Date.ToString("yyyy-MM-dd"),
            pr.SupplierId,
            SupplierName = supplierName,
            pr.PurchaseInvoiceId,
            pr.SettlementMode,
            pr.PaymentMethod,
            pr.AccountId,
            pr.GrossTotal,
            pr.DiscountPercentage,
            pr.DiscountAmount,
            pr.TaxPercentage,
            pr.TaxAmount,
            pr.Total,
            pr.CreditedAmount,
            pr.Remarks,
            Items = items.Select(i => new
            {
                i.ProductId,
                i.Batch,
                Expiry = i.Expiry?.ToString("yyyy-MM-dd"),
                i.PackSize,
                i.PackPurchasedQuantity,
                i.ReturnPackQuantity,
                i.ReturnUnitQuantity,
                i.PackPurchasePrice,
                i.UnitPurchasePrice,
                i.ItemDiscountPercentage,
                i.SubTotal
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
