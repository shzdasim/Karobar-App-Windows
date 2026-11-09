using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Karobar.Application.Interfaces;
using Karobar.Domain.Entities;
using Karobar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Services;

public sealed class CustomerService : ICustomerService
{
    public const int PreviewInvalidLimit = 20;
    public const int PreviewValidLimit = 10;

    private static readonly string[] NameHeaderAliases =
    [
        "name", "customer", "customer_name", "full_name", "client", "client_name"
    ];

    private static readonly string[] EmailHeaderAliases =
    [
        "email", "email_address", "e-mail", "mail"
    ];

    private static readonly string[] PhoneHeaderAliases =
    [
        "phone", "phone_number", "mobile", "contact", "contact_number", "tel", "telephone"
    ];

    private static readonly string[] AddressHeaderAliases =
    [
        "address", "addr", "customer_address", "location", "street"
    ];

    private static readonly string[] OpeningBalanceHeaderAliases =
    [
        "opening_balance", "opening", "balance"
    ];

    private static readonly Regex NumericRegex = new(@"^[+-]?\d+(\.\d+)?$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;

    public CustomerService(IDbContextFactory<KarobarDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public static string NormalizeWhatsAppPhone(string? rawPhone, string defaultCountryCode = "92")
    {
        string raw = (rawPhone ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        string digits = Regex.Replace(raw, @"\D+", string.Empty);
        if (string.IsNullOrEmpty(digits))
            return string.Empty;

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        else if (digits.StartsWith('0') && digits.Length >= 10 && digits.Length <= 11)
        {
            digits = defaultCountryCode + digits[1..];
        }
        else if (digits.Length == 10 && digits.StartsWith('3'))
        {
            digits = defaultCountryCode + digits;
        }

        return digits.Length >= 8 ? digits : string.Empty;
    }

    public static DateTime CalculateAdvancedRefillDate(DateTime baseDate, string? cycle, int? customDays)
    {
        string normalizedCycle = string.IsNullOrWhiteSpace(cycle) ? "monthly" : cycle.Trim().ToLowerInvariant();
        return normalizedCycle switch
        {
            "weekly" => baseDate.Date.AddDays(7),
            "biweekly" => baseDate.Date.AddDays(14),
            "custom" => baseDate.Date.AddDays(Math.Max(1, customDays ?? 30)),
            _ => baseDate.Date.AddMonths(1)
        };
    }

    public async Task<List<CustomerDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var customers = await db.Customers.AsNoTracking()
            .OrderByDescending(c => c.Id)
            .ToListAsync(cancellationToken);

        if (customers.Count == 0)
            return [];

        return await HydrateCustomerDtosAsync(db, customers, cancellationToken);
    }

    public async Task<List<CustomerDto>> SearchAsync(
        string? query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        string q = query?.Trim() ?? string.Empty;
        int safeLimit = Math.Clamp(limit, 1, 100);

        var dbQuery = db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(q))
        {
            dbQuery = dbQuery.Where(c =>
                EF.Functions.Like(c.Name, $"%{q}%") ||
                (c.Phone != null && EF.Functions.Like(c.Phone, $"%{q}%")) ||
                (c.Email != null && EF.Functions.Like(c.Email, $"%{q}%")));
        }

        var customers = await dbQuery
            .OrderBy(c => c.Name)
            .Take(safeLimit)
            .ToListAsync(cancellationToken);

        if (customers.Count == 0)
            return [];

        return await HydrateCustomerDtosAsync(db, customers, cancellationToken);
    }

    public async Task<CustomerDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer is null)
            return null;

