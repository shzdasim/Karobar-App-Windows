using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;
using Karobar.Domain.Formulas;

namespace Karobar.Desktop.ViewModels;

public sealed class SaleSelectOption
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

public sealed class SaleCustomerFilterOption
{
    public long? CustomerId { get; init; }
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

public partial class SaleInvoiceLineRowViewModel : ObservableObject
{
    private readonly Action<SaleInvoiceLineRowViewModel> _onRecalculated;
    private readonly Func<string> _getSaleType;
    private bool _suppressRecalc;

    public SaleInvoiceLineRowViewModel(
        int rowNumber,
        Func<string> getSaleType,
        Action<SaleInvoiceLineRowViewModel> onRecalculated)
    {
        RowNumber = rowNumber;
        _getSaleType = getSaleType;
        _onRecalculated = onRecalculated;
    }

    public Guid RowUid { get; } = Guid.NewGuid();

    [ObservableProperty]
    private int _rowNumber;

    [ObservableProperty]
    private long? _id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProduct))]
    [NotifyPropertyChangedFor(nameof(ProductDisplayText))]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    [NotifyPropertyChangedFor(nameof(IsZeroOrLowStock))]
    [NotifyPropertyChangedFor(nameof(IsStockExceeded))]
    [NotifyPropertyChangedFor(nameof(IsNegativeMargin))]
    private long _productId;

    public bool HasProduct => ProductId > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private string _productCode = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductDisplayText))]
    private string _productName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    [NotifyPropertyChangedFor(nameof(HasFormulation))]
    private string _formulation = string.Empty;

    public bool HasFormulation => !string.IsNullOrWhiteSpace(Formulation);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private string _brandName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private string _rack = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    [NotifyPropertyChangedFor(nameof(AvailPackUnitText))]
    private int _packSize = 1;

    public string ProductDisplayText => HasProduct
        ? ProductName
        : "Search product…";

    public string ProductMetaText
    {
        get
        {
            if (!HasProduct)
                return "Click or press Enter to search product";

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Formulation)) parts.Add(Formulation);
            if (!string.IsNullOrWhiteSpace(BrandName)) parts.Add(BrandName);
            parts.Add($"1×{Math.Max(1, PackSize)}");
            if (!string.IsNullOrWhiteSpace(Rack)) parts.Add($"Rack: {Rack}");
            return string.Join(" • ", parts);
        }
    }

    public ObservableCollection<SaleProductBatchOptionDto> AvailableBatches { get; } = [];

    [ObservableProperty]
    private long? _batchId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchDisplayText))]
    private string _batchNumber = string.Empty;

    public string BatchDisplayText => !HasProduct
        ? "—"
        : string.IsNullOrWhiteSpace(BatchNumber)
            ? "Unbatched"
            : BatchNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedExpiry))]
    [NotifyPropertyChangedFor(nameof(IsExpiredBatch))]
    private DateTime? _expiryDate;

    public string FormattedExpiry => ExpiryDate.HasValue ? ExpiryDate.Value.ToString("MM/yyyy") : "—";
    public bool IsExpiredBatch => HasProduct && ExpiryDate.HasValue && ExpiryDate.Value.Date < DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailPackUnitText))]
    [NotifyPropertyChangedFor(nameof(IsZeroOrLowStock))]
    [NotifyPropertyChangedFor(nameof(IsStockExceeded))]
    private int _currentQuantity;

    public string AvailPackUnitText => SaleProductBatchOptionDto.FormatPackUnitBadge(CurrentQuantity, PackSize);
    public bool IsZeroOrLowStock => HasProduct && CurrentQuantity <= 0;

    // Editable cell strings (bound with UpdateSourceTrigger=PropertyChanged)
    [ObservableProperty] private string _quantityText = "1";
    [ObservableProperty] private string _packQtyText = "0";
    [ObservableProperty] private string _unitQtyText = "1";
    [ObservableProperty] private string _itemDiscountText = "0";
    [ObservableProperty] private string _packBonusText = "0";
    [ObservableProperty] private string _unitBonusText = "0";
    [ObservableProperty] private string _packPriceText = "0.00";
    [ObservableProperty] private string _unitPriceText = "0.00";

    // Underlying numeric values
    public decimal PackQuantity { get; private set; }
    public int UnitQuantity { get; private set; } = 1;
    public int Quantity { get; private set; } = 1;
    public decimal PackBonus { get; private set; }
    public int UnitBonus { get; private set; }
    public int Bonus { get; private set; }
    public decimal PackPrice { get; private set; }
    public decimal Price { get; private set; }
    public decimal ItemDiscountPercentage { get; private set; }

    [ObservableProperty]
    private decimal _avgPrice;

    [ObservableProperty]
    private decimal _unitPurchasePrice;

    [ObservableProperty]
    private decimal _catalogUnitPrice;

    [ObservableProperty]
    private decimal _maxDiscountPercentage = 100m;

    [ObservableProperty]
    private bool _isCustomPrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarginDisplayText))]
    [NotifyPropertyChangedFor(nameof(IsNegativeMargin))]
    private decimal? _margin;

    [ObservableProperty]
    private decimal _subTotal;

    public string TotalUnitsDisplay => Bonus > 0 ? $"{Quantity}+{Bonus}" : $"{Quantity}";
    public bool HasBonus => Bonus > 0;
    public string MarginDisplayText => Margin.HasValue ? $"{Margin.Value:0.0}%" : "0.0%";
    public bool IsNegativeMargin => HasProduct && Margin.HasValue && Margin.Value < 0m;
    public bool IsStockExceeded => HasProduct && (Quantity + Bonus) > CurrentQuantity && CurrentQuantity >= 0;
    public decimal EffectiveUnitCost => AvgPrice > 0m ? AvgPrice : UnitPurchasePrice;
    public decimal EstimatedRowCost => Math.Round((Quantity + Bonus) * EffectiveUnitCost, 2);
    public decimal EstimatedRowProfit => Math.Round(SubTotal - EstimatedRowCost, 2);

    partial void OnQuantityTextChanged(string value)
    {
        if (_suppressRecalc) return;
        Quantity = Math.Max(0, ParseInt(value));
        UnitQuantity = Quantity;
        Recalculate("quantity");
    }

    partial void OnPackQtyTextChanged(string value)
    {
        if (_suppressRecalc) return;
        PackQuantity = Math.Max(0m, ParseDecimal(value));
        Recalculate("pack_quantity");
    }

    partial void OnUnitQtyTextChanged(string value)
    {
        if (_suppressRecalc) return;
        UnitQuantity = Math.Max(0, ParseInt(value));
        Quantity = UnitQuantity;
        Recalculate("unit_quantity");
    }

    partial void OnItemDiscountTextChanged(string value)
    {
        if (_suppressRecalc) return;
        ItemDiscountPercentage = ParseDecimal(value, allowNegative: true);
        Recalculate("disc");
    }

    partial void OnPackBonusTextChanged(string value)
    {
        if (_suppressRecalc) return;
        PackBonus = Math.Max(0m, ParseDecimal(value));
        Recalculate("pack_bonus");
    }

    partial void OnUnitBonusTextChanged(string value)
    {
        if (_suppressRecalc) return;
        UnitBonus = Math.Max(0, ParseInt(value));
        Bonus = UnitBonus;
        Recalculate("unit_bonus");
    }

    partial void OnPackPriceTextChanged(string value)
    {
        if (_suppressRecalc) return;
        PackPrice = Math.Max(0m, ParseDecimal(value));
        IsCustomPrice = true;
        Recalculate("pack_price");
    }

    partial void OnUnitPriceTextChanged(string value)
    {
        if (_suppressRecalc) return;
        Price = Math.Max(0m, ParseDecimal(value));
        IsCustomPrice = true;
        Recalculate("price");
    }

    public void ApplySelectedProduct(
        SaleProductSearchItemDto product,
        string saleType,
        Dictionary<long, decimal>? customerCustomPrices,
        decimal customerDefaultDiscountPct)
    {
        _suppressRecalc = true;
        try
        {
            ProductId = product.Id;
            ProductCode = product.ProductCode;
            ProductName = product.Name;
            Formulation = product.Formulation;
            BrandName = product.BrandName;
            Rack = product.Rack;
            PackSize = Math.Max(1, product.PackSize);
            AvgPrice = product.AvgPrice > 0m ? product.AvgPrice : product.UnitPurchasePrice;
            UnitPurchasePrice = product.UnitPurchasePrice;
            MaxDiscountPercentage = product.MaxDiscount > 0m ? product.MaxDiscount : 100m;

            AvailableBatches.Clear();
            foreach (var b in product.Batches)
            {
                AvailableBatches.Add(b);
            }

            // FEFO Auto-Selection: First unexpired batch with Quantity > 0, else first expired batch, else Unbatched
            var fefoBatch = AvailableBatches.FirstOrDefault(b => !b.IsUnbatched && !b.IsExpired && b.Quantity > 0)
                            ?? AvailableBatches.FirstOrDefault(b => !b.IsUnbatched && b.Quantity > 0)
                            ?? AvailableBatches.FirstOrDefault();

            ApplyBatchInternal(fefoBatch, product, saleType, customerCustomPrices);

            if (string.Equals(saleType, "wholesale", StringComparison.OrdinalIgnoreCase))
            {
                PackQuantity = 1m;
                UnitQuantity = PackSize;
                Quantity = PackSize;
                PackQtyText = "1";
                UnitQtyText = PackSize.ToString(CultureInfo.InvariantCulture);
                QuantityText = PackSize.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                Quantity = 1;
                UnitQuantity = 1;
                PackQuantity = Math.Round(1m / PackSize, 2);
                QuantityText = "1";
                UnitQtyText = "1";
                PackQtyText = PackQuantity.ToString("0.##", CultureInfo.InvariantCulture);
            }

            PackBonus = 0m;
            UnitBonus = 0;
            Bonus = 0;
            PackBonusText = "0";
            UnitBonusText = "0";

            ItemDiscountPercentage = customerDefaultDiscountPct > 0m ? customerDefaultDiscountPct : 0m;
            ItemDiscountText = ItemDiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture);
        }
        finally
        {
            _suppressRecalc = false;
        }

        Recalculate(string.Equals(saleType, "wholesale", StringComparison.OrdinalIgnoreCase) ? "pack_quantity" : "quantity");
    }

    public void ApplySelectedBatch(
        SaleProductBatchOptionDto batch,
        string saleType,
        Dictionary<long, decimal>? customerCustomPrices)
    {
        _suppressRecalc = true;
        try
        {
            ApplyBatchInternal(batch, null, saleType, customerCustomPrices);
        }
        finally
        {
            _suppressRecalc = false;
        }

        Recalculate(string.Equals(saleType, "wholesale", StringComparison.OrdinalIgnoreCase) ? "pack_price" : "quantity");
    }

    private void ApplyBatchInternal(
        SaleProductBatchOptionDto? batch,
        SaleProductSearchItemDto? product,
        string saleType,
        Dictionary<long, decimal>? customerCustomPrices)
    {
        var ps = Math.Max(1, PackSize);
        if (batch != null)
        {
            BatchId = batch.IsUnbatched ? null : batch.Id;
            BatchNumber = batch.IsUnbatched ? "Unbatched (Stock)" : batch.BatchNumber;
            ExpiryDate = batch.ExpiryDate;
            CurrentQuantity = batch.Quantity;
            if (!string.IsNullOrWhiteSpace(batch.Rack))
                Rack = batch.Rack;
        }
        else
        {
            BatchId = null;
            BatchNumber = "Unbatched (Stock)";
            ExpiryDate = null;
            CurrentQuantity = product?.Quantity ?? 0;
        }

        bool isWholesale = string.Equals(saleType, "wholesale", StringComparison.OrdinalIgnoreCase);
        if (isWholesale)
        {
            // Wholesale Price Resolution Cascade:
            // 1. Customer Custom Wholesale Price
            // 2. Batch Wholesale Price (fallback Batch Retail Price)
            // 3. Product Wholesale Price (fallback Product Retail Price)
            if (customerCustomPrices != null &&
                customerCustomPrices.TryGetValue(ProductId, out var customPackPrice) &&
                customPackPrice > 0m)
            {
                PackPrice = Math.Round(customPackPrice, 2);
                Price = Math.Round(customPackPrice / ps, 2);
                IsCustomPrice = true;
            }
            else if (batch != null && !batch.IsUnbatched && (batch.WholeSalePackPrice > 0m || batch.WholeSaleUnitPrice > 0m))
            {
                PackPrice = batch.WholeSalePackPrice > 0m
                    ? batch.WholeSalePackPrice
                    : Math.Round(batch.WholeSaleUnitPrice * ps, 2);
                Price = batch.WholeSaleUnitPrice > 0m
                    ? batch.WholeSaleUnitPrice
                    : Math.Round(PackPrice / ps, 2);
                IsCustomPrice = false;
            }
            else if (product != null && (product.WholeSalePackPrice > 0m || product.WholeSaleUnitPrice > 0m))
            {
                PackPrice = product.WholeSalePackPrice > 0m
                    ? product.WholeSalePackPrice
                    : Math.Round(product.WholeSaleUnitPrice * ps, 2);
                Price = product.WholeSaleUnitPrice > 0m
                    ? product.WholeSaleUnitPrice
                    : Math.Round(PackPrice / ps, 2);
                IsCustomPrice = product.IsCustomCustomerPrice;
            }
            else
            {
                var fallbackUnit = batch?.UnitSalePrice > 0m ? batch.UnitSalePrice : (product?.UnitSalePrice ?? Price);
                Price = Math.Round(fallbackUnit, 2);
                PackPrice = Math.Round(Price * ps, 2);
                IsCustomPrice = false;
            }

            CatalogUnitPrice = Price;
        }
        else
        {
            // Retail Price Resolution: Batch UnitSalePrice -> Product UnitSalePrice
            var retailUnit = batch != null && batch.UnitSalePrice > 0m
                ? batch.UnitSalePrice
                : (batch != null && batch.PackSalePrice > 0m
                    ? Math.Round(batch.PackSalePrice / ps, 2)
                    : (product?.UnitSalePrice > 0m
                        ? product.UnitSalePrice
                        : (product != null && product.PackSalePrice > 0m ? Math.Round(product.PackSalePrice / ps, 2) : Price)));

            Price = Math.Round(retailUnit, 2);
            PackPrice = Math.Round(Price * ps, 2);
            CatalogUnitPrice = Price;
            IsCustomPrice = false;
        }

        PackPriceText = PackPrice.ToString("0.##", CultureInfo.InvariantCulture);
        UnitPriceText = Price.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public void LoadFromDto(SaleInvoiceLineItemDto dto, string saleType)
    {
        _suppressRecalc = true;
        try
        {
            Id = dto.Id;
            ProductId = dto.ProductId;
            ProductCode = dto.ProductCode;
            ProductName = dto.ProductName;
            Formulation = dto.Formulation;
            BrandName = dto.BrandName;
            Rack = dto.Rack;
            PackSize = Math.Max(1, dto.PackSize);
            BatchId = dto.BatchId;
            BatchNumber = string.IsNullOrWhiteSpace(dto.BatchNumber) ? "Unbatched (Stock)" : dto.BatchNumber;
            ExpiryDate = dto.Expiry;
            CurrentQuantity = dto.CurrentQuantity;
            PackQuantity = dto.PackQuantity;
            UnitQuantity = dto.UnitQuantity > 0 ? dto.UnitQuantity : dto.Quantity;
            Quantity = dto.Quantity > 0 ? dto.Quantity : dto.UnitQuantity;
            PackBonus = dto.PackBonus;
            UnitBonus = dto.UnitBonus > 0 ? dto.UnitBonus : dto.Bonus;
            Bonus = dto.Bonus > 0 ? dto.Bonus : dto.UnitBonus;
            PackPrice = dto.PackPrice;
            Price = dto.Price;
            AvgPrice = dto.AvgPrice > 0m ? dto.AvgPrice : (dto.CostPriceSnapshot ?? dto.UnitPurchasePrice);
            UnitPurchasePrice = dto.UnitPurchasePrice;
            CatalogUnitPrice = dto.CatalogUnitPrice > 0m ? dto.CatalogUnitPrice : dto.Price;
            ItemDiscountPercentage = dto.ItemDiscountPercentage;
            MaxDiscountPercentage = dto.MaxDiscountPercentage > 0m ? dto.MaxDiscountPercentage : 100m;
            Margin = dto.Margin;
            SubTotal = dto.SubTotal;
            IsCustomPrice = dto.IsCustomPrice;

            AvailableBatches.Clear();
            foreach (var b in dto.AvailableBatches)
            {
                AvailableBatches.Add(b);
            }

            QuantityText = Quantity.ToString(CultureInfo.InvariantCulture);
            PackQtyText = PackQuantity.ToString("0.##", CultureInfo.InvariantCulture);
            UnitQtyText = UnitQuantity.ToString(CultureInfo.InvariantCulture);
            ItemDiscountText = ItemDiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            PackBonusText = PackBonus.ToString("0.##", CultureInfo.InvariantCulture);
            UnitBonusText = UnitBonus.ToString(CultureInfo.InvariantCulture);
            PackPriceText = PackPrice.ToString("0.##", CultureInfo.InvariantCulture);
            UnitPriceText = Price.ToString("0.##", CultureInfo.InvariantCulture);
        }
        finally
        {
            _suppressRecalc = false;
        }

        Recalculate(null);
    }

    public void Recalculate(string? changedField)
    {
        if (_suppressRecalc) return;

        _suppressRecalc = true;
        try
        {
            var isWholesale = string.Equals(_getSaleType(), "wholesale", StringComparison.OrdinalIgnoreCase);
            var calc = new SaleLineCalcModel
            {
                PackSize = Math.Max(1, PackSize),
                PackQuantity = PackQuantity,
                UnitQuantity = UnitQuantity,
                Quantity = Quantity,
                PackBonus = PackBonus,
                UnitBonus = UnitBonus,
                Bonus = Bonus,
                PackPrice = PackPrice,
                Price = Price,
                ItemDiscountPercentage = ItemDiscountPercentage,
                AvgPrice = AvgPrice,
                UnitPurchasePrice = UnitPurchasePrice
            };

            if (isWholesale)
            {
                InvoiceCalculators.RecalcWholesaleSaleItem(calc, changedField);
                PackQuantity = calc.PackQuantity;
                UnitQuantity = calc.UnitQuantity;
                Quantity = calc.Quantity;
                PackBonus = calc.PackBonus;
                UnitBonus = calc.UnitBonus;
                Bonus = calc.Bonus;
                PackPrice = calc.PackPrice;
                Price = calc.Price;
                Margin = calc.Margin;
                SubTotal = calc.SubTotal;

                if (changedField != "pack_quantity")
                    PackQtyText = PackQuantity.ToString("0.##", CultureInfo.InvariantCulture);
                if (changedField != "unit_quantity")
                    UnitQtyText = UnitQuantity.ToString(CultureInfo.InvariantCulture);
                if (changedField != "quantity")
                    QuantityText = Quantity.ToString(CultureInfo.InvariantCulture);
                if (changedField != "pack_bonus")
                    PackBonusText = PackBonus.ToString("0.##", CultureInfo.InvariantCulture);
                if (changedField != "unit_bonus")
                    UnitBonusText = UnitBonus.ToString(CultureInfo.InvariantCulture);
                if (changedField != "pack_price")
                    PackPriceText = PackPrice.ToString("0.##", CultureInfo.InvariantCulture);
                if (changedField != "price")
                    UnitPriceText = Price.ToString("0.##", CultureInfo.InvariantCulture);
            }
            else
            {
                calc.Quantity = Quantity;
                calc.UnitQuantity = Quantity;
                calc.PackQuantity = Math.Round((decimal)Quantity / Math.Max(1, PackSize), 2);
                calc.PackPrice = Math.Round(Price * Math.Max(1, PackSize), 2);
                InvoiceCalculators.RecalcRetailSaleItem(calc);

                PackQuantity = calc.PackQuantity;
                UnitQuantity = calc.Quantity;
                SubTotal = calc.SubTotal;

                var unitCost = EffectiveUnitCost;
                var netUnit = Price * (1m - (ItemDiscountPercentage / 100m));
                Margin = netUnit > 0m && unitCost > 0m
                    ? Math.Round(((netUnit - unitCost) / netUnit) * 100m, 2)
                    : 0m;
            }

            OnPropertyChanged(nameof(TotalUnitsDisplay));
            OnPropertyChanged(nameof(HasBonus));
            OnPropertyChanged(nameof(IsStockExceeded));
            OnPropertyChanged(nameof(IsNegativeMargin));
            OnPropertyChanged(nameof(EstimatedRowCost));
            OnPropertyChanged(nameof(EstimatedRowProfit));
        }
        finally
        {
            _suppressRecalc = false;
        }

        _onRecalculated(this);
    }

    public SaleInvoiceLineItemDto ToUpsertDto(string saleType) => new()
    {
        Id = Id,
        ProductId = ProductId,
        ProductCode = ProductCode,
        ProductName = ProductName,
        Formulation = Formulation,
        BrandName = BrandName,
        Rack = Rack,
        PackSize = Math.Max(1, PackSize),
        BatchId = BatchId,
        BatchNumber = BatchNumber,
        Expiry = ExpiryDate,
        CurrentQuantity = CurrentQuantity,
        PackQuantity = PackQuantity,
        UnitQuantity = UnitQuantity,
        Quantity = Quantity,
        PackBonus = PackBonus,
        UnitBonus = UnitBonus,
        Bonus = Bonus,
        PackPrice = PackPrice,
        Price = Price,
        CostPriceSnapshot = EffectiveUnitCost,
        AvgPrice = AvgPrice,
        UnitPurchasePrice = UnitPurchasePrice,
        CatalogUnitPrice = CatalogUnitPrice,
        ItemDiscountPercentage = ItemDiscountPercentage,
        MaxDiscountPercentage = MaxDiscountPercentage,
        Margin = Margin,
        EstimatedRowCost = EstimatedRowCost,
        EstimatedRowProfit = EstimatedRowProfit,
        SubTotal = SubTotal,
        SaleType = saleType,
        IsCustomPrice = IsCustomPrice
    };

    private static int ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;
        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
            return iv;
        if (decimal.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var dv))
            return (int)Math.Round(dv, 0, MidpointRounding.AwayFromZero);
        return 0;
    }

    private static decimal ParseDecimal(string? raw, bool allowNegative = false)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0m;
        var clean = raw.Trim();
        if (clean == "-" || clean == "." || clean == "-.")
            return 0m;
        if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv))
            return allowNegative ? dv : Math.Max(0m, dv);
        return 0m;
    }
}

