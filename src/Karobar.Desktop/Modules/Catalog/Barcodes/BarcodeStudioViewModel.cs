using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;

namespace Karobar.Desktop.ViewModels;

public sealed class BarcodeSelectOption<T>
{
    public T Value { get; init; } = default!;
    public string Label { get; init; } = string.Empty;
}

public sealed class BarcodeQueueBatchSelectOption
{
    public string Key { get; init; } = "unassigned";
    public string Label { get; init; } = string.Empty;
    public long? BatchId { get; init; }
    public string BatchNumber { get; init; } = string.Empty;
    public DateTime? ExpiryDate { get; init; }
    public int StockQty { get; init; }
    public bool IsCustom { get; init; }
    public bool IsUnassigned { get; init; }
    public decimal UnitSalePrice { get; init; }
    public decimal WholeSaleUnitPrice { get; init; }
}

public partial class BarcodeDocumentLineSelectionRow : ObservableObject
{
    private readonly Action _onChanged;

    public BarcodeDocumentLineSelectionRow(
        BarcodeImportDocumentLineDto dto,
        string qtyMode,
        Action onChanged)
    {
        _onChanged = onChanged;
        LineId = dto.LineId;
        Product = dto.Product;
        PackSize = dto.PackSize > 0 ? dto.PackSize : 1;
        OrderedUnits = Math.Max(1, dto.OrderedUnits);
        OrderedPacks = Math.Max(1, dto.OrderedPacks);
        GrnBatchNumber = dto.GrnBatchNumber ?? string.Empty;
        GrnExpiryDate = dto.GrnExpiryDate;
        _isSelected = true;
        _stickerQty = string.Equals(qtyMode, "packs", StringComparison.OrdinalIgnoreCase)
            ? OrderedPacks
            : OrderedUnits;
    }

    public long LineId { get; }
    public BarcodeProductLookupDto Product { get; }
    public string ProductName => Product.Name;
    public string Barcode => Product.Barcode;
    public string BrandName => !string.IsNullOrWhiteSpace(Product.BrandName) ? Product.BrandName : "—";
    public int PackSize { get; }
    public int OrderedUnits { get; }
    public int OrderedPacks { get; }
    public string GrnBatchNumber { get; }
    public DateTime? GrnExpiryDate { get; }

    public string OrderedSummaryText => $"{OrderedUnits} units ({OrderedPacks} packs × {PackSize})";

    public string BatchExpiryBadgeText =>
        !string.IsNullOrWhiteSpace(GrnBatchNumber)
            ? $"{GrnBatchNumber}{(GrnExpiryDate.HasValue ? $" • Exp {GrnExpiryDate:dd-MM-yyyy}" : "")}"
            : "Auto FEFO Batch";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _stickerQty;

    partial void OnIsSelectedChanged(bool value) => _onChanged();

    partial void OnStickerQtyChanged(int value)
    {
        if (value < 0)
        {
            StickerQty = 0;
            return;
        }
        _onChanged();
    }
}

public partial class BarcodeQueueItemRow : ObservableObject
{
    private readonly BarcodeStudioViewModel _parent;
    private bool _suppressBatchSelectionCallback;

    public BarcodeQueueItemRow(
        BarcodeStudioViewModel parent,
        BarcodeProductLookupDto product,
        int initialQty = 1,
        string? preferredBatchNumber = null,
        DateTime? preferredExpiryDate = null)
    {
        _parent = parent;
        QueueKey = Guid.NewGuid().ToString("N");
        Product = product;
        _printQty = Math.Max(1, initialQty);
        _unitSalePrice = product.UnitSalePrice;
        _wholeSaleUnitPrice = product.WholeSaleUnitPrice > 0 ? product.WholeSaleUnitPrice : product.UnitSalePrice;

        RebuildBatchOptions(preferredBatchNumber, preferredExpiryDate);
    }

    public string QueueKey { get; }
    public BarcodeProductLookupDto Product { get; }
    public long ProductId => Product.Id;
    public string ProductName => Product.Name;
    public string ProductCode => Product.ProductCode;
    public string Barcode => Product.Barcode;
    public int TotalProductStock => Product.Quantity;

    public ObservableCollection<BarcodeQueueBatchSelectOption> BatchOptions { get; } = [];

    [ObservableProperty]
    private BarcodeQueueBatchSelectOption? _selectedBatchOption;

    [ObservableProperty]
    private string _selectedBatchKey = "unassigned";

    [ObservableProperty]
    private long? _selectedBatchId;

    [ObservableProperty]
    private string _batchNumber = string.Empty;

    [ObservableProperty]
    private DateTime? _expiryDate;

    [ObservableProperty]
    private int _batchStockQty;

    [ObservableProperty]
    private int _printQty = 1;

    [ObservableProperty]
    private decimal _unitSalePrice;

    [ObservableProperty]
    private decimal _wholeSaleUnitPrice;

    [ObservableProperty]
    private bool _isPreviewed;

    public bool IsCustomBatchSelected => SelectedBatchKey == "custom";

    public bool CanPrintAllBatchStock => !IsCustomBatchSelected && BatchStockQty > 0 && PrintQty != BatchStockQty;

    public string PrintAllBatchButtonText => $"Print All ({BatchStockQty})";

    public decimal EffectivePrice =>
        string.Equals(_parent.PriceType, "wholesale", StringComparison.OrdinalIgnoreCase)
            ? (WholeSaleUnitPrice > 0 ? WholeSaleUnitPrice : UnitSalePrice)
            : UnitSalePrice;

    public string DisplayPriceText => _parent.FormatCurrency(EffectivePrice);

    public string FormattedStockBadge => $"Stock: {TotalProductStock:N0}";

    public void RebuildBatchOptions(string? preferredBatchNumber = null, DateTime? preferredExpiryDate = null)
    {
        _suppressBatchSelectionCallback = true;
        try
        {
            BatchOptions.Clear();

            foreach (var b in Product.Batches)
            {
                var expStr = b.ExpiryDate.HasValue ? b.ExpiryDate.Value.ToString("dd-MM-yyyy") : "No Exp";
                var fefoTag = b.IsFefo ? " ★ FEFO" : string.Empty;
                var expiredTag = b.IsExpired ? " [EXPIRED]" : string.Empty;
                var label = $"{b.BatchNumber} (Exp: {expStr}) — {b.Quantity} stock{fefoTag}{expiredTag}";

                BatchOptions.Add(new BarcodeQueueBatchSelectOption
                {
                    Key = $"batch:{b.Id}",
                    Label = label,
                    BatchId = b.Id,
                    BatchNumber = b.BatchNumber,
                    ExpiryDate = b.ExpiryDate,
                    StockQty = b.Quantity,
                    IsCustom = false,
                    IsUnassigned = false,
                    UnitSalePrice = b.UnitSalePrice > 0 ? b.UnitSalePrice : Product.UnitSalePrice,
                    WholeSaleUnitPrice = b.WholeSaleUnitPrice > 0 ? b.WholeSaleUnitPrice : Product.WholeSaleUnitPrice
                });
            }

            BatchOptions.Add(new BarcodeQueueBatchSelectOption
            {
                Key = "unassigned",
                Label = $"Unassigned Stock ({Product.UnassignedQuantity} units)",
                BatchId = null,
                BatchNumber = string.Empty,
                ExpiryDate = null,
                StockQty = Product.UnassignedQuantity,
                IsCustom = false,
                IsUnassigned = true,
                UnitSalePrice = Product.UnitSalePrice,
                WholeSaleUnitPrice = Product.WholeSaleUnitPrice
            });

            BatchOptions.Add(new BarcodeQueueBatchSelectOption
            {
                Key = "custom",
                Label = "Custom Batch & Expiry Entry…",
                BatchId = null,
                BatchNumber = BatchNumber,
                ExpiryDate = ExpiryDate,
                StockQty = 0,
                IsCustom = true,
                IsUnassigned = false,
                UnitSalePrice = Product.UnitSalePrice,
                WholeSaleUnitPrice = Product.WholeSaleUnitPrice
            });

            BarcodeQueueBatchSelectOption? chosen = null;

            if (!string.IsNullOrWhiteSpace(preferredBatchNumber))
            {
                var prefTrim = preferredBatchNumber.Trim();
                chosen = BatchOptions.FirstOrDefault(o =>
                    !o.IsCustom &&
                    !o.IsUnassigned &&
                    string.Equals(o.BatchNumber, prefTrim, StringComparison.OrdinalIgnoreCase));

                if (chosen == null)
                {
                    // Use Custom Batch mode with the GRN's exact batch & expiry
                    SelectedBatchKey = "custom";
                    SelectedBatchOption = BatchOptions.FirstOrDefault(o => o.IsCustom);
                    SelectedBatchId = null;
                    BatchNumber = prefTrim;
                    ExpiryDate = preferredExpiryDate;
                    BatchStockQty = 0;
                    UnitSalePrice = Product.UnitSalePrice;
                    WholeSaleUnitPrice = Product.WholeSaleUnitPrice > 0 ? Product.WholeSaleUnitPrice : Product.UnitSalePrice;
                    RaiseComputedProperties();
                    return;
                }
            }

            if (chosen == null)
            {
                var fefoDto = Product.Batches.FirstOrDefault(b => b.IsFefo);
                if (fefoDto != null)
                {
                    chosen = BatchOptions.FirstOrDefault(o => o.BatchId == fefoDto.Id);
                }
            }

            if (chosen == null && Product.Batches.Count > 0 && Product.UnassignedQuantity <= 0)
            {
                chosen = BatchOptions.FirstOrDefault(o => !o.IsCustom && !o.IsUnassigned);
            }

            chosen ??= BatchOptions.FirstOrDefault(o => o.IsUnassigned) ?? BatchOptions.First();

            SelectedBatchOption = chosen;
            SelectedBatchKey = chosen.Key;
            ApplyBatchOption(chosen);
        }
        finally
        {
            _suppressBatchSelectionCallback = false;
        }
    }

