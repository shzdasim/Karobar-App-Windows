using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Karobar.Application.Interfaces;
using Karobar.Domain.Entities;
using Karobar.Domain.Formulas;
using Karobar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Services;

public sealed class SaleInvoiceService : ISaleInvoiceService
{
    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;
    private readonly IAuthService _authService;

    private static readonly Regex WalkInRegex = new(@"walk[\s\-_]*in", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SaleInvoiceService(
        IDbContextFactory<KarobarDbContext> dbFactory,
        IAuthService authService)
    {
        _dbFactory = dbFactory;
        _authService = authService;
    }

    public static bool IsWalkInCustomerName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && WalkInRegex.IsMatch(name);

    // =========================================================================
    // 1. BOOTSTRAP, LOOKUPS & DIRECTORY
    // =========================================================================

    public async Task<SaleFormBootstrapDto> GetFormBootstrapAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var nextCode = await ComputeNextPostedNumberInternalAsync(db, cancellationToken);
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        var storeSettings = new SaleStoreSettingsDto
        {
            StoreName = !string.IsNullOrWhiteSpace(setting?.StoreName) ? setting.StoreName! : "Karobar App",
            Address = setting?.Address ?? string.Empty,
            PhoneNumber = setting?.PhoneNumber ?? string.Empty,
            LicenseNumber = setting?.LicenseNumber ?? string.Empty,
            SaleSystem = !string.IsNullOrWhiteSpace(setting?.SaleSystem) ? setting.SaleSystem : "retail_wholesale",
            ShopType = !string.IsNullOrWhiteSpace(setting?.ShopType) ? setting.ShopType : "pharmacy",
            CurrencySymbol = !string.IsNullOrWhiteSpace(setting?.CurrencySymbol) ? setting.CurrencySymbol : "Rs.",
            PrinterType = !string.IsNullOrWhiteSpace(setting?.PrinterType) ? setting.PrinterType : "thermal",
            ThermalTemplate = !string.IsNullOrWhiteSpace(setting?.ThermalTemplate) ? setting.ThermalTemplate : "standard",
            A4Template = !string.IsNullOrWhiteSpace(setting?.A4Template) ? setting.A4Template : "standard",
            FooterNote = !string.IsNullOrWhiteSpace(setting?.FooterNote) ? setting.FooterNote! : "Thank you for your business!"
        };

        var customers = await BuildCustomerLookupsAsync(db, null, cancellationToken);
        var walkInId = customers.FirstOrDefault(c => c.IsWalkIn)?.Id
                       ?? customers.FirstOrDefault()?.Id
                       ?? 0L;

        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        var accountDtos = accounts.Select(a => new SaleAccountLookupDto
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

        var heldDraftsCount = await db.InvoiceDrafts.AsNoTracking()
            .CountAsync(d =>
                (d.InvoiceKind == "sale_retail" || d.InvoiceKind == "sale_wholesale") &&
                d.ConvertedInvoiceId == null &&
                d.DeletedAt == null,
                cancellationToken);

        var productSearchLayout = await ReadModalWindowLayoutInternalAsync(db, "productSearchModal", null, cancellationToken);
        var quickAddProductLayout = await ReadModalWindowLayoutInternalAsync(db, "productModal", null, cancellationToken);

        return new SaleFormBootstrapDto
        {
            NextPostedNumber = nextCode,
            StoreSettings = storeSettings,
            Customers = customers,
            WalkInCustomerId = walkInId,
            Accounts = accountDtos,
            HasOpenCashDrawer = openSession != null,
            OpenCashDrawerOpenedAt = openSession?.OpenedAt,
            OpenCashDrawerExpectedBalance = openSession?.ExpectedClosingBalance ?? 0m,
            HeldDraftsCount = heldDraftsCount,
            UserMaxDiscountPercentage = 100m,
            ProductSearchModalLayout = productSearchLayout,
            QuickAddProductModalLayout = quickAddProductLayout
        };
    }

    public async Task<string> GenerateNextPostedNumberAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await ComputeNextPostedNumberInternalAsync(db, cancellationToken);
    }

    public async Task<SaleInvoiceDirectoryPageDto> GetDirectoryPageAsync(
        SaleInvoiceFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var customerMap = await db.Customers.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => (c.Name, Phone: c.Phone ?? string.Empty), cancellationToken);

        var baseQuery = db.SaleInvoices.AsNoTracking().AsQueryable();

        if (query.CustomerId is > 0)
        {
            baseQuery = baseQuery.Where(si => si.CustomerId == query.CustomerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SaleType) &&
            !string.Equals(query.SaleType, "all", StringComparison.OrdinalIgnoreCase))
        {
            var saleTypeFilter = query.SaleType.Trim().ToLowerInvariant();
            baseQuery = baseQuery.Where(si => si.SaleType != null && si.SaleType.ToLower() == saleTypeFilter);
        }

        if (!string.IsNullOrWhiteSpace(query.InvoiceType) &&
            !string.Equals(query.InvoiceType, "all", StringComparison.OrdinalIgnoreCase))
        {
            var invTypeFilter = query.InvoiceType.Trim().ToLowerInvariant();
            baseQuery = baseQuery.Where(si => si.InvoiceType != null && si.InvoiceType.ToLower() == invTypeFilter);
        }

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            !string.Equals(query.Status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var statusFilter = query.Status.Trim().ToLowerInvariant();
            if (statusFilter == "reversed")
                baseQuery = baseQuery.Where(si => si.Status == "reversed");
            else
                baseQuery = baseQuery.Where(si => si.Status == null || si.Status != "reversed");
        }

        if (query.FromDate.HasValue)
        {
            var fromDate = query.FromDate.Value.Date;
            baseQuery = baseQuery.Where(si => si.Date >= fromDate);
        }

        if (query.ToDate.HasValue)
        {
            var toDateExclusive = query.ToDate.Value.Date.AddDays(1);
            baseQuery = baseQuery.Where(si => si.Date < toDateExclusive);
        }

        if (!string.IsNullOrWhiteSpace(query.CustomerSearch))
        {
            var custTerm = query.CustomerSearch.Trim();
            var matchingCustIds = customerMap
                .Where(kv =>
                    kv.Value.Name.Contains(custTerm, StringComparison.OrdinalIgnoreCase) ||
                    kv.Value.Phone.Contains(custTerm, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
            baseQuery = baseQuery.Where(si => matchingCustIds.Contains(si.CustomerId));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var matchingCustIds = customerMap
                .Where(kv =>
                    kv.Value.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    kv.Value.Phone.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();

            baseQuery = baseQuery.Where(si =>
                EF.Functions.Like(si.PostedNumber, $"%{term}%") ||
                (si.DoctorName != null && EF.Functions.Like(si.DoctorName, $"%{term}%")) ||
                (si.PatientName != null && EF.Functions.Like(si.PatientName, $"%{term}%")) ||
                (si.Remarks != null && EF.Functions.Like(si.Remarks, $"%{term}%")) ||
                matchingCustIds.Contains(si.CustomerId));
        }

        var allFilteredHeaders = await baseQuery
            .OrderByDescending(si => si.Id)
            .Select(si => new
            {
                si.Id,
                si.PostedNumber,
                si.Date,
                si.DueDate,
                si.CustomerId,
                si.DoctorName,
                si.PatientName,
                si.SaleType,
                si.WholesaleType,
                si.InvoiceType,
                si.Status,
                si.GrossAmount,
                si.ItemDiscount,
                si.DiscountPercentage,
                si.DiscountAmount,
                si.TaxPercentage,
                si.TaxAmount,
                si.Total,
                si.TotalReceive,
                si.RemainThisBill,
                si.PaymentMethod,
                si.ReversalReason,
                si.ReversedAt
            })
            .ToListAsync(cancellationToken);

        int totalCount = allFilteredHeaders.Count;
        int pageSize = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, 200);
        int totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));
        int page = Math.Clamp(query.Page <= 0 ? 1 : query.Page, 1, totalPages);

        var activeRows = allFilteredHeaders
            .Where(h => !string.Equals(h.Status, "reversed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int retailCount = activeRows.Count(h => !string.Equals(h.SaleType, "wholesale", StringComparison.OrdinalIgnoreCase));
        int wholesaleCount = activeRows.Count(h => string.Equals(h.SaleType, "wholesale", StringComparison.OrdinalIgnoreCase));

        decimal totalPostedAmt = activeRows.Sum(h => h.Total);
        decimal totalReceivedAmt = activeRows.Sum(h =>
            !string.Equals(h.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase) && h.TotalReceive <= 0m
                ? h.Total
                : h.TotalReceive);
        decimal totalRemainingAmt = activeRows.Sum(h =>
            string.Equals(h.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0m, h.Total - h.TotalReceive)
                : 0m);

        var pageSlice = allFilteredHeaders
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var pageIds = pageSlice.Select(x => x.Id).ToList();
        var pageItems = pageIds.Count > 0
            ? await db.SaleInvoiceItems.AsNoTracking()
                .Where(i => pageIds.Contains(i.SaleInvoiceId))
                .Select(i => new
                {
                    i.SaleInvoiceId,
                    i.Quantity,
                    i.Bonus,
                    i.CostPriceSnapshot,
                    i.SubTotal
                })
                .ToListAsync(cancellationToken)
            : [];

        var itemStatsByInvoice = pageItems
            .GroupBy(i => i.SaleInvoiceId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var count = g.Count();
                    var cost = g.Sum(x => (x.Quantity + x.Bonus) * (x.CostPriceSnapshot ?? 0m));
                    return (Count: count, Cost: Math.Round(cost, 2));
                });

        var heldDraftsCount = await db.InvoiceDrafts.AsNoTracking()
            .CountAsync(d =>
                (d.InvoiceKind == "sale_retail" || d.InvoiceKind == "sale_wholesale") &&
                d.ConvertedInvoiceId == null &&
                d.DeletedAt == null,
                cancellationToken);

        var items = pageSlice.Select((h, idx) =>
        {
            var isCredit = string.Equals(h.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
            var effectiveRecv = !isCredit && h.TotalReceive <= 0m ? h.Total : h.TotalReceive;
            var remaining = isCredit ? Math.Max(0m, Math.Round(h.Total - h.TotalReceive, 2)) : 0m;
            var custInfo = customerMap.TryGetValue(h.CustomerId, out var cVal)
                ? cVal
                : (Name: $"Customer #{h.CustomerId}", Phone: string.Empty);

            var stats = itemStatsByInvoice.TryGetValue(h.Id, out var st) ? st : (Count: 0, Cost: 0m);
            var profit = Math.Round(h.Total - stats.Cost, 2);
            var marginPct = h.Total > 0m ? Math.Round((profit / h.Total) * 100m, 1) : 0m;

            return new SaleInvoiceDirectoryRowDto
            {
                RowIndex = ((page - 1) * pageSize) + idx + 1,
                Id = h.Id,
                PostedNumber = h.PostedNumber,
                Date = h.Date,
                DueDate = h.DueDate,
                CustomerId = h.CustomerId,
                CustomerName = custInfo.Name,
                CustomerPhone = custInfo.Phone,
                DoctorName = h.DoctorName ?? string.Empty,
                PatientName = h.PatientName ?? string.Empty,
                SaleType = string.IsNullOrWhiteSpace(h.SaleType) ? "retail" : h.SaleType,
                WholesaleType = string.IsNullOrWhiteSpace(h.WholesaleType) ? "unit" : h.WholesaleType!,
                InvoiceType = string.IsNullOrWhiteSpace(h.InvoiceType) ? "debit" : h.InvoiceType,
                Status = string.IsNullOrWhiteSpace(h.Status) ? "posted" : h.Status,
                GrossAmount = h.GrossAmount,
                ItemDiscount = h.ItemDiscount,
                DiscountPercentage = h.DiscountPercentage,
                DiscountAmount = h.DiscountAmount,
                TaxPercentage = h.TaxPercentage,
                TaxAmount = h.TaxAmount,
                Total = h.Total,
                TotalReceive = effectiveRecv,
                RemainingAmount = remaining,
                PaymentMethod = h.PaymentMethod ?? "cash",
                ItemsCount = stats.Count,
                EstimatedCost = stats.Cost,
                EstimatedProfit = profit,
                EstimatedMarginPct = marginPct,
                ReversalReason = h.ReversalReason,
                ReversedAt = h.ReversedAt
            };
        }).ToList();

        return new SaleInvoiceDirectoryPageDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPostedCount = activeRows.Count,
            RetailCount = retailCount,
            WholesaleCount = wholesaleCount,
            TotalPostedAmount = Math.Round(totalPostedAmt, 2),
            TotalReceivedAmount = Math.Round(totalReceivedAmt, 2),
            TotalRemainingReceivable = Math.Round(totalRemainingAmt, 2),
            TotalEstimatedProfit = Math.Round(items.Where(i => !i.IsReversed).Sum(i => i.EstimatedProfit), 2),
            HeldDraftsCount = heldDraftsCount
        };
    }

    public async Task<SaleCustomerLookupDto?> GetCustomerSummaryAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        if (customerId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var list = await BuildCustomerLookupsAsync(db, customerId, cancellationToken);
        return list.FirstOrDefault();
    }

