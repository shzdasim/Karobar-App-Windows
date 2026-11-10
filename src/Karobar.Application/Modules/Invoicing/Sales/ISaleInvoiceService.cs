namespace Karobar.Application.Interfaces;

public sealed class SaleInvoiceFilterQuery
{
    public string? Search { get; set; }
    public string? CustomerSearch { get; set; }
    public long? CustomerId { get; set; }
    public string SaleType { get; set; } = "all";    // all | retail | wholesale
    public string InvoiceType { get; set; } = "all"; // all | debit | credit
    public string Status { get; set; } = "all";      // all | posted | reversed
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class SaleInvoiceDirectoryRowDto
{
    public int RowIndex { get; set; }
    public long Id { get; set; }
    public string PostedNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime? DueDate { get; set; }
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string SaleType { get; set; } = "retail";       // retail | wholesale
    public string WholesaleType { get; set; } = "unit";    // unit | pack
    public string InvoiceType { get; set; } = "debit";     // debit | credit
    public string Status { get; set; } = "posted";         // posted | reversed
    public decimal GrossAmount { get; set; }
    public decimal ItemDiscount { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal TotalReceive { get; set; }
    public decimal RemainingAmount { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public int ItemsCount { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal EstimatedProfit { get; set; }
    public decimal EstimatedMarginPct { get; set; }
    public string? ReversalReason { get; set; }
    public DateTime? ReversedAt { get; set; }

    public bool IsReversed => string.Equals(Status, "reversed", StringComparison.OrdinalIgnoreCase);
    public bool IsWholesale => string.Equals(SaleType, "wholesale", StringComparison.OrdinalIgnoreCase);
    public bool IsCredit => string.Equals(InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
    public string SaleTypeBadge => IsWholesale ? "Wholesale" : "Retail";
    public string InvoiceTypeBadge => IsCredit ? "Credit" : "Debit";
    public string FormattedDate => Date.ToString("dd-MM-yyyy");
    public string FormattedDisplayDate => Date.ToString("d MMM yyyy");
    public bool HasRemaining => RemainingAmount > 0.009m && !IsReversed;
}

public sealed class SaleInvoiceDirectoryPageDto
{
    public List<SaleInvoiceDirectoryRowDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize)));

    // KPI Summary Strip
    public int TotalPostedCount { get; set; }
    public int RetailCount { get; set; }
    public int WholesaleCount { get; set; }
    public decimal TotalPostedAmount { get; set; }
    public decimal TotalReceivedAmount { get; set; }
    public decimal TotalRemainingReceivable { get; set; }
    public decimal TotalEstimatedProfit { get; set; }
    public int HeldDraftsCount { get; set; }
}

public sealed class SaleStoreSettingsDto
{
    public string StoreName { get; set; } = "Karobar App";
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string SaleSystem { get; set; } = "retail_wholesale"; // retail_wholesale | wholesale | retail
    public string ShopType { get; set; } = "pharmacy";           // pharmacy | general_store
    public string CurrencySymbol { get; set; } = "Rs.";
    public string PrinterType { get; set; } = "thermal";         // thermal | a4
    public string ThermalTemplate { get; set; } = "standard";
    public string A4Template { get; set; } = "standard";
    public string FooterNote { get; set; } = "Thank you for your business!";

    public bool IsPharmacy => string.Equals(ShopType, "pharmacy", StringComparison.OrdinalIgnoreCase);
    public bool HasWholesale =>
        string.Equals(SaleSystem, "retail_wholesale", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(SaleSystem, "wholesale", StringComparison.OrdinalIgnoreCase);
    public bool IsRetailOnly => string.Equals(SaleSystem, "retail", StringComparison.OrdinalIgnoreCase);
}

public sealed class SaleCustomerLookupDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CustomerType { get; set; } = "retail"; // retail | wholesale | both
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public int CreditDays { get; set; } = 30;
    public decimal CreditLimit { get; set; }
    public string CreditStatus { get; set; } = "active"; // active | on_hold | blocked
    public decimal DiscountPercentage { get; set; }
    public decimal CurrentBalance { get; set; }          // > 0 means customer owes us; < 0 means customer has advance/credit
    public decimal AvailableCreditToApply { get; set; }  // Unapplied payments + unapplied credit sale returns
    public bool IsWalkIn { get; set; }
    public Dictionary<long, decimal> CustomWholesalePrices { get; set; } = []; // ProductId -> PackPrice

    public int CustomPricesCount => CustomWholesalePrices.Count;
    public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);
    public bool HasAddress => !string.IsNullOrWhiteSpace(Address);
    public bool HasCustomPrices => CustomWholesalePrices.Count > 0;
    public bool HasDefaultDiscount => DiscountPercentage > 0m;
    public bool IsBlocked => string.Equals(CreditStatus, "blocked", StringComparison.OrdinalIgnoreCase);
    public bool IsOnHold => string.Equals(CreditStatus, "on_hold", StringComparison.OrdinalIgnoreCase);
    public string DisplayLabel => string.IsNullOrWhiteSpace(Phone) ? Name : $"{Name} ({Phone})";
    public string BalanceSummaryText => IsWalkIn
        ? "Walk-in (Cash Counter)"
        : CurrentBalance > 0.009m
            ? $"Due: Rs. {CurrentBalance:N0}"
            : CurrentBalance < -0.009m
                ? $"Credit: Rs. {Math.Abs(CurrentBalance):N0}"
                : "Rs. 0";
}

public sealed class SaleAccountLookupDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string AccountType { get; set; } = "cash_drawer";
    public decimal CurrentBalance { get; set; }
    public bool IsDefault { get; set; }
    public bool SyncWithCashDrawer { get; set; }