    partial void OnSelectedBatchOptionChanged(BarcodeQueueBatchSelectOption? value)
    {
        if (_suppressBatchSelectionCallback || value == null)
            return;

        SelectedBatchKey = value.Key;
    }

    partial void OnSelectedBatchKeyChanged(string value)
    {
        if (_suppressBatchSelectionCallback)
            return;

        var opt = BatchOptions.FirstOrDefault(o => o.Key == value);
        if (opt != null)
        {
            _suppressBatchSelectionCallback = true;
            SelectedBatchOption = opt;
            _suppressBatchSelectionCallback = false;
            ApplyBatchOption(opt);
        }

        RaiseComputedProperties();
        _parent.NotifyQueueChanged(this);
    }

    partial void OnBatchNumberChanged(string value)
    {
        _parent.NotifyQueueChanged(this);
    }

    partial void OnExpiryDateChanged(DateTime? value)
    {
        _parent.NotifyQueueChanged(this);
    }

    partial void OnPrintQtyChanged(int value)
    {
        if (value < 1)
        {
            PrintQty = 1;
            return;
        }

        OnPropertyChanged(nameof(CanPrintAllBatchStock));
        _parent.NotifyQueueChanged(this);
    }

    private void ApplyBatchOption(BarcodeQueueBatchSelectOption opt)
    {
        SelectedBatchId = opt.BatchId;
        if (!opt.IsCustom)
        {
            BatchNumber = opt.BatchNumber;
            ExpiryDate = opt.ExpiryDate;
        }
        BatchStockQty = opt.StockQty;
        UnitSalePrice = opt.UnitSalePrice > 0 ? opt.UnitSalePrice : Product.UnitSalePrice;
        WholeSaleUnitPrice = opt.WholeSaleUnitPrice > 0 ? opt.WholeSaleUnitPrice : UnitSalePrice;
    }

    public void RefreshPriceFormatting()
    {
        OnPropertyChanged(nameof(EffectivePrice));
        OnPropertyChanged(nameof(DisplayPriceText));
    }

    private void RaiseComputedProperties()
    {
        OnPropertyChanged(nameof(IsCustomBatchSelected));
        OnPropertyChanged(nameof(CanPrintAllBatchStock));
        OnPropertyChanged(nameof(PrintAllBatchButtonText));
        OnPropertyChanged(nameof(EffectivePrice));
        OnPropertyChanged(nameof(DisplayPriceText));
    }

    [RelayCommand]
    private void IncrementQty() => PrintQty++;

    [RelayCommand]
    private void DecrementQty()
    {
        if (PrintQty > 1)
            PrintQty--;
    }

    [RelayCommand]
    private void FillBatchStockQty()
    {
        if (BatchStockQty > 0)
            PrintQty = BatchStockQty;
    }

    [RelayCommand]
    private void PreviewRow() => _parent.SetActivePreviewItem(this);

    [RelayCommand]
    private void RemoveRow() => _parent.RemoveQueueItem(this);
}

public partial class BarcodeStudioViewModel : ObservableObject
{
    private readonly IBarcodeStudioService _barcodeService;
    private CancellationTokenSource? _productSearchCts;
    private bool _isApplyingTemplate;

    private const double DipPerMm = 96.0 / 25.4;

    public BarcodeStudioViewModel(IBarcodeStudioService barcodeService)
    {
        _barcodeService = barcodeService;

        PriceTypeOptions =
        [
            new BarcodeSelectOption<string> { Value = "retail", Label = "Retail (Unit Sale Price)" },
            new BarcodeSelectOption<string> { Value = "wholesale", Label = "Wholesale Unit Price" }
        ];

        ExpiryFormatOptions =
        [
            new BarcodeSelectOption<string> { Value = "MM/YY", Label = "MM/YY (e.g. 08/27)" },
            new BarcodeSelectOption<string> { Value = "MM/YYYY", Label = "MM/YYYY (e.g. 08/2027)" },
            new BarcodeSelectOption<string> { Value = "DD-MM-YYYY", Label = "DD-MM-YYYY (e.g. 31-08-2027)" }
        ];

        OrientationOptions =
        [
            new BarcodeSelectOption<string> { Value = "landscape", Label = "Landscape (Horizontal)" },
            new BarcodeSelectOption<string> { Value = "portrait", Label = "Portrait (Vertical)" }
        ];

        _selectedPriceTypeOption = PriceTypeOptions[0];
        _selectedExpiryFormatOption = ExpiryFormatOptions[0];
        _selectedOrientationOption = OrientationOptions[0];
    }

    // =========================================================================
    // Collections & Dropdown Options
    // =========================================================================

    public ObservableCollection<BarcodeTemplateDto> Templates { get; } = [];
    public ObservableCollection<BarcodeSelectOption<long>> SupplierFilterOptions { get; } = [];
    public ObservableCollection<BarcodeSelectOption<long>> BrandFilterOptions { get; } = [];
    public ObservableCollection<BarcodeImportDocumentDto> ImportDocumentOptions { get; } = [];
    public ObservableCollection<BarcodeDocumentLineSelectionRow> ImportDocumentLines { get; } = [];
    public ObservableCollection<BarcodeProductLookupDto> ProductSearchResults { get; } = [];
    public ObservableCollection<BarcodeQueueItemRow> PrintQueue { get; } = [];

    public IReadOnlyList<BarcodeSelectOption<string>> PriceTypeOptions { get; }
    public IReadOnlyList<BarcodeSelectOption<string>> ExpiryFormatOptions { get; }
    public IReadOnlyList<BarcodeSelectOption<string>> OrientationOptions { get; }

    // =========================================================================
    // Studio State & Navigation Tabs
    // =========================================================================

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _activeTab = "queue"; // "queue" | "designer" | "paper"

    public bool IsQueueTabActive => ActiveTab == "queue";
    public bool IsDesignerTabActive => ActiveTab == "designer";
    public bool IsPaperTabActive => ActiveTab == "paper";