        var list = await HydrateCustomerDtosAsync(db, [customer], cancellationToken);
        return list.FirstOrDefault();
    }

    public async Task<CustomerDto> CreateAsync(
        CustomerUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Customer name is required.");
        if (name.Length > 255)
            throw new InvalidOperationException("Customer name must not exceed 255 characters.");

        string? email = NullIfWhiteSpace(request.Email);
        if (email != null && !EmailRegex.IsMatch(email))
            throw new InvalidOperationException("Please enter a valid email address.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        if (email != null)
        {
            bool emailExists = await db.Customers
                .AnyAsync(c => c.Email != null && c.Email.ToLower() == email.ToLower(), cancellationToken);
            if (emailExists)
                throw new InvalidOperationException($"A customer with email '{email}' already exists.");
        }

        var now = DateTime.UtcNow;
        bool isRegular = request.IsRegular;
        string refillCycle = NormalizeRefillCycle(request.RefillCycle);
        int? refillCustomDays = isRegular && refillCycle == "custom"
            ? Math.Clamp(request.RefillCustomDays ?? 30, 1, 365)
            : null;
        int reminderLeadDays = Math.Clamp(request.ReminderLeadDays ?? 3, 0, 30);

        DateTime? nextRefillDate = null;
        if (isRegular)
        {
            if (!string.IsNullOrWhiteSpace(request.NextRefillDate) &&
                DateTime.TryParse(request.NextRefillDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                nextRefillDate = parsedDate.Date;
            }
            else
            {
                nextRefillDate = CalculateAdvancedRefillDate(DateTime.Today, refillCycle, refillCustomDays);
            }
        }

        var entity = new Customer
        {
            Name = name,
            CustomerType = "retail",
            Email = email,
            Phone = NullIfWhiteSpace(request.Phone),
            TaxNumber = NullIfWhiteSpace(request.TaxNumber),
            TaxStatus = NormalizeTaxStatus(request.TaxStatus),
            Address = NullIfWhiteSpace(request.Address),
            CreditLimit = request.CreditLimit.HasValue && request.CreditLimit.Value >= 0m
                ? Math.Round(request.CreditLimit.Value, 2)
                : null,
            CreditDays = request.CreditDays.HasValue && request.CreditDays.Value >= 0
                ? request.CreditDays.Value
                : 30,
            CreditStatus = NormalizeCreditStatus(request.CreditStatus),
            IsRegular = isRegular,
            RefillCycle = refillCycle,
            RefillCustomDays = refillCustomDays,
            ReminderLeadDays = reminderLeadDays,
            NextRefillDate = nextRefillDate,
            ReminderLanguage = NormalizeReminderLanguage(request.ReminderLanguage),
            RegularNotes = isRegular ? NullIfWhiteSpace(request.RegularNotes) : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Customers.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        await SyncRegularItemsAsync(db, entity.Id, isRegular ? request.RegularItems : [], now, cancellationToken);

        decimal openingBalance = request.OpeningBalance.HasValue && request.OpeningBalance.Value > 0m
            ? Math.Round(request.OpeningBalance.Value, 2)
            : 0m;

        if (openingBalance > 0m)
        {
            db.CustomerLedgers.Add(new CustomerLedger
            {
                CustomerId = entity.Id,
                SaleInvoiceId = null,
                EntryType = "manual",
                EntryDate = DateTime.Today,
                PostedNumber = $"OB-{entity.Id:D5}",
                InvoiceTotal = openingBalance,
                TotalReceived = 0m,
                BalanceRemaining = openingBalance,
                CreditedAmount = 0m,
                TotalBalance = openingBalance,
                Description = "Opening Balance",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return (await GetByIdAsync(entity.Id, cancellationToken))!;
    }

    public async Task<CustomerDto> UpdateAsync(
        long id,
        CustomerUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Customer name is required.");
        if (name.Length > 255)
            throw new InvalidOperationException("Customer name must not exceed 255 characters.");

        string? email = NullIfWhiteSpace(request.Email);
        if (email != null && !EmailRegex.IsMatch(email))
            throw new InvalidOperationException("Please enter a valid email address.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");

        if (email != null)
        {
            bool emailExists = await db.Customers
                .AnyAsync(c => c.Id != id && c.Email != null && c.Email.ToLower() == email.ToLower(), cancellationToken);
            if (emailExists)
                throw new InvalidOperationException($"A customer with email '{email}' already exists.");
        }

        var now = DateTime.UtcNow;
        bool isRegular = request.IsRegular;
        string refillCycle = NormalizeRefillCycle(request.RefillCycle);
        int? refillCustomDays = isRegular && refillCycle == "custom"
            ? Math.Clamp(request.RefillCustomDays ?? 30, 1, 365)
            : null;
        int reminderLeadDays = Math.Clamp(request.ReminderLeadDays ?? 3, 0, 30);

        DateTime? nextRefillDate = null;
        if (isRegular)
        {
            if (!string.IsNullOrWhiteSpace(request.NextRefillDate) &&
                DateTime.TryParse(request.NextRefillDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                nextRefillDate = parsedDate.Date;
            }
            else if (entity.NextRefillDate.HasValue)
            {
                nextRefillDate = entity.NextRefillDate.Value.Date;
            }
            else
            {
                nextRefillDate = CalculateAdvancedRefillDate(DateTime.Today, refillCycle, refillCustomDays);
            }
        }

        entity.Name = name;
        entity.Email = email;
        entity.Phone = NullIfWhiteSpace(request.Phone);
        entity.TaxNumber = NullIfWhiteSpace(request.TaxNumber);
        entity.TaxStatus = NormalizeTaxStatus(request.TaxStatus);
        entity.Address = NullIfWhiteSpace(request.Address);
        entity.CreditLimit = request.CreditLimit.HasValue && request.CreditLimit.Value >= 0m
            ? Math.Round(request.CreditLimit.Value, 2)
            : null;
        entity.CreditDays = request.CreditDays.HasValue && request.CreditDays.Value >= 0
            ? request.CreditDays.Value
            : 30;
        entity.CreditStatus = NormalizeCreditStatus(request.CreditStatus);
        entity.IsRegular = isRegular;
        entity.RefillCycle = refillCycle;
        entity.RefillCustomDays = refillCustomDays;
        entity.ReminderLeadDays = reminderLeadDays;
        entity.NextRefillDate = nextRefillDate;
        entity.ReminderLanguage = NormalizeReminderLanguage(request.ReminderLanguage);
        entity.RegularNotes = isRegular ? NullIfWhiteSpace(request.RegularNotes) : null;
        entity.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await SyncRegularItemsAsync(db, entity.Id, isRegular ? request.RegularItems : [], now, cancellationToken);

        return (await GetByIdAsync(entity.Id, cancellationToken))!;
    }

    public async Task DeleteAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");

        if (string.Equals(entity.Name, "WALK-IN-CUSTOMER", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot delete system default WALK-IN-CUSTOMER.");

        int salesCount = await db.SaleInvoices.CountAsync(s => s.CustomerId == id, cancellationToken);
        int returnsCount = await db.SaleReturns.CountAsync(r => r.CustomerId == id, cancellationToken);
        if (salesCount + returnsCount > 0)
            throw new InvalidOperationException("Cannot delete: customer has related sale invoices/returns.");

        var regularItems = await db.CustomerRegularItems
            .Where(ri => ri.CustomerId == id)
            .ToListAsync(cancellationToken);
        if (regularItems.Count > 0)
            db.CustomerRegularItems.RemoveRange(regularItems);

        var customPrices = await db.CustomerWholeSalePrices
            .Where(wp => wp.CustomerId == id)
            .ToListAsync(cancellationToken);
        if (customPrices.Count > 0)
            db.CustomerWholeSalePrices.RemoveRange(customPrices);

        db.Customers.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CustomerDto> AdvanceRefillCycleAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");

        DateTime baseDate = entity.NextRefillDate.HasValue && entity.NextRefillDate.Value.Date > DateTime.Today
            ? entity.NextRefillDate.Value.Date
            : DateTime.Today;

        entity.IsRegular = true;
        entity.NextRefillDate = CalculateAdvancedRefillDate(baseDate, entity.RefillCycle, entity.RefillCustomDays);
        entity.LastRefillCollectedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(id, cancellationToken))!;
    }

    public async Task<CustomerCreditSummaryDto> GetCreditSummaryAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");

        decimal netBalance = await db.CustomerLedgers.AsNoTracking()
            .Where(cl => cl.CustomerId == id)
            .SumAsync(cl => (decimal?)(cl.InvoiceTotal - cl.TotalReceived - cl.CreditedAmount), cancellationToken) ?? 0m;

        decimal currentBalance = Math.Max(0m, Math.Round(netBalance, 2));
        decimal? creditLimit = customer.CreditLimit.HasValue ? Math.Round(customer.CreditLimit.Value, 2) : null;
        decimal? availableCredit = creditLimit.HasValue && creditLimit.Value > 0m
            ? Math.Max(0m, Math.Round(creditLimit.Value - currentBalance, 2))
            : null;
        bool isExceeded = creditLimit.HasValue && creditLimit.Value > 0m && currentBalance > creditLimit.Value;

        var today = DateTime.Today;
        var overdueInvoices = await db.SaleInvoices.AsNoTracking()
            .Where(si =>
                si.CustomerId == id &&
                si.Status != "reversed" &&
                si.DueDate != null &&
                si.DueDate < today &&
                si.RemainThisBill > 0m)
            .Select(si => new
            {
                si.RemainThisBill,
                si.DueDate
            })
            .ToListAsync(cancellationToken);

        decimal totalOverdue = Math.Round(overdueInvoices.Sum(x => Math.Max(0m, x.RemainThisBill)), 2);
        DateTime? oldestDue = overdueInvoices
            .Where(x => x.DueDate.HasValue)
            .Select(x => x.DueDate!.Value)
            .OrderBy(d => d)
            .FirstOrDefault();

        return new CustomerCreditSummaryDto
        {
            CustomerId = customer.Id,
            Name = customer.Name,
            Phone = customer.Phone,
            CreditStatus = string.IsNullOrWhiteSpace(customer.CreditStatus) ? "active" : customer.CreditStatus,
            CreditLimit = creditLimit,
            CreditDays = customer.CreditDays ?? 30,
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

    public async Task<CustomerRefillReminderPreviewDto> GetRefillReminderPreviewAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var customerDto = await GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");

        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        string storeName = !string.IsNullOrWhiteSpace(setting?.StoreName) ? setting.StoreName! : "Karobar Pharmacy";
        string storePhone = setting?.PhoneNumber ?? string.Empty;

        string englishMsg = BuildRefillReminderMessage(customerDto, storeName, storePhone, "english");
        string urduMsg = BuildRefillReminderMessage(customerDto, storeName, storePhone, "urdu");

        return new CustomerRefillReminderPreviewDto
        {
            CustomerId = customerDto.Id,
            CustomerName = customerDto.Name,
            Phone = customerDto.Phone ?? string.Empty,
            NormalizedPhone = NormalizeWhatsAppPhone(customerDto.Phone),
            RefillCycle = customerDto.RefillCycle,
            NextRefillDate = customerDto.NextRefillDate,
            StockReady = customerDto.StockReady,
            OutOfStockCount = customerDto.OutOfStockCount,
            RegularItems = customerDto.RegularItems,
            EnglishMessage = englishMsg,
            UrduMessage = urduMsg
        };
    }

    public async Task<(string WhatsAppUrl, CustomerDto UpdatedCustomer)> RecordRefillReminderSentAsync(
        long id,
        string phone,
        string language,
        string message,
        bool savePhone,
        bool advanceCycle,
        CancellationToken cancellationToken = default)
    {
        string normalizedPhone = NormalizeWhatsAppPhone(phone);
        if (string.IsNullOrEmpty(normalizedPhone))
            throw new InvalidOperationException("Please enter a valid WhatsApp phone number (e.g., 03001234567 or +923001234567).");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");

        if (savePhone && !string.IsNullOrWhiteSpace(phone))
        {
            entity.Phone = phone.Trim();
        }

        entity.LastRefillRemindedAt = DateTime.UtcNow;
        entity.ReminderLanguage = NormalizeReminderLanguage(language);

        if (advanceCycle)
        {
            DateTime baseDate = entity.NextRefillDate.HasValue && entity.NextRefillDate.Value.Date > DateTime.Today
                ? entity.NextRefillDate.Value.Date
                : DateTime.Today;
            entity.NextRefillDate = CalculateAdvancedRefillDate(baseDate, entity.RefillCycle, entity.RefillCustomDays);
            entity.LastRefillCollectedAt = DateTime.UtcNow;
        }

        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var updatedDto = (await GetByIdAsync(id, cancellationToken))!;
        string finalMsg = string.IsNullOrWhiteSpace(message)
            ? BuildRefillReminderMessage(updatedDto, "Karobar Pharmacy", string.Empty, entity.ReminderLanguage ?? "english")
            : message.Trim();

        string waUrl = $"https://wa.me/{normalizedPhone}?text={Uri.EscapeDataString(finalMsg)}";
        return (waUrl, updatedDto);
    }

    public async Task<List<CustomerWholeSalePriceDto>> GetWholeSalePricesAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var rows = await (
            from wp in db.CustomerWholeSalePrices.AsNoTracking()
            join p in db.Products.AsNoTracking() on wp.ProductId equals p.Id
            where wp.CustomerId == customerId
            orderby p.Name ascending
            select new
            {
                wp.Id,
                wp.CustomerId,
                wp.ProductId,
                ProductName = p.Name,
                ProductCode = p.ProductCode,
                PackSize = p.PackSize > 0 ? p.PackSize : 1,
                StandardWholeSalePackPrice = p.WholeSalePackPrice ?? p.PackSalePrice,
                p.PackSalePrice,
                PackPrice = wp.PackPrice ?? 0m,
                wp.UpdatedAt
            }).ToListAsync(cancellationToken);

        return rows.Select(r => new CustomerWholeSalePriceDto
        {
            Id = r.Id,
            CustomerId = r.CustomerId,
            ProductId = r.ProductId,
            ProductName = r.ProductName,
            ProductCode = r.ProductCode ?? string.Empty,
            PackSize = r.PackSize,
            StandardWholeSalePackPrice = Math.Round(r.StandardWholeSalePackPrice, 2),
            PackSalePrice = Math.Round(r.PackSalePrice, 2),
            PackPrice = Math.Round(r.PackPrice, 2),
            ImpliedUnitPrice = Math.Round(r.PackPrice / Math.Max(1, r.PackSize), 2),
            UpdatedAt = r.UpdatedAt
        }).ToList();
    }

    public async Task<CustomerWholeSalePriceDto> SaveWholeSalePriceAsync(
        long customerId,
        long productId,
        decimal packPrice,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
            throw new InvalidOperationException("Please select a product.");
        if (packPrice < 0m)
            throw new InvalidOperationException("Custom pack price cannot be negative.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new InvalidOperationException("Selected product not found.");

        int packSize = Math.Max(1, product.PackSize);
        decimal roundedPack = Math.Round(packPrice, 2);
        decimal impliedUnit = Math.Round(roundedPack / packSize, 2);
        var now = DateTime.UtcNow;

        var existing = await db.CustomerWholeSalePrices
            .FirstOrDefaultAsync(wp => wp.CustomerId == customerId && wp.ProductId == productId, cancellationToken);

        if (existing != null)
        {
            existing.PackPrice = roundedPack;
            existing.UnitPrice = impliedUnit;
            existing.UpdatedAt = now;
        }
        else
        {
            existing = new CustomerWholeSalePrice
            {
                CustomerId = customerId,
                ProductId = productId,
                PackPrice = roundedPack,
                UnitPrice = impliedUnit,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.CustomerWholeSalePrices.Add(existing);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new CustomerWholeSalePriceDto
        {
            Id = existing.Id,
            CustomerId = customerId,
            ProductId = product.Id,
            ProductName = product.Name,
            ProductCode = product.ProductCode ?? string.Empty,
            PackSize = packSize,
            StandardWholeSalePackPrice = Math.Round(product.WholeSalePackPrice ?? product.PackSalePrice, 2),
            PackSalePrice = Math.Round(product.PackSalePrice, 2),
            PackPrice = roundedPack,
            ImpliedUnitPrice = impliedUnit,
            UpdatedAt = existing.UpdatedAt
        };
    }

    public async Task DeleteWholeSalePriceAsync(
        long customerId,
        long priceId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.CustomerWholeSalePrices
            .FirstOrDefaultAsync(wp => wp.Id == priceId && wp.CustomerId == customerId, cancellationToken)
            ?? throw new InvalidOperationException("Custom wholesale price entry not found.");

        db.CustomerWholeSalePrices.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<CustomerCatalogProductDto>> SearchProductsForCatalogAsync(
        string? searchQuery = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        string q = searchQuery?.Trim() ?? string.Empty;
        int safeLimit = Math.Clamp(limit, 1, 300);

        var query = db.Products.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(q))
        {
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{q}%") ||
                EF.Functions.Like(p.ProductCode, $"%{q}%") ||
                (p.Formulation != null && EF.Functions.Like(p.Formulation, $"%{q}%")));
        }

        var products = await query
            .OrderBy(p => p.Name)
            .Take(safeLimit)
            .Select(p => new CustomerCatalogProductDto
            {
                Id = p.Id,
                Name = p.Name,
                ProductCode = p.ProductCode ?? string.Empty,
                PackSize = p.PackSize > 0 ? p.PackSize : 1,
                OnHandUnits = p.Quantity,
                UnitSalePrice = p.UnitSalePrice,
                PackSalePrice = p.PackSalePrice,
                WholeSalePackPrice = p.WholeSalePackPrice ?? p.PackSalePrice,
                WholeSaleUnitPrice = p.WholeSaleUnitPrice ?? p.UnitSalePrice
            })
            .ToListAsync(cancellationToken);

        return products;
    }

    public async Task ExportCsvAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidOperationException("Please provide a valid export file path.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var items = await db.Customers.AsNoTracking()
            .OrderByDescending(c => c.Id)
            .Select(c => new { c.Name, c.Email, c.Phone, c.Address })
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("name,email,phone,address");

        foreach (var item in items)
        {
            sb.Append(EscapeCsvField(item.Name));
            sb.Append(',');
            sb.Append(EscapeCsvField(item.Email ?? string.Empty));
            sb.Append(',');
            sb.Append(EscapeCsvField(item.Phone ?? string.Empty));
            sb.Append(',');
            sb.AppendLine(EscapeCsvField(item.Address ?? string.Empty));
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
        sb.AppendLine("name,email,phone,address,opening_balance");
        sb.AppendLine("Ali Khan,ali@example.com,0300-1234567,House 10 Model Town,2500");
        sb.AppendLine("Ayesha Clinic,,0321-7654321,Main Boulevard Gulberg,18000");
        sb.AppendLine("Zain Medical Store,zain@med.pk,0333-9876543,,0");

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    public async Task<CustomerCsvValidationResult> ValidateCsvAsync(
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
            return new CustomerCsvValidationResult
            {
                Ok = false,
                Message = parsed.Message,
                UsedDelimiter = parsed.UsedDelimiter
            };
        }

        var invalidSamples = new List<CustomerCsvSampleRow>();
        var validSamples = new List<CustomerCsvSampleRow>();
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

        return new CustomerCsvValidationResult
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

    public async Task<CustomerCsvCommitStats> CommitCsvAsync(
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
            return new CustomerCsvCommitStats
            {
                Total = total,
                Valid = 0,
                Invalid = invalidCount,
                Committed = 0,
                NewCount = 0,
                ExistingCount = 0
            };
        }

        var uniqueRows = new Dictionary<string, CustomerCsvSampleRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in validRows)
        {
            string key = !string.IsNullOrWhiteSpace(r.Email)
                ? $"email:{r.Email.ToLowerInvariant()}"
                : $"np:{r.Name.ToLowerInvariant()}|{r.Phone.ToLowerInvariant()}";
            uniqueRows[key] = r;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existingCustomers = await db.Customers.ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        int newCount = 0;
        int existingCount = 0;
        var pendingOpeningBalances = new List<(Customer Entity, decimal Balance)>();

        foreach (var row in uniqueRows.Values)
        {
            Customer? match = null;
            if (!string.IsNullOrWhiteSpace(row.Email))
            {
                match = existingCustomers.FirstOrDefault(c =>
                    !string.IsNullOrWhiteSpace(c.Email) &&
                    string.Equals(c.Email.Trim(), row.Email, StringComparison.OrdinalIgnoreCase));
            }
            if (match == null)
            {
                match = existingCustomers.FirstOrDefault(c =>
                    string.Equals(c.Name.Trim(), row.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((c.Phone ?? string.Empty).Trim(), row.Phone, StringComparison.OrdinalIgnoreCase));
            }

            if (match != null)
            {
                existingCount++;
                match.Name = row.Name;
                if (!string.IsNullOrWhiteSpace(row.Email))
                    match.Email = row.Email;
                if (!string.IsNullOrWhiteSpace(row.Phone))
                    match.Phone = row.Phone;
                if (!string.IsNullOrWhiteSpace(row.Address))
                    match.Address = row.Address;
                match.UpdatedAt = now;
            }
            else
            {
                newCount++;
                var newCust = new Customer
                {
                    Name = row.Name,
                    CustomerType = "retail",
                    Email = NullIfWhiteSpace(row.Email),
                    Phone = NullIfWhiteSpace(row.Phone),
                    Address = NullIfWhiteSpace(row.Address),
                    TaxStatus = "unregistered",
                    CreditDays = 30,
                    CreditStatus = "active",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Customers.Add(newCust);
                existingCustomers.Add(newCust);

                if (row.OpeningBalance > 0m)
                {
                    pendingOpeningBalances.Add((newCust, Math.Round(row.OpeningBalance, 2)));
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (pendingOpeningBalances.Count > 0)
        {
            foreach (var (custEntity, balance) in pendingOpeningBalances)
            {
                db.CustomerLedgers.Add(new CustomerLedger
                {
                    CustomerId = custEntity.Id,
                    SaleInvoiceId = null,
                    EntryType = "manual",
                    EntryDate = DateTime.Today,
                    PostedNumber = $"OB-{custEntity.Id:D5}",
                    InvoiceTotal = balance,
                    TotalReceived = 0m,
                    BalanceRemaining = balance,
                    CreditedAmount = 0m,
                    TotalBalance = balance,
                    Description = "Opening Balance (Imported)",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        return new CustomerCsvCommitStats
        {
            Total = total,
            Valid = validCount,
            Invalid = invalidCount,
            Committed = uniqueRows.Count,
            NewCount = newCount,
            ExistingCount = existingCount
        };
    }

    private static async Task<List<CustomerDto>> HydrateCustomerDtosAsync(
        KarobarDbContext db,
        List<Customer> customers,
        CancellationToken cancellationToken)
    {
        var customerIds = customers.Select(c => c.Id).ToList();

        var saleCounts = await db.SaleInvoices.AsNoTracking()
            .Where(s => customerIds.Contains(s.CustomerId))
            .GroupBy(s => s.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count, cancellationToken);

        var returnCounts = await db.SaleReturns.AsNoTracking()
            .Where(r => customerIds.Contains(r.CustomerId))
            .GroupBy(r => r.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count, cancellationToken);

        var ledgerBalances = await db.CustomerLedgers.AsNoTracking()
            .Where(cl => customerIds.Contains(cl.CustomerId))
            .GroupBy(cl => cl.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                NetBalance = g.Sum(x => x.InvoiceTotal - x.TotalReceived - x.CreditedAmount)
            })
            .ToDictionaryAsync(x => x.CustomerId, x => x.NetBalance, cancellationToken);

        var regularItemRows = await (
            from ri in db.CustomerRegularItems.AsNoTracking()
            join p in db.Products.AsNoTracking() on ri.ProductId equals p.Id
            where customerIds.Contains(ri.CustomerId)
            orderby ri.Id ascending
            select new
            {
                ri.Id,
                ri.CustomerId,
                ri.ProductId,
                ri.PackQuantity,
                ri.UnitQuantity,
                ri.DosageQuantity,
                ri.DosageNotes,
                ri.DosageInstruction,
                ri.Notes,
                ProductName = p.Name,
                ProductCode = p.ProductCode,
                PackSize = p.PackSize > 0 ? p.PackSize : 1,
                ProductQuantity = p.Quantity,
                p.UnitSalePrice,
                p.PackSalePrice
            }).ToListAsync(cancellationToken);

        var productIds = regularItemRows.Select(r => r.ProductId).Distinct().ToList();
        var batchStockByProduct = productIds.Count > 0
            ? await db.Batches.AsNoTracking()
                .Where(b => productIds.Contains(b.ProductId) && b.Quantity > 0)
                .GroupBy(b => b.ProductId)
                .Select(g => new { ProductId = g.Key, Stock = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Stock, cancellationToken)
            : new Dictionary<long, int>();

        var regularItemsByCustomer = new Dictionary<long, List<CustomerRegularItemDto>>();
        foreach (var r in regularItemRows)
        {
            int packSize = Math.Max(1, r.PackSize);
            int packQty = Math.Max(0, r.PackQuantity);
            int unitQty = r.UnitQuantity > 0 ? r.UnitQuantity : Math.Max(1, r.DosageQuantity);
            if (packQty > 0 && unitQty < packQty * packSize)
            {
                unitQty = packQty * packSize;
            }
            int requiredUnits = Math.Max(1, unitQty);

            int onHand = batchStockByProduct.TryGetValue(r.ProductId, out int batchSum) && batchSum > 0
                ? batchSum
                : Math.Max(0, r.ProductQuantity);

            string? notes = !string.IsNullOrWhiteSpace(r.DosageNotes)
                ? r.DosageNotes
                : (!string.IsNullOrWhiteSpace(r.DosageInstruction) ? r.DosageInstruction : r.Notes);

            if (!regularItemsByCustomer.TryGetValue(r.CustomerId, out var list))
            {
                list = [];
                regularItemsByCustomer[r.CustomerId] = list;
            }

            list.Add(new CustomerRegularItemDto
            {
                Id = r.Id,
                CustomerId = r.CustomerId,
                ProductId = r.ProductId,
                ProductName = r.ProductName,
                ProductCode = r.ProductCode ?? string.Empty,
                PackSize = packSize,
                OnHandUnits = onHand,
                PackQuantity = packQty,
                UnitQuantity = requiredUnits,
                RequiredTotalUnits = requiredUnits,
                InStock = onHand >= requiredUnits,
                UnitSalePrice = Math.Round(r.UnitSalePrice, 2),
                PackSalePrice = Math.Round(r.PackSalePrice, 2),
                DosageNotes = notes
            });
        }

        DateTime today = DateTime.Today;
        var result = new List<CustomerDto>(customers.Count);

        foreach (var c in customers)
        {
            saleCounts.TryGetValue(c.Id, out int sCount);
            returnCounts.TryGetValue(c.Id, out int rCount);
            ledgerBalances.TryGetValue(c.Id, out decimal netBal);
            regularItemsByCustomer.TryGetValue(c.Id, out var regItems);
            regItems ??= [];

            int outOfStockCount = regItems.Count(i => !i.InStock);
            bool stockReady = regItems.Count > 0 && outOfStockCount == 0;

            int leadDays = c.ReminderLeadDays ?? 3;
            int? daysUntil = null;
            bool isDue = false;
            bool isOverdue = false;

            if (c.IsRegular && c.NextRefillDate.HasValue)
            {
                daysUntil = (c.NextRefillDate.Value.Date - today).Days;
                isDue = daysUntil.Value <= leadDays;
                isOverdue = daysUntil.Value < 0;
            }

            result.Add(new CustomerDto
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Phone = c.Phone,
                TaxNumber = c.TaxNumber,
                TaxStatus = string.IsNullOrWhiteSpace(c.TaxStatus) ? "unregistered" : c.TaxStatus!,
                Address = c.Address,
                CreditLimit = c.CreditLimit,
                CreditDays = c.CreditDays ?? 30,
                CreditStatus = string.IsNullOrWhiteSpace(c.CreditStatus) ? "active" : c.CreditStatus,
                IsRegular = c.IsRegular,
                RefillCycle = string.IsNullOrWhiteSpace(c.RefillCycle) ? "monthly" : c.RefillCycle!,
                RefillCustomDays = c.RefillCustomDays,
                ReminderLeadDays = leadDays,
                NextRefillDate = c.NextRefillDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                LastRefillRemindedAt = c.LastRefillRemindedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                LastRefillCollectedAt = c.LastRefillCollectedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ReminderLanguage = string.IsNullOrWhiteSpace(c.ReminderLanguage) ? "english" : c.ReminderLanguage!,
                RegularNotes = c.RegularNotes,
                TransactionsCount = sCount + rCount,
                RegularItemsCount = regItems.Count,
                OutOfStockCount = outOfStockCount,
                StockReady = stockReady,
                DaysUntilRefill = daysUntil,
                IsDueForReminder = isDue,
                IsOverdueRefill = isOverdue,
                CurrentReceivable = Math.Max(0m, Math.Round(netBal, 2)),
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                RegularItems = regItems
            });
        }

        return result;
    }

    private static async Task SyncRegularItemsAsync(
        KarobarDbContext db,
        long customerId,
        List<CustomerRegularItemUpsertDto>? items,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await db.CustomerRegularItems
            .Where(ri => ri.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            db.CustomerRegularItems.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (items == null || items.Count == 0)
            return;

        var uniqueByProduct = new Dictionary<long, CustomerRegularItemUpsertDto>();
        foreach (var item in items)
        {
            if (item.ProductId <= 0)
                continue;
            uniqueByProduct[item.ProductId] = item;
        }

        foreach (var item in uniqueByProduct.Values)
        {
            int packQty = Math.Max(0, item.PackQuantity);
            int unitQty = Math.Max(1, item.UnitQuantity);
            string? notes = NullIfWhiteSpace(item.DosageNotes);

            db.CustomerRegularItems.Add(new CustomerRegularItem
            {
                CustomerId = customerId,
                ProductId = item.ProductId,
                PackQuantity = packQty,
                UnitQuantity = unitQty,
                DosageQuantity = unitQty,
                DosageNotes = notes,
                DosageInstruction = notes,
                Notes = notes,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static string BuildRefillReminderMessage(
        CustomerDto customer,
        string storeName,
        string storePhone,
        string language)
    {
        var inStockItems = customer.RegularItems.Where(i => i.InStock).ToList();
        var itemLines = new List<string>();
        foreach (var item in inStockItems)
        {
            string qtyPart = item.PackQuantity > 0
                ? $"{item.PackQuantity} Pack ({item.RequiredTotalUnits} units)"
                : $"{item.RequiredTotalUnits} units";
            string notePart = !string.IsNullOrWhiteSpace(item.DosageNotes) ? $" — {item.DosageNotes}" : string.Empty;
            itemLines.Add($"• {item.ProductName} ({qtyPart}){notePart}");
        }

        if (string.Equals(language, "urdu", StringComparison.OrdinalIgnoreCase))
        {
            var lines = new List<string>
            {
                $"السلام علیکم {customer.Name} صاحب/صاحبہ،",
                $"امید ہے آپ خیریت سے ہوں گے۔ *{storeName}* کی جانب سے آپ کو یاد دہانی کرائی جاتی ہے کہ آپ کی معمول کی ادویات ری فل کا وقت ہو گیا ہے اور ہمارے پاس اسٹاک میں دستیاب ہیں:"
            };

            if (itemLines.Count > 0)
            {
                lines.Add(string.Empty);
                lines.AddRange(itemLines);
            }

            if (!string.IsNullOrWhiteSpace(customer.NextRefillDate))
            {
                lines.Add(string.Empty);
                lines.Add($"📅 مقررہ تاریخ: {customer.NextRefillDate}");
            }

            lines.Add(string.Empty);
            lines.Add("براہِ کرم اپنی سہولت کے مطابق تشریف لائیں یا ہوم ڈیلیوری/آرڈر کی تصدیق کے لیے اس پیغام کا جواب دیں۔");
            if (!string.IsNullOrWhiteSpace(storePhone))
            {
                lines.Add($"📞 رابطہ نمبر: {storePhone}");
            }
            lines.Add($"شکریہ — *{storeName}*");

            return string.Join("\n", lines);
        }
        else
        {
            string cycleLabel = customer.RefillCycle switch
            {
                "weekly" => "weekly",
                "biweekly" => "bi-weekly",
                "custom" => "scheduled",
                _ => "monthly"
            };

            var lines = new List<string>
            {
                $"Assalam-o-Alaikum *{customer.Name}*,",
                $"This is a friendly reminder from *{storeName}* that your {cycleLabel} regular medicines/items are ready and available in stock:"
            };

            if (itemLines.Count > 0)
            {
                lines.Add(string.Empty);
                lines.AddRange(itemLines);
            }

            if (!string.IsNullOrWhiteSpace(customer.NextRefillDate))
            {
                lines.Add(string.Empty);
                lines.Add($"📅 Scheduled Refill Date: {customer.NextRefillDate}");
            }

            lines.Add(string.Empty);
            lines.Add("Please visit our store at your convenience or reply to this message to confirm your order.");
            if (!string.IsNullOrWhiteSpace(storePhone))
            {
                lines.Add($"📞 Contact: {storePhone}");
            }
            lines.Add($"Thank you — *{storeName}*");

            return string.Join("\n", lines);
        }
    }

    private static string NormalizeTaxStatus(string? raw)
    {
        string s = string.IsNullOrWhiteSpace(raw) ? "unregistered" : raw.Trim().ToLowerInvariant();
        return s is "registered" or "exempt" or "unregistered" ? s : "unregistered";
    }

    private static string NormalizeCreditStatus(string? raw)
    {
        string s = string.IsNullOrWhiteSpace(raw) ? "active" : raw.Trim().ToLowerInvariant();
        return s is "active" or "hold" or "blocked" ? s : "active";
    }

    private static string NormalizeRefillCycle(string? raw)
    {
        string s = string.IsNullOrWhiteSpace(raw) ? "monthly" : raw.Trim().ToLowerInvariant();
        return s is "weekly" or "biweekly" or "monthly" or "custom" ? s : "monthly";
    }

    private static string NormalizeReminderLanguage(string? raw)
    {
        string s = string.IsNullOrWhiteSpace(raw) ? "english" : raw.Trim().ToLowerInvariant();
        return s is "urdu" or "english" ? s : "english";
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (bool Ok, string Message, string UsedDelimiter, List<CustomerCsvSampleRow> Rows) ParseAndValidateCsvFile(
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
        int emailIdx = FindHeaderIndex(normalizedHeaders, EmailHeaderAliases);
        int phoneIdx = FindHeaderIndex(normalizedHeaders, PhoneHeaderAliases);
        int addrIdx = FindHeaderIndex(normalizedHeaders, AddressHeaderAliases);
        int obIdx = FindHeaderIndex(normalizedHeaders, OpeningBalanceHeaderAliases);

        if (nameIdx < 0)
        {
            return (
                false,
                "Missing required column: \"name\" (accepted aliases: name, customer, customer_name, full_name, client, client_name).",
                usedDelimDisplay,
                []);
        }

        var rows = new List<CustomerCsvSampleRow>();
        var seenEmails = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenNamePhones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
            string rawEmail = emailIdx >= 0 && emailIdx < cols.Count ? cols[emailIdx] : string.Empty;
            string rawPhone = phoneIdx >= 0 && phoneIdx < cols.Count ? cols[phoneIdx] : string.Empty;
            string rawAddr = addrIdx >= 0 && addrIdx < cols.Count ? cols[addrIdx] : string.Empty;
            string rawOb = obIdx >= 0 && obIdx < cols.Count ? cols[obIdx] : string.Empty;

            string name = CollapseSpaces(rawName);
            string email = CollapseSpaces(rawEmail);
            string phone = CollapseSpaces(rawPhone);
            string address = CollapseSpaces(rawAddr);

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

            if (!string.IsNullOrWhiteSpace(email))
            {
                if (email.Length > 255)
                    errors.Add("Email exceeds 255 characters.");
                else if (!EmailRegex.IsMatch(email))
                    errors.Add("Email is not valid.");
            }

            if (!string.IsNullOrWhiteSpace(phone) && phone.Length > 255)
                errors.Add("Phone exceeds 255 characters.");

            if (!string.IsNullOrWhiteSpace(address) && address.Length > 255)
                errors.Add("Address exceeds 255 characters.");

            if (openingBalance < 0m)
                errors.Add("Opening balance cannot be negative.");

            if (!string.IsNullOrWhiteSpace(email))
            {
                if (seenEmails.TryGetValue(email, out int firstRow))
                    warnings.Add($"Duplicate email in file (first seen at row {firstRow}) — last occurrence will be used.");
                else
                    seenEmails[email] = rowNum;
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                string npKey = $"{name.ToLowerInvariant()}|{phone.ToLowerInvariant()}";
                if (seenNamePhones.TryGetValue(npKey, out int firstRow))
                    warnings.Add($"Duplicate (name+phone) in file (first seen at row {firstRow}) — last occurrence will be used.");
                else
                    seenNamePhones[npKey] = rowNum;
            }

            rows.Add(new CustomerCsvSampleRow
            {
                Row = rowNum,
                Name = name,
                Email = email,
                Phone = phone,
                Address = address,
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
