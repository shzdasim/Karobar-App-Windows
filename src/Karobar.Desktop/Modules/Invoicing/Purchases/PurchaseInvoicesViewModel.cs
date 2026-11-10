using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;
using Karobar.Domain.Formulas;

namespace Karobar.Desktop.ViewModels;

public sealed class PurchaseSelectOption
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

public sealed class PurchaseSupplierFilterOption
{
    public long? SupplierId { get; init; }
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

public sealed class PurchaseBatchSelectOption
{
    public const string UnassignedLabel = "[Unassigned Stock]";

    public long Id { get; init; }
    public string BatchNumber { get; init; } = string.Empty;
    public DateTime? ExpiryDate { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPurchasePrice { get; init; }
    public decimal UnitSalePrice { get; init; }
    public decimal WholeSaleUnitPrice { get; init; }
    public bool IsUnassigned { get; init; }

    public string DisplayLabel => IsUnassigned
        ? UnassignedLabel
        : BatchNumber;

    public override string ToString() => DisplayLabel;
}

public partial class PurchaseInvoiceLineRowViewModel : ObservableObject
{
    private readonly Action<PurchaseInvoiceLineRowViewModel> _onRecalculated;
    private bool _suppressRecalc;

    public PurchaseInvoiceLineRowViewModel(int rowNumber, Action<PurchaseInvoiceLineRowViewModel> onRecalculated)
    {
        _rowNumber = rowNumber;
        _onRecalculated = onRecalculated;
        ResetBatchOptions([]);
    }

    public Guid RowUid { get; } = Guid.NewGuid();
    public int OriginalEntrySequence { get; set; }

    [ObservableProperty]
    private int _rowNumber;

    [ObservableProperty]
    private long? _id;

    [ObservableProperty]
    private long? _purchaseOrderItemId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProduct))]
    [NotifyPropertyChangedFor(nameof(ProductDisplayText))]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private long _productId;

    public bool HasProduct => ProductId > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductDisplayText))]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private string _productCode = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductDisplayText))]
    private string _productName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private string _brandName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private string _rack = string.Empty;

    [ObservableProperty]
    private long? _storageLocationId;

    [ObservableProperty]
    private int _currentStock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private int _packSize = 1;

    public string ProductDisplayText => HasProduct
        ? ProductName
        : "Search product…";

    public string ProductMetaText
    {
        get
        {
            if (!HasProduct)
                return "Scan barcode or search by name / SKU / brand";

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ProductCode)) parts.Add(ProductCode);
            if (!string.IsNullOrWhiteSpace(BrandName)) parts.Add(BrandName);
            parts.Add($"1×{Math.Max(1, PackSize)}");
            if (!string.IsNullOrWhiteSpace(Rack)) parts.Add($"Rack: {Rack}");
            return string.Join(" • ", parts);
        }
    }

    public ObservableCollection<PurchaseBatchSelectOption> BatchOptions { get; } = [];

    [ObservableProperty]
    private PurchaseBatchSelectOption? _selectedBatchOption;

    [ObservableProperty]
    private string _batchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBatchWithoutExpiryWarning))]
    private string _batchNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBatchWithoutExpiryWarning))]
    private DateTime? _expiryDate;

    public bool HasBatchWithoutExpiryWarning =>
        HasProduct && !string.IsNullOrWhiteSpace(BatchNumber) && !ExpiryDate.HasValue;

    // Editable cell strings (bound with UpdateSourceTrigger=PropertyChanged)
    [ObservableProperty] private string _packQtyText = "0";
    [ObservableProperty] private string _unitQtyText = "0";
    [ObservableProperty] private string _packPurchaseText = "0.00";
    [ObservableProperty] private string _unitPurchaseText = "0.00";
    [ObservableProperty] private string _itemDiscountText = "0";
    [ObservableProperty] private string _packBonusText = "0";
    [ObservableProperty] private string _unitBonusText = "0";
    [ObservableProperty] private string _packSaleText = "0.00";
    [ObservableProperty] private string _unitSaleText = "0.00";
    [ObservableProperty] private string _wholeSalePackText = "0.00";
    [ObservableProperty] private string _wholeSaleUnitText = "0.00";

    // Underlying numeric values
    public decimal PackQuantity { get; private set; }
    public int UnitQuantity { get; private set; }
    public decimal PackPurchasePrice { get; private set; }
    public decimal UnitPurchasePrice { get; private set; }
    public decimal ItemDiscountPercentage { get; private set; }
    public decimal PackBonus { get; private set; }
    public int UnitBonus { get; private set; }
    public decimal PackSalePrice { get; private set; }
    public decimal UnitSalePrice { get; private set; }
    public decimal WholeSalePackPrice { get; private set; }
    public decimal WholeSaleUnitPrice { get; private set; }

    [ObservableProperty]
    private int _totalQuantity;

    [ObservableProperty]
    private decimal _avgPrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarginDisplayText))]
    [NotifyPropertyChangedFor(nameof(IsZeroOrNegativeMargin))]
    private decimal? _margin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WholeSaleMarginDisplayText))]
    private decimal? _wholeSaleMargin;

    [ObservableProperty]
    private decimal _subTotal;

    public string MarginDisplayText => Margin.HasValue ? $"{Margin.Value:0.##}%" : "—";
    public string WholeSaleMarginDisplayText => WholeSaleMargin.HasValue ? $"{WholeSaleMargin.Value:0.##}%" : "—";
    public bool IsZeroOrNegativeMargin => HasProduct && (!Margin.HasValue || Margin.Value <= 0m);

    partial void OnSelectedBatchOptionChanged(PurchaseBatchSelectOption? value)
    {
        if (_suppressRecalc || value == null)
            return;

        _suppressRecalc = true;
        try
        {
            if (value.IsUnassigned)
            {
                BatchNumber = string.Empty;
                BatchText = PurchaseBatchSelectOption.UnassignedLabel;
                ExpiryDate = null;
            }
            else
            {
                BatchNumber = value.BatchNumber;
                BatchText = value.DisplayLabel;
                if (value.ExpiryDate.HasValue)
                {
                    ExpiryDate = value.ExpiryDate.Value.Date;
                }
            }
        }
        finally
        {
            _suppressRecalc = false;
        }
    }

    partial void OnBatchTextChanged(string value)
    {
        if (_suppressRecalc)
            return;

        var clean = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(clean) ||
            string.Equals(clean, PurchaseBatchSelectOption.UnassignedLabel, StringComparison.OrdinalIgnoreCase))
        {
            BatchNumber = string.Empty;
            return;
        }

        var matched = BatchOptions.FirstOrDefault(o =>
            !o.IsUnassigned &&
            (string.Equals(o.DisplayLabel, clean, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(o.BatchNumber, clean, StringComparison.OrdinalIgnoreCase)));

        if (matched != null)
        {
            BatchNumber = matched.BatchNumber;
            if (matched.ExpiryDate.HasValue && !ExpiryDate.HasValue)
            {
                ExpiryDate = matched.ExpiryDate.Value.Date;
            }
        }
        else
        {
            BatchNumber = clean;
        }
    }

    partial void OnPackQtyTextChanged(string value) => HandleFieldEdited("pack_quantity");
    partial void OnUnitQtyTextChanged(string value) => HandleFieldEdited("unit_quantity");
    partial void OnPackPurchaseTextChanged(string value) => HandleFieldEdited("pack_purchase_price");
    partial void OnUnitPurchaseTextChanged(string value) => HandleFieldEdited("unit_purchase_price");
    partial void OnItemDiscountTextChanged(string value) => HandleFieldEdited("item_discount_percentage");
    partial void OnPackBonusTextChanged(string value) => HandleFieldEdited("pack_bonus");
    partial void OnUnitBonusTextChanged(string value) => HandleFieldEdited("unit_bonus");
    partial void OnPackSaleTextChanged(string value) => HandleFieldEdited("pack_sale_price");
    partial void OnUnitSaleTextChanged(string value) => HandleFieldEdited("unit_sale_price");
    partial void OnWholeSalePackTextChanged(string value) => HandleFieldEdited("whole_sale_pack_price");
    partial void OnWholeSaleUnitTextChanged(string value) => HandleFieldEdited("whole_sale_unit_price");

    private void HandleFieldEdited(string changedField)
    {
        if (_suppressRecalc)
            return;

        var prevPackSale = PackSalePrice;

        if (changedField == "pack_quantity") PackQuantity = ParseNonNegDecimal(PackQtyText);
        else if (changedField == "unit_quantity") UnitQuantity = ParseNonNegInt(UnitQtyText);
        else if (changedField == "pack_purchase_price") PackPurchasePrice = ParseNonNegDecimal(PackPurchaseText);
        else if (changedField == "unit_purchase_price") UnitPurchasePrice = ParseNonNegDecimal(UnitPurchaseText);
        else if (changedField == "item_discount_percentage") ItemDiscountPercentage = Math.Clamp(ParseNonNegDecimal(ItemDiscountText), 0m, 100m);
        else if (changedField == "pack_bonus") PackBonus = ParseNonNegDecimal(PackBonusText);
        else if (changedField == "unit_bonus") UnitBonus = ParseNonNegInt(UnitBonusText);
        else if (changedField == "pack_sale_price") PackSalePrice = ParseNonNegDecimal(PackSaleText);
        else if (changedField == "unit_sale_price") UnitSalePrice = ParseNonNegDecimal(UnitSaleText);
        else if (changedField == "whole_sale_pack_price") WholeSalePackPrice = ParseNonNegDecimal(WholeSalePackText);
        else if (changedField == "whole_sale_unit_price") WholeSaleUnitPrice = ParseNonNegDecimal(WholeSaleUnitText);

        var calc = BuildCalcModel();
        InvoiceCalculators.RecalcPurchaseItem(calc, changedField, prevPackSale);
        ApplyCalcResult(calc, activeEditedField: changedField);
        _onRecalculated(this);
    }

    public void ApplyProductSelection(PurchaseProductSearchItemDto product)
    {
        _suppressRecalc = true;
        try
        {
            ProductId = product.Id;
            ProductCode = product.ProductCode;
            ProductName = product.Name;
            BrandName = product.BrandName;
            Rack = product.Rack;
            StorageLocationId = product.StorageLocationId;
            CurrentStock = product.Quantity;
            PackSize = Math.Max(1, product.PackSize);

            ResetBatchOptions(product.Batches);

            // Default pricing: supplier last purchase cost if available, else product catalog cost
            var effUnitCost = product.LastSupplierUnitCost is > 0m
                ? product.LastSupplierUnitCost.Value
                : product.UnitPurchasePrice;
            var effPackCost = product.LastSupplierUnitCost is > 0m
                ? Math.Round(product.LastSupplierUnitCost.Value * PackSize, 2, MidpointRounding.AwayFromZero)
                : (product.PackPurchasePrice > 0m ? product.PackPurchasePrice : Math.Round(effUnitCost * PackSize, 2, MidpointRounding.AwayFromZero));

            var packSale = product.PackSalePrice > 0m
                ? product.PackSalePrice
                : Math.Round(product.UnitSalePrice * PackSize, 2, MidpointRounding.AwayFromZero);
            var unitSale = product.UnitSalePrice > 0m
                ? product.UnitSalePrice
                : (PackSize > 0 ? packSale / PackSize : 0m);

            var wsPack = product.WholeSalePackPrice > 0m ? product.WholeSalePackPrice : packSale;
            var wsUnit = product.WholeSaleUnitPrice > 0m ? product.WholeSaleUnitPrice : unitSale;

            if (PackQuantity <= 0m && UnitQuantity <= 0)
            {
                PackQuantity = 1m;
                UnitQuantity = PackSize;
            }
            else
            {
                UnitQuantity = (int)Math.Round(PackQuantity * PackSize, 0, MidpointRounding.AwayFromZero);
            }

            PackPurchasePrice = effPackCost;
            UnitPurchasePrice = effUnitCost;
            PackSalePrice = packSale;
            UnitSalePrice = unitSale;
            WholeSalePackPrice = wsPack;
            WholeSaleUnitPrice = wsUnit;

            if (product.Batches.Count > 0)
            {
                var latest = product.Batches[^1];
                var opt = BatchOptions.FirstOrDefault(b => !b.IsUnassigned && b.Id == latest.Id);
                SelectedBatchOption = opt;
                BatchNumber = latest.BatchNumber;
                BatchText = opt?.DisplayLabel ?? latest.BatchNumber;
                ExpiryDate = latest.ExpiryDate?.Date;
            }
            else
            {
                SelectedBatchOption = BatchOptions.FirstOrDefault(b => b.IsUnassigned);
                BatchNumber = string.Empty;
                BatchText = PurchaseBatchSelectOption.UnassignedLabel;
                ExpiryDate = null;
            }

            var calc = BuildCalcModel();
            InvoiceCalculators.RecalcPurchaseItem(calc, "pack_quantity");
            ApplyCalcResult(calc, activeEditedField: null);
        }
        finally
        {
            _suppressRecalc = false;
        }

        _onRecalculated(this);
    }

    public void LoadFromDto(PurchaseInvoiceLineItemDto dto)
    {
        _suppressRecalc = true;
        try
        {
            Id = dto.Id;
            PurchaseOrderItemId = dto.PurchaseOrderItemId;
            ProductId = dto.ProductId;
            ProductCode = dto.ProductCode;
            ProductName = dto.ProductName;
            BrandName = dto.BrandName;
            Rack = dto.Rack;
            StorageLocationId = dto.StorageLocationId;
            PackSize = Math.Max(1, dto.PackSize);

            ResetBatchOptions(dto.AvailableBatches);

            var cleanBatch = (dto.Batch ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(cleanBatch))
            {
                SelectedBatchOption = BatchOptions.FirstOrDefault(b => b.IsUnassigned);
                BatchNumber = string.Empty;
                BatchText = PurchaseBatchSelectOption.UnassignedLabel;
                ExpiryDate = null;
            }
            else
            {
                var existingOpt = BatchOptions.FirstOrDefault(b =>
                    !b.IsUnassigned && string.Equals(b.BatchNumber, cleanBatch, StringComparison.OrdinalIgnoreCase));
                SelectedBatchOption = existingOpt;
                BatchNumber = cleanBatch;
                BatchText = existingOpt?.DisplayLabel ?? cleanBatch;
                ExpiryDate = dto.Expiry?.Date;
            }

            PackQuantity = dto.PackQuantity;
            UnitQuantity = dto.UnitQuantity;
            PackPurchasePrice = dto.PackPurchasePrice;
            UnitPurchasePrice = dto.UnitPurchasePrice;
            ItemDiscountPercentage = dto.ItemDiscountPercentage;
            PackBonus = dto.PackBonus;
            UnitBonus = dto.UnitBonus;
            PackSalePrice = dto.PackSalePrice;
            UnitSalePrice = dto.UnitSalePrice;
            WholeSalePackPrice = dto.WholeSalePackPrice > 0m ? dto.WholeSalePackPrice : dto.PackSalePrice;
            WholeSaleUnitPrice = dto.WholeSaleUnitPrice > 0m ? dto.WholeSaleUnitPrice : dto.UnitSalePrice;

            var calc = BuildCalcModel();
            InvoiceCalculators.RecalcPurchaseItem(calc, "pack_size");
            ApplyCalcResult(calc, activeEditedField: null);
        }
        finally
        {
            _suppressRecalc = false;
        }
    }

    public void LoadFromUpsertLine(PurchaseInvoiceUpsertLineRequest req, PurchaseProductSearchItemDto? productLookup)
    {
        _suppressRecalc = true;
        try
        {
            PurchaseOrderItemId = req.PurchaseOrderItemId;
            ProductId = req.ProductId;
            ProductCode = productLookup?.ProductCode ?? string.Empty;
            ProductName = productLookup?.Name ?? (req.ProductId > 0 ? $"Product #{req.ProductId}" : string.Empty);
            BrandName = productLookup?.BrandName ?? string.Empty;
            Rack = productLookup?.Rack ?? string.Empty;
            StorageLocationId = req.StorageLocationId ?? productLookup?.StorageLocationId;
            CurrentStock = productLookup?.Quantity ?? 0;
            PackSize = Math.Max(1, req.PackSize > 0 ? req.PackSize : (productLookup?.PackSize ?? 1));

            ResetBatchOptions(productLookup?.Batches ?? []);

            var cleanBatch = (req.Batch ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(cleanBatch))
            {
                SelectedBatchOption = BatchOptions.FirstOrDefault(b => b.IsUnassigned);
                BatchNumber = string.Empty;
                BatchText = PurchaseBatchSelectOption.UnassignedLabel;
                ExpiryDate = null;
            }
            else
            {
                var existingOpt = BatchOptions.FirstOrDefault(b =>
                    !b.IsUnassigned && string.Equals(b.BatchNumber, cleanBatch, StringComparison.OrdinalIgnoreCase));
                SelectedBatchOption = existingOpt;
                BatchNumber = cleanBatch;
                BatchText = existingOpt?.DisplayLabel ?? cleanBatch;
                ExpiryDate = req.Expiry?.Date;
            }

            PackQuantity = req.PackQuantity;
            UnitQuantity = req.UnitQuantity;
            PackPurchasePrice = req.PackPurchasePrice;
            UnitPurchasePrice = req.UnitPurchasePrice;
            ItemDiscountPercentage = req.ItemDiscountPercentage;
            PackBonus = req.PackBonus;
            UnitBonus = req.UnitBonus;
            PackSalePrice = req.PackSalePrice;
            UnitSalePrice = req.UnitSalePrice;
            WholeSalePackPrice = req.WholeSalePackPrice;
            WholeSaleUnitPrice = req.WholeSaleUnitPrice;

            var calc = BuildCalcModel();
            InvoiceCalculators.RecalcPurchaseItem(calc, "pack_size");
            ApplyCalcResult(calc, activeEditedField: null);
        }
        finally
        {
            _suppressRecalc = false;
        }
    }

    public void NormalizeDisplayTexts()
    {
        _suppressRecalc = true;
        try
        {
            var calc = BuildCalcModel();
            InvoiceCalculators.RecalcPurchaseItem(calc, "pack_size");
            ApplyCalcResult(calc, activeEditedField: null);
        }
        finally
        {
            _suppressRecalc = false;
        }
    }

    public PurchaseLineCalcModel BuildCalcModel() => new()
    {
        PackSize = Math.Max(1, PackSize),
        PackQuantity = PackQuantity,
        UnitQuantity = UnitQuantity,
        PackPurchasePrice = PackPurchasePrice,
        UnitPurchasePrice = UnitPurchasePrice,
        ItemDiscountPercentage = ItemDiscountPercentage,
        PackBonus = PackBonus,
        UnitBonus = UnitBonus,
        PackSalePrice = PackSalePrice,
        UnitSalePrice = UnitSalePrice,
        WholeSalePackPrice = WholeSalePackPrice,
        WholeSaleUnitPrice = WholeSaleUnitPrice
    };

    public PurchaseInvoiceUpsertLineRequest ToUpsertLineRequest() => new()
    {
        PurchaseOrderItemId = PurchaseOrderItemId,
        ProductId = ProductId,
        StorageLocationId = StorageLocationId,
        PackSize = Math.Max(1, PackSize),
        Batch = string.IsNullOrWhiteSpace(BatchNumber) ? null : BatchNumber.Trim(),
        Expiry = string.IsNullOrWhiteSpace(BatchNumber) ? null : ExpiryDate?.Date,
        PackQuantity = PackQuantity,
        UnitQuantity = UnitQuantity,
        PackPurchasePrice = PackPurchasePrice,
        UnitPurchasePrice = UnitPurchasePrice,
        ItemDiscountPercentage = ItemDiscountPercentage,
        PackBonus = PackBonus,
        UnitBonus = UnitBonus,
        PackSalePrice = PackSalePrice,
        UnitSalePrice = UnitSalePrice,
        WholeSalePackPrice = WholeSalePackPrice,
        WholeSaleUnitPrice = WholeSaleUnitPrice,
        Margin = Margin,
        WholeSaleMargin = WholeSaleMargin,
        AvgPrice = AvgPrice,
        SubTotal = SubTotal,
        Quantity = TotalQuantity
    };

    private void ResetBatchOptions(List<PurchaseProductBatchOptionDto> batches)
    {
        BatchOptions.Clear();
        BatchOptions.Add(new PurchaseBatchSelectOption
        {
            IsUnassigned = true,
            BatchNumber = string.Empty
        });

        foreach (var b in batches)
        {
            if (string.IsNullOrWhiteSpace(b.BatchNumber))
                continue;

            BatchOptions.Add(new PurchaseBatchSelectOption
            {
                Id = b.Id,
                BatchNumber = b.BatchNumber,
                ExpiryDate = b.ExpiryDate,
                Quantity = b.Quantity,
                UnitPurchasePrice = b.UnitPurchasePrice,
                UnitSalePrice = b.UnitSalePrice,
                WholeSaleUnitPrice = b.WholeSaleUnitPrice,
                IsUnassigned = false
            });
        }
    }

    private void ApplyCalcResult(PurchaseLineCalcModel calc, string? activeEditedField)
    {
        PackQuantity = calc.PackQuantity;
        UnitQuantity = calc.UnitQuantity;
        PackPurchasePrice = calc.PackPurchasePrice;
        UnitPurchasePrice = calc.UnitPurchasePrice;
        ItemDiscountPercentage = calc.ItemDiscountPercentage;
        PackBonus = calc.PackBonus;
        UnitBonus = calc.UnitBonus;
        PackSalePrice = calc.PackSalePrice;
        UnitSalePrice = calc.UnitSalePrice;
        WholeSalePackPrice = calc.WholeSalePackPrice;
        WholeSaleUnitPrice = calc.WholeSaleUnitPrice;
        TotalQuantity = calc.Quantity;
        AvgPrice = calc.AvgPrice;
        Margin = calc.Margin;
        WholeSaleMargin = calc.WholeSaleMargin;
        SubTotal = calc.SubTotal;

        _suppressRecalc = true;
        try
        {
            if (activeEditedField != "pack_quantity") PackQtyText = FormatQty(PackQuantity);
            if (activeEditedField != "unit_quantity") UnitQtyText = UnitQuantity.ToString(CultureInfo.InvariantCulture);
            if (activeEditedField != "pack_purchase_price") PackPurchaseText = FormatMoney(PackPurchasePrice);
            if (activeEditedField != "unit_purchase_price") UnitPurchaseText = FormatRate(UnitPurchasePrice);
            if (activeEditedField != "item_discount_percentage") ItemDiscountText = FormatQty(ItemDiscountPercentage);
            if (activeEditedField != "pack_bonus") PackBonusText = FormatQty(PackBonus);
            if (activeEditedField != "unit_bonus") UnitBonusText = UnitBonus.ToString(CultureInfo.InvariantCulture);
            if (activeEditedField != "pack_sale_price") PackSaleText = FormatMoney(PackSalePrice);
            if (activeEditedField != "unit_sale_price") UnitSaleText = FormatRate(UnitSalePrice);
            if (activeEditedField != "whole_sale_pack_price") WholeSalePackText = FormatMoney(WholeSalePackPrice);
            if (activeEditedField != "whole_sale_unit_price") WholeSaleUnitText = FormatRate(WholeSaleUnitPrice);
        }
        finally
        {
            _suppressRecalc = false;
        }
    }

    private static string FormatQty(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatRate(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.00##", CultureInfo.InvariantCulture);

    private static decimal ParseNonNegDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0m;
        var clean = raw.Trim().Replace(",", "");
        return decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d > 0m
            ? d
            : 0m;
    }

    private static int ParseNonNegInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;
        var clean = raw.Trim().Replace(",", "");
        if (int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return Math.Max(0, i);
        if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return Math.Max(0, (int)Math.Round(d, 0, MidpointRounding.AwayFromZero));
        return 0;
    }
}

