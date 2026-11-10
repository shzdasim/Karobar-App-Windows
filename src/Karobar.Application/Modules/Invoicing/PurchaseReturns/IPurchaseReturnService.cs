using Karobar.Domain.Formulas;

namespace Karobar.Application.Interfaces;

public sealed class PurchaseReturnDirectoryFilter
{
    public string? PostedNumber { get; set; }
    public string? SupplierSearch { get; set; }
    public long? SupplierId { get; set; }
    public string? SettlementMode { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class PurchaseReturnDirectoryRowDto
{
    public int RowIndex { get; set; }
    public long Id { get; set; }
    public string PostedNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string DateFormatted => Date.ToString("dd MMM yyyy");
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public long? PurchaseInvoiceId { get; set; }
    public string? PurchaseInvoicePostedNumber { get; set; }
    public string? PurchaseInvoiceNumber { get; set; }
    public string InvoiceDisplay => !string.IsNullOrWhiteSpace(PurchaseInvoicePostedNumber)
        ? PurchaseInvoicePostedNumber!
        : "Open Return";
    public string SettlementMode { get; set; } = "cash_refund";
    public string SettlementBadgeLabel => SettlementMode switch
    {
        "credit_note" => "Debit Note (Ledger)",
        "invoice_offset" => "Invoice Offset (Ledger)",
        _ => "Instant Refund"
    };
    public string PaymentMethod { get; set; } = "cash";
    public long? AccountId { get; set; }
    public string? AccountName { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal CreditedAmount { get; set; }
    public int ItemsCount { get; set; }
    public int TotalReturnUnits { get; set; }
    public string? Remarks { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public sealed class PurchaseReturnDirectoryPageDto
{
    public List<PurchaseReturnDirectoryRowDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));
    public int TotalReturnsCount { get; set; }
    public decimal TotalGrossAmount { get; set; }
    public decimal TotalNetAmount { get; set; }
    public decimal TotalCashRefundAmount { get; set; }
    public decimal TotalLedgerCreditAmount { get; set; }
}

public sealed class PurchaseReturnFormBootstrapDto
{
    public string NextPostedNumberPreview { get; set; } = "PRRET-0001";
    public List<PurchaseSupplierLookupDto> Suppliers { get; set; } = [];
    public List<PurchaseAccountLookupDto> Accounts { get; set; } = [];
    public List<PurchaseAccountLookupDto> PaymentAccounts => Accounts;
    public long? DefaultCashAccountId { get; set; }
    public bool HasOpenCashDrawerSession { get; set; }
    public DateTime? ActiveDrawerSessionOpenedAt { get; set; }
    public string CurrencySymbol { get; set; } = "Rs.";
    public string StoreName { get; set; } = "Karobar App";
    public string StoreAddress { get; set; } = string.Empty;
    public string StorePhone { get; set; } = string.Empty;
}

public sealed class PurchaseReturnInvoiceLookupDto
{
    public long Id { get; set; }
    public string PostedNumber { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime PostedDate { get; set; }
    public string PostedDateFormatted => PostedDate.ToString("dd MMM yyyy");
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string InvoiceType { get; set; } = "debit";
    public bool IsCredit => string.Equals(InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
    public string InvoiceTypeBadge => IsCredit ? "Credit" : "Cash/Debit";
    public decimal TotalAmount { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal RemainingAmount { get; set; }
    public string? Remarks { get; set; }
    public int ItemsCount { get; set; }
}

public sealed class PurchaseReturnInvoiceItemOptionDto
{
    public long InvoiceItemId { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Batch { get; set; }
    public DateTime? Expiry { get; set; }
    public string ExpiryFormatted => Expiry.HasValue ? Expiry.Value.ToString("MM/yyyy") : string.Empty;
    public bool IsUnbatched => string.IsNullOrWhiteSpace(Batch);
    public int PackSize { get; set; } = 1;
    public decimal PackPurchasedQuantity { get; set; }
    public int UnitPurchasedQuantity { get; set; }
    public int UnitBonus { get; set; }
    public int TotalPurchasedUnits { get; set; }
    public int AlreadyReturnedUnits { get; set; }
    public int ReturnableUnits { get; set; }
    public int PhysicalAvailableUnits { get; set; }
    public int EffectiveAvailableUnits { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal ItemDiscountPercentage { get; set; }
}

public sealed class PurchaseReturnProductSearchDto
{
    public long Id { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Formulation { get; set; }
    public string CategoryName { get; set; } = "Uncategorized";
    public string BrandName { get; set; } = "—";
    public string? Rack { get; set; }
    public int PackSize { get; set; } = 1;
    public int QuantityOnHand { get; set; }
    public int? ReturnableUnitsOnInvoice { get; set; }
    public int DisplayAvailableUnits => ReturnableUnitsOnInvoice ?? QuantityOnHand;
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal AvgPrice { get; set; }
    public int BatchesCount { get; set; }
}

public sealed class PurchaseReturnBatchOptionDto
{
    public long? BatchId { get; set; }
    public long ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public string ExpiryFormatted => ExpiryDate.HasValue ? ExpiryDate.Value.ToString("MM/yyyy") : "—";
    public bool IsUnbatched { get; set; }
    public int PackSize { get; set; } = 1;
    public int PhysicalAvailableUnits { get; set; }
    public int? InvoiceReturnableUnits { get; set; }
    public int AvailableUnits { get; set; }
    public decimal PackPurchasedQuantity { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal ItemDiscountPercentage { get; set; }
    public string DisplayBatchNumber => IsUnbatched ? "[Unassigned Stock]" : BatchNumber;
}

public sealed class PurchaseReturnLineItemDto
{
    public long Id { get; set; }
    public int RowNumber { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Batch { get; set; }
    public DateTime? Expiry { get; set; }
    public string ExpiryFormatted => Expiry.HasValue ? Expiry.Value.ToString("MM/yyyy") : "—";
    public bool IsUnbatched => string.IsNullOrWhiteSpace(Batch);
    public int PackSize { get; set; } = 1;
    public decimal PackPurchasedQuantity { get; set; }
    public int AvailableUnits { get; set; }
    public decimal ReturnPackQuantity { get; set; }
    public int ReturnUnitQuantity { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal ItemDiscountPercentage { get; set; }
    public decimal SubTotal { get; set; }
}

public sealed class PurchaseReturnDetailDto
{
    public long Id { get; set; }
    public string PostedNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string DateFormatted => Date.ToString("dd MMM yyyy");
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierPhone { get; set; }
    public string? SupplierAddress { get; set; }
    public long? PurchaseInvoiceId { get; set; }
    public string? PurchaseInvoicePostedNumber { get; set; }
    public string? PurchaseInvoiceNumber { get; set; }
    public string? PurchaseInvoiceType { get; set; }
    public string? Remarks { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string SettlementMode { get; set; } = "cash_refund";
    public string SettlementBadgeLabel => SettlementMode switch
    {
        "credit_note" => "Debit Note (Supplier Ledger)",
        "invoice_offset" => "Invoice Offset (Supplier Ledger)",
        _ => "Instant Refund"
    };
    public string PaymentMethod { get; set; } = "cash";
    public long? AccountId { get; set; }
    public string? AccountName { get; set; }
    public decimal CreditedAmount { get; set; }
    public string? VoucherNumber { get; set; }
    public decimal SupplierBalanceAfter { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<PurchaseReturnLineItemDto> Items { get; set; } = [];
    public int TotalItemsCount => Items.Count;
    public int TotalReturnUnits => Items.Sum(i => i.ReturnUnitQuantity);
    public decimal TotalReturnPacks => Math.Round(Items.Sum(i => i.ReturnPackQuantity), 2);
}

public sealed class PurchaseReturnLineInput
{
    public long ProductId { get; set; }
    public string? Batch { get; set; }
    public DateTime? Expiry { get; set; }
    public bool IsUnbatched { get; set; }
    public int PackSize { get; set; } = 1;
    public decimal PackPurchasedQuantity { get; set; }
    public int AvailableUnits { get; set; }
    public decimal ReturnPackQuantity { get; set; }
    public int ReturnUnitQuantity { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal ItemDiscountPercentage { get; set; }
    public decimal SubTotal { get; set; }
}

public sealed class PurchaseReturnUpsertRequest
{
    public long SupplierId { get; set; }
    public long? PurchaseInvoiceId { get; set; }
    public string? PostedNumber { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public string? Remarks { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string? SettlementMode { get; set; }
    public string? PaymentMethod { get; set; } = "cash";
    public long? AccountId { get; set; }
    public bool UpdateDrawerIfPreShift { get; set; } = true;
    public List<PurchaseReturnLineInput> Items { get; set; } = [];
}

public sealed class PurchaseReturnPreShiftCheckResult
{
    public bool RequiresPrompt { get; set; }
    public decimal OldCashAmount { get; set; }
    public decimal NewCashAmount { get; set; }
    public decimal CashDelta { get; set; }
    public DateTime? ReturnCreatedAt { get; set; }
    public DateTime? ShiftOpenedAt { get; set; }
}

public sealed class DocumentHistoryEntryDto
{
    public long Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string PostedNumber { get; set; } = string.Empty;
    public string UserName { get; set; } = "System";
    public string? Reason { get; set; }
    public string ChangesSummary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string FormattedTimestamp => CreatedAt.ToLocalTime().ToString("dd-MM-yyyy HH:mm");
}

public sealed class PurchaseReturnOperationResult
{
    public bool Succeeded { get; set; }
    public long? ReturnId { get; set; }
    public string? PostedNumber { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? RowIndex { get; set; }
    public string? FieldName { get; set; }

    public static PurchaseReturnOperationResult Fail(string message, int? rowIndex = null, string? fieldName = null) => new()
    {
        Succeeded = false,
        Message = message,
        RowIndex = rowIndex,
        FieldName = fieldName
    };
}

public interface IPurchaseReturnService
{
    Task<PurchaseReturnDirectoryPageDto> GetDirectoryPageAsync(
        PurchaseReturnDirectoryFilter filter,
        CancellationToken cancellationToken = default);

    Task<PurchaseReturnFormBootstrapDto> GetFormBootstrapAsync(
        CancellationToken cancellationToken = default);

    Task<string> GenerateNextPostedNumberAsync(
        CancellationToken cancellationToken = default);

    Task<PurchaseReturnDetailDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<List<PurchaseReturnInvoiceLookupDto>> SearchSupplierInvoicesAsync(
        long? supplierId = null,
        string? search = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<List<PurchaseReturnInvoiceItemOptionDto>> GetInvoiceReturnableItemsAsync(
        long purchaseInvoiceId,
        long? excludePurchaseReturnId = null,
        CancellationToken cancellationToken = default);

    Task<List<PurchaseReturnProductSearchDto>> SearchProductsForReturnAsync(
        string? search = null,
        long? supplierId = null,
        long? purchaseInvoiceId = null,
        long? excludePurchaseReturnId = null,
        int limit = 200,
        CancellationToken cancellationToken = default);

    Task<List<PurchaseReturnBatchOptionDto>> GetProductBatchesForReturnAsync(
        long productId,
        long? purchaseInvoiceId = null,
        long? excludePurchaseReturnId = null,
        CancellationToken cancellationToken = default);

    Task<PurchaseReturnPreShiftCheckResult> CheckPreShiftCashEditImpactAsync(
        long returnId,
        string? newSettlementMode,
        string? newPaymentMethod,
        decimal newTotal,
        CancellationToken cancellationToken = default);

    Task<PurchaseReturnOperationResult> CreateAsync(
        PurchaseReturnUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<PurchaseReturnOperationResult> UpdateAsync(
        long id,
        PurchaseReturnUpsertRequest request,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<PurchaseReturnOperationResult> DeleteAsync(
        long id,
        string userPassword,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<List<DocumentHistoryEntryDto>> GetDocumentHistoryAsync(
        long returnId,
        CancellationToken cancellationToken = default);
}
