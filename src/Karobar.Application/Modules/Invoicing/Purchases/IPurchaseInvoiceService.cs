namespace Karobar.Application.Interfaces;

public sealed class PurchaseInvoiceFilterQuery
{
    public string? Search { get; set; }
    public string? SupplierSearch { get; set; }
    public long? SupplierId { get; set; }
    public string InvoiceType { get; set; } = "all"; // all | debit | credit
    public string Status { get; set; } = "all";      // all | posted | reversed
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class PurchaseInvoiceDirectoryRowDto
{
    public int RowIndex { get; set; }
    public long Id { get; set; }
    public string PostedNumber { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime PostedDate { get; set; }
    public DateTime? DueDate { get; set; }
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string InvoiceType { get; set; } = "debit"; // debit | credit
    public string Status { get; set; } = "posted";     // posted | reversed
    public decimal InvoiceAmount { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal RemainingAmount { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public int ItemsCount { get; set; }
    public long? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? ReversalReason { get; set; }
    public DateTime? ReversedAt { get; set; }

    public bool IsReversed => string.Equals(Status, "reversed", StringComparison.OrdinalIgnoreCase);
    public bool IsCredit => string.Equals(InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
    public string InvoiceTypeBadge => IsCredit ? "Credit" : "Debit";
    public string FormattedDate => PostedDate.ToString("dd-MM-yyyy");
    public string FormattedDisplayDate => PostedDate.ToString("d MMM yyyy");
}

public sealed class PurchaseInvoiceDirectoryPageDto
{
    public List<PurchaseInvoiceDirectoryRowDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize)));

    // KPI Summary Strip
    public int TotalPostedCount { get; set; }
    public decimal TotalPostedAmount { get; set; }
    public decimal TotalPaidAmount { get; set; }
    public decimal TotalRemainingPayable { get; set; }
    public int HeldDraftsCount { get; set; }
}

public sealed class PurchaseStoreSettingsDto
{
    public string StoreName { get; set; } = "Karobar App";
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string SaleSystem { get; set; } = "retail_wholesale"; // retail_wholesale | wholesale | retail
    public string ShopType { get; set; } = "pharmacy";           // pharmacy | general_store
    public string CurrencySymbol { get; set; } = "Rs.";
    public string PrinterType { get; set; } = "a4";              // thermal | a4
    public bool IsPharmacy => string.Equals(ShopType, "pharmacy", StringComparison.OrdinalIgnoreCase);
    public bool HasWholesale =>
        string.Equals(SaleSystem, "retail_wholesale", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(SaleSystem, "wholesale", StringComparison.OrdinalIgnoreCase);
}

public sealed class PurchaseSupplierLookupDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int CreditDays { get; set; } = 30;
    public decimal CreditLimit { get; set; }
    public decimal CurrentPayable { get; set; }
    public decimal CurrentPayableBalance => CurrentPayable;
    public int OverdueCount { get; set; }
    public decimal OverdueAmount { get; set; }
    public string ExpiryReturnPolicy { get; set; } = "before_expiry";
    public int ExpiryReturnMonths { get; set; } = 3;
    public int ExpiryReturnDays { get; set; } = 90;
    public string ExpiryPolicyLabel { get; set; } = "3 months before expiry";
    public List<long> SavedProductOrderIds { get; set; } = [];

    public bool HasAddress => !string.IsNullOrWhiteSpace(Address);
    public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);
    public string DisplayLabel => string.IsNullOrWhiteSpace(Phone) ? Name : $"{Name} ({Phone})";
}

public sealed class PurchaseAccountLookupDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string AccountType { get; set; } = "cash_drawer"; // cash_drawer | cash | bank | card | mobile_wallet | other
    public decimal CurrentBalance { get; set; }
    public bool IsDefault { get; set; }
    public bool SyncWithCashDrawer { get; set; }

    public string DisplayLabel => $"{Name} ({CurrentBalance:N2})";
}