    public string DisplayLabel => $"{Name} ({CurrentBalance:N0})";
}

public sealed class SaleFormBootstrapDto
{
    public string NextPostedNumber { get; set; } = "SI-000001";
    public SaleStoreSettingsDto StoreSettings { get; set; } = new();
    public List<SaleCustomerLookupDto> Customers { get; set; } = [];
    public long WalkInCustomerId { get; set; }
    public List<SaleAccountLookupDto> Accounts { get; set; } = [];
    public bool HasOpenCashDrawer { get; set; }
    public DateTime? OpenCashDrawerOpenedAt { get; set; }
    public decimal OpenCashDrawerExpectedBalance { get; set; }
    public int HeldDraftsCount { get; set; }
    public decimal UserMaxDiscountPercentage { get; set; } = 100m;
    public PurchaseModalWindowLayoutDto ProductSearchModalLayout { get; set; } = new() { Width = 960, Height = 600 };
    public PurchaseModalWindowLayoutDto QuickAddProductModalLayout { get; set; } = new() { Width = 800, Height = 600 };
}

public sealed class SaleProductBatchOptionDto
{
    public long Id { get; set; }
    public bool IsUnbatched => Id <= 0 || string.Equals(BatchNumber, "Unbatched (Stock)", StringComparison.OrdinalIgnoreCase);
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public int Quantity { get; set; }
    public int PackSize { get; set; } = 1;
    public string Rack { get; set; } = string.Empty;
    public decimal UnitPurchasePrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal WholeSalePackPrice { get; set; }
    public decimal WholeSaleUnitPrice { get; set; }

    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value.Date < DateTime.Today;
    public bool IsNearExpiry => ExpiryDate.HasValue && !IsExpired && ExpiryDate.Value.Date <= DateTime.Today.AddDays(90);
    public string FormattedExpiry => ExpiryDate.HasValue ? ExpiryDate.Value.ToString("MM/yyyy") : "—";
    public string FormattedFullExpiry => ExpiryDate.HasValue ? ExpiryDate.Value.ToString("dd-MM-yyyy") : "No Expiry";
    public string StatusBadgeText => IsExpired ? "EXPIRED" : IsNearExpiry ? "Expiring Soon" : "Valid";
    public string AvailPackUnitBadge => FormatPackUnitBadge(Quantity, PackSize);

