namespace Karobar.Application.Interfaces;

public interface IProductService
{
    Task<ProductListResponse> GetProductsAsync(
        ProductQueryFilter filter,
        CancellationToken cancellationToken = default);

    Task<ProductFormLookupsDto> GetProductFormLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<GeneratedProductCodesDto> GenerateNewCodeAndBarcodeAsync(
        CancellationToken cancellationToken = default);

    Task<ProductDetailDto?> GetProductByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ProductResult> CreateProductAsync(
        ProductUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductResult> UpdateProductAsync(
        long id,
        ProductUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductResult> DeleteProductAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<BulkDeleteProductsResult> BulkDeleteProductsAsync(
        IReadOnlyCollection<long> productIds,
        CancellationToken cancellationToken = default);

    Task<ProductResult> BulkUpdateProductMetaAsync(
        BulkUpdateProductMetaRequest request,
        CancellationToken cancellationToken = default);

    Task<FormulationOptionDto?> ResolveOrCreateFormulationAsync(
        string? rawName,
        CancellationToken cancellationToken = default);

    // Storage Locations
    Task<List<StorageLocationDto>> GetStorageLocationsAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default);

    Task<ProductResult> SaveStorageLocationAsync(
        StorageLocationSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductResult> DeleteStorageLocationAsync(
        long id,
        CancellationToken cancellationToken = default);

    // Batches (FEFO)
    Task<ProductBatchSummaryDto> GetProductBatchesAsync(
        long productId,
        CancellationToken cancellationToken = default);

    Task<ProductResult> SaveProductBatchAsync(
        long productId,
        BatchSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductResult> MoveBatchLocationAsync(
        long batchId,
        MoveBatchLocationRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductResult> DeleteBatchAsync(
        long batchId,
        CancellationToken cancellationToken = default);

    // Multi-Supplier Catalog
    Task<List<ProductSupplierLinkDto>> GetProductSuppliersAsync(
        long productId,
        CancellationToken cancellationToken = default);

    Task<ProductResult> SaveProductSupplierAsync(
        long productId,
        ProductSupplierSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductResult> RemoveProductSupplierAsync(
        long productId,
        long supplierId,
        CancellationToken cancellationToken = default);

    // Substitute Lookup
    Task<SubstituteProductsResult> GetSubstituteProductsAsync(
        long productId,
        CancellationToken cancellationToken = default);

    // CSV Export & Import
    string GetCsvTemplateContent();

    Task<string> ExportProductsCsvAsync(
        CancellationToken cancellationToken = default);

    Task<ProductCsvValidationResult> ValidateCsvImportAsync(
        string filePath,
        string delimiter = ",",
        bool createMissingRefs = true,
        CancellationToken cancellationToken = default);

    Task<ProductCsvCommitResult> CommitCsvImportAsync(
        string filePath,
        bool insertValidOnly = true,
        string delimiter = ",",
        bool createMissingRefs = true,
        CancellationToken cancellationToken = default);
}

public sealed class ProductQueryFilter
{
    public string? Search { get; set; }
    public long? CategoryId { get; set; }
    public long? BrandId { get; set; }
    public long? SupplierId { get; set; }
    public long? StorageLocationId { get; set; }
    public bool LowStockOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 25;
}

public sealed class ProductListResponse
{
    public List<ProductListItemDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PerPage { get; set; } = 25;
    public int LastPage { get; set; } = 1;
    public int FromItem { get; set; }
    public int ToItem { get; set; }
}

public sealed class ProductListItemDto
{
    public long Id { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Initials { get; set; } = "PR";
    public string? Formulation { get; set; }
    public string? Rack { get; set; }
    public string? ImagePath { get; set; }
    public string Narcotic { get; set; } = "no";
    public bool IsNarcotic => string.Equals(Narcotic, "yes", StringComparison.OrdinalIgnoreCase);

    public long? CategoryId { get; set; }
    public string CategoryName { get; set; } = "—";

    public long? BrandId { get; set; }
    public string BrandName { get; set; } = "—";

    public long? SupplierId { get; set; }
    public string SupplierName { get; set; } = "—";

    public long? StorageLocationId { get; set; }
    public string StorageLocationName { get; set; } = "Shop Counter";
    public string StorageLocationCode { get; set; } = "SHOP";

    public int PackSize { get; set; } = 1;
    public int Quantity { get; set; }
    public int BatchesCount { get; set; }
    public int BatchedQuantity { get; set; }

    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal? WholeSalePackPrice { get; set; }
    public decimal? WholeSaleUnitPrice { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal Margin { get; set; }

    public bool HasStock => Quantity > 0;
    public bool IsLowStock => Quantity < PackSize;
    public bool CanDelete => Quantity <= 0 && BatchesCount == 0;
    public string DeleteTooltip => Quantity > 0
        ? "Cannot delete: product has on-hand quantity"
        : BatchesCount > 0
            ? "Cannot delete: product has batch records"
            : "Delete product";
}

public sealed class ProductFormLookupsDto
{
    public List<LookupOptionDto> Categories { get; set; } = [];
    public List<LookupOptionDto> Brands { get; set; } = [];
    public List<LookupOptionDto> Suppliers { get; set; } = [];
    public List<FormulationOptionDto> Formulations { get; set; } = [];
    public List<StorageLocationDto> StorageLocations { get; set; } = [];
    public string ShopType { get; set; } = "pharmacy";
    public string CurrencySymbol { get; set; } = "Rs.";
}

public sealed class LookupOptionDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class FormulationOptionDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class GeneratedProductCodesDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
}

public sealed class ProductDetailDto
{
    public long Id { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Formulation { get; set; }
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public string? FullImagePath { get; set; }
    public int PackSize { get; set; } = 1;
    public int Quantity { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal? WholeSalePackPrice { get; set; }
    public decimal? WholeSaleUnitPrice { get; set; }
    public decimal? WholeSaleMargin { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal? Margin { get; set; }
    public string Narcotic { get; set; } = "no";
    public decimal? MaxDiscount { get; set; }
    public int? ExpiryWarningDays { get; set; }
    public string? Rack { get; set; }
    public long? CategoryId { get; set; }
    public long? BrandId { get; set; }
    public long? SupplierId { get; set; }
    public long? StorageLocationId { get; set; }
}

public sealed class ProductUpsertRequest
{
    public string ProductCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Formulation { get; set; }
    public string? Description { get; set; }
    public string? SourceImageFilePath { get; set; }
    public bool ClearExistingImage { get; set; }
    public int PackSize { get; set; } = 1;
    public int Quantity { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal? WholeSalePackPrice { get; set; }
    public decimal? WholeSaleUnitPrice { get; set; }
    public decimal? WholeSaleMargin { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal? Margin { get; set; }
    public string Narcotic { get; set; } = "no";
    public decimal? MaxDiscount { get; set; }
    public int? ExpiryWarningDays { get; set; }
    public string? Rack { get; set; }
    public long? CategoryId { get; set; }
    public long? BrandId { get; set; }
    public long? SupplierId { get; set; }
    public long? StorageLocationId { get; set; }
}

public sealed class ProductResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public long? EntityId { get; set; }

    public static ProductResult Success(string message, long? entityId = null) =>
        new() { Succeeded = true, Message = message, EntityId = entityId };

    public static ProductResult Failure(string message) =>
        new() { Succeeded = false, Message = message };
}

public sealed class BulkDeleteProductsResult
{
    public int DeletedCount { get; set; }
    public List<BulkDeleteFailedItemDto> FailedItems { get; set; } = [];
}

public sealed class BulkDeleteFailedItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class BulkUpdateProductMetaRequest
{
    public List<long> ProductIds { get; set; } = [];
    public long? CategoryId { get; set; }
    public long? BrandId { get; set; }
    public long? SupplierId { get; set; }
}

// Storage Locations DTOs
public sealed class StorageLocationDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Type { get; set; } = "shop";
    public string? DefaultRack { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public int ProductsCount { get; set; }
    public int BatchesCount { get; set; }

    public string DisplayLabel => $"{Name} ({Code})";
    public string TypeLabel => Type switch
    {
        "godown" => "Godown",
        "cold_storage" => "Cold Chain",
        "quarantine" => "Quarantine",
        "other" => "Other Area",
        _ => "Shop Floor"
    };
}

public sealed class StorageLocationSaveRequest
{
    public long? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string Type { get; set; } = "shop";
    public string? DefaultRack { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

// Batches DTOs
public sealed class ProductBatchSummaryDto
{
    public long ProductId { get; set; }
    public int TotalStock { get; set; }
    public int BatchedStock { get; set; }
    public int UnassignedStock { get; set; }
    public List<ProductBatchItemDto> Batches { get; set; } = [];
    public List<LocationStockPillDto> LocationBreakdown { get; set; } = [];
}

public sealed class LocationStockPillDto
{
    public long? LocationId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "shop";
    public int Quantity { get; set; }
    public int BatchCount { get; set; }
}

public sealed class ProductBatchItemDto
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public string ExpiryDateFormatted => ExpiryDate?.ToString("yyyy-MM-dd") ?? "N/A";
    public int Quantity { get; set; }
    public long? StorageLocationId { get; set; }
    public string StorageLocationName { get; set; } = "Shop Counter";
    public string StorageLocationCode { get; set; } = "SHOP";
    public string StorageLocationType { get; set; } = "shop";
    public string? Rack { get; set; }
    public decimal? UnitPurchasePrice { get; set; }
    public decimal? UnitSalePrice { get; set; }
    public decimal? PackPurchasePrice { get; set; }
    public decimal? PackSalePrice { get; set; }
    public decimal? WholeSaleUnitPrice { get; set; }
    public decimal? WholeSalePackPrice { get; set; }
    public bool IsExpired { get; set; }
    public int? DaysRemaining { get; set; }
    public bool IsFefoRecommended { get; set; }

    public string PickerLabel =>
        $"{BatchNumber} — {Quantity} units • Exp: {ExpiryDateFormatted} • [{StorageLocationName}]" +
        (IsExpired ? " [Expired]" : IsFefoRecommended ? " [FEFO]" : "");
}

public sealed class BatchSaveRequest
{
    public long? BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public int Quantity { get; set; }
    public long? StorageLocationId { get; set; }
    public string? Rack { get; set; }
    public decimal? UnitSalePrice { get; set; }
    public decimal? UnitPurchasePrice { get; set; }
}

public sealed class MoveBatchLocationRequest
{
    public long ToStorageLocationId { get; set; }
    public int Quantity { get; set; }
    public string? Rack { get; set; }
}

// Multi-Supplier DTOs
public sealed class ProductSupplierLinkDto
{
    public long ProductId { get; set; }
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierPhone { get; set; }
    public string? SupplierSku { get; set; }
    public decimal? LastPurchasePrice { get; set; }
    public int? LeadTimeDays { get; set; }
    public int MinOrderQty { get; set; } = 1;
    public bool IsPreferred { get; set; }
    public bool IsPrimarySupplier { get; set; }
    public string? Notes { get; set; }
}

public sealed class ProductSupplierSaveRequest
{
    public long SupplierId { get; set; }
    public string? SupplierSku { get; set; }
    public decimal? LastPurchasePrice { get; set; }
    public int? LeadTimeDays { get; set; }
    public int MinOrderQty { get; set; } = 1;
    public bool IsPreferred { get; set; }
    public string? Notes { get; set; }
}

// Substitute Lookup DTOs
public sealed class SubstituteProductsResult
{
    public long SourceProductId { get; set; }
    public string SourceProductName { get; set; } = string.Empty;
    public string? SourceFormulation { get; set; }
    public decimal SourceUnitSalePrice { get; set; }
    public int SourceQuantity { get; set; }
    public string MatchType { get; set; } = "none";
    public List<SubstituteProductItemDto> Substitutes { get; set; } = [];
}

public sealed class SubstituteProductItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? Formulation { get; set; }
    public bool IsExactFormulation { get; set; }
    public string? Rack { get; set; }
    public int PackSize { get; set; } = 1;
    public int Quantity { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal PriceDiff { get; set; }
    public string BrandName { get; set; } = "—";
    public string CategoryName { get; set; } = "—";
    public string? FefoBatchNumber { get; set; }
    public string? FefoExpiryDate { get; set; }
    public int? FefoBatchQuantity { get; set; }

    public bool HasStock => Quantity > 0;
    public string PriceDiffSummary => PriceDiff == 0m
        ? "Same price"
        : PriceDiff < 0m
            ? $"Rs. {Math.Abs(PriceDiff):N2} cheaper"
            : $"+Rs. {PriceDiff:N2} higher";
}

// CSV Import DTOs
public sealed class ProductCsvValidationResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public string UsedDelimiter { get; set; } = ",";
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public List<ProductCsvSampleRowDto> InvalidSamples { get; set; } = [];
    public List<ProductCsvSampleRowDto> ValidSamples { get; set; } = [];
}

public sealed class ProductCsvSampleRowDto
{
    public int RowNumber { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PackSize { get; set; } = string.Empty;
    public string UnitPurchasePrice { get; set; } = string.Empty;
    public string UnitSalePrice { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string ErrorsSummary { get; set; } = string.Empty;
}

public sealed class ProductCsvCommitResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public int Committed { get; set; }
    public int NewCount { get; set; }
    public int UpdatedCount { get; set; }
}