public sealed class ReceivablePurchaseOrderLookupDto
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = "ordered";
    public int RemainingItemsCount { get; set; }
    public int RemainingUnits { get; set; }

    public string DisplayLabel => $"{OrderNumber} — {SupplierName} ({RemainingUnits} units pending)";
}

public sealed class PurchaseModalWindowLayoutDto
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public double Width { get; set; } = 960;
    public double Height { get; set; } = 600;
    public bool HasSavedPosition => X.HasValue && Y.HasValue;
}

public sealed class PurchaseFormBootstrapDto
{
    public string NextPostedNumber { get; set; } = "PRINV-0001";
    public PurchaseStoreSettingsDto StoreSettings { get; set; } = new();
    public List<PurchaseSupplierLookupDto> Suppliers { get; set; } = [];
    public List<PurchaseAccountLookupDto> Accounts { get; set; } = [];
    public List<ReceivablePurchaseOrderLookupDto> ReceivableOrders { get; set; } = [];
    public bool HasOpenCashDrawer { get; set; }
    public DateTime? OpenCashDrawerOpenedAt { get; set; }
    public decimal OpenCashDrawerExpectedBalance { get; set; }
    public int HeldDraftsCount { get; set; }
    public PurchaseModalWindowLayoutDto ProductSearchModalLayout { get; set; } = new() { Width = 960, Height = 600 };
    public PurchaseModalWindowLayoutDto QuickAddProductModalLayout { get; set; } = new() { Width = 800, Height = 600 };
}