public partial class PurchaseInvoicesViewModel : ObservableObject
{
    private readonly IPurchaseInvoiceService _purchaseService;
    private readonly IProductService? _productService;
    private readonly IToastNotificationService _toast;
    private readonly Action<string>? _navigate;

    private bool _suppressDirectoryReload;
    private bool _suppressFooterRecalc;
    private bool _paidTouched;
    private int _nextEntrySequence = 1;
    private CancellationTokenSource? _directoryDebounceCts;
    private CancellationTokenSource? _productSearchDebounceCts;
    private List<PurchaseAccountLookupDto> _allAccounts = [];

    // Focus & Printing callbacks wired by PurchaseInvoicesView.xaml.cs
    public Action? RequestFocusSupplierSelect { get; set; }
    public Action? RequestFocusSupplierModalSearch { get; set; }
    public Action? RequestFocusInvoiceNumber { get; set; }
    public Action? RequestFocusBillAmount { get; set; }
    public Action? RequestFocusDirectorySearch { get; set; }
    public Action? RequestFocusProductModalSearch { get; set; }
    public Action? RequestFocusQuickAddProductName { get; set; }
    public Action? RequestApplyProductSearchModalLayout { get; set; }
    public Action? RequestApplyQuickAddProductModalLayout { get; set; }
    public Action<int, string>? RequestFocusLineCell { get; set; }
    public Action<string>? RequestFocusFooterField { get; set; }
    public Action<PurchaseInvoiceDetailDto, bool>? RequestPrintDocument { get; set; }

    public PurchaseInvoicesViewModel(
        IPurchaseInvoiceService purchaseService,
        IToastNotificationService toast,
        Action<string>? navigate = null,
        IProductService? productService = null)
    {
        _purchaseService = purchaseService;
        _productService = productService;
        _toast = toast;
        _navigate = navigate;

        InitializeStaticOptions();
    }