    public async Task<SaleCustomerLookupDto> QuickCreateCustomerAsync(
        string name,
        string? phone,
        string customerType = "retail",
        CancellationToken cancellationToken = default)
    {
        var cleanName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanName))
            throw new InvalidOperationException("Customer name is required.");

        var cleanPhone = phone?.Trim();
        var cleanType = string.Equals(customerType, "wholesale", StringComparison.OrdinalIgnoreCase)
            ? "wholesale"
            : "retail";

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // If phone matches an existing customer, return it or create new if distinct name
        if (!string.IsNullOrWhiteSpace(cleanPhone))
        {
            var existingByPhone = await db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Phone == cleanPhone && c.Name.ToLower() == cleanName.ToLower(), cancellationToken);
            if (existingByPhone != null)
            {
                var found = await BuildCustomerLookupsAsync(db, existingByPhone.Id, cancellationToken);
                if (found.Count > 0)
                    return found[0];
            }
        }

        var now = DateTime.UtcNow;
        var entity = new Customer
        {
            Name = cleanName,
            Phone = string.IsNullOrWhiteSpace(cleanPhone) ? null : cleanPhone,
            CustomerType = cleanType,
            CreditDays = 30,
            CreditStatus = "active",
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Customers.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        var createdList = await BuildCustomerLookupsAsync(db, entity.Id, cancellationToken);
        return createdList.First();
    }

    public async Task<SaleAccountLookupDto> QuickCreatePaymentAccountAsync(
        string name,
        string accountType = "cash",
        string? accountNumber = null,
        CancellationToken cancellationToken = default)
    {
        var cleanName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanName))
            throw new InvalidOperationException("Payment account name is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var count = await db.Accounts.CountAsync(cancellationToken);
        var code = $"ACC-{(count + 1):D3}";

        var normalizedType = (accountType?.Trim().ToLowerInvariant()) switch
        {
            "bank" => "bank",
            "wallet" or "mobile_wallet" => "mobile_wallet",
            "card" => "card",
            "cash_drawer" => "cash_drawer",
            _ => "cash"
        };

        var account = new Account
        {
            Name = cleanName,
            Code = code,
            AccountType = normalizedType,
            AccountNumber = string.IsNullOrWhiteSpace(accountNumber) ? null : accountNumber.Trim(),
            Currency = "PKR",
            IsActive = true,
            IsDefault = false,
            SyncWithCashDrawer = normalizedType == "cash_drawer",
            CreatedBy = _authService.CurrentSession?.UserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return new SaleAccountLookupDto
        {
            Id = account.Id,
            Name = account.Name,
            Code = account.Code,
            AccountType = account.AccountType,
            CurrentBalance = account.CurrentBalance,
            IsDefault = account.IsDefault,
            SyncWithCashDrawer = account.SyncWithCashDrawer
        };
    }

    public async Task<bool> OpenCashDrawerSessionAsync(
        decimal openingBalance,
        string? openingNote = null,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existingOpen = await db.CashDrawerSessions
            .FirstOrDefaultAsync(s => s.Status == "open", cancellationToken);
        if (existingOpen != null)
            return true;

        var now = DateTime.UtcNow;
        var safeOpening = Math.Max(0m, Math.Round(openingBalance, 2));
        var session = new CashDrawerSession
        {
            OpenedBy = actorUserId ?? _authService.CurrentSession?.UserId,
            OpenedAt = now,
            OpeningBalance = safeOpening,
            ExpectedClosingBalance = safeOpening,
            Status = "open",
            OpeningNote = string.IsNullOrWhiteSpace(openingNote) ? "Opened from Sale Invoice POS" : openingNote.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.CashDrawerSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // =========================================================================
    // 2. PRODUCT, BATCH (FEFO) & SUBSTITUTE SEARCH
    // =========================================================================

    public async Task<List<SaleProductSearchItemDto>> SearchProductsForSaleAsync(
        string? search = null,
        long? customerId = null,
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

            // Search strictly by Product Name in exact typed character order (StartsWith)
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

        return await BuildSaleProductSearchDtosAsync(db, products, customerId, cancellationToken);
    }

    public async Task<SaleProductSearchItemDto?> ScanProductByCodeOrBarcodeAsync(
        string codeOrBarcode,
        long? customerId = null,
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

        var dtos = await BuildSaleProductSearchDtosAsync(db, [product], customerId, cancellationToken);
        return dtos.FirstOrDefault();
    }

    public async Task<List<SaleProductSearchItemDto>> GetSubstituteProductsAsync(
        long productId,
        long? customerId = null,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
            return [];

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (source == null || string.IsNullOrWhiteSpace(source.Formulation))
            return [];

        var formulationLower = source.Formulation.Trim().ToLowerInvariant();
        var substitutes = await db.Products.AsNoTracking()
            .Where(p =>
                p.Id != productId &&
                p.Quantity > 0 &&
                p.Formulation != null &&
                p.Formulation.ToLower() == formulationLower)
            .OrderByDescending(p => p.Quantity)
            .ThenBy(p => p.Name)
            .Take(50)
            .ToListAsync(cancellationToken);

        return await BuildSaleProductSearchDtosAsync(db, substitutes, customerId, cancellationToken);
    }

    public async Task<List<SaleProductBatchOptionDto>> GetProductBatchesForSaleAsync(
        long productId,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
            return [];

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product == null)
            return [];

        var dtos = await BuildSaleProductSearchDtosAsync(db, [product], null, cancellationToken);
        return dtos.FirstOrDefault()?.Batches ?? [];
    }

    // =========================================================================
    // 3. GET BY ID (SHOW / EDIT / PRINT)
    // =========================================================================

    public async Task<SaleInvoiceDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var invoice = await db.SaleInvoices.AsNoTracking()
            .FirstOrDefaultAsync(si => si.Id == id, cancellationToken);

        if (invoice == null)
            return null;

        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken);

        var items = await db.SaleInvoiceItems.AsNoTracking()
            .Where(i => i.SaleInvoiceId == invoice.Id)
            .OrderBy(i => i.Id)
            .ToListAsync(cancellationToken);

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var products = productIds.Count > 0
            ? await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken)
            : [];
        var productSearchMap = (await BuildSaleProductSearchDtosAsync(db, products, invoice.CustomerId, cancellationToken))
            .ToDictionary(p => p.Id);

        var userIds = new HashSet<long>();
        if (invoice.UserId.HasValue) userIds.Add(invoice.UserId.Value);
        if (invoice.ReversedBy.HasValue) userIds.Add(invoice.ReversedBy.Value);

        var histories = await db.DocumentHistories.AsNoTracking()
            .Where(h => h.DocumentType == "sale_invoice" && h.DocumentId == invoice.Id)
            .OrderByDescending(h => h.CreatedAt)
            .ThenByDescending(h => h.Id)
            .ToListAsync(cancellationToken);

        foreach (var h in histories)
        {
            if (h.UserId.HasValue)
                userIds.Add(h.UserId.Value);
        }

        var userMap = userIds.Count > 0
            ? await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => !string.IsNullOrWhiteSpace(u.Name) ? u.Name : u.Username,
                    cancellationToken)
            : new Dictionary<long, string>();

        string accountName = string.Empty;
        if (invoice.AccountId is > 0)
        {
            var accId = invoice.AccountId.Value;
            accountName = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == accId)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        }

        var hasLinkedReturns = await db.SaleReturns.AsNoTracking()
            .AnyAsync(sr => sr.SaleInvoiceId == invoice.Id, cancellationToken);

        var openSession = await db.CashDrawerSessions.AsNoTracking()
            .Where(s => s.Status == "open")
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        bool isPreShift = openSession != null &&
                          invoice.CreatedAt.HasValue &&
                          invoice.CreatedAt.Value < openSession.OpenedAt;

        var isWalkIn = IsWalkInCustomerName(customer?.Name);
        decimal prevCustBalance = 0m;
        decimal totalCustBalance = 0m;

        if (!isWalkIn && customer != null)
        {
            var ledgerRow = await db.CustomerLedgers.AsNoTracking()
                .Where(l => l.SaleInvoiceId == invoice.Id && l.EntryType == "invoice")
                .OrderByDescending(l => l.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (ledgerRow != null)
            {
                prevCustBalance = ledgerRow.PreviousBalance;
                totalCustBalance = ledgerRow.TotalBalance;
            }
            else
            {
                var latestLedger = await db.CustomerLedgers.AsNoTracking()
                    .Where(l => l.CustomerId == customer.Id)
                    .OrderBy(l => l.EntryDate)
                    .ThenBy(l => l.Id)
                    .LastOrDefaultAsync(cancellationToken);
                totalCustBalance = latestLedger?.TotalBalance ?? 0m;
                prevCustBalance = Math.Max(0m, totalCustBalance - invoice.RemainThisBill);
            }
        }

        var lineDtos = new List<SaleInvoiceLineItemDto>();
        decimal totalEstimatedCost = 0m;

        foreach (var item in items)
        {
            productSearchMap.TryGetValue(item.ProductId, out var prodDto);
            var packSize = item.PackSize > 0 ? item.PackSize : (prodDto?.PackSize > 0 ? prodDto.PackSize : 1);
            var unitCost = item.CostPriceSnapshot is > 0m
                ? item.CostPriceSnapshot.Value
                : (prodDto?.AvgPrice > 0m ? prodDto.AvgPrice : (prodDto?.UnitPurchasePrice ?? 0m));

            var totalOutUnits = item.Quantity + item.Bonus;
            var estRowCost = Math.Round(totalOutUnits * unitCost, 2);
            var estRowProfit = Math.Round(item.SubTotal - estRowCost, 2);
            totalEstimatedCost += estRowCost;

            decimal? rowMargin = item.Margin;
            if (!rowMargin.HasValue)
            {
                if (item.Quantity > 0 && item.Bonus > 0 && item.SubTotal > 0m)
                {
                    rowMargin = Math.Round(((item.SubTotal - estRowCost) / item.SubTotal) * 100m, 2);
                }
                else
                {
                    var netUnit = item.Price * (1m - (item.ItemDiscountPercentage / 100m));
                    rowMargin = netUnit > 0m ? Math.Round(((netUnit - unitCost) / netUnit) * 100m, 2) : 0m;
                }
            }

            // Ensure available batches include the batch currently on the item plus current stock
            var availBatches = prodDto?.Batches.ToList() ?? [];
            int currentAvailPlusOwn = item.CurrentQuantity;
            if (!string.IsNullOrWhiteSpace(item.BatchNumber) &&
                !string.Equals(item.BatchNumber, "Unbatched (Stock)", StringComparison.OrdinalIgnoreCase))
            {
                var matchedBatch = availBatches.FirstOrDefault(b =>
                    !b.IsUnbatched &&
                    string.Equals(b.BatchNumber, item.BatchNumber, StringComparison.OrdinalIgnoreCase));
                if (matchedBatch != null)
                {
                    currentAvailPlusOwn = matchedBatch.Quantity + (invoice.Status == "reversed" ? 0 : totalOutUnits);
                }
            }
            else if (prodDto != null)
            {
                currentAvailPlusOwn = prodDto.UnbatchedQuantity + (invoice.Status == "reversed" ? 0 : totalOutUnits);
            }

            lineDtos.Add(new SaleInvoiceLineItemDto
            {
                Id = item.Id,
                ProductId = item.ProductId,
                ProductCode = prodDto?.ProductCode ?? string.Empty,
                ProductName = prodDto?.Name ?? $"Product #{item.ProductId}",
                Formulation = prodDto?.Formulation ?? string.Empty,
                BrandName = prodDto?.BrandName ?? string.Empty,
                Rack = prodDto?.Rack ?? string.Empty,
                PackSize = packSize,
                BatchId = item.BatchId,
                BatchNumber = string.IsNullOrWhiteSpace(item.BatchNumber) ? "Unbatched (Stock)" : item.BatchNumber!,
                Expiry = item.Expiry,
                CurrentQuantity = currentAvailPlusOwn,
                PackQuantity = item.PackQuantity > 0m ? item.PackQuantity : Math.Round((decimal)item.Quantity / packSize, 2),
                UnitQuantity = item.UnitQuantity > 0 ? item.UnitQuantity : item.Quantity,
                Quantity = item.Quantity,
                PackBonus = item.PackBonus > 0m ? item.PackBonus : Math.Round((decimal)item.Bonus / packSize, 2),
                UnitBonus = item.UnitBonus > 0 ? item.UnitBonus : item.Bonus,
                Bonus = item.Bonus,
                PackPrice = item.PackPrice > 0m ? item.PackPrice : Math.Round(item.Price * packSize, 2),
                Price = item.Price,
                CostPriceSnapshot = unitCost,
                AvgPrice = prodDto?.AvgPrice ?? unitCost,
                UnitPurchasePrice = prodDto?.UnitPurchasePrice ?? unitCost,
                CatalogUnitPrice = string.Equals(invoice.SaleType, "wholesale", StringComparison.OrdinalIgnoreCase)
                    ? (prodDto?.WholeSaleUnitPrice > 0m ? prodDto.WholeSaleUnitPrice : item.Price)
                    : (prodDto?.UnitSalePrice > 0m ? prodDto.UnitSalePrice : item.Price),
                ItemDiscountPercentage = item.ItemDiscountPercentage,
                MaxDiscountPercentage = prodDto?.MaxDiscount ?? 100m,
                Margin = rowMargin,
                EstimatedRowCost = estRowCost,
                EstimatedRowProfit = estRowProfit,
                SubTotal = item.SubTotal,
                SaleType = string.IsNullOrWhiteSpace(item.SaleType) ? invoice.SaleType : item.SaleType!,
                IsCustomPrice = item.IsCustomPrice,
                AvailableBatches = availBatches
            });
        }

        var isCredit = string.Equals(invoice.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
        var effectiveReceive = !isCredit && invoice.TotalReceive <= 0m ? invoice.Total : invoice.TotalReceive;
        var remainBill = isCredit ? Math.Max(0m, Math.Round(invoice.Total - effectiveReceive, 2)) : 0m;
        var totalProfit = Math.Round(invoice.Total - totalEstimatedCost, 2);
        var overallMarginPct = invoice.Total > 0m ? Math.Round((totalProfit / invoice.Total) * 100m, 2) : 0m;

        return new SaleInvoiceDetailDto
        {
            Id = invoice.Id,
            PostedNumber = invoice.PostedNumber,
            Date = invoice.Date,
            CreatedAt = invoice.CreatedAt,
            DueDate = invoice.DueDate,
            CreditDays = invoice.CreditDays ?? customer?.CreditDays ?? 30,
            CustomerId = invoice.CustomerId,
            CustomerName = customer?.Name ?? $"Customer #{invoice.CustomerId}",
            CustomerPhone = customer?.Phone ?? string.Empty,
            CustomerAddress = customer?.Address ?? string.Empty,
            IsWalkInCustomer = isWalkIn,
            CreatedByName = invoice.UserId.HasValue && userMap.TryGetValue(invoice.UserId.Value, out var uName)
                ? uName
                : "Admin User",
            DoctorName = invoice.DoctorName ?? string.Empty,
            PatientName = invoice.PatientName ?? string.Empty,
            Remarks = invoice.Remarks ?? string.Empty,
            SaleType = string.IsNullOrWhiteSpace(invoice.SaleType) ? "retail" : invoice.SaleType,
            WholesaleType = string.IsNullOrWhiteSpace(invoice.WholesaleType) ? "unit" : invoice.WholesaleType!,
            InvoiceType = string.IsNullOrWhiteSpace(invoice.InvoiceType) ? "debit" : invoice.InvoiceType,
            Status = string.IsNullOrWhiteSpace(invoice.Status) ? "posted" : invoice.Status,
            ReversedAt = invoice.ReversedAt,
            ReversedByName = invoice.ReversedBy.HasValue && userMap.TryGetValue(invoice.ReversedBy.Value, out var revName)
                ? revName
                : null,
            ReversalReason = invoice.ReversalReason,
            GrossAmount = invoice.GrossAmount,
            ItemDiscount = invoice.ItemDiscount,
            DiscountPercentage = invoice.DiscountPercentage,
            DiscountAmount = invoice.DiscountAmount,
            TaxPercentage = invoice.TaxPercentage,
            TaxAmount = invoice.TaxAmount,
            Total = invoice.Total,
            TotalReceive = effectiveReceive,
            RemainThisBill = remainBill,
            PreviousCustomerBalance = prevCustBalance,
            TotalCustomerBalance = totalCustBalance,
            PaymentMethod = invoice.PaymentMethod ?? "cash",
            AccountId = invoice.AccountId,
            AccountName = accountName,
            PaymentReference = invoice.PaymentReference ?? string.Empty,
            TotalEstimatedCost = Math.Round(totalEstimatedCost, 2),
            TotalEstimatedProfit = totalProfit,
            OverallMarginPct = overallMarginPct,
            HasLinkedReturns = hasLinkedReturns,
            IsPreShiftInvoice = isPreShift,
            Items = lineDtos,
            Histories = histories.Select(h => new SaleDocumentHistoryEntryDto
            {
                Id = h.Id,
                Action = h.Action,
                UserName = h.UserId.HasValue && userMap.TryGetValue(h.UserId.Value, out var hUser) ? hUser : "Admin User",
                CreatedAt = h.CreatedAt ?? DateTime.UtcNow,
                Reason = h.Reason ?? string.Empty,
                ChangesSummary = h.ChangesSummary ?? string.Empty
            }).ToList()
        };
    }

    // =========================================================================
    // 4. CREATE, UPDATE, REVERSE & DELETE SALE INVOICES
    // =========================================================================

    public async Task<SaleInvoiceOperationResult> CreateAsync(
        SaleInvoiceUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var validation = await ValidateAndNormalizeSaleRequestAsync(
                db,
                request,
                existingInvoice: null,
                existingItems: null,
                effectiveActorId,
                cancellationToken);

            if (!validation.Result.Succeeded)
            {
                await tx.RollbackAsync(cancellationToken);
                return validation.Result;
            }

            var now = DateTime.UtcNow;
            var postedNo = await ComputeNextPostedNumberInternalAsync(db, cancellationToken);
            var customer = validation.Customer!;
            var footer = validation.Footer!;
            var lines = validation.NormalizedLines;

            var creditDays = customer.CreditDays ?? 30;
            DateTime? dueDate = validation.InvoiceType == "credit"
                ? (request.DueDate?.Date ?? request.Date.Date.AddDays(creditDays))
                : null;

            var invoice = new SaleInvoice
            {
                UserId = effectiveActorId,
                SalesmanId = request.SalesmanId,
                CustomerId = customer.Id,
                PostedNumber = postedNo,
                Date = request.Date.Date,
                DueDate = dueDate,
                CreditDays = validation.InvoiceType == "credit" ? creditDays : null,
                Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
                DoctorName = string.IsNullOrWhiteSpace(request.DoctorName) ? null : request.DoctorName.Trim(),
                PatientName = string.IsNullOrWhiteSpace(request.PatientName) ? null : request.PatientName.Trim(),
                SaleType = validation.SaleType,
                WholesaleType = validation.SaleType == "wholesale"
                    ? (string.Equals(request.WholesaleType, "pack", StringComparison.OrdinalIgnoreCase) ? "pack" : "unit")
                    : "unit",
                InvoiceType = validation.InvoiceType,
                Status = "posted",
                GrossAmount = footer.GrossAmount,
                ItemDiscount = footer.ItemDiscount,
                DiscountPercentage = footer.DiscountPercentage,
                DiscountAmount = footer.DiscountAmount,
                TaxPercentage = footer.TaxPercentage,
                TaxAmount = footer.TaxAmount,
                Total = footer.Total,
                TotalReceive = footer.TotalReceive,
                RemainThisBill = Math.Max(0m, footer.Total - footer.TotalReceive),
                TotalBalance = 0m,
                PaymentMethod = validation.PaymentMethod,
                AccountId = validation.PrimaryAccountId,
                PaymentReference = string.IsNullOrWhiteSpace(request.PaymentReference) ? null : request.PaymentReference.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };

            db.SaleInvoices.Add(invoice);
            await db.SaveChangesAsync(cancellationToken);

            // Insert Line Items & Deduct Stock (Batch + Product) + Upsert Custom Wholesale Prices
            var createdItems = new List<SaleInvoiceItem>();
            foreach (var line in lines)
            {
                var entityItem = new SaleInvoiceItem
                {
                    SaleInvoiceId = invoice.Id,
                    ProductId = line.Product.Id,
                    BatchId = line.ResolvedBatchId,
                    PackSize = line.Calc.PackSize,
                    BatchNumber = line.IsUnbatched ? null : line.BatchNumber,
                    Expiry = line.IsUnbatched ? null : line.Expiry,
                    CurrentQuantity = line.AvailableStockBeforeSale,
                    PackQuantity = line.Calc.PackQuantity,
                    UnitQuantity = line.Calc.UnitQuantity,
                    Quantity = line.Calc.Quantity,
                    PackBonus = line.Calc.PackBonus,
                    UnitBonus = line.Calc.UnitBonus,
                    Bonus = line.Calc.Bonus,
                    PackPrice = line.Calc.PackPrice,
                    Price = line.Calc.Price,
                    CostPriceSnapshot = line.UnitCostSnapshot,
                    ItemDiscountPercentage = line.Calc.ItemDiscountPercentage,
                    Margin = line.Calc.Margin,
                    SubTotal = line.Calc.SubTotal,
                    SaleType = validation.SaleType,
                    IsCustomPrice = line.IsCustomPrice,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                createdItems.Add(entityItem);

                await DeductLineStockAndSyncCustomPriceAsync(
                    db,
                    line,
                    customer.Id,
                    validation.SaleType,
                    now,
                    cancellationToken);
            }

            db.SaleInvoiceItems.AddRange(createdItems);
            await db.SaveChangesAsync(cancellationToken);

            // Apply Customer Credits (FIFO) if requested on Credit invoice
            if (validation.InvoiceType == "credit" && request.ApplyCreditAmount > 0m && !validation.IsWalkIn)
            {
                var applied = await AllocateAvailableCustomerCreditsFifoAsync(
                    db,
                    customer.Id,
                    invoice.Id,
                    request.ApplyCreditAmount,
                    Math.Max(0m, invoice.Total - invoice.TotalReceive),
                    now,
                    cancellationToken);

                if (applied > 0m)
                {
                    invoice.TotalReceive = Math.Round(invoice.TotalReceive + applied, 2);
                    invoice.RemainThisBill = Math.Max(0m, Math.Round(invoice.Total - invoice.TotalReceive, 2));
                }
            }

            // Create Customer Ledger entries for Credit invoices & Rebuild Running Balance
            if (validation.InvoiceType == "credit" && !validation.IsWalkIn)
            {
                await CreateOrSyncCustomerLedgerForCreditInvoiceAsync(
                    db,
                    invoice,
                    effectiveActorId,
                    now,
                    cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                var newLedgerBalance = await RebuildSingleCustomerLedgerAsync(db, customer.Id, cancellationToken);
                invoice.TotalBalance = newLedgerBalance;
            }

            // Record Financial Account Deposits & Cash Drawer Session Inflow
            var directCashReceived = validation.DirectPaidNow;
            if (directCashReceived > 0m)
            {
                await RecordSalePaymentInflowAsync(
                    db,
                    invoice,
                    customer.Name,
                    validation.PaymentSplits,
                    effectiveActorId,
                    now,
                    cancellationToken);
            }

            // Mark Held Draft as Converted if created from a draft
            if (request.DraftId is > 0)
            {
                var dId = request.DraftId.Value;
                var draft = await db.InvoiceDrafts.FirstOrDefaultAsync(d => d.Id == dId, cancellationToken);
                if (draft != null)
                {
                    draft.ConvertedInvoiceId = invoice.Id;
                    draft.UpdatedAt = now;
                }
            }

            // Record Document Audit History
            var snapshotJson = BuildAuditSnapshotJson(invoice, customer.Name, lines);
            db.DocumentHistories.Add(new DocumentHistory
            {
                DocumentType = "sale_invoice",
                DocumentId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Action = "created",
                UserId = effectiveActorId,
                Reason = $"Created {validation.SaleType} sale invoice ({validation.InvoiceType})",
                BeforeSnapshot = null,
                AfterSnapshot = snapshotJson,
                ChangesSummary = $"Created {invoice.PostedNumber} for {customer.Name} — Total: Rs. {invoice.Total:N0}",
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new SaleInvoiceOperationResult
            {
                Succeeded = true,
                InvoiceId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Message = $"Sale invoice {invoice.PostedNumber} saved successfully."
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return SaleInvoiceOperationResult.Fail($"Failed to save sale invoice: {ex.Message}");
        }
    }

    public async Task<SaleInvoiceOperationResult> UpdateAsync(
        long id,
        SaleInvoiceUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var invoice = await db.SaleInvoices.FirstOrDefaultAsync(si => si.Id == id, cancellationToken);
            if (invoice == null)
                return SaleInvoiceOperationResult.Fail($"Sale invoice #{id} was not found.");

            if (string.Equals(invoice.Status, "reversed", StringComparison.OrdinalIgnoreCase))
                return SaleInvoiceOperationResult.Fail("Reversed sale invoices cannot be edited.");

            var existingItems = await db.SaleInvoiceItems
                .Where(i => i.SaleInvoiceId == invoice.Id)
                .OrderBy(i => i.Id)
                .ToListAsync(cancellationToken);

            var validation = await ValidateAndNormalizeSaleRequestAsync(
                db,
                request,
                invoice,
                existingItems,
                effectiveActorId,
                cancellationToken);

            if (!validation.Result.Succeeded)
            {
                await tx.RollbackAsync(cancellationToken);
                return validation.Result;
            }

            var openSession = await db.CashDrawerSessions
                .Where(s => s.Status == "open")
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            bool isPreShift = openSession != null &&
                              invoice.CreatedAt.HasValue &&
                              invoice.CreatedAt.Value < openSession.OpenedAt;

            var oldIsDebit = !string.Equals(invoice.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
            var oldDirectRecv = oldIsDebit && invoice.TotalReceive <= 0m ? invoice.Total : invoice.TotalReceive;
            var newDirectRecv = validation.DirectPaidNow;
            var cashDelta = Math.Round(newDirectRecv - oldDirectRecv, 2);

            if (isPreShift && Math.Abs(cashDelta) > 0.009m && !request.UpdatePreShiftDrawer.HasValue)
            {
                await tx.RollbackAsync(cancellationToken);
                return new SaleInvoiceOperationResult
                {
                    Succeeded = false,
                    RequiresPreShiftDrawerChoice = true,
                    PreShiftCashDelta = cashDelta,
                    Message = "This invoice was created before the current cash drawer shift. Choose whether to adjust the current shift's cash drawer."
                };
            }

            var now = DateTime.UtcNow;
            var oldCustomerId = invoice.CustomerId;
            var oldCustomer = await db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == oldCustomerId, cancellationToken);
            var newCustomer = validation.Customer!;
            var footer = validation.Footer!;
            var lines = validation.NormalizedLines;

            var beforeHeader = (
                CustomerId: invoice.CustomerId,
                CustomerName: oldCustomer?.Name ?? string.Empty,
                InvoiceType: invoice.InvoiceType,
                Total: invoice.Total,
                TotalReceive: invoice.TotalReceive,
                ItemsCount: existingItems.Count
            );

            // 1. Restore Stock from Existing Items
            var oldProductIds = existingItems.Select(i => i.ProductId).Distinct().ToList();
            var trackedOldProducts = await db.Products
                .Where(p => oldProductIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            await RestoreSaleItemsStockAsync(db, existingItems, trackedOldProducts, now, cancellationToken);
            db.SaleInvoiceItems.RemoveRange(existingItems);
            await db.SaveChangesAsync(cancellationToken);

            // 2. Update Invoice Header
            var creditDays = newCustomer.CreditDays ?? 30;
            invoice.CustomerId = newCustomer.Id;
            invoice.SalesmanId = request.SalesmanId;
            invoice.Date = request.Date.Date;
            invoice.DueDate = validation.InvoiceType == "credit"
                ? (request.DueDate?.Date ?? request.Date.Date.AddDays(creditDays))
                : null;
            invoice.CreditDays = validation.InvoiceType == "credit" ? creditDays : null;
            invoice.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
            invoice.DoctorName = string.IsNullOrWhiteSpace(request.DoctorName) ? null : request.DoctorName.Trim();
            invoice.PatientName = string.IsNullOrWhiteSpace(request.PatientName) ? null : request.PatientName.Trim();
            invoice.SaleType = validation.SaleType;
            invoice.WholesaleType = validation.SaleType == "wholesale"
                ? (string.Equals(request.WholesaleType, "pack", StringComparison.OrdinalIgnoreCase) ? "pack" : "unit")
                : "unit";
            invoice.InvoiceType = validation.InvoiceType;
            invoice.GrossAmount = footer.GrossAmount;
            invoice.ItemDiscount = footer.ItemDiscount;
            invoice.DiscountPercentage = footer.DiscountPercentage;
            invoice.DiscountAmount = footer.DiscountAmount;
            invoice.TaxPercentage = footer.TaxPercentage;
            invoice.TaxAmount = footer.TaxAmount;
            invoice.Total = footer.Total;
            invoice.TotalReceive = footer.TotalReceive;
            invoice.RemainThisBill = Math.Max(0m, footer.Total - footer.TotalReceive);
            invoice.PaymentMethod = validation.PaymentMethod;
            invoice.AccountId = validation.PrimaryAccountId;
            invoice.PaymentReference = string.IsNullOrWhiteSpace(request.PaymentReference) ? null : request.PaymentReference.Trim();
            invoice.UpdatedAt = now;

            // 3. Insert New Line Items & Deduct Stock
            var newEntities = new List<SaleInvoiceItem>();
            foreach (var line in lines)
            {
                newEntities.Add(new SaleInvoiceItem
                {
                    SaleInvoiceId = invoice.Id,
                    ProductId = line.Product.Id,
                    BatchId = line.ResolvedBatchId,
                    PackSize = line.Calc.PackSize,
                    BatchNumber = line.IsUnbatched ? null : line.BatchNumber,
                    Expiry = line.IsUnbatched ? null : line.Expiry,
                    CurrentQuantity = line.AvailableStockBeforeSale,
                    PackQuantity = line.Calc.PackQuantity,
                    UnitQuantity = line.Calc.UnitQuantity,
                    Quantity = line.Calc.Quantity,
                    PackBonus = line.Calc.PackBonus,
                    UnitBonus = line.Calc.UnitBonus,
                    Bonus = line.Calc.Bonus,
                    PackPrice = line.Calc.PackPrice,
                    Price = line.Calc.Price,
                    CostPriceSnapshot = line.UnitCostSnapshot,
                    ItemDiscountPercentage = line.Calc.ItemDiscountPercentage,
                    Margin = line.Calc.Margin,
                    SubTotal = line.Calc.SubTotal,
                    SaleType = validation.SaleType,
                    IsCustomPrice = line.IsCustomPrice,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                await DeductLineStockAndSyncCustomPriceAsync(
                    db,
                    line,
                    newCustomer.Id,
                    validation.SaleType,
                    now,
                    cancellationToken);
            }

            db.SaleInvoiceItems.AddRange(newEntities);
            await db.SaveChangesAsync(cancellationToken);

            // 4. Sync Customer Ledgers
            await DeleteInvoiceCustomerLedgerRowsAsync(db, invoice.Id, invoice.PostedNumber, cancellationToken);
            if (validation.InvoiceType == "credit" && !validation.IsWalkIn)
            {
                await CreateOrSyncCustomerLedgerForCreditInvoiceAsync(
                    db,
                    invoice,
                    effectiveActorId,
                    now,
                    cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);

            if (!IsWalkInCustomerName(newCustomer.Name))
            {
                invoice.TotalBalance = await RebuildSingleCustomerLedgerAsync(db, newCustomer.Id, cancellationToken);
            }
            if (oldCustomerId != newCustomer.Id && !IsWalkInCustomerName(oldCustomer?.Name))
            {
                await RebuildSingleCustomerLedgerAsync(db, oldCustomerId, cancellationToken);
            }

            // 5. Sync Account Transactions & Cash Drawer
            await RevertInvoiceAccountAndDrawerAsync(
                db,
                invoice.Id,
                invoice.PostedNumber,
                adjustOpenDrawer: !isPreShift || request.UpdatePreShiftDrawer == true,
                now,
                cancellationToken);

            if (newDirectRecv > 0m)
            {
                await RecordSalePaymentInflowAsync(
                    db,
                    invoice,
                    newCustomer.Name,
                    validation.PaymentSplits,
                    effectiveActorId,
                    now,
                    cancellationToken,
                    recordInDrawer: !isPreShift || request.UpdatePreShiftDrawer == true);
            }

            // 6. Audit History
            var diffs = new List<string>();
            if (beforeHeader.CustomerId != invoice.CustomerId)
                diffs.Add($"Customer: {beforeHeader.CustomerName} -> {newCustomer.Name}");
            if (!string.Equals(beforeHeader.InvoiceType, invoice.InvoiceType, StringComparison.OrdinalIgnoreCase))
                diffs.Add($"Type: {beforeHeader.InvoiceType} -> {invoice.InvoiceType}");
            if (beforeHeader.Total != invoice.Total)
                diffs.Add($"Total: {beforeHeader.Total:N0} -> {invoice.Total:N0}");
            if (beforeHeader.TotalReceive != invoice.TotalReceive)
                diffs.Add($"Received: {beforeHeader.TotalReceive:N0} -> {invoice.TotalReceive:N0}");
            if (beforeHeader.ItemsCount != lines.Count)
                diffs.Add($"Items: {beforeHeader.ItemsCount} -> {lines.Count}");
            if (diffs.Count == 0)
                diffs.Add("Line items or pricing updated");

            var afterSnapshot = BuildAuditSnapshotJson(invoice, newCustomer.Name, lines);
            db.DocumentHistories.Add(new DocumentHistory
            {
                DocumentType = "sale_invoice",
                DocumentId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Action = "updated",
                UserId = effectiveActorId,
                Reason = string.IsNullOrWhiteSpace(request.EditReason) ? "Sale invoice updated" : request.EditReason.Trim(),
                BeforeSnapshot = null,
                AfterSnapshot = afterSnapshot,
                ChangesSummary = string.Join(" | ", diffs),
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new SaleInvoiceOperationResult
            {
                Succeeded = true,
                InvoiceId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Message = $"Sale invoice {invoice.PostedNumber} updated successfully."
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return SaleInvoiceOperationResult.Fail($"Failed to update sale invoice: {ex.Message}");
        }
    }

    public async Task<SaleInvoiceOperationResult> ReverseAsync(
        long id,
        string reason,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var cleanReason = reason?.Trim() ?? string.Empty;
        if (cleanReason.Length < 3)
            return SaleInvoiceOperationResult.Fail("Reversal reason must be at least 3 characters.");

        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var invoice = await db.SaleInvoices.FirstOrDefaultAsync(si => si.Id == id, cancellationToken);
            if (invoice == null)
                return SaleInvoiceOperationResult.Fail($"Sale invoice #{id} was not found.");

            if (string.Equals(invoice.Status, "reversed", StringComparison.OrdinalIgnoreCase))
                return SaleInvoiceOperationResult.Fail("This sale invoice has already been reversed.");

            var hasReturns = await db.SaleReturns.AnyAsync(sr => sr.SaleInvoiceId == invoice.Id, cancellationToken);
            if (hasReturns)
            {
                return SaleInvoiceOperationResult.Fail(
                    "Cannot reverse this sale invoice because sale return(s) exist against it. Delete or reverse the return(s) first.");
            }

            var items = await db.SaleInvoiceItems
                .Where(i => i.SaleInvoiceId == invoice.Id)
                .ToListAsync(cancellationToken);

            var productIds = items.Select(i => i.ProductId).Distinct().ToList();
            var trackedProducts = await db.Products
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            var customer = await db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken);

            var now = DateTime.UtcNow;

            // 1. Restore Stock to Batches & Products
            await RestoreSaleItemsStockAsync(db, items, trackedProducts, now, cancellationToken);

            // 2. Detach Payment Allocations
            await DetachSalePaymentAllocationsAsync(db, invoice.Id, now, cancellationToken);

            // 3. Delete Customer Ledger entries & rebuild
            await DeleteInvoiceCustomerLedgerRowsAsync(db, invoice.Id, invoice.PostedNumber, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (!IsWalkInCustomerName(customer?.Name))
            {
                await RebuildSingleCustomerLedgerAsync(db, invoice.CustomerId, cancellationToken);
            }

            // 4. Revert Account Deposit & Cash Drawer
            await RevertInvoiceAccountAndDrawerAsync(
                db,
                invoice.Id,
                invoice.PostedNumber,
                adjustOpenDrawer: true,
                now,
                cancellationToken);

            // 5. Mark Invoice Reversed
            invoice.Status = "reversed";
            invoice.ReversedAt = now;
            invoice.ReversedBy = effectiveActorId;
            invoice.ReversalReason = cleanReason;
            invoice.UpdatedAt = now;

            db.DocumentHistories.Add(new DocumentHistory
            {
                DocumentType = "sale_invoice",
                DocumentId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Action = "reversed",
                UserId = effectiveActorId,
                Reason = cleanReason,
                ChangesSummary = $"Reversed {invoice.PostedNumber} — Reason: {cleanReason}",
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new SaleInvoiceOperationResult
            {
                Succeeded = true,
                InvoiceId = invoice.Id,
                PostedNumber = invoice.PostedNumber,
                Message = $"Sale invoice {invoice.PostedNumber} reversed (voided) successfully."
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return SaleInvoiceOperationResult.Fail($"Failed to reverse sale invoice: {ex.Message}");
        }
    }

    public async Task<SaleInvoiceOperationResult> DeleteAsync(
        long id,
        string userPassword,
        string refundMode = "refund",
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;
        if (effectiveActorId is > 0)
        {
            var passwordOk = await _authService.VerifyPasswordAsync(effectiveActorId.Value, userPassword ?? string.Empty, cancellationToken);
            if (!passwordOk)
            {
                return SaleInvoiceOperationResult.Fail("Incorrect password. Deletion was not authorized.");
            }
        }
        else if (string.IsNullOrWhiteSpace(userPassword))
        {
            return SaleInvoiceOperationResult.Fail("Password confirmation is required to delete a sale invoice.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var invoice = await db.SaleInvoices.FirstOrDefaultAsync(si => si.Id == id, cancellationToken);
            if (invoice == null)
                return SaleInvoiceOperationResult.Fail($"Sale invoice #{id} was not found.");

            var hasReturns = await db.SaleReturns.AnyAsync(sr => sr.SaleInvoiceId == invoice.Id, cancellationToken);
            if (hasReturns)
            {
                return SaleInvoiceOperationResult.Fail(
                    "Cannot delete this sale invoice because sale return(s) exist against it.");
            }

            var items = await db.SaleInvoiceItems
                .Where(i => i.SaleInvoiceId == invoice.Id)
                .ToListAsync(cancellationToken);

            var customer = await db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken);

            var wasReversed = string.Equals(invoice.Status, "reversed", StringComparison.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;
            var customerId = invoice.CustomerId;
            var postedNo = invoice.PostedNumber;

            if (!wasReversed)
            {
                var productIds = items.Select(i => i.ProductId).Distinct().ToList();
                var trackedProducts = await db.Products
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, cancellationToken);

                await RestoreSaleItemsStockAsync(db, items, trackedProducts, now, cancellationToken);
                await DetachSalePaymentAllocationsAsync(db, invoice.Id, now, cancellationToken);
                await DeleteInvoiceCustomerLedgerRowsAsync(db, invoice.Id, invoice.PostedNumber, cancellationToken);

                if (string.Equals(refundMode, "credit", StringComparison.OrdinalIgnoreCase) &&
                    customer != null &&
                    !IsWalkInCustomerName(customer.Name) &&
                    invoice.TotalReceive > 0m)
                {
                    // Convert received amount into store credit on CustomerLedger
                    db.CustomerLedgers.Add(new CustomerLedger
                    {
                        CustomerId = customer.Id,
                        EntryType = "payment",
                        PostedNumber = postedNo,
                        InvoiceNumber = postedNo,
                        EntryDate = now.Date,
                        Description = $"Store credit from deleted invoice {postedNo}",
                        PaymentMethod = "credit",
                        PaymentReference = $"Deleted {postedNo}",
                        CreditedAmount = invoice.TotalReceive,
                        CreatedBy = effectiveActorId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
                else if (!string.Equals(refundMode, "none", StringComparison.OrdinalIgnoreCase))
                {
                    await RevertInvoiceAccountAndDrawerAsync(
                        db,
                        invoice.Id,
                        invoice.PostedNumber,
                        adjustOpenDrawer: true,
                        now,
                        cancellationToken);
                }
            }

            db.SaleInvoiceItems.RemoveRange(items);
            db.SaleInvoices.Remove(invoice);
            await db.SaveChangesAsync(cancellationToken);

            if (!wasReversed && customer != null && !IsWalkInCustomerName(customer.Name))
            {
                await RebuildSingleCustomerLedgerAsync(db, customerId, cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);

            return new SaleInvoiceOperationResult
            {
                Succeeded = true,
                InvoiceId = id,
                PostedNumber = postedNo,
                Message = $"Sale invoice {postedNo} permanently deleted."
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return SaleInvoiceOperationResult.Fail($"Failed to delete sale invoice: {ex.Message}");
        }
    }

    public async Task<(bool Succeeded, string Message, long? ApproverUserId)> VerifySupervisorOverrideAsync(
        string usernameOrEmail,
        string password,
        string overrideType,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var cleanUser = usernameOrEmail?.Trim() ?? string.Empty;
        var cleanReason = reason?.Trim() ?? string.Empty;
        if (cleanReason.Length < 3)
            return (false, "Override reason must be at least 3 characters.", null);

        if (string.IsNullOrWhiteSpace(cleanUser) || string.IsNullOrWhiteSpace(password))
            return (false, "Supervisor username/email and password are required.", null);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var lower = cleanUser.ToLowerInvariant();
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Username.ToLower() == lower || (u.Email != null && u.Email.ToLower() == lower),
                cancellationToken);

        if (user == null)
            return (false, "Supervisor account not found.", null);

        var passOk = await _authService.VerifyPasswordAsync(user.Id, password, cancellationToken);
        if (!passOk)
            return (false, "Invalid supervisor password.", null);

        return (true, $"Override approved by {(!string.IsNullOrWhiteSpace(user.Name) ? user.Name : user.Username)}.", user.Id);
    }

    // =========================================================================
    // 5. HELD SALE DRAFTS (DRF-RET-0001 / DRF-WSL-0001)
    // =========================================================================

    public async Task<HeldSaleDraftSummaryDto> SaveDraftAsync(
        long? existingDraftId,
        SaleInvoiceUpsertRequest state,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var activeItems = state.Items.Where(i => i.ProductId > 0).ToList();
        if (activeItems.Count == 0)
            throw new InvalidOperationException("Add at least one product before holding a sale draft.");

        var effectiveActorId = actorUserId ?? _authService.CurrentSession?.UserId;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var isWholesale = string.Equals(state.SaleType, "wholesale", StringComparison.OrdinalIgnoreCase);
        var invoiceKind = isWholesale ? "sale_wholesale" : "sale_retail";

        string customerName = "WALK-IN-CUSTOMER";
        if (!string.IsNullOrWhiteSpace(state.OneTimeCustomerName))
        {
            customerName = state.OneTimeCustomerName.Trim();
        }
        else if (state.CustomerId > 0)
        {
            customerName = await db.Customers.AsNoTracking()
                .Where(c => c.Id == state.CustomerId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "WALK-IN-CUSTOMER";
        }

        var now = DateTime.UtcNow;
        var payloadJson = JsonSerializer.Serialize(state, JsonOptions);

        InvoiceDraft? draft = null;
        if (existingDraftId is > 0)
        {
            var dId = existingDraftId.Value;
            draft = await db.InvoiceDrafts.FirstOrDefaultAsync(
                d => d.Id == dId &&
                     (d.InvoiceKind == "sale_retail" || d.InvoiceKind == "sale_wholesale") &&
                     d.DeletedAt == null,
                cancellationToken);
        }

        if (draft == null)
        {
            var nextDraftNo = await ComputeNextSaleDraftNumberAsync(db, isWholesale, cancellationToken);
            draft = new InvoiceDraft
            {
                DraftNumber = nextDraftNo,
                InvoiceKind = invoiceKind,
                UserId = effectiveActorId,
                PartyId = state.CustomerId > 0 ? state.CustomerId : null,
                PartyName = customerName,
                Title = $"{(isWholesale ? "Wholesale" : "Retail")} — {customerName}",
                TotalAmount = Math.Round(state.Total, 2),
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
            draft.InvoiceKind = invoiceKind;
            draft.PartyId = state.CustomerId > 0 ? state.CustomerId : null;
            draft.PartyName = customerName;
            draft.Title = $"{(isWholesale ? "Wholesale" : "Retail")} — {customerName}";
            draft.TotalAmount = Math.Round(state.Total, 2);
            draft.ItemCount = activeItems.Count;
            draft.Payload = payloadJson;
            draft.Version += 1;
            draft.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var preview = string.Join(", ", activeItems.Take(3).Select(i =>
            !string.IsNullOrWhiteSpace(i.ProductName) ? $"{i.ProductName} x{i.Quantity}" : $"#{i.ProductId} x{i.Quantity}"));

        return new HeldSaleDraftSummaryDto
        {
            Id = draft.Id,
            DraftNumber = draft.DraftNumber,
            InvoiceKind = draft.InvoiceKind,
            CustomerId = draft.PartyId,
            CustomerName = draft.PartyName ?? "WALK-IN-CUSTOMER",
            Title = draft.Title ?? draft.DraftNumber,
            ItemCount = draft.ItemCount,
            TotalAmount = draft.TotalAmount,
            Version = draft.Version,
            UpdatedAt = draft.UpdatedAt ?? now,
            ItemsPreview = preview
        };
    }

    public async Task<List<HeldSaleDraftSummaryDto>> GetHeldDraftsAsync(
        string? saleTypeFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.InvoiceDrafts.AsNoTracking()
            .Where(d =>
                (d.InvoiceKind == "sale_retail" || d.InvoiceKind == "sale_wholesale") &&
                d.ConvertedInvoiceId == null &&
                d.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(saleTypeFilter) &&
            !string.Equals(saleTypeFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            var targetKind = string.Equals(saleTypeFilter, "wholesale", StringComparison.OrdinalIgnoreCase)
                ? "sale_wholesale"
                : "sale_retail";
            query = query.Where(d => d.InvoiceKind == targetKind);
        }

        var drafts = await query
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .ToListAsync(cancellationToken);

        return drafts.Select(d =>
        {
            string preview = string.Empty;
            if (!string.IsNullOrWhiteSpace(d.Payload))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<SaleInvoiceUpsertRequest>(d.Payload, JsonOptions);
                    if (parsed?.Items is { Count: > 0 })
                    {
                        preview = string.Join(", ", parsed.Items
                            .Where(i => i.ProductId > 0)
                            .Take(3)
                            .Select(i => !string.IsNullOrWhiteSpace(i.ProductName) ? $"{i.ProductName} x{i.Quantity}" : $"Item x{i.Quantity}"));
                    }
                }
                catch
                {
                    // Ignore preview parse failure
                }
            }

            return new HeldSaleDraftSummaryDto
            {
                Id = d.Id,
                DraftNumber = d.DraftNumber,
                InvoiceKind = d.InvoiceKind,
                CustomerId = d.PartyId,
                CustomerName = d.PartyName ?? "WALK-IN-CUSTOMER",
                Title = d.Title ?? d.DraftNumber,
                ItemCount = d.ItemCount,
                TotalAmount = d.TotalAmount,
                Version = d.Version,
                UpdatedAt = d.UpdatedAt ?? d.CreatedAt ?? DateTime.UtcNow,
                ItemsPreview = preview
            };
        }).ToList();
    }

    public async Task<HeldSaleDraftDetailDto?> GetDraftByIdAsync(
        long draftId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var draft = await db.InvoiceDrafts.AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.Id == draftId &&
                     (d.InvoiceKind == "sale_retail" || d.InvoiceKind == "sale_wholesale") &&
                     d.ConvertedInvoiceId == null &&
                     d.DeletedAt == null,
                cancellationToken);

        if (draft == null)
            return null;

        SaleInvoiceUpsertRequest? parsed = null;
        if (!string.IsNullOrWhiteSpace(draft.Payload))
        {
            try
            {
                parsed = JsonSerializer.Deserialize<SaleInvoiceUpsertRequest>(draft.Payload, JsonOptions);
            }
            catch
            {
                parsed = null;
            }
        }

        parsed ??= new SaleInvoiceUpsertRequest();
        parsed.DraftId = draft.Id;

        return new HeldSaleDraftDetailDto
        {
            Id = draft.Id,
            DraftNumber = draft.DraftNumber,
            InvoiceKind = draft.InvoiceKind,
            Version = draft.Version,
            FormState = parsed
        };
    }

    public async Task<bool> DeleteDraftAsync(long draftId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var draft = await db.InvoiceDrafts.FirstOrDefaultAsync(
            d => d.Id == draftId && (d.InvoiceKind == "sale_retail" || d.InvoiceKind == "sale_wholesale"),
            cancellationToken);
        if (draft == null)
            return false;

        draft.DeletedAt = DateTime.UtcNow;
        draft.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> ClearAllDraftsAsync(string? saleTypeFilter = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.InvoiceDrafts.Where(d =>
            (d.InvoiceKind == "sale_retail" || d.InvoiceKind == "sale_wholesale") &&
            d.ConvertedInvoiceId == null &&
            d.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(saleTypeFilter) &&
            !string.Equals(saleTypeFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            var targetKind = string.Equals(saleTypeFilter, "wholesale", StringComparison.OrdinalIgnoreCase)
                ? "sale_wholesale"
                : "sale_retail";
            query = query.Where(d => d.InvoiceKind == targetKind);
        }

        var drafts = await query.ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var d in drafts)
        {
            d.DeletedAt = now;
            d.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return drafts.Count;
    }

    // =========================================================================
    // 6. MODAL WINDOW LAYOUT PREFERENCES (Shared with PurchaseInvoiceService)
    // =========================================================================

    public async Task<PurchaseModalWindowLayoutDto> GetModalWindowLayoutAsync(
        string modalKey = "productSearchModal",
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await ReadModalWindowLayoutInternalAsync(db, modalKey, actorUserId, cancellationToken);
    }

    public async Task SaveModalWindowLayoutAsync(
        string modalKey,
        PurchaseModalWindowLayoutDto layout,
        long? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (layout == null)
            return;

        var cleanKey = string.IsNullOrWhiteSpace(modalKey) ? "productSearchModal" : modalKey.Trim();
        var isQuickAdd = string.Equals(cleanKey, "productModal", StringComparison.OrdinalIgnoreCase);
        double minW = isQuickAdd ? 600d : 640d;
        double minH = isQuickAdd ? 400d : 420d;
        double safeW = Math.Round(Math.Max(minW, layout.Width), 1);
        double safeH = Math.Round(Math.Max(minH, layout.Height), 1);

        var posKey = $"{cleanKey}Pos";
        var sizeKey = $"{cleanKey}Size";

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var effectiveUserId = await ResolvePreferenceUserIdAsync(db, actorUserId, cancellationToken);
        var now = DateTime.UtcNow;

        var sizeJson = JsonSerializer.Serialize(new { width = safeW, height = safeH });
        await UpsertUserPreferenceRowAsync(db, effectiveUserId, sizeKey, sizeJson, now, cancellationToken);

        string? posJson = null;
        if (layout.X.HasValue && layout.Y.HasValue &&
            double.IsFinite(layout.X.Value) && double.IsFinite(layout.Y.Value))
        {
            var safeX = Math.Round(layout.X.Value, 1);
            var safeY = Math.Round(layout.Y.Value, 1);
            posJson = JsonSerializer.Serialize(new { x = safeX, y = safeY });
            await UpsertUserPreferenceRowAsync(db, effectiveUserId, posKey, posJson, now, cancellationToken);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == effectiveUserId, cancellationToken);
        if (user != null)
        {
            var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(user.Preferences))
            {
                try
                {
                    using var doc = JsonDocument.Parse(user.Preferences);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            dict[prop.Name] = prop.Value.Clone();
                        }
                    }
                }
                catch
                {
                    // Ignore malformed legacy JSON
                }
            }

            using var sizeDoc = JsonDocument.Parse(sizeJson);
            dict[sizeKey] = sizeDoc.RootElement.Clone();

            if (posJson != null)
            {
                using var posDoc = JsonDocument.Parse(posJson);
                dict[posKey] = posDoc.RootElement.Clone();
            }

            user.Preferences = JsonSerializer.Serialize(dict);
            user.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // =========================================================================
    // 7. INTERNAL VALIDATION, STOCK, LEDGER & TREASURY HELPERS
    // =========================================================================

    private sealed class NormalizedSaleLine
    {
        public int RowIndex { get; init; }
        public Product Product { get; init; } = null!;
        public SaleLineCalcModel Calc { get; init; } = null!;
        public long? ResolvedBatchId { get; init; }
        public string BatchNumber { get; init; } = string.Empty;
        public DateTime? Expiry { get; init; }
        public bool IsUnbatched { get; init; }
        public int AvailableStockBeforeSale { get; init; }
        public decimal UnitCostSnapshot { get; init; }
        public bool IsCustomPrice { get; init; }
    }

    private sealed class SaleValidationBundle
    {
        public SaleInvoiceOperationResult Result { get; init; } = new() { Succeeded = true };
        public Customer? Customer { get; init; }
        public bool IsWalkIn { get; init; }
        public string SaleType { get; init; } = "retail";
        public string InvoiceType { get; init; } = "debit";
        public string PaymentMethod { get; init; } = "cash";
        public long? PrimaryAccountId { get; init; }
        public decimal DirectPaidNow { get; init; }
        public List<SaleSplitPaymentLineDto> PaymentSplits { get; init; } = [];
        public SaleFooterCalcModel? Footer { get; init; }
        public List<NormalizedSaleLine> NormalizedLines { get; init; } = [];
    }

    private async Task<SaleValidationBundle> ValidateAndNormalizeSaleRequestAsync(
        KarobarDbContext db,
        SaleInvoiceUpsertRequest request,
        SaleInvoice? existingInvoice,
        List<SaleInvoiceItem>? existingItems,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var saleSystem = setting?.SaleSystem ?? "retail_wholesale";
        var saleType = string.Equals(request.SaleType, "wholesale", StringComparison.OrdinalIgnoreCase)
            ? "wholesale"
            : "retail";

        if (saleType == "wholesale" && string.Equals(saleSystem, "retail", StringComparison.OrdinalIgnoreCase))
        {
            return new SaleValidationBundle
            {
                Result = SaleInvoiceOperationResult.Fail("Wholesale sales are disabled in store settings. Please use Retail Sale Invoice.")
            };
        }

        var invoiceType = string.Equals(request.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase)
            ? "credit"
            : "debit";

        // Resolve Customer (or fallback to Walk-In on Debit, or auto-create from OneTimeCustomerName/Phone)
        Customer? customer = null;
        if (request.CustomerId > 0)
        {
            customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        }

        if (customer == null)
        {
            customer = await db.Customers
                .OrderBy(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (customer == null)
        {
            return new SaleValidationBundle
            {
                Result = SaleInvoiceOperationResult.Fail("Please select a customer before saving.")
            };
        }

        // If Walk-In customer is selected AND one-time name or phone is provided, auto-create/attach named customer
        if (IsWalkInCustomerName(customer.Name) &&
            (!string.IsNullOrWhiteSpace(request.OneTimeCustomerName) || !string.IsNullOrWhiteSpace(request.OneTimeCustomerPhone)))
        {
            var createdDto = await QuickCreateCustomerAsync(
                !string.IsNullOrWhiteSpace(request.OneTimeCustomerName)
                    ? request.OneTimeCustomerName!.Trim()
                    : $"Walk-In ({request.OneTimeCustomerPhone!.Trim()})",
                request.OneTimeCustomerPhone,
                saleType,
                cancellationToken);
            customer = await db.Customers.FirstAsync(c => c.Id == createdDto.Id, cancellationToken);
        }

        bool isWalkIn = IsWalkInCustomerName(customer.Name);

        var activeLines = request.Items
            .Select((item, idx) => (Item: item, Index: idx))
            .Where(x => x.Item.ProductId > 0)
            .ToList();

        if (activeLines.Count == 0)
        {
            return new SaleValidationBundle
            {
                Result = SaleInvoiceOperationResult.Fail("Please add at least one product to the sale invoice.", 0, "product")
            };
        }

        var productIds = activeLines.Select(x => x.Item.ProductId).Distinct().ToList();
        var productsMap = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var allBatches = await db.Batches.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId))
            .ToListAsync(cancellationToken);

        var batchesByProduct = allBatches
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Map of units previously deducted by existingInvoice (when updating)
        var oldDeductedByBatchKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var oldDeductedByProduct = new Dictionary<long, int>();
        if (existingItems is { Count: > 0 })
        {
            foreach (var oldItem in existingItems)
            {
                var oldUnits = oldItem.Quantity + Math.Max(0, oldItem.Bonus);
                oldDeductedByProduct[oldItem.ProductId] = oldDeductedByProduct.GetValueOrDefault(oldItem.ProductId) + oldUnits;

                var oldBatchKey = BuildBatchStockKey(oldItem.ProductId, oldItem.BatchNumber);
                oldDeductedByBatchKey[oldBatchKey] = oldDeductedByBatchKey.GetValueOrDefault(oldBatchKey) + oldUnits;
            }
        }

        var normalizedLines = new List<NormalizedSaleLine>();
        var calcModels = new List<SaleLineCalcModel>();
        var seenRowBatchKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requiredUnitsByBatchKey = new Dictionary<string, (int TotalUnits, int AvailableUnits, string ProductName, string BatchLabel, int FirstRowIndex)>(StringComparer.OrdinalIgnoreCase);
        var expiredItemsList = new List<string>();
        var discountViolationsList = new List<string>();

        foreach (var (rawItem, rowIdx) in activeLines)
        {
            if (!productsMap.TryGetValue(rawItem.ProductId, out var product))
            {
                return new SaleValidationBundle
                {
                    Result = SaleInvoiceOperationResult.Fail($"Row {rowIdx + 1}: Selected product #{rawItem.ProductId} does not exist.", rowIdx, "product")
                };
            }

            var packSize = Math.Max(1, rawItem.PackSize > 0 ? rawItem.PackSize : product.PackSize);
            var unitCost = product.AvgPrice > 0m ? product.AvgPrice : product.UnitPurchasePrice;

            var calc = new SaleLineCalcModel
            {
                PackSize = packSize,
                PackQuantity = rawItem.PackQuantity,
                UnitQuantity = rawItem.UnitQuantity > 0 ? rawItem.UnitQuantity : rawItem.Quantity,
                Quantity = rawItem.Quantity > 0 ? rawItem.Quantity : rawItem.UnitQuantity,
                PackBonus = rawItem.PackBonus,
                UnitBonus = rawItem.UnitBonus > 0 ? rawItem.UnitBonus : rawItem.Bonus,
                Bonus = rawItem.Bonus > 0 ? rawItem.Bonus : rawItem.UnitBonus,
                PackPrice = rawItem.PackPrice,
                Price = rawItem.Price,
                ItemDiscountPercentage = rawItem.ItemDiscountPercentage,
                AvgPrice = product.AvgPrice,
                UnitPurchasePrice = product.UnitPurchasePrice
            };

            if (saleType == "wholesale")
            {
                InvoiceCalculators.RecalcWholesaleSaleItem(calc);
            }
            else
            {
                if (calc.Quantity <= 0 && calc.UnitQuantity > 0)
                    calc.Quantity = calc.UnitQuantity;
                else if (calc.Quantity <= 0 && calc.PackQuantity > 0m)
                    calc.Quantity = (int)Math.Round(calc.PackQuantity * packSize, 0, MidpointRounding.AwayFromZero);

                calc.UnitQuantity = calc.Quantity;
                calc.PackQuantity = Math.Round((decimal)calc.Quantity / packSize, 2);
                calc.Bonus = Math.Max(0, calc.Bonus);
                calc.UnitBonus = calc.Bonus;
                calc.PackBonus = Math.Round((decimal)calc.Bonus / packSize, 2);
                calc.PackPrice = Math.Round(calc.Price * packSize, 2);
                InvoiceCalculators.RecalcRetailSaleItem(calc);
            }

            if (calc.Quantity < 1)
            {
                return new SaleValidationBundle
                {
                    Result = SaleInvoiceOperationResult.Fail(
                        $"Row {rowIdx + 1}: Paid quantity for '{product.Name}' must be at least 1 unit.",
                        rowIdx,
                        saleType == "wholesale" ? "pack_quantity" : "quantity")
                };
            }

            if (calc.Price < 0m)
            {
                return new SaleValidationBundle
                {
                    Result = SaleInvoiceOperationResult.Fail(
                        $"Row {rowIdx + 1}: Sale price for '{product.Name}' cannot be negative.",
                        rowIdx,
                        saleType == "wholesale" ? "pack_price" : "quantity")
                };
            }

            // Wholesale 0% Margin Floor Check (Strict Hard Block)
            if (saleType == "wholesale" && unitCost > 0m)
            {
                var totalOutUnits = calc.Quantity + Math.Max(0, calc.Bonus);
                var totalLineCost = Math.Round(totalOutUnits * unitCost, 2);
                var netUnitPrice = calc.Price * (1m - (calc.ItemDiscountPercentage / 100m));

                if (netUnitPrice < unitCost - 0.005m || calc.SubTotal < totalLineCost - 0.05m)
                {
                    var rowMarginPct = calc.Margin ?? -100m;
                    return new SaleValidationBundle
                    {
                        Result = SaleInvoiceOperationResult.Fail(
                            $"Row {rowIdx + 1}: Margin ({rowMarginPct:0.1}%) for '{product.Name}' cannot be less than 0% (Net sale: Rs. {calc.SubTotal:N2}, Cost: Rs. {totalLineCost:N2} at avg cost Rs. {unitCost:N2}/unit).",
                            rowIdx,
                            "pack_price")
                    };
                }
            }

            // Batch & Unbatched Stock Resolution
            var productBatches = batchesByProduct.GetValueOrDefault(product.Id) ?? [];
            var rawBatchNo = rawItem.BatchNumber?.Trim() ?? string.Empty;
            bool isUnbatched = string.IsNullOrWhiteSpace(rawBatchNo) ||
                               string.Equals(rawBatchNo, "Unbatched (Stock)", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(rawBatchNo, "__UNBATCHED__", StringComparison.OrdinalIgnoreCase);

            var batchStockKey = BuildBatchStockKey(product.Id, isUnbatched ? null : rawBatchNo);
            if (!seenRowBatchKeys.Add(batchStockKey))
            {
                var dupLabel = isUnbatched ? "Unbatched stock" : $"Batch '{rawBatchNo}'";
                return new SaleValidationBundle
                {
                    Result = SaleInvoiceOperationResult.Fail(
                        $"Row {rowIdx + 1}: {dupLabel} for '{product.Name}' is already added in another row. Combine quantities into a single row.",
                        rowIdx,
                        "batch")
                };
            }

            int availableUnitsForSelection;
            long? resolvedBatchId = null;
            DateTime? resolvedExpiry = rawItem.Expiry?.Date;

            if (!isUnbatched)
            {
                var matchingBatches = productBatches
                    .Where(b => string.Equals(b.BatchNumber.Trim(), rawBatchNo, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                    .ThenBy(b => b.Id)
                    .ToList();

                var primaryBatch = (rawItem.BatchId is > 0
                    ? matchingBatches.FirstOrDefault(b => b.Id == rawItem.BatchId.Value)
                    : null) ?? matchingBatches.FirstOrDefault();

                resolvedBatchId = primaryBatch?.Id;
                resolvedExpiry ??= primaryBatch?.ExpiryDate?.Date;

                int currentBatchUnits = matchingBatches.Sum(b => Math.Max(0, b.Quantity));
                int restoredFromOld = oldDeductedByBatchKey.GetValueOrDefault(batchStockKey);
                availableUnitsForSelection = currentBatchUnits + restoredFromOld;

                if (resolvedExpiry.HasValue && resolvedExpiry.Value.Date < DateTime.Today)
                {
                    expiredItemsList.Add($"Row {rowIdx + 1}: {product.Name} (Batch {rawBatchNo}, Exp {resolvedExpiry.Value:MM/yyyy})");
                }
            }
            else
            {
                int totalBatchedPositive = productBatches.Sum(b => Math.Max(0, b.Quantity));
                int currentProductQty = Math.Max(0, product.Quantity);
                int currentUnbatched = Math.Max(0, currentProductQty - totalBatchedPositive);
                int restoredFromOld = oldDeductedByBatchKey.GetValueOrDefault(batchStockKey);
                availableUnitsForSelection = currentUnbatched + restoredFromOld;
            }

            int rowRequiredUnits = calc.Quantity + Math.Max(0, calc.Bonus);
            if (requiredUnitsByBatchKey.TryGetValue(batchStockKey, out var existingAgg))
            {
                requiredUnitsByBatchKey[batchStockKey] = (
                    existingAgg.TotalUnits + rowRequiredUnits,
                    existingAgg.AvailableUnits,
                    existingAgg.ProductName,
                    existingAgg.BatchLabel,
                    existingAgg.FirstRowIndex);
            }
            else
            {
                requiredUnitsByBatchKey[batchStockKey] = (
                    rowRequiredUnits,
                    availableUnitsForSelection,
                    product.Name,
                    isUnbatched ? "Unbatched" : $"Batch {rawBatchNo}",
                    rowIdx);
            }

            // Max Discount & Retail Below-Cost Check
            decimal catalogUnitPrice = saleType == "wholesale"
                ? (product.WholeSaleUnitPrice is > 0m
                    ? product.WholeSaleUnitPrice.Value
                    : (product.WholeSalePackPrice is > 0m ? Math.Round(product.WholeSalePackPrice.Value / packSize, 2) : product.UnitSalePrice))
                : product.UnitSalePrice;

            decimal effectivePriceAfterDisc = calc.Price * (1m - (calc.ItemDiscountPercentage / 100m));
            decimal effectiveDiscPct = calc.ItemDiscountPercentage;
            if (catalogUnitPrice > 0m && effectivePriceAfterDisc < catalogUnitPrice)
            {
                var impliedDisc = Math.Round(((catalogUnitPrice - effectivePriceAfterDisc) / catalogUnitPrice) * 100m, 2);
                if (impliedDisc > effectiveDiscPct)
                    effectiveDiscPct = impliedDisc;
            }

            var maxAllowedDisc = product.MaxDiscount is > 0m ? product.MaxDiscount.Value : 100m;
            if (effectiveDiscPct > maxAllowedDisc + 0.01m)
            {
                discountViolationsList.Add(
                    $"Row {rowIdx + 1}: '{product.Name}' discount ({effectiveDiscPct:0.##}%) exceeds max allowed ({maxAllowedDisc:0.##}%).");
            }
            else if (saleType == "retail" && unitCost > 0m && effectivePriceAfterDisc < unitCost - 0.005m)
            {
                discountViolationsList.Add(
                    $"Row {rowIdx + 1}: '{product.Name}' net retail price (Rs. {effectivePriceAfterDisc:N2}) is below unit cost (Rs. {unitCost:N2}).");
            }

            calcModels.Add(calc);
            normalizedLines.Add(new NormalizedSaleLine
            {
                RowIndex = rowIdx,
                Product = product,
                Calc = calc,
                ResolvedBatchId = resolvedBatchId,
                BatchNumber = isUnbatched ? string.Empty : rawBatchNo,
                Expiry = resolvedExpiry,
                IsUnbatched = isUnbatched,
                AvailableStockBeforeSale = availableUnitsForSelection,
                UnitCostSnapshot = unitCost,
                IsCustomPrice = rawItem.IsCustomPrice
            });
        }

        // Validate Stock Availability Across All Rows
        foreach (var (_, agg) in requiredUnitsByBatchKey)
        {
            if (agg.TotalUnits > agg.AvailableUnits)
            {
                return new SaleValidationBundle
                {
                    Result = SaleInvoiceOperationResult.Fail(
                        $"Row {agg.FirstRowIndex + 1}: Required quantity ({agg.TotalUnits} units) exceeds available stock ({agg.AvailableUnits} units) for '{agg.ProductName}' ({agg.BatchLabel}).",
                        agg.FirstRowIndex,
                        saleType == "wholesale" ? "pack_quantity" : "quantity")
                };
            }
        }

        // Footer Calculation
        var footer = new SaleFooterCalcModel
        {
            InvoiceType = invoiceType,
            DiscountPercentage = request.DiscountPercentage,
            DiscountAmount = request.DiscountAmount,
            TaxPercentage = request.TaxPercentage,
            TaxAmount = request.TaxAmount,
            TotalReceive = request.TotalReceive
        };
        InvoiceCalculators.RecalcSaleFooter(calcModels, footer, invoiceType == "credit" ? "total_receive" : null);

        // Check if invoice-level discount pushes any product over its MaxDiscount
        if (footer.DiscountPercentage > 0m)
        {
            foreach (var line in normalizedLines)
            {
                var maxAllowed = line.Product.MaxDiscount is > 0m ? line.Product.MaxDiscount.Value : 100m;
                var combinedDisc = line.Calc.ItemDiscountPercentage + footer.DiscountPercentage;
                if (combinedDisc > maxAllowed + 0.01m)
                {
                    discountViolationsList.Add(
                        $"Row {line.RowIndex + 1}: '{line.Product.Name}' combined item + bill discount ({combinedDisc:0.##}%) exceeds max allowed ({maxAllowed:0.##}%).");
                }
            }
        }

        // Check Expired Stock Override
        if (expiredItemsList.Count > 0 && !request.ExpiredOverrideConfirmed)
        {
            return new SaleValidationBundle
            {
                Result = new SaleInvoiceOperationResult
                {
                    Succeeded = false,
                    RequiresExpiredOverride = true,
                    ExpiredBatchDetails = string.Join("\n", expiredItemsList),
                    Message = "One or more selected batches are expired. Supervisor authorization is required."
                }
            };
        }

        // Check Discount Override
        if (discountViolationsList.Count > 0 && !request.DiscountOverrideConfirmed)
        {
            return new SaleValidationBundle
            {
                Result = new SaleInvoiceOperationResult
                {
                    Succeeded = false,
                    RequiresDiscountOverride = true,
                    DiscountViolationDetails = string.Join("\n", discountViolationsList.Distinct()),
                    Message = "Discount exceeds maximum allowed limit. Supervisor authorization is required."
                }
            };
        }

        // Check Walk-In vs Credit & Registered Customer Credit Limit
        var unpaidBalance = Math.Max(0m, Math.Round(footer.Total - footer.TotalReceive - request.ApplyCreditAmount, 2));
        if (isWalkIn && (invoiceType == "credit" || unpaidBalance > 0.009m))
        {
            return new SaleValidationBundle
            {
                Result = SaleInvoiceOperationResult.Fail(
                    "Walk-In Customer bills cannot be saved with unpaid credit (Udhar)—select or create a named Customer if the bill is not paid in full.")
            };
        }

        if (!isWalkIn && invoiceType == "credit" && unpaidBalance > 0.009m)
        {
            if (string.Equals(customer.CreditStatus, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                return new SaleValidationBundle
                {
                    Result = SaleInvoiceOperationResult.Fail($"Customer '{customer.Name}' is blocked for credit transactions.")
                };
            }

            var custSummary = (await BuildCustomerLookupsAsync(db, customer.Id, cancellationToken)).FirstOrDefault();
            var currentDue = custSummary?.CurrentBalance ?? 0m;
            if (existingInvoice != null && existingInvoice.CustomerId == customer.Id &&
                string.Equals(existingInvoice.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase))
            {
                currentDue -= Math.Max(0m, existingInvoice.Total - existingInvoice.TotalReceive);
            }

            var projectedDue = Math.Round(currentDue + unpaidBalance, 2);
            var creditLimit = customer.CreditLimit ?? 0m;
            bool isOnHold = string.Equals(customer.CreditStatus, "on_hold", StringComparison.OrdinalIgnoreCase);
            bool exceedsLimit = creditLimit > 0m && projectedDue > creditLimit + 0.009m;

            if ((isOnHold || exceedsLimit) && !request.CreditOverrideConfirmed)
            {
                var detailMsg = isOnHold
                    ? $"Customer '{customer.Name}' credit status is On Hold."
                    : $"Projected balance (Rs. {projectedDue:N0}) exceeds credit limit (Rs. {creditLimit:N0}) by Rs. {(projectedDue - creditLimit):N0}.";

                return new SaleValidationBundle
                {
                    Result = new SaleInvoiceOperationResult
                    {
                        Succeeded = false,
                        RequiresCreditOverride = true,
                        CreditViolationDetails = detailMsg,
                        CustomerCurrentBalance = currentDue,
                        NewBillCreditAmount = unpaidBalance,
                        ProjectedCustomerBalance = projectedDue,
                        CustomerCreditLimit = creditLimit,
                        Message = detailMsg
                    }
                };
            }
        }

        // Resolve Payment Account & Splits
        var defaultAccount = await db.Accounts.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var primaryAccountId = request.AccountId is > 0 ? request.AccountId : defaultAccount?.Id;
        var paymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "cash" : request.PaymentMethod.Trim().ToLowerInvariant();
        var directPaid = footer.TotalReceive;

        var splits = new List<SaleSplitPaymentLineDto>();
        if (request.SplitPayments is { Count: > 0 })
        {
            foreach (var sp in request.SplitPayments.Where(s => s.Amount > 0m))
            {
                splits.Add(new SaleSplitPaymentLineDto
                {
                    PaymentMethod = string.IsNullOrWhiteSpace(sp.PaymentMethod) ? paymentMethod : sp.PaymentMethod.Trim().ToLowerInvariant(),
                    AccountId = sp.AccountId is > 0 ? sp.AccountId : primaryAccountId,
                    Amount = Math.Round(sp.Amount, 2),
                    Reference = sp.Reference
                });
            }
        }
        else if (directPaid > 0m)
        {
            splits.Add(new SaleSplitPaymentLineDto
            {
                PaymentMethod = paymentMethod,
                AccountId = primaryAccountId,
                Amount = directPaid,
                Reference = request.PaymentReference
            });
        }

        return new SaleValidationBundle
        {
            Result = new SaleInvoiceOperationResult { Succeeded = true },
            Customer = customer,
            IsWalkIn = isWalkIn,
            SaleType = saleType,
            InvoiceType = invoiceType,
            PaymentMethod = paymentMethod,
            PrimaryAccountId = primaryAccountId,
            DirectPaidNow = directPaid,
            PaymentSplits = splits,
            Footer = footer,
            NormalizedLines = normalizedLines
        };
    }

    private static string BuildBatchStockKey(long productId, string? batchNumber)
    {
        var clean = batchNumber?.Trim();
        if (string.IsNullOrWhiteSpace(clean) ||
            string.Equals(clean, "Unbatched (Stock)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clean, "__UNBATCHED__", StringComparison.OrdinalIgnoreCase))
        {
            return $"{productId}::__UNBATCHED__";
        }
        return $"{productId}::{clean.ToUpperInvariant()}";
    }

    private static async Task DeductLineStockAndSyncCustomPriceAsync(
        KarobarDbContext db,
        NormalizedSaleLine line,
        long customerId,
        string saleType,
        DateTime now,
        CancellationToken cancellationToken)
    {
        int totalDeductUnits = line.Calc.Quantity + Math.Max(0, line.Calc.Bonus);

        if (!line.IsUnbatched && !string.IsNullOrWhiteSpace(line.BatchNumber))
        {
            var batchNoLower = line.BatchNumber.Trim().ToLowerInvariant();
            var matchingBatches = await db.Batches
                .Where(b => b.ProductId == line.Product.Id && b.BatchNumber.ToLower() == batchNoLower)
                .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(b => b.Id)
                .ToListAsync(cancellationToken);

            int remainingToDeduct = totalDeductUnits;
            foreach (var b in matchingBatches)
            {
                if (remainingToDeduct <= 0)
                    break;

                int take = Math.Min(Math.Max(0, b.Quantity), remainingToDeduct);
                b.Quantity -= take;
                b.UpdatedAt = now;
                remainingToDeduct -= take;
            }

            if (remainingToDeduct > 0 && matchingBatches.Count > 0)
            {
                matchingBatches[0].Quantity = Math.Max(0, matchingBatches[0].Quantity - remainingToDeduct);
                matchingBatches[0].UpdatedAt = now;
            }
        }

        line.Product.Quantity = Math.Max(0, line.Product.Quantity - totalDeductUnits);
        if (line.Product.Quantity <= 0)
        {
            line.Product.Quantity = 0;
            line.Product.AvgPrice = 0m;
            line.Product.Margin = 0m;
            line.Product.WholeSaleMargin = 0m;
        }
        line.Product.UpdatedAt = now;

        // Upsert CustomerWholeSalePrice when wholesale custom price is used
        if (saleType == "wholesale" && line.IsCustomPrice && line.Calc.PackPrice > 0m && customerId > 0)
        {
            var existingCustom = await db.CustomerWholeSalePrices
                .FirstOrDefaultAsync(
                    cp => cp.CustomerId == customerId && cp.ProductId == line.Product.Id,
                    cancellationToken);

            if (existingCustom == null)
            {
                db.CustomerWholeSalePrices.Add(new CustomerWholeSalePrice
                {
                    CustomerId = customerId,
                    ProductId = line.Product.Id,
                    PackPrice = line.Calc.PackPrice,
                    UnitPrice = line.Calc.Price,
                    DiscountPercentage = line.Calc.ItemDiscountPercentage,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                existingCustom.PackPrice = line.Calc.PackPrice;
                existingCustom.UnitPrice = line.Calc.Price;
                existingCustom.DiscountPercentage = line.Calc.ItemDiscountPercentage;
                existingCustom.UpdatedAt = now;
            }
        }
    }

    private static async Task RestoreSaleItemsStockAsync(
        KarobarDbContext db,
        List<SaleInvoiceItem> items,
        Dictionary<long, Product> trackedProducts,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            int restoreUnits = item.Quantity + Math.Max(0, item.Bonus);
            if (restoreUnits <= 0)
                continue;

            trackedProducts.TryGetValue(item.ProductId, out var product);

            if (!string.IsNullOrWhiteSpace(item.BatchNumber) &&
                !string.Equals(item.BatchNumber, "Unbatched (Stock)", StringComparison.OrdinalIgnoreCase))
            {
                var batchNoLower = item.BatchNumber.Trim().ToLowerInvariant();
                var batch = await db.Batches
                    .Where(b => b.ProductId == item.ProductId && b.BatchNumber.ToLower() == batchNoLower)
                    .OrderByDescending(b => b.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (batch != null)
                {
                    batch.Quantity += restoreUnits;
                    if (!batch.ExpiryDate.HasValue && item.Expiry.HasValue)
                        batch.ExpiryDate = item.Expiry.Value.Date;
                    batch.UpdatedAt = now;
                }
                else
                {
                    db.Batches.Add(new Batch
                    {
                        ProductId = item.ProductId,
                        BatchNumber = item.BatchNumber.Trim(),
                        ExpiryDate = item.Expiry?.Date,
                        Quantity = restoreUnits,
                        PackSize = item.PackSize > 0 ? item.PackSize : (product?.PackSize ?? 1),
                        UnitPurchasePrice = item.CostPriceSnapshot ?? product?.UnitPurchasePrice ?? 0m,
                        PackPurchasePrice = (item.CostPriceSnapshot ?? product?.UnitPurchasePrice ?? 0m) * (item.PackSize > 0 ? item.PackSize : 1),
                        UnitSalePrice = product?.UnitSalePrice ?? item.Price,
                        PackSalePrice = product?.PackSalePrice ?? item.PackPrice,
                        WholeSaleUnitPrice = product?.WholeSaleUnitPrice ?? item.Price,
                        WholeSalePackPrice = product?.WholeSalePackPrice ?? item.PackPrice,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }

            if (product != null)
            {
                product.Quantity += restoreUnits;
                if (product.AvgPrice <= 0m && item.CostPriceSnapshot is > 0m)
                {
                    product.AvgPrice = item.CostPriceSnapshot.Value;
                }
                product.UpdatedAt = now;
            }
        }
    }

    private static async Task<decimal> AllocateAvailableCustomerCreditsFifoAsync(
        KarobarDbContext db,
        long customerId,
        long saleInvoiceId,
        decimal requestedCreditAmount,
        decimal maxAllocatableOnInvoice,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cap = Math.Min(requestedCreditAmount, maxAllocatableOnInvoice);
        if (cap <= 0m)
            return 0m;

        var unappliedPayments = await db.Payments
            .Where(p =>
                p.PartyType == "customer" &&
                p.CustomerId == customerId &&
                p.Status != "reversed" &&
                p.UnappliedAmount > 0m)
            .OrderBy(p => p.PaymentDate)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        decimal totalAllocated = 0m;
        foreach (var pay in unappliedPayments)
        {
            var rem = cap - totalAllocated;
            if (rem <= 0m)
                break;

            var alloc = Math.Round(Math.Min(pay.UnappliedAmount, rem), 2);
            if (alloc <= 0m)
                continue;

            pay.AllocatedAmount = Math.Round(pay.AllocatedAmount + alloc, 2);
            pay.UnappliedAmount = Math.Max(0m, Math.Round(pay.UnappliedAmount - alloc, 2));
            pay.UpdatedAt = now;

            db.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = pay.Id,
                InvoiceType = "sale_invoice",
                InvoiceId = saleInvoiceId,
                AllocatedAmount = alloc,
                CreatedAt = now,
                UpdatedAt = now
            });

            totalAllocated = Math.Round(totalAllocated + alloc, 2);
        }

        return totalAllocated;
    }

    private static async Task DetachSalePaymentAllocationsAsync(
        KarobarDbContext db,
        long saleInvoiceId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var allocations = await db.PaymentAllocations
            .Where(pa => pa.InvoiceType == "sale_invoice" && pa.InvoiceId == saleInvoiceId)
            .ToListAsync(cancellationToken);

        if (allocations.Count == 0)
            return;

        var paymentIds = allocations.Select(a => a.PaymentId).Distinct().ToList();
        var payments = await db.Payments
            .Where(p => paymentIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var alloc in allocations)
        {
            if (payments.TryGetValue(alloc.PaymentId, out var pay))
            {
                pay.AllocatedAmount = Math.Max(0m, Math.Round(pay.AllocatedAmount - alloc.AllocatedAmount, 2));
                pay.UnappliedAmount = Math.Round(pay.UnappliedAmount + alloc.AllocatedAmount, 2);
                pay.UpdatedAt = now;
            }
        }

        db.PaymentAllocations.RemoveRange(allocations);
    }

    private static async Task CreateOrSyncCustomerLedgerForCreditInvoiceAsync(
        KarobarDbContext db,
        SaleInvoice invoice,
        long? actorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await DeleteInvoiceCustomerLedgerRowsAsync(db, invoice.Id, invoice.PostedNumber, cancellationToken);

        var recvOnInvoice = Math.Min(invoice.TotalReceive, invoice.Total);
        var remainOnInvoice = Math.Max(0m, Math.Round(invoice.Total - recvOnInvoice, 2));

        // Row #1: Invoice Ledger Entry
        db.CustomerLedgers.Add(new CustomerLedger
        {
            CustomerId = invoice.CustomerId,
            SaleInvoiceId = invoice.Id,
            EntryType = "invoice",
            PostedNumber = invoice.PostedNumber,
            InvoiceNumber = invoice.PostedNumber,
            EntryDate = invoice.Date.Date,
            DueDate = invoice.DueDate?.Date,
            Description = $"Sale Invoice {invoice.PostedNumber}",
            PaymentMethod = invoice.PaymentMethod,
            PaymentReference = invoice.PaymentReference,
            InvoiceTotal = invoice.Total,
            TotalReceived = recvOnInvoice,
            DeductedAmount = 0m,
            CreditedAmount = 0m,
            BalanceRemaining = remainOnInvoice,
            CreatedBy = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        });

        // Row #2: Excess Payment Entry (when customer pays more than current invoice total)
        var excessCredit = Math.Max(0m, Math.Round(invoice.TotalReceive - invoice.Total, 2));
        if (excessCredit > 0m)
        {
            db.CustomerLedgers.Add(new CustomerLedger
            {
                CustomerId = invoice.CustomerId,
                SaleInvoiceId = invoice.Id,
                EntryType = "payment",
                PostedNumber = invoice.PostedNumber,
                InvoiceNumber = invoice.PostedNumber,
                EntryDate = invoice.Date.Date,
                Description = $"Excess payment on Inv #{invoice.PostedNumber}",
                PaymentMethod = invoice.PaymentMethod,
                PaymentReference = $"Inv #{invoice.PostedNumber}",
                InvoiceTotal = 0m,
                TotalReceived = 0m,
                DeductedAmount = 0m,
                CreditedAmount = excessCredit,
                BalanceRemaining = 0m,
                CreatedBy = actorUserId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private static async Task DeleteInvoiceCustomerLedgerRowsAsync(
        KarobarDbContext db,
        long saleInvoiceId,
        string postedNumber,
        CancellationToken cancellationToken)
    {
        var refTag = $"Inv #{postedNumber}";
        var rows = await db.CustomerLedgers
            .Where(l =>
                l.SaleInvoiceId == saleInvoiceId ||
                (l.EntryType == "payment" && l.PaymentReference == refTag))
            .ToListAsync(cancellationToken);

        if (rows.Count > 0)
        {
            db.CustomerLedgers.RemoveRange(rows);
        }
    }

    private static async Task<decimal> RebuildSingleCustomerLedgerAsync(
        KarobarDbContext db,
        long customerId,
        CancellationToken cancellationToken)
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
        }

        await db.SaveChangesAsync(cancellationToken);
        return runningBalance;
    }

    private static async Task RecordSalePaymentInflowAsync(
        KarobarDbContext db,
        SaleInvoice invoice,
        string customerName,
        List<SaleSplitPaymentLineDto> splits,
        long? actorUserId,
        DateTime now,
        CancellationToken cancellationToken,
        bool recordInDrawer = true)
    {
        if (splits.Count == 0)
            return;

        var openSession = recordInDrawer
            ? await db.CashDrawerSessions
                .Where(s => s.Status == "open")
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        foreach (var split in splits.Where(s => s.Amount > 0m))
        {
            Account? account = null;
            if (split.AccountId is > 0)
            {
                var accId = split.AccountId.Value;
                account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accId, cancellationToken);
            }
            account ??= await db.Accounts
                .Where(a => a.IsActive)
                .OrderByDescending(a => a.IsDefault)
                .ThenBy(a => a.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (account != null)
            {
                account.CurrentBalance = Math.Round(account.CurrentBalance + split.Amount, 2);
                account.UpdatedAt = now;

                db.AccountTransactions.Add(new AccountTransaction
                {
                    AccountId = account.Id,
                    TransactionDate = invoice.Date.Date,
                    Direction = "in",
                    Amount = split.Amount,
                    BalanceAfter = account.CurrentBalance,
                    Category = "sale",
                    PaymentMethod = split.PaymentMethod,
                    ReferenceType = "sale_invoice",
                    ReferenceId = invoice.Id,
                    ReferenceNumber = invoice.PostedNumber,
                    CounterpartyName = customerName,
                    Description = $"Sale Invoice {invoice.PostedNumber} ({customerName})",
                    CreatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                bool isCashDrawerAccount = account.SyncWithCashDrawer ||
                                           string.Equals(account.AccountType, "cash_drawer", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(account.Code, "CASH-DRAWER", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(split.PaymentMethod, "cash", StringComparison.OrdinalIgnoreCase);

                if (recordInDrawer && openSession != null && isCashDrawerAccount)
                {
                    openSession.ExpectedClosingBalance = Math.Round(openSession.ExpectedClosingBalance + split.Amount, 2);
                    openSession.UpdatedAt = now;

                    db.CashDrawerTransactions.Add(new CashDrawerTransaction
                    {
                        CashDrawerSessionId = openSession.Id,
                        Type = "cash_sale",
                        Amount = split.Amount,
                        ReferenceType = "sale_invoice",
                        ReferenceId = invoice.Id,
                        Description = $"Cash Sale {invoice.PostedNumber} — {customerName}",
                        CreatedBy = actorUserId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }
        }
    }

    private static async Task RevertInvoiceAccountAndDrawerAsync(
        KarobarDbContext db,
        long saleInvoiceId,
        string postedNumber,
        bool adjustOpenDrawer,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var accTxs = await db.AccountTransactions
            .Where(t => t.ReferenceType == "sale_invoice" && t.ReferenceId == saleInvoiceId)
            .ToListAsync(cancellationToken);

        if (accTxs.Count > 0)
        {
            var accIds = accTxs.Select(t => t.AccountId).Distinct().ToList();
            var accounts = await db.Accounts
                .Where(a => accIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, cancellationToken);

            foreach (var tx in accTxs)
            {
                if (accounts.TryGetValue(tx.AccountId, out var acc))
                {
                    if (string.Equals(tx.Direction, "in", StringComparison.OrdinalIgnoreCase))
                        acc.CurrentBalance = Math.Round(acc.CurrentBalance - tx.Amount, 2);
                    else
                        acc.CurrentBalance = Math.Round(acc.CurrentBalance + tx.Amount, 2);
                    acc.UpdatedAt = now;
                }
            }

            db.AccountTransactions.RemoveRange(accTxs);
        }

        var drawerTxs = await db.CashDrawerTransactions
            .Where(t => t.ReferenceType == "sale_invoice" && t.ReferenceId == saleInvoiceId)
            .ToListAsync(cancellationToken);

        if (drawerTxs.Count > 0)
        {
            if (adjustOpenDrawer)
            {
                var sessionIds = drawerTxs.Select(t => t.CashDrawerSessionId).Distinct().ToList();
                var sessions = await db.CashDrawerSessions
                    .Where(s => sessionIds.Contains(s.Id) && s.Status == "open")
                    .ToDictionaryAsync(s => s.Id, cancellationToken);

                foreach (var dtx in drawerTxs)
                {
                    if (sessions.TryGetValue(dtx.CashDrawerSessionId, out var sess))
                    {
                        sess.ExpectedClosingBalance = Math.Round(sess.ExpectedClosingBalance - dtx.Amount, 2);
                        sess.UpdatedAt = now;
                    }
                }
            }

            db.CashDrawerTransactions.RemoveRange(drawerTxs);
        }
    }

    private static async Task<List<SaleCustomerLookupDto>> BuildCustomerLookupsAsync(
        KarobarDbContext db,
        long? filterCustomerId,
        CancellationToken cancellationToken)
    {
        var query = db.Customers.AsNoTracking().AsQueryable();
        if (filterCustomerId is > 0)
        {
            var cid = filterCustomerId.Value;
            query = query.Where(c => c.Id == cid);
        }

        var customers = await query
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);

        if (customers.Count == 0)
            return [];

        var custIds = customers.Select(c => c.Id).ToList();

        // Latest ledger balance per customer
        var ledgerRows = await db.CustomerLedgers.AsNoTracking()
            .Where(l => custIds.Contains(l.CustomerId))
            .OrderBy(l => l.EntryDate)
            .ThenBy(l => l.Id)
            .Select(l => new { l.CustomerId, l.TotalBalance })
            .ToListAsync(cancellationToken);

        var balanceMap = new Dictionary<long, decimal>();
        foreach (var row in ledgerRows)
        {
            balanceMap[row.CustomerId] = row.TotalBalance;
        }

        // Unapplied payments per customer
        var unappliedPayments = await db.Payments.AsNoTracking()
            .Where(p =>
                p.PartyType == "customer" &&
                p.CustomerId != null &&
                custIds.Contains(p.CustomerId.Value) &&
                p.Status != "reversed" &&
                p.UnappliedAmount > 0m)
            .Select(p => new { CustomerId = p.CustomerId!.Value, p.UnappliedAmount })
            .ToListAsync(cancellationToken);

        var unappliedMap = unappliedPayments
            .GroupBy(x => x.CustomerId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.UnappliedAmount));

        // Custom wholesale prices per customer
        var customPrices = await db.CustomerWholeSalePrices.AsNoTracking()
            .Where(cp => custIds.Contains(cp.CustomerId) && cp.PackPrice != null && cp.PackPrice > 0m)
            .ToListAsync(cancellationToken);

        var customPriceMap = customPrices
            .GroupBy(cp => cp.CustomerId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.ProductId)
                      .ToDictionary(pg => pg.Key, pg => pg.Last().PackPrice!.Value));

        var dtos = customers.Select(c =>
        {
            bool isWalkIn = IsWalkInCustomerName(c.Name);
            return new SaleCustomerLookupDto
            {
                Id = c.Id,
                Name = c.Name,
                CustomerType = string.IsNullOrWhiteSpace(c.CustomerType) ? "retail" : c.CustomerType,
                Phone = c.Phone ?? string.Empty,
                Email = c.Email ?? string.Empty,
                Address = c.Address ?? string.Empty,
                Area = c.Area ?? string.Empty,
                CreditDays = c.CreditDays ?? 30,
                CreditLimit = c.CreditLimit ?? 0m,
                CreditStatus = string.IsNullOrWhiteSpace(c.CreditStatus) ? "active" : c.CreditStatus,
                DiscountPercentage = c.DiscountPercentage ?? 0m,
                CurrentBalance = isWalkIn ? 0m : Math.Round(balanceMap.GetValueOrDefault(c.Id), 2),
                AvailableCreditToApply = isWalkIn ? 0m : Math.Round(unappliedMap.GetValueOrDefault(c.Id), 2),
                IsWalkIn = isWalkIn,
                CustomWholesalePrices = customPriceMap.TryGetValue(c.Id, out var cpDict) ? cpDict : []
            };
        }).ToList();

        // Pin Walk-in Customer to the very top of the dropdown list
        return dtos
            .OrderByDescending(c => c.IsWalkIn)
            .ThenBy(c => c.Name)
            .ToList();
    }

    private static async Task<List<SaleProductSearchItemDto>> BuildSaleProductSearchDtosAsync(
        KarobarDbContext db,
        List<Product> products,
        long? customerId,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0)
            return [];

        var productIds = products.Select(p => p.Id).ToList();
        var brandIds = products.Where(p => p.BrandId.HasValue).Select(p => p.BrandId!.Value).Distinct().ToList();
        var categoryIds = products.Where(p => p.CategoryId.HasValue).Select(p => p.CategoryId!.Value).Distinct().ToList();
        var supplierIds = products.Where(p => p.SupplierId.HasValue).Select(p => p.SupplierId!.Value).Distinct().ToList();

        var brandMap = brandIds.Count > 0
            ? await db.Brands.AsNoTracking()
                .Where(b => brandIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken)
            : new Dictionary<long, string>();

        var categoryMap = categoryIds.Count > 0
            ? await db.Categories.AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            : new Dictionary<long, string>();

        var supplierMap = supplierIds.Count > 0
            ? await db.Suppliers.AsNoTracking()
                .Where(s => supplierIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken)
            : new Dictionary<long, string>();

        var allBatches = await db.Batches.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId) && b.Quantity > 0)
            .ToListAsync(cancellationToken);

        var batchesByProduct = allBatches
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        Dictionary<long, decimal> customerCustomPackPrices = [];
        if (customerId is > 0)
        {
            var cid = customerId.Value;
            var customRows = await db.CustomerWholeSalePrices.AsNoTracking()
                .Where(cp => cp.CustomerId == cid && productIds.Contains(cp.ProductId) && cp.PackPrice != null && cp.PackPrice > 0m)
                .ToListAsync(cancellationToken);

            foreach (var r in customRows)
            {
                customerCustomPackPrices[r.ProductId] = r.PackPrice!.Value;
            }
        }

        var today = DateTime.Today;
        var result = new List<SaleProductSearchItemDto>(products.Count);

        foreach (var p in products)
        {
            var packSize = Math.Max(1, p.PackSize);
            var rawBatches = batchesByProduct.GetValueOrDefault(p.Id) ?? [];

            // Sort FEFO: Unexpired earliest-expiry first, then expired earliest-expiry
            var sortedBatches = rawBatches
                .OrderBy(b => b.ExpiryDate.HasValue && b.ExpiryDate.Value.Date < today ? 1 : 0)
                .ThenBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(b => b.Id)
                .Select(b =>
                {
                    var bPackSize = b.PackSize is > 0 ? b.PackSize.Value : packSize;
                    var bPackSale = b.PackSalePrice is > 0m ? b.PackSalePrice.Value : p.PackSalePrice;
                    var bUnitSale = b.UnitSalePrice is > 0m ? b.UnitSalePrice.Value : (bPackSale > 0m ? Math.Round(bPackSale / bPackSize, 2) : p.UnitSalePrice);
                    var bWsPack = b.WholeSalePackPrice is > 0m
                        ? b.WholeSalePackPrice.Value
                        : (p.WholeSalePackPrice is > 0m ? p.WholeSalePackPrice.Value : bPackSale);
                    var bWsUnit = b.WholeSaleUnitPrice is > 0m
                        ? b.WholeSaleUnitPrice.Value
                        : (bWsPack > 0m ? Math.Round(bWsPack / bPackSize, 2) : bUnitSale);

                    return new SaleProductBatchOptionDto
                    {
                        Id = b.Id,
                        BatchNumber = b.BatchNumber,
                        ExpiryDate = b.ExpiryDate,
                        Quantity = b.Quantity,
                        PackSize = bPackSize,
                        Rack = !string.IsNullOrWhiteSpace(b.Rack) ? b.Rack! : (p.Rack ?? string.Empty),
                        UnitPurchasePrice = b.UnitPurchasePrice ?? p.UnitPurchasePrice,
                        PackSalePrice = bPackSale,
                        UnitSalePrice = bUnitSale,
                        WholeSalePackPrice = bWsPack,
                        WholeSaleUnitPrice = bWsUnit
                    };
                })
                .ToList();

            int totalBatchedQty = sortedBatches.Sum(b => Math.Max(0, b.Quantity));
            int unbatchedQty = Math.Max(0, p.Quantity - totalBatchedQty);

            var wsPackPrice = p.WholeSalePackPrice is > 0m ? p.WholeSalePackPrice.Value : p.PackSalePrice;
            var wsUnitPrice = p.WholeSaleUnitPrice is > 0m
                ? p.WholeSaleUnitPrice.Value
                : Math.Round(wsPackPrice / packSize, 2);

            bool hasCustomPrice = false;
            if (customerCustomPackPrices.TryGetValue(p.Id, out var customPack) && customPack > 0m)
            {
                wsPackPrice = customPack;
                wsUnitPrice = Math.Round(customPack / packSize, 2);
                hasCustomPrice = true;
            }

            if (unbatchedQty > 0 || sortedBatches.Count == 0)
            {
                sortedBatches.Add(new SaleProductBatchOptionDto
                {
                    Id = 0,
                    BatchNumber = "Unbatched (Stock)",
                    ExpiryDate = null,
                    Quantity = unbatchedQty,
                    PackSize = packSize,
                    Rack = p.Rack ?? string.Empty,
                    UnitPurchasePrice = p.UnitPurchasePrice,
                    PackSalePrice = p.PackSalePrice,
                    UnitSalePrice = p.UnitSalePrice,
                    WholeSalePackPrice = wsPackPrice,
                    WholeSaleUnitPrice = wsUnitPrice
                });
            }

            result.Add(new SaleProductSearchItemDto
            {
                Id = p.Id,
                ProductCode = p.ProductCode,
                Barcode = p.Barcode ?? string.Empty,
                Name = p.Name,
                Formulation = p.Formulation ?? string.Empty,
                BrandId = p.BrandId,
                BrandName = p.BrandId.HasValue && brandMap.TryGetValue(p.BrandId.Value, out var bName) ? bName : string.Empty,
                CategoryName = p.CategoryId.HasValue && categoryMap.TryGetValue(p.CategoryId.Value, out var cName) ? cName : string.Empty,
                SupplierName = p.SupplierId.HasValue && supplierMap.TryGetValue(p.SupplierId.Value, out var sName) ? sName : string.Empty,
                Rack = p.Rack ?? string.Empty,
                PackSize = packSize,
                Quantity = p.Quantity,
                UnbatchedQuantity = unbatchedQty,
                PackPurchasePrice = p.PackPurchasePrice,
                UnitPurchasePrice = p.UnitPurchasePrice,
                AvgPrice = p.AvgPrice > 0m ? p.AvgPrice : p.UnitPurchasePrice,
                PackSalePrice = p.PackSalePrice,
                UnitSalePrice = p.UnitSalePrice,
                WholeSalePackPrice = wsPackPrice,
                WholeSaleUnitPrice = wsUnitPrice,
                MaxDiscount = p.MaxDiscount ?? 100m,
                Margin = p.Margin,
                WholeSaleMargin = p.WholeSaleMargin,
                BatchEnabled = p.BatchEnabled ?? "yes",
                IsCustomCustomerPrice = hasCustomPrice,
                Batches = sortedBatches
            });
        }

        return result;
    }

    private static async Task<string> ComputeNextPostedNumberInternalAsync(
        KarobarDbContext db,
        CancellationToken cancellationToken)
    {
        var numbers = await db.SaleInvoices.AsNoTracking()
            .Select(si => si.PostedNumber)
            .ToListAsync(cancellationToken);

        int maxSeq = 0;
        foreach (var num in numbers)
        {
            if (string.IsNullOrWhiteSpace(num))
                continue;

            var digits = new string(num.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var seq) && seq > maxSeq)
            {
                maxSeq = seq;
            }
        }

        return $"SI-{(maxSeq + 1):D6}";
    }

    private static async Task<string> ComputeNextSaleDraftNumberAsync(
        KarobarDbContext db,
        bool isWholesale,
        CancellationToken cancellationToken)
    {
        var kind = isWholesale ? "sale_wholesale" : "sale_retail";
        var prefix = isWholesale ? "DRF-WSL-" : "DRF-RET-";

        var existing = await db.InvoiceDrafts.AsNoTracking()
            .Where(d => d.InvoiceKind == kind)
            .Select(d => d.DraftNumber)
            .ToListAsync(cancellationToken);

        int maxSeq = 0;
        foreach (var num in existing)
        {
            if (string.IsNullOrWhiteSpace(num))
                continue;

            var digits = new string(num.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var seq) && seq > maxSeq)
            {
                maxSeq = seq;
            }
        }

        return $"{prefix}{(maxSeq + 1):D4}";
    }

    private static string BuildAuditSnapshotJson(
        SaleInvoice invoice,
        string customerName,
        List<NormalizedSaleLine> lines)
    {
        var payload = new
        {
            invoice.PostedNumber,
            Date = invoice.Date.ToString("yyyy-MM-dd"),
            invoice.CustomerId,
            CustomerName = customerName,
            invoice.SaleType,
            invoice.InvoiceType,
            invoice.Status,
            invoice.GrossAmount,
            invoice.ItemDiscount,
            invoice.DiscountPercentage,
            invoice.DiscountAmount,
            invoice.TaxPercentage,
            invoice.TaxAmount,
            invoice.Total,
            invoice.TotalReceive,
            Items = lines.Select(l => new
            {
                l.Product.Id,
                l.Product.Name,
                l.BatchNumber,
                Expiry = l.Expiry?.ToString("yyyy-MM-dd"),
                l.Calc.PackSize,
                l.Calc.PackQuantity,
                l.Calc.UnitQuantity,
                l.Calc.Quantity,
                l.Calc.Bonus,
                l.Calc.PackPrice,
                l.Calc.Price,
                l.Calc.ItemDiscountPercentage,
                l.Calc.SubTotal
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private async Task<PurchaseModalWindowLayoutDto> ReadModalWindowLayoutInternalAsync(
        KarobarDbContext db,
        string modalKey,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        var cleanKey = string.IsNullOrWhiteSpace(modalKey) ? "productSearchModal" : modalKey.Trim();
        var isQuickAdd = string.Equals(cleanKey, "productModal", StringComparison.OrdinalIgnoreCase);
        double defaultW = isQuickAdd ? 800d : 960d;
        double defaultH = 600d;
        double minW = isQuickAdd ? 600d : 640d;
        double minH = isQuickAdd ? 400d : 420d;

        var result = new PurchaseModalWindowLayoutDto
        {
            Width = defaultW,
            Height = defaultH
        };

        var posKey = $"{cleanKey}Pos";
        var sizeKey = $"{cleanKey}Size";
        var effectiveUserId = await ResolvePreferenceUserIdAsync(db, actorUserId, cancellationToken);

        var userPrefsJson = await db.Users.AsNoTracking()
            .Where(u => u.Id == effectiveUserId)
            .Select(u => u.Preferences)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(userPrefsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(userPrefsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty(sizeKey, out var sizeEl))
                    {
                        TryApplySizeElement(sizeEl, minW, minH, result);
                    }
                    if (doc.RootElement.TryGetProperty(posKey, out var posEl))
                    {
                        TryApplyPosElement(posEl, result);
                    }
                }
            }
            catch
            {
                // Ignore malformed JSON
            }
        }

        var rows = await db.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == effectiveUserId && (p.Key == posKey || p.Key == sizeKey))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Value))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(row.Value);
                if (string.Equals(row.Key, sizeKey, StringComparison.Ordinal))
                {
                    TryApplySizeElement(doc.RootElement, minW, minH, result);
                }
                else if (string.Equals(row.Key, posKey, StringComparison.Ordinal))
                {
                    TryApplyPosElement(doc.RootElement, result);
                }
            }
            catch
            {
                // Ignore malformed JSON
            }
        }

        return result;
    }

    private static void TryApplySizeElement(JsonElement el, double minW, double minH, PurchaseModalWindowLayoutDto target)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return;

        if (el.TryGetProperty("width", out var wProp) && wProp.TryGetDouble(out var w) && double.IsFinite(w) && w >= minW)
        {
            target.Width = Math.Round(w, 1);
        }
        if (el.TryGetProperty("height", out var hProp) && hProp.TryGetDouble(out var h) && double.IsFinite(h) && h >= minH)
        {
            target.Height = Math.Round(h, 1);
        }
    }

    private static void TryApplyPosElement(JsonElement el, PurchaseModalWindowLayoutDto target)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return;

        if (el.TryGetProperty("x", out var xProp) && xProp.TryGetDouble(out var x) &&
            el.TryGetProperty("y", out var yProp) && yProp.TryGetDouble(out var y) &&
            double.IsFinite(x) && double.IsFinite(y))
        {
            target.X = Math.Round(x, 1);
            target.Y = Math.Round(y, 1);
        }
    }

    private async Task<long> ResolvePreferenceUserIdAsync(
        KarobarDbContext db,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        var candidate = actorUserId ?? _authService.CurrentSession?.UserId;
        if (candidate is > 0)
            return candidate.Value;

        var firstUserId = await db.Users.AsNoTracking()
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return firstUserId > 0 ? firstUserId : 1L;
    }

    private static async Task UpsertUserPreferenceRowAsync(
        KarobarDbContext db,
        long userId,
        string key,
        string jsonValue,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key, cancellationToken);

        if (existing == null)
        {
            db.UserPreferences.Add(new UserPreference
            {
                UserId = userId,
                Key = key,
                Value = jsonValue,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.Value = jsonValue;
            existing.UpdatedAt = now;
        }
    }
}
