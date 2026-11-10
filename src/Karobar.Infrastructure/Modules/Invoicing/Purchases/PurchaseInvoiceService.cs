using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Karobar.Application.Interfaces;
using Karobar.Domain.Entities;
using Karobar.Domain.Formulas;
using Karobar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Services;

public sealed class PurchaseInvoiceService : IPurchaseInvoiceService
{
    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;
    private readonly IAuthService _authService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PurchaseInvoiceService(
        IDbContextFactory<KarobarDbContext> dbFactory,
        IAuthService authService)
    {
        _dbFactory = dbFactory;
        _authService = authService;
    }

    // =========================================================================
    // 1. BOOTSTRAP, LOOKUPS & DIRECTORY
    // =========================================================================

    public async Task<PurchaseFormBootstrapDto> GetFormBootstrapAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var nextCode = await ComputeNextPostedNumberInternalAsync(db, cancellationToken);
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        var storeSettings = new PurchaseStoreSettingsDto
        {
            StoreName = !string.IsNullOrWhiteSpace(setting?.StoreName) ? setting.StoreName! : "Karobar App",
            Address = setting?.Address ?? string.Empty,
            PhoneNumber = setting?.PhoneNumber ?? string.Empty,
            LicenseNumber = setting?.LicenseNumber ?? string.Empty,
            SaleSystem = !string.IsNullOrWhiteSpace(setting?.SaleSystem) ? setting.SaleSystem : "retail_wholesale",
            ShopType = !string.IsNullOrWhiteSpace(setting?.ShopType) ? setting.ShopType : "pharmacy",
            CurrencySymbol = !string.IsNullOrWhiteSpace(setting?.CurrencySymbol) ? setting.CurrencySymbol : "Rs.",
            PrinterType = !string.IsNullOrWhiteSpace(setting?.PrinterType) ? setting.PrinterType : "a4"
        };

        var suppliers = await BuildSupplierLookupsAsync(db, null, cancellationToken);

        // Ensure default cash account exists
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        var accountDtos = accounts.Select(a => new PurchaseAccountLookupDto
        {
            Id = a.Id,
            Name = a.Name,
            Code = a.Code,
            AccountType = a.AccountType,
            CurrentBalance = a.CurrentBalance,
            IsDefault = a.IsDefault,
            SyncWithCashDrawer = a.SyncWithCashDrawer
        }).ToList();

        var openSession = await db.CashDrawerSessions.AsNoTracking()
            .Where(s => s.Status == "open")
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var receivableOrders = await BuildReceivableOrdersAsync(db, cancellationToken);

        var heldDraftsCount = await db.InvoiceDrafts.AsNoTracking()
            .CountAsync(d =>
                d.InvoiceKind == "purchase" &&
                d.ConvertedInvoiceId == null &&
                d.DeletedAt == null,
                cancellationToken);