    // =========================================================================
    // 1. WORKSPACE MODE & STORE SETTINGS
    // =========================================================================

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirectoryMode))]
    [NotifyPropertyChangedFor(nameof(IsFormMode))]
    [NotifyPropertyChangedFor(nameof(IsDetailMode))]
    private string _activeMode = "directory"; // directory | form | detail

    public bool IsDirectoryMode => string.Equals(ActiveMode, "directory", StringComparison.OrdinalIgnoreCase);
    public bool IsFormMode => string.Equals(ActiveMode, "form", StringComparison.OrdinalIgnoreCase);
    public bool IsDetailMode => string.Equals(ActiveMode, "detail", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWholesale))]
    [NotifyPropertyChangedFor(nameof(IsPharmacyShop))]
    [NotifyPropertyChangedFor(nameof(WholesaleEditColWidth))]
    [NotifyPropertyChangedFor(nameof(WholesaleReadOnlyColWidth))]
    private PurchaseStoreSettingsDto _storeSettings = new();

    public bool HasWholesale => StoreSettings.HasWholesale;
    public bool IsPharmacyShop => StoreSettings.IsPharmacy;
    public string CurrencySymbol => StoreSettings.CurrencySymbol;
    public System.Windows.GridLength WholesaleEditColWidth => HasWholesale
        ? new System.Windows.GridLength(0.90, System.Windows.GridUnitType.Star)
        : new System.Windows.GridLength(0);
    public System.Windows.GridLength WholesaleReadOnlyColWidth => HasWholesale
        ? new System.Windows.GridLength(0.76, System.Windows.GridUnitType.Star)
        : new System.Windows.GridLength(0);

    // =========================================================================
    // 2. DIRECTORY MODE STATE & FILTERS
    // =========================================================================

    public ObservableCollection<PurchaseInvoiceDirectoryRowDto> DirectoryItems { get; } = [];
    public ObservableCollection<PurchaseSupplierFilterOption> DirectorySupplierOptions { get; } = [];
    public ObservableCollection<PurchaseSelectOption> DirectoryTypeOptions { get; } = [];
    public ObservableCollection<PurchaseSelectOption> DirectoryStatusOptions { get; } = [];
    public ObservableCollection<PurchaseSelectOption> PageSizeOptions { get; } = [];

    [ObservableProperty]
    private string _directorySearchText = string.Empty;

    [ObservableProperty]
    private string _directorySupplierSearchText = string.Empty;

    [ObservableProperty]
    private PurchaseSupplierFilterOption? _selectedDirectorySupplierOption;

    [ObservableProperty]
    private PurchaseSelectOption? _selectedDirectoryTypeOption;

    [ObservableProperty]
    private PurchaseSelectOption? _selectedDirectoryStatusOption;

    [ObservableProperty]
    private DateTime? _directoryFromDate;

    [ObservableProperty]
    private DateTime? _directoryToDate;

    [ObservableProperty]
    private PurchaseSelectOption? _selectedPageSizeOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPrevPage))]
    [NotifyPropertyChangedFor(nameof(CanNextPage))]
    [NotifyPropertyChangedFor(nameof(PaginationSummaryText))]
    [NotifyPropertyChangedFor(nameof(DirectoryRangeSubtitleText))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPrevPage))]
    [NotifyPropertyChangedFor(nameof(CanNextPage))]
    [NotifyPropertyChangedFor(nameof(PaginationSummaryText))]
    private int _totalPages = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaginationSummaryText))]
    [NotifyPropertyChangedFor(nameof(DirectoryRangeSubtitleText))]
    [NotifyPropertyChangedFor(nameof(DirectoryTotalSubtitleText))]
    [NotifyPropertyChangedFor(nameof(HasDirectoryItems))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyDirectoryState))]
    private int _totalDirectoryCount;

    public bool HasDirectoryItems => DirectoryItems.Count > 0;
    public bool ShowEmptyDirectoryState => !IsBusy && DirectoryItems.Count == 0;
    public bool CanPrevPage => CurrentPage > 1;
    public bool CanNextPage => CurrentPage < TotalPages;
    public string PaginationSummaryText => $"Page {CurrentPage} of {TotalPages} ({TotalDirectoryCount:N0} total)";
    public string DirectoryTotalSubtitleText => $"{TotalDirectoryCount:N0} invoices";
    public string DirectoryRangeSubtitleText
    {
        get
        {
            if (TotalDirectoryCount <= 0 || DirectoryItems.Count == 0)
                return "0 of 0";
            var ps = int.TryParse(SelectedPageSizeOption?.Key, out var n) ? n : 10;
            var start = ((CurrentPage - 1) * ps) + 1;
            var end = Math.Min(TotalDirectoryCount, start + DirectoryItems.Count - 1);
            return $"{start}-{end} of {TotalDirectoryCount}";
        }
    }

    // KPI Cards
    [ObservableProperty] private int _kpiPostedCount;
    [ObservableProperty] private decimal _kpiTotalAmount;
    [ObservableProperty] private decimal _kpiPaidAmount;
    [ObservableProperty] private decimal _kpiRemainingPayable;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeldDrafts))]
    [NotifyPropertyChangedFor(nameof(HeldDraftsBadgeText))]
    private int _heldDraftsCount;

    public bool HasHeldDrafts => HeldDraftsCount > 0;
    public string HeldDraftsBadgeText => HeldDraftsCount > 0 ? $"Held Drafts ({HeldDraftsCount})" : "Held Drafts";

    partial void OnDirectorySearchTextChanged(string value)
    {
        if (_suppressDirectoryReload) return;
        DebounceReloadDirectory();
    }

    partial void OnDirectorySupplierSearchTextChanged(string value)
    {
        if (_suppressDirectoryReload) return;
        DebounceReloadDirectory();
    }

    partial void OnSelectedDirectorySupplierOptionChanged(PurchaseSupplierFilterOption? value)
    {
        if (_suppressDirectoryReload) return;
        CurrentPage = 1;
        _ = RefreshDirectoryAsync();
    }

    partial void OnSelectedDirectoryTypeOptionChanged(PurchaseSelectOption? value)
    {
        if (_suppressDirectoryReload) return;
        CurrentPage = 1;
        _ = RefreshDirectoryAsync();
    }

    partial void OnSelectedDirectoryStatusOptionChanged(PurchaseSelectOption? value)
    {
        if (_suppressDirectoryReload) return;
        CurrentPage = 1;
        _ = RefreshDirectoryAsync();
    }

    partial void OnDirectoryFromDateChanged(DateTime? value)
    {
        if (_suppressDirectoryReload) return;
        CurrentPage = 1;
        _ = RefreshDirectoryAsync();
    }

    partial void OnDirectoryToDateChanged(DateTime? value)
    {
        if (_suppressDirectoryReload) return;
        CurrentPage = 1;
        _ = RefreshDirectoryAsync();
    }

    partial void OnSelectedPageSizeOptionChanged(PurchaseSelectOption? value)
    {
        if (_suppressDirectoryReload || value == null) return;
        CurrentPage = 1;
        _ = RefreshDirectoryAsync();
    }

    // =========================================================================
    // 3. CREATE / EDIT FORM STATE
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditFormMode))]
    [NotifyPropertyChangedFor(nameof(FormHeaderTitle))]
    [NotifyPropertyChangedFor(nameof(SaveButtonLabel))]
    private long? _editingInvoiceId;

    public bool IsEditFormMode => EditingInvoiceId is > 0;
    public string FormHeaderTitle => IsEditFormMode
        ? $"Edit Purchase Invoice ({PostedNumber})"
        : "Purchase Invoice";
    public string SaveButtonLabel => IsEditFormMode
        ? "Update Invoice"
        : "Save Purchase Invoice";

    [ObservableProperty]
    private long? _activeDraftId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveDraftBanner))]
    private string _activeDraftNumber = string.Empty;

    public bool HasActiveDraftBanner => !string.IsNullOrWhiteSpace(ActiveDraftNumber);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLinkedToPurchaseOrder))]
    private long? _linkedPurchaseOrderId;

    [ObservableProperty]
    private string _linkedPurchaseOrderNumber = string.Empty;

    [ObservableProperty]
    private string _linkedReceiptKey = string.Empty;

    public bool IsLinkedToPurchaseOrder => LinkedPurchaseOrderId is > 0;

    public ObservableCollection<PurchaseSupplierLookupDto> SupplierOptions { get; } = [];
    public ObservableCollection<ReceivablePurchaseOrderLookupDto> ReceivableOrderOptions { get; } = [];
    public ObservableCollection<PurchaseSelectOption> PaymentMethodOptions { get; } = [];
    public ObservableCollection<PurchaseAccountLookupDto> FilteredAccountOptions { get; } = [];
    public ObservableCollection<PurchaseSelectOption> ArrangementOptions { get; } = [];
    public ObservableCollection<PurchaseInvoiceLineRowViewModel> Lines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormHeaderTitle))]
    private string _postedNumber = "PRINV-0001";

    [ObservableProperty]
    private DateTime _postedDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSupplier))]
    [NotifyPropertyChangedFor(nameof(SelectedSupplierButtonText))]
    private PurchaseSupplierLookupDto? _selectedSupplierOption;

    public bool HasSelectedSupplier => SelectedSupplierOption != null;
    public string SelectedSupplierButtonText => SelectedSupplierOption != null
        ? SelectedSupplierOption.Name
        : "Click to search supplier...";

    [ObservableProperty]
    private ReceivablePurchaseOrderLookupDto? _selectedReceivableOrderOption;

    [ObservableProperty]
    private string _invoiceNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BillAmount))]
    [NotifyPropertyChangedFor(nameof(BillDifference))]
    [NotifyPropertyChangedFor(nameof(FormattedBillDifference))]
    [NotifyPropertyChangedFor(nameof(IsBillDifferenceZero))]
    [NotifyPropertyChangedFor(nameof(HasBillDiffWarning))]
    [NotifyPropertyChangedFor(nameof(IsBillDiffExceedingTolerance))]
    private string _billAmountText = string.Empty;

    public decimal BillAmount => ParseDecimalSafe(BillAmountText);
    public decimal BillDifference => Math.Round(BillAmount - TotalAmount, 2, MidpointRounding.AwayFromZero);
    public string FormattedBillDifference =>
        (!string.IsNullOrWhiteSpace(BillAmountText) || TotalAmount > 0m)
            ? BillDifference.ToString("0.00", CultureInfo.InvariantCulture)
            : "—";
    public bool IsBillDifferenceZero => Math.Abs(BillDifference) < 0.009m;
    public bool HasBillDiffWarning => (!string.IsNullOrWhiteSpace(BillAmountText) || TotalAmount > 0m) && !IsBillDifferenceZero;
    public bool IsBillDiffExceedingTolerance => Math.Abs(BillDifference) > 5m;

    [ObservableProperty]
    private string _remarks = string.Empty;

    [ObservableProperty]
    private string _editReason = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDebitInvoice))]
    [NotifyPropertyChangedFor(nameof(IsCreditInvoice))]
    [NotifyPropertyChangedFor(nameof(ShowSupplierCreditBar))]
    [NotifyPropertyChangedFor(nameof(RemainingPayableAmount))]
    [NotifyPropertyChangedFor(nameof(LedgerCreditExcessAmount))]
    [NotifyPropertyChangedFor(nameof(HasLedgerCreditExcess))]
    [NotifyPropertyChangedFor(nameof(ProjectedSupplierPayable))]
    [NotifyPropertyChangedFor(nameof(IsCreditLimitExceeded))]
    private string _invoiceType = "debit"; // debit | credit

    public bool IsDebitInvoice => !IsCreditInvoice;
    public bool IsCreditInvoice => string.Equals(InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private DateTime? _dueDate = DateTime.Today.AddDays(30);

    // Supplier Credit Summary
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSupplierCreditBar))]
    [NotifyPropertyChangedFor(nameof(AvailableSupplierCredit))]
    [NotifyPropertyChangedFor(nameof(ProjectedSupplierPayable))]
    [NotifyPropertyChangedFor(nameof(IsCreditLimitExceeded))]
    private PurchaseSupplierLookupDto? _activeSupplierCredit;

    public bool ShowSupplierCreditBar =>
        ActiveSupplierCredit != null &&
        (IsCreditInvoice || ActiveSupplierCredit.OverdueCount > 0 || IsCreditLimitExceeded);

    public decimal AvailableSupplierCredit => ActiveSupplierCredit is { CreditLimit: > 0m } s
        ? Math.Max(0m, Math.Round(s.CreditLimit - s.CurrentPayable, 2))
        : 0m;

    public decimal ProjectedSupplierPayable =>
        Math.Round((ActiveSupplierCredit?.CurrentPayable ?? 0m) + RemainingPayableAmount, 2);

    public bool IsCreditLimitExceeded =>
        IsCreditInvoice &&
        ActiveSupplierCredit is { CreditLimit: > 0m } s &&
        ProjectedSupplierPayable > s.CreditLimit;

    // Arrangement
    [ObservableProperty]
    private PurchaseSelectOption? _selectedArrangementOption;

    // Footer Calculations & Payment
    [ObservableProperty] private decimal _rowsTotal;
    [ObservableProperty] private string _discountPctText = "0";
    [ObservableProperty] private string _discountAmtText = "0.00";
    [ObservableProperty] private string _taxPctText = "0";
    [ObservableProperty] private string _taxAmtText = "0.00";

    public decimal DiscountPercentage { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxPercentage { get; private set; }
    public decimal TaxAmount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BillDifference))]
    [NotifyPropertyChangedFor(nameof(FormattedBillDifference))]
    [NotifyPropertyChangedFor(nameof(IsBillDifferenceZero))]
    [NotifyPropertyChangedFor(nameof(HasBillDiffWarning))]
    [NotifyPropertyChangedFor(nameof(IsBillDiffExceedingTolerance))]
    [NotifyPropertyChangedFor(nameof(RemainingPayableAmount))]
    [NotifyPropertyChangedFor(nameof(IsRemainingPositive))]
    [NotifyPropertyChangedFor(nameof(LedgerCreditExcessAmount))]
    [NotifyPropertyChangedFor(nameof(HasLedgerCreditExcess))]
    [NotifyPropertyChangedFor(nameof(ProjectedSupplierPayable))]
    [NotifyPropertyChangedFor(nameof(IsCreditLimitExceeded))]
    private decimal _totalAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPaid))]
    [NotifyPropertyChangedFor(nameof(RemainingPayableAmount))]
    [NotifyPropertyChangedFor(nameof(IsRemainingPositive))]
    [NotifyPropertyChangedFor(nameof(LedgerCreditExcessAmount))]
    [NotifyPropertyChangedFor(nameof(HasLedgerCreditExcess))]
    [NotifyPropertyChangedFor(nameof(ProjectedSupplierPayable))]
    [NotifyPropertyChangedFor(nameof(IsCreditLimitExceeded))]
    [NotifyPropertyChangedFor(nameof(ShowRelinkPaidButton))]
    private string _totalPaidText = "0.00";

    public decimal TotalPaid => ParseDecimalSafe(TotalPaidText);

    [ObservableProperty]
    private decimal _allocatedPaymentsOnEdit;

    public bool ShowRelinkPaidButton => IsDebitInvoice && _paidTouched && Math.Abs(TotalPaid - TotalAmount) > 0.009m;

    public decimal RemainingPayableAmount => Math.Max(0m, Math.Round(TotalAmount - TotalPaid, 2));
    public bool IsRemainingPositive => RemainingPayableAmount > 0.009m;
    public bool HasDiscountSummary => DiscountAmount > 0.009m;
    public bool HasTaxSummary => TaxAmount > 0.009m;

    public decimal LedgerCreditExcessAmount => IsCreditInvoice && TotalPaid > TotalAmount
        ? Math.Round(TotalPaid - TotalAmount, 2)
        : 0m;

    public bool HasLedgerCreditExcess => LedgerCreditExcessAmount > 0.009m;

    [ObservableProperty]
    private PurchaseSelectOption? _selectedPaymentMethodOption;

    [ObservableProperty]
    private PurchaseAccountLookupDto? _selectedAccountOption;

    [ObservableProperty]
    private bool _hasOpenCashDrawer;

    [ObservableProperty]
    private decimal _openCashDrawerBalance;

    public ObservableCollection<string> ValidationErrors { get; } = [];
    public bool HasValidationErrors => ValidationErrors.Count > 0;

    public int ActiveProductLinesCount => Lines.Count(l => l.HasProduct);
    public int TotalReceivedUnitsCount => Lines.Where(l => l.HasProduct).Sum(l => l.TotalQuantity);

    partial void OnPostedDateChanged(DateTime value)
    {
        if (SelectedSupplierOption != null && !IsEditFormMode)
        {
            DueDate = value.Date.AddDays(Math.Max(0, SelectedSupplierOption.CreditDays));
        }
    }

    partial void OnSelectedSupplierOptionChanged(PurchaseSupplierLookupDto? value)
    {
        ActiveSupplierCredit = value;
        if (value != null)
        {
            DueDate = PostedDate.Date.AddDays(Math.Max(0, value.CreditDays));
            _ = RefreshSupplierCreditSummaryAsync(value.Id);
        }
    }

    partial void OnSelectedReceivableOrderOptionChanged(ReceivablePurchaseOrderLookupDto? value)
    {
        if (value != null && value.Id > 0)
        {
            _ = LoadFromPurchaseOrderAsync(value.Id);
        }
    }

    partial void OnSelectedArrangementOptionChanged(PurchaseSelectOption? value)
    {
        if (value != null)
        {
            ApplyRowArrangement(value.Key);
        }
    }

    partial void OnSelectedPaymentMethodOptionChanged(PurchaseSelectOption? value)
    {
        RefreshFilteredAccounts(value?.Key ?? "cash", preserveCurrentAccountId: SelectedAccountOption?.Id);
    }

    partial void OnDiscountPctTextChanged(string value)
    {
        if (_suppressFooterRecalc) return;
        DiscountPercentage = Math.Clamp(ParseDecimalSafe(value), 0m, 100m);
        RecalculateFooter("discount_percentage");
    }

    partial void OnDiscountAmtTextChanged(string value)
    {
        if (_suppressFooterRecalc) return;
        DiscountAmount = Math.Max(0m, ParseDecimalSafe(value));
        RecalculateFooter("discount_amount");
    }

    partial void OnTaxPctTextChanged(string value)
    {
        if (_suppressFooterRecalc) return;
        TaxPercentage = Math.Max(0m, ParseDecimalSafe(value));
        RecalculateFooter("tax_percentage");
    }

    partial void OnTaxAmtTextChanged(string value)
    {
        if (_suppressFooterRecalc) return;
        TaxAmount = Math.Max(0m, ParseDecimalSafe(value));
        RecalculateFooter("tax_amount");
    }

    partial void OnTotalPaidTextChanged(string value)
    {
        if (_suppressFooterRecalc) return;
        _paidTouched = true;
        OnPropertyChanged(nameof(ShowRelinkPaidButton));
    }

    // =========================================================================
    // 4. DETAIL / SHOW MODE STATE
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentDetail))]
    [NotifyPropertyChangedFor(nameof(CanMutateCurrentDetail))]
    private PurchaseInvoiceDetailDto? _currentDetail;

    public bool HasCurrentDetail => CurrentDetail != null;
    public bool CanMutateCurrentDetail =>
        CurrentDetail != null &&
        !string.Equals(CurrentDetail.Status, "reversed", StringComparison.OrdinalIgnoreCase);

    // =========================================================================
    // 5. MODALS STATE (Supplier Search, Product Search, Quick Add Product, Drafts, Pre-Shift, History, Reverse, Delete)
    // =========================================================================

    // A0. Select Supplier Modal (SupplierSearch.jsx parity)
    [ObservableProperty]
    private bool _isSupplierSearchModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSupplierSearchQuery))]
    [NotifyPropertyChangedFor(nameof(SupplierModalFooterCountText))]
    private string _supplierSearchQuery = string.Empty;

    public ObservableCollection<PurchaseSupplierLookupDto> FilteredSupplierModalResults { get; } = [];

    [ObservableProperty]
    private PurchaseSupplierLookupDto? _highlightedModalSupplier;

    public bool HasSupplierSearchQuery => !string.IsNullOrWhiteSpace(SupplierSearchQuery);
    public bool ShowEmptySupplierResults => FilteredSupplierModalResults.Count == 0;
    public string SupplierModalFooterCountText => HasSupplierSearchQuery
        ? $"{FilteredSupplierModalResults.Count} results for \"{SupplierSearchQuery.Trim()}\""
        : "All suppliers";

    partial void OnSupplierSearchQueryChanged(string value)
    {
        if (!IsSupplierSearchModalOpen) return;
        RefreshSupplierModalResults(value);
    }

    // A. Fast Product Search Modal (ProductSearchInput.jsx parity)
    private static readonly Regex ValidProductSearchRegex = new(@"^[a-zA-Z0-9\-\.()/+%,&_:\s]*$", RegexOptions.Compiled);
    private string _lastValidProductSearchQuery = string.Empty;
    private bool _suppressProductSearchQueryValidation;

    public PurchaseModalWindowLayoutDto ProductSearchModalLayout { get; private set; } = new() { Width = 960, Height = 600 };
    public PurchaseModalWindowLayoutDto QuickAddProductModalLayout { get; private set; } = new() { Width = 800, Height = 600 };

    [ObservableProperty]
    private bool _isProductSearchModalOpen;

    [ObservableProperty]
    private int _activeSearchLineIndex;

    [ObservableProperty]
    private bool _isProductSearchInputInvalid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProductSearchQuery))]
    [NotifyPropertyChangedFor(nameof(ShowAllProductsFooterText))]
    [NotifyPropertyChangedFor(nameof(ProductSearchFooterCountText))]
    [NotifyPropertyChangedFor(nameof(ProductSearchTrimmedQueryText))]
    private string _productSearchQuery = string.Empty;

    public ObservableCollection<PurchaseProductSearchItemDto> ProductSearchResults { get; } = [];

    [ObservableProperty]
    private PurchaseProductSearchItemDto? _highlightedSearchProduct;

    public bool HasProductSearchQuery => !string.IsNullOrWhiteSpace(ProductSearchQuery);
    public bool ShowAllProductsFooterText => !HasProductSearchQuery;
    public bool HasProductSearchResults => ProductSearchResults.Count > 0;
    public bool ShowEmptyProductSearchState => ProductSearchResults.Count == 0;
    public int ProductSearchResultsCount => ProductSearchResults.Count;
    public string ProductSearchTrimmedQueryText => $"\"{(ProductSearchQuery ?? string.Empty).Trim()}\"";
    public string ProductSearchFooterCountText => HasProductSearchQuery
        ? $"{ProductSearchResults.Count} results for \"{ProductSearchQuery.Trim()}\""
        : "All products";

    partial void OnProductSearchQueryChanged(string value)
    {
        if (_suppressProductSearchQueryValidation)
            return;

        var candidate = value ?? string.Empty;
        if (!ValidProductSearchRegex.IsMatch(candidate))
        {
            _suppressProductSearchQueryValidation = true;
            ProductSearchQuery = _lastValidProductSearchQuery;
            _suppressProductSearchQueryValidation = false;
            _ = TriggerProductSearchInvalidFlashAsync();
            return;
        }

        _lastValidProductSearchQuery = candidate;
        if (!IsProductSearchModalOpen) return;
        DebounceProductSearch(candidate);
    }

    private async Task TriggerProductSearchInvalidFlashAsync()
    {
        IsProductSearchInputInvalid = true;
        await Task.Delay(200);
        IsProductSearchInputInvalid = false;
    }

    // A2. Quick "+ Add Product" Catalog Creation Modal (ProductFormModal.jsx parity)
    [ObservableProperty]
    private bool _isQuickAddProductModalOpen;

    [ObservableProperty] private string _newProdCode = string.Empty;
    [ObservableProperty] private string _newProdBarcode = string.Empty;
    [ObservableProperty] private string _newProdRack = string.Empty;
    [ObservableProperty] private string _newProdName = string.Empty;
    [ObservableProperty] private string _newProdFormulation = string.Empty;
    [ObservableProperty] private string _newProdPackSizeText = "1";
    [ObservableProperty] private LookupOptionDto? _selectedNewProdCategory;
    [ObservableProperty] private LookupOptionDto? _selectedNewProdBrand;
    [ObservableProperty] private LookupOptionDto? _selectedNewProdSupplier;
    [ObservableProperty] private FormulationOptionDto? _selectedNewProdFormulationOption;
    [ObservableProperty] private string _newProdDescription = string.Empty;
    [ObservableProperty] private bool _newProdIsNarcotic;
    [ObservableProperty] private string _newProdError = string.Empty;

    public ObservableCollection<LookupOptionDto> QuickProductCategories { get; } = [];
    public ObservableCollection<LookupOptionDto> QuickProductBrands { get; } = [];
    public ObservableCollection<LookupOptionDto> QuickProductSuppliers { get; } = [];
    public ObservableCollection<FormulationOptionDto> QuickProductFormulations { get; } = [];

    partial void OnSelectedNewProdFormulationOptionChanged(FormulationOptionDto? value)
    {
        if (value != null && !string.IsNullOrWhiteSpace(value.Name))
        {
            NewProdFormulation = value.Name;
        }
    }

    // B. Held Drafts Modal
    [ObservableProperty]
    private bool _isDraftsModalOpen;

    public ObservableCollection<HeldPurchaseDraftSummaryDto> HeldDrafts { get; } = [];

    // C. Pre-Shift Cash Drawer Prompt Modal
    [ObservableProperty]
    private bool _isPreShiftPromptOpen;

    [ObservableProperty]
    private string _preShiftPromptMessage = string.Empty;

    // D. Document Audit History Modal
    [ObservableProperty]
    private bool _isHistoryModalOpen;

    [ObservableProperty]
    private string _historyModalTitle = "Document Audit History";

    public ObservableCollection<PurchaseDocumentHistoryEntryDto> HistoryEntries { get; } = [];

    // E. Reverse / Void Invoice Modal
    [ObservableProperty]
    private bool _isReverseModalOpen;

    [ObservableProperty]
    private long _reverseTargetInvoiceId;

    [ObservableProperty]
    private string _reverseTargetPostedNumber = string.Empty;

    [ObservableProperty]
    private string _reverseReason = string.Empty;

    [ObservableProperty]
    private string _reverseModalError = string.Empty;

    // F. 2-Step Password Delete Modal
    [ObservableProperty]
    private bool _isDeleteModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteStep1))]
    [NotifyPropertyChangedFor(nameof(IsDeleteStep2))]
    private int _deleteStep = 1;

    public bool IsDeleteStep1 => DeleteStep == 1;
    public bool IsDeleteStep2 => DeleteStep == 2;

    [ObservableProperty]
    private long _deleteTargetInvoiceId;

    [ObservableProperty]
    private string _deleteTargetPostedNumber = string.Empty;

    [ObservableProperty]
    private string _deletePassword = string.Empty;

    [ObservableProperty]
    private string _deleteModalError = string.Empty;

    public bool HasAnyModalOpen =>
        IsSupplierSearchModalOpen ||
        IsProductSearchModalOpen ||
        IsQuickAddProductModalOpen ||
        IsDraftsModalOpen ||
        IsPreShiftPromptOpen ||
        IsHistoryModalOpen ||
        IsReverseModalOpen ||
        IsDeleteModalOpen;

    // =========================================================================
    // 6. NAVIGATION & ENTRY POINTS
    // =========================================================================

    public async Task OpenDirectoryModeAsync()
    {
        CloseAllModals();
        ActiveMode = "directory";
        await RefreshDirectoryLookupsAndPageAsync();
    }

    public async Task OpenCreateModeAsync(long? purchaseOrderId = null)
    {
        CloseAllModals();
        IsBusy = true;
        try
        {
            var boot = await _purchaseService.GetFormBootstrapAsync();
            ApplyBootstrap(boot);

            EditingInvoiceId = null;
            AllocatedPaymentsOnEdit = 0m;
            ActiveDraftId = null;
            ActiveDraftNumber = string.Empty;
            LinkedPurchaseOrderId = null;
            LinkedPurchaseOrderNumber = string.Empty;
            LinkedReceiptKey = string.Empty;
            SelectedReceivableOrderOption = null;

            PostedNumber = boot.NextPostedNumber;
            PostedDate = DateTime.Today;
            SelectedSupplierOption = null;
            ActiveSupplierCredit = null;
            InvoiceNumber = string.Empty;
            BillAmountText = string.Empty;
            Remarks = string.Empty;
            EditReason = string.Empty;
            InvoiceType = "debit";
            DueDate = DateTime.Today.AddDays(30);

            _suppressFooterRecalc = true;
            DiscountPercentage = 0m;
            DiscountAmount = 0m;
            TaxPercentage = 0m;
            TaxAmount = 0m;
            DiscountPctText = "0";
            DiscountAmtText = "0.00";
            TaxPctText = "0";
            TaxAmtText = "0.00";
            TotalAmount = 0m;
            TotalPaidText = "0.00";
            _suppressFooterRecalc = false;
            _paidTouched = false;

            SelectedPaymentMethodOption = PaymentMethodOptions[0];
            RefreshFilteredAccounts("cash", null);

            Lines.Clear();
            _nextEntrySequence = 1;
            AppendEmptyLine();

            ClearValidationErrors();
            ActiveMode = "form";

            if (purchaseOrderId is > 0)
            {
                await LoadFromPurchaseOrderAsync(purchaseOrderId.Value);
            }
            else
            {
                RequestFocusSupplierSelect?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _toast.ShowError(ex.Message, "Failed to Open Purchase Form");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenEditModeAsync(long invoiceId)
    {
        CloseAllModals();
        IsBusy = true;
        try
        {
            var boot = await _purchaseService.GetFormBootstrapAsync();
            var detail = await _purchaseService.GetByIdAsync(invoiceId);
            if (detail == null)
            {
                _toast.ShowError($"Purchase invoice #{invoiceId} was not found.", "Not Found");
                await OpenDirectoryModeAsync();
                return;
            }

            if (string.Equals(detail.Status, "reversed", StringComparison.OrdinalIgnoreCase))
            {
                _toast.ShowWarning("Reversed purchase invoices are read-only and cannot be edited.", "Reversed Invoice");
                await OpenDetailModeAsync(invoiceId);
                return;
            }

            ApplyBootstrap(boot);

            EditingInvoiceId = detail.Id;
            AllocatedPaymentsOnEdit = detail.AllocatedPaymentsAmount;
            ActiveDraftId = null;
            ActiveDraftNumber = string.Empty;
            LinkedPurchaseOrderId = detail.PurchaseOrderId;
            LinkedPurchaseOrderNumber = detail.PurchaseOrderNumber ?? string.Empty;
            LinkedReceiptKey = detail.ReceiptKey ?? string.Empty;

            PostedNumber = detail.PostedNumber;
            PostedDate = detail.PostedDate.Date;
            SelectedSupplierOption = SupplierOptions.FirstOrDefault(s => s.Id == detail.SupplierId);
            DueDate = detail.DueDate?.Date ?? detail.PostedDate.Date.AddDays(detail.CreditDays ?? 30);
            InvoiceNumber = detail.InvoiceNumber;
            BillAmountText = detail.InvoiceAmount.ToString("0.00", CultureInfo.InvariantCulture);
            Remarks = detail.Remarks;
            EditReason = string.Empty;
            InvoiceType = string.Equals(detail.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase) ? "credit" : "debit";

            Lines.Clear();
            _nextEntrySequence = 1;
            foreach (var itemDto in detail.Items)
            {
                var rowVm = new PurchaseInvoiceLineRowViewModel(Lines.Count + 1, OnLineRecalculated)
                {
                    OriginalEntrySequence = _nextEntrySequence++
                };
                rowVm.LoadFromDto(itemDto);
                Lines.Add(rowVm);
            }
            if (Lines.Count == 0)
            {
                AppendEmptyLine();
            }

            _suppressFooterRecalc = true;
            DiscountPercentage = detail.DiscountPercentage;
            DiscountAmount = detail.DiscountAmount;
            TaxPercentage = detail.TaxPercentage;
            TaxAmount = detail.TaxAmount;
            DiscountPctText = detail.DiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            DiscountAmtText = detail.DiscountAmount.ToString("0.00", CultureInfo.InvariantCulture);
            TaxPctText = detail.TaxPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            TaxAmtText = detail.TaxAmount.ToString("0.00", CultureInfo.InvariantCulture);
            _suppressFooterRecalc = false;

            RecalculateFooter(null);

            _suppressFooterRecalc = true;
            TotalPaidText = detail.DirectPaidAmount.ToString("0.00", CultureInfo.InvariantCulture);
            _suppressFooterRecalc = false;
            _paidTouched = IsCreditInvoice || Math.Abs(detail.TotalPaid - detail.TotalAmount) > 0.009m;

            var pmOpt = PaymentMethodOptions.FirstOrDefault(o =>
                string.Equals(o.Key, detail.PaymentMethod, StringComparison.OrdinalIgnoreCase)) ?? PaymentMethodOptions[0];
            SelectedPaymentMethodOption = pmOpt;
            RefreshFilteredAccounts(pmOpt.Key, detail.AccountId);

            ClearValidationErrors();
            NotifyLinesAggregateChanged();
            ActiveMode = "form";
        }
        catch (Exception ex)
        {
            _toast.ShowError(ex.Message, "Failed to Load Invoice");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenDetailModeAsync(long invoiceId)
    {
        CloseAllModals();
        IsBusy = true;
        try
        {
            var boot = await _purchaseService.GetFormBootstrapAsync();
            StoreSettings = boot.StoreSettings;
            OnPropertyChanged(nameof(CurrencySymbol));

            var detail = await _purchaseService.GetByIdAsync(invoiceId);
            if (detail == null)
            {
                _toast.ShowError($"Purchase invoice #{invoiceId} was not found.", "Not Found");
                await OpenDirectoryModeAsync();
                return;
            }

            CurrentDetail = detail;
            ActiveMode = "detail";
        }
        catch (Exception ex)
        {
            _toast.ShowError(ex.Message, "Failed to Load Invoice Detail");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // 7. DIRECTORY COMMANDS
    // =========================================================================

    [RelayCommand]
    public async Task RefreshDirectoryAsync()
    {
        IsBusy = true;
        try
        {
            var pageSize = int.TryParse(SelectedPageSizeOption?.Key, out var ps) ? ps : 10;
            var query = new PurchaseInvoiceFilterQuery
            {
                Search = DirectorySearchText,
                SupplierSearch = DirectorySupplierSearchText,
                SupplierId = SelectedDirectorySupplierOption?.SupplierId,
                InvoiceType = SelectedDirectoryTypeOption?.Key ?? "all",
                Status = SelectedDirectoryStatusOption?.Key ?? "all",
                FromDate = DirectoryFromDate,
                ToDate = DirectoryToDate,
                Page = CurrentPage,
                PageSize = pageSize
            };

            var page = await _purchaseService.GetDirectoryPageAsync(query);

            DirectoryItems.Clear();
            foreach (var item in page.Items)
            {
                DirectoryItems.Add(item);
            }

            TotalDirectoryCount = page.TotalCount;
            CurrentPage = page.Page;
            TotalPages = page.TotalPages;

            KpiPostedCount = page.TotalPostedCount;
            KpiTotalAmount = page.TotalPostedAmount;
            KpiPaidAmount = page.TotalPaidAmount;
            KpiRemainingPayable = page.TotalRemainingPayable;
            HeldDraftsCount = page.HeldDraftsCount;

            OnPropertyChanged(nameof(HasDirectoryItems));
            OnPropertyChanged(nameof(ShowEmptyDirectoryState));
            OnPropertyChanged(nameof(DirectoryRangeSubtitleText));
        }
        catch (Exception ex)
        {
            _toast.ShowError(ex.Message, "Directory Load Failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ResetDirectoryFilters()
    {
        _suppressDirectoryReload = true;
        DirectorySearchText = string.Empty;
        DirectorySupplierSearchText = string.Empty;
        SelectedDirectorySupplierOption = DirectorySupplierOptions.FirstOrDefault();
        SelectedDirectoryTypeOption = DirectoryTypeOptions.FirstOrDefault();
        SelectedDirectoryStatusOption = DirectoryStatusOptions.FirstOrDefault();
        DirectoryFromDate = null;
        DirectoryToDate = null;
        CurrentPage = 1;
        _suppressDirectoryReload = false;

        _ = RefreshDirectoryAsync();
    }

    [RelayCommand]
    public void FirstPage()
    {
        if (!CanPrevPage) return;
        CurrentPage = 1;
        _ = RefreshDirectoryAsync();
    }

    [RelayCommand]
    public void PrevPage()
    {
        if (!CanPrevPage) return;
        CurrentPage--;
        _ = RefreshDirectoryAsync();
    }

    [RelayCommand]
    public void NextPage()
    {
        if (!CanNextPage) return;
        CurrentPage++;
        _ = RefreshDirectoryAsync();
    }

    [RelayCommand]
    public void LastPage()
    {
        if (!CanNextPage) return;
        CurrentPage = TotalPages;
        _ = RefreshDirectoryAsync();
    }

    [RelayCommand]
    public void GoToCreateForm()
    {
        if (_navigate != null)
            _navigate("/purchase-invoices/create");
        else
            _ = OpenCreateModeAsync();
    }

    [RelayCommand]
    public void BackToDirectory()
    {
        if (_navigate != null)
            _navigate("/purchase-invoices");
        else
            _ = OpenDirectoryModeAsync();
    }

    [RelayCommand]
    public void ViewInvoice(PurchaseInvoiceDirectoryRowDto? row)
    {
        if (row == null) return;
        if (_navigate != null)
            _navigate($"/purchase-invoices/{row.Id}");
        else
            _ = OpenDetailModeAsync(row.Id);
    }

    [RelayCommand]
    public void EditInvoiceFromRow(PurchaseInvoiceDirectoryRowDto? row)
    {
        if (row == null) return;
        if (row.IsReversed)
        {
            _toast.ShowWarning("Cannot edit a reversed invoice.", "Reversed Invoice");
            return;
        }
        if (_navigate != null)
            _navigate($"/purchase-invoices/edit/{row.Id}");
        else
            _ = OpenEditModeAsync(row.Id);
    }

    [RelayCommand]
    public void EditCurrentDetail()
    {
        if (CurrentDetail == null) return;
        if (_navigate != null)
            _navigate($"/purchase-invoices/edit/{CurrentDetail.Id}");
        else
            _ = OpenEditModeAsync(CurrentDetail.Id);
    }

    [RelayCommand]
    public void SendToBarcodeStudioFromRow(PurchaseInvoiceDirectoryRowDto? row)
    {
        if (row == null) return;
        _navigate?.Invoke($"/barcode-labels?purchase_invoice_id={row.Id}");
    }

    [RelayCommand]
    public void SendCurrentDetailToBarcodeStudio()
    {
        if (CurrentDetail == null) return;
        _navigate?.Invoke($"/barcode-labels?purchase_invoice_id={CurrentDetail.Id}");
    }

    [RelayCommand]
    public async Task PrintInvoiceA4FromRowAsync(PurchaseInvoiceDirectoryRowDto? row)
    {
        if (row == null) return;
        var detail = await _purchaseService.GetByIdAsync(row.Id);
        if (detail != null)
            RequestPrintDocument?.Invoke(detail, false);
    }

    [RelayCommand]
    public async Task PrintInvoiceThermalFromRowAsync(PurchaseInvoiceDirectoryRowDto? row)
    {
        if (row == null) return;
        var detail = await _purchaseService.GetByIdAsync(row.Id);
        if (detail != null)
            RequestPrintDocument?.Invoke(detail, true);
    }

    [RelayCommand]
    public void PrintCurrentDetailA4()
    {
        if (CurrentDetail != null)
            RequestPrintDocument?.Invoke(CurrentDetail, false);
    }

    [RelayCommand]
    public void PrintCurrentDetailThermal()
    {
        if (CurrentDetail != null)
            RequestPrintDocument?.Invoke(CurrentDetail, true);
    }

    // =========================================================================
    // 8. FORM HEADER, LINE ITEMS, ARRANGEMENT & FOOTER COMMANDS
    // =========================================================================

    [RelayCommand]
    public void SetInvoiceType(string? mode)
    {
        var clean = string.Equals(mode, "credit", StringComparison.OrdinalIgnoreCase) ? "credit" : "debit";
        if (string.Equals(InvoiceType, clean, StringComparison.OrdinalIgnoreCase))
            return;

        InvoiceType = clean;
        _suppressFooterRecalc = true;
        if (clean == "credit")
        {
            TotalPaidText = "0.00";
            _paidTouched = true;
            if (SelectedSupplierOption != null)
            {
                DueDate = PostedDate.Date.AddDays(Math.Max(0, SelectedSupplierOption.CreditDays));
            }
        }
        else
        {
            TotalPaidText = TotalAmount.ToString("0.00", CultureInfo.InvariantCulture);
            _paidTouched = false;
        }
        _suppressFooterRecalc = false;

        OnPropertyChanged(nameof(ShowRelinkPaidButton));
        OnPropertyChanged(nameof(RemainingPayableAmount));
        OnPropertyChanged(nameof(LedgerCreditExcessAmount));
        OnPropertyChanged(nameof(HasLedgerCreditExcess));
    }

    [RelayCommand]
    public void ToggleInvoiceType()
    {
        SetInvoiceType(IsCreditInvoice ? "debit" : "credit");
    }

    [RelayCommand]
    public void MatchBillAmountToTotal()
    {
        BillAmountText = TotalAmount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    public void RelinkPaidToTotal()
    {
        _paidTouched = false;
        _suppressFooterRecalc = true;
        TotalPaidText = TotalAmount.ToString("0.00", CultureInfo.InvariantCulture);
        _suppressFooterRecalc = false;
        OnPropertyChanged(nameof(ShowRelinkPaidButton));
    }

    [RelayCommand]
    public void AppendEmptyLine()
    {
        var row = new PurchaseInvoiceLineRowViewModel(Lines.Count + 1, OnLineRecalculated)
        {
            OriginalEntrySequence = _nextEntrySequence++
        };
        Lines.Add(row);
        RenumberLines();
        NotifyLinesAggregateChanged();
    }

    [RelayCommand]
    public void RemoveLine(PurchaseInvoiceLineRowViewModel? row)
    {
        if (row == null) return;
        Lines.Remove(row);
        if (Lines.Count == 0)
        {
            AppendEmptyLine();
        }
        else
        {
            RenumberLines();
            RecalculateFooter(null);
            NotifyLinesAggregateChanged();
        }
    }

    [RelayCommand]
    public void MoveLineUp(PurchaseInvoiceLineRowViewModel? row)
    {
        if (row == null) return;
        var idx = Lines.IndexOf(row);
        if (idx <= 0) return;

        Lines.Move(idx, idx - 1);
        RenumberLines();
    }

    [RelayCommand]
    public void MoveLineDown(PurchaseInvoiceLineRowViewModel? row)
    {
        if (row == null) return;
        var idx = Lines.IndexOf(row);
        if (idx < 0 || idx >= Lines.Count - 1) return;
        if (!Lines[idx + 1].HasProduct) return; // keep empty rows pinned at bottom

        Lines.Move(idx, idx + 1);
        RenumberLines();
    }

    public void ApplyRowArrangement(string modeKey)
    {
        var filled = Lines.Where(l => l.HasProduct).ToList();
        var empties = Lines.Where(l => !l.HasProduct).ToList();
        if (filled.Count <= 1)
            return;

        List<PurchaseInvoiceLineRowViewModel> sorted = modeKey.ToLowerInvariant() switch
        {
            "name_asc" => filled.OrderBy(l => l.ProductName, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.OriginalEntrySequence).ToList(),
            "name_desc" => filled.OrderByDescending(l => l.ProductName, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.OriginalEntrySequence).ToList(),
            "brand_asc" => filled.OrderBy(l => string.IsNullOrWhiteSpace(l.BrandName) ? "ZZZZ" : l.BrandName, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.ProductName, StringComparer.OrdinalIgnoreCase).ToList(),
            "brand_desc" => filled.OrderByDescending(l => l.BrandName, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.ProductName, StringComparer.OrdinalIgnoreCase).ToList(),
            "brand_code" => filled.OrderBy(l => string.IsNullOrWhiteSpace(l.BrandName) ? "ZZZZ" : l.BrandName, StringComparer.OrdinalIgnoreCase).ThenBy(l => ExtractCodeNumber(l.ProductCode)).ThenBy(l => l.ProductCode, StringComparer.OrdinalIgnoreCase).ToList(),
            "code_asc" => filled.OrderBy(l => ExtractCodeNumber(l.ProductCode)).ThenBy(l => l.ProductCode, StringComparer.OrdinalIgnoreCase).ToList(),
            "code_desc" => filled.OrderByDescending(l => ExtractCodeNumber(l.ProductCode)).ThenByDescending(l => l.ProductCode, StringComparer.OrdinalIgnoreCase).ToList(),
            "packs_asc" => filled.OrderBy(l => l.PackQuantity).ThenBy(l => l.OriginalEntrySequence).ToList(),
            "packs_desc" => filled.OrderByDescending(l => l.PackQuantity).ThenBy(l => l.OriginalEntrySequence).ToList(),
            "amount_asc" => filled.OrderBy(l => l.SubTotal).ThenBy(l => l.OriginalEntrySequence).ToList(),
            "amount_desc" => filled.OrderByDescending(l => l.SubTotal).ThenBy(l => l.OriginalEntrySequence).ToList(),
            "price_asc" => filled.OrderBy(l => l.UnitPurchasePrice).ThenBy(l => l.OriginalEntrySequence).ToList(),
            "price_desc" => filled.OrderByDescending(l => l.UnitPurchasePrice).ThenBy(l => l.OriginalEntrySequence).ToList(),
            "size_asc" => filled.OrderBy(l => l.PackSize).ThenBy(l => l.OriginalEntrySequence).ToList(),
            "size_desc" => filled.OrderByDescending(l => l.PackSize).ThenBy(l => l.OriginalEntrySequence).ToList(),
            "supplier_saved" => SortBySupplierSavedOrder(filled),
            _ => filled.OrderBy(l => l.OriginalEntrySequence).ToList()
        };

        Lines.Clear();
        foreach (var r in sorted) Lines.Add(r);
        foreach (var e in empties) Lines.Add(e);
        RenumberLines();
    }

    private List<PurchaseInvoiceLineRowViewModel> SortBySupplierSavedOrder(List<PurchaseInvoiceLineRowViewModel> filled)
    {
        var orderIds = SelectedSupplierOption?.SavedProductOrderIds ?? [];
        if (orderIds.Count == 0)
            return filled.OrderBy(l => l.OriginalEntrySequence).ToList();

        var indexMap = new Dictionary<long, int>();
        for (int i = 0; i < orderIds.Count; i++)
        {
            indexMap.TryAdd(orderIds[i], i);
        }

        return filled
            .OrderBy(l => indexMap.TryGetValue(l.ProductId, out var pos) ? pos : int.MaxValue)
            .ThenBy(l => l.OriginalEntrySequence)
            .ToList();
    }

    private static long ExtractCodeNumber(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return long.MaxValue;
        var m = Regex.Match(code, @"(\d+)");
        return m.Success && long.TryParse(m.Groups[1].Value, out var n) ? n : long.MaxValue;
    }

    public async Task LoadFromPurchaseOrderAsync(long purchaseOrderId)
    {
        try
        {
            var prepared = await _purchaseService.PreparePurchaseOrderReceiptAsync(purchaseOrderId);
            LinkedPurchaseOrderId = prepared.PurchaseOrderId;
            LinkedPurchaseOrderNumber = prepared.OrderNumber;
            LinkedReceiptKey = prepared.ReceiptKey;

            var supOpt = SupplierOptions.FirstOrDefault(s => s.Id == prepared.SupplierId);
            if (supOpt != null)
            {
                SelectedSupplierOption = supOpt;
            }

            Lines.Clear();
            _nextEntrySequence = 1;
            foreach (var dto in prepared.Lines)
            {
                var rowVm = new PurchaseInvoiceLineRowViewModel(Lines.Count + 1, OnLineRecalculated)
                {
                    OriginalEntrySequence = _nextEntrySequence++
                };
                rowVm.LoadFromDto(dto);
                Lines.Add(rowVm);
            }

            RecalculateFooter(null);
            BillAmountText = TotalAmount.ToString("0.00", CultureInfo.InvariantCulture);
            NotifyLinesAggregateChanged();

            _toast.ShowInfo(
                $"Loaded {prepared.Lines.Count} pending item(s) from {prepared.OrderNumber}.",
                "Purchase Order Loaded");

            RequestFocusInvoiceNumber?.Invoke();
        }
        catch (Exception ex)
        {
            _toast.ShowError(ex.Message, "PO Receipt Error");
        }
    }

    [RelayCommand]
    public void DetachPurchaseOrder()
    {
        LinkedPurchaseOrderId = null;
        LinkedPurchaseOrderNumber = string.Empty;
        LinkedReceiptKey = string.Empty;
        SelectedReceivableOrderOption = null;
    }

    [RelayCommand]
    public void ApplyCurrentArrangement()
    {
        if (SelectedArrangementOption != null)
        {
            ApplyRowArrangement(SelectedArrangementOption.Key);
        }
    }

    [RelayCommand]
    public async Task AppendRowAndOpenProductModalAsync()
    {
        AppendEmptyLine();
        await OpenProductSearchForLineAsync(Lines[^1]);
    }

    // =========================================================================
    // 8B. SELECT SUPPLIER MODAL (SupplierSearch.jsx PARITY)
    // =========================================================================

    [RelayCommand]
    public void OpenSupplierSearchModal(string? seedText = null)
    {
        SupplierSearchQuery = seedText ?? string.Empty;
        RefreshSupplierModalResults(SupplierSearchQuery);
        IsSupplierSearchModalOpen = true;
        RequestFocusSupplierModalSearch?.Invoke();
    }

    [RelayCommand]
    public void ClearSupplierSearchQuery()
    {
        SupplierSearchQuery = string.Empty;
        RequestFocusSupplierModalSearch?.Invoke();
    }

    [RelayCommand]
    public void CloseSupplierSearchModal()
    {
        IsSupplierSearchModalOpen = false;
    }

    public void MoveSupplierModalHighlight(int delta)
    {
        if (FilteredSupplierModalResults.Count == 0)
            return;

        var curIdx = HighlightedModalSupplier != null
            ? FilteredSupplierModalResults.IndexOf(HighlightedModalSupplier)
            : -1;
        var nextIdx = Math.Clamp(curIdx + delta, 0, FilteredSupplierModalResults.Count - 1);
        HighlightedModalSupplier = FilteredSupplierModalResults[nextIdx];
    }

    [RelayCommand]
    public void CommitSupplierModalSelection(PurchaseSupplierLookupDto? explicitSupplier = null)
    {
        var chosen = explicitSupplier ?? HighlightedModalSupplier;
        if (chosen == null)
            return;

        SelectedSupplierOption = chosen;
        IsSupplierSearchModalOpen = false;
        RequestFocusInvoiceNumber?.Invoke();
    }

    private void RefreshSupplierModalResults(string? query)
    {
        var clean = (query ?? string.Empty).Trim();
        List<PurchaseSupplierLookupDto> matches;

        if (string.IsNullOrEmpty(clean))
        {
            matches = SupplierOptions.ToList();
        }
        else
        {
            // Prefix matches first in typing order, then any remaining name matches
            var prefixMatches = SupplierOptions
                .Where(s => s.Name.StartsWith(clean, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var otherMatches = SupplierOptions
                .Where(s => !s.Name.StartsWith(clean, StringComparison.OrdinalIgnoreCase) &&
                            s.Name.Contains(clean, StringComparison.OrdinalIgnoreCase))
                .ToList();
            matches = prefixMatches.Concat(otherMatches).ToList();
        }

        FilteredSupplierModalResults.Clear();
        foreach (var item in matches)
            FilteredSupplierModalResults.Add(item);

        HighlightedModalSupplier = FilteredSupplierModalResults.FirstOrDefault();
        OnPropertyChanged(nameof(ShowEmptySupplierResults));
        OnPropertyChanged(nameof(SupplierModalFooterCountText));
    }

    // =========================================================================
    // 9. PRODUCT SEARCH MODAL & QUICK ADD PRODUCT MODAL
    // =========================================================================

    [RelayCommand]
    public async Task OpenProductSearchForLineAsync(PurchaseInvoiceLineRowViewModel? row)
    {
        await OpenProductSearchWithSeedAsync(row, null);
    }

    public async Task OpenProductSearchWithSeedAsync(PurchaseInvoiceLineRowViewModel? row, string? seedChar)
    {
        var lineIdx = row != null ? Lines.IndexOf(row) : 0;
        if (lineIdx < 0) lineIdx = 0;

        ActiveSearchLineIndex = lineIdx;
        var targetRow = lineIdx < Lines.Count ? Lines[lineIdx] : null;
        var initialQuery = !string.IsNullOrEmpty(seedChar)
            ? seedChar
            : (targetRow is { HasProduct: true } ? targetRow.ProductName : string.Empty);

        _suppressProductSearchQueryValidation = true;
        _lastValidProductSearchQuery = initialQuery;
        ProductSearchQuery = initialQuery;
        _suppressProductSearchQueryValidation = false;

        IsProductSearchModalOpen = true;
        RequestApplyProductSearchModalLayout?.Invoke();
        _productSearchDebounceCts?.Cancel();

        await ExecuteProductSearchAsync(initialQuery);
        RequestFocusProductModalSearch?.Invoke();
    }

    public async Task SaveProductSearchModalLayoutAsync(double x, double y, double width, double height)
    {
        ProductSearchModalLayout = new PurchaseModalWindowLayoutDto
        {
            X = Math.Round(x, 1),
            Y = Math.Round(y, 1),
            Width = Math.Round(Math.Max(640d, width), 1),
            Height = Math.Round(Math.Max(420d, height), 1)
        };

        try
        {
            await _purchaseService.SaveModalWindowLayoutAsync("productSearchModal", ProductSearchModalLayout);
        }
        catch
        {
            // Ignore non-critical preference save failure
        }
    }

    public async Task SaveQuickAddProductModalLayoutAsync(double x, double y, double width, double height)
    {
        QuickAddProductModalLayout = new PurchaseModalWindowLayoutDto
        {
            X = Math.Round(x, 1),
            Y = Math.Round(y, 1),
            Width = Math.Round(Math.Max(600d, width), 1),
            Height = Math.Round(Math.Max(400d, height), 1)
        };

        try
        {
            await _purchaseService.SaveModalWindowLayoutAsync("productModal", QuickAddProductModalLayout);
        }
        catch
        {
            // Ignore non-critical preference save failure
        }
    }

    [RelayCommand]
    public void ClearProductSearchQuery()
    {
        ProductSearchQuery = string.Empty;
        RequestFocusProductModalSearch?.Invoke();
    }

    [RelayCommand]
    public void CloseProductSearchModal()
    {
        IsProductSearchModalOpen = false;
    }

    // Quick "+ Add Product" Modal (ProductFormModal.jsx parity)
    [RelayCommand]
    public async Task OpenQuickAddProductModalAsync()
    {
        NewProdError = string.Empty;
        NewProdName = string.Empty;
        NewProdRack = string.Empty;
        NewProdFormulation = string.Empty;
        NewProdPackSizeText = "1";
        NewProdDescription = string.Empty;
        NewProdIsNarcotic = false;
        SelectedNewProdFormulationOption = null;

        try
        {
            if (_productService != null)
            {
                var lookups = await _productService.GetProductFormLookupsAsync();
                var codes = await _productService.GenerateNewCodeAndBarcodeAsync();

                NewProdCode = codes.ProductCode;
                NewProdBarcode = codes.Barcode;

                QuickProductCategories.Clear();
                foreach (var c in lookups.Categories) QuickProductCategories.Add(c);

                QuickProductBrands.Clear();
                foreach (var b in lookups.Brands) QuickProductBrands.Add(b);

                QuickProductSuppliers.Clear();
                foreach (var s in lookups.Suppliers) QuickProductSuppliers.Add(s);

                QuickProductFormulations.Clear();
                foreach (var f in lookups.Formulations) QuickProductFormulations.Add(f);

                SelectedNewProdCategory = QuickProductCategories.FirstOrDefault();
                SelectedNewProdBrand = QuickProductBrands.FirstOrDefault();
                SelectedNewProdSupplier = SelectedSupplierOption != null
                    ? QuickProductSuppliers.FirstOrDefault(s => s.Id == SelectedSupplierOption.Id) ?? QuickProductSuppliers.FirstOrDefault()
                    : QuickProductSuppliers.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            NewProdError = ex.Message;
        }

        IsQuickAddProductModalOpen = true;
        RequestApplyQuickAddProductModalLayout?.Invoke();
        RequestFocusQuickAddProductName?.Invoke();
    }

    [RelayCommand]
    public async Task RegenerateQuickProductBarcodeAsync()
    {
        if (_productService == null) return;
        try
        {
            var codes = await _productService.GenerateNewCodeAndBarcodeAsync();
            if (!string.IsNullOrWhiteSpace(codes.Barcode))
            {
                NewProdBarcode = codes.Barcode;
            }
        }
        catch (Exception ex)
        {
            _toast.ShowError(ex.Message, "Barcode Generation Failed");
        }
    }

    [RelayCommand]
    public void CloseQuickAddProductModal()
    {
        IsQuickAddProductModalOpen = false;
        NewProdError = string.Empty;
    }

    [RelayCommand]
    public async Task SaveQuickAddProductAsync()
    {
        if (_productService == null)
        {
            NewProdError = "Product service is unavailable.";
            return;
        }

        var cleanName = (NewProdName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(cleanName))
        {
            NewProdError = "Product Name is required.";
            return;
        }

        var packSize = int.TryParse(NewProdPackSizeText?.Trim(), out var ps) && ps > 0 ? ps : 1;

        try
        {
            var res = await _productService.CreateProductAsync(new ProductUpsertRequest
            {
                ProductCode = NewProdCode,
                Barcode = (NewProdBarcode ?? string.Empty).Trim(),
                Name = cleanName,
                Formulation = string.IsNullOrWhiteSpace(NewProdFormulation) ? null : NewProdFormulation.Trim(),
                Rack = string.IsNullOrWhiteSpace(NewProdRack) ? null : NewProdRack.Trim(),
                PackSize = packSize,
                CategoryId = SelectedNewProdCategory?.Id,
                BrandId = SelectedNewProdBrand?.Id,
                SupplierId = SelectedNewProdSupplier?.Id ?? SelectedSupplierOption?.Id,
                Description = string.IsNullOrWhiteSpace(NewProdDescription) ? null : NewProdDescription.Trim(),
                Narcotic = NewProdIsNarcotic ? "yes" : "no"
            });

            if (!res.Succeeded)
            {
                NewProdError = res.Message;
                return;
            }

            IsQuickAddProductModalOpen = false;
            _toast.ShowSuccess($"Product '{cleanName}' created.", "Product Added");

            if (IsProductSearchModalOpen)
            {
                await ExecuteProductSearchAsync(ProductSearchQuery);
            }
        }
        catch (Exception ex)
        {
            NewProdError = ex.Message;
        }
    }

    public void MoveProductSearchHighlight(int delta)
    {
        if (ProductSearchResults.Count == 0)
            return;

        var curIdx = HighlightedSearchProduct != null
            ? ProductSearchResults.IndexOf(HighlightedSearchProduct)
            : -1;
        var nextIdx = Math.Clamp(curIdx + delta, 0, ProductSearchResults.Count - 1);
        HighlightedSearchProduct = ProductSearchResults[nextIdx];
    }

    [RelayCommand]
    public async Task CommitProductSelectionAsync(PurchaseProductSearchItemDto? explicitItem = null)
    {
        var chosen = explicitItem ?? HighlightedSearchProduct;

        // If user scanned an exact barcode and pressed Enter before debounce completed:
        if (chosen == null && !string.IsNullOrWhiteSpace(ProductSearchQuery))
        {
            chosen = await _purchaseService.ScanProductByCodeOrBarcodeAsync(
                ProductSearchQuery.Trim(),
                SelectedSupplierOption?.Id);
        }

        if (chosen == null)
            return;

        if (ActiveSearchLineIndex < 0 || ActiveSearchLineIndex >= Lines.Count)
        {
            AppendEmptyLine();
            ActiveSearchLineIndex = Lines.Count - 1;
        }

        // Check duplicate unbatched/latest batch on another row
        var defaultBatch = chosen.Batches.Count > 0 ? chosen.Batches[^1].BatchNumber : string.Empty;
        var dupRow = Lines
            .Where((r, idx) => idx != ActiveSearchLineIndex && r.ProductId == chosen.Id &&
                               string.Equals(r.BatchNumber, defaultBatch, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (dupRow != null && chosen.Batches.Count == 0)
        {
            _toast.ShowWarning(
                $"'{chosen.Name}' ([Unassigned Stock]) is already on Row #{dupRow.RowNumber}. Enter a new Batch # or adjust Row #{dupRow.RowNumber}.",
                "Duplicate Product Row");
        }

        var targetRow = Lines[ActiveSearchLineIndex];
        targetRow.ApplyProductSelection(chosen);
        IsProductSearchModalOpen = false;
        NotifyLinesAggregateChanged();

        // Exact web app focus jump: if product has batches -> focus Batch, else -> focus Pack.Q!
        var nextCol = chosen.Batches.Count > 0 ? "batch" : "pack_quantity";
        RequestFocusLineCell?.Invoke(ActiveSearchLineIndex, nextCol);
    }

    // =========================================================================
    // 10. SAVE / POST PURCHASE INVOICE & PRE-SHIFT DRAWER PROMPT
    // =========================================================================

    [RelayCommand]
    public async Task SaveInvoiceAsync()
    {
        await SubmitInvoiceInternalAsync(updatePreShiftDrawer: null);
    }

    [RelayCommand]
    public async Task ConfirmPreShiftUpdateDrawerAsync()
    {
        IsPreShiftPromptOpen = false;
        await SubmitInvoiceInternalAsync(updatePreShiftDrawer: true);
    }

    [RelayCommand]
    public async Task ConfirmPreShiftKeepDrawerAsync()
    {
        IsPreShiftPromptOpen = false;
        await SubmitInvoiceInternalAsync(updatePreShiftDrawer: false);
    }

    [RelayCommand]
    public void CancelPreShiftPrompt()
    {
        IsPreShiftPromptOpen = false;
    }

    private async Task SubmitInvoiceInternalAsync(bool? updatePreShiftDrawer)
    {
        if (IsBusy) return;

        foreach (var line in Lines.Where(l => l.HasProduct))
        {
            line.NormalizeDisplayTexts();
        }
        RecalculateFooter(null);

        ClearValidationErrors();

        var request = BuildUpsertRequest(updatePreShiftDrawer);

        // Quick client-side pre-checks with instant focus jump
        if (request.SupplierId <= 0)
        {
            SetValidationFailure("Please select a Supplier before saving.");
            RequestFocusSupplierSelect?.Invoke();
            return;
        }

        if (string.IsNullOrWhiteSpace(request.InvoiceNumber))
        {
            SetValidationFailure("Supplier Invoice # is required.");
            RequestFocusInvoiceNumber?.Invoke();
            return;
        }

        if (request.Items.Count == 0)
        {
            SetValidationFailure("Add at least one product line item before saving.");
            RequestFocusLineCell?.Invoke(0, "product");
            return;
        }

        if (Math.Abs(request.InvoiceAmount - request.TotalAmount) > 5m)
        {
            SetValidationFailure(
                $"Bill Amount ({request.InvoiceAmount:N2}) differs from Total Amount ({request.TotalAmount:N2}) by {request.InvoiceAmount - request.TotalAmount:N2} (maximum allowed rounding difference is ±5.00). Click '= Net' to match Bill Amount.");
            RequestFocusBillAmount?.Invoke();
            return;
        }

        IsBusy = true;
        try
        {
            PurchaseInvoiceOperationResult result = IsEditFormMode
                ? await _purchaseService.UpdateAsync(EditingInvoiceId!.Value, request)
                : await _purchaseService.CreateAsync(request);

            if (result.RequiresPreShiftDrawerChoice)
            {
                PreShiftPromptMessage = result.Message;
                IsPreShiftPromptOpen = true;
                return;
            }

            if (!result.Succeeded)
            {
                foreach (var err in result.ValidationErrors)
                    ValidationErrors.Add(err);
                if (ValidationErrors.Count == 0 && !string.IsNullOrWhiteSpace(result.Message))
                    ValidationErrors.Add(result.Message);
                OnPropertyChanged(nameof(HasValidationErrors));

                _toast.ShowError(result.Message, "Validation Error");

                if (result.OffendingRowIndex.HasValue && !string.IsNullOrWhiteSpace(result.OffendingFieldName))
                {
                    RequestFocusLineCell?.Invoke(result.OffendingRowIndex.Value, result.OffendingFieldName);
                }
                else if (result.OffendingFieldName == "invoice_number")
                {
                    RequestFocusInvoiceNumber?.Invoke();
                }
                else if (result.OffendingFieldName == "invoice_amount")
                {
                    RequestFocusBillAmount?.Invoke();
                }
                return;
            }

            _toast.ShowSuccess(result.Message, "Purchase Invoice Posted");

            if (result.InvoiceId.HasValue)
            {
                if (_navigate != null)
                    _navigate($"/purchase-invoices/{result.InvoiceId.Value}");
                else
                    await OpenDetailModeAsync(result.InvoiceId.Value);
            }
            else
            {
                BackToDirectory();
            }
        }
        catch (Exception ex)
        {
            SetValidationFailure(ex.Message);
            _toast.ShowError(ex.Message, "Save Failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // 11. HELD DRAFTS COMMANDS (Alt+H / Alt+D)
    // =========================================================================

    [RelayCommand]
    public async Task HoldCurrentDraftAsync()
    {
        if (!IsFormMode) return;

        var activeLines = Lines.Where(l => l.HasProduct).ToList();
        if (activeLines.Count == 0)
        {
            _toast.ShowWarning("Add at least one product line before holding a draft.", "Empty Invoice");
            return;
        }

        IsBusy = true;
        try
        {
            var req = BuildUpsertRequest(null);
            var saved = await _purchaseService.SaveDraftAsync(ActiveDraftId, req);
            ActiveDraftId = saved.Id;
            ActiveDraftNumber = saved.DraftNumber;
            HeldDraftsCount = (await _purchaseService.GetHeldDraftsAsync()).Count;

            _toast.ShowSuccess($"Held purchase draft {saved.DraftNumber} saved ({saved.ItemCount} items).", "Draft Held");
        }
        catch (Exception ex)
        {
            _toast.ShowError(ex.Message, "Hold Draft Failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task OpenHeldDraftsModalAsync()
    {
        try
        {
            var list = await _purchaseService.GetHeldDraftsAsync();
            HeldDrafts.Clear();
            foreach (var d in list)
                HeldDrafts.Add(d);
            HeldDraftsCount = list.Count;
            IsDraftsModalOpen = true;
        }
        catch (Exception ex)
        {
            _toast.ShowError(ex.Message, "Load Drafts Failed");
        }
    }

    [RelayCommand]
    public void CloseHeldDraftsModal()
    {
        IsDraftsModalOpen = false;
    }

    [RelayCommand]
    public async Task ResumeHeldDraftAsync(HeldPurchaseDraftSummaryDto? summary)
    {
        if (summary == null) return;

        IsDraftsModalOpen = false;
        IsBusy = true;
        try
        {
            var boot = await _purchaseService.GetFormBootstrapAsync();
            var draft = await _purchaseService.GetDraftByIdAsync(summary.Id);
            if (draft == null)
            {
                _toast.ShowWarning("Selected draft is no longer available.", "Draft Not Found");
                return;
            }

            ApplyBootstrap(boot);

            var state = draft.FormState;
            EditingInvoiceId = null;
            AllocatedPaymentsOnEdit = 0m;
            ActiveDraftId = draft.Id;
            ActiveDraftNumber = draft.DraftNumber;
            LinkedPurchaseOrderId = state.PurchaseOrderId;
            LinkedPurchaseOrderNumber = string.Empty;
            LinkedReceiptKey = state.ReceiptKey ?? string.Empty;

            PostedNumber = boot.NextPostedNumber;
            PostedDate = state.PostedDate == default ? DateTime.Today : state.PostedDate.Date;
            SelectedSupplierOption = SupplierOptions.FirstOrDefault(s => s.Id == state.SupplierId);
            DueDate = state.DueDate?.Date ?? PostedDate.AddDays(SelectedSupplierOption?.CreditDays ?? 30);
            InvoiceNumber = state.InvoiceNumber ?? string.Empty;
            BillAmountText = state.InvoiceAmount.ToString("0.00", CultureInfo.InvariantCulture);
            Remarks = state.Remarks ?? string.Empty;
            InvoiceType = string.Equals(state.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase) ? "credit" : "debit";

            var allProducts = await _purchaseService.SearchProductsForPurchaseAsync(null, state.SupplierId, 200);
            var prodMap = allProducts.ToDictionary(p => p.Id);

            Lines.Clear();
            _nextEntrySequence = 1;
            foreach (var itemReq in state.Items.Where(i => i.ProductId > 0))
            {
                prodMap.TryGetValue(itemReq.ProductId, out var pLookup);
                var rowVm = new PurchaseInvoiceLineRowViewModel(Lines.Count + 1, OnLineRecalculated)
                {
                    OriginalEntrySequence = _nextEntrySequence++
                };
                rowVm.LoadFromUpsertLine(itemReq, pLookup);
                Lines.Add(rowVm);
            }
            AppendEmptyLine();

            _suppressFooterRecalc = true;
            DiscountPercentage = state.DiscountPercentage;
            DiscountAmount = state.DiscountAmount;
            TaxPercentage = state.TaxPercentage;
            TaxAmount = state.TaxAmount;
            DiscountPctText = state.DiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            DiscountAmtText = state.DiscountAmount.ToString("0.00", CultureInfo.InvariantCulture);
            TaxPctText = state.TaxPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            TaxAmtText = state.TaxAmount.ToString("0.00", CultureInfo.InvariantCulture);
            _suppressFooterRecalc = false;

            RecalculateFooter(null);

            if (state.TotalPaid.HasValue)
            {
                _suppressFooterRecalc = true;
                TotalPaidText = state.TotalPaid.Value.ToString("0.00", CultureInfo.InvariantCulture);
                _suppressFooterRecalc = false;
                _paidTouched = true;
            }

            var pmOpt = PaymentMethodOptions.FirstOrDefault(o =>
                string.Equals(o.Key, state.PaymentMethod, StringComparison.OrdinalIgnoreCase)) ?? PaymentMethodOptions[0];
            SelectedPaymentMethodOption = pmOpt;
            RefreshFilteredAccounts(pmOpt.Key, state.AccountId);

            ClearValidationErrors();
            NotifyLinesAggregateChanged();
            ActiveMode = "form";

            _toast.ShowInfo($"Resumed draft {draft.DraftNumber}.", "Draft Resumed");
        }
        catch (Exception ex)
        {
            _toast.ShowError(ex.Message, "Resume Draft Failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteHeldDraftAsync(HeldPurchaseDraftSummaryDto? summary)
    {
        if (summary == null) return;
        await _purchaseService.DeleteDraftAsync(summary.Id);
        HeldDrafts.Remove(summary);
        HeldDraftsCount = HeldDrafts.Count;
        if (ActiveDraftId == summary.Id)
        {
            ActiveDraftId = null;
            ActiveDraftNumber = string.Empty;
        }
    }

    // =========================================================================
    // 12. AUDIT HISTORY, REVERSAL & 2-STEP PASSWORD DELETE MODALS
    // =========================================================================

    [RelayCommand]
    public async Task OpenHistoryFromRowAsync(PurchaseInvoiceDirectoryRowDto? row)
    {
        if (row == null) return;
        await LoadAndShowHistoryAsync(row.Id, row.PostedNumber);
    }

    [RelayCommand]
    public async Task OpenCurrentDetailHistoryAsync()
    {
        if (CurrentDetail == null) return;
        await LoadAndShowHistoryAsync(CurrentDetail.Id, CurrentDetail.PostedNumber);
    }

    private async Task LoadAndShowHistoryAsync(long invoiceId, string postedNumber)
    {
        try
        {
            var entries = await _purchaseService.GetDocumentHistoryAsync(invoiceId);
            HistoryEntries.Clear();
            foreach (var e in entries)
                HistoryEntries.Add(e);
            HistoryModalTitle = $"Audit History — {postedNumber}";
            IsHistoryModalOpen = true;
        }
        catch (Exception ex)
        {
            _toast.ShowError(ex.Message, "History Error");
        }
    }

    [RelayCommand]
    public void CloseHistoryModal()
    {
        IsHistoryModalOpen = false;
    }

    [RelayCommand]
    public void OpenReverseFromRow(PurchaseInvoiceDirectoryRowDto? row)
    {
        if (row == null || row.IsReversed) return;
        ReverseTargetInvoiceId = row.Id;
        ReverseTargetPostedNumber = row.PostedNumber;
        ReverseReason = string.Empty;
        ReverseModalError = string.Empty;
        IsReverseModalOpen = true;
    }

    [RelayCommand]
    public void OpenReverseForCurrentDetail()
    {
        if (CurrentDetail == null || !CanMutateCurrentDetail) return;
        ReverseTargetInvoiceId = CurrentDetail.Id;
        ReverseTargetPostedNumber = CurrentDetail.PostedNumber;
        ReverseReason = string.Empty;
        ReverseModalError = string.Empty;
        IsReverseModalOpen = true;
    }

    [RelayCommand]
    public void CloseReverseModal()
    {
        IsReverseModalOpen = false;
    }

    [RelayCommand]
    public async Task ConfirmReverseInvoiceAsync()
    {
        if (string.IsNullOrWhiteSpace(ReverseReason) || ReverseReason.Trim().Length < 3)
        {
            ReverseModalError = "Please enter a reversal reason (at least 3 characters).";
            return;
        }

        IsBusy = true;
        try
        {
            var res = await _purchaseService.ReverseAsync(ReverseTargetInvoiceId, ReverseReason.Trim());
            if (!res.Succeeded)
            {
                ReverseModalError = res.Message;
                return;
            }

            IsReverseModalOpen = false;
            _toast.ShowSuccess(res.Message, "Invoice Reversed");

            if (IsDetailMode && CurrentDetail?.Id == ReverseTargetInvoiceId)
            {
                await OpenDetailModeAsync(ReverseTargetInvoiceId);
            }
            else
            {
                await RefreshDirectoryAsync();
            }
        }
        catch (Exception ex)
        {
            ReverseModalError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void OpenDeleteFromRow(PurchaseInvoiceDirectoryRowDto? row)
    {
        if (row == null) return;
        DeleteTargetInvoiceId = row.Id;
        DeleteTargetPostedNumber = row.PostedNumber;
        DeleteStep = 1;
        DeletePassword = string.Empty;
        DeleteModalError = string.Empty;
        IsDeleteModalOpen = true;
    }

    [RelayCommand]
    public void OpenDeleteForCurrentDetail()
    {
        if (CurrentDetail == null) return;
        DeleteTargetInvoiceId = CurrentDetail.Id;
        DeleteTargetPostedNumber = CurrentDetail.PostedNumber;
        DeleteStep = 1;
        DeletePassword = string.Empty;
        DeleteModalError = string.Empty;
        IsDeleteModalOpen = true;
    }

    [RelayCommand]
    public void ProceedToDeleteStep2()
    {
        DeleteModalError = string.Empty;
        DeleteStep = 2;
    }

    [RelayCommand]
    public void CloseDeleteModal()
    {
        IsDeleteModalOpen = false;
        DeletePassword = string.Empty;
        DeleteModalError = string.Empty;
    }

    [RelayCommand]
    public async Task ConfirmDeleteInvoiceAsync()
    {
        if (string.IsNullOrWhiteSpace(DeletePassword))
        {
            DeleteModalError = "Enter your account password to authorize permanent deletion.";
            return;
        }

        IsBusy = true;
        try
        {
            var res = await _purchaseService.DeleteAsync(DeleteTargetInvoiceId, DeletePassword);
            if (!res.Succeeded)
            {
                DeleteModalError = res.Message;
                return;
            }

            IsDeleteModalOpen = false;
            DeletePassword = string.Empty;
            _toast.ShowSuccess(res.Message, "Invoice Deleted");

            if (IsDetailMode)
            {
                BackToDirectory();
            }
            else
            {
                await RefreshDirectoryAsync();
            }
        }
        catch (Exception ex)
        {
            DeleteModalError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // 13. INTERNAL HELPERS & CALCULATIONS
    // =========================================================================

    private void InitializeStaticOptions()
    {
        DirectoryTypeOptions.Add(new PurchaseSelectOption { Key = "all", Label = "All Types" });
        DirectoryTypeOptions.Add(new PurchaseSelectOption { Key = "debit", Label = "Debit (Cash)" });
        DirectoryTypeOptions.Add(new PurchaseSelectOption { Key = "credit", Label = "Credit (Payable)" });
        _selectedDirectoryTypeOption = DirectoryTypeOptions[0];

        DirectoryStatusOptions.Add(new PurchaseSelectOption { Key = "all", Label = "All Statuses" });
        DirectoryStatusOptions.Add(new PurchaseSelectOption { Key = "posted", Label = "Posted Only" });
        DirectoryStatusOptions.Add(new PurchaseSelectOption { Key = "reversed", Label = "Reversed (Void)" });
        _selectedDirectoryStatusOption = DirectoryStatusOptions[0];

        PageSizeOptions.Add(new PurchaseSelectOption { Key = "10", Label = "10" });
        PageSizeOptions.Add(new PurchaseSelectOption { Key = "25", Label = "25" });
        PageSizeOptions.Add(new PurchaseSelectOption { Key = "50", Label = "50" });
        PageSizeOptions.Add(new PurchaseSelectOption { Key = "100", Label = "100" });
        _selectedPageSizeOption = PageSizeOptions[0];

        PaymentMethodOptions.Add(new PurchaseSelectOption { Key = "cash", Label = "Cash" });
        PaymentMethodOptions.Add(new PurchaseSelectOption { Key = "bank_transfer", Label = "Bank Transfer" });
        PaymentMethodOptions.Add(new PurchaseSelectOption { Key = "card", Label = "Card / POS" });
        PaymentMethodOptions.Add(new PurchaseSelectOption { Key = "mobile_wallet", Label = "Mobile Wallet" });
        PaymentMethodOptions.Add(new PurchaseSelectOption { Key = "cheque", Label = "Cheque" });
        PaymentMethodOptions.Add(new PurchaseSelectOption { Key = "other", Label = "Other" });
        _selectedPaymentMethodOption = PaymentMethodOptions[0];

        // 16 Row Arrangement Presets matching purchaseInvoiceArrangement.js + Supplier Remembered Order
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "original", Label = "Entry Order (Original)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "supplier_saved", Label = "Supplier Saved Order" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "name_asc", Label = "Product Name (A → Z)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "name_desc", Label = "Product Name (Z → A)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "brand_asc", Label = "Brand Name (A → Z)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "brand_desc", Label = "Brand Name (Z → A)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "brand_code", Label = "Brand + Product Code" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "code_asc", Label = "Product Code (Low → High)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "code_desc", Label = "Product Code (High → Low)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "packs_asc", Label = "Pack Qty (Low → High)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "packs_desc", Label = "Pack Qty (High → Low)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "amount_asc", Label = "Line Net Total (Low → High)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "amount_desc", Label = "Line Net Total (High → Low)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "price_asc", Label = "Unit Cost (Low → High)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "price_desc", Label = "Unit Cost (High → Low)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "size_asc", Label = "Pack Size (Small → Large)" });
        ArrangementOptions.Add(new PurchaseSelectOption { Key = "size_desc", Label = "Pack Size (Large → Small)" });
        _selectedArrangementOption = ArrangementOptions[0];
    }

    private void ApplyBootstrap(PurchaseFormBootstrapDto boot)
    {
        StoreSettings = boot.StoreSettings;
        OnPropertyChanged(nameof(HasWholesale));
        OnPropertyChanged(nameof(CurrencySymbol));

        SupplierOptions.Clear();
        foreach (var s in boot.Suppliers)
            SupplierOptions.Add(s);

        _allAccounts = boot.Accounts;

        ReceivableOrderOptions.Clear();
        foreach (var po in boot.ReceivableOrders)
            ReceivableOrderOptions.Add(po);

        HasOpenCashDrawer = boot.HasOpenCashDrawer;
        OpenCashDrawerBalance = boot.OpenCashDrawerExpectedBalance;
        HeldDraftsCount = boot.HeldDraftsCount;
        ProductSearchModalLayout = boot.ProductSearchModalLayout ?? new PurchaseModalWindowLayoutDto { Width = 960, Height = 600 };
        QuickAddProductModalLayout = boot.QuickAddProductModalLayout ?? new PurchaseModalWindowLayoutDto { Width = 800, Height = 600 };
    }

    private void RefreshFilteredAccounts(string paymentMethod, long? preserveCurrentAccountId)
    {
        var expectedTypes = paymentMethod.ToLowerInvariant() switch
        {
            "cash" => new[] { "cash_drawer", "cash" },
            "bank_transfer" or "cheque" => new[] { "bank" },
            "card" => new[] { "card", "bank" },
            "mobile_wallet" => new[] { "mobile_wallet" },
            _ => new[] { "cash_drawer", "cash", "bank", "card", "mobile_wallet", "other" }
        };

        var matches = _allAccounts
            .Where(a => expectedTypes.Contains(a.AccountType.ToLowerInvariant()))
            .ToList();
        if (matches.Count == 0)
            matches = _allAccounts.ToList();

        FilteredAccountOptions.Clear();
        foreach (var acc in matches)
            FilteredAccountOptions.Add(acc);

        SelectedAccountOption = preserveCurrentAccountId.HasValue
            ? FilteredAccountOptions.FirstOrDefault(a => a.Id == preserveCurrentAccountId.Value) ?? FilteredAccountOptions.FirstOrDefault()
            : FilteredAccountOptions.FirstOrDefault(a => a.IsDefault) ?? FilteredAccountOptions.FirstOrDefault();
    }

    private async Task RefreshDirectoryLookupsAndPageAsync()
    {
        try
        {
            var boot = await _purchaseService.GetFormBootstrapAsync();
            StoreSettings = boot.StoreSettings;
            OnPropertyChanged(nameof(CurrencySymbol));

            _suppressDirectoryReload = true;
            var prevSupId = SelectedDirectorySupplierOption?.SupplierId;
            DirectorySupplierOptions.Clear();
            DirectorySupplierOptions.Add(new PurchaseSupplierFilterOption { SupplierId = null, Label = "All Suppliers" });
            foreach (var s in boot.Suppliers)
            {
                DirectorySupplierOptions.Add(new PurchaseSupplierFilterOption
                {
                    SupplierId = s.Id,
                    Label = s.DisplayLabel
                });
            }
            SelectedDirectorySupplierOption = DirectorySupplierOptions.FirstOrDefault(o => o.SupplierId == prevSupId)
                                              ?? DirectorySupplierOptions[0];
            _suppressDirectoryReload = false;
        }
        catch
        {
            _suppressDirectoryReload = false;
        }

        await RefreshDirectoryAsync();
    }

    private async Task RefreshSupplierCreditSummaryAsync(long supplierId)
    {
        try
        {
            var fresh = await _purchaseService.GetSupplierCreditSummaryAsync(supplierId);
            if (fresh != null && SelectedSupplierOption?.Id == supplierId)
            {
                ActiveSupplierCredit = fresh;
            }
        }
        catch
        {
            // Keep existing lookup summary on transient error
        }
    }

    private void OnLineRecalculated(PurchaseInvoiceLineRowViewModel row)
    {
        RecalculateFooter(null);
        NotifyLinesAggregateChanged();
    }

    public void RecalculateFooter(string? changedField)
    {
        var calcItems = Lines.Where(l => l.HasProduct).Select(l => l.BuildCalcModel()).ToList();
        foreach (var c in calcItems)
        {
            InvoiceCalculators.RecalcPurchaseItem(c, null);
        }

        var footer = new PurchaseFooterCalcModel
        {
            DiscountPercentage = DiscountPercentage,
            DiscountAmount = DiscountAmount,
            TaxPercentage = TaxPercentage,
            TaxAmount = TaxAmount
        };

        InvoiceCalculators.RecalcPurchaseFooter(calcItems, footer, changedField);

        RowsTotal = footer.RowsTotal;
        DiscountPercentage = footer.DiscountPercentage;
        DiscountAmount = footer.DiscountAmount;
        TaxPercentage = footer.TaxPercentage;
        TaxAmount = footer.TaxAmount;
        TotalAmount = footer.TotalAmount;

        _suppressFooterRecalc = true;
        try
        {
            if (changedField != "discount_percentage")
                DiscountPctText = DiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            if (changedField != "discount_amount")
                DiscountAmtText = DiscountAmount.ToString("0.00", CultureInfo.InvariantCulture);
            if (changedField != "tax_percentage")
                TaxPctText = TaxPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            if (changedField != "tax_amount")
                TaxAmtText = TaxAmount.ToString("0.00", CultureInfo.InvariantCulture);

            if (IsDebitInvoice && !_paidTouched)
            {
                var targetDirectPaid = Math.Max(0m, Math.Round(TotalAmount - AllocatedPaymentsOnEdit, 2));
                TotalPaidText = targetDirectPaid.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _suppressFooterRecalc = false;
        }

        OnPropertyChanged(nameof(ShowRelinkPaidButton));
        OnPropertyChanged(nameof(RemainingPayableAmount));
        OnPropertyChanged(nameof(IsRemainingPositive));
        OnPropertyChanged(nameof(HasDiscountSummary));
        OnPropertyChanged(nameof(HasTaxSummary));
        OnPropertyChanged(nameof(LedgerCreditExcessAmount));
        OnPropertyChanged(nameof(HasLedgerCreditExcess));
        OnPropertyChanged(nameof(ProjectedSupplierPayable));
        OnPropertyChanged(nameof(IsCreditLimitExceeded));
    }

    private PurchaseInvoiceUpsertRequest BuildUpsertRequest(bool? updatePreShiftDrawer)
    {
        decimal? resolvedTotalPaid;
        if (IsDebitInvoice)
        {
            resolvedTotalPaid = _paidTouched
                ? Math.Round(TotalPaid + AllocatedPaymentsOnEdit, 2)
                : TotalAmount;
        }
        else
        {
            resolvedTotalPaid = Math.Round(TotalPaid + AllocatedPaymentsOnEdit, 2);
        }

        return new PurchaseInvoiceUpsertRequest
        {
            DraftId = ActiveDraftId,
            PurchaseOrderId = LinkedPurchaseOrderId,
            ReceiptKey = string.IsNullOrWhiteSpace(LinkedReceiptKey) ? null : LinkedReceiptKey,
            SupplierId = SelectedSupplierOption?.Id ?? 0,
            InvoiceType = InvoiceType,
            PaymentMethod = SelectedPaymentMethodOption?.Key ?? "cash",
            AccountId = SelectedAccountOption?.Id,
            PostedDate = PostedDate.Date,
            DueDate = DueDate?.Date,
            Remarks = Remarks,
            InvoiceNumber = InvoiceNumber.Trim(),
            InvoiceAmount = BillAmount,
            DiscountPercentage = DiscountPercentage,
            DiscountAmount = DiscountAmount,
            TaxPercentage = TaxPercentage,
            TaxAmount = TaxAmount,
            TotalAmount = TotalAmount,
            TotalPaid = resolvedTotalPaid,
            EditReason = EditReason,
            UpdatePreShiftDrawer = updatePreShiftDrawer,
            Items = Lines
                .Where(l => l.HasProduct)
                .Select(l => l.ToUpsertLineRequest())
                .ToList()
        };
    }

    private void RenumberLines()
    {
        for (int i = 0; i < Lines.Count; i++)
        {
            Lines[i].RowNumber = i + 1;
        }
    }

    private void NotifyLinesAggregateChanged()
    {
        OnPropertyChanged(nameof(ActiveProductLinesCount));
        OnPropertyChanged(nameof(TotalReceivedUnitsCount));
    }

    private void ClearValidationErrors()
    {
        ValidationErrors.Clear();
        OnPropertyChanged(nameof(HasValidationErrors));
    }

    private void SetValidationFailure(string message)
    {
        ValidationErrors.Clear();
        ValidationErrors.Add(message);
        OnPropertyChanged(nameof(HasValidationErrors));
        _toast.ShowWarning(message, "Check Invoice Fields");
    }

    public void CloseAllModals()
    {
        IsSupplierSearchModalOpen = false;
        IsProductSearchModalOpen = false;
        IsQuickAddProductModalOpen = false;
        IsDraftsModalOpen = false;
        IsPreShiftPromptOpen = false;
        IsHistoryModalOpen = false;
        IsReverseModalOpen = false;
        IsDeleteModalOpen = false;
    }

    private void DebounceReloadDirectory()
    {
        _directoryDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _directoryDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cts.Token);
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (!cts.IsCancellationRequested)
                    {
                        CurrentPage = 1;
                        _ = RefreshDirectoryAsync();
                    }
                });
            }
            catch (TaskCanceledException) { }
        });
    }

    private void DebounceProductSearch(string query)
    {
        _productSearchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _productSearchDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(140, cts.Token);
                if (cts.IsCancellationRequested) return;
                var results = await _purchaseService.SearchProductsForPurchaseAsync(
                    query,
                    SelectedSupplierOption?.Id,
                    200,
                    cts.Token);

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    ProductSearchResults.Clear();
                    foreach (var item in results)
                        ProductSearchResults.Add(item);
                    HighlightedSearchProduct = ProductSearchResults.FirstOrDefault();
                    OnPropertyChanged(nameof(HasProductSearchResults));
                    OnPropertyChanged(nameof(ShowEmptyProductSearchState));
                    OnPropertyChanged(nameof(ProductSearchResultsCount));
                    OnPropertyChanged(nameof(ProductSearchFooterCountText));
                });
            }
            catch (TaskCanceledException) { }
        });
    }

    private async Task ExecuteProductSearchAsync(string query)
    {
        var results = await _purchaseService.SearchProductsForPurchaseAsync(
            query,
            SelectedSupplierOption?.Id,
            200);

        ProductSearchResults.Clear();
        foreach (var item in results)
            ProductSearchResults.Add(item);
        HighlightedSearchProduct = ProductSearchResults.FirstOrDefault();
        OnPropertyChanged(nameof(HasProductSearchResults));
        OnPropertyChanged(nameof(ShowEmptyProductSearchState));
        OnPropertyChanged(nameof(ProductSearchResultsCount));
        OnPropertyChanged(nameof(ProductSearchFooterCountText));
    }

    private static decimal ParseDecimalSafe(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0m;
        var clean = raw.Trim().Replace(",", "");
        return decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }
}