    partial void OnActiveTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsQueueTabActive));
        OnPropertyChanged(nameof(IsDesignerTabActive));
        OnPropertyChanged(nameof(IsPaperTabActive));
    }

    // Store Info
    [ObservableProperty]
    private string _storeName = "Karobar Pharmacy";

    [ObservableProperty]
    private string _currencySymbol = "Rs.";

    [ObservableProperty]
    private string _currencyPosition = "before";

    [ObservableProperty]
    private int _currencyDecimalPlaces = 2;

    // Active Template Preset
    [ObservableProperty]
    private BarcodeTemplateDto? _selectedTemplateOption;

    [ObservableProperty]
    private long _selectedTemplateId;

    public bool CanDeleteActiveTemplate => Templates.Count > 1 && SelectedTemplateId > 0;
    public bool IsActiveTemplateDefault => Templates.FirstOrDefault(t => t.Id == SelectedTemplateId)?.IsDefault == true;

    partial void OnSelectedTemplateOptionChanged(BarcodeTemplateDto? value)
    {
        if (value != null && SelectedTemplateId != value.Id)
        {
            SelectedTemplateId = value.Id;
        }
    }

    partial void OnSelectedTemplateIdChanged(long value)
    {
        OnPropertyChanged(nameof(CanDeleteActiveTemplate));
        OnPropertyChanged(nameof(IsActiveTemplateDefault));

        var tpl = Templates.FirstOrDefault(t => t.Id == value);
        if (!ReferenceEquals(SelectedTemplateOption, tpl))
        {
            SelectedTemplateOption = tpl;
        }

        if (_isApplyingTemplate || value <= 0)
            return;

        if (tpl != null)
        {
            ApplyTemplateToEditor(tpl);
        }
    }

    // =========================================================================
    // Layout & Styling State (Tab 2 & Tab 3)
    // =========================================================================

    [ObservableProperty]
    private string _layoutName = "Thermal Roll 50x25mm (1-Up)";

    [ObservableProperty]
    private string _paperType = "roll"; // "roll" | "sheet"

    [ObservableProperty]
    private decimal _widthMm = 50m;

    [ObservableProperty]
    private decimal _heightMm = 25m;

    [ObservableProperty]
    private string _orientation = "landscape"; // "landscape" | "portrait"

    [ObservableProperty]
    private int _columns = 1;

    [ObservableProperty]
    private int _rows = 1;

    [ObservableProperty]
    private decimal _gapXMm = 0m;

    [ObservableProperty]
    private decimal _gapYMm = 3m;

    [ObservableProperty]
    private decimal _marginTopMm = 1m;

    [ObservableProperty]
    private decimal _marginLeftMm = 1m;

    [ObservableProperty]
    private decimal _marginRightMm = 1m;

    [ObservableProperty]
    private decimal _marginBottomMm = 1m;

    [ObservableProperty]
    private bool _showStoreName = true;

    [ObservableProperty]
    private string _storeNameOverride = string.Empty;

    [ObservableProperty]
    private bool _showProductName = true;

    [ObservableProperty]
    private int _nameLines = 2;

    [ObservableProperty]
    private bool _showProductCode;

    [ObservableProperty]
    private bool _showBarcode = true;

    [ObservableProperty]
    private int _barcodeHeight = 28;

    [ObservableProperty]
    private double _barWidth = 1.3;

    [ObservableProperty]
    private bool _showBarcodeText = true;

    [ObservableProperty]
    private bool _showPrice = true;

    [ObservableProperty]
    private string _priceType = "retail";

    [ObservableProperty]
    private string _pricePrefix = "MRP:";

    [ObservableProperty]
    private bool _showBatch = true;

    [ObservableProperty]
    private bool _showExpiry = true;

    [ObservableProperty]
    private string _expiryFormat = "MM/YY";

    [ObservableProperty]
    private string _customFooter = string.Empty;

    [ObservableProperty]
    private int _fontScale = 10;

    [ObservableProperty]
    private string _textAlign = "center";

    // Partial Sheet Reuse
    [ObservableProperty]
    private int _skipLabelsCount;

    // Preview Zoom & Active Item
    [ObservableProperty]
    private int _previewZoomPercent = 150;

    [ObservableProperty]
    private BarcodeQueueItemRow? _activePreviewItem;

    // Save As Preset Modal
    [ObservableProperty]
    private bool _isSavePresetModalOpen;

    [ObservableProperty]
    private string _newPresetName = string.Empty;

    [ObservableProperty]
    private bool _newPresetIsDefault;

    [ObservableProperty]
    private BarcodeSelectOption<string>? _selectedPriceTypeOption;

    [ObservableProperty]
    private BarcodeSelectOption<string>? _selectedExpiryFormatOption;

    [ObservableProperty]
    private BarcodeSelectOption<string>? _selectedOrientationOption;

    // =========================================================================
    // GRN & PO Loader State (Tab 1)
    // =========================================================================

    [ObservableProperty]
    private string _importDocType = "purchase_invoices"; // "purchase_invoices" | "purchase_orders"

    [ObservableProperty]
    private BarcodeSelectOption<long>? _selectedSupplierFilterOption;

    [ObservableProperty]
    private long _selectedSupplierFilterId;

    [ObservableProperty]
    private BarcodeSelectOption<long>? _selectedBrandFilterOption;

    [ObservableProperty]
    private long _selectedBrandFilterId;

    [ObservableProperty]
    private BarcodeImportDocumentDto? _selectedImportDocumentOption;

    [ObservableProperty]
    private long _selectedImportDocumentId;

    [ObservableProperty]
    private string _importQtyMode = "units"; // "units" | "packs"

    [ObservableProperty]
    private bool _isLoadingImportDocs;

    public bool IsImportInvoicesActive => ImportDocType == "purchase_invoices";
    public bool IsImportOrdersActive => ImportDocType == "purchase_orders";
    public bool IsImportUnitsMode => ImportQtyMode == "units";
    public bool IsImportPacksMode => ImportQtyMode == "packs";
    public string ImportDocumentSelectLabel => IsImportInvoicesActive
        ? "Select Purchase Invoice (GRN)"
        : "Select Purchase Order";
    public string ImportDocumentPlaceholder => IsImportInvoicesActive
        ? $"Choose from {ImportDocumentOptions.Count} Purchase Invoices (GRNs)…"
        : $"Choose from {ImportDocumentOptions.Count} Purchase Orders…";
    public bool HasSelectedImportDocument => SelectedImportDocumentId > 0 && ImportDocumentLines.Count > 0;
    public int SelectedImportItemsCount => ImportDocumentLines.Count(r => r.IsSelected && r.StickerQty > 0);
    public int SelectedImportTotalStickers => ImportDocumentLines.Where(r => r.IsSelected).Sum(r => Math.Max(0, r.StickerQty));
    public string AddSelectedImportButtonText =>
        SelectedImportItemsCount > 0
            ? $"+ Add {SelectedImportItemsCount} Selected Items ({SelectedImportTotalStickers:N0} Stickers) to Queue"
            : "+ Add Selected Items to Queue";

    // =========================================================================
    // Fast Product Search & Add State (Tab 1)
    // =========================================================================

    [ObservableProperty]
    private string _productSearchText = string.Empty;

    [ObservableProperty]
    private bool _isProductDropdownOpen;

    [ObservableProperty]
    private BarcodeProductLookupDto? _highlightedSearchProduct;

    // =========================================================================
    // Computed Studio & Preview Properties
    // =========================================================================

    public bool IsRollMode => PaperType == "roll";
    public bool IsSheetMode => PaperType == "sheet";
    public bool IsNameLines1 => NameLines == 1;
    public bool IsNameLines2 => NameLines != 1;
    public bool IsTextAlignCenter => TextAlign != "left";
    public bool IsTextAlignLeft => TextAlign == "left";
    public bool IsZoom100 => PreviewZoomPercent == 100;
    public bool IsZoom150 => PreviewZoomPercent == 150;
    public bool IsZoom200 => PreviewZoomPercent == 200;
    public double PreviewScaleFactor => PreviewZoomPercent / 100.0;

    public int QueueProductsCount => PrintQueue.Count;
    public int TotalLabelsToPrint => PrintQueue.Sum(i => Math.Max(0, i.PrintQty));
    public bool HasQueueItems => PrintQueue.Count > 0;
    public bool IsQueueEmpty => PrintQueue.Count == 0;
    public bool HasMultipleQueueItems => PrintQueue.Count > 1;

    public string PrintButtonLabel =>
        TotalLabelsToPrint > 0
            ? $"🖨 Print {TotalLabelsToPrint:N0} Stickers (Ctrl+P)"
            : "🖨 Print Sample Sticker (Ctrl+P)";

    public string TargetDimensionsSummary =>
        IsSheetMode
            ? $"{WidthMm:0.#}mm × {HeightMm:0.#}mm (Sheet {Columns}×{Rows})"
            : $"{WidthMm:0.#}mm × {HeightMm:0.#}mm (Thermal Roll)";

    public string OrientationBadgeText => Orientation.ToUpperInvariant();

    public string QueueSummaryText => $"{QueueProductsCount:N0} Products ({TotalLabelsToPrint:N0} Labels Total)";

    public int MaxSkipLabels => Math.Max(0, Columns * Rows - 1);
    public int LabelsPerSheetCapacity => Math.Max(1, Columns * Rows);

    // Millimeter-Accurate Sticker Preview Geometry
    public double StickerWidthDip => Math.Max(60.0, (double)WidthMm * DipPerMm);
    public double StickerHeightDip => Math.Max(38.0, (double)HeightMm * DipPerMm);

    public Thickness StickerPaddingThickness => new(
        Math.Max(1.0, (double)MarginLeftMm * DipPerMm),
        Math.Max(1.0, (double)MarginTopMm * DipPerMm),
        Math.Max(1.0, (double)MarginRightMm * DipPerMm),
        Math.Max(1.0, (double)MarginBottomMm * DipPerMm));

    public TextAlignment PreviewTextAlignment => IsTextAlignLeft ? TextAlignment.Left : TextAlignment.Center;
    public HorizontalAlignment PreviewHorizontalAlignment => IsTextAlignLeft ? HorizontalAlignment.Left : HorizontalAlignment.Center;

    public double StoreFontSize => Math.Max(6.5, FontScale * 0.78);
    public double ProductFontSize => Math.Max(7.0, FontScale * 0.92);
    public double CodeSkuFontSize => Math.Max(6.0, FontScale * 0.68);
    public double MetaFontSize => Math.Max(6.0, FontScale * 0.72);
    public double PriceFontSize => Math.Max(7.5, FontScale * 0.98);
    public double FooterFontSize => Math.Max(5.8, FontScale * 0.68);
    public double BarcodeCodeFontSize => Math.Max(6.5, FontScale * 0.74);
    public double BarcodeHeightDip => BarcodeHeight;
    public double ProductNameMaxHeight => NameLines == 1 ? ProductFontSize * 1.42 : ProductFontSize * 2.75;

    public string PreviewStoreNameText =>
        !string.IsNullOrWhiteSpace(StoreNameOverride)
            ? StoreNameOverride.Trim()
            : StoreName;

    public string PreviewProductNameText =>
        ActivePreviewItem != null
            ? ActivePreviewItem.ProductName
            : "Panadol Extra 500mg Tablets";

    public string PreviewProductCodeText =>
        ActivePreviewItem != null && !string.IsNullOrWhiteSpace(ActivePreviewItem.ProductCode)
            ? $"SKU: {ActivePreviewItem.ProductCode}"
            : "SKU: PRD-0001";

    public string PreviewBarcodeValue =>
        ActivePreviewItem != null && !string.IsNullOrWhiteSpace(ActivePreviewItem.Barcode)
            ? ActivePreviewItem.Barcode
            : "896400012345";

    public string PreviewBatchText
    {
        get
        {
            var batch = ActivePreviewItem != null ? ActivePreviewItem.BatchNumber : "B-2026";
            return !string.IsNullOrWhiteSpace(batch) ? $"B:{batch}" : string.Empty;
        }
    }

    public string PreviewExpiryText
    {
        get
        {
            DateTime? exp = ActivePreviewItem != null
                ? ActivePreviewItem.ExpiryDate
                : new DateTime(2027, 8, 31);

            if (!exp.HasValue)
                return string.Empty;

            return $"Exp:{FormatExpiryDate(exp.Value, ExpiryFormat)}";
        }
    }

    public bool ShowPreviewBatchElement => ShowBatch && !string.IsNullOrWhiteSpace(PreviewBatchText);
    public bool ShowPreviewExpiryElement => ShowExpiry && !string.IsNullOrWhiteSpace(PreviewExpiryText);
    public bool ShowPreviewBatchOrExpiryRow => ShowPreviewBatchElement || ShowPreviewExpiryElement || ShowPrice;
    public bool ShowPreviewFooterElement => !string.IsNullOrWhiteSpace(CustomFooter);

    public string PreviewPriceText
    {
        get
        {
            var price = ActivePreviewItem != null ? ActivePreviewItem.EffectivePrice : 150m;
            var formatted = FormatCurrency(price);
            return !string.IsNullOrWhiteSpace(PricePrefix)
                ? $"{PricePrefix.Trim()} {formatted}"
                : formatted;
        }
    }

    public string PreviewActiveItemIndicator
    {
        get
        {
            if (ActivePreviewItem == null || PrintQueue.Count == 0)
                return "Previewing: Sample Label";
            var idx = PrintQueue.IndexOf(ActivePreviewItem);
            return $"Previewing Item {idx + 1} of {PrintQueue.Count}: {ActivePreviewItem.ProductName}";
        }
    }

    // =========================================================================
    // Property Change Hooks for Live Preview
    // =========================================================================

    partial void OnPaperTypeChanged(string value)
    {
        if (!_isApplyingTemplate)
        {
            if (value == "roll")
            {
                Columns = 1;
                Rows = 1;
                SkipLabelsCount = 0;
            }
            else if (Columns <= 1 && Rows <= 1)
            {
                Columns = 4;
                Rows = 10;
            }
        }
        RaisePreviewChanged();
    }

    partial void OnWidthMmChanged(decimal value) => RaisePreviewChanged();
    partial void OnHeightMmChanged(decimal value) => RaisePreviewChanged();
    partial void OnOrientationChanged(string value)
    {
        var match = OrientationOptions.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));
        if (match != null && !ReferenceEquals(SelectedOrientationOption, match))
            SelectedOrientationOption = match;
        RaisePreviewChanged();
    }
    partial void OnSelectedOrientationOptionChanged(BarcodeSelectOption<string>? value)
    {
        if (value != null && !string.Equals(Orientation, value.Value, StringComparison.OrdinalIgnoreCase))
            Orientation = value.Value;
    }
    partial void OnColumnsChanged(int value)
    {
        OnPropertyChanged(nameof(MaxSkipLabels));
        OnPropertyChanged(nameof(LabelsPerSheetCapacity));
        RaisePreviewChanged();
    }
    partial void OnRowsChanged(int value)
    {
        OnPropertyChanged(nameof(MaxSkipLabels));
        OnPropertyChanged(nameof(LabelsPerSheetCapacity));
        RaisePreviewChanged();
    }
    partial void OnGapXMmChanged(decimal value) => RaisePreviewChanged();
    partial void OnGapYMmChanged(decimal value) => RaisePreviewChanged();
    partial void OnMarginTopMmChanged(decimal value) => RaisePreviewChanged();
    partial void OnMarginLeftMmChanged(decimal value) => RaisePreviewChanged();
    partial void OnMarginRightMmChanged(decimal value) => RaisePreviewChanged();
    partial void OnMarginBottomMmChanged(decimal value) => RaisePreviewChanged();
    partial void OnShowStoreNameChanged(bool value) => RaisePreviewChanged();
    partial void OnStoreNameOverrideChanged(string value) => RaisePreviewChanged();
    partial void OnShowProductNameChanged(bool value) => RaisePreviewChanged();
    partial void OnNameLinesChanged(int value) => RaisePreviewChanged();
    partial void OnShowProductCodeChanged(bool value) => RaisePreviewChanged();
    partial void OnShowBarcodeChanged(bool value) => RaisePreviewChanged();
    partial void OnBarcodeHeightChanged(int value) => RaisePreviewChanged();
    partial void OnBarWidthChanged(double value) => RaisePreviewChanged();
    partial void OnShowBarcodeTextChanged(bool value) => RaisePreviewChanged();
    partial void OnShowPriceChanged(bool value) => RaisePreviewChanged();
    partial void OnPriceTypeChanged(string value)
    {
        var match = PriceTypeOptions.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));
        if (match != null && !ReferenceEquals(SelectedPriceTypeOption, match))
            SelectedPriceTypeOption = match;
        foreach (var row in PrintQueue)
            row.RefreshPriceFormatting();
        RaisePreviewChanged();
    }
    partial void OnSelectedPriceTypeOptionChanged(BarcodeSelectOption<string>? value)
    {
        if (value != null && !string.Equals(PriceType, value.Value, StringComparison.OrdinalIgnoreCase))
            PriceType = value.Value;
    }
    partial void OnPricePrefixChanged(string value) => RaisePreviewChanged();
    partial void OnShowBatchChanged(bool value) => RaisePreviewChanged();
    partial void OnShowExpiryChanged(bool value) => RaisePreviewChanged();
    partial void OnExpiryFormatChanged(string value)
    {
        var match = ExpiryFormatOptions.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));
        if (match != null && !ReferenceEquals(SelectedExpiryFormatOption, match))
            SelectedExpiryFormatOption = match;
        RaisePreviewChanged();
    }
    partial void OnSelectedExpiryFormatOptionChanged(BarcodeSelectOption<string>? value)
    {
        if (value != null && !string.Equals(ExpiryFormat, value.Value, StringComparison.OrdinalIgnoreCase))
            ExpiryFormat = value.Value;
    }
    partial void OnCustomFooterChanged(string value) => RaisePreviewChanged();
    partial void OnFontScaleChanged(int value) => RaisePreviewChanged();
    partial void OnTextAlignChanged(string value) => RaisePreviewChanged();
    partial void OnPreviewZoomPercentChanged(int value) => RaisePreviewChanged();
    partial void OnActivePreviewItemChanged(BarcodeQueueItemRow? value)
    {
        foreach (var row in PrintQueue)
            row.IsPreviewed = ReferenceEquals(row, value);
        RaisePreviewChanged();
    }

    partial void OnSkipLabelsCountChanged(int value)
    {
        if (value < 0)
            SkipLabelsCount = 0;
        else if (value > MaxSkipLabels)
            SkipLabelsCount = MaxSkipLabels;
    }

    partial void OnImportDocTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsImportInvoicesActive));
        OnPropertyChanged(nameof(IsImportOrdersActive));
        OnPropertyChanged(nameof(ImportDocumentSelectLabel));
        _ = LoadImportDocumentsAsync();
    }

    partial void OnSelectedSupplierFilterOptionChanged(BarcodeSelectOption<long>? value)
    {
        var nextId = value?.Value ?? 0;
        if (SelectedSupplierFilterId != nextId)
            SelectedSupplierFilterId = nextId;
    }

    partial void OnSelectedSupplierFilterIdChanged(long value)
    {
        var match = SupplierFilterOptions.FirstOrDefault(o => o.Value == value);
        if (!ReferenceEquals(SelectedSupplierFilterOption, match))
            SelectedSupplierFilterOption = match;
        _ = LoadImportDocumentsAsync();
    }

    partial void OnSelectedBrandFilterOptionChanged(BarcodeSelectOption<long>? value)
    {
        var nextId = value?.Value ?? 0;
        if (SelectedBrandFilterId != nextId)
            SelectedBrandFilterId = nextId;
    }

    partial void OnSelectedBrandFilterIdChanged(long value)
    {
        var match = BrandFilterOptions.FirstOrDefault(o => o.Value == value);
        if (!ReferenceEquals(SelectedBrandFilterOption, match))
            SelectedBrandFilterOption = match;
        _ = LoadImportDocumentsAsync();
    }

    partial void OnSelectedImportDocumentOptionChanged(BarcodeImportDocumentDto? value)
    {
        var nextId = value?.Id ?? 0;
        if (SelectedImportDocumentId != nextId)
            SelectedImportDocumentId = nextId;
    }

    partial void OnSelectedImportDocumentIdChanged(long value)
    {
        var match = ImportDocumentOptions.FirstOrDefault(d => d.Id == value);
        if (!ReferenceEquals(SelectedImportDocumentOption, match))
            SelectedImportDocumentOption = match;
        RebuildImportDocumentLines();
    }

    partial void OnImportQtyModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsImportUnitsMode));
        OnPropertyChanged(nameof(IsImportPacksMode));
        foreach (var line in ImportDocumentLines)
        {
            line.StickerQty = value == "packs" ? line.OrderedPacks : line.OrderedUnits;
        }
        NotifyImportLinesSelectionChanged();
    }

    partial void OnProductSearchTextChanged(string value)
    {
        _ = DebouncedSearchProductsAsync(value);
    }

    private void RaisePreviewChanged()
    {
        OnPropertyChanged(nameof(IsRollMode));
        OnPropertyChanged(nameof(IsSheetMode));
        OnPropertyChanged(nameof(IsNameLines1));
        OnPropertyChanged(nameof(IsNameLines2));
        OnPropertyChanged(nameof(IsTextAlignCenter));
        OnPropertyChanged(nameof(IsTextAlignLeft));
        OnPropertyChanged(nameof(IsZoom100));
        OnPropertyChanged(nameof(IsZoom150));
        OnPropertyChanged(nameof(IsZoom200));
        OnPropertyChanged(nameof(PreviewScaleFactor));
        OnPropertyChanged(nameof(TargetDimensionsSummary));
        OnPropertyChanged(nameof(OrientationBadgeText));
        OnPropertyChanged(nameof(StickerWidthDip));
        OnPropertyChanged(nameof(StickerHeightDip));
        OnPropertyChanged(nameof(StickerPaddingThickness));
        OnPropertyChanged(nameof(PreviewTextAlignment));
        OnPropertyChanged(nameof(PreviewHorizontalAlignment));
        OnPropertyChanged(nameof(StoreFontSize));
        OnPropertyChanged(nameof(ProductFontSize));
        OnPropertyChanged(nameof(CodeSkuFontSize));
        OnPropertyChanged(nameof(MetaFontSize));
        OnPropertyChanged(nameof(PriceFontSize));
        OnPropertyChanged(nameof(FooterFontSize));
        OnPropertyChanged(nameof(BarcodeCodeFontSize));
        OnPropertyChanged(nameof(BarcodeHeightDip));
        OnPropertyChanged(nameof(ProductNameMaxHeight));
        OnPropertyChanged(nameof(PreviewStoreNameText));
        OnPropertyChanged(nameof(PreviewProductNameText));
        OnPropertyChanged(nameof(PreviewProductCodeText));
        OnPropertyChanged(nameof(PreviewBarcodeValue));
        OnPropertyChanged(nameof(PreviewBatchText));
        OnPropertyChanged(nameof(PreviewExpiryText));
        OnPropertyChanged(nameof(ShowPreviewBatchElement));
        OnPropertyChanged(nameof(ShowPreviewExpiryElement));
        OnPropertyChanged(nameof(ShowPreviewBatchOrExpiryRow));
        OnPropertyChanged(nameof(ShowPreviewFooterElement));
        OnPropertyChanged(nameof(PreviewPriceText));
        OnPropertyChanged(nameof(PreviewActiveItemIndicator));
    }

    // =========================================================================
    // Initialization & Deep-Link Navigation
    // =========================================================================

    public async Task InitializeAsync(string? routeWithQuery = null)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            var bootstrap = await _barcodeService.GetStudioBootstrapAsync();

            StoreName = bootstrap.StoreInfo.StoreName;
            CurrencySymbol = bootstrap.StoreInfo.CurrencySymbol;
            CurrencyPosition = bootstrap.StoreInfo.CurrencyPosition;
            CurrencyDecimalPlaces = bootstrap.StoreInfo.CurrencyDecimalPlaces;

            Templates.Clear();
            foreach (var t in bootstrap.Templates)
                Templates.Add(t);

            SupplierFilterOptions.Clear();
            SupplierFilterOptions.Add(new BarcodeSelectOption<long> { Value = 0, Label = "All Suppliers" });
            foreach (var s in bootstrap.Suppliers)
                SupplierFilterOptions.Add(new BarcodeSelectOption<long> { Value = s.Id, Label = s.Name });
            SelectedSupplierFilterOption = SupplierFilterOptions.FirstOrDefault();

            BrandFilterOptions.Clear();
            BrandFilterOptions.Add(new BarcodeSelectOption<long> { Value = 0, Label = "All Brands" });
            foreach (var b in bootstrap.Brands)
                BrandFilterOptions.Add(new BarcodeSelectOption<long> { Value = b.Id, Label = b.Name });
            SelectedBrandFilterOption = BrandFilterOptions.FirstOrDefault();

            var defaultTemplate = Templates.FirstOrDefault(t => t.IsDefault) ?? Templates.FirstOrDefault();
            if (defaultTemplate != null)
            {
                _isApplyingTemplate = true;
                SelectedTemplateOption = defaultTemplate;
                SelectedTemplateId = defaultTemplate.Id;
                _isApplyingTemplate = false;
                ApplyTemplateToEditor(defaultTemplate);
            }

            await LoadImportDocumentsAsync();

            var initialProducts = await _barcodeService.SearchProductsAsync(limit: 30);
            ProductSearchResults.Clear();
            foreach (var p in initialProducts)
                ProductSearchResults.Add(p);
            HighlightedSearchProduct = ProductSearchResults.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(routeWithQuery))
            {
                await ApplyNavigationQueryAsync(routeWithQuery);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Barcode Studio: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ApplyNavigationQueryAsync(string routeWithQuery)
    {
        if (string.IsNullOrWhiteSpace(routeWithQuery))
            return;

        var qIdx = routeWithQuery.IndexOf('?');
        if (qIdx < 0 || qIdx >= routeWithQuery.Length - 1)
            return;

        var query = routeWithQuery[(qIdx + 1)..];
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2)
                dict[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
        }

        ActiveTab = "queue";

        // 1. Check product_id or product_ids
        var productIdList = new List<long>();
        if (dict.TryGetValue("product_id", out var singlePid) && long.TryParse(singlePid, out var pid) && pid > 0)
        {
            productIdList.Add(pid);
        }
        if (dict.TryGetValue("product_ids", out var multiPids) && !string.IsNullOrWhiteSpace(multiPids))
        {
            foreach (var part in multiPids.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part.Trim(), out var idVal) && idVal > 0)
                    productIdList.Add(idVal);
            }
        }

        if (productIdList.Count > 0)
        {
            var prods = await _barcodeService.SearchProductsAsync(productIds: productIdList, limit: productIdList.Count + 10);
            foreach (var prod in prods)
            {
                AddProductToQueue(prod, initialQty: Math.Max(1, prod.Quantity > 0 ? prod.Quantity : 1));
            }
            SetStatus($"Loaded {prods.Count} product(s) into print queue.");
        }

        // 2. Check purchase_invoice_id
        if (dict.TryGetValue("purchase_invoice_id", out var piStr) && long.TryParse(piStr, out var piId) && piId > 0)
        {
            ImportDocType = "purchase_invoices";
            var docs = await _barcodeService.GetImportDocumentsAsync("purchase_invoices", specificDocumentId: piId, limit: 1);
            if (docs.Count > 0)
            {
                var doc = docs[0];
                if (!ImportDocumentOptions.Any(d => d.Id == doc.Id))
                    ImportDocumentOptions.Insert(0, doc);
                SelectedImportDocumentId = doc.Id;
                AddSelectedImportItemsToQueue();
            }
        }

        // 3. Check purchase_order_id
        if (dict.TryGetValue("purchase_order_id", out var poStr) && long.TryParse(poStr, out var poId) && poId > 0)
        {
            ImportDocType = "purchase_orders";
            var docs = await _barcodeService.GetImportDocumentsAsync("purchase_orders", specificDocumentId: poId, limit: 1);
            if (docs.Count > 0)
            {
                var doc = docs[0];
                if (!ImportDocumentOptions.Any(d => d.Id == doc.Id))
                    ImportDocumentOptions.Insert(0, doc);
                SelectedImportDocumentId = doc.Id;
                AddSelectedImportItemsToQueue();
            }
        }
    }

    // =========================================================================
    // Template Preset Actions
    // =========================================================================

    private void ApplyTemplateToEditor(BarcodeTemplateDto tpl)
    {
        _isApplyingTemplate = true;
        try
        {
            LayoutName = tpl.Name;
            PaperType = tpl.PaperType;
            WidthMm = tpl.WidthMm;
            HeightMm = tpl.HeightMm;
            Orientation = tpl.Orientation;
            Columns = tpl.Columns;
            Rows = tpl.Rows;
            GapXMm = tpl.GapXMm;
            GapYMm = tpl.GapYMm;
            MarginTopMm = tpl.MarginTopMm;
            MarginLeftMm = tpl.MarginLeftMm;
            MarginRightMm = tpl.MarginRightMm;
            MarginBottomMm = tpl.MarginBottomMm;

            var opts = tpl.Options ?? new BarcodeLayoutOptionsDto();
            ShowStoreName = opts.ShowStoreName;
            StoreNameOverride = opts.StoreNameOverride ?? string.Empty;
            ShowProductName = opts.ShowProductName;
            NameLines = opts.NameLines == 1 ? 1 : 2;
            ShowProductCode = opts.ShowProductCode;
            ShowBarcode = opts.ShowBarcode;
            BarcodeHeight = Math.Clamp(opts.BarcodeHeight, 16, 50);
            BarWidth = Math.Clamp(opts.BarWidth, 1.0, 2.2);
            ShowBarcodeText = opts.ShowBarcodeText;
            ShowPrice = opts.ShowPrice;
            PriceType = string.Equals(opts.PriceType, "wholesale", StringComparison.OrdinalIgnoreCase) ? "wholesale" : "retail";
            PricePrefix = opts.PricePrefix ?? "MRP:";
            ShowBatch = opts.ShowBatch;
            ShowExpiry = opts.ShowExpiry;
            ExpiryFormat = string.IsNullOrWhiteSpace(opts.ExpiryFormat) ? "MM/YY" : opts.ExpiryFormat;
            CustomFooter = opts.CustomFooter ?? string.Empty;
            FontScale = Math.Clamp(opts.FontScale, 8, 14);
            TextAlign = string.Equals(opts.TextAlign, "left", StringComparison.OrdinalIgnoreCase) ? "left" : "center";
        }
        finally
        {
            _isApplyingTemplate = false;
            RaisePreviewChanged();
        }
    }

    private BarcodeTemplateSaveRequest BuildCurrentTemplateRequest(string name, bool isDefault)
    {
        return new BarcodeTemplateSaveRequest
        {
            Name = name,
            PaperType = PaperType,
            WidthMm = WidthMm,
            HeightMm = HeightMm,
            Orientation = Orientation,
            Columns = IsSheetMode ? Math.Max(1, Columns) : 1,
            Rows = IsSheetMode ? Math.Max(1, Rows) : 1,
            GapXMm = GapXMm,
            GapYMm = GapYMm,
            MarginTopMm = MarginTopMm,
            MarginLeftMm = MarginLeftMm,
            MarginRightMm = MarginRightMm,
            MarginBottomMm = MarginBottomMm,
            IsDefault = isDefault,
            Options = new BarcodeLayoutOptionsDto
            {
                ShowStoreName = ShowStoreName,
                StoreNameOverride = StoreNameOverride,
                ShowProductName = ShowProductName,
                NameLines = NameLines,
                ShowProductCode = ShowProductCode,
                ShowBarcode = ShowBarcode,
                BarcodeHeight = BarcodeHeight,
                BarWidth = Math.Round(BarWidth, 2),
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
            }
        };
    }

    [RelayCommand]
    public async Task UpdateActiveTemplateAsync()
    {
        if (SelectedTemplateId <= 0)
        {
            OpenSavePresetModal();
            return;
        }

        try
        {
            var existing = Templates.FirstOrDefault(t => t.Id == SelectedTemplateId);
            var req = BuildCurrentTemplateRequest(
                existing?.Name ?? LayoutName,
                existing?.IsDefault ?? false);

            var updated = await _barcodeService.UpdateTemplateAsync(SelectedTemplateId, req);
            ReplaceOrAddTemplateInList(updated);
            SetStatus($"Updated preset \"{updated.Name}\".");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to update preset: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SetActiveTemplateDefaultAsync()
    {
        if (SelectedTemplateId <= 0)
            return;

        try
        {
            var updated = await _barcodeService.SetDefaultTemplateAsync(SelectedTemplateId);
            var bootstrap = await _barcodeService.GetStudioBootstrapAsync();
            _isApplyingTemplate = true;
            Templates.Clear();
            foreach (var t in bootstrap.Templates)
                Templates.Add(t);
            SelectedTemplateOption = Templates.FirstOrDefault(t => t.Id == updated.Id);
            SelectedTemplateId = updated.Id;
            _isApplyingTemplate = false;
            OnPropertyChanged(nameof(IsActiveTemplateDefault));
            SetStatus($"\"{updated.Name}\" is now the default barcode template.");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to set default preset: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task DeleteActiveTemplateAsync()
    {
        if (!CanDeleteActiveTemplate)
            return;

        var current = Templates.FirstOrDefault(t => t.Id == SelectedTemplateId);
        if (current == null)
            return;

        var confirm = MessageBox.Show(
            $"Delete barcode template preset \"{current.Name}\"?",
            "Delete Barcode Template",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var (deleted, msg) = await _barcodeService.DeleteTemplateAsync(current.Id);
            if (!deleted)
            {
                ErrorMessage = msg;
                return;
            }

            var bootstrap = await _barcodeService.GetStudioBootstrapAsync();
            _isApplyingTemplate = true;
            Templates.Clear();
            foreach (var t in bootstrap.Templates)
                Templates.Add(t);
            var next = Templates.FirstOrDefault(t => t.IsDefault) ?? Templates.FirstOrDefault();
            SelectedTemplateId = next?.Id ?? 0;
            _isApplyingTemplate = false;

            if (next != null)
                ApplyTemplateToEditor(next);

            OnPropertyChanged(nameof(CanDeleteActiveTemplate));
            SetStatus(msg);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete template: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenSavePresetModal()
    {
        NewPresetName = $"{(IsSheetMode ? "Sheet" : "Roll")} {WidthMm:0.#}x{HeightMm:0.#}mm Custom";
        NewPresetIsDefault = false;
        IsSavePresetModalOpen = true;
    }

    [RelayCommand]
    public void CloseSavePresetModal()
    {
        IsSavePresetModalOpen = false;
    }

    [RelayCommand]
    public async Task ConfirmSaveNewPresetAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPresetName))
        {
            ErrorMessage = "Please enter a name for the custom template preset.";
            return;
        }

        try
        {
            var req = BuildCurrentTemplateRequest(NewPresetName.Trim(), NewPresetIsDefault);
            var created = await _barcodeService.SaveTemplateAsync(req);

            var bootstrap = await _barcodeService.GetStudioBootstrapAsync();
            _isApplyingTemplate = true;
            Templates.Clear();
            foreach (var t in bootstrap.Templates)
                Templates.Add(t);
            SelectedTemplateId = created.Id;
            _isApplyingTemplate = false;

            IsSavePresetModalOpen = false;
            OnPropertyChanged(nameof(CanDeleteActiveTemplate));
            OnPropertyChanged(nameof(IsActiveTemplateDefault));
            SetStatus($"Saved new preset \"{created.Name}\".");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save preset: {ex.Message}";
        }
    }

    private void ReplaceOrAddTemplateInList(BarcodeTemplateDto dto)
    {
        _isApplyingTemplate = true;
        try
        {
            var idx = -1;
            for (var i = 0; i < Templates.Count; i++)
            {
                if (Templates[i].Id == dto.Id)
                {
                    idx = i;
                    break;
                }
            }

            if (idx >= 0)
                Templates[idx] = dto;
            else
                Templates.Add(dto);

            SelectedTemplateOption = dto;
            SelectedTemplateId = dto.Id;
        }
        finally
        {
            _isApplyingTemplate = false;
        }
    }

    // =========================================================================
    // Tab & Styling Quick Toggle Commands
    // =========================================================================

    [RelayCommand]
    public void SwitchTab(string tab)
    {
        if (!string.IsNullOrWhiteSpace(tab))
            ActiveTab = tab;
    }

    [RelayCommand]
    public void SetPaperTypeMode(string mode)
    {
        if (mode is "roll" or "sheet")
            PaperType = mode;
    }

    [RelayCommand]
    public void SetNameLinesCount(string countStr)
    {
        if (int.TryParse(countStr, out var n))
            NameLines = n == 1 ? 1 : 2;
    }

    [RelayCommand]
    public void SetTextAlignmentMode(string align)
    {
        if (align is "center" or "left")
            TextAlign = align;
    }

    [RelayCommand]
    public void SetPreviewZoom(string zoomStr)
    {
        if (int.TryParse(zoomStr, out var z) && z is 100 or 150 or 200)
            PreviewZoomPercent = z;
    }

    [RelayCommand]
    public void PreviewPreviousItem()
    {
        if (PrintQueue.Count <= 1)
            return;
        var idx = ActivePreviewItem != null ? PrintQueue.IndexOf(ActivePreviewItem) : 0;
        var prevIdx = idx <= 0 ? PrintQueue.Count - 1 : idx - 1;
        ActivePreviewItem = PrintQueue[prevIdx];
    }

    [RelayCommand]
    public void PreviewNextItem()
    {
        if (PrintQueue.Count <= 1)
            return;
        var idx = ActivePreviewItem != null ? PrintQueue.IndexOf(ActivePreviewItem) : 0;
        var nextIdx = idx >= PrintQueue.Count - 1 ? 0 : idx + 1;
        ActivePreviewItem = PrintQueue[nextIdx];
    }

    // =========================================================================
    // Purchase Invoice (GRN) & Purchase Order Loader
    // =========================================================================

    [RelayCommand]
    public void SetImportSourceType(string docType)
    {
        if (docType is "purchase_invoices" or "purchase_orders")
            ImportDocType = docType;
    }

    [RelayCommand]
    public void SetImportQuantityMode(string mode)
    {
        if (mode is "units" or "packs")
            ImportQtyMode = mode;
    }

    private async Task LoadImportDocumentsAsync()
    {
        try
        {
            IsLoadingImportDocs = true;
            var supId = SelectedSupplierFilterId > 0 ? SelectedSupplierFilterId : (long?)null;
            var brandId = SelectedBrandFilterId > 0 ? SelectedBrandFilterId : (long?)null;

            var docs = await _barcodeService.GetImportDocumentsAsync(
                ImportDocType,
                supplierId: supId,
                brandId: brandId,
                limit: 60);

            ImportDocumentOptions.Clear();
            foreach (var d in docs)
                ImportDocumentOptions.Add(d);

            SelectedImportDocumentId = 0;
            ImportDocumentLines.Clear();
            OnPropertyChanged(nameof(ImportDocumentPlaceholder));
            NotifyImportLinesSelectionChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load {ImportDocType}: {ex.Message}";
        }
        finally
        {
            IsLoadingImportDocs = false;
        }
    }

    private void RebuildImportDocumentLines()
    {
        ImportDocumentLines.Clear();
        var doc = ImportDocumentOptions.FirstOrDefault(d => d.Id == SelectedImportDocumentId);
        if (doc != null)
        {
            var brandFilter = SelectedBrandFilterId > 0 ? SelectedBrandFilterId : (long?)null;
            foreach (var line in doc.Items)
            {
                if (brandFilter.HasValue && line.Product.BrandId != brandFilter.Value)
                    continue;

                ImportDocumentLines.Add(new BarcodeDocumentLineSelectionRow(
                    line,
                    ImportQtyMode,
                    NotifyImportLinesSelectionChanged));
            }
        }

        NotifyImportLinesSelectionChanged();
    }

    private void NotifyImportLinesSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedImportDocument));
        OnPropertyChanged(nameof(SelectedImportItemsCount));
        OnPropertyChanged(nameof(SelectedImportTotalStickers));
        OnPropertyChanged(nameof(AddSelectedImportButtonText));
    }

    [RelayCommand]
    public void SelectAllImportLines()
    {
        var allChecked = ImportDocumentLines.All(r => r.IsSelected);
        foreach (var line in ImportDocumentLines)
            line.IsSelected = !allChecked;
        NotifyImportLinesSelectionChanged();
    }

    [RelayCommand]
    public void AddSelectedImportItemsToQueue()
    {
        var selectedRows = ImportDocumentLines.Where(r => r.IsSelected && r.StickerQty > 0).ToList();
        if (selectedRows.Count == 0)
        {
            ErrorMessage = "Please select at least one item with Sticker Qty > 0.";
            return;
        }

        BarcodeQueueItemRow? lastAdded = null;
        foreach (var line in selectedRows)
        {
            lastAdded = AddProductToQueue(
                line.Product,
                initialQty: line.StickerQty,
                preferredBatchNumber: line.GrnBatchNumber,
                preferredExpiryDate: line.GrnExpiryDate);
        }

        if (lastAdded != null)
            ActivePreviewItem = lastAdded;

        SetStatus($"Added {selectedRows.Count} item(s) to the barcode print queue.");
    }

    // =========================================================================
    // Fast Product Search & Print Queue Management
    // =========================================================================

    private async Task DebouncedSearchProductsAsync(string search)
    {
        _productSearchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _productSearchCts = cts;

        try
        {
            await Task.Delay(160, cts.Token);
            var results = await _barcodeService.SearchProductsAsync(search, limit: 35, cancellationToken: cts.Token);
            if (cts.Token.IsCancellationRequested)
                return;

            ProductSearchResults.Clear();
            foreach (var p in results)
                ProductSearchResults.Add(p);

            HighlightedSearchProduct = ProductSearchResults.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(search))
                IsProductDropdownOpen = ProductSearchResults.Count > 0;
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch
        {
            // ignored
        }
    }

    public void MoveProductSearchHighlight(int delta)
    {
        if (ProductSearchResults.Count == 0)
            return;

        IsProductDropdownOpen = true;
        var currentIndex = HighlightedSearchProduct != null
            ? ProductSearchResults.IndexOf(HighlightedSearchProduct)
            : -1;

        var nextIndex = Math.Clamp(currentIndex + delta, 0, ProductSearchResults.Count - 1);
        HighlightedSearchProduct = ProductSearchResults[nextIndex];
    }

    [RelayCommand]
    public async Task CommitHighlightedSearchProductAsync()
    {
        if (HighlightedSearchProduct != null && IsProductDropdownOpen)
        {
            var chosen = HighlightedSearchProduct;
            var row = AddProductToQueue(chosen, initialQty: 1);
            ActivePreviewItem = row;
            ProductSearchText = string.Empty;
            IsProductDropdownOpen = false;
            SetStatus($"Added \"{chosen.Name}\" to print queue.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(ProductSearchText))
        {
            var scan = await _barcodeService.ScanProductAsync(ProductSearchText.Trim());
            if (scan.Found && scan.Product != null)
            {
                var row = AddProductToQueue(scan.Product, initialQty: 1);
                ActivePreviewItem = row;
                ProductSearchText = string.Empty;
                IsProductDropdownOpen = false;
                SetStatus($"Scanned & added \"{scan.Product.Name}\" to print queue.");
                return;
            }
        }
    }

    [RelayCommand]
    public void SelectSearchProduct(BarcodeProductLookupDto? product)
    {
        if (product == null)
            return;

        var row = AddProductToQueue(product, initialQty: 1);
        ActivePreviewItem = row;
        ProductSearchText = string.Empty;
        IsProductDropdownOpen = false;
        SetStatus($"Added \"{product.Name}\" to print queue.");
    }

    public BarcodeQueueItemRow AddProductToQueue(
        BarcodeProductLookupDto product,
        int initialQty = 1,
        string? preferredBatchNumber = null,
        DateTime? preferredExpiryDate = null)
    {
        // Check if an identical product + batch already exists in queue
        var existing = PrintQueue.FirstOrDefault(r =>
            r.ProductId == product.Id &&
            (string.IsNullOrWhiteSpace(preferredBatchNumber) ||
             string.Equals(r.BatchNumber, preferredBatchNumber.Trim(), StringComparison.OrdinalIgnoreCase)));

        if (existing != null && string.IsNullOrWhiteSpace(preferredBatchNumber))
        {
            existing.PrintQty += Math.Max(1, initialQty);
            NotifyQueueChanged(existing);
            return existing;
        }

        var newRow = new BarcodeQueueItemRow(
            this,
            product,
            initialQty,
            preferredBatchNumber,
            preferredExpiryDate);

        PrintQueue.Add(newRow);
        ActivePreviewItem ??= newRow;
        NotifyQueueChanged(newRow);
        return newRow;
    }

    public void RemoveQueueItem(BarcodeQueueItemRow row)
    {
        var wasActive = ReferenceEquals(ActivePreviewItem, row);
        PrintQueue.Remove(row);
        if (wasActive)
        {
            ActivePreviewItem = PrintQueue.FirstOrDefault();
        }
        NotifyQueueChanged();
    }

    public void SetActivePreviewItem(BarcodeQueueItemRow row)
    {
        ActivePreviewItem = row;
    }

    [RelayCommand]
    public void FillAllStockQuantities()
    {
        foreach (var row in PrintQueue)
        {
            var targetQty = !row.IsCustomBatchSelected && row.BatchStockQty > 0
                ? row.BatchStockQty
                : (row.TotalProductStock > 0 ? row.TotalProductStock : 1);
            row.PrintQty = Math.Max(1, targetQty);
        }
        NotifyQueueChanged();
        SetStatus("Filled sticker quantities from on-hand stock.");
    }

    [RelayCommand]
    public void ResetAllQuantitiesToOne()
    {
        foreach (var row in PrintQueue)
            row.PrintQty = 1;
        NotifyQueueChanged();
        SetStatus("Reset all queue quantities to 1.");
    }

    [RelayCommand]
    public void ClearQueue()
    {
        PrintQueue.Clear();
        ActivePreviewItem = null;
        NotifyQueueChanged();
        SetStatus("Cleared barcode print queue.");
    }

    [RelayCommand]
    public async Task RefreshStudioAsync()
    {
        await InitializeAsync();
        SetStatus("Refreshed barcode templates and catalog.");
    }

    public void NotifyQueueChanged(BarcodeQueueItemRow? changedRow = null)
    {
        OnPropertyChanged(nameof(QueueProductsCount));
        OnPropertyChanged(nameof(TotalLabelsToPrint));
        OnPropertyChanged(nameof(HasQueueItems));
        OnPropertyChanged(nameof(IsQueueEmpty));
        OnPropertyChanged(nameof(HasMultipleQueueItems));
        OnPropertyChanged(nameof(PrintButtonLabel));
        OnPropertyChanged(nameof(QueueSummaryText));

        if (changedRow != null && ReferenceEquals(changedRow, ActivePreviewItem))
        {
            RaisePreviewChanged();
        }
        else if (ActivePreviewItem == null && PrintQueue.Count > 0)
        {
            ActivePreviewItem = PrintQueue[0];
        }
        else
        {
            RaisePreviewChanged();
        }
    }

    // =========================================================================
    // Formatting Helpers
    // =========================================================================

    public string FormatCurrency(decimal amount)
    {
        var decimals = Math.Clamp(CurrencyDecimalPlaces, 0, 2);
        var num = amount.ToString($"N{decimals}", CultureInfo.InvariantCulture);
        if (num.EndsWith(".00", StringComparison.Ordinal))
            num = num[..^3];

        return string.Equals(CurrencyPosition, "after", StringComparison.OrdinalIgnoreCase)
            ? $"{num} {CurrencySymbol}"
            : $"{CurrencySymbol} {num}";
    }

    public static string FormatExpiryDate(DateTime date, string? format)
    {
        return (format?.ToUpperInvariant()) switch
        {
            "MM/YYYY" => date.ToString("MM/yyyy", CultureInfo.InvariantCulture),
            "DD-MM-YYYY" => date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            _ => date.ToString("MM/yy", CultureInfo.InvariantCulture)
        };
    }

    private void SetStatus(string message)
    {
        ErrorMessage = string.Empty;
        StatusMessage = message;
    }
}