public partial class SaleInvoicesViewModel : ObservableObject
{
    private readonly ISaleInvoiceService _saleService;
    private readonly IProductService? _productService;
    private readonly IToastNotificationService _toastService;
    private readonly Action<string> _navigate;
    private bool _suppressFooterRecalc;
    private bool _isBootstrapLoaded;

    // View Focus & Print Callbacks wired by SaleInvoicesView.xaml.cs
    public Action? RequestFocusCustomerSelect { get; set; }
    public Action? RequestFocusCustomerModalSearch { get; set; }
    public Action? RequestFocusOneTimeCustomerName { get; set; }
    public Action? RequestFocusOneTimeCustomerPhone { get; set; }
    public Action? RequestFocusDoctorName { get; set; }
    public Action? RequestFocusPatientName { get; set; }
    public Action? RequestFocusRemarks { get; set; }
    public Action? RequestFocusDirectorySearch { get; set; }
    public Action? RequestFocusProductModalSearch { get; set; }
    public Action? RequestFocusBatchModalSearch { get; set; }
    public Action? RequestFocusQuickAddProductName { get; set; }
    public Action? RequestFocusShowInvoiceSearch { get; set; }
    public Action? RequestFocusDeletePassword { get; set; }
    public Action? RequestApplyProductSearchModalLayout { get; set; }
    public Action? RequestApplyQuickAddProductModalLayout { get; set; }
    public Action<int, string>? RequestFocusLineCell { get; set; }
    public Action<string>? RequestFocusFooterField { get; set; }
    public Action? RequestPrintDocument { get; set; }
    public Action? RequestPrintDeliveryChallan { get; set; }

    public SaleInvoicesViewModel(
        ISaleInvoiceService saleService,
        IToastNotificationService toastService,
        Action<string> navigate,
        IProductService? productService = null)
    {
        _saleService = saleService;
        _toastService = toastService;
        _navigate = navigate;
        _productService = productService;

        InitializeStaticOptions();
        ResetFormState("retail");
    }