    public static string FormatPackUnitBadge(int units, int packSize)
    {
        var ps = Math.Max(1, packSize);
        if (ps <= 1)
            return $"{units}U";
        int packs = units / ps;
        int remUnits = units % ps;
        if (packs > 0 && remUnits > 0)
            return $"{packs}P+{remUnits}U ({units})";
        if (packs > 0)
            return $"{packs}P ({units})";
        return $"{remUnits}U";
    }
}

public sealed class SaleProductSearchItemDto
{
    public long Id { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Formulation { get; set; } = string.Empty;
    public long? BrandId { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string Rack { get; set; } = string.Empty;
    public int PackSize { get; set; } = 1;
    public int Quantity { get; set; }
    public int UnbatchedQuantity { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal WholeSalePackPrice { get; set; }
    public decimal WholeSaleUnitPrice { get; set; }
    public decimal MaxDiscount { get; set; }
    public decimal? Margin { get; set; }
    public decimal? WholeSaleMargin { get; set; }
    public string BatchEnabled { get; set; } = "yes";
    public bool IsCustomCustomerPrice { get; set; }
    public List<SaleProductBatchOptionDto> Batches { get; set; } = [];

    public bool IsOutOfStock => Quantity <= 0;
    public bool IsLowStock => Quantity > 0 && Quantity < Math.Max(1, PackSize);
    public bool HasBrand => !string.IsNullOrWhiteSpace(BrandName);
    public bool HasFormulation => !string.IsNullOrWhiteSpace(Formulation);
    public bool HasSupplier => !string.IsNullOrWhiteSpace(SupplierName);
    public bool HasRack => !string.IsNullOrWhiteSpace(Rack);
    public string SupplierBulletText => HasSupplier ? $"• {SupplierName}" : string.Empty;
    public string RackBadgeText => HasRack ? $"Rack: {Rack}" : string.Empty;
    public string StockTrendArrow => IsOutOfStock ? "✕" : IsLowStock ? "▼" : "▲";
    public string StockBadgeText => IsOutOfStock ? $"Out ({Quantity})" : $"In Stock: {Quantity}";
    public string AvailPackUnitText => SaleProductBatchOptionDto.FormatPackUnitBadge(Quantity, PackSize);
    public string MarginBadgeText => Margin.HasValue ? $"{Margin.Value:0.##}%" : "—";
    public string FormattedPackPurchasePrice => PackPurchasePrice.ToString("#,##0.##");
    public string FormattedUnitPurchasePrice => UnitPurchasePrice.ToString("#,##0.##");
    public string FormattedPackSalePrice => PackSalePrice.ToString("#,##0.##");
    public string FormattedUnitSalePrice => UnitSalePrice.ToString("#,##0.##");
    public string FormattedWholeSalePackPrice => WholeSalePackPrice.ToString("#,##0.##");
    public string FormattedWholeSaleUnitPrice => WholeSaleUnitPrice.ToString("#,##0.##");
    public string FormattedAvgPrice => AvgPrice.ToString("#,##0.##");
}

public sealed class SaleInvoiceLineItemDto
{
    public long? Id { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Formulation { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public string Rack { get; set; } = string.Empty;
    public int PackSize { get; set; } = 1;
    public long? BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? Expiry { get; set; }
    public int CurrentQuantity { get; set; }
    public decimal PackQuantity { get; set; }
    public int UnitQuantity { get; set; }
    public int Quantity { get; set; }
    public decimal PackBonus { get; set; }
    public int UnitBonus { get; set; }
    public int Bonus { get; set; }
    public decimal PackPrice { get; set; }
    public decimal Price { get; set; }
    public decimal? CostPriceSnapshot { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal CatalogUnitPrice { get; set; }
    public decimal ItemDiscountPercentage { get; set; }
    public decimal MaxDiscountPercentage { get; set; }
    public decimal? Margin { get; set; }
    public decimal EstimatedRowCost { get; set; }
    public decimal EstimatedRowProfit { get; set; }
    public decimal SubTotal { get; set; }
    public string SaleType { get; set; } = "retail";
    public bool IsCustomPrice { get; set; }
    public List<SaleProductBatchOptionDto> AvailableBatches { get; set; } = [];

    public string FormattedExpiry => Expiry.HasValue ? Expiry.Value.ToString("MM/yyyy") : "—";
    public string TotalUnitsDisplay => Bonus > 0 ? $"{Quantity}+{Bonus}" : $"{Quantity}";
    public string FormattedMargin => Margin.HasValue ? $"{Margin.Value:0.0}%" : "0.0%";
}

public sealed class SaleSplitPaymentLineDto
{
    public string PaymentMethod { get; set; } = "cash";
    public long? AccountId { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
}

public sealed class SaleDocumentHistoryEntryDto
{
    public long Id { get; set; }
    public string Action { get; set; } = string.Empty; // created | updated | reversed
    public string ActionBadge => Action.ToLowerInvariant() switch
    {
        "created" => "Created",
        "updated" => "Edited",
        "reversed" => "Reversed (Voided)",
        _ => Action
    };
    public string UserName { get; set; } = "System";
    public DateTime CreatedAt { get; set; }
    public string FormattedTimestamp => CreatedAt.ToLocalTime().ToString("dd-MM-yyyy HH:mm");
    public string Reason { get; set; } = string.Empty;
    public string ChangesSummary { get; set; } = string.Empty;
}

public sealed class SaleInvoiceDetailDto
{
    public long Id { get; set; }
    public string PostedNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public int? CreditDays { get; set; }
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public bool IsWalkInCustomer { get; set; }
    public string CreatedByName { get; set; } = "Admin User";
    public string DoctorName { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public string SaleType { get; set; } = "retail";       // retail | wholesale
    public string WholesaleType { get; set; } = "unit";    // unit | pack
    public string InvoiceType { get; set; } = "debit";     // debit | credit
    public string Status { get; set; } = "posted";         // posted | reversed
    public DateTime? ReversedAt { get; set; }
    public string? ReversedByName { get; set; }
    public string? ReversalReason { get; set; }

    public decimal GrossAmount { get; set; }
    public decimal ItemDiscount { get; set; }
    public decimal RowsNetSum => Math.Round(GrossAmount - ItemDiscount, 2);
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal TotalReceive { get; set; }
    public decimal RemainThisBill { get; set; }
    public decimal PreviousCustomerBalance { get; set; }
    public decimal TotalCustomerBalance { get; set; }

    public string PaymentMethod { get; set; } = "cash";
    public long? AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;

    public decimal TotalEstimatedCost { get; set; }
    public decimal TotalEstimatedProfit { get; set; }
    public decimal OverallMarginPct { get; set; }

    public bool HasLinkedReturns { get; set; }
    public bool IsPreShiftInvoice { get; set; }
    public bool IsWholesale => string.Equals(SaleType, "wholesale", StringComparison.OrdinalIgnoreCase);
    public bool IsCredit => string.Equals(InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
    public bool IsReversed => string.Equals(Status, "reversed", StringComparison.OrdinalIgnoreCase);
    public bool HasDoctorOrPatient => !string.IsNullOrWhiteSpace(DoctorName) || !string.IsNullOrWhiteSpace(PatientName);
    public bool HasRemarks => !string.IsNullOrWhiteSpace(Remarks);
    public string FormattedDate => Date.ToString("dd-MM-yyyy");
    public string FormattedDueDate => DueDate.HasValue ? DueDate.Value.ToString("dd-MM-yyyy") : "—";
    public string DocumentTitle => IsWholesale ? "Wholesale Sale Invoice" : "Retail Sale Invoice";

    public List<SaleInvoiceLineItemDto> Items { get; set; } = [];
    public List<SaleDocumentHistoryEntryDto> Histories { get; set; } = [];
}

public sealed class SaleInvoiceUpsertRequest
{
    public long? DraftId { get; set; }
    public string? PostedNumber { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime? DueDate { get; set; }
    public long CustomerId { get; set; }
    public string? OneTimeCustomerName { get; set; }
    public string? OneTimeCustomerPhone { get; set; }
    public long? SalesmanId { get; set; }
    public string? DoctorName { get; set; }
    public string? PatientName { get; set; }
    public string? Remarks { get; set; }

    public string SaleType { get; set; } = "retail";       // retail | wholesale
    public string WholesaleType { get; set; } = "unit";    // unit | pack
    public string InvoiceType { get; set; } = "debit";     // debit | credit

    public decimal GrossAmount { get; set; }
    public decimal ItemDiscount { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal TotalReceive { get; set; }
    public decimal CashGiven { get; set; }
    public decimal ApplyCreditAmount { get; set; }

    public string PaymentMethod { get; set; } = "cash";
    public long? AccountId { get; set; }
    public string? PaymentReference { get; set; }
    public List<SaleSplitPaymentLineDto> SplitPayments { get; set; } = [];

    // Supervisor Overrides
    public bool DiscountOverrideConfirmed { get; set; }
    public string? DiscountApproverUsername { get; set; }
    public string? DiscountApproverPassword { get; set; }
    public string? DiscountOverrideReason { get; set; }

    public bool CreditOverrideConfirmed { get; set; }
    public string? CreditApproverUsername { get; set; }
    public string? CreditApproverPassword { get; set; }
    public string? CreditOverrideReason { get; set; }

    public bool ExpiredOverrideConfirmed { get; set; }
    public string? ExpiredApproverUsername { get; set; }
    public string? ExpiredApproverPassword { get; set; }
    public string? ExpiredOverrideReason { get; set; }

    public bool? UpdatePreShiftDrawer { get; set; }
    public string? EditReason { get; set; }

    public List<SaleInvoiceLineItemDto> Items { get; set; } = [];
}

public sealed class SaleInvoiceOperationResult
{
    public bool Succeeded { get; set; }
    public long? InvoiceId { get; set; }
    public string? PostedNumber { get; set; }
    public string Message { get; set; } = string.Empty;

    // Structured override / prompt flags for UI modals
    public bool RequiresDiscountOverride { get; set; }
    public string? DiscountViolationDetails { get; set; }

    public bool RequiresCreditOverride { get; set; }
    public string? CreditViolationDetails { get; set; }
    public decimal CustomerCurrentBalance { get; set; }
    public decimal NewBillCreditAmount { get; set; }
    public decimal ProjectedCustomerBalance { get; set; }
    public decimal CustomerCreditLimit { get; set; }

    public bool RequiresExpiredOverride { get; set; }
    public string? ExpiredBatchDetails { get; set; }

    public bool RequiresPreShiftDrawerChoice { get; set; }
    public decimal PreShiftCashDelta { get; set; }

    public int? OffendingRowIndex { get; set; }
    public string? OffendingField { get; set; }

    public static SaleInvoiceOperationResult Fail(
        string message,
        int? offendingRow = null,
        string? offendingField = null) => new()
    {
        Succeeded = false,
        Message = message,
        OffendingRowIndex = offendingRow,
        OffendingField = offendingField
    };
}

public sealed class HeldSaleDraftSummaryDto
{
    public long Id { get; set; }
    public string DraftNumber { get; set; } = string.Empty;
    public string InvoiceKind { get; set; } = "sale_retail"; // sale_retail | sale_wholesale
    public string SaleType => string.Equals(InvoiceKind, "sale_wholesale", StringComparison.OrdinalIgnoreCase) ? "wholesale" : "retail";
    public string SaleTypeBadge => string.Equals(SaleType, "wholesale", StringComparison.OrdinalIgnoreCase) ? "Wholesale" : "Retail";
    public long? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal TotalAmount { get; set; }
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public string ItemsPreview { get; set; } = string.Empty;
    public string FormattedUpdatedAt => UpdatedAt.ToLocalTime().ToString("dd-MM-yyyy HH:mm");
}

public sealed class HeldSaleDraftDetailDto
{
    public long Id { get; set; }
    public string DraftNumber { get; set; } = string.Empty;
    public string InvoiceKind { get; set; } = "sale_retail";
    public int Version { get; set; } = 1;
    public SaleInvoiceUpsertRequest FormState { get; set; } = new();
}

public interface ISaleInvoiceService
{
    Task<SaleFormBootstrapDto> GetFormBootstrapAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateNextPostedNumberAsync(CancellationToken cancellationToken = default);
    Task<SaleInvoiceDirectoryPageDto> GetDirectoryPageAsync(SaleInvoiceFilterQuery query, CancellationToken cancellationToken = default);
    Task<SaleCustomerLookupDto?> GetCustomerSummaryAsync(long customerId, CancellationToken cancellationToken = default);

    Task<SaleCustomerLookupDto> QuickCreateCustomerAsync(
        string name,
        string? phone,
        string customerType = "retail",
        CancellationToken cancellationToken = default);

    Task<SaleAccountLookupDto> QuickCreatePaymentAccountAsync(
        string name,
        string accountType = "cash",
        string? accountNumber = null,
        CancellationToken cancellationToken = default);

    Task<bool> OpenCashDrawerSessionAsync(
        decimal openingBalance,
        string? openingNote = null,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<List<SaleProductSearchItemDto>> SearchProductsForSaleAsync(
        string? search = null,
        long? customerId = null,
        int limit = 200,
        CancellationToken cancellationToken = default);

    Task<SaleProductSearchItemDto?> ScanProductByCodeOrBarcodeAsync(
        string codeOrBarcode,
        long? customerId = null,
        CancellationToken cancellationToken = default);

    Task<List<SaleProductSearchItemDto>> GetSubstituteProductsAsync(
        long productId,
        long? customerId = null,
        CancellationToken cancellationToken = default);

    Task<List<SaleProductBatchOptionDto>> GetProductBatchesForSaleAsync(
        long productId,
        CancellationToken cancellationToken = default);

    Task<SaleInvoiceDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<SaleInvoiceOperationResult> CreateAsync(
        SaleInvoiceUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<SaleInvoiceOperationResult> UpdateAsync(
        long id,
        SaleInvoiceUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<SaleInvoiceOperationResult> ReverseAsync(
        long id,
        string reason,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<SaleInvoiceOperationResult> DeleteAsync(
        long id,
        string userPassword,
        string refundMode = "refund",
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string Message, long? ApproverUserId)> VerifySupervisorOverrideAsync(
        string usernameOrEmail,
        string password,
        string overrideType,
        string reason,
        CancellationToken cancellationToken = default);

    Task<HeldSaleDraftSummaryDto> SaveDraftAsync(
        long? existingDraftId,
        SaleInvoiceUpsertRequest state,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<List<HeldSaleDraftSummaryDto>> GetHeldDraftsAsync(
        string? saleTypeFilter = null,
        CancellationToken cancellationToken = default);

    Task<HeldSaleDraftDetailDto?> GetDraftByIdAsync(
        long draftId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteDraftAsync(long draftId, CancellationToken cancellationToken = default);
    Task<int> ClearAllDraftsAsync(string? saleTypeFilter = null, CancellationToken cancellationToken = default);

    Task<PurchaseModalWindowLayoutDto> GetModalWindowLayoutAsync(
        string modalKey = "productSearchModal",
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task SaveModalWindowLayoutAsync(
        string modalKey,
        PurchaseModalWindowLayoutDto layout,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);
}