        return new PurchaseFormBootstrapDto
        {
            NextPostedNumber = nextCode,
            StoreSettings = storeSettings,
            Suppliers = suppliers,
            Accounts = accountDtos,
            ReceivableOrders = receivableOrders,
            HasOpenCashDrawer = openSession != null,
            OpenCashDrawerOpenedAt = openSession?.OpenedAt,
            OpenCashDrawerExpectedBalance = openSession?.ExpectedClosingBalance ?? 0m,
            HeldDraftsCount = heldDraftsCount
        };
    }

    public async Task<string> GenerateNextPostedNumberAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await ComputeNextPostedNumberInternalAsync(db, cancellationToken);
    }

    public async Task<PurchaseInvoiceDirectoryPageDto> GetDirectoryPageAsync(
        PurchaseInvoiceFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var supplierMap = await db.Suppliers.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var baseQuery = db.PurchaseInvoices.AsNoTracking().AsQueryable();

        if (query.SupplierId is > 0)
        {
            baseQuery = baseQuery.Where(pi => pi.SupplierId == query.SupplierId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.InvoiceType) &&
            !string.Equals(query.InvoiceType, "all", StringComparison.OrdinalIgnoreCase))
        {
            var typeFilter = query.InvoiceType.Trim().ToLowerInvariant();
            baseQuery = baseQuery.Where(pi => pi.InvoiceType.ToLower() == typeFilter);
        }

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            !string.Equals(query.Status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var statusFilter = query.Status.Trim().ToLowerInvariant();
            if (statusFilter == "reversed")
                baseQuery = baseQuery.Where(pi => pi.Status == "reversed");
            else
                baseQuery = baseQuery.Where(pi => pi.Status == null || pi.Status != "reversed");
        }

        if (query.FromDate.HasValue)
        {
            var fromDate = query.FromDate.Value.Date;
            baseQuery = baseQuery.Where(pi => pi.PostedDate >= fromDate);
        }

        if (query.ToDate.HasValue)
        {
            var toDateExclusive = query.ToDate.Value.Date.AddDays(1);
            baseQuery = baseQuery.Where(pi => pi.PostedDate < toDateExclusive);
        }

        if (!string.IsNullOrWhiteSpace(query.SupplierSearch))
        {
            var supTerm = query.SupplierSearch.Trim();
            var matchingSupIds = supplierMap
                .Where(kv => kv.Value.Contains(supTerm, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
            baseQuery = baseQuery.Where(pi => matchingSupIds.Contains(pi.SupplierId));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var matchingSupplierIds = supplierMap
                .Where(kv => kv.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();

            baseQuery = baseQuery.Where(pi =>
                EF.Functions.Like(pi.PostedNumber, $"%{term}%") ||
                (pi.InvoiceNumber != null && EF.Functions.Like(pi.InvoiceNumber, $"%{term}%")) ||
                matchingSupplierIds.Contains(pi.SupplierId));
        }

        var allFilteredHeaders = await baseQuery
            .OrderByDescending(pi => pi.Id)
            .Select(pi => new
            {
                pi.Id,
                pi.PostedNumber,
                pi.InvoiceNumber,
                pi.PostedDate,
                pi.DueDate,
                pi.SupplierId,
                pi.InvoiceType,
                pi.Status,
                pi.InvoiceAmount,
                pi.GrossTotal,
                pi.DiscountAmount,
                pi.TaxAmount,
                pi.TotalAmount,
                pi.TotalPaid,
                pi.PaymentMethod,
                pi.PurchaseOrderId,
                pi.ReversalReason,
                pi.ReversedAt
            })
            .ToListAsync(cancellationToken);

        int totalCount = allFilteredHeaders.Count;
        int pageSize = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, 200);
        int totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));
        int page = Math.Clamp(query.Page <= 0 ? 1 : query.Page, 1, totalPages);

        var activeRows = allFilteredHeaders
            .Where(h => !string.Equals(h.Status, "reversed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        decimal totalPostedAmt = activeRows.Sum(h => h.TotalAmount);
        decimal totalPaidAmt = activeRows.Sum(h =>
            string.Equals(h.InvoiceType, "debit", StringComparison.OrdinalIgnoreCase) && h.TotalPaid <= 0m
                ? h.TotalAmount
                : h.TotalPaid);
        decimal totalRemainingAmt = activeRows.Sum(h =>
            string.Equals(h.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0m, h.TotalAmount - h.TotalPaid)
                : 0m);

        var pageSlice = allFilteredHeaders
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var pageIds = pageSlice.Select(x => x.Id).ToList();
        var itemCounts = pageIds.Count > 0
            ? await db.PurchaseInvoiceItems.AsNoTracking()
                .Where(i => pageIds.Contains(i.PurchaseInvoiceId))
                .GroupBy(i => i.PurchaseInvoiceId)
                .Select(g => new { InvoiceId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.Count, cancellationToken)
            : new Dictionary<long, int>();

        var poIds = pageSlice.Where(x => x.PurchaseOrderId.HasValue).Select(x => x.PurchaseOrderId!.Value).Distinct().ToList();
        var poNumberMap = poIds.Count > 0
            ? await db.PurchaseOrders.AsNoTracking()
                .Where(po => poIds.Contains(po.Id))
                .ToDictionaryAsync(
                    po => po.Id,
                    po => !string.IsNullOrWhiteSpace(po.OrderNumber) ? po.OrderNumber : $"PO-{po.Id:D5}",
                    cancellationToken)
            : new Dictionary<long, string>();

        var heldDraftsCount = await db.InvoiceDrafts.AsNoTracking()
            .CountAsync(d =>
                d.InvoiceKind == "purchase" &&
                d.ConvertedInvoiceId == null &&
                d.DeletedAt == null,
                cancellationToken);

        var items = pageSlice.Select((h, idx) =>
        {
            var isCredit = string.Equals(h.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
            var effectivePaid = !isCredit && h.TotalPaid <= 0m ? h.TotalAmount : h.TotalPaid;
            var remaining = isCredit ? Math.Max(0m, Math.Round(h.TotalAmount - h.TotalPaid, 2)) : 0m;

            return new PurchaseInvoiceDirectoryRowDto
            {
                RowIndex = ((page - 1) * pageSize) + idx + 1,
                Id = h.Id,
                PostedNumber = h.PostedNumber,
                InvoiceNumber = h.InvoiceNumber ?? string.Empty,
                PostedDate = h.PostedDate,
                DueDate = h.DueDate,
                SupplierId = h.SupplierId,
                SupplierName = supplierMap.TryGetValue(h.SupplierId, out var sName) ? sName : $"Supplier #{h.SupplierId}",
                InvoiceType = string.IsNullOrWhiteSpace(h.InvoiceType) ? "debit" : h.InvoiceType,
                Status = string.IsNullOrWhiteSpace(h.Status) ? "posted" : h.Status,
                InvoiceAmount = h.InvoiceAmount > 0m ? h.InvoiceAmount : h.TotalAmount,
                GrossTotal = h.GrossTotal,
                DiscountAmount = h.DiscountAmount,
                TaxAmount = h.TaxAmount,
                TotalAmount = h.TotalAmount,
                TotalPaid = effectivePaid,
                RemainingAmount = remaining,
                PaymentMethod = h.PaymentMethod ?? "cash",
                ItemsCount = itemCounts.TryGetValue(h.Id, out var ic) ? ic : 0,
                PurchaseOrderId = h.PurchaseOrderId,
                PurchaseOrderNumber = h.PurchaseOrderId.HasValue && poNumberMap.TryGetValue(h.PurchaseOrderId.Value, out var poNum)
                    ? poNum
                    : null,
                ReversalReason = h.ReversalReason,
                ReversedAt = h.ReversedAt
            };
        }).ToList();

        return new PurchaseInvoiceDirectoryPageDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPostedCount = activeRows.Count,
            TotalPostedAmount = Math.Round(totalPostedAmt, 2),
            TotalPaidAmount = Math.Round(totalPaidAmt, 2),
            TotalRemainingPayable = Math.Round(totalRemainingAmt, 2),
            HeldDraftsCount = heldDraftsCount
        };
    }

    public async Task<PurchaseSupplierLookupDto?> GetSupplierCreditSummaryAsync(
        long supplierId,
        CancellationToken cancellationToken = default)
    {
        if (supplierId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var list = await BuildSupplierLookupsAsync(db, supplierId, cancellationToken);
        return list.FirstOrDefault();
    }

    public async Task<List<PurchaseProductSearchItemDto>> SearchProductsForPurchaseAsync(
        string? search = null,
        long? supplierId = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var safeLimit = Math.Clamp(limit, 1, 300);
        List<Product> products;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var termLower = term.ToLowerInvariant();

            // Search strictly by Product Name in exact typed character order (StartsWith, not Contains)
            products = (await db.Products.AsNoTracking()
                .Where(p => p.Name != null && p.Name.ToLower().StartsWith(termLower))
                .OrderBy(p => p.Name)
                .ThenBy(p => p.Id)
                .Take(safeLimit)
                .ToListAsync(cancellationToken))
                .Where(p => p.Name != null && p.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else
        {
            products = await db.Products.AsNoTracking()
                .OrderBy(p => p.Name)
                .ThenBy(p => p.Id)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);
        }

        return await BuildProductSearchDtosAsync(db, products, supplierId, cancellationToken);
    }

    public async Task<PurchaseProductSearchItemDto?> ScanProductByCodeOrBarcodeAsync(
        string codeOrBarcode,
        long? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        var code = codeOrBarcode?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(code))
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Barcode == code || p.ProductCode == code,
                cancellationToken);

        if (product == null)
            return null;

        var dtos = await BuildProductSearchDtosAsync(db, [product], supplierId, cancellationToken);
        return dtos.FirstOrDefault();
    }

    public async Task<bool> IsInvoiceNumberUniqueAsync(
        string invoiceNumber,
        long? excludeInvoiceId = null,
        CancellationToken cancellationToken = default)
    {
        var clean = invoiceNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(clean))
            return true;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var q = db.PurchaseInvoices.AsNoTracking()
            .Where(pi => pi.InvoiceNumber != null && pi.InvoiceNumber.ToLower() == clean.ToLower());

        if (excludeInvoiceId is > 0)
        {
            var exId = excludeInvoiceId.Value;
            q = q.Where(pi => pi.Id != exId);
        }

        return !await q.AnyAsync(cancellationToken);
    }

    // =========================================================================
    // 2. PURCHASE ORDER RECEIPT PRE-FILL
    // =========================================================================

    public async Task<PreparedPurchaseOrderReceiptDto> PreparePurchaseOrderReceiptAsync(
        long purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var order = await db.PurchaseOrders.AsNoTracking()
            .FirstOrDefaultAsync(po => po.Id == purchaseOrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Purchase Order #{purchaseOrderId} was not found.");

        var statusLower = (order.Status ?? "ordered").ToLowerInvariant();
        if (statusLower != "ordered" && statusLower != "partially_received" && statusLower != "draft" && statusLower != "sent")
        {
            throw new InvalidOperationException($"Purchase Order '{order.OrderNumber}' cannot be received in status '{order.Status}'.");
        }

        var supplier = await db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == order.SupplierId, cancellationToken);

        var poItems = await db.PurchaseOrderItems.AsNoTracking()
            .Where(i => i.PurchaseOrderId == order.Id)
            .OrderBy(i => i.Position)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

        // Exclude reversed invoices when summing already-received units
        var activeLinkedInvoices = await db.PurchaseInvoices.AsNoTracking()
            .Where(pi => pi.PurchaseOrderId == order.Id && (pi.Status == null || pi.Status != "reversed"))
            .Select(pi => pi.Id)
            .ToListAsync(cancellationToken);

        var receivedItems = activeLinkedInvoices.Count > 0
            ? await db.PurchaseInvoiceItems.AsNoTracking()
                .Where(i => activeLinkedInvoices.Contains(i.PurchaseInvoiceId))
                .ToListAsync(cancellationToken)
            : [];

        var savedPositions = await db.SupplierProductPositions.AsNoTracking()
            .Where(p => p.SupplierId == order.SupplierId)
            .ToDictionaryAsync(p => p.ProductId, p => p.Position, cancellationToken);

        var sortedPoItems = poItems
            .OrderBy(i => savedPositions.TryGetValue(i.ProductId, out var pos) ? pos : int.MaxValue)
            .ThenBy(i => i.Position)
            .ThenBy(i => i.Id)
            .ToList();

        var productIds = sortedPoItems.Select(i => i.ProductId).Distinct().ToList();
        var productEntities = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        var productDtoMap = (await BuildProductSearchDtosAsync(db, productEntities, order.SupplierId, cancellationToken))
            .ToDictionary(p => p.Id);

        var lines = new List<PurchaseInvoiceLineItemDto>();
        foreach (var poItem in sortedPoItems)
        {
            var matchingReceipts = receivedItems
                .Where(ri => ri.PurchaseOrderItemId == poItem.Id || (ri.PurchaseOrderItemId == null && ri.ProductId == poItem.ProductId))
                .ToList();

            var receivedPaid = matchingReceipts.Sum(ri => ri.UnitQuantity);
            var receivedBonus = matchingReceipts.Sum(ri => ri.UnitBonus);

            var remainingPaid = poItem.QuantityOrdered - receivedPaid;
            if (remainingPaid <= 0)
                continue;

            var remainingBonus = Math.Max(0, poItem.UnitBonus - receivedBonus);
            productDtoMap.TryGetValue(poItem.ProductId, out var prodDto);

            var packSize = poItem.PackSize > 0 ? poItem.PackSize : (prodDto?.PackSize > 0 ? prodDto.PackSize : 1);
            var unitPurchase = poItem.UnitCost > 0m ? poItem.UnitCost : (prodDto?.UnitPurchasePrice ?? 0m);
            var packPurchase = unitPurchase * packSize;
            var packSale = prodDto?.PackSalePrice ?? 0m;
            var unitSale = prodDto?.UnitSalePrice ?? 0m;
            var wsPack = prodDto?.WholeSalePackPrice > 0m ? prodDto.WholeSalePackPrice : packSale;
            var wsUnit = prodDto?.WholeSaleUnitPrice > 0m ? prodDto.WholeSaleUnitPrice : unitSale;
            var latestBatch = prodDto?.Batches.LastOrDefault();

            var calc = new PurchaseLineCalcModel
            {
                PackSize = packSize,
                UnitQuantity = remainingPaid,
                UnitBonus = remainingBonus,
                PackPurchasePrice = packPurchase,
                UnitPurchasePrice = unitPurchase,
                PackSalePrice = packSale,
                UnitSalePrice = unitSale,
                WholeSalePackPrice = wsPack,
                WholeSaleUnitPrice = wsUnit,
                ItemDiscountPercentage = poItem.DiscountPercentage
            };
            InvoiceCalculators.RecalcPurchaseItem(calc, "unit_quantity");
            InvoiceCalculators.RecalcPurchaseItem(calc, "unit_bonus");

            lines.Add(new PurchaseInvoiceLineItemDto
            {
                PurchaseOrderItemId = poItem.Id,
                ProductId = poItem.ProductId,
                ProductCode = prodDto?.ProductCode ?? string.Empty,
                ProductName = prodDto?.Name ?? $"Product #{poItem.ProductId}",
                BrandName = prodDto?.BrandName ?? string.Empty,
                Rack = prodDto?.Rack ?? string.Empty,
                StorageLocationId = prodDto?.StorageLocationId,
                PackSize = packSize,
                Batch = latestBatch?.BatchNumber ?? string.Empty,
                Expiry = latestBatch?.ExpiryDate,
                PackQuantity = calc.PackQuantity,
                UnitQuantity = calc.UnitQuantity,
                PackPurchasePrice = calc.PackPurchasePrice,
                UnitPurchasePrice = calc.UnitPurchasePrice,
                ItemDiscountPercentage = calc.ItemDiscountPercentage,
                PackBonus = calc.PackBonus,
                UnitBonus = calc.UnitBonus,
                PackSalePrice = calc.PackSalePrice,
                UnitSalePrice = calc.UnitSalePrice,
                WholeSalePackPrice = calc.WholeSalePackPrice,
                WholeSaleUnitPrice = calc.WholeSaleUnitPrice,
                Margin = calc.Margin,
                WholeSaleMargin = calc.WholeSaleMargin,
                AvgPrice = calc.AvgPrice,
                SubTotal = calc.SubTotal,
                Quantity = calc.Quantity,
                LineOrder = lines.Count,
                AvailableBatches = prodDto?.Batches ?? []
            });
        }

        if (lines.Count == 0)
        {
            throw new InvalidOperationException("This purchase order has no remaining items to receive.");
        }

        return new PreparedPurchaseOrderReceiptDto
        {
            PurchaseOrderId = order.Id,
            OrderNumber = !string.IsNullOrWhiteSpace(order.OrderNumber) ? order.OrderNumber : $"PO-{order.Id:D5}",
            SupplierId = order.SupplierId,
            SupplierName = supplier?.Name ?? string.Empty,
            ReceiptKey = Guid.NewGuid().ToString(),
            PriorDeliveriesCount = activeLinkedInvoices.Count,
            Lines = lines
        };
    }

    // =========================================================================
    // 3. DETAIL / SHOW & AUDIT HISTORY
    // =========================================================================

    public async Task<PurchaseInvoiceDetailDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var invoice = await db.PurchaseInvoices.AsNoTracking()
            .FirstOrDefaultAsync(pi => pi.Id == id, cancellationToken);
        if (invoice == null)
            return null;

        var supplier = await db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == invoice.SupplierId, cancellationToken);

        Account? account = null;
        if (invoice.AccountId.HasValue)
        {
            account = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == invoice.AccountId.Value, cancellationToken);
        }

        PurchaseOrder? po = null;
        if (invoice.PurchaseOrderId.HasValue)
        {
            po = await db.PurchaseOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == invoice.PurchaseOrderId.Value, cancellationToken);
        }

        User? reversedByUser = null;
        if (invoice.ReversedBy.HasValue)
        {
            reversedByUser = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == invoice.ReversedBy.Value, cancellationToken);
        }

        var rawItems = await db.PurchaseInvoiceItems.AsNoTracking()
            .Where(i => i.PurchaseInvoiceId == invoice.Id)
            .OrderBy(i => i.LineOrder)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

        var productIds = rawItems.Select(i => i.ProductId).Distinct().ToList();
        var productEntities = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        var productDtoMap = (await BuildProductSearchDtosAsync(db, productEntities, invoice.SupplierId, cancellationToken))
            .ToDictionary(p => p.Id);

        // Compute ReturnableQuantity (ReturnEntitlement parity)
        var linkedReturnIds = await db.PurchaseReturns.AsNoTracking()
            .Where(pr => pr.PurchaseInvoiceId == invoice.Id)
            .Select(pr => pr.Id)
            .ToListAsync(cancellationToken);

        var returnLines = linkedReturnIds.Count > 0
            ? await db.PurchaseReturnItems.AsNoTracking()
                .Where(pri => linkedReturnIds.Contains(pri.PurchaseReturnId))
                .ToListAsync(cancellationToken)
            : [];

        var returnableByItemId = ComputeReturnableQuantities(rawItems, returnLines);

        // Allocated payment vouchers sum
        var allocatedAmount = await (
            from pa in db.PaymentAllocations.AsNoTracking()
            join p in db.Payments.AsNoTracking() on pa.PaymentId equals p.Id
            where pa.InvoiceType == "purchase_invoice" &&
                  pa.InvoiceId == invoice.Id &&
                  p.Status == "posted"
            select (decimal?)pa.AllocatedAmount
        ).SumAsync(cancellationToken) ?? 0m;

        var lineDtos = rawItems.Select(item =>
        {
            productDtoMap.TryGetValue(item.ProductId, out var prodDto);
            return new PurchaseInvoiceLineItemDto
            {
                Id = item.Id,
                PurchaseOrderItemId = item.PurchaseOrderItemId,
                ProductId = item.ProductId,
                ProductCode = prodDto?.ProductCode ?? string.Empty,
                ProductName = prodDto?.Name ?? $"Product #{item.ProductId}",
                BrandName = prodDto?.BrandName ?? string.Empty,
                Rack = prodDto?.Rack ?? string.Empty,
                StorageLocationId = item.StorageLocationId ?? prodDto?.StorageLocationId,
                PackSize = item.PackSize > 0 ? item.PackSize : 1,
                Batch = item.Batch ?? string.Empty,
                Expiry = item.Expiry,
                PackQuantity = item.PackQuantity,
                UnitQuantity = item.UnitQuantity,
                PackPurchasePrice = item.PackPurchasePrice,
                UnitPurchasePrice = item.UnitPurchasePrice,
                ItemDiscountPercentage = item.ItemDiscountPercentage,
                PackBonus = item.PackBonus,
                UnitBonus = item.UnitBonus,
                PackSalePrice = item.PackSalePrice,
                UnitSalePrice = item.UnitSalePrice,
                WholeSalePackPrice = item.WholeSalePackPrice ?? item.PackSalePrice,
                WholeSaleUnitPrice = item.WholeSaleUnitPrice ?? item.UnitSalePrice,
                Margin = item.Margin,
                WholeSaleMargin = item.WholeSaleMargin,
                AvgPrice = item.AvgPrice,
                SubTotal = item.SubTotal,
                Quantity = item.Quantity,
                ReturnableQuantity = returnableByItemId.TryGetValue(item.Id, out var rq) ? rq : item.Quantity,
                LineOrder = item.LineOrder,
                AvailableBatches = prodDto?.Batches ?? []
            };
        }).ToList();

        var histories = await GetDocumentHistoryInternalAsync(db, invoice.Id, cancellationToken);

        var isCredit = string.Equals(invoice.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
        var effectiveTotalPaid = !isCredit && invoice.TotalPaid <= 0m ? invoice.TotalAmount : invoice.TotalPaid;

        return new PurchaseInvoiceDetailDto
        {
            Id = invoice.Id,
            PostedNumber = invoice.PostedNumber,
            PostedDate = invoice.PostedDate,
            CreatedAt = invoice.CreatedAt,
            DueDate = invoice.DueDate,
            CreditDays = invoice.CreditDays ?? supplier?.CreditDays,
            SupplierId = invoice.SupplierId,
            SupplierName = supplier?.Name ?? $"Supplier #{invoice.SupplierId}",
            SupplierPhone = supplier?.Phone ?? string.Empty,
            SupplierExpiryPolicyLabel = FormatExpiryPolicyLabel(
                supplier?.ExpiryReturnPolicy,
                supplier?.ExpiryReturnMonths,
                supplier?.ExpiryReturnDays),
            PurchaseOrderId = invoice.PurchaseOrderId,
            PurchaseOrderNumber = po != null
                ? (!string.IsNullOrWhiteSpace(po.OrderNumber) ? po.OrderNumber : $"PO-{po.Id:D5}")
                : null,
            PurchaseOrderStatus = po?.Status,
            ReceiptKey = invoice.ReceiptKey,
            InvoiceNumber = invoice.InvoiceNumber ?? string.Empty,
            InvoiceType = string.IsNullOrWhiteSpace(invoice.InvoiceType) ? "debit" : invoice.InvoiceType,
            Status = string.IsNullOrWhiteSpace(invoice.Status) ? "posted" : invoice.Status,
            ReversedAt = invoice.ReversedAt,
            ReversedByName = reversedByUser?.Name ?? reversedByUser?.Username,
            ReversalReason = invoice.ReversalReason,
            Remarks = invoice.Remarks ?? string.Empty,
            InvoiceAmount = invoice.InvoiceAmount > 0m ? invoice.InvoiceAmount : invoice.TotalAmount,
            GrossTotal = invoice.GrossTotal,
            DiscountPercentage = invoice.DiscountPercentage,
            DiscountAmount = invoice.DiscountAmount,
            TaxPercentage = invoice.TaxPercentage,
            TaxAmount = invoice.TaxAmount,
            TotalAmount = invoice.TotalAmount,
            TotalPaid = effectiveTotalPaid,
            AllocatedPaymentsAmount = Math.Round(allocatedAmount, 2),
            PaymentMethod = invoice.PaymentMethod ?? "cash",
            AccountId = invoice.AccountId,
            AccountName = account?.Name ?? "Default Cash Account",
            Items = lineDtos,
            Histories = histories
        };
    }

    public async Task<List<PurchaseDocumentHistoryEntryDto>> GetDocumentHistoryAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await GetDocumentHistoryInternalAsync(db, id, cancellationToken);
    }

    // =========================================================================
    // 4. TRANSACTIONAL CREATE, UPDATE, REVERSE & DELETE
    // =========================================================================

    public async Task<PurchaseInvoiceOperationResult> CreateAsync(
        PurchaseInvoiceUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. Draft Idempotency Check
            InvoiceDraft? linkedDraft = null;
            if (request.DraftId is > 0)
            {
                var draftIdVal = request.DraftId.Value;
                linkedDraft = await db.InvoiceDrafts.FirstOrDefaultAsync(d => d.Id == draftIdVal, cancellationToken);
                if (linkedDraft?.ConvertedInvoiceId is > 0)
                {
                    var existingInv = await db.PurchaseInvoices.AsNoTracking()
                        .FirstOrDefaultAsync(pi => pi.Id == linkedDraft.ConvertedInvoiceId.Value, cancellationToken);
                    if (existingInv != null)
                    {
                        await tx.CommitAsync(cancellationToken);
                        return new PurchaseInvoiceOperationResult
                        {
                            Succeeded = true,
                            InvoiceId = existingInv.Id,
                            PostedNumber = existingInv.PostedNumber,
                            Message = $"Purchase invoice {existingInv.PostedNumber} was already posted from this draft."
                        };
                    }
                }
            }

            // 2. Validate & Normalize Request
            var validation = await ValidateAndNormalizePurchaseRequestAsync(
                db,
                request,
                excludeInvoiceId: null,
                allocatedOnInvoice: 0m,
                cancellationToken);

            if (!validation.IsValid)
            {
                return validation.FailureResult!;
            }

            var supplier = validation.Supplier!;
            var normalizedLines = validation.NormalizedLines;
            var footer = validation.Footer;
            var invoiceType = validation.InvoiceType;
            var totalPaid = validation.ResolvedTotalPaid;
            var dueDate = validation.ResolvedDueDate;
            var invoiceAmount = validation.ResolvedInvoiceAmount;
            var now = DateTime.UtcNow;

            // 3. Purchase Order Validation & Line Mapping
            PurchaseOrder? order = null;
            if (request.PurchaseOrderId is > 0)
            {
                var poId = request.PurchaseOrderId.Value;
                order = await db.PurchaseOrders.FirstOrDefaultAsync(po => po.Id == poId, cancellationToken);
                if (order == null)
                    return PurchaseInvoiceOperationResult.Fail("Linked Purchase Order was not found.");

                var poStatus = (order.Status ?? "ordered").ToLowerInvariant();
                if (poStatus == "cancelled" || poStatus == "received")
                    return PurchaseInvoiceOperationResult.Fail($"Purchase Order '{order.OrderNumber}' is already '{order.Status}' and cannot receive new deliveries.");

                if (!string.IsNullOrWhiteSpace(request.ReceiptKey))
                {
                    var keyTrim = request.ReceiptKey.Trim();
                    var duplicateReceipt = await db.PurchaseInvoices.AnyAsync(pi => pi.ReceiptKey == keyTrim, cancellationToken);
                    if (duplicateReceipt)
                        return PurchaseInvoiceOperationResult.Fail("This Purchase Order receipt has already been posted.");
                }

                if (normalizedLines.Sum(l => l.Calc.Quantity) <= 0)
                    return PurchaseInvoiceOperationResult.Fail("Receive at least one unit before posting the purchase invoice.");

                var poItemsByProduct = await db.PurchaseOrderItems
                    .Where(i => i.PurchaseOrderId == order.Id)
                    .ToListAsync(cancellationToken);
                var poItemLookup = poItemsByProduct
                    .GroupBy(i => i.ProductId)
                    .ToDictionary(g => g.Key, g => g.First().Id);

                foreach (var nl in normalizedLines)
                {
                    nl.PurchaseOrderItemId = poItemLookup.TryGetValue(nl.Product.Id, out var poiId) ? poiId : null;
                }
            }

            // 4. Generate Atomic PostedNumber (PRINV-0001)
            var nextPostedNumber = await ComputeNextPostedNumberInternalAsync(db, cancellationToken);

            // 5. Insert PurchaseInvoice Header
            var invoice = new PurchaseInvoice
            {
                SupplierId = supplier.Id,
                PurchaseOrderId = order?.Id,
                ReceiptKey = order != null
                    ? (!string.IsNullOrWhiteSpace(request.ReceiptKey) ? request.ReceiptKey.Trim() : Guid.NewGuid().ToString())
                    : null,
                PostedNumber = nextPostedNumber,
                PostedDate = request.PostedDate.Date,
                InvoiceDate = request.PostedDate.Date,
                DueDate = dueDate,
                CreditDays = supplier.CreditDays ?? 30,
                InvoiceNumber = request.InvoiceNumber.Trim(),
                InvoiceType = invoiceType,
                Status = "posted",
                Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
                InvoiceAmount = invoiceAmount,
                GrossTotal = footer.RowsTotal,
                DiscountPercentage = footer.DiscountPercentage,
                DiscountAmount = footer.DiscountAmount,
                TaxPercentage = footer.TaxPercentage,
                TaxAmount = footer.TaxAmount,
                TotalAmount = footer.TotalAmount,
                TotalPaid = totalPaid,
                PaymentMethod = validation.PaymentMethod,
                AccountId = request.AccountId,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.PurchaseInvoices.Add(invoice);
            await db.SaveChangesAsync(cancellationToken);

            // 6. Insert Line Items + Update Stock, Moving Weighted Average Cost, Batches, ProductSuppliers, UserDemands
            var createdItems = new List<PurchaseInvoiceItem>(normalizedLines.Count);
            for (int idx = 0; idx < normalizedLines.Count; idx++)
            {
                var nl = normalizedLines[idx];
                var entityItem = new PurchaseInvoiceItem
                {
                    PurchaseInvoiceId = invoice.Id,
                    PurchaseOrderItemId = nl.PurchaseOrderItemId,
                    ProductId = nl.Product.Id,
                    StorageLocationId = nl.StorageLocationId ?? nl.Product.StorageLocationId,
                    Batch = string.IsNullOrWhiteSpace(nl.Batch) ? null : nl.Batch,
                    Expiry = nl.Expiry?.Date,
                    PackSize = nl.Calc.PackSize,
                    PackQuantity = Math.Round(nl.Calc.PackQuantity, 2, MidpointRounding.AwayFromZero),
                    UnitQuantity = nl.Calc.UnitQuantity,
                    PackPurchasePrice = Math.Round(nl.Calc.PackPurchasePrice, 2, MidpointRounding.AwayFromZero),
                    UnitPurchasePrice = Math.Round(nl.Calc.UnitPurchasePrice, 4, MidpointRounding.AwayFromZero),
                    PackSalePrice = Math.Round(nl.Calc.PackSalePrice, 2, MidpointRounding.AwayFromZero),
                    UnitSalePrice = Math.Round(nl.Calc.UnitSalePrice, 4, MidpointRounding.AwayFromZero),
                    WholeSalePackPrice = Math.Round(nl.Calc.WholeSalePackPrice, 2, MidpointRounding.AwayFromZero),
                    WholeSaleUnitPrice = Math.Round(nl.Calc.WholeSaleUnitPrice, 4, MidpointRounding.AwayFromZero),
                    PackBonus = Math.Round(nl.Calc.PackBonus, 2, MidpointRounding.AwayFromZero),
                    UnitBonus = nl.Calc.UnitBonus,
                    ItemDiscountPercentage = Math.Round(nl.Calc.ItemDiscountPercentage, 2, MidpointRounding.AwayFromZero),
                    Margin = nl.Calc.Margin,
                    WholeSaleMargin = nl.Calc.WholeSaleMargin,
                    AvgPrice = Math.Round(nl.Calc.AvgPrice, 2, MidpointRounding.AwayFromZero),
                    SubTotal = nl.Calc.SubTotal,
                    Quantity = nl.Calc.Quantity,
                    LineOrder = idx,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                db.PurchaseInvoiceItems.Add(entityItem);
                createdItems.Add(entityItem);

                ApplyPurchaseToProduct(nl.Product, nl.Calc, now);
                await UpsertBatchOnReceiveAsync(db, nl.Product, nl.Batch, nl.Expiry, nl.StorageLocationId, nl.Calc, now, cancellationToken);
                await SyncProductSupplierLinkAsync(db, nl.Product.Id, supplier.Id, nl.Calc, now, cancellationToken);
            }

            // Fulfill pending/ordered user demands for received products
            var fulfilledProdIds = normalizedLines
                .Where(l => l.Calc.Quantity > 0)
                .Select(l => l.Product.Id)
                .Distinct()
                .ToList();

            if (fulfilledProdIds.Count > 0)
            {
                var openDemands = await db.UserDemands
                    .Where(ud => ud.ProductId != null &&
                                 fulfilledProdIds.Contains(ud.ProductId.Value) &&
                                 (ud.Status == "pending" || ud.Status == "ordered"))
                    .ToListAsync(cancellationToken);
                foreach (var demand in openDemands)
                {
                    demand.Status = "fulfilled";
                    demand.UpdatedAt = now;
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            // 7. Purchase Order Sync & Supplier Product Sorting
            if (order != null)
            {
                await SyncPurchaseOrderReceivingAsync(db, order, effectiveActorId, now, cancellationToken);
                await RememberSupplierProductSortingAsync(
                    db,
                    supplier.Id,
                    normalizedLines.Select(l => l.Product.Id).ToList(),
                    now,
                    cancellationToken);
            }

            // 8. Supplier Ledger (Credit Invoices + Excess Advance Payment)
            if (invoiceType == "credit")
            {
                await CreateOrSyncSupplierLedgerForCreditInvoiceAsync(
                    db,
                    invoice,
                    allocatedOnInvoice: 0m,
                    effectiveActorId,
                    now,
                    cancellationToken);
                await RebuildSingleSupplierLedgerAsync(db, supplier.Id, cancellationToken);
            }

            // 9. Financial Account & Cash Drawer Outflow
            var cashPaid = invoiceType == "debit" && totalPaid <= 0m ? footer.TotalAmount : totalPaid;
            if (cashPaid > 0m)
            {
                var account = await ResolveOrEnsurePaymentAccountAsync(db, validation.PaymentMethod, request.AccountId, now, cancellationToken);
                invoice.AccountId = account.Id;

                await RecordAccountWithdrawalAsync(
                    db,
                    account,
                    cashPaid,
                    invoice.PostedDate,
                    validation.PaymentMethod,
                    "purchase_invoice",
                    invoice.Id,
                    invoice.PostedNumber,
                    supplier.Name,
                    $"Purchase Invoice {invoice.PostedNumber} ({supplier.Name})",
                    effectiveActorId,
                    now);

                if (IsCashDrawerLinkedAccount(account, validation.PaymentMethod))
                {
                    var openSession = await db.CashDrawerSessions
                        .Where(s => s.Status == "open")
                        .OrderByDescending(s => s.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (openSession != null)
                    {
                        db.CashDrawerTransactions.Add(new CashDrawerTransaction
                        {
                            CashDrawerSessionId = openSession.Id,
                            Type = "cash_purchase",
                            Amount = cashPaid,
                            ReferenceType = "purchase_invoice",
                            ReferenceId = invoice.Id,
                            Description = $"Cash Purchase {invoice.PostedNumber} - {supplier.Name}",
                            CreatedBy = effectiveActorId,
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                        openSession.ExpectedClosingBalance = Math.Round(openSession.ExpectedClosingBalance - cashPaid, 2);
                        openSession.UpdatedAt = now;
                    }
                }
            }

            // 10. Mark Held Draft Converted
            if (linkedDraft != null)
            {
                linkedDraft.ConvertedInvoiceId = invoice.Id;
                linkedDraft.ConvertedAt = now;
                linkedDraft.UpdatedAt = now;
            }

            // 11. Record Document Audit History
            var afterSnapshot = BuildAuditSnapshotJson(invoice, supplier.Name, normalizedLines.Select(l => (l.Product.Name, l.Batch, l.Expiry, l.Calc)).ToList());
            db.DocumentHistories.Add(new DocumentHistory
            {
                DocumentType = "purchase_invoice",
                DocumentId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Action = "created",
                UserId = effectiveActorId,
                Reason = "Purchase invoice created",
                BeforeSnapshot = null,
                AfterSnapshot = afterSnapshot,
                ChangesSummary = $"Created {invoice.PostedNumber} ({invoiceType.ToUpperInvariant()}) for {supplier.Name} — Net Total: {invoice.TotalAmount:N2}, Items: {createdItems.Count}",
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new PurchaseInvoiceOperationResult
            {
                Succeeded = true,
                InvoiceId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Message = $"Purchase invoice {invoice.PostedNumber} saved and posted successfully."
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return PurchaseInvoiceOperationResult.Fail($"Failed to create purchase invoice: {ex.Message}");
        }
    }

    public async Task<PurchaseInvoiceOperationResult> UpdateAsync(
        long id,
        PurchaseInvoiceUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var invoice = await db.PurchaseInvoices
                .FirstOrDefaultAsync(pi => pi.Id == id, cancellationToken);
            if (invoice == null)
                return PurchaseInvoiceOperationResult.Fail($"Purchase invoice #{id} was not found.");

            if (string.Equals(invoice.Status, "reversed", StringComparison.OrdinalIgnoreCase))
                return PurchaseInvoiceOperationResult.Fail("Cannot edit an invoice that has been reversed.");

            // Check allocated payments BEFORE mutating header (fixing web app bug #5!)
            var allocatedOnInvoice = await (
                from pa in db.PaymentAllocations
                join p in db.Payments on pa.PaymentId equals p.Id
                where pa.InvoiceType == "purchase_invoice" &&
                      pa.InvoiceId == invoice.Id &&
                      p.Status == "posted"
                select (decimal?)pa.AllocatedAmount
            ).SumAsync(cancellationToken) ?? 0m;

            var oldSupplierId = invoice.SupplierId;
            var oldSupplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == oldSupplierId, cancellationToken);

            if (allocatedOnInvoice > 0.009m)
            {
                if (request.SupplierId != oldSupplierId)
                {
                    return PurchaseInvoiceOperationResult.Fail(
                        "Cannot change supplier because payment voucher(s) are already allocated to this invoice.",
                        fieldName: "supplier_id");
                }
                if (!string.Equals(request.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase))
                {
                    return PurchaseInvoiceOperationResult.Fail(
                        "Cannot switch invoice type from Credit to Debit because payment voucher(s) are allocated to this invoice.",
                        fieldName: "invoice_type");
                }
            }

            // Validate & Normalize Request
            var validation = await ValidateAndNormalizePurchaseRequestAsync(
                db,
                request,
                excludeInvoiceId: invoice.Id,
                allocatedOnInvoice: allocatedOnInvoice,
                cancellationToken);

            if (!validation.IsValid)
                return validation.FailureResult!;

            var newSupplier = validation.Supplier!;
            var normalizedLines = validation.NormalizedLines;
            var footer = validation.Footer;
            var newInvoiceType = validation.InvoiceType;
            var newTotalPaid = validation.ResolvedTotalPaid;
            var newDueDate = validation.ResolvedDueDate;
            var newInvoiceAmount = validation.ResolvedInvoiceAmount;

            // Pre-Shift Cash Drawer Prompt Check (PreShiftDrawerPromptModal parity)
            var oldIsCredit = string.Equals(invoice.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
            var oldDirectPaid = !oldIsCredit && invoice.TotalPaid <= 0m
                ? invoice.TotalAmount
                : Math.Max(0m, Math.Round(invoice.TotalPaid - allocatedOnInvoice, 2));

            var newDirectPaid = newInvoiceType == "debit" && newTotalPaid <= 0m
                ? footer.TotalAmount
                : Math.Max(0m, Math.Round(newTotalPaid - allocatedOnInvoice, 2));

            var openSession = await db.CashDrawerSessions
                .Where(s => s.Status == "open")
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var oldWasCash = string.Equals(invoice.PaymentMethod ?? "cash", "cash", StringComparison.OrdinalIgnoreCase);
            var newIsCash = string.Equals(validation.PaymentMethod, "cash", StringComparison.OrdinalIgnoreCase);
            var oldCashLeg = oldWasCash ? oldDirectPaid : 0m;
            var newCashLeg = newIsCash ? newDirectPaid : 0m;

            if (openSession != null &&
                invoice.CreatedAt.HasValue &&
                openSession.OpenedAt > invoice.CreatedAt.Value &&
                Math.Abs(newCashLeg - oldCashLeg) > 0.009m &&
                request.UpdatePreShiftDrawer == null)
            {
                await tx.RollbackAsync(cancellationToken);
                return new PurchaseInvoiceOperationResult
                {
                    Succeeded = false,
                    RequiresPreShiftDrawerChoice = true,
                    Message = "This invoice was created before the current Cash Drawer shift opened. Choose whether to adjust the current Cash Drawer balance."
                };
            }

            var oldItems = await db.PurchaseInvoiceItems
                .Where(i => i.PurchaseInvoiceId == invoice.Id)
                .OrderBy(i => i.LineOrder)
                .ThenBy(i => i.Id)
                .ToListAsync(cancellationToken);

            var allProductIds = oldItems.Select(i => i.ProductId)
                .Concat(normalizedLines.Select(l => l.Product.Id))
                .Distinct()
                .ToList();

            var trackedProducts = await db.Products
                .Where(p => allProductIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            // Capture Before Snapshot
            var beforeSnapshot = BuildAuditSnapshotJsonFromEntities(
                invoice,
                oldSupplier?.Name ?? string.Empty,
                oldItems,
                trackedProducts);

            // Linked Purchase Order validation
            PurchaseOrder? order = null;
            if (invoice.PurchaseOrderId is > 0)
            {
                var poId = invoice.PurchaseOrderId.Value;
                order = await db.PurchaseOrders.FirstOrDefaultAsync(po => po.Id == poId, cancellationToken);
                if (order != null)
                {
                    if (string.Equals(order.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                        return PurchaseInvoiceOperationResult.Fail("Cannot edit a receipt linked to a cancelled Purchase Order.");

                    if (normalizedLines.Sum(l => l.Calc.Quantity) <= 0)
                        return PurchaseInvoiceOperationResult.Fail("Receive at least one unit before updating this purchase order receipt.");

                    var poItems = await db.PurchaseOrderItems
                        .Where(i => i.PurchaseOrderId == order.Id)
                        .ToListAsync(cancellationToken);
                    var poItemLookup = poItems
                        .GroupBy(i => i.ProductId)
                        .ToDictionary(g => g.Key, g => g.First().Id);

                    foreach (var nl in normalizedLines)
                    {
                        nl.PurchaseOrderItemId = poItemLookup.TryGetValue(nl.Product.Id, out var poiId) ? poiId : null;
                    }
                }
            }

            var now = DateTime.UtcNow;

            // Group old and new items by stock bucket: ProductId | Batch | Expiry | PackSize
            var oldBuckets = oldItems
                .GroupBy(i => MakeStockBucketKey(i.ProductId, i.Batch, i.Expiry, i.PackSize))
                .ToDictionary(g => g.Key, g => new
                {
                    First = g.First(),
                    Qty = g.Sum(x => x.Quantity),
                    TotalCost = g.Sum(x => x.Quantity * x.AvgPrice)
                });

            var newBuckets = normalizedLines
                .GroupBy(l => MakeStockBucketKey(l.Product.Id, l.Batch, l.Expiry, l.Calc.PackSize))
                .ToDictionary(g => g.Key, g => new
                {
                    First = g.First(),
                    Qty = g.Sum(x => x.Calc.Quantity),
                    TotalCost = g.Sum(x => x.Calc.Quantity * x.Calc.EffectiveAvgPrice4)
                });

            var unionKeys = oldBuckets.Keys.Union(newBuckets.Keys).ToList();

            // First validate all negative stock deltas before mutating
            foreach (var key in unionKeys)
            {
                var oldQty = oldBuckets.TryGetValue(key, out var ob) ? ob.Qty : 0;
                var newQty = newBuckets.TryGetValue(key, out var nb) ? nb.Qty : 0;
                var delta = newQty - oldQty;
                if (delta >= 0)
                    continue;

                var pid = ob!.First.ProductId;
                if (!trackedProducts.TryGetValue(pid, out var prod))
                    continue;

                if (prod.Quantity + delta < 0)
                {
                    return PurchaseInvoiceOperationResult.Fail(
                        $"Cannot reduce quantity for '{prod.Name}': only {prod.Quantity} units in stock, cannot deduct {Math.Abs(delta)} units.");
                }

                var batchNo = (ob.First.Batch ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(batchNo) && ob.First.Expiry.HasValue)
                {
                    var expDate = ob.First.Expiry.Value.Date;
                    var existingBatch = await FindTrackedBatchAsync(db, pid, batchNo, expDate, cancellationToken);

                    if (existingBatch != null && existingBatch.Quantity + delta < 0)
                    {
                        return PurchaseInvoiceOperationResult.Fail(
                            $"Cannot reduce batch '{batchNo}' for '{prod.Name}': only {existingBatch.Quantity} units left in stock, cannot deduct {Math.Abs(delta)} units.");
                    }
                }
            }

            // Apply stock & weighted average cost deltas
            foreach (var key in unionKeys)
            {
                oldBuckets.TryGetValue(key, out var ob);
                newBuckets.TryGetValue(key, out var nb);

                var oldQty = ob?.Qty ?? 0;
                var newQty = nb?.Qty ?? 0;
                var delta = newQty - oldQty;
                var pid = nb?.First.Product.Id ?? ob!.First.ProductId;

                if (!trackedProducts.TryGetValue(pid, out var prod))
                    continue;

                var oldGroupAvg = oldQty > 0 ? Math.Round(ob!.TotalCost / oldQty, 4) : 0m;
                var newGroupAvg = newQty > 0 ? Math.Round(nb!.TotalCost / newQty, 4) : 0m;

                if (delta > 0)
                {
                    prod.AvgPrice = InvoiceCalculators.ApplyPurchaseWeightedAverageCost(
                        prod.Quantity,
                        prod.AvgPrice,
                        delta,
                        newGroupAvg);
                    prod.Quantity += delta;

                    if (nb != null)
                    {
                        await AdjustBatchQuantityOnUpdateAsync(
                            db,
                            prod,
                            nb.First.Batch,
                            nb.First.Expiry,
                            nb.First.StorageLocationId,
                            delta,
                            nb.First.Calc,
                            now,
                            cancellationToken);
                    }
                }
                else if (delta < 0)
                {
                    prod.AvgPrice = InvoiceCalculators.RevertPurchaseWeightedAverageCost(
                        prod.Quantity,
                        prod.AvgPrice,
                        Math.Abs(delta),
                        oldGroupAvg);
                    prod.Quantity = Math.Max(0, prod.Quantity + delta);

                    if (ob != null)
                    {
                        await AdjustBatchQuantityOnUpdateAsync(
                            db,
                            prod,
                            ob.First.Batch,
                            ob.First.Expiry,
                            ob.First.StorageLocationId,
                            delta,
                            nb?.First.Calc,
                            now,
                            cancellationToken);
                    }
                }
                else if (oldQty > 0 && Math.Abs(newGroupAvg - oldGroupAvg) > 0.0001m)
                {
                    // Fix web app quirk #3: when quantity is unchanged but cost/discount changed, update moving avg!
                    var revertedAvg = InvoiceCalculators.RevertPurchaseWeightedAverageCost(
                        prod.Quantity,
                        prod.AvgPrice,
                        oldQty,
                        oldGroupAvg);
                    var tempQty = Math.Max(0, prod.Quantity - oldQty);
                    prod.AvgPrice = InvoiceCalculators.ApplyPurchaseWeightedAverageCost(
                        tempQty,
                        revertedAvg,
                        newQty,
                        newGroupAvg);

                    if (nb != null)
                    {
                        await UpdateBatchPricesOnlyAsync(
                            db,
                            prod.Id,
                            nb.First.Batch,
                            nb.First.Expiry,
                            nb.First.Calc,
                            now,
                            cancellationToken);
                    }
                }
            }

            // Replace PurchaseInvoiceItems
            var previousHeaderSummary = new
            {
                invoice.SupplierId,
                invoice.InvoiceNumber,
                invoice.PostedDate,
                invoice.DueDate,
                invoice.InvoiceType,
                invoice.InvoiceAmount,
                invoice.DiscountAmount,
                invoice.TaxAmount,
                invoice.TotalAmount,
                invoice.TotalPaid,
                ItemsCount = oldItems.Count
            };

            db.PurchaseInvoiceItems.RemoveRange(oldItems);
            await db.SaveChangesAsync(cancellationToken);

            for (int idx = 0; idx < normalizedLines.Count; idx++)
            {
                var nl = normalizedLines[idx];
                db.PurchaseInvoiceItems.Add(new PurchaseInvoiceItem
                {
                    PurchaseInvoiceId = invoice.Id,
                    PurchaseOrderItemId = nl.PurchaseOrderItemId,
                    ProductId = nl.Product.Id,
                    StorageLocationId = nl.StorageLocationId ?? nl.Product.StorageLocationId,
                    Batch = string.IsNullOrWhiteSpace(nl.Batch) ? null : nl.Batch,
                    Expiry = nl.Expiry?.Date,
                    PackSize = nl.Calc.PackSize,
                    PackQuantity = Math.Round(nl.Calc.PackQuantity, 2, MidpointRounding.AwayFromZero),
                    UnitQuantity = nl.Calc.UnitQuantity,
                    PackPurchasePrice = Math.Round(nl.Calc.PackPurchasePrice, 2, MidpointRounding.AwayFromZero),
                    UnitPurchasePrice = Math.Round(nl.Calc.UnitPurchasePrice, 4, MidpointRounding.AwayFromZero),
                    PackSalePrice = Math.Round(nl.Calc.PackSalePrice, 2, MidpointRounding.AwayFromZero),
                    UnitSalePrice = Math.Round(nl.Calc.UnitSalePrice, 4, MidpointRounding.AwayFromZero),
                    WholeSalePackPrice = Math.Round(nl.Calc.WholeSalePackPrice, 2, MidpointRounding.AwayFromZero),
                    WholeSaleUnitPrice = Math.Round(nl.Calc.WholeSaleUnitPrice, 4, MidpointRounding.AwayFromZero),
                    PackBonus = Math.Round(nl.Calc.PackBonus, 2, MidpointRounding.AwayFromZero),
                    UnitBonus = nl.Calc.UnitBonus,
                    ItemDiscountPercentage = Math.Round(nl.Calc.ItemDiscountPercentage, 2, MidpointRounding.AwayFromZero),
                    Margin = nl.Calc.Margin,
                    WholeSaleMargin = nl.Calc.WholeSaleMargin,
                    AvgPrice = Math.Round(nl.Calc.AvgPrice, 2, MidpointRounding.AwayFromZero),
                    SubTotal = nl.Calc.SubTotal,
                    Quantity = nl.Calc.Quantity,
                    LineOrder = idx,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                await SyncProductSupplierLinkAsync(db, nl.Product.Id, newSupplier.Id, nl.Calc, now, cancellationToken);
            }

            // Sync catalog pricing for present & removed products
            var presentProductIds = new HashSet<long>();
            foreach (var nl in normalizedLines)
            {
                presentProductIds.Add(nl.Product.Id);
                if (trackedProducts.TryGetValue(nl.Product.Id, out var prod))
                {
                    SyncCatalogPricesOntoProduct(prod, nl.Calc, now);
                }
            }

            var removedProductIds = oldItems
                .Select(i => i.ProductId)
                .Distinct()
                .Where(pid => !presentProductIds.Contains(pid))
                .ToList();

            foreach (var remPid in removedProductIds)
            {
                if (!trackedProducts.TryGetValue(remPid, out var prod))
                    continue;

                var latestHistItem = await (
                    from pii in db.PurchaseInvoiceItems.AsNoTracking()
                    join pi in db.PurchaseInvoices.AsNoTracking() on pii.PurchaseInvoiceId equals pi.Id
                    where pii.ProductId == remPid &&
                          pi.Id != invoice.Id &&
                          (pi.Status == null || pi.Status != "reversed")
                    orderby pii.Id descending
                    select pii
                ).FirstOrDefaultAsync(cancellationToken);

                if (latestHistItem != null)
                {
                    prod.PackPurchasePrice = latestHistItem.PackPurchasePrice;
                    prod.UnitPurchasePrice = latestHistItem.UnitPurchasePrice;
                    prod.PackSalePrice = latestHistItem.PackSalePrice;
                    prod.UnitSalePrice = latestHistItem.UnitSalePrice;
                    prod.WholeSalePackPrice = latestHistItem.WholeSalePackPrice ?? latestHistItem.PackSalePrice;
                    prod.WholeSaleUnitPrice = latestHistItem.WholeSaleUnitPrice ?? latestHistItem.UnitSalePrice;
                }

                prod.Margin = InvoiceCalculators.ComputeProductMargin(prod.UnitSalePrice, prod.AvgPrice);
                prod.WholeSaleMargin = InvoiceCalculators.ComputeProductMargin(prod.WholeSaleUnitPrice ?? 0m, prod.AvgPrice);
                prod.UpdatedAt = now;
            }

            // Update Header
            var oldAccountId = invoice.AccountId;
            var oldPaymentMethod = invoice.PaymentMethod ?? "cash";

            invoice.SupplierId = newSupplier.Id;
            invoice.PostedDate = request.PostedDate.Date;
            invoice.InvoiceDate = request.PostedDate.Date;
            invoice.DueDate = newDueDate;
            invoice.CreditDays = newSupplier.CreditDays ?? 30;
            invoice.InvoiceNumber = request.InvoiceNumber.Trim();
            invoice.InvoiceType = newInvoiceType;
            invoice.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
            invoice.InvoiceAmount = newInvoiceAmount;
            invoice.GrossTotal = footer.RowsTotal;
            invoice.DiscountPercentage = footer.DiscountPercentage;
            invoice.DiscountAmount = footer.DiscountAmount;
            invoice.TaxPercentage = footer.TaxPercentage;
            invoice.TaxAmount = footer.TaxAmount;
            invoice.TotalAmount = footer.TotalAmount;
            invoice.TotalPaid = newTotalPaid;
            invoice.PaymentMethod = validation.PaymentMethod;
            invoice.UpdatedAt = now;

            // Fulfill user demands
            var fulfilledProdIds = normalizedLines
                .Where(l => l.Calc.Quantity > 0)
                .Select(l => l.Product.Id)
                .Distinct()
                .ToList();
            if (fulfilledProdIds.Count > 0)
            {
                var openDemands = await db.UserDemands
                    .Where(ud => ud.ProductId != null &&
                                 fulfilledProdIds.Contains(ud.ProductId.Value) &&
                                 (ud.Status == "pending" || ud.Status == "ordered"))
                    .ToListAsync(cancellationToken);
                foreach (var demand in openDemands)
                {
                    demand.Status = "fulfilled";
                    demand.UpdatedAt = now;
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            // Sync Purchase Order if linked
            if (order != null)
            {
                await SyncPurchaseOrderReceivingAsync(db, order, effectiveActorId, now, cancellationToken);
                await RememberSupplierProductSortingAsync(
                    db,
                    newSupplier.Id,
                    normalizedLines.Select(l => l.Product.Id).ToList(),
                    now,
                    cancellationToken);
            }

            // Sync Supplier Ledger
            if (newInvoiceType != "credit")
            {
                await DeleteInvoiceLedgerRowsAsync(db, invoice.Id, invoice.PostedNumber, cancellationToken);
            }
            else
            {
                await CreateOrSyncSupplierLedgerForCreditInvoiceAsync(
                    db,
                    invoice,
                    allocatedOnInvoice,
                    effectiveActorId,
                    now,
                    cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await RebuildSingleSupplierLedgerAsync(db, newSupplier.Id, cancellationToken);
            if (oldSupplierId != newSupplier.Id)
            {
                await RebuildSingleSupplierLedgerAsync(db, oldSupplierId, cancellationToken);
            }

            // Sync Financial Account & Cash Drawer
            await SyncAccountAndDrawerOnUpdateAsync(
                db,
                invoice,
                newSupplier.Name,
                oldAccountId,
                oldPaymentMethod,
                oldDirectPaid,
                validation.PaymentMethod,
                request.AccountId,
                newDirectPaid,
                openSession,
                request.UpdatePreShiftDrawer,
                effectiveActorId,
                now,
                cancellationToken);

            // Record Audit History
            var afterSnapshot = BuildAuditSnapshotJson(
                invoice,
                newSupplier.Name,
                normalizedLines.Select(l => (l.Product.Name, l.Batch, l.Expiry, l.Calc)).ToList());

            var diffs = new List<string>();
            if (previousHeaderSummary.SupplierId != invoice.SupplierId)
                diffs.Add($"Supplier: {oldSupplier?.Name} -> {newSupplier.Name}");
            if (!string.Equals(previousHeaderSummary.InvoiceNumber, invoice.InvoiceNumber, StringComparison.Ordinal))
                diffs.Add($"Invoice #: {previousHeaderSummary.InvoiceNumber} -> {invoice.InvoiceNumber}");
            if (!string.Equals(previousHeaderSummary.InvoiceType, invoice.InvoiceType, StringComparison.OrdinalIgnoreCase))
                diffs.Add($"Type: {previousHeaderSummary.InvoiceType} -> {invoice.InvoiceType}");
            if (previousHeaderSummary.TotalAmount != invoice.TotalAmount)
                diffs.Add($"Total: {previousHeaderSummary.TotalAmount:N2} -> {invoice.TotalAmount:N2}");
            if (previousHeaderSummary.TotalPaid != invoice.TotalPaid)
                diffs.Add($"Paid: {previousHeaderSummary.TotalPaid:N2} -> {invoice.TotalPaid:N2}");
            if (previousHeaderSummary.ItemsCount != normalizedLines.Count)
                diffs.Add($"Items: {previousHeaderSummary.ItemsCount} -> {normalizedLines.Count}");
            if (diffs.Count == 0)
                diffs.Add("Line items or pricing updated");

            db.DocumentHistories.Add(new DocumentHistory
            {
                DocumentType = "purchase_invoice",
                DocumentId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Action = "updated",
                UserId = effectiveActorId,
                Reason = string.IsNullOrWhiteSpace(request.EditReason) ? "Purchase invoice updated" : request.EditReason.Trim(),
                BeforeSnapshot = beforeSnapshot,
                AfterSnapshot = afterSnapshot,
                ChangesSummary = string.Join(" | ", diffs),
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new PurchaseInvoiceOperationResult
            {
                Succeeded = true,
                InvoiceId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Message = $"Purchase invoice {invoice.PostedNumber} updated successfully."
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return PurchaseInvoiceOperationResult.Fail($"Failed to update purchase invoice: {ex.Message}");
        }
    }

    public async Task<PurchaseInvoiceOperationResult> ReverseAsync(
        long id,
        string reason,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var cleanReason = reason?.Trim() ?? string.Empty;
        if (cleanReason.Length < 3)
            return PurchaseInvoiceOperationResult.Fail("Reversal reason must be at least 3 characters.");

        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var invoice = await db.PurchaseInvoices
                .FirstOrDefaultAsync(pi => pi.Id == id, cancellationToken);
            if (invoice == null)
                return PurchaseInvoiceOperationResult.Fail($"Purchase invoice #{id} was not found.");

            if (string.Equals(invoice.Status, "reversed", StringComparison.OrdinalIgnoreCase))
                return PurchaseInvoiceOperationResult.Fail("This purchase invoice has already been reversed.");

            var hasReturns = await db.PurchaseReturns.AnyAsync(pr => pr.PurchaseInvoiceId == invoice.Id, cancellationToken);
            if (hasReturns)
            {
                return PurchaseInvoiceOperationResult.Fail(
                    "Cannot reverse this purchase invoice because purchase return(s) exist against it. Delete or reverse the return(s) first.");
            }

            var items = await db.PurchaseInvoiceItems
                .Where(i => i.PurchaseInvoiceId == invoice.Id)
                .OrderBy(i => i.LineOrder)
                .ThenBy(i => i.Id)
                .ToListAsync(cancellationToken);

            var productIds = items.Select(i => i.ProductId).Distinct().ToList();
            var trackedProducts = await db.Products
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            var stockError = await ValidateReversalStockAsync(db, items, trackedProducts, "reverse", cancellationToken);
            if (stockError != null)
                return PurchaseInvoiceOperationResult.Fail(stockError);

            var supplier = await db.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == invoice.SupplierId, cancellationToken);
            var beforeSnapshot = BuildAuditSnapshotJsonFromEntities(
                invoice,
                supplier?.Name ?? string.Empty,
                items,
                trackedProducts);

            var now = DateTime.UtcNow;

            // 1. Revert Product & Batch Stock + Inverse Moving Weighted Average Cost
            await RevertItemsStockAndCostAsync(db, items, trackedProducts, now, cancellationToken);

            // 2. Detach Payment Allocations (restoring Payment.UnappliedAmount)
            var detachedSum = await DetachInvoicePaymentAllocationsAsync(db, invoice.Id, now, cancellationToken);
            if (detachedSum > 0m)
            {
                invoice.TotalPaid = Math.Max(0m, Math.Round(invoice.TotalPaid - detachedSum, 2));
            }

            // 3. Delete Supplier Ledger entries & rebuild
            await DeleteInvoiceLedgerRowsAsync(db, invoice.Id, invoice.PostedNumber, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await RebuildSingleSupplierLedgerAsync(db, invoice.SupplierId, cancellationToken);

            // 4. Refund direct cash/account outflow & compensate open Cash Drawer
            var isDebit = !string.Equals(invoice.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
            var revPaid = isDebit && invoice.TotalPaid <= 0m ? invoice.TotalAmount : invoice.TotalPaid;
            if (revPaid > 0m)
            {
                await RefundInvoiceAccountAndDrawerAsync(
                    db,
                    invoice,
                    supplier?.Name ?? string.Empty,
                    revPaid,
                    isDeletion: false,
                    effectiveActorId,
                    now,
                    cancellationToken);
            }

            // 5. Mark Invoice Reversed (preserving rows for audit trail)
            invoice.Status = "reversed";
            invoice.ReversedAt = now;
            invoice.ReversedBy = effectiveActorId;
            invoice.ReversalReason = cleanReason;
            invoice.UpdatedAt = now;

            await db.SaveChangesAsync(cancellationToken);

            // 6. Sync Linked Purchase Order (after status = 'reversed' is persisted!)
            if (invoice.PurchaseOrderId is > 0)
            {
                var poId = invoice.PurchaseOrderId.Value;
                var order = await db.PurchaseOrders.FirstOrDefaultAsync(po => po.Id == poId, cancellationToken);
                if (order != null)
                {
                    await SyncPurchaseOrderReceivingAsync(db, order, effectiveActorId, now, cancellationToken);
                }
            }

            // 7. Record Document History
            var afterSnapshot = BuildAuditSnapshotJsonFromEntities(
                invoice,
                supplier?.Name ?? string.Empty,
                items,
                trackedProducts);

            db.DocumentHistories.Add(new DocumentHistory
            {
                DocumentType = "purchase_invoice",
                DocumentId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Action = "reversed",
                UserId = effectiveActorId,
                Reason = cleanReason,
                BeforeSnapshot = beforeSnapshot,
                AfterSnapshot = afterSnapshot,
                ChangesSummary = $"Reversed {invoice.PostedNumber} — Reason: {cleanReason}",
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new PurchaseInvoiceOperationResult
            {
                Succeeded = true,
                InvoiceId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Message = $"Purchase invoice {invoice.PostedNumber} reversed successfully."
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return PurchaseInvoiceOperationResult.Fail($"Failed to reverse purchase invoice: {ex.Message}");
        }
    }

    public async Task<PurchaseInvoiceOperationResult> DeleteAsync(
        long id,
        string userPassword,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;
        if (effectiveActorId is > 0)
        {
            var passwordOk = await _authService.VerifyPasswordAsync(effectiveActorId.Value, userPassword ?? string.Empty, cancellationToken);
            if (!passwordOk)
            {
                return PurchaseInvoiceOperationResult.Fail("Incorrect password. Deletion was not authorized.");
            }
        }
        else if (string.IsNullOrWhiteSpace(userPassword))
        {
            return PurchaseInvoiceOperationResult.Fail("Password confirmation is required to delete a purchase invoice.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var invoice = await db.PurchaseInvoices
                .FirstOrDefaultAsync(pi => pi.Id == id, cancellationToken);
            if (invoice == null)
                return PurchaseInvoiceOperationResult.Fail($"Purchase invoice #{id} was not found.");

            var hasReturns = await db.PurchaseReturns.AnyAsync(pr => pr.PurchaseInvoiceId == invoice.Id, cancellationToken);
            if (hasReturns)
            {
                return PurchaseInvoiceOperationResult.Fail(
                    "Cannot delete this purchase invoice because purchase return(s) exist against it.");
            }

            var items = await db.PurchaseInvoiceItems
                .Where(i => i.PurchaseInvoiceId == invoice.Id)
                .ToListAsync(cancellationToken);

            var wasReversed = string.Equals(invoice.Status, "reversed", StringComparison.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;
            var supplierId = invoice.SupplierId;
            var linkedPoId = invoice.PurchaseOrderId;
            var postedNo = invoice.PostedNumber;

            if (!wasReversed)
            {
                var productIds = items.Select(i => i.ProductId).Distinct().ToList();
                var trackedProducts = await db.Products
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, cancellationToken);

                var stockError = await ValidateReversalStockAsync(db, items, trackedProducts, "delete", cancellationToken);
                if (stockError != null)
                    return PurchaseInvoiceOperationResult.Fail(stockError);

                var supplier = await db.Suppliers.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == invoice.SupplierId, cancellationToken);

                await RevertItemsStockAndCostAsync(db, items, trackedProducts, now, cancellationToken);

                var detachedSum = await DetachInvoicePaymentAllocationsAsync(db, invoice.Id, now, cancellationToken);
                if (detachedSum > 0m)
                {
                    invoice.TotalPaid = Math.Max(0m, Math.Round(invoice.TotalPaid - detachedSum, 2));
                }

                await DeleteInvoiceLedgerRowsAsync(db, invoice.Id, invoice.PostedNumber, cancellationToken);

                var isDebit = !string.Equals(invoice.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
                var delPaid = isDebit && invoice.TotalPaid <= 0m ? invoice.TotalAmount : invoice.TotalPaid;
                if (delPaid > 0m)
                {
                    await RefundInvoiceAccountAndDrawerAsync(
                        db,
                        invoice,
                        supplier?.Name ?? string.Empty,
                        delPaid,
                        isDeletion: true,
                        effectiveActorId,
                        now,
                        cancellationToken);
                }
            }

            db.PurchaseInvoiceItems.RemoveRange(items);
            db.PurchaseInvoices.Remove(invoice);
            await db.SaveChangesAsync(cancellationToken);

            if (!wasReversed)
            {
                await RebuildSingleSupplierLedgerAsync(db, supplierId, cancellationToken);
            }

            if (linkedPoId is > 0)
            {
                var poVal = linkedPoId.Value;
                var order = await db.PurchaseOrders.FirstOrDefaultAsync(po => po.Id == poVal, cancellationToken);
                if (order != null)
                {
                    await SyncPurchaseOrderReceivingAsync(db, order, effectiveActorId, now, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            await tx.CommitAsync(cancellationToken);

            return new PurchaseInvoiceOperationResult
            {
                Succeeded = true,
                InvoiceId = id,
                PostedNumber = postedNo,
                Message = $"Purchase invoice {postedNo} permanently deleted."
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return PurchaseInvoiceOperationResult.Fail($"Failed to delete purchase invoice: {ex.Message}");
        }
    }

    // =========================================================================
    // 5. HELD PURCHASE DRAFTS (DRF-PUR-0001)
    // =========================================================================

    public async Task<HeldPurchaseDraftSummaryDto> SaveDraftAsync(
        long? existingDraftId,
        PurchaseInvoiceUpsertRequest state,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var activeItems = state.Items.Where(i => i.ProductId > 0).ToList();
        if (activeItems.Count == 0)
            throw new InvalidOperationException("Add at least one product before holding a purchase draft.");

        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var supplierName = state.SupplierId > 0
            ? (await db.Suppliers.AsNoTracking()
                .Where(s => s.Id == state.SupplierId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken)) ?? "Selected Supplier"
            : "Unselected Supplier";

        var now = DateTime.UtcNow;
        var payloadJson = JsonSerializer.Serialize(state, JsonOptions);

        InvoiceDraft? draft = null;
        if (existingDraftId is > 0)
        {
            var dId = existingDraftId.Value;
            draft = await db.InvoiceDrafts.FirstOrDefaultAsync(
                d => d.Id == dId && d.InvoiceKind == "purchase" && d.DeletedAt == null,
                cancellationToken);
        }

        if (draft == null)
        {
            var nextDraftNo = await ComputeNextDraftNumberAsync(db, cancellationToken);
            draft = new InvoiceDraft
            {
                DraftNumber = nextDraftNo,
                InvoiceKind = "purchase",
                UserId = effectiveActorId,
                PartyId = state.SupplierId > 0 ? state.SupplierId : null,
                PartyName = supplierName,
                Title = !string.IsNullOrWhiteSpace(state.InvoiceNumber)
                    ? $"Bill #{state.InvoiceNumber.Trim()}"
                    : nextDraftNo,
                TotalAmount = Math.Round(state.TotalAmount, 2),
                ItemCount = activeItems.Count,
                Payload = payloadJson,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.InvoiceDrafts.Add(draft);
        }
        else
        {
            draft.PartyId = state.SupplierId > 0 ? state.SupplierId : null;
            draft.PartyName = supplierName;
            draft.Title = !string.IsNullOrWhiteSpace(state.InvoiceNumber)
                ? $"Bill #{state.InvoiceNumber.Trim()}"
                : draft.DraftNumber;
            draft.TotalAmount = Math.Round(state.TotalAmount, 2);
            draft.ItemCount = activeItems.Count;
            draft.Payload = payloadJson;
            draft.Version += 1;
            draft.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new HeldPurchaseDraftSummaryDto
        {
            Id = draft.Id,
            DraftNumber = draft.DraftNumber,
            SupplierId = draft.PartyId,
            SupplierName = draft.PartyName ?? "Unselected Supplier",
            Title = draft.Title ?? draft.DraftNumber,
            ItemCount = draft.ItemCount,
            TotalAmount = draft.TotalAmount,
            Version = draft.Version,
            UpdatedAt = draft.UpdatedAt ?? now
        };
    }

    public async Task<List<HeldPurchaseDraftSummaryDto>> GetHeldDraftsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var drafts = await db.InvoiceDrafts.AsNoTracking()
            .Where(d => d.InvoiceKind == "purchase" && d.ConvertedInvoiceId == null && d.DeletedAt == null)
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .ToListAsync(cancellationToken);

        return drafts.Select(d => new HeldPurchaseDraftSummaryDto
        {
            Id = d.Id,
            DraftNumber = d.DraftNumber,
            SupplierId = d.PartyId,
            SupplierName = d.PartyName ?? "Unselected Supplier",
            Title = d.Title ?? d.DraftNumber,
            ItemCount = d.ItemCount,
            TotalAmount = d.TotalAmount,
            Version = d.Version,
            UpdatedAt = d.UpdatedAt ?? d.CreatedAt ?? DateTime.UtcNow
        }).ToList();
    }

    public async Task<HeldPurchaseDraftDetailDto?> GetDraftByIdAsync(
        long draftId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var draft = await db.InvoiceDrafts.AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.Id == draftId && d.InvoiceKind == "purchase" && d.ConvertedInvoiceId == null && d.DeletedAt == null,
                cancellationToken);

        if (draft == null)
            return null;

        PurchaseInvoiceUpsertRequest? parsed = null;
        if (!string.IsNullOrWhiteSpace(draft.Payload))
        {
            try
            {
                parsed = JsonSerializer.Deserialize<PurchaseInvoiceUpsertRequest>(draft.Payload, JsonOptions);
            }
            catch
            {
                parsed = null;
            }
        }

        parsed ??= new PurchaseInvoiceUpsertRequest();
        parsed.DraftId = draft.Id;

        return new HeldPurchaseDraftDetailDto
        {
            Id = draft.Id,
            DraftNumber = draft.DraftNumber,
            Version = draft.Version,
            FormState = parsed
        };
    }

    public async Task<bool> DeleteDraftAsync(long draftId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var draft = await db.InvoiceDrafts.FirstOrDefaultAsync(
            d => d.Id == draftId && d.InvoiceKind == "purchase",
            cancellationToken);
        if (draft == null)
            return false;

        draft.DeletedAt = DateTime.UtcNow;
        draft.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // =========================================================================
    // PRIVATE INTERNAL HELPERS
    // =========================================================================

    private sealed class NormalizedLineInternal
    {
        public Product Product { get; init; } = null!;
        public long? PurchaseOrderItemId { get; set; }
        public long? StorageLocationId { get; init; }
        public string Batch { get; init; } = string.Empty;
        public DateTime? Expiry { get; init; }
        public PurchaseLineCalcModel Calc { get; init; } = null!;
    }

    private sealed class ValidatedPurchaseRequestInternal
    {
        public bool IsValid { get; init; }
        public PurchaseInvoiceOperationResult? FailureResult { get; init; }
        public Supplier? Supplier { get; init; }
        public string InvoiceType { get; init; } = "debit";
        public string PaymentMethod { get; init; } = "cash";
        public List<NormalizedLineInternal> NormalizedLines { get; init; } = [];
        public PurchaseFooterCalcModel Footer { get; init; } = new();
        public decimal ResolvedTotalPaid { get; init; }
        public decimal ResolvedInvoiceAmount { get; init; }
        public DateTime ResolvedDueDate { get; init; }
    }

    private static async Task<ValidatedPurchaseRequestInternal> ValidateAndNormalizePurchaseRequestAsync(
        KarobarDbContext db,
        PurchaseInvoiceUpsertRequest request,
        long? excludeInvoiceId,
        decimal allocatedOnInvoice,
        CancellationToken cancellationToken)
    {
        if (request.SupplierId <= 0)
        {
            return new ValidatedPurchaseRequestInternal
            {
                IsValid = false,
                FailureResult = PurchaseInvoiceOperationResult.Fail("Please select a supplier.", fieldName: "supplier_id")
            };
        }

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken);
        if (supplier == null)
        {
            return new ValidatedPurchaseRequestInternal
            {
                IsValid = false,
                FailureResult = PurchaseInvoiceOperationResult.Fail("Selected supplier does not exist.", fieldName: "supplier_id")
            };
        }

        if (string.IsNullOrWhiteSpace(request.InvoiceNumber))
        {
            return new ValidatedPurchaseRequestInternal
            {
                IsValid = false,
                FailureResult = PurchaseInvoiceOperationResult.Fail("Supplier Invoice # is required.", fieldName: "invoice_number")
            };
        }

        var cleanInvNumber = request.InvoiceNumber.Trim();
        var dupQuery = db.PurchaseInvoices.AsNoTracking()
            .Where(pi => pi.InvoiceNumber != null && pi.InvoiceNumber.ToLower() == cleanInvNumber.ToLower());
        if (excludeInvoiceId is > 0)
        {
            var exId = excludeInvoiceId.Value;
            dupQuery = dupQuery.Where(pi => pi.Id != exId);
        }
        if (await dupQuery.AnyAsync(cancellationToken))
        {
            return new ValidatedPurchaseRequestInternal
            {
                IsValid = false,
                FailureResult = PurchaseInvoiceOperationResult.Fail(
                    $"Supplier Invoice # '{cleanInvNumber}' already exists on another purchase invoice.",
                    fieldName: "invoice_number")
            };
        }

        var activeInputRows = request.Items
            .Select((row, idx) => (Row: row, OriginalIndex: idx))
            .Where(x => x.Row.ProductId > 0)
            .ToList();

        if (activeInputRows.Count == 0)
        {
            return new ValidatedPurchaseRequestInternal
            {
                IsValid = false,
                FailureResult = PurchaseInvoiceOperationResult.Fail("Please add at least one product line item.")
            };
        }

        var productIds = activeInputRows.Select(x => x.Row.ProductId).Distinct().ToList();
        var productsMap = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var seenProductBatchKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedLines = new List<NormalizedLineInternal>(activeInputRows.Count);
        var isPoReceipt = request.PurchaseOrderId is > 0;

        foreach (var (raw, rowIdx) in activeInputRows)
        {
            if (!productsMap.TryGetValue(raw.ProductId, out var product))
            {
                return new ValidatedPurchaseRequestInternal
                {
                    IsValid = false,
                    FailureResult = PurchaseInvoiceOperationResult.Fail(
                        $"Row {rowIdx + 1}: Selected product #{raw.ProductId} was not found.",
                        rowIndex: rowIdx,
                        fieldName: "product_id")
                };
            }

            if (raw.PackQuantity < 0m || raw.UnitQuantity < 0)
                return FailLine(rowIdx, "pack_quantity", $"Row {rowIdx + 1} ({product.Name}): Quantities cannot be negative.");
            if (raw.PackBonus < 0m || raw.UnitBonus < 0)
                return FailLine(rowIdx, "pack_bonus", $"Row {rowIdx + 1} ({product.Name}): Bonus quantities cannot be negative.");
            if (raw.PackPurchasePrice < 0m || raw.UnitPurchasePrice < 0m)
                return FailLine(rowIdx, "pack_purchase_price", $"Row {rowIdx + 1} ({product.Name}): Purchase prices cannot be negative.");
            if (raw.PackSalePrice < 0m || raw.UnitSalePrice < 0m)
                return FailLine(rowIdx, "pack_sale_price", $"Row {rowIdx + 1} ({product.Name}): Sale prices cannot be negative.");
            if (raw.ItemDiscountPercentage < 0m || raw.ItemDiscountPercentage > 100m)
                return FailLine(rowIdx, "item_discount_percentage", $"Row {rowIdx + 1} ({product.Name}): Item discount % must be between 0 and 100.");

            var packSize = raw.PackSize > 0 ? raw.PackSize : (product.PackSize > 0 ? product.PackSize : 1);
            var calc = new PurchaseLineCalcModel
            {
                PackSize = packSize,
                PackQuantity = raw.PackQuantity,
                UnitQuantity = raw.UnitQuantity,
                PackPurchasePrice = raw.PackPurchasePrice,
                UnitPurchasePrice = raw.UnitPurchasePrice,
                PackSalePrice = raw.PackSalePrice,
                UnitSalePrice = raw.UnitSalePrice,
                WholeSalePackPrice = raw.WholeSalePackPrice,
                WholeSaleUnitPrice = raw.WholeSaleUnitPrice,
                PackBonus = raw.PackBonus,
                UnitBonus = raw.UnitBonus,
                ItemDiscountPercentage = raw.ItemDiscountPercentage
            };

            // Server-side two-way fallback sync matching InvoiceCalculationService::calculateAndValidatePurchase
            InvoiceCalculators.RecalcPurchaseItem(calc, "pack_size");

            var cleanBatch = (raw.Batch ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(cleanBatch) && !raw.Expiry.HasValue)
            {
                return FailLine(rowIdx, "expiry", $"Row {rowIdx + 1} ({product.Name}): Expiry date is required for batch '{cleanBatch}'.");
            }

            var dupKey = $"{product.Id}|{(string.IsNullOrEmpty(cleanBatch) ? "__UNBATCHED__" : cleanBatch)}";
            if (!seenProductBatchKeys.Add(dupKey))
            {
                var dupLabel = string.IsNullOrEmpty(cleanBatch) ? "[Unassigned Stock]" : $"Batch '{cleanBatch}'";
                return FailLine(rowIdx, "batch", $"Row {rowIdx + 1} ({product.Name}): Duplicate {dupLabel} row for the same product.");
            }

            // Margin > 0 check (matching PurchaseInvoiceForm.jsx:1221)
            bool requiresMarginCheck = !isPoReceipt || calc.Quantity > 0;
            if (requiresMarginCheck && (!calc.Margin.HasValue || calc.Margin.Value <= 0m))
            {
                return FailLine(
                    rowIdx,
                    "pack_sale_price",
                    $"Row {rowIdx + 1}: Margin must be greater than 0% for '{product.Name}' (current sale price does not exceed cost).");
            }

            normalizedLines.Add(new NormalizedLineInternal
            {
                Product = product,
                PurchaseOrderItemId = raw.PurchaseOrderItemId,
                StorageLocationId = raw.StorageLocationId,
                Batch = cleanBatch,
                Expiry = raw.Expiry?.Date,
                Calc = calc
            });
        }

        // Footer Calculation (discount_amount / tax_amount take precedence if != 0, matching InvoiceCalculationService.php)
        var footer = new PurchaseFooterCalcModel
        {
            DiscountPercentage = request.DiscountPercentage,
            DiscountAmount = request.DiscountAmount,
            TaxPercentage = request.TaxPercentage,
            TaxAmount = request.TaxAmount
        };

        var discMode = request.DiscountAmount != 0m ? "discount_amount" : (request.DiscountPercentage != 0m ? "discount_percentage" : null);
        InvoiceCalculators.RecalcPurchaseFooter(normalizedLines.Select(l => l.Calc), footer, discMode);

        var taxMode = request.TaxAmount != 0m ? "tax_amount" : (request.TaxPercentage != 0m ? "tax_percentage" : null);
        if (taxMode != null)
        {
            InvoiceCalculators.RecalcPurchaseFooter(normalizedLines.Select(l => l.Calc), footer, taxMode);
        }

        var invoiceType = string.Equals(request.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase)
            ? "credit"
            : "debit";

        decimal resolvedTotalPaid;
        if (!request.TotalPaid.HasValue)
        {
            resolvedTotalPaid = invoiceType == "credit" ? allocatedOnInvoice : footer.TotalAmount;
        }
        else
        {
            resolvedTotalPaid = Math.Round(request.TotalPaid.Value, 2, MidpointRounding.AwayFromZero);
            if (invoiceType == "credit" && allocatedOnInvoice > 0m && resolvedTotalPaid < allocatedOnInvoice)
            {
                resolvedTotalPaid = Math.Round(resolvedTotalPaid + allocatedOnInvoice, 2, MidpointRounding.AwayFromZero);
            }
        }

        if (resolvedTotalPaid < 0m)
        {
            return new ValidatedPurchaseRequestInternal
            {
                IsValid = false,
                FailureResult = PurchaseInvoiceOperationResult.Fail("Total Paid cannot be negative.", fieldName: "total_paid")
            };
        }

        if (invoiceType != "credit" && resolvedTotalPaid > footer.TotalAmount + 0.009m)
        {
            return new ValidatedPurchaseRequestInternal
            {
                IsValid = false,
                FailureResult = PurchaseInvoiceOperationResult.Fail(
                    "Total Paid cannot exceed Total Amount on a Debit purchase.",
                    fieldName: "total_paid")
            };
        }

        var resolvedInvoiceAmount = Math.Round(request.InvoiceAmount, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(resolvedInvoiceAmount - footer.TotalAmount) > 5m)
        {
            return new ValidatedPurchaseRequestInternal
            {
                IsValid = false,
                FailureResult = PurchaseInvoiceOperationResult.Fail(
                    $"Bill Amount ({resolvedInvoiceAmount:N2}) must match Total Amount ({footer.TotalAmount:N2}) within ±5.00 (difference: {resolvedInvoiceAmount - footer.TotalAmount:N2}).",
                    fieldName: "invoice_amount")
            };
        }

        var creditDays = supplier.CreditDays is > 0 ? supplier.CreditDays.Value : 30;
        var resolvedDueDate = request.DueDate?.Date ?? request.PostedDate.Date.AddDays(creditDays);

        var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);

        return new ValidatedPurchaseRequestInternal
        {
            IsValid = true,
            Supplier = supplier,
            InvoiceType = invoiceType,
            PaymentMethod = paymentMethod,
            NormalizedLines = normalizedLines,
            Footer = footer,
            ResolvedTotalPaid = resolvedTotalPaid,
            ResolvedInvoiceAmount = resolvedInvoiceAmount,
            ResolvedDueDate = resolvedDueDate
        };

        static ValidatedPurchaseRequestInternal FailLine(int idx, string field, string msg) => new()
        {
            IsValid = false,
            FailureResult = PurchaseInvoiceOperationResult.Fail(msg, rowIndex: idx, fieldName: field)
        };
    }

    private static string NormalizePaymentMethod(string? raw)
    {
        var clean = (raw ?? "cash").Trim().ToLowerInvariant();
        return clean switch
        {
            "cash" or "card" or "bank_transfer" or "cheque" or "mobile_wallet" or "other" => clean,
            _ => "cash"
        };
    }

    private static void ApplyPurchaseToProduct(Product product, PurchaseLineCalcModel calc, DateTime now)
    {
        if (calc.Quantity > 0)
        {
            product.AvgPrice = InvoiceCalculators.ApplyPurchaseWeightedAverageCost(
                product.Quantity,
                product.AvgPrice,
                calc.Quantity,
                calc.EffectiveAvgPrice4);
            product.Quantity += calc.Quantity;
        }

        // Update all 6 catalog price columns (even when recovering from negative stock — fixing web bug #4!)
        SyncCatalogPricesOntoProduct(product, calc, now);
    }

    private static void SyncCatalogPricesOntoProduct(Product product, PurchaseLineCalcModel calc, DateTime now)
    {
        product.PackPurchasePrice = Math.Round(calc.PackPurchasePrice, 2, MidpointRounding.AwayFromZero);
        product.UnitPurchasePrice = Math.Round(calc.UnitPurchasePrice, 4, MidpointRounding.AwayFromZero);
        product.PackSalePrice = Math.Round(calc.PackSalePrice, 2, MidpointRounding.AwayFromZero);
        product.UnitSalePrice = Math.Round(calc.UnitSalePrice, 4, MidpointRounding.AwayFromZero);
        product.WholeSalePackPrice = Math.Round(calc.WholeSalePackPrice, 2, MidpointRounding.AwayFromZero);
        product.WholeSaleUnitPrice = Math.Round(calc.WholeSaleUnitPrice, 4, MidpointRounding.AwayFromZero);
        product.Margin = InvoiceCalculators.ComputeProductMargin(product.UnitSalePrice, product.AvgPrice);
        product.WholeSaleMargin = InvoiceCalculators.ComputeProductMargin(product.WholeSaleUnitPrice ?? 0m, product.AvgPrice);
        product.UpdatedAt = now;
    }

    private static async Task<Batch?> FindTrackedBatchAsync(
        KarobarDbContext db,
        long productId,
        string batchNumber,
        DateTime expiryDate,
        CancellationToken cancellationToken)
    {
        var cleanBatch = batchNumber.Trim();
        var exp = expiryDate.Date;

        var localMatch = db.Batches.Local.FirstOrDefault(b =>
            b.ProductId == productId &&
            string.Equals((b.BatchNumber ?? string.Empty).Trim(), cleanBatch, StringComparison.OrdinalIgnoreCase) &&
            b.ExpiryDate.HasValue &&
            b.ExpiryDate.Value.Date == exp);

        if (localMatch != null)
            return localMatch;

        var productBatches = await db.Batches
            .Where(b => b.ProductId == productId)
            .ToListAsync(cancellationToken);

        return productBatches.FirstOrDefault(b =>
            string.Equals((b.BatchNumber ?? string.Empty).Trim(), cleanBatch, StringComparison.OrdinalIgnoreCase) &&
            b.ExpiryDate.HasValue &&
            b.ExpiryDate.Value.Date == exp);
    }

    private static async Task UpsertBatchOnReceiveAsync(
        KarobarDbContext db,
        Product product,
        string batchNumber,
        DateTime? expiry,
        long? storageLocationId,
        PurchaseLineCalcModel calc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(batchNumber) || !expiry.HasValue)
            return;

        var cleanBatch = batchNumber.Trim();
        var expDate = expiry.Value.Date;
        var locId = storageLocationId ?? product.StorageLocationId;

        var existing = await FindTrackedBatchAsync(db, product.Id, cleanBatch, expDate, cancellationToken);

        if (existing == null)
        {
            existing = new Batch
            {
                ProductId = product.Id,
                BatchNumber = cleanBatch,
                ExpiryDate = expDate,
                Quantity = calc.Quantity,
                PackSize = calc.PackSize,
                StorageLocationId = locId,
                Rack = product.Rack,
                PackPurchasePrice = Math.Round(calc.PackPurchasePrice, 2, MidpointRounding.AwayFromZero),
                UnitPurchasePrice = Math.Round(calc.UnitPurchasePrice, 4, MidpointRounding.AwayFromZero),
                PackSalePrice = Math.Round(calc.PackSalePrice, 2, MidpointRounding.AwayFromZero),
                UnitSalePrice = Math.Round(calc.UnitSalePrice, 4, MidpointRounding.AwayFromZero),
                WholeSalePackPrice = Math.Round(calc.WholeSalePackPrice, 2, MidpointRounding.AwayFromZero),
                WholeSaleUnitPrice = Math.Round(calc.WholeSaleUnitPrice, 4, MidpointRounding.AwayFromZero),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Batches.Add(existing);
        }
        else
        {
            existing.Quantity += calc.Quantity;
            existing.PackSize = calc.PackSize;
            existing.PackPurchasePrice = Math.Round(calc.PackPurchasePrice, 2, MidpointRounding.AwayFromZero);
            existing.UnitPurchasePrice = Math.Round(calc.UnitPurchasePrice, 4, MidpointRounding.AwayFromZero);
            existing.PackSalePrice = Math.Round(calc.PackSalePrice, 2, MidpointRounding.AwayFromZero);
            existing.UnitSalePrice = Math.Round(calc.UnitSalePrice, 4, MidpointRounding.AwayFromZero);
            existing.WholeSalePackPrice = Math.Round(calc.WholeSalePackPrice, 2, MidpointRounding.AwayFromZero);
            existing.WholeSaleUnitPrice = Math.Round(calc.WholeSaleUnitPrice, 4, MidpointRounding.AwayFromZero);
            existing.UpdatedAt = now;
        }
    }

    private static async Task AdjustBatchQuantityOnUpdateAsync(
        KarobarDbContext db,
        Product product,
        string? batchNumber,
        DateTime? expiry,
        long? storageLocationId,
        int delta,
        PurchaseLineCalcModel? calc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cleanBatch = (batchNumber ?? string.Empty).Trim();
        if (delta == 0 || string.IsNullOrEmpty(cleanBatch) || !expiry.HasValue)
            return;

        var expDate = expiry.Value.Date;
        var batch = await FindTrackedBatchAsync(db, product.Id, cleanBatch, expDate, cancellationToken);

        if (delta > 0)
        {
            if (batch == null)
            {
                batch = new Batch
                {
                    ProductId = product.Id,
                    BatchNumber = cleanBatch,
                    ExpiryDate = expDate,
                    Quantity = delta,
                    PackSize = calc?.PackSize ?? product.PackSize,
                    StorageLocationId = storageLocationId ?? product.StorageLocationId,
                    Rack = product.Rack,
                    CreatedAt = now
                };
                db.Batches.Add(batch);
            }
            else
            {
                batch.Quantity += delta;
            }

            if (calc != null)
            {
                batch.PackSize = calc.PackSize;
                batch.PackPurchasePrice = Math.Round(calc.PackPurchasePrice, 2, MidpointRounding.AwayFromZero);
                batch.UnitPurchasePrice = Math.Round(calc.UnitPurchasePrice, 4, MidpointRounding.AwayFromZero);
                batch.PackSalePrice = Math.Round(calc.PackSalePrice, 2, MidpointRounding.AwayFromZero);
                batch.UnitSalePrice = Math.Round(calc.UnitSalePrice, 4, MidpointRounding.AwayFromZero);
                batch.WholeSalePackPrice = Math.Round(calc.WholeSalePackPrice, 2, MidpointRounding.AwayFromZero);
                batch.WholeSaleUnitPrice = Math.Round(calc.WholeSaleUnitPrice, 4, MidpointRounding.AwayFromZero);
            }
            batch.UpdatedAt = now;
        }
        else if (batch != null)
        {
            batch.Quantity = Math.Max(0, batch.Quantity + delta);
            batch.UpdatedAt = now;
        }
    }

    private static async Task UpdateBatchPricesOnlyAsync(
        KarobarDbContext db,
        long productId,
        string? batchNumber,
        DateTime? expiry,
        PurchaseLineCalcModel calc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cleanBatch = (batchNumber ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(cleanBatch) || !expiry.HasValue)
            return;

        var expDate = expiry.Value.Date;
        var batch = await FindTrackedBatchAsync(db, productId, cleanBatch, expDate, cancellationToken);

        if (batch != null)
        {
            batch.PackPurchasePrice = Math.Round(calc.PackPurchasePrice, 2, MidpointRounding.AwayFromZero);
            batch.UnitPurchasePrice = Math.Round(calc.UnitPurchasePrice, 4, MidpointRounding.AwayFromZero);
            batch.PackSalePrice = Math.Round(calc.PackSalePrice, 2, MidpointRounding.AwayFromZero);
            batch.UnitSalePrice = Math.Round(calc.UnitSalePrice, 4, MidpointRounding.AwayFromZero);
            batch.WholeSalePackPrice = Math.Round(calc.WholeSalePackPrice, 2, MidpointRounding.AwayFromZero);
            batch.WholeSaleUnitPrice = Math.Round(calc.WholeSaleUnitPrice, 4, MidpointRounding.AwayFromZero);
            batch.UpdatedAt = now;
        }
    }

    private static async Task SyncProductSupplierLinkAsync(
        KarobarDbContext db,
        long productId,
        long supplierId,
        PurchaseLineCalcModel calc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var lastCost = calc.UnitPurchasePrice > 0m ? calc.UnitPurchasePrice : calc.EffectiveAvgPrice4;
        var link = await db.ProductSuppliers
            .FirstOrDefaultAsync(ps => ps.ProductId == productId && ps.SupplierId == supplierId, cancellationToken);

        if (link == null)
        {
            var anyExisting = await db.ProductSuppliers.AnyAsync(ps => ps.ProductId == productId, cancellationToken);
            db.ProductSuppliers.Add(new ProductSupplier
            {
                ProductId = productId,
                SupplierId = supplierId,
                LastPurchasePrice = Math.Round(lastCost, 4, MidpointRounding.AwayFromZero),
                MinOrderQty = 1,
                IsPreferred = !anyExisting,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            link.LastPurchasePrice = Math.Round(lastCost, 4, MidpointRounding.AwayFromZero);
            link.UpdatedAt = now;
        }
    }

    private static async Task<string?> ValidateReversalStockAsync(
        KarobarDbContext db,
        List<PurchaseInvoiceItem> items,
        Dictionary<long, Product> trackedProducts,
        string verb,
        CancellationToken cancellationToken)
    {
        foreach (var prodGroup in items.GroupBy(i => i.ProductId))
        {
            var needQty = prodGroup.Sum(i => i.Quantity);
            if (needQty <= 0) continue;

            if (!trackedProducts.TryGetValue(prodGroup.Key, out var prod))
                continue;

            if (prod.Quantity - needQty < 0)
            {
                return $"Cannot {verb} invoice: Product '{prod.Name}' only has {prod.Quantity} units in stock, but {needQty} units need to be deducted (already sold or returned).";
            }
        }

        foreach (var batchGroup in items
            .Where(i => !string.IsNullOrWhiteSpace(i.Batch) && i.Expiry.HasValue)
            .GroupBy(i => new { i.ProductId, Batch = i.Batch!.Trim(), Expiry = i.Expiry!.Value.Date }))
        {
            var needBatchQty = batchGroup.Sum(i => i.Quantity);
            if (needBatchQty <= 0) continue;

            var batch = await FindTrackedBatchAsync(db, batchGroup.Key.ProductId, batchGroup.Key.Batch, batchGroup.Key.Expiry, cancellationToken);

            if (batch != null && batch.Quantity - needBatchQty < 0)
            {
                return $"Cannot {verb} invoice: Batch '{batchGroup.Key.Batch}' only has {batch.Quantity} units left in stock, cannot deduct {needBatchQty} units.";
            }
        }

        return null;
    }

    private static async Task RevertItemsStockAndCostAsync(
        KarobarDbContext db,
        List<PurchaseInvoiceItem> items,
        Dictionary<long, Product> trackedProducts,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (item.Quantity <= 0)
                continue;

            if (trackedProducts.TryGetValue(item.ProductId, out var prod))
            {
                var effCost = item.AvgPrice > 0m ? item.AvgPrice : item.UnitPurchasePrice;
                prod.AvgPrice = InvoiceCalculators.RevertPurchaseWeightedAverageCost(
                    prod.Quantity,
                    prod.AvgPrice,
                    item.Quantity,
                    effCost);
                prod.Quantity = Math.Max(0, prod.Quantity - item.Quantity);
                prod.Margin = InvoiceCalculators.ComputeProductMargin(prod.UnitSalePrice, prod.AvgPrice);
                prod.WholeSaleMargin = InvoiceCalculators.ComputeProductMargin(prod.WholeSaleUnitPrice ?? 0m, prod.AvgPrice);
                prod.UpdatedAt = now;
            }

            if (!string.IsNullOrWhiteSpace(item.Batch) && item.Expiry.HasValue)
            {
                var bNo = item.Batch.Trim();
                var exp = item.Expiry.Value.Date;
                var batch = await FindTrackedBatchAsync(db, item.ProductId, bNo, exp, cancellationToken);

                if (batch != null)
                {
                    batch.Quantity = Math.Max(0, batch.Quantity - item.Quantity);
                    batch.UpdatedAt = now;
                }
            }
        }
    }

    private static async Task<decimal> DetachInvoicePaymentAllocationsAsync(
        KarobarDbContext db,
        long invoiceId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var allocations = await db.PaymentAllocations
            .Where(pa => pa.InvoiceType == "purchase_invoice" && pa.InvoiceId == invoiceId)
            .ToListAsync(cancellationToken);

        if (allocations.Count == 0)
            return 0m;

        var totalDetached = 0m;
        var byPayment = allocations.GroupBy(a => a.PaymentId).ToList();
        foreach (var grp in byPayment)
        {
            var sumForPayment = grp.Sum(a => a.AllocatedAmount);
            var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == grp.Key, cancellationToken);
            if (payment != null && string.Equals(payment.Status, "posted", StringComparison.OrdinalIgnoreCase))
            {
                payment.AllocatedAmount = Math.Max(0m, Math.Round(payment.AllocatedAmount - sumForPayment, 2));
                payment.UnappliedAmount = Math.Round(payment.UnappliedAmount + sumForPayment, 2);
                payment.UpdatedAt = now;
                totalDetached += sumForPayment;
            }
        }

        db.PaymentAllocations.RemoveRange(allocations);
        return Math.Round(totalDetached, 2);
    }

    private static async Task CreateOrSyncSupplierLedgerForCreditInvoiceAsync(
        KarobarDbContext db,
        PurchaseInvoice invoice,
        decimal allocatedOnInvoice,
        long? actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var excessRef = $"Inv #{invoice.PostedNumber}";
        var oldExcessRows = await db.SupplierLedgers
            .Where(sl => sl.EntryType == "payment" && sl.PaymentReference == excessRef && sl.PaymentId == null)
            .ToListAsync(cancellationToken);
        if (oldExcessRows.Count > 0)
        {
            db.SupplierLedgers.RemoveRange(oldExcessRows);
        }

        var invTotal = invoice.TotalAmount;
        var directPaid = Math.Max(0m, Math.Round(invoice.TotalPaid - allocatedOnInvoice, 2));
        var directPaidOnInv = Math.Min(directPaid, invTotal);
        var creditRemaining = Math.Max(0m, Math.Round(invTotal - directPaidOnInv - allocatedOnInvoice, 2));
        var maxDirectNeeded = Math.Max(0m, Math.Round(invTotal - allocatedOnInvoice, 2));
        var excessPaid = Math.Max(0m, Math.Round(directPaid - maxDirectNeeded, 2));

        var primaryRow = await db.SupplierLedgers
            .FirstOrDefaultAsync(sl => sl.PurchaseInvoiceId == invoice.Id, cancellationToken);

        if (primaryRow == null)
        {
            primaryRow = new SupplierLedger
            {
                SupplierId = invoice.SupplierId,
                PurchaseInvoiceId = invoice.Id,
                EntryType = "invoice",
                PostedNumber = invoice.PostedNumber,
                InvoiceNumber = invoice.InvoiceNumber,
                EntryDate = invoice.PostedDate,
                DueDate = invoice.DueDate,
                Description = invoice.Remarks ?? $"Purchase Invoice {invoice.PostedNumber}",
                InvoiceTotal = invTotal,
                TotalPaid = directPaidOnInv,
                DebitedAmount = allocatedOnInvoice,
                CreditRemaining = creditRemaining,
                CreatedBy = actorUserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.SupplierLedgers.Add(primaryRow);
        }
        else
        {
            primaryRow.SupplierId = invoice.SupplierId;
            primaryRow.PostedNumber = invoice.PostedNumber;
            primaryRow.InvoiceNumber = invoice.InvoiceNumber;
            primaryRow.EntryDate = invoice.PostedDate;
            primaryRow.DueDate = invoice.DueDate;
            primaryRow.Description = invoice.Remarks ?? $"Purchase Invoice {invoice.PostedNumber}";
            primaryRow.InvoiceTotal = invTotal;
            primaryRow.TotalPaid = directPaidOnInv;
            primaryRow.DebitedAmount = allocatedOnInvoice;
            primaryRow.CreditRemaining = creditRemaining;
            primaryRow.UpdatedAt = now;
        }

        if (excessPaid > 0m)
        {
            db.SupplierLedgers.Add(new SupplierLedger
            {
                SupplierId = invoice.SupplierId,
                PurchaseInvoiceId = null,
                EntryType = "payment",
                PostedNumber = invoice.PostedNumber,
                InvoiceNumber = invoice.InvoiceNumber,
                EntryDate = invoice.PostedDate,
                PaymentReference = excessRef,
                Description = $"Advance / Excess payment on Purchase Invoice {invoice.PostedNumber}",
                InvoiceTotal = 0m,
                TotalPaid = 0m,
                DebitedAmount = excessPaid,
                CreditRemaining = 0m,
                CreatedBy = actorUserId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task DeleteInvoiceLedgerRowsAsync(
        KarobarDbContext db,
        long invoiceId,
        string postedNumber,
        CancellationToken cancellationToken)
    {
        var excessRef = $"Inv #{postedNumber}";
        var rows = await db.SupplierLedgers
            .Where(sl =>
                sl.PurchaseInvoiceId == invoiceId ||
                (sl.EntryType == "payment" && sl.PaymentReference == excessRef && sl.PaymentId == null))
            .ToListAsync(cancellationToken);

        if (rows.Count > 0)
        {
            db.SupplierLedgers.RemoveRange(rows);
        }
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
            "bank_transfer" or "cheque" => new[] { "bank" },
            "card" => new[] { "card", "bank" },
            "mobile_wallet" => new[] { "mobile_wallet" },
            _ => new[] { "cash_drawer", "cash", "bank", "other" }
        };

        var matched = await db.Accounts
            .Where(a => a.IsActive && expectedTypes.Contains(a.AccountType))
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (matched != null)
            return matched;

        var fallbackAny = await db.Accounts
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (fallbackAny != null)
            return fallbackAny;

        var createdDefault = new Account
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
        db.Accounts.Add(createdDefault);
        await db.SaveChangesAsync(cancellationToken);
        return createdDefault;
    }

    private static bool IsCashDrawerLinkedAccount(Account account, string paymentMethod)
    {
        if (!string.Equals(paymentMethod, "cash", StringComparison.OrdinalIgnoreCase))
            return false;

        return account.SyncWithCashDrawer ||
               string.Equals(account.AccountType, "cash_drawer", StringComparison.OrdinalIgnoreCase) ||
               (string.Equals(account.AccountType, "cash", StringComparison.OrdinalIgnoreCase) && account.IsDefault);
    }

    private static Task RecordAccountWithdrawalAsync(
        KarobarDbContext db,
        Account account,
        decimal amount,
        DateTime txDate,
        string paymentMethod,
        string referenceType,
        long referenceId,
        string referenceNumber,
        string counterpartyName,
        string description,
        long? actorUserId,
        DateTime now)
    {
        account.CurrentBalance = Math.Round(account.CurrentBalance - amount, 2);
        account.UpdatedAt = now;

        db.AccountTransactions.Add(new AccountTransaction
        {
            AccountId = account.Id,
            TransactionDate = txDate,
            Direction = "out",
            Amount = amount,
            BalanceAfter = account.CurrentBalance,
            Category = "purchase",
            PaymentMethod = paymentMethod,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ReferenceNumber = referenceNumber,
            CounterpartyName = counterpartyName,
            Description = description,
            CreatedBy = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        });
        return Task.CompletedTask;
    }

    private static Task RecordAccountDepositAsync(
        KarobarDbContext db,
        Account account,
        decimal amount,
        DateTime txDate,
        string paymentMethod,
        string referenceType,
        long referenceId,
        string referenceNumber,
        string counterpartyName,
        string description,
        long? actorUserId,
        DateTime now)
    {
        account.CurrentBalance = Math.Round(account.CurrentBalance + amount, 2);
        account.UpdatedAt = now;

        db.AccountTransactions.Add(new AccountTransaction
        {
            AccountId = account.Id,
            TransactionDate = txDate,
            Direction = "in",
            Amount = amount,
            BalanceAfter = account.CurrentBalance,
            Category = "purchase_refund",
            PaymentMethod = paymentMethod,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ReferenceNumber = referenceNumber,
            CounterpartyName = counterpartyName,
            Description = description,
            CreatedBy = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        });
        return Task.CompletedTask;
    }

    private static async Task SyncAccountAndDrawerOnUpdateAsync(
        KarobarDbContext db,
        PurchaseInvoice invoice,
        string supplierName,
        long? oldAccountId,
        string oldPaymentMethod,
        decimal oldDirectPaid,
        string newPaymentMethod,
        long? requestedNewAccountId,
        decimal newDirectPaid,
        CashDrawerSession? openSession,
        bool? updatePreShiftDrawer,
        long? actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        Account? oldAccount = oldAccountId.HasValue
            ? await db.Accounts.FirstOrDefaultAsync(a => a.Id == oldAccountId.Value, cancellationToken)
            : null;
        if (oldAccount == null && oldDirectPaid > 0m)
        {
            oldAccount = await ResolveOrEnsurePaymentAccountAsync(db, oldPaymentMethod, null, now, cancellationToken);
        }

        Account? newAccount = newDirectPaid > 0m || requestedNewAccountId.HasValue
            ? await ResolveOrEnsurePaymentAccountAsync(db, newPaymentMethod, requestedNewAccountId, now, cancellationToken)
            : oldAccount;

        invoice.AccountId = newAccount?.Id;

        bool isPreShift = openSession != null &&
                          invoice.CreatedAt.HasValue &&
                          openSession.OpenedAt > invoice.CreatedAt.Value;
        bool skipDrawerAccountLeg = isPreShift && updatePreShiftDrawer == false;

        if (oldAccount != null && newAccount != null && oldAccount.Id == newAccount.Id)
        {
            var delta = Math.Round(newDirectPaid - oldDirectPaid, 2);
            bool isDrawerAcc = IsCashDrawerLinkedAccount(newAccount, newPaymentMethod);
            if (Math.Abs(delta) > 0.009m && !(isDrawerAcc && skipDrawerAccountLeg))
            {
                if (delta > 0m)
                {
                    await RecordAccountWithdrawalAsync(
                        db, newAccount, delta, invoice.PostedDate, newPaymentMethod,
                        "purchase_invoice_update", invoice.Id, invoice.PostedNumber, supplierName,
                        $"Additional payment on Purchase Invoice {invoice.PostedNumber}", actorUserId, now);
                }
                else
                {
                    await RecordAccountDepositAsync(
                        db, newAccount, Math.Abs(delta), invoice.PostedDate, newPaymentMethod,
                        "purchase_invoice_update", invoice.Id, invoice.PostedNumber, supplierName,
                        $"Payment reduction refund on Purchase Invoice {invoice.PostedNumber}", actorUserId, now);
                }
            }
        }
        else
        {
            if (oldAccount != null && oldDirectPaid > 0m)
            {
                bool oldWasDrawer = IsCashDrawerLinkedAccount(oldAccount, oldPaymentMethod);
                if (!(oldWasDrawer && skipDrawerAccountLeg))
                {
                    await RecordAccountDepositAsync(
                        db, oldAccount, oldDirectPaid, invoice.PostedDate, oldPaymentMethod,
                        "purchase_invoice_update", invoice.Id, invoice.PostedNumber, supplierName,
                        $"Account switch refund for Purchase Invoice {invoice.PostedNumber}", actorUserId, now);
                }
            }

            if (newAccount != null && newDirectPaid > 0m)
            {
                bool newIsDrawer = IsCashDrawerLinkedAccount(newAccount, newPaymentMethod);
                if (!(newIsDrawer && skipDrawerAccountLeg))
                {
                    await RecordAccountWithdrawalAsync(
                        db, newAccount, newDirectPaid, invoice.PostedDate, newPaymentMethod,
                        "purchase_invoice_update", invoice.Id, invoice.PostedNumber, supplierName,
                        $"Payment on Purchase Invoice {invoice.PostedNumber}", actorUserId, now);
                }
            }
        }

        // Cash Drawer Sync
        if (openSession != null)
        {
            var sameShiftTx = await db.CashDrawerTransactions
                .FirstOrDefaultAsync(t =>
                    t.CashDrawerSessionId == openSession.Id &&
                    t.ReferenceType == "purchase_invoice" &&
                    t.ReferenceId == invoice.Id &&
                    t.Type == "cash_purchase",
                    cancellationToken);

            bool newUsesDrawer = newAccount != null && IsCashDrawerLinkedAccount(newAccount, newPaymentMethod);
            bool oldUsedDrawer = oldAccount != null && IsCashDrawerLinkedAccount(oldAccount, oldPaymentMethod);

            if (sameShiftTx != null)
            {
                var prevAmt = sameShiftTx.Amount;
                var targetAmt = newUsesDrawer ? newDirectPaid : 0m;
                if (targetAmt <= 0m)
                {
                    db.CashDrawerTransactions.Remove(sameShiftTx);
                }
                else
                {
                    sameShiftTx.Amount = targetAmt;
                    sameShiftTx.UpdatedAt = now;
                }
                openSession.ExpectedClosingBalance = Math.Round(openSession.ExpectedClosingBalance + prevAmt - targetAmt, 2);
                openSession.UpdatedAt = now;
            }
            else if (!isPreShift && newUsesDrawer && newDirectPaid > 0m)
            {
                db.CashDrawerTransactions.Add(new CashDrawerTransaction
                {
                    CashDrawerSessionId = openSession.Id,
                    Type = "cash_purchase",
                    Amount = newDirectPaid,
                    ReferenceType = "purchase_invoice",
                    ReferenceId = invoice.Id,
                    Description = $"Cash Purchase {invoice.PostedNumber} - {supplierName}",
                    CreatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                openSession.ExpectedClosingBalance = Math.Round(openSession.ExpectedClosingBalance - newDirectPaid, 2);
                openSession.UpdatedAt = now;
            }
            else if (isPreShift && updatePreShiftDrawer == true)
            {
                var oldDrawerAmt = oldUsedDrawer ? oldDirectPaid : 0m;
                var newDrawerAmt = newUsesDrawer ? newDirectPaid : 0m;
                var cashDelta = Math.Round(newDrawerAmt - oldDrawerAmt, 2);
                if (Math.Abs(cashDelta) > 0.009m)
                {
                    db.CashDrawerTransactions.Add(new CashDrawerTransaction
                    {
                        CashDrawerSessionId = openSession.Id,
                        Type = cashDelta > 0m ? "cash_purchase" : "cash_in",
                        Amount = Math.Abs(cashDelta),
                        ReferenceType = "purchase_invoice_edit",
                        ReferenceId = invoice.Id,
                        Description = $"Edited Pre-Shift Purchase Invoice {invoice.PostedNumber}",
                        CreatedBy = actorUserId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    openSession.ExpectedClosingBalance = Math.Round(openSession.ExpectedClosingBalance - cashDelta, 2);
                    openSession.UpdatedAt = now;
                }
            }
        }
    }

    private static async Task RefundInvoiceAccountAndDrawerAsync(
        KarobarDbContext db,
        PurchaseInvoice invoice,
        string supplierName,
        decimal refundAmount,
        bool isDeletion,
        long? actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var method = invoice.PaymentMethod ?? "cash";
        var account = await ResolveOrEnsurePaymentAccountAsync(db, method, invoice.AccountId, now, cancellationToken);

        var refType = isDeletion ? "purchase_invoice_deletion" : "purchase_invoice_reversal";
        var desc = isDeletion
            ? $"Refund from deleted Purchase Invoice {invoice.PostedNumber}"
            : $"Refund from reversed Purchase Invoice {invoice.PostedNumber}";

        await RecordAccountDepositAsync(
            db, account, refundAmount, DateTime.Today, method,
            refType, invoice.Id, invoice.PostedNumber, supplierName, desc, actorUserId, now);

        if (IsCashDrawerLinkedAccount(account, method))
        {
            var openSession = await db.CashDrawerSessions
                .Where(s => s.Status == "open")
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (openSession != null)
            {
                if (isDeletion)
                {
                    var sameShiftTx = await db.CashDrawerTransactions
                        .FirstOrDefaultAsync(t =>
                            t.CashDrawerSessionId == openSession.Id &&
                            t.ReferenceType == "purchase_invoice" &&
                            t.ReferenceId == invoice.Id &&
                            t.Type == "cash_purchase",
                            cancellationToken);

                    if (sameShiftTx != null)
                    {
                        openSession.ExpectedClosingBalance = Math.Round(openSession.ExpectedClosingBalance + sameShiftTx.Amount, 2);
                        openSession.UpdatedAt = now;
                        db.CashDrawerTransactions.Remove(sameShiftTx);
                        return;
                    }
                }

                db.CashDrawerTransactions.Add(new CashDrawerTransaction
                {
                    CashDrawerSessionId = openSession.Id,
                    Type = "cash_in",
                    Amount = refundAmount,
                    ReferenceType = isDeletion ? "purchase_invoice_deleted" : "purchase_invoice",
                    ReferenceId = invoice.Id,
                    Description = desc,
                    CreatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                openSession.ExpectedClosingBalance = Math.Round(openSession.ExpectedClosingBalance + refundAmount, 2);
                openSession.UpdatedAt = now;
            }
        }
    }

    private static async Task SyncPurchaseOrderReceivingAsync(
        KarobarDbContext db,
        PurchaseOrder order,
        long? actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var poItems = await db.PurchaseOrderItems
            .Where(i => i.PurchaseOrderId == order.Id)
            .ToListAsync(cancellationToken);

        var activeReceipts = await db.PurchaseInvoices.AsNoTracking()
            .Where(pi => pi.PurchaseOrderId == order.Id && (pi.Status == null || pi.Status != "reversed"))
            .OrderByDescending(pi => pi.Id)
            .ToListAsync(cancellationToken);

        var hasActiveReceipts = activeReceipts.Count > 0;
        var activeReceiptIds = activeReceipts.Select(r => r.Id).ToList();

        var receiptItems = hasActiveReceipts
            ? await db.PurchaseInvoiceItems.AsNoTracking()
                .Where(i => activeReceiptIds.Contains(i.PurchaseInvoiceId))
                .ToListAsync(cancellationToken)
            : [];

        bool allComplete = poItems.Count > 0;
        var shortItems = new List<(long ProductId, int ShortQty)>();

        foreach (var poItem in poItems)
        {
            var receivedUnits = receiptItems
                .Where(ri => ri.PurchaseOrderItemId == poItem.Id || (ri.PurchaseOrderItemId == null && ri.ProductId == poItem.ProductId))
                .Sum(ri => ri.UnitQuantity);

            poItem.QuantityReceived = receivedUnits;
            poItem.UpdatedAt = now;

            if (receivedUnits < poItem.QuantityOrdered)
            {
                allComplete = false;
                shortItems.Add((poItem.ProductId, poItem.QuantityOrdered - receivedUnits));
            }
        }

        var newStatus = hasActiveReceipts
            ? (allComplete ? "received" : "partially_received")
            : "ordered";

        order.Status = newStatus;
        if (hasActiveReceipts)
        {
            order.SupplierId = activeReceipts[0].SupplierId;
            order.ReceivedBy ??= actorUserId ?? order.CreatedBy;
            order.ReceivedAt ??= now;
        }
        else
        {
            order.ReceivedBy = null;
            order.ReceivedAt = null;
        }
        order.UpdatedAt = now;

        // Sync Undelivered User Demands
        var poRef = !string.IsNullOrWhiteSpace(order.OrderNumber) ? order.OrderNumber : $"PO-{order.Id:D5}";
        var demandNote = $"Undelivered from Purchase Order {poRef}";
        var existingPoDemands = await db.UserDemands
            .Where(ud => ud.Notes == demandNote)
            .ToListAsync(cancellationToken);

        if (newStatus == "partially_received")
        {
            var shortMap = shortItems.ToDictionary(x => x.ProductId, x => x.ShortQty);
            foreach (var oldDem in existingPoDemands)
            {
                if (!oldDem.ProductId.HasValue || !shortMap.ContainsKey(oldDem.ProductId.Value))
                {
                    db.UserDemands.Remove(oldDem);
                }
            }

            foreach (var (prodId, shortQty) in shortItems)
            {
                var match = existingPoDemands.FirstOrDefault(d => d.ProductId == prodId);
                if (match == null)
                {
                    var prodName = await db.Products.AsNoTracking()
                        .Where(p => p.Id == prodId)
                        .Select(p => p.Name)
                        .FirstOrDefaultAsync(cancellationToken);

                    db.UserDemands.Add(new UserDemand
                    {
                        ProductId = prodId,
                        ProductName = prodName,
                        Quantity = shortQty,
                        Notes = demandNote,
                        DemandDate = DateTime.Today,
                        Status = "pending",
                        CreatedBy = actorUserId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
                else
                {
                    match.Quantity = shortQty;
                    match.Status = "pending";
                    match.UpdatedAt = now;
                }
            }
        }
        else if (existingPoDemands.Count > 0)
        {
            db.UserDemands.RemoveRange(existingPoDemands);
        }
    }

    private static async Task RememberSupplierProductSortingAsync(
        KarobarDbContext db,
        long supplierId,
        List<long> orderedProductIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var distinctIncoming = orderedProductIds.Where(id => id > 0).Distinct().ToList();
        if (distinctIncoming.Count == 0)
            return;

        var existingRows = await db.SupplierProductPositions
            .Where(p => p.SupplierId == supplierId)
            .OrderBy(p => p.Position)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        var remainingOtherIds = existingRows
            .Select(r => r.ProductId)
            .Where(pid => !distinctIncoming.Contains(pid))
            .Distinct()
            .ToList();

        var finalSequence = distinctIncoming.Concat(remainingOtherIds).ToList();
        var existingByProd = existingRows.ToDictionary(r => r.ProductId);

        for (int i = 0; i < finalSequence.Count; i++)
        {
            var pid = finalSequence[i];
            if (existingByProd.TryGetValue(pid, out var row))
            {
                row.Position = i + 1;
                row.UpdatedAt = now;
            }
            else
            {
                db.SupplierProductPositions.Add(new SupplierProductPosition
                {
                    SupplierId = supplierId,
                    ProductId = pid,
                    Position = i + 1,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }
    }

    private static async Task<string> ComputeNextPostedNumberInternalAsync(
        KarobarDbContext db,
        CancellationToken cancellationToken)
    {
        var codes = await db.PurchaseInvoices.AsNoTracking()
            .Where(pi => pi.PostedNumber != null && EF.Functions.Like(pi.PostedNumber, "PRINV-%"))
            .OrderByDescending(pi => pi.Id)
            .Take(50)
            .Select(pi => pi.PostedNumber)
            .ToListAsync(cancellationToken);

        long maxNum = 0;
        foreach (var c in codes)
        {
            var m = Regex.Match(c ?? string.Empty, @"^PRINV-(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success && long.TryParse(m.Groups[1].Value, out var n) && n > maxNum)
            {
                maxNum = n;
            }
        }

        var next = maxNum + 1;
        return next < 10000 ? $"PRINV-{next:D4}" : $"PRINV-{next}";
    }

    private static async Task<string> ComputeNextDraftNumberAsync(
        KarobarDbContext db,
        CancellationToken cancellationToken)
    {
        var codes = await db.InvoiceDrafts.AsNoTracking()
            .Where(d => EF.Functions.Like(d.DraftNumber, "DRF-PUR-%"))
            .OrderByDescending(d => d.Id)
            .Take(50)
            .Select(d => d.DraftNumber)
            .ToListAsync(cancellationToken);

        long maxNum = 0;
        foreach (var c in codes)
        {
            var m = Regex.Match(c ?? string.Empty, @"^DRF-PUR-(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success && long.TryParse(m.Groups[1].Value, out var n) && n > maxNum)
            {
                maxNum = n;
            }
        }

        return $"DRF-PUR-{maxNum + 1:D4}";
    }

    private static async Task<List<PurchaseSupplierLookupDto>> BuildSupplierLookupsAsync(
        KarobarDbContext db,
        long? specificSupplierId,
        CancellationToken cancellationToken)
    {
        var supQuery = db.Suppliers.AsNoTracking().AsQueryable();
        if (specificSupplierId is > 0)
        {
            var sid = specificSupplierId.Value;
            supQuery = supQuery.Where(s => s.Id == sid);
        }

        var suppliers = await supQuery
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        if (suppliers.Count == 0)
            return [];

        var supplierIds = suppliers.Select(s => s.Id).ToList();
        var ledgerRows = await db.SupplierLedgers.AsNoTracking()
            .Where(sl => supplierIds.Contains(sl.SupplierId))
            .Select(sl => new
            {
                sl.SupplierId,
                sl.EntryType,
                sl.DueDate,
                sl.CreditRemaining,
                sl.DebitedAmount,
                sl.TotalPaid
            })
            .ToListAsync(cancellationToken);

        var positions = await db.SupplierProductPositions.AsNoTracking()
            .Where(p => supplierIds.Contains(p.SupplierId))
            .OrderBy(p => p.Position)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        var posBySupplier = positions
            .GroupBy(p => p.SupplierId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ProductId).ToList());

        var ledgerBySupplier = ledgerRows
            .GroupBy(l => l.SupplierId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var today = DateTime.Today;
        var results = new List<PurchaseSupplierLookupDto>(suppliers.Count);

        foreach (var s in suppliers)
        {
            decimal netPayable = 0m;
            int overdueCount = 0;
            decimal overdueAmt = 0m;

            if (ledgerBySupplier.TryGetValue(s.Id, out var rows))
            {
                foreach (var r in rows)
                {
                    if (r.EntryType == "invoice")
                    {
                        netPayable += r.CreditRemaining;
                        if (r.CreditRemaining > 0.009m && r.DueDate.HasValue && r.DueDate.Value.Date < today)
                        {
                            overdueCount++;
                            overdueAmt += r.CreditRemaining;
                        }
                    }
                    else if (r.EntryType == "payment" || r.EntryType == "return")
                    {
                        netPayable -= (r.DebitedAmount + r.TotalPaid);
                    }
                }
            }

            results.Add(new PurchaseSupplierLookupDto
            {
                Id = s.Id,
                Name = s.Name,
                Address = s.Address ?? string.Empty,
                Phone = s.Phone ?? string.Empty,
                CreditDays = s.CreditDays is > 0 ? s.CreditDays.Value : 30,
                CreditLimit = s.CreditLimit ?? 0m,
                CurrentPayable = Math.Round(netPayable, 2),
                OverdueCount = overdueCount,
                OverdueAmount = Math.Round(overdueAmt, 2),
                ExpiryReturnPolicy = s.ExpiryReturnPolicy ?? "before_expiry",
                ExpiryReturnMonths = s.ExpiryReturnMonths ?? 3,
                ExpiryReturnDays = s.ExpiryReturnDays ?? 90,
                ExpiryPolicyLabel = FormatExpiryPolicyLabel(s.ExpiryReturnPolicy, s.ExpiryReturnMonths, s.ExpiryReturnDays),
                SavedProductOrderIds = posBySupplier.TryGetValue(s.Id, out var pids) ? pids : []
            });
        }

        return results;
    }

    private static async Task<List<ReceivablePurchaseOrderLookupDto>> BuildReceivableOrdersAsync(
        KarobarDbContext db,
        CancellationToken cancellationToken)
    {
        var orders = await db.PurchaseOrders.AsNoTracking()
            .Where(po => po.Status == "ordered" || po.Status == "partially_received" || po.Status == "draft" || po.Status == "sent")
            .OrderByDescending(po => po.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
            return [];

        var orderIds = orders.Select(o => o.Id).ToList();
        var supplierIds = orders.Select(o => o.SupplierId).Distinct().ToList();

        var supplierMap = await db.Suppliers.AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var orderItems = await db.PurchaseOrderItems.AsNoTracking()
            .Where(i => orderIds.Contains(i.PurchaseOrderId))
            .ToListAsync(cancellationToken);

        var activeReceipts = await db.PurchaseInvoices.AsNoTracking()
            .Where(pi => pi.PurchaseOrderId != null &&
                         orderIds.Contains(pi.PurchaseOrderId.Value) &&
                         (pi.Status == null || pi.Status != "reversed"))
            .Select(pi => new { pi.Id, PoId = pi.PurchaseOrderId!.Value })
            .ToListAsync(cancellationToken);

        var activeReceiptIds = activeReceipts.Select(r => r.Id).ToList();
        var receiptLines = activeReceiptIds.Count > 0
            ? await db.PurchaseInvoiceItems.AsNoTracking()
                .Where(i => activeReceiptIds.Contains(i.PurchaseInvoiceId))
                .ToListAsync(cancellationToken)
            : [];

        var receiptPoMap = activeReceipts.ToDictionary(r => r.Id, r => r.PoId);

        var results = new List<ReceivablePurchaseOrderLookupDto>();
        foreach (var po in orders)
        {
            var poLines = orderItems.Where(i => i.PurchaseOrderId == po.Id).ToList();
            if (poLines.Count == 0)
                continue;

            var poReceipts = receiptLines
                .Where(rl => receiptPoMap.TryGetValue(rl.PurchaseInvoiceId, out var pid) && pid == po.Id)
                .ToList();

            int remItems = 0;
            int remUnits = 0;

            foreach (var pl in poLines)
            {
                var recPaid = poReceipts
                    .Where(rl => rl.PurchaseOrderItemId == pl.Id || (rl.PurchaseOrderItemId == null && rl.ProductId == pl.ProductId))
                    .Sum(rl => rl.UnitQuantity);
                var diff = pl.QuantityOrdered - recPaid;
                if (diff > 0)
                {
                    remItems++;
                    remUnits += diff;
                }
            }

            if (remUnits <= 0)
                continue;

            results.Add(new ReceivablePurchaseOrderLookupDto
            {
                Id = po.Id,
                OrderNumber = !string.IsNullOrWhiteSpace(po.OrderNumber) ? po.OrderNumber : $"PO-{po.Id:D5}",
                SupplierId = po.SupplierId,
                SupplierName = supplierMap.TryGetValue(po.SupplierId, out var sName) ? sName : $"Supplier #{po.SupplierId}",
                OrderDate = po.OrderDate,
                Status = po.Status ?? "ordered",
                RemainingItemsCount = remItems,
                RemainingUnits = remUnits
            });
        }

        return results;
    }

    private static async Task<List<PurchaseProductSearchItemDto>> BuildProductSearchDtosAsync(
        KarobarDbContext db,
        List<Product> products,
        long? supplierId,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0)
            return [];

        var productIds = products.Select(p => p.Id).ToList();
        var brandIds = products.Where(p => p.BrandId.HasValue).Select(p => p.BrandId!.Value).Distinct().ToList();
        var catIds = products.Where(p => p.CategoryId.HasValue).Select(p => p.CategoryId!.Value).Distinct().ToList();
        var supIds = products.Where(p => p.SupplierId.HasValue).Select(p => p.SupplierId!.Value).Distinct().ToList();

        var brandMap = brandIds.Count > 0
            ? await db.Brands.AsNoTracking()
                .Where(b => brandIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken)
            : new Dictionary<long, string>();

        var catMap = catIds.Count > 0
            ? await db.Categories.AsNoTracking()
                .Where(c => catIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            : new Dictionary<long, string>();

        var supNameMap = supIds.Count > 0
            ? await db.Suppliers.AsNoTracking()
                .Where(s => supIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken)
            : new Dictionary<long, string>();

        var allBatches = await db.Batches.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId))
            .ToListAsync(cancellationToken);

        var batchesByProduct = allBatches
            .GroupBy(b => b.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(b => b.ExpiryDate == null ? 1 : 0)
                      .ThenBy(b => b.ExpiryDate)
                      .ThenBy(b => b.Id)
                      .ToList());

        var supplierCostMap = new Dictionary<long, decimal?>();
        if (supplierId is > 0)
        {
            var sid = supplierId.Value;
            supplierCostMap = await db.ProductSuppliers.AsNoTracking()
                .Where(ps => ps.SupplierId == sid && productIds.Contains(ps.ProductId))
                .ToDictionaryAsync(ps => ps.ProductId, ps => ps.LastPurchasePrice, cancellationToken);
        }

        var dtos = new List<PurchaseProductSearchItemDto>(products.Count);
        foreach (var p in products)
        {
            var rawBatches = batchesByProduct.TryGetValue(p.Id, out var bList) ? bList : [];
            var batchDtos = rawBatches.Select(b => new PurchaseProductBatchOptionDto
            {
                Id = b.Id,
                BatchNumber = b.BatchNumber ?? string.Empty,
                ExpiryDate = b.ExpiryDate,
                Quantity = b.Quantity,
                UnitPurchasePrice = b.UnitPurchasePrice ?? p.UnitPurchasePrice,
                UnitSalePrice = b.UnitSalePrice ?? p.UnitSalePrice,
                WholeSaleUnitPrice = b.WholeSaleUnitPrice ?? p.WholeSaleUnitPrice ?? p.UnitSalePrice
            }).ToList();

            dtos.Add(new PurchaseProductSearchItemDto
            {
                Id = p.Id,
                ProductCode = p.ProductCode ?? string.Empty,
                Barcode = p.Barcode ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Formulation = p.Formulation ?? string.Empty,
                BrandId = p.BrandId,
                BrandName = p.BrandId.HasValue && brandMap.TryGetValue(p.BrandId.Value, out var bName) ? bName : string.Empty,
                CategoryName = p.CategoryId.HasValue && catMap.TryGetValue(p.CategoryId.Value, out var cName) ? cName : string.Empty,
                SupplierName = p.SupplierId.HasValue && supNameMap.TryGetValue(p.SupplierId.Value, out var supName) ? supName : string.Empty,
                Rack = p.Rack ?? string.Empty,
                StorageLocationId = p.StorageLocationId,
                PackSize = p.PackSize > 0 ? p.PackSize : 1,
                Quantity = p.Quantity,
                PackPurchasePrice = p.PackPurchasePrice,
                UnitPurchasePrice = p.UnitPurchasePrice,
                PackSalePrice = p.PackSalePrice,
                UnitSalePrice = p.UnitSalePrice,
                WholeSalePackPrice = p.WholeSalePackPrice ?? p.PackSalePrice,
                WholeSaleUnitPrice = p.WholeSaleUnitPrice ?? p.UnitSalePrice,
                AvgPrice = p.AvgPrice,
                Margin = p.Margin,
                WholeSaleMargin = p.WholeSaleMargin,
                LastSupplierUnitCost = supplierCostMap.TryGetValue(p.Id, out var sc) ? sc : null,
                Batches = batchDtos
            });
        }

        return dtos;
    }

    private static Dictionary<long, int> ComputeReturnableQuantities(
        List<PurchaseInvoiceItem> invoiceItems,
        List<PurchaseReturnItem> returnLines)
    {
        var remaining = invoiceItems.ToDictionary(i => i.Id, i => Math.Max(0, i.Quantity));
        if (returnLines.Count == 0)
            return remaining;

        foreach (var ret in returnLines)
        {
            int toDeduct = Math.Max(0, ret.ReturnUnitQuantity);
            if (toDeduct == 0) continue;

            var rBatch = (ret.Batch ?? string.Empty).Trim();
            var rExp = ret.Expiry?.Date;

            var exactCandidates = invoiceItems.Where(ii =>
                ii.ProductId == ret.ProductId &&
                string.Equals((ii.Batch ?? string.Empty).Trim(), rBatch, StringComparison.OrdinalIgnoreCase) &&
                ii.Expiry?.Date == rExp).ToList();

            var candidates = exactCandidates.Count > 0
                ? exactCandidates
                : invoiceItems.Where(ii => ii.ProductId == ret.ProductId).ToList();

            foreach (var cand in candidates)
            {
                if (toDeduct <= 0) break;
                var avail = remaining[cand.Id];
                var take = Math.Min(avail, toDeduct);
                remaining[cand.Id] = avail - take;
                toDeduct -= take;
            }
        }

        return remaining;
    }

    private static async Task<List<PurchaseDocumentHistoryEntryDto>> GetDocumentHistoryInternalAsync(
        KarobarDbContext db,
        long invoiceId,
        CancellationToken cancellationToken)
    {
        var rows = await db.DocumentHistories.AsNoTracking()
            .Where(h => h.DocumentType == "purchase_invoice" && h.DocumentId == invoiceId)
            .OrderByDescending(h => h.Id)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        var userIds = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
        var userMap = userIds.Count > 0
            ? await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => !string.IsNullOrWhiteSpace(u.Name) ? u.Name : u.Username, cancellationToken)
            : new Dictionary<long, string>();

        return rows.Select(r => new PurchaseDocumentHistoryEntryDto
        {
            Id = r.Id,
            Action = r.Action ?? "created",
            UserName = r.UserId.HasValue && userMap.TryGetValue(r.UserId.Value, out var uName) ? uName : "Admin User",
            CreatedAt = r.CreatedAt ?? DateTime.UtcNow,
            Reason = r.Reason ?? string.Empty,
            ChangesSummary = r.ChangesSummary ?? string.Empty
        }).ToList();
    }

    private static string MakeStockBucketKey(long productId, string? batch, DateTime? expiry, int packSize)
    {
        var b = (batch ?? string.Empty).Trim().ToUpperInvariant();
        var e = expiry.HasValue ? expiry.Value.Date.ToString("yyyy-MM-dd") : string.Empty;
        return $"{productId}|{b}|{e}|{(packSize > 0 ? packSize : 1)}";
    }

    private static string BuildAuditSnapshotJson(
        PurchaseInvoice invoice,
        string supplierName,
        List<(string ProductName, string Batch, DateTime? Expiry, PurchaseLineCalcModel Calc)> lines)
    {
        // Includes batch_number properly populated (fixing web app DocumentAuditService bug #8!)
        var payload = new
        {
            invoice.Id,
            invoice.PostedNumber,
            PostedDate = invoice.PostedDate.ToString("yyyy-MM-dd"),
            DueDate = invoice.DueDate?.ToString("yyyy-MM-dd"),
            invoice.SupplierId,
            SupplierName = supplierName,
            invoice.InvoiceNumber,
            invoice.InvoiceType,
            invoice.Status,
            invoice.InvoiceAmount,
            invoice.GrossTotal,
            invoice.DiscountPercentage,
            invoice.DiscountAmount,
            invoice.TaxPercentage,
            invoice.TaxAmount,
            invoice.TotalAmount,
            invoice.TotalPaid,
            invoice.PaymentMethod,
            invoice.ReversalReason,
            Items = lines.Select(l => new
            {
                ProductName = l.ProductName,
                BatchNumber = string.IsNullOrWhiteSpace(l.Batch) ? null : l.Batch,
                ExpiryDate = l.Expiry?.ToString("yyyy-MM-dd"),
                l.Calc.PackSize,
                l.Calc.PackQuantity,
                l.Calc.UnitQuantity,
                l.Calc.PackBonus,
                l.Calc.UnitBonus,
                l.Calc.Quantity,
                l.Calc.UnitPurchasePrice,
                l.Calc.UnitSalePrice,
                l.Calc.ItemDiscountPercentage,
                l.Calc.SubTotal
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string BuildAuditSnapshotJsonFromEntities(
        PurchaseInvoice invoice,
        string supplierName,
        List<PurchaseInvoiceItem> items,
        Dictionary<long, Product> products)
    {
        var payload = new
        {
            invoice.Id,
            invoice.PostedNumber,
            PostedDate = invoice.PostedDate.ToString("yyyy-MM-dd"),
            DueDate = invoice.DueDate?.ToString("yyyy-MM-dd"),
            invoice.SupplierId,
            SupplierName = supplierName,
            invoice.InvoiceNumber,
            invoice.InvoiceType,
            invoice.Status,
            invoice.InvoiceAmount,
            invoice.GrossTotal,
            invoice.DiscountPercentage,
            invoice.DiscountAmount,
            invoice.TaxPercentage,
            invoice.TaxAmount,
            invoice.TotalAmount,
            invoice.TotalPaid,
            invoice.PaymentMethod,
            invoice.ReversalReason,
            Items = items.Select(i => new
            {
                i.Id,
                i.ProductId,
                ProductName = products.TryGetValue(i.ProductId, out var p) ? p.Name : $"Product #{i.ProductId}",
                BatchNumber = i.Batch,
                ExpiryDate = i.Expiry?.ToString("yyyy-MM-dd"),
                i.PackSize,
                i.PackQuantity,
                i.UnitQuantity,
                i.PackBonus,
                i.UnitBonus,
                i.Quantity,
                i.UnitPurchasePrice,
                i.UnitSalePrice,
                i.ItemDiscountPercentage,
                i.SubTotal
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string FormatExpiryPolicyLabel(string? policy, int? months, int? days)
    {
        var clean = (policy ?? "before_expiry").Trim().ToLowerInvariant();
        if (clean == "accepts_expired")
            return "Expired returnable";
        if (clean == "non_returnable")
            return "Non-returnable";

        var m = months ?? 0;
        var d = days ?? 0;
        if (m > 0)
            return m == 1 ? "1 month before expiry" : $"{m} months before expiry";
        if (d > 0)
            return $"{d} days before expiry";
        return "3 months before expiry";
    }
}
