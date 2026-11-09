namespace Karobar.Application.Interfaces;

public interface IBarcodeStudioService
{
    Task<BarcodeStudioBootstrapDto> GetStudioBootstrapAsync(CancellationToken cancellationToken = default);

    Task<BarcodeTemplateDto> SaveTemplateAsync(
        BarcodeTemplateSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<BarcodeTemplateDto> UpdateTemplateAsync(
        long id,
        BarcodeTemplateSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<BarcodeTemplateDto> SetDefaultTemplateAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<(bool Deleted, string Message)> DeleteTemplateAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<List<BarcodeProductLookupDto>> SearchProductsAsync(
        string? search = null,
        long? categoryId = null,
        IReadOnlyList<long>? productIds = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<List<BarcodeImportDocumentDto>> GetImportDocumentsAsync(
        string docType,
        long? supplierId = null,
        long? brandId = null,
        string? search = null,
        long? specificDocumentId = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<BarcodeScanLookupResultDto> ScanProductAsync(
        string? codeOrBarcode,
        long? productId = null,
        CancellationToken cancellationToken = default);
}

public sealed class BarcodeStudioBootstrapDto
{
    public List<BarcodeTemplateDto> Templates { get; set; } = [];
    public BarcodeStoreInfoDto StoreInfo { get; set; } = new();
    public List<BarcodeLookupItemDto> Suppliers { get; set; } = [];
    public List<BarcodeLookupItemDto> Brands { get; set; } = [];
}

public sealed class BarcodeLookupItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class BarcodeStoreInfoDto
{
    public string StoreName { get; set; } = "Karobar App";
    public string PhoneNumber { get; set; } = string.Empty;
    public string CurrencySymbol { get; set; } = "Rs.";
    public string CurrencyPosition { get; set; } = "before";
    public int CurrencyDecimalPlaces { get; set; } = 2;
}

public sealed class BarcodeLayoutOptionsDto
{
    public bool ShowStoreName { get; set; } = true;
    public string StoreNameOverride { get; set; } = string.Empty;
    public bool ShowProductName { get; set; } = true;
    public int NameLines { get; set; } = 2;
    public bool ShowProductCode { get; set; } = false;
    public bool ShowBarcode { get; set; } = true;
    public int BarcodeHeight { get; set; } = 28;
    public double BarWidth { get; set; } = 1.3;
    public bool ShowBarcodeText { get; set; } = true;
    public bool ShowPrice { get; set; } = true;
    public string PriceType { get; set; } = "retail"; // "retail" | "wholesale"
    public string PricePrefix { get; set; } = "MRP:";
    public bool ShowBatch { get; set; } = true;
    public bool ShowExpiry { get; set; } = true;
    public string ExpiryFormat { get; set; } = "MM/YY"; // "MM/YY" | "MM/YYYY" | "DD-MM-YYYY"
    public string CustomFooter { get; set; } = string.Empty;
    public int FontScale { get; set; } = 10;
    public string TextAlign { get; set; } = "center"; // "center" | "left"

    public BarcodeLayoutOptionsDto Clone() => new()
    {
        ShowStoreName = ShowStoreName,
        StoreNameOverride = StoreNameOverride,
        ShowProductName = ShowProductName,
        NameLines = NameLines,
        ShowProductCode = ShowProductCode,
        ShowBarcode = ShowBarcode,
        BarcodeHeight = BarcodeHeight,
        BarWidth = BarWidth,
        ShowBarcodeText = ShowBarcodeText,
        ShowPrice = ShowPrice,
        PriceType = PriceType,
        PricePrefix = PricePrefix,
        ShowBatch = ShowBatch,
        ShowExpiry = ShowExpiry,
        ExpiryFormat = ExpiryFormat,
        CustomFooter = CustomFooter,
        FontScale = FontScale,
        TextAlign = TextAlign
    };
}

public sealed class BarcodeTemplateDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string PaperType { get; set; } = "roll"; // "roll" | "sheet"
    public decimal WidthMm { get; set; } = 50m;
    public decimal HeightMm { get; set; } = 25m;
    public string Orientation { get; set; } = "landscape"; // "landscape" | "portrait"
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public decimal GapXMm { get; set; } = 0m;
    public decimal GapYMm { get; set; } = 3m;
    public decimal MarginTopMm { get; set; } = 1m;
    public decimal MarginLeftMm { get; set; } = 1m;
    public decimal MarginRightMm { get; set; } = 1m;
    public decimal MarginBottomMm { get; set; } = 1m;
    public bool IsDefault { get; set; }
    public BarcodeLayoutOptionsDto Options { get; set; } = new();

    public string DisplayTitle => $"{Name} ({WidthMm:0.#}×{HeightMm:0.#}mm){(IsDefault ? " ★" : "")}";
}

public sealed class BarcodeTemplateSaveRequest
{
    public string Name { get; set; } = "Custom Label";
    public string? Description { get; set; }
    public string PaperType { get; set; } = "roll";
    public decimal WidthMm { get; set; } = 50m;
    public decimal HeightMm { get; set; } = 25m;
    public string Orientation { get; set; } = "landscape";
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public decimal GapXMm { get; set; } = 0m;
    public decimal GapYMm { get; set; } = 3m;
    public decimal MarginTopMm { get; set; } = 1m;
    public decimal MarginLeftMm { get; set; } = 1m;
    public decimal MarginRightMm { get; set; } = 1m;
    public decimal MarginBottomMm { get; set; } = 1m;
    public bool IsDefault { get; set; }
    public BarcodeLayoutOptionsDto Options { get; set; } = new();
}

public sealed class BarcodeBatchOptionDto
{
    public long? Id { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public int Quantity { get; set; }
    public bool IsExpired { get; set; }
    public bool IsFefo { get; set; }
    public bool IsUnbatched { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal WholeSaleUnitPrice { get; set; }
}

public sealed class BarcodeProductLookupDto
{
    public long Id { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Formulation { get; set; } = string.Empty;
    public string Rack { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string RawBarcode { get; set; } = string.Empty;
    public decimal UnitSalePrice { get; set; }
    public decimal WholeSaleUnitPrice { get; set; }
    public int Quantity { get; set; }
    public int PackSize { get; set; } = 1;
    public long? CategoryId { get; set; }
    public long? BrandId { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public long? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int UnassignedQuantity { get; set; }
    public List<BarcodeBatchOptionDto> Batches { get; set; } = [];
}

public sealed class BarcodeImportDocumentDto
{
    public long Id { get; set; }
    public string DocType { get; set; } = "purchase_invoices"; // "purchase_invoices" | "purchase_orders"
    public string ReferenceNumber { get; set; } = string.Empty;
    public string SubReferenceNumber { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int ItemsCount { get; set; }
    public int TotalUnits { get; set; }
    public List<BarcodeImportDocumentLineDto> Items { get; set; } = [];

    public string DisplayTitle =>
        DocType == "purchase_invoices"
            ? $"{ReferenceNumber}{(!string.IsNullOrWhiteSpace(SubReferenceNumber) ? $" (Inv: {SubReferenceNumber})" : "")} — {SupplierName} ({ItemsCount} items, {TotalUnits} units) • {DocumentDate:dd-MM-yyyy}"
            : $"{ReferenceNumber} — {SupplierName} ({ItemsCount} items, {TotalUnits} units) • {DocumentDate:dd-MM-yyyy}";
}

public sealed class BarcodeImportDocumentLineDto
{
    public long LineId { get; set; }
    public BarcodeProductLookupDto Product { get; set; } = new();
    public int OrderedUnits { get; set; } = 1;
    public int OrderedPacks { get; set; } = 1;
    public int PackSize { get; set; } = 1;
    public string GrnBatchNumber { get; set; } = string.Empty;
    public DateTime? GrnExpiryDate { get; set; }
}

public sealed class BarcodeScanLookupResultDto
{
    public bool Found { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsWeightBarcode { get; set; }
    public decimal ParsedQuantity { get; set; } = 1m;
    public int? WeightGrams { get; set; }
    public BarcodeProductLookupDto? Product { get; set; }
    public BarcodeBatchOptionDto? SelectedBatch { get; set; }
}