    // =========================================================================
    // 1. PAGE MODE STATE (Directory vs Form vs Detail/Show)
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirectoryMode))]
    [NotifyPropertyChangedFor(nameof(IsFormMode))]
    [NotifyPropertyChangedFor(nameof(IsDetailMode))]
    [NotifyPropertyChangedFor(nameof(IsRetailFormMode))]
    [NotifyPropertyChangedFor(nameof(IsWholesaleFormMode))]
    private string _activeViewMode = "directory"; // directory | form | detail

    public bool IsDirectoryMode => string.Equals(ActiveViewMode, "directory", StringComparison.OrdinalIgnoreCase);
    public bool IsFormMode => string.Equals(ActiveViewMode, "form", StringComparison.OrdinalIgnoreCase);
    public bool IsDetailMode => string.Equals(ActiveViewMode, "detail", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRetailFormMode))]
    [NotifyPropertyChangedFor(nameof(IsWholesaleFormMode))]
    [NotifyPropertyChangedFor(nameof(FormBannerTitle))]
    [NotifyPropertyChangedFor(nameof(FormShortcutHintText))]
    [NotifyPropertyChangedFor(nameof(FormModeBadgeText))]
    private string _formSaleType = "retail"; // retail | wholesale

    public bool IsRetailFormMode => IsFormMode && !string.Equals(FormSaleType, "wholesale", StringComparison.OrdinalIgnoreCase);
    public bool IsWholesaleFormMode => IsFormMode && string.Equals(FormSaleType, "wholesale", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(FormBannerTitle))]
    [NotifyPropertyChangedFor(nameof(SubmitButtonText))]
    private long? _editingInvoiceId;

    public bool IsEditMode => EditingInvoiceId is > 0;

    public string FormBannerTitle => IsWholesaleFormMode
        ? (IsEditMode ? "Edit Wholesale Sale Invoice" : "Create Wholesale Sale Invoice")
        : (IsEditMode ? "Edit Retail Sale Invoice" : "Create Retail Sale Invoice");

    public string FormShortcutHintText => IsWholesaleFormMode
        ? "Ctrl+S Save & Print • Ctrl+H Hold & New • Ctrl+D Drafts • Ctrl+N Clear • Enter Next • Shift+Tab Back • ↑↓ Rows"
        : "Ctrl+S Save & Print • Ctrl+H Hold & New • Ctrl+D Drafts • Ctrl+N Clear • Enter Next Cell • ↑↓ Navigate Rows";

    public string FormModeBadgeText => IsWholesaleFormMode ? "WHOLESALE B2B" : "RETAIL POS";
    public string SubmitButtonText => IsEditMode ? "Update Invoice (Ctrl+S)" : "Save & Print (Ctrl+S)";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private SaleStoreSettingsDto _storeSettings = new();

    public bool IsPharmacy => StoreSettings.IsPharmacy;
    public bool HasWholesale => StoreSettings.HasWholesale;

    // =========================================================================
    // 2. DIRECTORY / INDEX STATE & FILTERS
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoryHeaderTitle))]
    [NotifyPropertyChangedFor(nameof(ShowSaleTypeFilterPills))]
    private string _lockedDirectorySaleType = "all"; // all | retail | wholesale

    public string DirectoryHeaderTitle => LockedDirectorySaleType switch
    {
        "wholesale" => "Wholesale Sale Invoices",
        "retail" => "Retail Sale Invoices",
        _ => "Sale Invoices (Retail & Wholesale)"
    };

    public bool ShowSaleTypeFilterPills => HasWholesale && string.Equals(LockedDirectorySaleType, "all", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterAllSaleTypes))]
    [NotifyPropertyChangedFor(nameof(IsFilterRetailOnly))]
    [NotifyPropertyChangedFor(nameof(IsFilterWholesaleOnly))]
    private string _directorySaleTypeFilter = "all"; // all | retail | wholesale

    public bool IsFilterAllSaleTypes => string.Equals(DirectorySaleTypeFilter, "all", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterRetailOnly => string.Equals(DirectorySaleTypeFilter, "retail", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterWholesaleOnly => string.Equals(DirectorySaleTypeFilter, "wholesale", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _directorySearch = string.Empty;

    public ObservableCollection<SaleCustomerFilterOption> CustomerFilterOptions { get; } = [];

    [ObservableProperty]
    private SaleCustomerFilterOption? _selectedCustomerFilter;

    public ObservableCollection<SaleSelectOption> InvoiceTypeFilterOptions { get; } = [];

    [ObservableProperty]
    private SaleSelectOption? _selectedInvoiceTypeFilter;

    public ObservableCollection<SaleSelectOption> StatusFilterOptions { get; } = [];

    [ObservableProperty]
    private SaleSelectOption? _selectedStatusFilter;

    [ObservableProperty]
    private DateTime? _filterFromDate;

    [ObservableProperty]
    private DateTime? _filterToDate;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private int _totalDirectoryCount;

    [ObservableProperty]
    private int _kpiTotalPostedCount;

    [ObservableProperty]
    private int _kpiRetailCount;

    [ObservableProperty]
    private int _kpiWholesaleCount;

    [ObservableProperty]
    private decimal _kpiTotalPostedAmount;

    [ObservableProperty]
    private decimal _kpiTotalReceivedAmount;

    [ObservableProperty]
    private decimal _kpiTotalRemainingReceivable;

    [ObservableProperty]
    private decimal _kpiTotalEstimatedProfit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeldDrafts))]
    private int _heldDraftsCount;

    public bool HasHeldDrafts => HeldDraftsCount > 0;

    public ObservableCollection<SaleInvoiceDirectoryRowDto> DirectoryItems { get; } = [];

    // =========================================================================
    // 3. CREATE / EDIT FORM HEADER & FOOTER STATE
    // =========================================================================

    [ObservableProperty]
    private long? _activeDraftId;

    [ObservableProperty]
    private string _postedNumber = "SI-000001";

    [ObservableProperty]
    private DateTime _invoiceDate = DateTime.Today;

    [ObservableProperty]
    private DateTime? _dueDate;

    public ObservableCollection<SaleCustomerLookupDto> AllCustomers { get; } = [];
    private long _walkInCustomerId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCustomer))]
    [NotifyPropertyChangedFor(nameof(SelectedCustomerDisplayText))]
    [NotifyPropertyChangedFor(nameof(IsWalkInSelected))]
    [NotifyPropertyChangedFor(nameof(ShowRegisteredCustomerInfoStrip))]
    [NotifyPropertyChangedFor(nameof(CustomerCurrentDueText))]
    [NotifyPropertyChangedFor(nameof(CustomerProjectedDueText))]
    [NotifyPropertyChangedFor(nameof(CustomerCreditLimitText))]
    [NotifyPropertyChangedFor(nameof(HasCustomerCustomPrices))]
    [NotifyPropertyChangedFor(nameof(CustomerCustomPricesBadgeText))]
    [NotifyPropertyChangedFor(nameof(HasCustomerDefaultDiscount))]
    [NotifyPropertyChangedFor(nameof(CustomerDefaultDiscountBadgeText))]
    private SaleCustomerLookupDto? _selectedCustomer;

    public bool HasSelectedCustomer => SelectedCustomer != null;
    public string SelectedCustomerDisplayText => SelectedCustomer?.DisplayLabel ?? "Select Customer…";
    public bool IsWalkInSelected => SelectedCustomer == null || SelectedCustomer.IsWalkIn || SelectedCustomer.Id == _walkInCustomerId;
    public bool ShowRegisteredCustomerInfoStrip => SelectedCustomer != null && !IsWalkInSelected;
    public string CustomerCurrentDueText => SelectedCustomer == null ? "Rs. 0" : SelectedCustomer.BalanceSummaryText;
    public string CustomerProjectedDueText => SelectedCustomer == null
        ? "Rs. 0"
        : $"Rs. {Math.Round(SelectedCustomer.CurrentBalance + RemainingDueAmount, 0):N0}";
    public string CustomerCreditLimitText => SelectedCustomer is { CreditLimit: > 0m }
        ? $"Limit: Rs. {SelectedCustomer.CreditLimit:N0}"
        : "No Limit";
    public bool HasCustomerCustomPrices => IsWholesaleFormMode && SelectedCustomer is { CustomPricesCount: > 0 };
    public string CustomerCustomPricesBadgeText => SelectedCustomer != null
        ? $"{SelectedCustomer.CustomPricesCount} Custom Prices"
        : string.Empty;
    public bool HasCustomerDefaultDiscount => SelectedCustomer is { DiscountPercentage: > 0m };
    public string CustomerDefaultDiscountBadgeText => SelectedCustomer != null
        ? $"Disc: {SelectedCustomer.DiscountPercentage:0.##}%"
        : string.Empty;

    [ObservableProperty]
    private string _oneTimeCustomerName = string.Empty;

    [ObservableProperty]
    private string _oneTimeCustomerPhone = string.Empty;

    // Quick Add Customer Inline Panel
    [ObservableProperty]
    private bool _isQuickAddCustomerOpen;

    [ObservableProperty]
    private string _quickCustomerName = string.Empty;

    [ObservableProperty]
    private string _quickCustomerPhone = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDebitInvoice))]
    [NotifyPropertyChangedFor(nameof(IsCreditInvoice))]
    [NotifyPropertyChangedFor(nameof(ShowRemainingDueBanner))]
    [NotifyPropertyChangedFor(nameof(ShowExcessCreditBanner))]
    private string _invoiceType = "debit"; // debit | credit

    public bool IsDebitInvoice => !string.Equals(InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);
    public bool IsCreditInvoice => string.Equals(InvoiceType, "credit", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _doctorName = string.Empty;

    [ObservableProperty]
    private string _patientName = string.Empty;

    [ObservableProperty]
    private string _remarks = string.Empty;

    // Repeater Lines
    public ObservableCollection<SaleInvoiceLineRowViewModel> Lines { get; } = [];

    [ObservableProperty]
    private int _activeItemsCount;

    // Footer Calculations
    [ObservableProperty]
    private decimal _grossAmount;

    [ObservableProperty]
    private decimal _itemDiscountAmount;

    [ObservableProperty]
    private decimal _rowsNetSum;

    [ObservableProperty]
    private string _discountPercentageText = "0";

    [ObservableProperty]
    private string _discountAmountText = "0";

    [ObservableProperty]
    private string _taxPercentageText = "0";

    [ObservableProperty]
    private string _taxAmountText = "0";

    public decimal DiscountPercentage { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxPercentage { get; private set; }
    public decimal TaxAmount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedNetTotal))]
    private decimal _netTotal;

    public string FormattedNetTotal => $"Rs. {Math.Round(NetTotal, 0):N0}";

    [ObservableProperty]
    private string _totalReceiveText = "0";

    public decimal TotalReceive { get; private set; }

    [ObservableProperty]
    private string _cashGivenText = string.Empty;

    public decimal CashGiven { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChangeToReturnBanner))]
    private decimal _changeToReturnAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRemainingDueBanner))]
    [NotifyPropertyChangedFor(nameof(CustomerProjectedDueText))]
    private decimal _remainingDueAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExcessCreditBanner))]
    private decimal _excessCreditAmount;

    public bool ShowChangeToReturnBanner => ChangeToReturnAmount > 0m;
    public bool ShowRemainingDueBanner => IsCreditInvoice && RemainingDueAmount > 0m;
    public bool ShowExcessCreditBanner => IsCreditInvoice && ExcessCreditAmount > 0m;

    // Wholesale Overall Profit & Margin Strip
    [ObservableProperty]
    private decimal _overallEstimatedCost;

    [ObservableProperty]
    private decimal _overallEstimatedProfit;

    [ObservableProperty]
    private decimal _overallMarginPercentage;

    // Payment Accounts
    public ObservableCollection<SaleAccountLookupDto> PaymentAccounts { get; } = [];

    [ObservableProperty]
    private SaleAccountLookupDto? _selectedPaymentAccount;

    [ObservableProperty]
    private bool _isQuickAddAccountOpen;

    [ObservableProperty]
    private string _quickAccountName = string.Empty;

    [ObservableProperty]
    private string _quickAccountType = "cash";

    [ObservableProperty]
    private string _quickAccountNumber = string.Empty;

    // =========================================================================
    // 4. MODALS STATE (CustomerSearch, ProductSearch, BatchSearch, Substitutes, Drafts, Overrides)
    // =========================================================================

    // 4.1 CustomerSearch Modal
    [ObservableProperty]
    private bool _isCustomerSearchModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomerSearchQuery))]
    private string _customerModalSearchText = string.Empty;

    public bool HasCustomerSearchQuery => !string.IsNullOrWhiteSpace(CustomerModalSearchText);
    public bool ShowEmptyCustomerResults => FilteredModalCustomers.Count == 0;
    public string CustomerModalFooterCountText => $"{FilteredModalCustomers.Count} customer{(FilteredModalCustomers.Count == 1 ? "" : "s")}";

    public ObservableCollection<SaleCustomerLookupDto> FilteredModalCustomers { get; } = [];

    [ObservableProperty]
    private SaleCustomerLookupDto? _highlightedModalCustomer;

    // 4.2 Draggable & 8-Way Resizable ProductSearchInput Modal
    [ObservableProperty]
    private bool _isProductSearchModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAllProductsFooterText))]
    [NotifyPropertyChangedFor(nameof(HasProductSearchQuery))]
    [NotifyPropertyChangedFor(nameof(ProductSearchTrimmedQueryText))]
    private string _productModalSearchText = string.Empty;

    public ObservableCollection<SaleProductSearchItemDto> ProductSearchResults { get; } = [];

    [ObservableProperty]
    private SaleProductSearchItemDto? _highlightedModalProduct;

    public bool HasProductSearchResults => ProductSearchResults.Count > 0;
    public bool ShowEmptyProductSearchState => ProductSearchResults.Count == 0;
    public bool ShowAllProductsFooterText => string.IsNullOrWhiteSpace(ProductModalSearchText);
    public bool HasProductSearchQuery => !string.IsNullOrWhiteSpace(ProductModalSearchText);
    public int ProductSearchResultsCount => ProductSearchResults.Count;
    public string ProductSearchTrimmedQueryText => $"\"{ProductModalSearchText?.Trim()}\"";

    private SaleInvoiceLineRowViewModel? _targetLineForProductSearch;

    [ObservableProperty]
    private PurchaseModalWindowLayoutDto _productSearchModalLayout = new() { Width = 960, Height = 600 };

    [ObservableProperty]
    private PurchaseModalWindowLayoutDto _quickAddProductModalLayout = new() { Width = 800, Height = 600 };

    // 4.3 Quick Add Product Modal
    [ObservableProperty]
    private bool _isQuickAddProductModalOpen;

    [ObservableProperty]
    private string _quickAddProductName = string.Empty;

    [ObservableProperty]
    private string _quickAddProductFormulation = string.Empty;

    [ObservableProperty]
    private string _quickAddProductPackSize = "1";

    [ObservableProperty]
    private string _quickAddProductPackPurchasePrice = "0";

    [ObservableProperty]
    private string _quickAddProductPackSalePrice = "0";

    [ObservableProperty]
    private string _quickAddProductWholesalePackPrice = "0";

    [ObservableProperty]
    private string _quickAddProductRack = string.Empty;

    // 4.4 BatchSearchInput Modal
    [ObservableProperty]
    private bool _isBatchSearchModalOpen;

    [ObservableProperty]
    private string _batchModalSearchText = string.Empty;

    [ObservableProperty]
    private string _batchModalProductTitle = string.Empty;

    public ObservableCollection<SaleProductBatchOptionDto> FilteredModalBatches { get; } = [];

    [ObservableProperty]
    private SaleProductBatchOptionDto? _highlightedModalBatch;

    private SaleInvoiceLineRowViewModel? _targetLineForBatchSearch;

    // 4.5 SubstituteProductsModal (Retail Same Generic/Salt Lookup)
    [ObservableProperty]
    private bool _isSubstituteModalOpen;

    [ObservableProperty]
    private string _substituteModalSourceProductText = string.Empty;

    public ObservableCollection<SaleProductSearchItemDto> SubstituteProducts { get; } = [];

    private SaleInvoiceLineRowViewModel? _targetLineForSubstitute;

    // 4.6 Held Drafts Modal (Alt+D)
    [ObservableProperty]
    private bool _isDraftsModalOpen;

    [ObservableProperty]
    private string _draftsModalFilter = "all"; // all | retail | wholesale

    [ObservableProperty]
    private string _draftsModalSearch = string.Empty;

    public ObservableCollection<HeldSaleDraftSummaryDto> HeldDrafts { get; } = [];

    // 4.7 Supervisor Override Modals (Discount / Credit / Expired Stock)
    [ObservableProperty]
    private bool _isDiscountOverrideModalOpen;

    [ObservableProperty]
    private string _discountViolationDetails = string.Empty;

    [ObservableProperty]
    private bool _isCreditOverrideModalOpen;

    [ObservableProperty]
    private string _creditViolationDetails = string.Empty;

    [ObservableProperty]
    private decimal _overrideCurrentBalance;

    [ObservableProperty]
    private decimal _overrideNewBillCredit;

    [ObservableProperty]
    private decimal _overrideProjectedBalance;

    [ObservableProperty]
    private decimal _overrideCreditLimit;

    [ObservableProperty]
    private bool _isExpiredOverrideModalOpen;

    [ObservableProperty]
    private string _expiredBatchDetails = string.Empty;

    [ObservableProperty]
    private string _supervisorUsername = "admin";

    [ObservableProperty]
    private string _supervisorPassword = string.Empty;

    [ObservableProperty]
    private string _supervisorOverrideReason = string.Empty;

    private bool _discountOverrideApproved;
    private bool _creditOverrideApproved;
    private bool _expiredOverrideApproved;

    // 4.8 Pre-Shift Cash Drawer Prompt Modal
    [ObservableProperty]
    private bool _isPreShiftDrawerModalOpen;

    [ObservableProperty]
    private string _openingFloatAmountText = "0";

    // 4.9 Detail / Show & Print View State
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentDetail))]
    [NotifyPropertyChangedFor(nameof(IsPrintThermalMode))]
    [NotifyPropertyChangedFor(nameof(IsPrintA4Mode))]
    private SaleInvoiceDetailDto? _currentDetail;

    public bool HasCurrentDetail => CurrentDetail != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrintThermalMode))]
    [NotifyPropertyChangedFor(nameof(IsPrintA4Mode))]
    [NotifyPropertyChangedFor(nameof(PrintPaperSizeUpper))]
    private string _printPaperSize = "thermal"; // thermal | a4

    public bool IsPrintThermalMode => string.Equals(PrintPaperSize, "thermal", StringComparison.OrdinalIgnoreCase);
    public bool IsPrintA4Mode => !IsPrintThermalMode;
    public string PrintPaperSizeUpper => IsPrintA4Mode ? "A4" : "THERMAL";

    // 4.9b Show Page Modals (SaleInvoiceSearch, RecordPaymentModal, DocumentHistoryModal)
    [ObservableProperty]
    private bool _isShowInvoiceSearchModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShowInvoiceSearchQuery))]
    private string _showInvoiceSearchQuery = string.Empty;

    public bool HasShowInvoiceSearchQuery => !string.IsNullOrWhiteSpace(ShowInvoiceSearchQuery);
    public ObservableCollection<SaleInvoiceDirectoryRowDto> ShowInvoiceSearchResults { get; } = [];

    [ObservableProperty]
    private SaleInvoiceDirectoryRowDto? _highlightedShowSearchInvoice;

    [ObservableProperty]
    private bool _isHistoryModalOpen;

    [ObservableProperty]
    private bool _isRecordPaymentModalOpen;

    [ObservableProperty]
    private string _recordPaymentAmountText = "0";

    [ObservableProperty]
    private SaleAccountLookupDto? _recordPaymentAccount;

    [ObservableProperty]
    private string _recordPaymentReference = string.Empty;

    // 4.10 Reverse & 3-Step Delete Modals
    [ObservableProperty]
    private bool _isReverseModalOpen;

    [ObservableProperty]
    private long? _reversingInvoiceId;

    [ObservableProperty]
    private string _reversingInvoiceNumber = string.Empty;

    [ObservableProperty]
    private string _reversalReason = string.Empty;

    [ObservableProperty]
    private bool _isDeleteModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteStep1))]
    [NotifyPropertyChangedFor(nameof(IsDeleteStep2))]
    [NotifyPropertyChangedFor(nameof(IsDeleteStep3))]
    private int _deleteStep = 1; // 1 = Confirm, 2 = Credit/Refund Choice, 3 = Password

    public bool IsDeleteStep1 => DeleteStep == 1;
    public bool IsDeleteStep2 => DeleteStep == 2;
    public bool IsDeleteStep3 => DeleteStep == 3;

    [ObservableProperty]
    private long? _deletingInvoiceId;

    [ObservableProperty]
    private string _deletingInvoiceNumber = string.Empty;

    [ObservableProperty]
    private decimal _deletingInvoiceTotal;

    [ObservableProperty]
    private decimal _deletingInvoiceReceived;

    [ObservableProperty]
    private decimal _deletingInvoiceRemaining;

    [ObservableProperty]
    private bool _deletingInvoiceNeedsChoice;

    [ObservableProperty]
    private string _deletePassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteModeCredit))]
    [NotifyPropertyChangedFor(nameof(IsDeleteModeRefund))]
    private string _deleteRefundMode = "credit"; // credit | refund | none

    public bool IsDeleteModeCredit => string.Equals(DeleteRefundMode, "credit", StringComparison.OrdinalIgnoreCase);
    public bool IsDeleteModeRefund => string.Equals(DeleteRefundMode, "refund", StringComparison.OrdinalIgnoreCase);

    // =========================================================================
    // 5. NAVIGATION ENTRY POINTS (Called by MainViewModel)
    // =========================================================================

    public async Task OpenDirectoryModeAsync(string? defaultSaleType = null)
    {
        await EnsureBootstrappedAsync();

        var normalizedLock = string.IsNullOrWhiteSpace(defaultSaleType)
            ? "all"
            : defaultSaleType.Trim().ToLowerInvariant();

        if (normalizedLock == "wholesale" && !HasWholesale)
        {
            _toastService.ShowWarning("Wholesale sales are disabled in store settings.", "Retail Only");
            normalizedLock = "retail";
        }

        LockedDirectorySaleType = normalizedLock;
        DirectorySaleTypeFilter = normalizedLock;
        ActiveViewMode = "directory";
        await LoadDirectoryPageAsync(1);
    }

    public async Task OpenCreateModeAsync(string saleType = "retail")
    {
        await EnsureBootstrappedAsync();

        var normalizedMode = string.Equals(saleType, "wholesale", StringComparison.OrdinalIgnoreCase)
            ? "wholesale"
            : "retail";

        if (normalizedMode == "wholesale" && !HasWholesale)
        {
            _toastService.ShowWarning("Wholesale sales are disabled. Opening Retail Sale Invoice.", "Retail Only");
            normalizedMode = "retail";
        }

        var nextNo = await _saleService.GenerateNextPostedNumberAsync();
        ResetFormState(normalizedMode, nextNo);
        ActiveViewMode = "form";

        // Requirement 1: When Sale Invoice Create loads, immediately open the ProductSearchInput modal!
        if (Lines.Count == 0)
        {
            AppendEmptyLine();
        }
        await OpenProductSearchForLineAsync(Lines[0]);
    }

    public async Task OpenEditModeAsync(long invoiceId)
    {
        await EnsureBootstrappedAsync();
        IsBusy = true;
        try
        {
            var detail = await _saleService.GetByIdAsync(invoiceId);
            if (detail == null)
            {
                _toastService.ShowError($"Sale invoice #{invoiceId} was not found.", "Not Found");
                await OpenDirectoryModeAsync();
                return;
            }

            if (detail.IsReversed)
            {
                _toastService.ShowWarning("Reversed sale invoices cannot be edited.", "Reversed Invoice");
                await OpenDetailModeAsync(invoiceId);
                return;
            }

            LoadFormFromDetail(detail);
            ActiveViewMode = "form";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenDetailModeAsync(long invoiceId, bool autoPrint = false)
    {
        await EnsureBootstrappedAsync();
        IsBusy = true;
        try
        {
            var detail = await _saleService.GetByIdAsync(invoiceId);
            if (detail == null)
            {
                _toastService.ShowError($"Sale invoice #{invoiceId} was not found.", "Not Found");
                return;
            }

            CurrentDetail = detail;
            PrintPaperSize = detail.IsWholesale ? "a4" : StoreSettings.PrinterType;
            ActiveViewMode = "detail";

            if (autoPrint)
            {
                RequestPrintDocument?.Invoke();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // 6. DIRECTORY COMMANDS & FILTERING
    // =========================================================================

    private void InitializeStaticOptions()
    {
        CustomerFilterOptions.Add(new SaleCustomerFilterOption { CustomerId = null, Label = "All Customers" });

        InvoiceTypeFilterOptions.Add(new SaleSelectOption { Key = "all", Label = "All Types (Debit & Credit)" });
        InvoiceTypeFilterOptions.Add(new SaleSelectOption { Key = "debit", Label = "Debit (Cash Counter)" });
        InvoiceTypeFilterOptions.Add(new SaleSelectOption { Key = "credit", Label = "Credit (Customer Ledger)" });
        SelectedInvoiceTypeFilter = InvoiceTypeFilterOptions[0];

        StatusFilterOptions.Add(new SaleSelectOption { Key = "all", Label = "All Statuses" });
        StatusFilterOptions.Add(new SaleSelectOption { Key = "posted", Label = "Posted Only" });
        StatusFilterOptions.Add(new SaleSelectOption { Key = "reversed", Label = "Reversed (Voided)" });
        SelectedStatusFilter = StatusFilterOptions[0];
    }

    private async Task EnsureBootstrappedAsync()
    {
        var boot = await _saleService.GetFormBootstrapAsync();
        StoreSettings = boot.StoreSettings;
        OnPropertyChanged(nameof(IsPharmacy));
        OnPropertyChanged(nameof(HasWholesale));

        _walkInCustomerId = boot.WalkInCustomerId;
        AllCustomers.Clear();
        CustomerFilterOptions.Clear();
        CustomerFilterOptions.Add(new SaleCustomerFilterOption { CustomerId = null, Label = "All Customers" });

        foreach (var c in boot.Customers)
        {
            AllCustomers.Add(c);
            CustomerFilterOptions.Add(new SaleCustomerFilterOption { CustomerId = c.Id, Label = c.DisplayLabel });
        }
        SelectedCustomerFilter ??= CustomerFilterOptions[0];

        PaymentAccounts.Clear();
        foreach (var acc in boot.Accounts)
        {
            PaymentAccounts.Add(acc);
        }
        SelectedPaymentAccount ??= PaymentAccounts.FirstOrDefault(a => a.IsDefault) ?? PaymentAccounts.FirstOrDefault();

        HeldDraftsCount = boot.HeldDraftsCount;
        ProductSearchModalLayout = boot.ProductSearchModalLayout;
        QuickAddProductModalLayout = boot.QuickAddProductModalLayout;
        _isBootstrapLoaded = true;
    }

    [RelayCommand]
    public async Task LoadDirectoryPageAsync(int page = 1)
    {
        if (!_isBootstrapLoaded)
            await EnsureBootstrappedAsync();

        IsBusy = true;
        try
        {
            var pageDto = await _saleService.GetDirectoryPageAsync(new SaleInvoiceFilterQuery
            {
                Search = DirectorySearch,
                CustomerId = SelectedCustomerFilter?.CustomerId,
                SaleType = DirectorySaleTypeFilter,
                InvoiceType = SelectedInvoiceTypeFilter?.Key ?? "all",
                Status = SelectedStatusFilter?.Key ?? "all",
                FromDate = FilterFromDate,
                ToDate = FilterToDate,
                Page = page,
                PageSize = 25
            });

            DirectoryItems.Clear();
            foreach (var row in pageDto.Items)
            {
                DirectoryItems.Add(row);
            }

            CurrentPage = pageDto.Page;
            TotalPages = pageDto.TotalPages;
            TotalDirectoryCount = pageDto.TotalCount;

            KpiTotalPostedCount = pageDto.TotalPostedCount;
            KpiRetailCount = pageDto.RetailCount;
            KpiWholesaleCount = pageDto.WholesaleCount;
            KpiTotalPostedAmount = pageDto.TotalPostedAmount;
            KpiTotalReceivedAmount = pageDto.TotalReceivedAmount;
            KpiTotalRemainingReceivable = pageDto.TotalRemainingReceivable;
            KpiTotalEstimatedProfit = pageDto.TotalEstimatedProfit;
            HeldDraftsCount = pageDto.HeldDraftsCount;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SetDirectorySaleTypeFilterAsync(string? saleType)
    {
        DirectorySaleTypeFilter = string.IsNullOrWhiteSpace(saleType) ? "all" : saleType.Trim().ToLowerInvariant();
        await LoadDirectoryPageAsync(1);
    }

    [RelayCommand]
    public async Task ClearDirectoryFiltersAsync()
    {
        DirectorySearch = string.Empty;
        SelectedCustomerFilter = CustomerFilterOptions.FirstOrDefault();
        SelectedInvoiceTypeFilter = InvoiceTypeFilterOptions.FirstOrDefault();
        SelectedStatusFilter = StatusFilterOptions.FirstOrDefault();
        FilterFromDate = null;
        FilterToDate = null;
        DirectorySaleTypeFilter = LockedDirectorySaleType;
        await LoadDirectoryPageAsync(1);
    }

    [RelayCommand]
    public async Task NextPageAsync()
    {
        if (CurrentPage < TotalPages)
            await LoadDirectoryPageAsync(CurrentPage + 1);
    }

    [RelayCommand]
    public async Task PreviousPageAsync()
    {
        if (CurrentPage > 1)
            await LoadDirectoryPageAsync(CurrentPage - 1);
    }

    [RelayCommand]
    public void NavigateToCreateRetail() => _navigate("/sale-invoices/create/retail");

    [RelayCommand]
    public void NavigateToCreateWholesale() => _navigate("/sale-invoices/create/wholesale");

    [RelayCommand]
    public void NavigateBackToList()
    {
        if (string.Equals(FormSaleType, "wholesale", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(LockedDirectorySaleType, "wholesale", StringComparison.OrdinalIgnoreCase))
        {
            _navigate("/wholesale-invoices");
        }
        else
        {
            _navigate("/sale-invoices");
        }
    }

    [RelayCommand]
    public async Task ViewInvoiceDetailAsync(SaleInvoiceDirectoryRowDto? row)
    {
        if (row == null) return;
        await OpenDetailModeAsync(row.Id, autoPrint: false);
    }

    [RelayCommand]
    public async Task PrintInvoiceFromDirectoryAsync(SaleInvoiceDirectoryRowDto? row)
    {
        if (row == null) return;
        await OpenDetailModeAsync(row.Id, autoPrint: true);
    }

    [RelayCommand]
    public async Task EditInvoiceFromDirectoryAsync(SaleInvoiceDirectoryRowDto? row)
    {
        if (row == null) return;
        await OpenEditModeAsync(row.Id);
    }

    [RelayCommand]
    public async Task EditCurrentDetailInvoiceAsync()
    {
        if (CurrentDetail == null) return;
        await OpenEditModeAsync(CurrentDetail.Id);
    }

    [RelayCommand]
    public void PrintCurrentDetailInvoice()
    {
        RequestPrintDocument?.Invoke();
    }

    [RelayCommand]
    public void PrintDeliveryChallan()
    {
        RequestPrintDeliveryChallan?.Invoke();
    }

    [RelayCommand]
    public void CreateNewFromShowMode()
    {
        if (CurrentDetail?.IsWholesale == true)
        {
            NavigateToCreateWholesale();
        }
        else
        {
            NavigateToCreateRetail();
        }
    }

    [RelayCommand]
    public void SetPrintPaperSize(string? size)
    {
        PrintPaperSize = string.Equals(size, "a4", StringComparison.OrdinalIgnoreCase) ? "a4" : "thermal";
    }

    // =========================================================================
    // 7. FORM STATE, REPEATER ROWS & FOOTER CALCULATIONS
    // =========================================================================

    private void ResetFormState(string saleType, string? nextPostedNo = null)
    {
        EditingInvoiceId = null;
        ActiveDraftId = null;
        FormSaleType = string.Equals(saleType, "wholesale", StringComparison.OrdinalIgnoreCase) ? "wholesale" : "retail";
        PostedNumber = !string.IsNullOrWhiteSpace(nextPostedNo) ? nextPostedNo : "SI-000001";
        InvoiceDate = DateTime.Today;
        DueDate = null;

        SelectedCustomer = AllCustomers.FirstOrDefault(c => c.IsWalkIn) ?? AllCustomers.FirstOrDefault();
        OneTimeCustomerName = string.Empty;
        OneTimeCustomerPhone = string.Empty;
        IsQuickAddCustomerOpen = false;
        QuickCustomerName = string.Empty;
        QuickCustomerPhone = string.Empty;

        InvoiceType = "debit";
        DoctorName = string.Empty;
        PatientName = string.Empty;
        Remarks = string.Empty;

        _discountOverrideApproved = false;
        _creditOverrideApproved = false;
        _expiredOverrideApproved = false;

        _suppressFooterRecalc = true;
        try
        {
            DiscountPercentage = 0m;
            DiscountAmount = 0m;
            TaxPercentage = 0m;
            TaxAmount = 0m;
            DiscountPercentageText = "0";
            DiscountAmountText = "0";
            TaxPercentageText = "0";
            TaxAmountText = "0";
            TotalReceive = 0m;
            TotalReceiveText = "0";
            CashGiven = 0m;
            CashGivenText = string.Empty;
        }
        finally
        {
            _suppressFooterRecalc = false;
        }

        SelectedPaymentAccount = PaymentAccounts.FirstOrDefault(a => a.IsDefault) ?? PaymentAccounts.FirstOrDefault();

        Lines.Clear();
        AppendEmptyLine();
        RecalculateFooter(null);
    }

    private void LoadFormFromDetail(SaleInvoiceDetailDto detail)
    {
        EditingInvoiceId = detail.Id;
        ActiveDraftId = null;
        FormSaleType = detail.IsWholesale ? "wholesale" : "retail";
        PostedNumber = detail.PostedNumber;
        InvoiceDate = detail.Date;
        DueDate = detail.DueDate;

        SelectedCustomer = AllCustomers.FirstOrDefault(c => c.Id == detail.CustomerId)
                           ?? AllCustomers.FirstOrDefault();
        OneTimeCustomerName = string.Empty;
        OneTimeCustomerPhone = string.Empty;
        InvoiceType = detail.InvoiceType;
        DoctorName = detail.DoctorName;
        PatientName = detail.PatientName;
        Remarks = detail.Remarks;

        _discountOverrideApproved = true;
        _creditOverrideApproved = true;
        _expiredOverrideApproved = true;

        Lines.Clear();
        int idx = 1;
        foreach (var itemDto in detail.Items)
        {
            var rowVm = new SaleInvoiceLineRowViewModel(idx++, () => FormSaleType, _ => RecalculateFooter(null));
            rowVm.LoadFromDto(itemDto, FormSaleType);
            Lines.Add(rowVm);
        }
        if (Lines.Count == 0)
        {
            AppendEmptyLine();
        }

        _suppressFooterRecalc = true;
        try
        {
            DiscountPercentage = detail.DiscountPercentage;
            DiscountAmount = detail.DiscountAmount;
            TaxPercentage = detail.TaxPercentage;
            TaxAmount = detail.TaxAmount;
            TotalReceive = detail.TotalReceive;

            DiscountPercentageText = DiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            DiscountAmountText = DiscountAmount.ToString("0.##", CultureInfo.InvariantCulture);
            TaxPercentageText = TaxPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            TaxAmountText = TaxAmount.ToString("0.##", CultureInfo.InvariantCulture);
            TotalReceiveText = Math.Round(TotalReceive, 0).ToString("0", CultureInfo.InvariantCulture);
            CashGiven = 0m;
            CashGivenText = string.Empty;
        }
        finally
        {
            _suppressFooterRecalc = false;
        }

        if (detail.AccountId.HasValue)
        {
            SelectedPaymentAccount = PaymentAccounts.FirstOrDefault(a => a.Id == detail.AccountId.Value)
                                     ?? SelectedPaymentAccount;
        }

        RecalculateFooter(IsCreditInvoice ? "total_receive" : null);
    }

    [RelayCommand]
    public void SwitchFormSaleType(string? targetSaleType)
    {
        var desired = string.Equals(targetSaleType, "wholesale", StringComparison.OrdinalIgnoreCase)
            ? "wholesale"
            : "retail";

        if (desired == "wholesale" && !HasWholesale)
        {
            _toastService.ShowWarning("Wholesale sales are disabled in store settings.", "Retail Only");
            return;
        }

        if (string.Equals(FormSaleType, desired, StringComparison.OrdinalIgnoreCase))
            return;

        FormSaleType = desired;
        foreach (var line in Lines)
        {
            line.Recalculate(null);
        }
        RecalculateFooter(null);
    }

    [RelayCommand]
    public void SetInvoiceType(string? type)
    {
        var desired = string.Equals(type, "credit", StringComparison.OrdinalIgnoreCase) ? "credit" : "debit";
        if (desired == "credit" && IsWalkInSelected && string.IsNullOrWhiteSpace(OneTimeCustomerName))
        {
            _toastService.ShowInfo(
                "Credit sale: Select a registered customer or enter Walk-in Customer Name & Phone.",
                "Customer Required for Udhar");
            RequestFocusOneTimeCustomerName?.Invoke();
        }

        var prev = InvoiceType;
        InvoiceType = desired;

        if (!string.Equals(prev, desired, StringComparison.OrdinalIgnoreCase))
        {
            _suppressFooterRecalc = true;
            try
            {
                if (desired == "credit")
                {
                    TotalReceive = 0m;
                    TotalReceiveText = "0";
                }
                else
                {
                    TotalReceive = NetTotal;
                    TotalReceiveText = Math.Round(NetTotal, 0).ToString("0", CultureInfo.InvariantCulture);
                }
            }
            finally
            {
                _suppressFooterRecalc = false;
            }
            RecalculateFooter(desired == "credit" ? "total_receive" : null);
        }
    }

    public SaleInvoiceLineRowViewModel AppendEmptyLine()
    {
        var row = new SaleInvoiceLineRowViewModel(Lines.Count + 1, () => FormSaleType, _ => RecalculateFooter(null));
        Lines.Add(row);
        RenumberLines();
        return row;
    }

    [RelayCommand]
    public void RemoveLine(SaleInvoiceLineRowViewModel? row)
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
        }
        RecalculateFooter(null);
    }

    private void RenumberLines()
    {
        for (int i = 0; i < Lines.Count; i++)
        {
            Lines[i].RowNumber = i + 1;
        }
    }

    partial void OnDiscountPercentageTextChanged(string value)
    {
        if (_suppressFooterRecalc) return;
        DiscountPercentage = ParseDecimal(value, allowNegative: true);
        RecalculateFooter("discount_percentage");
    }

    partial void OnDiscountAmountTextChanged(string value)
    {
        if (_suppressFooterRecalc) return;
        DiscountAmount = ParseDecimal(value, allowNegative: true);
        RecalculateFooter("discount_amount");
    }

    partial void OnTaxPercentageTextChanged(string value)
    {
        if (_suppressFooterRecalc) return;
        TaxPercentage = ParseDecimal(value);
        RecalculateFooter("tax_percentage");
    }

    partial void OnTaxAmountTextChanged(string value)
    {
        if (_suppressFooterRecalc) return;
        TaxAmount = ParseDecimal(value);
        RecalculateFooter("tax_amount");
    }

    partial void OnTotalReceiveTextChanged(string value)
    {
        if (_suppressFooterRecalc) return;
        TotalReceive = Math.Max(0m, ParseDecimal(value));
        RecalculateFooter("total_receive");
    }

    partial void OnCashGivenTextChanged(string value)
    {
        if (_suppressFooterRecalc) return;
        CashGiven = Math.Max(0m, ParseDecimal(value));
        RecalculatePaymentChangeAndDue();
    }

    [RelayCommand]
    public void SetQuickCashGiven(string? preset)
    {
        if (string.Equals(preset, "exact", StringComparison.OrdinalIgnoreCase))
        {
            var target = IsCreditInvoice && TotalReceive > 0m ? TotalReceive : NetTotal;
            CashGivenText = Math.Round(target, 0).ToString("0", CultureInfo.InvariantCulture);
        }
        else if (decimal.TryParse(preset, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
        {
            CashGivenText = Math.Round(amt, 0).ToString("0", CultureInfo.InvariantCulture);
        }
    }

    public void RecalculateFooter(string? changedField)
    {
        if (_suppressFooterRecalc) return;

        _suppressFooterRecalc = true;
        try
        {
            var activeRows = Lines.Where(l => l.HasProduct).ToList();
            ActiveItemsCount = activeRows.Count;

            var calcItems = activeRows.Select(l => new SaleLineCalcModel
            {
                PackSize = l.PackSize,
                Quantity = l.Quantity,
                Price = l.Price,
                ItemDiscountPercentage = l.ItemDiscountPercentage
            }).ToList();

            var footer = new SaleFooterCalcModel
            {
                InvoiceType = InvoiceType,
                DiscountPercentage = DiscountPercentage,
                DiscountAmount = DiscountAmount,
                TaxPercentage = TaxPercentage,
                TaxAmount = TaxAmount,
                TotalReceive = TotalReceive,
                PreviousBalance = SelectedCustomer?.CurrentBalance ?? 0m
            };

            InvoiceCalculators.RecalcSaleFooter(calcItems, footer, changedField);

            GrossAmount = footer.GrossAmount;
            ItemDiscountAmount = footer.ItemDiscount;
            RowsNetSum = Math.Round(footer.GrossAmount - footer.ItemDiscount, 2);
            DiscountPercentage = footer.DiscountPercentage;
            DiscountAmount = footer.DiscountAmount;
            TaxPercentage = footer.TaxPercentage;
            TaxAmount = footer.TaxAmount;
            NetTotal = footer.Total;
            TotalReceive = footer.TotalReceive;

            if (changedField != "discount_percentage")
                DiscountPercentageText = DiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            if (changedField != "discount_amount")
                DiscountAmountText = DiscountAmount.ToString("0.##", CultureInfo.InvariantCulture);
            if (changedField != "tax_percentage")
                TaxPercentageText = TaxPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            if (changedField != "tax_amount")
                TaxAmountText = TaxAmount.ToString("0.##", CultureInfo.InvariantCulture);
            if (changedField != "total_receive")
                TotalReceiveText = Math.Round(TotalReceive, 0).ToString("0", CultureInfo.InvariantCulture);

            // Wholesale overall profit & margin
            OverallEstimatedCost = Math.Round(activeRows.Sum(r => r.EstimatedRowCost), 2);
            OverallEstimatedProfit = Math.Round(NetTotal - OverallEstimatedCost, 2);
            OverallMarginPercentage = NetTotal > 0m
                ? Math.Round((OverallEstimatedProfit / NetTotal) * 100m, 1)
                : 0m;

            RecalculatePaymentChangeAndDue();
        }
        finally
        {
            _suppressFooterRecalc = false;
        }
    }

    private void RecalculatePaymentChangeAndDue()
    {
        var billTotal = Math.Round(NetTotal, 0);
        var paidNow = IsCreditInvoice ? Math.Round(TotalReceive, 0) : billTotal;
        var tendered = Math.Round(CashGiven, 0);

        if (IsCreditInvoice)
        {
            ChangeToReturnAmount = tendered > paidNow && paidNow > 0m ? tendered - paidNow : 0m;
            RemainingDueAmount = Math.Max(0m, billTotal - paidNow);
            ExcessCreditAmount = paidNow > billTotal ? paidNow - billTotal : 0m;
        }
        else
        {
            ChangeToReturnAmount = tendered > billTotal && billTotal > 0m ? tendered - billTotal : 0m;
            RemainingDueAmount = 0m;
            ExcessCreditAmount = 0m;
        }
    }

    // =========================================================================
    // 8. CUSTOMER SEARCH MODAL & QUICK ADD CUSTOMER
    // =========================================================================

    [RelayCommand]
    public void OpenCustomerSearchModal(string? seedText = null)
    {
        CustomerModalSearchText = seedText ?? string.Empty;
        RefreshFilteredModalCustomers();
        IsCustomerSearchModalOpen = true;
        RequestFocusCustomerModalSearch?.Invoke();
    }

    [RelayCommand]
    public void CloseCustomerSearchModal()
    {
        IsCustomerSearchModalOpen = false;
    }

    [RelayCommand]
    public void ClearCustomerSearchQuery()
    {
        CustomerModalSearchText = string.Empty;
    }

    partial void OnCustomerModalSearchTextChanged(string value)
    {
        RefreshFilteredModalCustomers();
    }

    private void RefreshFilteredModalCustomers()
    {
        var term = CustomerModalSearchText?.Trim() ?? string.Empty;
        FilteredModalCustomers.Clear();

        var matches = string.IsNullOrWhiteSpace(term)
            ? AllCustomers
            : AllCustomers.Where(c =>
                c.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.Phone.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.Area.Contains(term, StringComparison.OrdinalIgnoreCase));

        foreach (var c in matches)
        {
            FilteredModalCustomers.Add(c);
        }

        HighlightedModalCustomer = FilteredModalCustomers.FirstOrDefault();
        OnPropertyChanged(nameof(ShowEmptyCustomerResults));
        OnPropertyChanged(nameof(CustomerModalFooterCountText));
    }

    public void MoveCustomerModalHighlight(int delta)
    {
        if (FilteredModalCustomers.Count == 0) return;
        int cur = HighlightedModalCustomer != null ? FilteredModalCustomers.IndexOf(HighlightedModalCustomer) : 0;
        int next = Math.Clamp(cur + delta, 0, FilteredModalCustomers.Count - 1);
        HighlightedModalCustomer = FilteredModalCustomers[next];
    }

    public void CommitCustomerModalSelection(SaleCustomerLookupDto? explicitCustomer = null)
    {
        var chosen = explicitCustomer ?? HighlightedModalCustomer;
        if (chosen == null) return;

        SelectedCustomer = chosen;
        IsCustomerSearchModalOpen = false;

        // Refresh custom wholesale prices on existing rows if in wholesale mode
        if (IsWholesaleFormMode)
        {
            foreach (var row in Lines.Where(r => r.HasProduct))
            {
                if (chosen.CustomWholesalePrices.TryGetValue(row.ProductId, out var customPack) && customPack > 0m)
                {
                    row.PackPriceText = customPack.ToString("0.##", CultureInfo.InvariantCulture);
                }
            }
        }

        RecalculateFooter(null);

        if (chosen.IsWalkIn)
        {
            RequestFocusOneTimeCustomerName?.Invoke();
        }
        else
        {
            if (Lines.Count == 0)
                AppendEmptyLine();
            _ = OpenProductSearchForLineAsync(Lines[0]);
        }
    }

    [RelayCommand]
    public void ResetToWalkInCustomer()
    {
        SelectedCustomer = AllCustomers.FirstOrDefault(c => c.IsWalkIn) ?? AllCustomers.FirstOrDefault();
        OneTimeCustomerName = string.Empty;
        OneTimeCustomerPhone = string.Empty;
        if (IsCreditInvoice)
        {
            SetInvoiceType("debit");
        }
    }

    [RelayCommand]
    public void ToggleQuickAddCustomer()
    {
        IsQuickAddCustomerOpen = !IsQuickAddCustomerOpen;
        if (IsQuickAddCustomerOpen)
        {
            QuickCustomerName = string.Empty;
            QuickCustomerPhone = string.Empty;
        }
    }

    [RelayCommand]
    public async Task SaveQuickCustomerAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickCustomerName))
        {
            _toastService.ShowWarning("Please enter Customer Name.", "Validation");
            return;
        }

        try
        {
            var created = await _saleService.QuickCreateCustomerAsync(
                QuickCustomerName,
                QuickCustomerPhone,
                FormSaleType);

            AllCustomers.Add(created);
            SelectedCustomer = created;
            IsQuickAddCustomerOpen = false;
            _toastService.ShowSuccess($"Customer '{created.Name}' added and selected.", "Customer Created");
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Failed to Create Customer");
        }
    }

    [RelayCommand]
    public void ToggleQuickAddAccount()
    {
        IsQuickAddAccountOpen = !IsQuickAddAccountOpen;
        if (IsQuickAddAccountOpen)
        {
            QuickAccountName = string.Empty;
            QuickAccountType = "cash";
            QuickAccountNumber = string.Empty;
        }
    }

    [RelayCommand]
    public async Task SaveQuickAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickAccountName))
        {
            _toastService.ShowWarning("Please enter Account Name.", "Validation");
            return;
        }

        try
        {
            var created = await _saleService.QuickCreatePaymentAccountAsync(
                QuickAccountName,
                QuickAccountType,
                QuickAccountNumber);

            PaymentAccounts.Add(created);
            SelectedPaymentAccount = created;
            IsQuickAddAccountOpen = false;
            _toastService.ShowSuccess($"Payment account '{created.Name}' created.", "Account Created");
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Failed to Create Account");
        }
    }

    // =========================================================================
    // 9. PRODUCT SEARCH MODAL, BATCH MODAL & SUBSTITUTE MODAL
    // =========================================================================

    [RelayCommand]
    public async Task OpenProductSearchForLineCommandAsync(SaleInvoiceLineRowViewModel? line)
    {
        await OpenProductSearchForLineAsync(line, null);
    }

    [RelayCommand]
    public async Task AppendRowAndOpenProductModalAsync()
    {
        var row = AppendEmptyLine();
        await OpenProductSearchForLineAsync(row, null);
    }

    public async Task OpenProductSearchForLineAsync(SaleInvoiceLineRowViewModel? line, string? initialSearch = null)
    {
        _targetLineForProductSearch = line ?? Lines.FirstOrDefault() ?? AppendEmptyLine();
        ProductModalSearchText = initialSearch ?? string.Empty;
        await SearchProductsInModalAsync(ProductModalSearchText);
        IsProductSearchModalOpen = true;
        RequestApplyProductSearchModalLayout?.Invoke();
        RequestFocusProductModalSearch?.Invoke();
    }

    [RelayCommand]
    public void CloseProductSearchModal()
    {
        IsProductSearchModalOpen = false;
    }

    [RelayCommand]
    public void ClearProductSearchQuery()
    {
        ProductModalSearchText = string.Empty;
    }

    partial void OnProductModalSearchTextChanged(string value)
    {
        if (!IsProductSearchModalOpen) return;
        _ = SearchProductsInModalAsync(value);
    }

    private async Task SearchProductsInModalAsync(string? query)
    {
        var items = await _saleService.SearchProductsForSaleAsync(
            query,
            SelectedCustomer?.Id,
            limit: 150);

        ProductSearchResults.Clear();
        foreach (var item in items)
        {
            ProductSearchResults.Add(item);
        }

        HighlightedModalProduct = ProductSearchResults.FirstOrDefault();
        OnPropertyChanged(nameof(HasProductSearchResults));
        OnPropertyChanged(nameof(ShowEmptyProductSearchState));
        OnPropertyChanged(nameof(ProductSearchResultsCount));
    }

    public void MoveProductModalHighlight(int delta)
    {
        if (ProductSearchResults.Count == 0) return;
        int cur = HighlightedModalProduct != null ? ProductSearchResults.IndexOf(HighlightedModalProduct) : 0;
        int next = Math.Clamp(cur + delta, 0, ProductSearchResults.Count - 1);
        HighlightedModalProduct = ProductSearchResults[next];
    }

    public void CommitProductModalSelection(SaleProductSearchItemDto? explicitProduct = null)
    {
        var chosen = explicitProduct ?? HighlightedModalProduct;
        if (chosen == null) return;

        var targetLine = _targetLineForProductSearch ?? Lines.LastOrDefault() ?? AppendEmptyLine();
        int rowIndex = Lines.IndexOf(targetLine);
        if (rowIndex < 0)
        {
            rowIndex = 0;
            targetLine = Lines[0];
        }

        targetLine.ApplySelectedProduct(
            chosen,
            FormSaleType,
            SelectedCustomer?.CustomWholesalePrices,
            SelectedCustomer?.DiscountPercentage ?? 0m);

        if (targetLine.IsExpiredBatch)
        {
            _toastService.ShowWarning(
                $"Only expired batch ({targetLine.BatchNumber}) is available for '{chosen.Name}'.",
                "Expired Batch Warning");
        }

        IsProductSearchModalOpen = false;

        // Just like the web app: after selecting product & auto-selecting FEFO batch, jump straight to Quantity / Pack.Q!
        var nextField = IsWholesaleFormMode ? "pack_quantity" : "quantity";
        RequestFocusLineCell?.Invoke(rowIndex, nextField);
    }

    public async Task SaveProductSearchModalLayoutAsync(double x, double y, double width, double height)
    {
        ProductSearchModalLayout = new PurchaseModalWindowLayoutDto
        {
            X = x,
            Y = y,
            Width = width,
            Height = height
        };
        await _saleService.SaveModalWindowLayoutAsync("productSearchModal", ProductSearchModalLayout);
    }

    public async Task SaveQuickAddProductModalLayoutAsync(double x, double y, double width, double height)
    {
        QuickAddProductModalLayout = new PurchaseModalWindowLayoutDto
        {
            X = x,
            Y = y,
            Width = width,
            Height = height
        };
        await _saleService.SaveModalWindowLayoutAsync("productModal", QuickAddProductModalLayout);
    }

    [RelayCommand]
    public void OpenQuickAddProductModal()
    {
        QuickAddProductName = ProductModalSearchText?.Trim() ?? string.Empty;
        QuickAddProductFormulation = string.Empty;
        QuickAddProductPackSize = "1";
        QuickAddProductPackPurchasePrice = "0";
        QuickAddProductPackSalePrice = "0";
        QuickAddProductWholesalePackPrice = "0";
        QuickAddProductRack = string.Empty;

        IsQuickAddProductModalOpen = true;
        RequestApplyQuickAddProductModalLayout?.Invoke();
        RequestFocusQuickAddProductName?.Invoke();
    }

    [RelayCommand]
    public void CloseQuickAddProductModal()
    {
        IsQuickAddProductModalOpen = false;
    }

    [RelayCommand]
    public async Task SaveQuickAddProductAsync()
    {
        if (_productService == null)
        {
            _toastService.ShowError("Product service is unavailable.", "Error");
            return;
        }

        var cleanName = QuickAddProductName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            _toastService.ShowWarning("Product name is required.", "Validation");
            return;
        }

        int packSize = Math.Max(1, int.TryParse(QuickAddProductPackSize, out var ps) ? ps : 1);
        decimal packPur = ParseDecimal(QuickAddProductPackPurchasePrice);
        decimal packSale = ParseDecimal(QuickAddProductPackSalePrice);
        decimal wsPack = ParseDecimal(QuickAddProductWholesalePackPrice);
        if (wsPack <= 0m) wsPack = packSale;

        var lookups = await _productService.GetProductFormLookupsAsync();
        var codes = await _productService.GenerateNewCodeAndBarcodeAsync();

        var res = await _productService.CreateProductAsync(new ProductUpsertRequest
        {
            ProductCode = codes.ProductCode,
            Barcode = codes.Barcode,
            Name = cleanName,
            Formulation = QuickAddProductFormulation?.Trim(),
            PackSize = packSize,
            PackPurchasePrice = packPur,
            UnitPurchasePrice = Math.Round(packPur / packSize, 2),
            PackSalePrice = packSale,
            UnitSalePrice = Math.Round(packSale / packSize, 2),
            WholeSalePackPrice = wsPack,
            WholeSaleUnitPrice = Math.Round(wsPack / packSize, 2),
            Rack = QuickAddProductRack?.Trim(),
            CategoryId = lookups.Categories.FirstOrDefault()?.Id,
            BrandId = lookups.Brands.FirstOrDefault()?.Id,
            SupplierId = lookups.Suppliers.FirstOrDefault()?.Id
        });

        if (!res.Succeeded)
        {
            _toastService.ShowError(res.Message, "Failed to Create Product");
            return;
        }

        IsQuickAddProductModalOpen = false;
        _toastService.ShowSuccess($"Product '{cleanName}' created.", "Product Added");
        await SearchProductsInModalAsync(cleanName);
        if (ProductSearchResults.Count > 0)
        {
            CommitProductModalSelection(ProductSearchResults[0]);
        }
    }

    // Batch Search Modal
    [RelayCommand]
    public async Task OpenBatchSearchForLineAsync(SaleInvoiceLineRowViewModel? line)
    {
        if (line == null || !line.HasProduct)
        {
            await OpenProductSearchForLineAsync(line);
            return;
        }

        _targetLineForBatchSearch = line;
        BatchModalProductTitle = $"{line.ProductName} (1×{line.PackSize})";
        BatchModalSearchText = string.Empty;

        var freshBatches = await _saleService.GetProductBatchesForSaleAsync(line.ProductId);
        line.AvailableBatches.Clear();
        foreach (var b in freshBatches)
        {
            line.AvailableBatches.Add(b);
        }

        RefreshFilteredModalBatches();
        IsBatchSearchModalOpen = true;
        RequestFocusBatchModalSearch?.Invoke();
    }

    [RelayCommand]
    public void CloseBatchSearchModal()
    {
        IsBatchSearchModalOpen = false;
    }

    partial void OnBatchModalSearchTextChanged(string value)
    {
        RefreshFilteredModalBatches();
    }

    private void RefreshFilteredModalBatches()
    {
        FilteredModalBatches.Clear();
        if (_targetLineForBatchSearch == null) return;

        var term = BatchModalSearchText?.Trim() ?? string.Empty;
        var source = string.IsNullOrWhiteSpace(term)
            ? _targetLineForBatchSearch.AvailableBatches
            : _targetLineForBatchSearch.AvailableBatches.Where(b =>
                b.BatchNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.FormattedExpiry.Contains(term, StringComparison.OrdinalIgnoreCase));

        foreach (var b in source)
        {
            FilteredModalBatches.Add(b);
        }

        HighlightedModalBatch = FilteredModalBatches.FirstOrDefault();
    }

    public void MoveBatchModalHighlight(int delta)
    {
        if (FilteredModalBatches.Count == 0) return;
        int cur = HighlightedModalBatch != null ? FilteredModalBatches.IndexOf(HighlightedModalBatch) : 0;
        int next = Math.Clamp(cur + delta, 0, FilteredModalBatches.Count - 1);
        HighlightedModalBatch = FilteredModalBatches[next];
    }

    public void CommitBatchModalSelection(SaleProductBatchOptionDto? explicitBatch = null)
    {
        var chosen = explicitBatch ?? HighlightedModalBatch;
        if (chosen == null || _targetLineForBatchSearch == null) return;

        int rowIndex = Lines.IndexOf(_targetLineForBatchSearch);
        _targetLineForBatchSearch.ApplySelectedBatch(
            chosen,
            FormSaleType,
            SelectedCustomer?.CustomWholesalePrices);

        IsBatchSearchModalOpen = false;

        var nextField = IsWholesaleFormMode ? "pack_quantity" : "quantity";
        if (rowIndex >= 0)
        {
            RequestFocusLineCell?.Invoke(rowIndex, nextField);
        }
    }

    // Substitute Products Modal (Retail Only)
    [RelayCommand]
    public async Task OpenSubstituteModalForLineAsync(SaleInvoiceLineRowViewModel? line)
    {
        if (line == null || !line.HasProduct)
        {
            _toastService.ShowInfo("Select a product first to look up generic/salt substitutes.", "Substitute Lookup");
            return;
        }

        _targetLineForSubstitute = line;
        SubstituteModalSourceProductText = $"{line.ProductName} — Salt/Formula: {(string.IsNullOrWhiteSpace(line.Formulation) ? "None specified" : line.Formulation)}";

        var subs = await _saleService.GetSubstituteProductsAsync(line.ProductId, SelectedCustomer?.Id);
        SubstituteProducts.Clear();
        foreach (var s in subs)
        {
            SubstituteProducts.Add(s);
        }

        IsSubstituteModalOpen = true;
    }

    [RelayCommand]
    public void CloseSubstituteModal()
    {
        IsSubstituteModalOpen = false;
    }

    [RelayCommand]
    public void SelectSubstituteProduct(SaleProductSearchItemDto? substitute)
    {
        if (substitute == null || _targetLineForSubstitute == null) return;

        int rowIndex = Lines.IndexOf(_targetLineForSubstitute);
        _targetLineForSubstitute.ApplySelectedProduct(
            substitute,
            FormSaleType,
            SelectedCustomer?.CustomWholesalePrices,
            SelectedCustomer?.DiscountPercentage ?? 0m);

        IsSubstituteModalOpen = false;
        _toastService.ShowSuccess($"Swapped to substitute '{substitute.Name}'.", "Substitute Applied");

        if (rowIndex >= 0)
        {
            RequestFocusLineCell?.Invoke(rowIndex, "quantity");
        }
    }

    // =========================================================================
    // 10. HELD DRAFTS (Alt+H Hold & New / Alt+D Drafts Modal)
    // =========================================================================

    [RelayCommand]
    public async Task HoldAndNewAsync()
    {
        var activeRows = Lines.Where(l => l.HasProduct).ToList();
        if (activeRows.Count == 0)
        {
            _toastService.ShowWarning("Add at least one product before holding a draft.", "Empty Invoice");
            return;
        }

        try
        {
            var req = BuildUpsertRequest();
            var saved = await _saleService.SaveDraftAsync(ActiveDraftId, req);
            HeldDraftsCount = (await _saleService.GetHeldDraftsAsync()).Count;

            _toastService.ShowSuccess(
                $"Invoice held as draft {saved.DraftNumber}. Form cleared for next customer.",
                "Held & New (Ctrl+H)");

            var nextNo = await _saleService.GenerateNextPostedNumberAsync();
            ResetFormState(FormSaleType, nextNo);
            await OpenProductSearchForLineAsync(Lines.FirstOrDefault() ?? AppendEmptyLine());
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Hold Draft Failed");
        }
    }

    [RelayCommand]
    public async Task OpenDraftsModalAsync()
    {
        DraftsModalFilter = "all";
        DraftsModalSearch = string.Empty;
        await RefreshHeldDraftsListAsync();
        IsDraftsModalOpen = true;
    }

    [RelayCommand]
    public void CloseDraftsModal()
    {
        IsDraftsModalOpen = false;
    }

    [RelayCommand]
    public async Task SetDraftsModalFilterAsync(string? filter)
    {
        DraftsModalFilter = string.IsNullOrWhiteSpace(filter) ? "all" : filter.Trim().ToLowerInvariant();
        await RefreshHeldDraftsListAsync();
    }

    private async Task RefreshHeldDraftsListAsync()
    {
        var all = await _saleService.GetHeldDraftsAsync(DraftsModalFilter);
        var term = DraftsModalSearch?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(term))
        {
            all = all.Where(d =>
                d.DraftNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                d.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                d.ItemsPreview.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        HeldDrafts.Clear();
        foreach (var d in all)
        {
            HeldDrafts.Add(d);
        }
        HeldDraftsCount = all.Count;
    }

    partial void OnDraftsModalSearchChanged(string value)
    {
        if (IsDraftsModalOpen)
            _ = RefreshHeldDraftsListAsync();
    }

    [RelayCommand]
    public async Task RestoreDraftAsync(HeldSaleDraftSummaryDto? draftSummary)
    {
        if (draftSummary == null) return;
        var detail = await _saleService.GetDraftByIdAsync(draftSummary.Id);
        if (detail == null)
        {
            _toastService.ShowError("Draft was not found.", "Restore Failed");
            return;
        }

        var state = detail.FormState;
        EditingInvoiceId = null;
        ActiveDraftId = detail.Id;
        FormSaleType = string.Equals(state.SaleType, "wholesale", StringComparison.OrdinalIgnoreCase) && HasWholesale
            ? "wholesale"
            : "retail";
        PostedNumber = await _saleService.GenerateNextPostedNumberAsync();
        InvoiceDate = state.Date;
        DueDate = state.DueDate;
        SelectedCustomer = AllCustomers.FirstOrDefault(c => c.Id == state.CustomerId)
                           ?? AllCustomers.FirstOrDefault();
        OneTimeCustomerName = state.OneTimeCustomerName ?? string.Empty;
        OneTimeCustomerPhone = state.OneTimeCustomerPhone ?? string.Empty;
        InvoiceType = state.InvoiceType;
        DoctorName = state.DoctorName ?? string.Empty;
        PatientName = state.PatientName ?? string.Empty;
        Remarks = state.Remarks ?? string.Empty;

        Lines.Clear();
        int idx = 1;
        foreach (var itemDto in state.Items.Where(i => i.ProductId > 0))
        {
            itemDto.AvailableBatches = await _saleService.GetProductBatchesForSaleAsync(itemDto.ProductId);
            var rowVm = new SaleInvoiceLineRowViewModel(idx++, () => FormSaleType, _ => RecalculateFooter(null));
            rowVm.LoadFromDto(itemDto, FormSaleType);
            Lines.Add(rowVm);
        }
        if (Lines.Count == 0)
            AppendEmptyLine();

        _suppressFooterRecalc = true;
        try
        {
            DiscountPercentage = state.DiscountPercentage;
            DiscountAmount = state.DiscountAmount;
            TaxPercentage = state.TaxPercentage;
            TaxAmount = state.TaxAmount;
            TotalReceive = state.TotalReceive;

            DiscountPercentageText = DiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            DiscountAmountText = DiscountAmount.ToString("0.##", CultureInfo.InvariantCulture);
            TaxPercentageText = TaxPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            TaxAmountText = TaxAmount.ToString("0.##", CultureInfo.InvariantCulture);
            TotalReceiveText = Math.Round(TotalReceive, 0).ToString("0", CultureInfo.InvariantCulture);
        }
        finally
        {
            _suppressFooterRecalc = false;
        }

        RecalculateFooter(IsCreditInvoice ? "total_receive" : null);
        IsDraftsModalOpen = false;
        ActiveViewMode = "form";
        _toastService.ShowSuccess($"Restored draft {detail.DraftNumber}.", "Draft Restored");
    }

    [RelayCommand]
    public async Task DeleteDraftItemAsync(HeldSaleDraftSummaryDto? draftSummary)
    {
        if (draftSummary == null) return;
        await _saleService.DeleteDraftAsync(draftSummary.Id);
        await RefreshHeldDraftsListAsync();
    }

    [RelayCommand]
    public async Task ClearAllDraftsCommandAsync()
    {
        await _saleService.ClearAllDraftsAsync(DraftsModalFilter);
        await RefreshHeldDraftsListAsync();
        _toastService.ShowInfo("All matching held drafts cleared.", "Drafts Cleared");
    }

    // =========================================================================
    // 11. SUBMIT / SAVE & PRINT, OVERRIDES, REVERSE & DELETE
    // =========================================================================

    [RelayCommand]
    public async Task ClearOrNewInvoiceAsync()
    {
        var nextNo = await _saleService.GenerateNextPostedNumberAsync();
        ResetFormState(FormSaleType, nextNo);
        _toastService.ShowInfo("Form cleared for a new sale invoice.", "New Invoice (Ctrl+N)");
        await OpenProductSearchForLineAsync(Lines.FirstOrDefault() ?? AppendEmptyLine());
    }

    [RelayCommand]
    public async Task SubmitInvoiceAsync()
    {
        if (IsBusy) return;

        var activeRows = Lines.Where(l => l.HasProduct).ToList();
        if (activeRows.Count == 0)
        {
            _toastService.ShowWarning("Please add at least one product to the invoice.", "No Items");
            if (Lines.Count == 0) AppendEmptyLine();
            await OpenProductSearchForLineAsync(Lines[0]);
            return;
        }

        IsBusy = true;
        try
        {
            var req = BuildUpsertRequest();
            var res = IsEditMode
                ? await _saleService.UpdateAsync(EditingInvoiceId!.Value, req)
                : await _saleService.CreateAsync(req);

            if (!res.Succeeded)
            {
                if (res.RequiresExpiredOverride)
                {
                    ExpiredBatchDetails = res.ExpiredBatchDetails ?? res.Message;
                    SupervisorPassword = string.Empty;
                    SupervisorOverrideReason = string.Empty;
                    IsExpiredOverrideModalOpen = true;
                    return;
                }

                if (res.RequiresDiscountOverride)
                {
                    DiscountViolationDetails = res.DiscountViolationDetails ?? res.Message;
                    SupervisorPassword = string.Empty;
                    SupervisorOverrideReason = string.Empty;
                    IsDiscountOverrideModalOpen = true;
                    return;
                }

                if (res.RequiresCreditOverride)
                {
                    CreditViolationDetails = res.CreditViolationDetails ?? res.Message;
                    OverrideCurrentBalance = res.CustomerCurrentBalance;
                    OverrideNewBillCredit = res.NewBillCreditAmount;
                    OverrideProjectedBalance = res.ProjectedCustomerBalance;
                    OverrideCreditLimit = res.CustomerCreditLimit;
                    SupervisorPassword = string.Empty;
                    SupervisorOverrideReason = string.Empty;
                    IsCreditOverrideModalOpen = true;
                    return;
                }

                if (res.RequiresPreShiftDrawerChoice)
                {
                    req.UpdatePreShiftDrawer = true;
                    res = await _saleService.UpdateAsync(EditingInvoiceId!.Value, req);
                }

                if (!res.Succeeded)
                {
                    _toastService.ShowError(res.Message, "Validation Error");
                    if (res.OffendingRowIndex.HasValue && !string.IsNullOrWhiteSpace(res.OffendingField))
                    {
                        RequestFocusLineCell?.Invoke(res.OffendingRowIndex.Value, res.OffendingField!);
                    }
                    return;
                }
            }

            _toastService.ShowSuccess(res.Message, IsEditMode ? "Invoice Updated" : "Invoice Saved");

            if (res.InvoiceId.HasValue)
            {
                await OpenDetailModeAsync(res.InvoiceId.Value, autoPrint: !IsEditMode);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private SaleInvoiceUpsertRequest BuildUpsertRequest()
    {
        var activeRows = Lines.Where(l => l.HasProduct).ToList();
        var acct = SelectedPaymentAccount ?? PaymentAccounts.FirstOrDefault();
        var payMethod = acct?.AccountType switch
        {
            "bank" => "bank",
            "mobile_wallet" => "wallet",
            "card" => "card",
            _ => "cash"
        };

        return new SaleInvoiceUpsertRequest
        {
            DraftId = ActiveDraftId,
            PostedNumber = PostedNumber,
            Date = InvoiceDate,
            DueDate = DueDate,
            CustomerId = SelectedCustomer?.Id ?? _walkInCustomerId,
            OneTimeCustomerName = IsWalkInSelected ? OneTimeCustomerName : null,
            OneTimeCustomerPhone = IsWalkInSelected ? OneTimeCustomerPhone : null,
            DoctorName = IsPharmacy ? DoctorName : null,
            PatientName = IsPharmacy ? PatientName : null,
            Remarks = Remarks,
            SaleType = FormSaleType,
            WholesaleType = IsWholesaleFormMode ? "pack" : "unit",
            InvoiceType = InvoiceType,
            GrossAmount = GrossAmount,
            ItemDiscount = ItemDiscountAmount,
            DiscountPercentage = DiscountPercentage,
            DiscountAmount = DiscountAmount,
            TaxPercentage = TaxPercentage,
            TaxAmount = TaxAmount,
            Total = NetTotal,
            TotalReceive = IsCreditInvoice ? TotalReceive : NetTotal,
            CashGiven = CashGiven,
            PaymentMethod = payMethod,
            AccountId = acct?.Id,
            DiscountOverrideConfirmed = _discountOverrideApproved,
            CreditOverrideConfirmed = _creditOverrideApproved,
            ExpiredOverrideConfirmed = _expiredOverrideApproved,
            Items = activeRows.Select(r => r.ToUpsertDto(FormSaleType)).ToList()
        };
    }

    [RelayCommand]
    public async Task ConfirmSupervisorOverrideAsync(string? overrideKind)
    {
        var (ok, msg, _) = await _saleService.VerifySupervisorOverrideAsync(
            SupervisorUsername,
            SupervisorPassword,
            overrideKind ?? "discount",
            SupervisorOverrideReason);

        if (!ok)
        {
            _toastService.ShowError(msg, "Authorization Failed");
            return;
        }

        if (string.Equals(overrideKind, "expired", StringComparison.OrdinalIgnoreCase))
        {
            _expiredOverrideApproved = true;
            IsExpiredOverrideModalOpen = false;
        }
        else if (string.Equals(overrideKind, "credit", StringComparison.OrdinalIgnoreCase))
        {
            _creditOverrideApproved = true;
            IsCreditOverrideModalOpen = false;
        }
        else
        {
            _discountOverrideApproved = true;
            IsDiscountOverrideModalOpen = false;
        }

        _toastService.ShowSuccess(msg, "Override Approved");
        await SubmitInvoiceAsync();
    }

    [RelayCommand]
    public void CloseOverrideModals()
    {
        IsDiscountOverrideModalOpen = false;
        IsCreditOverrideModalOpen = false;
        IsExpiredOverrideModalOpen = false;
    }

    [RelayCommand]
    public async Task ConfirmOpenPreShiftDrawerAsync()
    {
        var openAmt = ParseDecimal(OpeningFloatAmountText);
        await _saleService.OpenCashDrawerSessionAsync(openAmt, "Opened from Sale Invoice POS");
        IsPreShiftDrawerModalOpen = false;
        _toastService.ShowSuccess($"Cash drawer shift opened with float Rs. {openAmt:N0}.", "Cash Drawer Open");
    }

    [RelayCommand]
    public void SkipPreShiftDrawerPrompt()
    {
        IsPreShiftDrawerModalOpen = false;
    }

    // Show Page Modals: Search Invoice (Ctrl+F), Audit History, Receive Payment, Reverse & 3-Step Delete
    [RelayCommand]
    public async Task OpenShowInvoiceSearchModalAsync()
    {
        ShowInvoiceSearchQuery = string.Empty;
        await RefreshShowInvoiceSearchResultsAsync(string.Empty);
        IsShowInvoiceSearchModalOpen = true;
        RequestFocusShowInvoiceSearch?.Invoke();
    }

    [RelayCommand]
    public void CloseShowInvoiceSearchModal()
    {
        IsShowInvoiceSearchModalOpen = false;
    }

    [RelayCommand]
    public void ClearShowInvoiceSearchQuery()
    {
        ShowInvoiceSearchQuery = string.Empty;
    }

    partial void OnShowInvoiceSearchQueryChanged(string value)
    {
        if (!IsShowInvoiceSearchModalOpen) return;
        _ = RefreshShowInvoiceSearchResultsAsync(value);
    }

    private async Task RefreshShowInvoiceSearchResultsAsync(string? query)
    {
        var pageDto = await _saleService.GetDirectoryPageAsync(new SaleInvoiceFilterQuery
        {
            Search = query?.Trim(),
            SaleType = "all",
            InvoiceType = "all",
            Status = "all",
            Page = 1,
            PageSize = 30
        });

        ShowInvoiceSearchResults.Clear();
        foreach (var row in pageDto.Items)
        {
            ShowInvoiceSearchResults.Add(row);
        }
        HighlightedShowSearchInvoice = ShowInvoiceSearchResults.FirstOrDefault();
    }

    public void MoveShowInvoiceSearchHighlight(int delta)
    {
        if (ShowInvoiceSearchResults.Count == 0) return;
        int cur = HighlightedShowSearchInvoice != null ? ShowInvoiceSearchResults.IndexOf(HighlightedShowSearchInvoice) : 0;
        int next = Math.Clamp(cur + delta, 0, ShowInvoiceSearchResults.Count - 1);
        HighlightedShowSearchInvoice = ShowInvoiceSearchResults[next];
    }

    [RelayCommand]
    public async Task CommitShowInvoiceSearchSelectionAsync(SaleInvoiceDirectoryRowDto? explicitRow = null)
    {
        var chosen = explicitRow ?? HighlightedShowSearchInvoice;
        if (chosen == null) return;

        IsShowInvoiceSearchModalOpen = false;
        await OpenDetailModeAsync(chosen.Id);
    }

    [RelayCommand]
    public void OpenHistoryModal()
    {
        IsHistoryModalOpen = true;
    }

    [RelayCommand]
    public void CloseHistoryModal()
    {
        IsHistoryModalOpen = false;
    }

    [RelayCommand]
    public void OpenRecordPaymentModal()
    {
        if (CurrentDetail == null || !CurrentDetail.HasRemainingDue) return;

        RecordPaymentAmountText = Math.Round(CurrentDetail.RemainThisBill, 0).ToString("0", CultureInfo.InvariantCulture);
        RecordPaymentAccount = PaymentAccounts.FirstOrDefault(a => a.IsDefault) ?? PaymentAccounts.FirstOrDefault();
        RecordPaymentReference = string.Empty;
        IsRecordPaymentModalOpen = true;
    }

    [RelayCommand]
    public void CloseRecordPaymentModal()
    {
        IsRecordPaymentModalOpen = false;
    }

    [RelayCommand]
    public async Task ConfirmRecordPaymentAsync()
    {
        if (CurrentDetail == null) return;

        var amt = ParseDecimal(RecordPaymentAmountText);
        if (amt <= 0m)
        {
            _toastService.ShowWarning("Enter a valid payment amount greater than zero.", "Receive Payment");
            return;
        }

        var res = await _saleService.RecordInvoicePaymentAsync(
            CurrentDetail.Id,
            amt,
            RecordPaymentAccount?.Id,
            RecordPaymentReference);

        if (!res.Succeeded)
        {
            _toastService.ShowError(res.Message, "Payment Failed");
            return;
        }

        IsRecordPaymentModalOpen = false;
        _toastService.ShowSuccess(res.Message, "Payment Recorded");
        await OpenDetailModeAsync(CurrentDetail.Id);
    }

    [RelayCommand]
    public void OpenReverseModal(SaleInvoiceDirectoryRowDto? row)
    {
        var targetId = row?.Id ?? CurrentDetail?.Id;
        var targetNo = row?.PostedNumber ?? CurrentDetail?.PostedNumber ?? string.Empty;
        if (targetId is not > 0) return;

        ReversingInvoiceId = targetId;
        ReversingInvoiceNumber = targetNo;
        ReversalReason = string.Empty;
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
        if (ReversingInvoiceId is not > 0) return;
        var res = await _saleService.ReverseAsync(ReversingInvoiceId.Value, ReversalReason);
        if (!res.Succeeded)
        {
            _toastService.ShowError(res.Message, "Reversal Blocked");
            return;
        }

        IsReverseModalOpen = false;
        _toastService.ShowSuccess(res.Message, "Invoice Reversed");

        if (IsDetailMode && CurrentDetail?.Id == ReversingInvoiceId.Value)
        {
            await OpenDetailModeAsync(ReversingInvoiceId.Value);
        }
        else
        {
            await LoadDirectoryPageAsync(CurrentPage);
        }
    }

    [RelayCommand]
    public void OpenDeleteModal(SaleInvoiceDirectoryRowDto? row)
    {
        var targetId = row?.Id ?? CurrentDetail?.Id;
        var targetNo = row?.PostedNumber ?? CurrentDetail?.PostedNumber ?? string.Empty;
        if (targetId is not > 0) return;

        DeletingInvoiceId = targetId;
        DeletingInvoiceNumber = targetNo;
        DeletingInvoiceTotal = row?.Total ?? CurrentDetail?.Total ?? 0m;
        DeletingInvoiceReceived = row?.TotalReceive ?? CurrentDetail?.TotalReceive ?? 0m;
        DeletingInvoiceRemaining = row?.RemainingAmount ?? CurrentDetail?.RemainThisBill ?? 0m;

        bool isCredit = row != null ? row.IsCredit : (CurrentDetail?.IsCredit ?? false);
        bool isWalkIn = CurrentDetail?.IsWalkInCustomer ?? false;
        DeletingInvoiceNeedsChoice = isCredit && DeletingInvoiceReceived > 0m && !isWalkIn;

        DeleteStep = 1;
        DeletePassword = string.Empty;
        DeleteRefundMode = DeletingInvoiceNeedsChoice ? "credit" : "refund";
        IsDeleteModalOpen = true;
    }

    [RelayCommand]
    public void SetDeleteRefundMode(string? mode)
    {
        DeleteRefundMode = string.Equals(mode, "credit", StringComparison.OrdinalIgnoreCase) ? "credit" : "refund";
    }

    [RelayCommand]
    public void ProceedDeleteStep1()
    {
        if (DeletingInvoiceNeedsChoice)
        {
            DeleteRefundMode = "credit";
            DeleteStep = 2;
        }
        else
        {
            DeleteRefundMode = "refund";
            DeleteStep = 3;
            RequestFocusDeletePassword?.Invoke();
        }
    }

    [RelayCommand]
    public void ProceedDeleteStep2()
    {
        DeleteStep = 3;
        RequestFocusDeletePassword?.Invoke();
    }

    [RelayCommand]
    public void BackDeleteStep()
    {
        if (DeleteStep == 3)
        {
            DeleteStep = DeletingInvoiceNeedsChoice ? 2 : 1;
        }
        else
        {
            DeleteStep = 1;
        }
    }

    [RelayCommand]
    public void CloseDeleteModal()
    {
        IsDeleteModalOpen = false;
    }

    [RelayCommand]
    public async Task ConfirmDeleteInvoiceAsync()
    {
        if (DeletingInvoiceId is not > 0) return;
        var deletedId = DeletingInvoiceId.Value;
        var res = await _saleService.DeleteAsync(deletedId, DeletePassword, DeleteRefundMode);
        if (!res.Succeeded)
        {
            _toastService.ShowError(res.Message, "Delete Failed");
            return;
        }

        IsDeleteModalOpen = false;
        _toastService.ShowSuccess(res.Message, "Invoice Deleted");

        if (IsDetailMode)
        {
            // Match Show.jsx goToPrevOrIndex: navigate to previous/next invoice if one exists, else index list!
            var adjId = await _saleService.GetAdjacentInvoiceIdAsync(
                deletedId,
                CurrentDetail?.IsWholesale == true ? "wholesale" : "retail");

            if (adjId.HasValue)
            {
                await OpenDetailModeAsync(adjId.Value);
                return;
            }
        }

        await OpenDirectoryModeAsync(LockedDirectorySaleType);
    }

    private static decimal ParseDecimal(string? raw, bool allowNegative = false)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0m;
        var clean = raw.Trim();
        if (clean == "-" || clean == "." || clean == "-.")
            return 0m;
        if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv))
            return allowNegative ? dv : Math.Max(0m, dv);
        return 0m;
    }
}