public sealed class PurchaseProductBatchOptionDto
{
    public long Id { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal WholeSaleUnitPrice { get; set; }
}

public sealed class PurchaseProductSearchItemDto
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
    public long? StorageLocationId { get; set; }
    public int PackSize { get; set; } = 1;
    public int Quantity { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal WholeSalePackPrice { get; set; }
    public decimal WholeSaleUnitPrice { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal? Margin { get; set; }
    public decimal? WholeSaleMargin { get; set; }
    public decimal? LastSupplierUnitCost { get; set; }
    public bool IsLowStock => Quantity < Math.Max(1, PackSize);
    public bool HasBrand => !string.IsNullOrWhiteSpace(BrandName);
    public bool HasFormulation => !string.IsNullOrWhiteSpace(Formulation);
    public bool HasSupplier => !string.IsNullOrWhiteSpace(SupplierName);
    public bool HasRack => !string.IsNullOrWhiteSpace(Rack);
    public string SupplierBulletText => HasSupplier ? $"• {SupplierName}" : string.Empty;
    public string RackBadgeText => HasRack ? $"Rack: {Rack}" : string.Empty;
    public string StockTrendArrow => IsLowStock ? "▼" : "▲";
    public string MarginBadgeText => Margin.HasValue ? $"{Margin.Value:0.##}%" : "—";
    public string FormattedPackPurchasePrice => PackPurchasePrice.ToString("#,##0.###");
    public string FormattedUnitPurchasePrice => UnitPurchasePrice.ToString("#,##0.###");
    public string FormattedPackSalePrice => PackSalePrice.ToString("#,##0.###");
    public string FormattedUnitSalePrice => UnitSalePrice.ToString("#,##0.###");
    public string FormattedAvgPrice => AvgPrice.ToString("#,##0.###");
    public List<PurchaseProductBatchOptionDto> Batches { get; set; } = [];
}

public sealed class PreparedPurchaseOrderReceiptDto
{
    public long PurchaseOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string ReceiptKey { get; set; } = string.Empty;
    public int PriorDeliveriesCount { get; set; }
    public List<PurchaseInvoiceLineItemDto> Lines { get; set; } = [];
}

public sealed class PurchaseInvoiceLineItemDto
{
    public long? Id { get; set; }
    public long? PurchaseOrderItemId { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public string Rack { get; set; } = string.Empty;
    public long? StorageLocationId { get; set; }
    public int PackSize { get; set; } = 1;
    public string Batch { get; set; } = string.Empty;
    public DateTime? Expiry { get; set; }
    public decimal PackQuantity { get; set; }
    public int UnitQuantity { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal ItemDiscountPercentage { get; set; }
    public decimal PackBonus { get; set; }
    public int UnitBonus { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal WholeSalePackPrice { get; set; }
    public decimal WholeSaleUnitPrice { get; set; }
    public decimal? Margin { get; set; }
    public decimal? WholeSaleMargin { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal SubTotal { get; set; }
    public int Quantity { get; set; }
    public int ReturnableQuantity { get; set; }
    public int LineOrder { get; set; }
    public List<PurchaseProductBatchOptionDto> AvailableBatches { get; set; } = [];
}

public sealed class PurchaseDocumentHistoryEntryDto
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

public sealed class PurchaseInvoiceDetailDto
{
    public long Id { get; set; }
    public string PostedNumber { get; set; } = string.Empty;
    public DateTime PostedDate { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public int? CreditDays { get; set; }
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierPhone { get; set; } = string.Empty;
    public string SupplierExpiryPolicyLabel { get; set; } = string.Empty;
    public long? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? PurchaseOrderStatus { get; set; }
    public string? ReceiptKey { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string InvoiceType { get; set; } = "debit";
    public string Status { get; set; } = "posted";
    public DateTime? ReversedAt { get; set; }
    public string? ReversedByName { get; set; }
    public string? ReversalReason { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public decimal InvoiceAmount { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal AllocatedPaymentsAmount { get; set; }
    public decimal DirectPaidAmount => Math.Max(0m, Math.Round(TotalPaid - AllocatedPaymentsAmount, 2));
    public decimal RemainingAmount => Math.Max(0m, Math.Round(TotalAmount - TotalPaid, 2));
    public decimal LedgerCreditAmount =>
        string.Equals(InvoiceType, "credit", StringComparison.OrdinalIgnoreCase) && TotalPaid > TotalAmount
            ? Math.Round(TotalPaid - TotalAmount, 2)
            : 0m;
    public decimal BillDifference => Math.Round(InvoiceAmount - TotalAmount, 2);
    public string PaymentMethod { get; set; } = "cash";
    public long? AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public List<PurchaseInvoiceLineItemDto> Items { get; set; } = [];
    public List<PurchaseDocumentHistoryEntryDto> Histories { get; set; } = [];
}

public sealed class PurchaseInvoiceUpsertLineRequest
{
    public long? PurchaseOrderItemId { get; set; }
    public long ProductId { get; set; }
    public long? StorageLocationId { get; set; }
    public int PackSize { get; set; } = 1;
    public string? Batch { get; set; }
    public DateTime? Expiry { get; set; }
    public decimal PackQuantity { get; set; }
    public int UnitQuantity { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal ItemDiscountPercentage { get; set; }
    public decimal PackBonus { get; set; }
    public int UnitBonus { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal WholeSalePackPrice { get; set; }
    public decimal WholeSaleUnitPrice { get; set; }
    public decimal? Margin { get; set; }
    public decimal? WholeSaleMargin { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal SubTotal { get; set; }
    public int Quantity { get; set; }
}

public sealed class PurchaseInvoiceUpsertRequest
{
    public long? DraftId { get; set; }
    public long? PurchaseOrderId { get; set; }
    public string? ReceiptKey { get; set; }
    public long SupplierId { get; set; }
    public string InvoiceType { get; set; } = "debit"; // debit | credit
    public string PaymentMethod { get; set; } = "cash";
    public long? AccountId { get; set; }
    public DateTime PostedDate { get; set; } = DateTime.Today;
    public DateTime? DueDate { get; set; }
    public string? Remarks { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal InvoiceAmount { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal? TotalPaid { get; set; }
    public string? EditReason { get; set; }
    public bool? UpdatePreShiftDrawer { get; set; }
    public List<PurchaseInvoiceUpsertLineRequest> Items { get; set; } = [];
}

public sealed class PurchaseInvoiceOperationResult
{
    public bool Succeeded { get; set; }
    public bool RequiresPreShiftDrawerChoice { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> ValidationErrors { get; set; } = [];
    public int? OffendingRowIndex { get; set; }
    public string? OffendingFieldName { get; set; }
    public long? InvoiceId { get; set; }
    public string? PostedNumber { get; set; }

    public static PurchaseInvoiceOperationResult Fail(
        string message,
        List<string>? errors = null,
        int? rowIndex = null,
        string? fieldName = null) => new()
    {
        Succeeded = false,
        Message = message,
        ValidationErrors = errors is { Count: > 0 } ? errors : [message],
        OffendingRowIndex = rowIndex,
        OffendingFieldName = fieldName
    };
}

public sealed class HeldPurchaseDraftSummaryDto
{
    public long Id { get; set; }
    public string DraftNumber { get; set; } = string.Empty;
    public long? SupplierId { get; set; }
    public string SupplierName { get; set; } = "Unselected Supplier";
    public string Title { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal TotalAmount { get; set; }
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public string FormattedUpdatedAt => UpdatedAt.ToLocalTime().ToString("dd-MM-yyyy HH:mm");
}

public sealed class HeldPurchaseDraftDetailDto
{
    public long Id { get; set; }
    public string DraftNumber { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public PurchaseInvoiceUpsertRequest FormState { get; set; } = new();
}

public interface IPurchaseInvoiceService
{
    Task<PurchaseFormBootstrapDto> GetFormBootstrapAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateNextPostedNumberAsync(CancellationToken cancellationToken = default);
    Task<PurchaseInvoiceDirectoryPageDto> GetDirectoryPageAsync(PurchaseInvoiceFilterQuery query, CancellationToken cancellationToken = default);
    Task<PurchaseSupplierLookupDto?> GetSupplierCreditSummaryAsync(long supplierId, CancellationToken cancellationToken = default);
    Task<List<PurchaseProductSearchItemDto>> SearchProductsForPurchaseAsync(
        string? search = null,
        long? supplierId = null,
        int limit = 200,
        CancellationToken cancellationToken = default);
    Task<PurchaseProductSearchItemDto?> ScanProductByCodeOrBarcodeAsync(
        string codeOrBarcode,
        long? supplierId = null,
        CancellationToken cancellationToken = default);
    Task<PreparedPurchaseOrderReceiptDto> PreparePurchaseOrderReceiptAsync(
        long purchaseOrderId,
        CancellationToken cancellationToken = default);
    Task<bool> IsInvoiceNumberUniqueAsync(
        string invoiceNumber,
        long? excludeInvoiceId = null,
        CancellationToken cancellationToken = default);
    Task<PurchaseInvoiceDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<List<PurchaseDocumentHistoryEntryDto>> GetDocumentHistoryAsync(long id, CancellationToken cancellationToken = default);

    Task<PurchaseInvoiceOperationResult> CreateAsync(
        PurchaseInvoiceUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<PurchaseInvoiceOperationResult> UpdateAsync(
        long id,
        PurchaseInvoiceUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<PurchaseInvoiceOperationResult> ReverseAsync(
        long id,
        string reason,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<PurchaseInvoiceOperationResult> DeleteAsync(
        long id,
        string userPassword,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    // Held Drafts
    Task<HeldPurchaseDraftSummaryDto> SaveDraftAsync(
        long? existingDraftId,
        PurchaseInvoiceUpsertRequest state,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);
    Task<List<HeldPurchaseDraftSummaryDto>> GetHeldDraftsAsync(CancellationToken cancellationToken = default);
    Task<HeldPurchaseDraftDetailDto?> GetDraftByIdAsync(long draftId, CancellationToken cancellationToken = default);
    Task<bool> DeleteDraftAsync(long draftId, CancellationToken cancellationToken = default);

    // User Modal Window Size & Placement Preferences (ProductSearchInput.jsx / ProductFormModal.jsx parity)
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
