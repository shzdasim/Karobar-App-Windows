using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;
using Karobar.Domain.Formulas;

namespace Karobar.Desktop.ViewModels;

public partial class PurchaseReturnLineEditorViewModel : ObservableObject
{
    private readonly Action<PurchaseReturnLineEditorViewModel, string?> _onChanged;
    private readonly Func<PurchaseReturnLineEditorViewModel, PurchaseReturnBatchOptionDto, bool>? _onBatchOptionPicked;
    private bool _isInternalSync;

    public PurchaseReturnLineEditorViewModel(
        Action<PurchaseReturnLineEditorViewModel, string?> onChanged,
        Func<PurchaseReturnLineEditorViewModel, PurchaseReturnBatchOptionDto, bool>? onBatchOptionPicked = null)
    {
        _onChanged = onChanged;
        _onBatchOptionPicked = onBatchOptionPicked;
    }

    [ObservableProperty]
    private int _rowNumber = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProduct))]
    [NotifyPropertyChangedFor(nameof(ProductDisplayText))]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private long _productId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductDisplayText))]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private string _productCode = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductDisplayText))]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private string _productName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private string _brandName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private string _rack = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchDisplayText))]
    private string _batch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchDisplayText))]
    private bool _isUnbatched;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpiryFormatted))]
    private DateTime? _expiry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductMetaText))]
    private int _packSize = 1;

    [ObservableProperty]
    private decimal _packPurchasedQuantity;

    [ObservableProperty]
    private int _availableUnits;

    [ObservableProperty]
    private decimal _returnPackQuantity;

    [ObservableProperty]
    private int _returnUnitQuantity;

    [ObservableProperty]
    private decimal _packPurchasePrice;

    [ObservableProperty]
    private decimal _unitPurchasePrice;

    [ObservableProperty]
    private decimal _itemDiscountPercentage;

    [ObservableProperty]
    private decimal _subTotal;

    // String properties for smooth typing in repeater cells
    [ObservableProperty]
    private string _returnPackQtyText = string.Empty;

    [ObservableProperty]
    private string _returnUnitQtyText = string.Empty;

    [ObservableProperty]
    private string _packPurchasePriceText = string.Empty;

    [ObservableProperty]
    private string _unitPurchasePriceText = string.Empty;

    [ObservableProperty]
    private string _itemDiscountText = string.Empty;

    [ObservableProperty]
    private PurchaseReturnBatchOptionDto? _selectedBatchOption;

    [ObservableProperty]
    private string _batchText = string.Empty;

    [ObservableProperty]
    private bool _hasRowError;

    [ObservableProperty]
    private string _rowErrorMessage = string.Empty;

    public ObservableCollection<PurchaseReturnBatchOptionDto> RowBatches { get; } = [];

    public bool HasProduct => ProductId > 0;

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
            if (!string.IsNullOrWhiteSpace(BrandName) && BrandName != "—") parts.Add(BrandName);
            parts.Add($"1×{Math.Max(1, PackSize)}");
            if (!string.IsNullOrWhiteSpace(Rack)) parts.Add($"Rack: {Rack}");
            return string.Join(" • ", parts);
        }
    }

    public string BatchDisplayText => !string.IsNullOrWhiteSpace(Batch)
        ? Batch
        : (IsUnbatched ? "[Unassigned Stock]" : string.Empty);

    public string ExpiryFormatted => Expiry.HasValue ? Expiry.Value.ToString("MM/yyyy") : string.Empty;

    partial void OnSelectedBatchOptionChanged(PurchaseReturnBatchOptionDto? value)
    {
        if (_isInternalSync || value == null)
            return;

        if (_onBatchOptionPicked != null)
        {
            _onBatchOptionPicked(this, value);
        }
    }

    partial void OnReturnPackQtyTextChanged(string value)
    {
        if (_isInternalSync) return;
        ReturnPackQuantity = ParseNonNegDecimal(value);
        Recalculate("return_pack_quantity");
    }

    partial void OnReturnUnitQtyTextChanged(string value)
    {
        if (_isInternalSync) return;
        ReturnUnitQuantity = ParseNonNegInt(value);
        Recalculate("return_unit_quantity");
    }

    partial void OnPackPurchasePriceTextChanged(string value)
    {
        if (_isInternalSync) return;
        PackPurchasePrice = ParseNonNegDecimal(value);
        Recalculate("pack_purchase_price");
    }

    partial void OnUnitPurchasePriceTextChanged(string value)
    {
        if (_isInternalSync) return;
        UnitPurchasePrice = ParseNonNegDecimal(value);
        Recalculate("unit_purchase_price");
    }

    partial void OnItemDiscountTextChanged(string value)
    {
        if (_isInternalSync) return;
        ItemDiscountPercentage = Math.Clamp(ParseNonNegDecimal(value), 0m, 100m);
        Recalculate("item_discount_percentage");
    }

    partial void OnReturnPackQuantityChanged(decimal value)
    {
        if (_isInternalSync) return;
        Recalculate("return_pack_quantity");
    }

    partial void OnReturnUnitQuantityChanged(int value)
    {
        if (_isInternalSync) return;
        Recalculate("return_unit_quantity");
    }

    partial void OnPackPurchasePriceChanged(decimal value)
    {
        if (_isInternalSync) return;
        Recalculate("pack_purchase_price");
    }

    partial void OnUnitPurchasePriceChanged(decimal value)
    {
        if (_isInternalSync) return;
        Recalculate("unit_purchase_price");
    }

    partial void OnItemDiscountPercentageChanged(decimal value)
    {
        if (_isInternalSync) return;
        Recalculate("item_discount_percentage");
    }

    public void Recalculate(string? changedField = null)
    {
        if (_isInternalSync) return;
        try
        {
            _isInternalSync = true;
            var model = new PurchaseReturnLineCalcModel
            {
                PackSize = PackSize > 0 ? PackSize : 1,
                PackPurchasedQuantity = PackPurchasedQuantity,
                ReturnPackQuantity = ReturnPackQuantity,
                ReturnUnitQuantity = ReturnUnitQuantity,
                PackPurchasePrice = PackPurchasePrice,
                UnitPurchasePrice = UnitPurchasePrice,
                ItemDiscountPercentage = ItemDiscountPercentage,
                SubTotal = SubTotal
            };

            InvoiceCalculators.RecalcPurchaseReturnItem(model, changedField);

            ReturnPackQuantity = Math.Round(model.ReturnPackQuantity, 2);
            ReturnUnitQuantity = model.ReturnUnitQuantity;
            PackPurchasePrice = Math.Round(model.PackPurchasePrice, 2);
            UnitPurchasePrice = Math.Round(model.UnitPurchasePrice, 4);
            ItemDiscountPercentage = Math.Round(model.ItemDiscountPercentage, 2);
            SubTotal = model.SubTotal;

            SyncDisplayTexts(changedField);
        }
        finally
        {
            _isInternalSync = false;
        }

        _onChanged(this, changedField);
    }

    public void NormalizeDisplayTexts()
    {
        if (_isInternalSync) return;
        try
        {
            _isInternalSync = true;
            SyncDisplayTexts(activeEditedField: null);
        }
        finally
        {
            _isInternalSync = false;
        }
    }

    private void SyncDisplayTexts(string? activeEditedField)
    {
        if (activeEditedField != "return_pack_quantity")
            ReturnPackQtyText = ReturnPackQuantity == 0m ? string.Empty : FormatQty(ReturnPackQuantity);
        if (activeEditedField != "return_unit_quantity")
            ReturnUnitQtyText = ReturnUnitQuantity == 0 ? string.Empty : ReturnUnitQuantity.ToString(CultureInfo.InvariantCulture);
        if (activeEditedField != "pack_purchase_price")
            PackPurchasePriceText = PackPurchasePrice == 0m && !HasProduct ? string.Empty : FormatMoney(PackPurchasePrice);
        if (activeEditedField != "unit_purchase_price")
            UnitPurchasePriceText = UnitPurchasePrice == 0m && !HasProduct ? string.Empty : FormatRate(UnitPurchasePrice);
        if (activeEditedField != "item_discount_percentage")
            ItemDiscountText = ItemDiscountPercentage == 0m ? string.Empty : FormatQty(ItemDiscountPercentage);

        BatchText = BatchDisplayText;
    }

    public void HydrateSilent(Action<PurchaseReturnLineEditorViewModel> mutator)
    {
        try
        {
            _isInternalSync = true;
            mutator(this);
            SyncDisplayTexts(activeEditedField: null);
        }
        finally
        {
            _isInternalSync = false;
        }
    }

    private static string FormatQty(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatRate(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);

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

public partial class PurchaseReturnsViewModel : ObservableObject
{
    private readonly IPurchaseReturnService _returnService;
    private readonly IAuthService _authService;
    private readonly IToastNotificationService? _toast;
    private readonly IPurchaseInvoiceService? _purchaseInvoiceService;

    private bool _isFooterSyncing;
    private bool _suppressDirectoryReload;
    private bool _pendingPrintAfterSave;
    private CancellationTokenSource? _directoryDebounceCts;
    private CancellationTokenSource? _productSearchDebounceCts;

    public event Action<string, int>? RequestFocusField;
    public event Action<PurchaseReturnDetailDto>? RequestPrintDocument;
    public Action? RequestApplyProductSearchModalLayout { get; set; }

    public PurchaseReturnsViewModel(
        IPurchaseReturnService returnService,
        IAuthService authService,
        IToastNotificationService? toast = null,
        IPurchaseInvoiceService? purchaseInvoiceService = null)
    {
        _returnService = returnService;
        _authService = authService;
        _toast = toast;
        _purchaseInvoiceService = purchaseInvoiceService;

        InitializeStaticOptions();
    }

    private void InitializeStaticOptions()
    {
        _suppressDirectoryReload = true;
        try
        {
            PageSizeOptions.Clear();
            PageSizeOptions.Add(new PurchaseSelectOption { Key = "10", Label = "10" });
            PageSizeOptions.Add(new PurchaseSelectOption { Key = "25", Label = "25" });
            PageSizeOptions.Add(new PurchaseSelectOption { Key = "50", Label = "50" });
            PageSizeOptions.Add(new PurchaseSelectOption { Key = "100", Label = "100" });
            SelectedPageSizeOption = PageSizeOptions[0]; // Default 10 matching PurchaseReturn/index.jsx:65

            PaymentMethodOptions.Clear();
            PaymentMethodOptions.Add(new PurchaseSelectOption { Key = "cash", Label = "Cash (Drawer)" });
            PaymentMethodOptions.Add(new PurchaseSelectOption { Key = "bank_transfer", Label = "Bank Transfer" });
            PaymentMethodOptions.Add(new PurchaseSelectOption { Key = "card", Label = "Card / POS" });
            PaymentMethodOptions.Add(new PurchaseSelectOption { Key = "mobile_wallet", Label = "Mobile Wallet" });
            SelectedPaymentMethodOption = PaymentMethodOptions[0];
        }
        finally
        {
            _suppressDirectoryReload = false;
        }
    }

    // =========================================================================
    // 1. MODE & STATUS STATE
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirectoryMode))]
    [NotifyPropertyChangedFor(nameof(IsFormMode))]
    private string _activeSubView = "Directory";

    public bool IsDirectoryMode => ActiveSubView == "Directory";
    public bool IsFormMode => ActiveSubView == "Form";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyDirectoryState))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusBanner))]
    private string _statusBannerMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusBannerError;

    public bool HasStatusBanner => !string.IsNullOrWhiteSpace(StatusBannerMessage);

    [ObservableProperty]
    private string _currencySymbol = "Rs.";

    [ObservableProperty]
    private string _storeName = "Karobar App";

    [ObservableProperty]
    private string _storeAddress = string.Empty;

    [ObservableProperty]
    private string _storePhone = string.Empty;

    // =========================================================================
    // 2. DIRECTORY STATE
    // =========================================================================

    public ObservableCollection<PurchaseSelectOption> PageSizeOptions { get; } = [];
    public ObservableCollection<PurchaseReturnDirectoryRowDto> DirectoryRows { get; } = [];

    [ObservableProperty]
    private string _filterPostedNumber = string.Empty;

    [ObservableProperty]
    private string _filterSupplierSearch = string.Empty;

    [ObservableProperty]
    private string _filterSettlementMode = "all";

    [ObservableProperty]
    private PurchaseSelectOption? _selectedPageSizeOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPrevPage))]
    [NotifyPropertyChangedFor(nameof(CanNextPage))]
    [NotifyPropertyChangedFor(nameof(PaginationSummaryText))]
    [NotifyPropertyChangedFor(nameof(DirectoryRangeSubtitleText))]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _pageSize = 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaginationSummaryText))]
    [NotifyPropertyChangedFor(nameof(DirectoryRangeSubtitleText))]
    [NotifyPropertyChangedFor(nameof(DirectoryTotalSubtitleText))]
    [NotifyPropertyChangedFor(nameof(HasDirectoryItems))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyDirectoryState))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPrevPage))]
    [NotifyPropertyChangedFor(nameof(CanNextPage))]
    [NotifyPropertyChangedFor(nameof(PaginationSummaryText))]
    private int _totalPages = 1;

    [ObservableProperty]
    private int _totalReturnsCount;

    [ObservableProperty]
    private decimal _totalGrossAmount;

    [ObservableProperty]
    private decimal _totalNetAmount;

    [ObservableProperty]
    private decimal _totalCashRefundAmount;

    [ObservableProperty]
    private decimal _totalLedgerCreditAmount;

    [ObservableProperty]
    private PurchaseReturnDirectoryRowDto? _selectedDirectoryRow;

    public bool HasDirectoryItems => DirectoryRows.Count > 0;
    public bool ShowEmptyDirectoryState => !IsBusy && DirectoryRows.Count == 0;
    public bool CanPrevPage => CurrentPage > 1;
    public bool CanNextPage => CurrentPage < TotalPages;
    public string PaginationSummaryText => $"Page {CurrentPage} of {TotalPages} ({TotalCount:N0} total)";
    public string DirectoryTotalSubtitleText => $"{TotalCount:N0} return(s) in records";
    public string DirectoryRangeSubtitleText
    {
        get
        {
            if (TotalCount <= 0 || DirectoryRows.Count == 0)
                return "0 of 0";
            var ps = PageSize > 0 ? PageSize : 10;
            var start = ((CurrentPage - 1) * ps) + 1;
            var end = Math.Min(TotalCount, start + DirectoryRows.Count - 1);
            return $"{start}-{end} of {TotalCount}";
        }
    }

    partial void OnFilterPostedNumberChanged(string value)
    {
        if (_suppressDirectoryReload) return;
        TriggerDirectoryDebounce();
    }

    partial void OnFilterSupplierSearchChanged(string value)
    {
        if (_suppressDirectoryReload) return;
        TriggerDirectoryDebounce();
    }

    partial void OnSelectedPageSizeOptionChanged(PurchaseSelectOption? value)
    {
        if (value == null) return;
        if (int.TryParse(value.Key, out var ps) && ps > 0)
        {
            PageSize = ps;
        }
        if (_suppressDirectoryReload) return;
        CurrentPage = 1;
        _ = RefreshDirectoryAsync();
    }

    private void TriggerDirectoryDebounce()
    {
        _directoryDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _directoryDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(220, cts.Token);
                if (cts.IsCancellationRequested) return;
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    CurrentPage = 1;
                    _ = RefreshDirectoryAsync();
                });
            }
            catch (TaskCanceledException)
            {
            }
        });
    }

    // =========================================================================
    // 3. FORM STATE (CREATE / EDIT)
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(FormModeBadgeText))]
    [NotifyPropertyChangedFor(nameof(HeaderSubmitButtonText))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private long? _editingReturnId;

    public bool IsEditMode => EditingReturnId is > 0;
    public string FormModeBadgeText => IsEditMode ? "EDIT" : "CREATE";
    public string HeaderSubmitButtonText => IsEditMode ? "Update (Alt+S)" : "Create (Alt+S)";
    public string SaveButtonText => IsEditMode ? "Update Return" : "Create Return";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PostedNumberDisplayText))]
    private string _formPostedNumber = string.Empty;

    public string PostedNumberDisplayText => !string.IsNullOrWhiteSpace(FormPostedNumber)
        ? FormPostedNumber
        : "Auto on Save";

    [ObservableProperty]
    private DateTime _formDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSupplier))]
    [NotifyPropertyChangedFor(nameof(SupplierButtonText))]
    private long _formSupplierId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupplierButtonText))]
    private string _formSupplierName = string.Empty;

    [ObservableProperty]
    private decimal _formSupplierPayableBalance;

    public bool HasSelectedSupplier => FormSupplierId > 0;
    public string SupplierButtonText => HasSelectedSupplier
        ? FormSupplierName
        : "Click to search supplier...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedInvoice))]
    [NotifyPropertyChangedFor(nameof(InvoiceButtonText))]
    [NotifyPropertyChangedFor(nameof(AvailableColumnHeader))]
    [NotifyPropertyChangedFor(nameof(CanSelectInvoiceOffset))]
    private long? _formPurchaseInvoiceId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InvoiceButtonText))]
    private string _formPurchaseInvoicePostedNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectInvoiceOffset))]
    private string _formPurchaseInvoiceType = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectInvoiceOffset))]
    private decimal _formPurchaseInvoiceRemaining;

    public bool HasSelectedInvoice => FormPurchaseInvoiceId is > 0;
    public string InvoiceButtonText => HasSelectedInvoice
        ? FormPurchaseInvoicePostedNumber
        : "Click to search invoice...";
    public string AvailableColumnHeader => HasSelectedInvoice ? "Returnable Qty" : "Avail.Q (Units)";
    public bool CanSelectInvoiceOffset => HasSelectedInvoice &&
        string.Equals(FormPurchaseInvoiceType, "credit", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _formRemarks = string.Empty;

    [ObservableProperty]
    private decimal _formGrossTotal;

    [ObservableProperty]
    private decimal _formDiscountPercentage;

    [ObservableProperty]
    private decimal _formDiscountAmount;

    [ObservableProperty]
    private decimal _formTaxPercentage;

    [ObservableProperty]
    private decimal _formTaxAmount;

    [ObservableProperty]
    private decimal _formTotal;

    [ObservableProperty]
    private string _footerDiscountPctText = "0";

    [ObservableProperty]
    private string _footerTaxPctText = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCashRefundSettlement))]
    [NotifyPropertyChangedFor(nameof(IsCreditNoteSettlement))]
    [NotifyPropertyChangedFor(nameof(IsInvoiceOffsetSettlement))]
    [NotifyPropertyChangedFor(nameof(IsLedgerSettlement))]
    private string _formSettlementMode = "cash_refund";

    public bool IsCashRefundSettlement => FormSettlementMode == "cash_refund";
    public bool IsCreditNoteSettlement => FormSettlementMode == "credit_note";
    public bool IsInvoiceOffsetSettlement => FormSettlementMode == "invoice_offset";
    public bool IsLedgerSettlement => FormSettlementMode == "credit_note" || FormSettlementMode == "invoice_offset";

    [ObservableProperty]
    private string _formPaymentMethod = "cash";

    [ObservableProperty]
    private long? _formAccountId;

    public ObservableCollection<PurchaseSelectOption> PaymentMethodOptions { get; } = [];
    public ObservableCollection<PurchaseAccountLookupDto> AccountOptions { get; } = [];

    [ObservableProperty]
    private PurchaseSelectOption? _selectedPaymentMethodOption;

    [ObservableProperty]
    private PurchaseAccountLookupDto? _selectedAccountOption;

    public ObservableCollection<PurchaseSupplierLookupDto> AllSuppliers { get; } = [];
    public ObservableCollection<PurchaseAccountLookupDto> PaymentAccounts { get; } = [];
    public ObservableCollection<PurchaseReturnInvoiceItemOptionDto> ActiveInvoiceItems { get; } = [];
    public ObservableCollection<PurchaseReturnLineEditorViewModel> Lines { get; } = [];

    [ObservableProperty]
    private int _activeRowIndex;

    public int ActiveProductLinesCount => Lines.Count(l => l.HasProduct);

    partial void OnSelectedPaymentMethodOptionChanged(PurchaseSelectOption? value)
    {
        if (value == null) return;
        FormPaymentMethod = value.Key;
        RefreshAccountOptionsForPaymentMethod();
    }

    partial void OnSelectedAccountOptionChanged(PurchaseAccountLookupDto? value)
    {
        FormAccountId = value is { Id: > 0 } ? value.Id : null;
    }

    private void RefreshAccountOptionsForPaymentMethod()
    {
        AccountOptions.Clear();
        AccountOptions.Add(new PurchaseAccountLookupDto
        {
            Id = 0,
            Name = $"Auto ({FormPaymentMethod})",
            AccountType = FormPaymentMethod
        });

        foreach (var acc in PaymentAccounts)
        {
            AccountOptions.Add(acc);
        }

        if (FormAccountId is > 0)
        {
            SelectedAccountOption = AccountOptions.FirstOrDefault(a => a.Id == FormAccountId.Value) ?? AccountOptions[0];
        }
        else
        {
            SelectedAccountOption = AccountOptions[0];
        }
    }

    // =========================================================================
    // 4. MODALS STATE
    // =========================================================================

    // 4.1 Supplier Search Modal (Exact SupplierSearch.jsx)
    [ObservableProperty]
    private bool _isSupplierSearchModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSupplierSearchQuery))]
    [NotifyPropertyChangedFor(nameof(SupplierModalFooterText))]
    private string _supplierSearchQuery = string.Empty;

    public ObservableCollection<PurchaseSupplierLookupDto> FilteredSuppliers { get; } = [];

    [ObservableProperty]
    private PurchaseSupplierLookupDto? _selectedSupplierInModal;

    public bool HasSupplierSearchQuery => !string.IsNullOrWhiteSpace(SupplierSearchQuery);
    public bool ShowEmptySupplierModalState => FilteredSuppliers.Count == 0;
    public string SupplierModalFooterText => HasSupplierSearchQuery
        ? $"{FilteredSuppliers.Count} result(s) for \"{SupplierSearchQuery.Trim()}\""
        : "Recent suppliers";

    // 4.2 Purchase Invoice Search Modal (Exact PurchaseInvoiceSearch.jsx)
    [ObservableProperty]
    private bool _isInvoiceSearchModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvoiceSearchQuery))]
    [NotifyPropertyChangedFor(nameof(InvoiceModalFooterText))]
    private string _invoiceSearchQuery = string.Empty;

    public ObservableCollection<PurchaseReturnInvoiceLookupDto> InvoiceSearchResults { get; } = [];

    [ObservableProperty]
    private PurchaseReturnInvoiceLookupDto? _selectedInvoiceInModal;

    public bool HasInvoiceSearchQuery => !string.IsNullOrWhiteSpace(InvoiceSearchQuery);
    public bool ShowEmptyInvoiceModalState => InvoiceSearchResults.Count == 0;
    public string InvoiceModalFooterText => HasInvoiceSearchQuery
        ? $"{InvoiceSearchResults.Count} result(s) for \"{InvoiceSearchQuery.Trim()}\""
        : "Recent invoices";

    // 4.3 Product Search Modal (Exact ProductSearchInput.jsx with Drag & 8-Way Resize)
    [ObservableProperty]
    private bool _isProductSearchModalOpen;

    [ObservableProperty]
    private int _productSearchTargetRowIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProductSearchQuery))]
    [NotifyPropertyChangedFor(nameof(ShowAllProductsFooterText))]
    [NotifyPropertyChangedFor(nameof(ProductSearchTrimmedQueryText))]
    private string _productSearchQuery = string.Empty;

    public ObservableCollection<PurchaseReturnProductSearchDto> ProductSearchResults { get; } = [];

    [ObservableProperty]
    private PurchaseReturnProductSearchDto? _selectedProductInModal;

    [ObservableProperty]
    private PurchaseModalWindowLayoutDto _productSearchModalLayout = new() { Width = 960, Height = 600 };

    public bool HasProductSearchQuery => !string.IsNullOrWhiteSpace(ProductSearchQuery);
    public bool ShowAllProductsFooterText => !HasProductSearchQuery;
    public int ProductSearchResultsCount => ProductSearchResults.Count;
    public string ProductSearchTrimmedQueryText => $"\"{ProductSearchQuery.Trim()}\"";
    public bool ShowEmptyProductSearchState => ProductSearchResults.Count == 0;

    // 4.4 Batch Search Modal (Fallback F3 modal in addition to inline SearchableSelect)
    [ObservableProperty]
    private bool _isBatchSearchModalOpen;

    [ObservableProperty]
    private int _batchSearchTargetRowIndex;

    [ObservableProperty]
    private string _batchSearchQuery = string.Empty;

    public ObservableCollection<PurchaseReturnBatchOptionDto> BatchSearchResults { get; } = [];

    [ObservableProperty]
    private PurchaseReturnBatchOptionDto? _selectedBatchInModal;

    // 4.5 View Return Detail Modal
    [ObservableProperty]
    private bool _isDetailModalOpen;

    [ObservableProperty]
    private PurchaseReturnDetailDto? _viewingDetail;

    // 4.6 Document History Modal
    [ObservableProperty]
    private bool _isHistoryModalOpen;

    [ObservableProperty]
    private string _historyModalTitle = "Purchase Return Audit History";

    public ObservableCollection<DocumentHistoryEntryDto> HistoryEntries { get; } = [];

    // 4.7 Pre-Shift Cash Drawer Edit Prompt Modal
    [ObservableProperty]
    private bool _isPreShiftModalOpen;

    [ObservableProperty]
    private PurchaseReturnPreShiftCheckResult? _preShiftCheck;

    // 4.8 Password-Confirmed Delete Modal
    [ObservableProperty]
    private bool _isDeleteModalOpen;

    [ObservableProperty]
    private PurchaseReturnDirectoryRowDto? _deletingRow;

    [ObservableProperty]
    private string _deletePassword = string.Empty;

    [ObservableProperty]
    private string _deleteErrorMessage = string.Empty;

    // =========================================================================
    // 5. INITIALIZATION & DIRECTORY COMMANDS
    // =========================================================================

    public async Task InitializeAsync(string? initialRoute = null)
    {
        await LoadBootstrapMetadataAsync();

        if (!string.IsNullOrWhiteSpace(initialRoute) &&
            initialRoute.Contains("/create", StringComparison.OrdinalIgnoreCase))
        {
            await OpenCreateFormAsync();
        }
        else
        {
            ActiveSubView = "Directory";
            await RefreshDirectoryAsync();
        }
    }

    private async Task LoadBootstrapMetadataAsync()
    {
        var boot = await _returnService.GetFormBootstrapAsync();
        CurrencySymbol = boot.CurrencySymbol;
        StoreName = boot.StoreName;
        StoreAddress = boot.StoreAddress;
        StorePhone = boot.StorePhone;

        AllSuppliers.Clear();
        foreach (var s in boot.Suppliers)
            AllSuppliers.Add(s);

        PaymentAccounts.Clear();
        foreach (var a in boot.Accounts)
            PaymentAccounts.Add(a);

        RefreshAccountOptionsForPaymentMethod();

        if (_purchaseInvoiceService != null)
        {
            try
            {
                ProductSearchModalLayout = await _purchaseInvoiceService.GetModalWindowLayoutAsync("productSearchModal");
            }
            catch
            {
                // Keep default 960x600
            }
        }
    }

    [RelayCommand]
    public async Task RefreshDirectoryAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var filter = new PurchaseReturnDirectoryFilter
            {
                PostedNumber = FilterPostedNumber,
                SupplierSearch = FilterSupplierSearch,
                SettlementMode = FilterSettlementMode,
                Page = CurrentPage,
                PageSize = PageSize
            };

            var page = await _returnService.GetDirectoryPageAsync(filter);
            DirectoryRows.Clear();
            foreach (var r in page.Items)
                DirectoryRows.Add(r);

            TotalCount = page.TotalCount;
            TotalPages = page.TotalPages;
            TotalReturnsCount = page.TotalReturnsCount;
            TotalGrossAmount = page.TotalGrossAmount;
            TotalNetAmount = page.TotalNetAmount;
            TotalCashRefundAmount = page.TotalCashRefundAmount;
            TotalLedgerCreditAmount = page.TotalLedgerCreditAmount;

            OnPropertyChanged(nameof(HasDirectoryItems));
            OnPropertyChanged(nameof(ShowEmptyDirectoryState));
            OnPropertyChanged(nameof(DirectoryRangeSubtitleText));
            OnPropertyChanged(nameof(DirectoryTotalSubtitleText));
            OnPropertyChanged(nameof(PaginationSummaryText));
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to load purchase returns: {ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SearchDirectoryAsync()
    {
        CurrentPage = 1;
        await RefreshDirectoryAsync();
    }

    [RelayCommand]
    public async Task ResetDirectoryFiltersAsync()
    {
        _suppressDirectoryReload = true;
        try
        {
            FilterPostedNumber = string.Empty;
            FilterSupplierSearch = string.Empty;
            FilterSettlementMode = "all";
            CurrentPage = 1;
        }
        finally
        {
            _suppressDirectoryReload = false;
        }
        await RefreshDirectoryAsync();
    }

    [RelayCommand]
    public async Task FirstPageAsync()
    {
        if (CurrentPage > 1)
        {
            CurrentPage = 1;
            await RefreshDirectoryAsync();
        }
    }

    [RelayCommand]
    public async Task NextPageAsync()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            await RefreshDirectoryAsync();
        }
    }

    [RelayCommand]
    public async Task PreviousPageAsync()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            await RefreshDirectoryAsync();
        }
    }

    [RelayCommand]
    public async Task LastPageAsync()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage = TotalPages;
            await RefreshDirectoryAsync();
        }
    }

    // =========================================================================
    // 6. CREATE / EDIT FORM WORKFLOW
    // =========================================================================

    [RelayCommand]
    public async Task OpenCreateFormAsync()
    {
        await LoadBootstrapMetadataAsync();

        EditingReturnId = null;
        FormPostedNumber = string.Empty;
        FormDate = DateTime.Today;
        FormSupplierId = 0;
        FormSupplierName = string.Empty;
        FormSupplierPayableBalance = 0m;
        FormPurchaseInvoiceId = null;
        FormPurchaseInvoicePostedNumber = string.Empty;
        FormPurchaseInvoiceType = string.Empty;
        FormPurchaseInvoiceRemaining = 0m;
        FormRemarks = string.Empty;
        FormGrossTotal = 0m;
        FormDiscountPercentage = 0m;
        FormDiscountAmount = 0m;
        FormTaxPercentage = 0m;
        FormTaxAmount = 0m;
        FormTotal = 0m;
        FooterDiscountPctText = "0";
        FooterTaxPctText = "0";
        FormSettlementMode = "cash_refund";
        FormPaymentMethod = "cash";
        FormAccountId = null;
        SelectedPaymentMethodOption = PaymentMethodOptions.FirstOrDefault(o => o.Key == "cash") ?? PaymentMethodOptions.FirstOrDefault();
        RefreshAccountOptionsForPaymentMethod();

        ActiveInvoiceItems.Clear();
        Lines.Clear();
        Lines.Add(CreateBlankLine(1));
        ActiveRowIndex = 0;
        ClearStatus();

        ActiveSubView = "Form";

        // Auto-open Supplier Search Modal on new return load (matching PurchaseReturnForm.jsx:108-113)
        OpenSupplierSearchModal();
    }

    [RelayCommand]
    public async Task OpenEditFormAsync(PurchaseReturnDirectoryRowDto? row)
    {
        var targetId = row?.Id ?? ViewingDetail?.Id;
        if (targetId is not > 0) return;

        await OpenEditFormByIdAsync(targetId.Value);
    }

    public async Task OpenEditFormByIdAsync(long returnId)
    {
        try
        {
            IsBusy = true;
            IsDetailModalOpen = false;
            await LoadBootstrapMetadataAsync();

            var detail = await _returnService.GetByIdAsync(returnId);
            if (detail == null)
            {
                SetStatus($"Purchase return #{returnId} was not found.", isError: true);
                return;
            }

            EditingReturnId = detail.Id;
            FormPostedNumber = detail.PostedNumber;
            FormDate = detail.Date;
            FormSupplierId = detail.SupplierId;
            FormSupplierName = detail.SupplierName;
            var supLookup = AllSuppliers.FirstOrDefault(s => s.Id == detail.SupplierId);
            FormSupplierPayableBalance = supLookup?.CurrentPayableBalance ?? detail.SupplierBalanceAfter;

            FormPurchaseInvoiceId = detail.PurchaseInvoiceId;
            FormPurchaseInvoicePostedNumber = detail.PurchaseInvoicePostedNumber ?? string.Empty;
            FormPurchaseInvoiceType = detail.PurchaseInvoiceType ?? string.Empty;
            FormRemarks = detail.Remarks ?? string.Empty;
            FormSettlementMode = detail.SettlementMode;
            FormPaymentMethod = detail.PaymentMethod;
            FormAccountId = detail.AccountId;
            SelectedPaymentMethodOption = PaymentMethodOptions.FirstOrDefault(o => o.Key == detail.PaymentMethod) ?? PaymentMethodOptions.FirstOrDefault();
            RefreshAccountOptionsForPaymentMethod();

            ActiveInvoiceItems.Clear();
            if (detail.PurchaseInvoiceId is > 0)
            {
                var invItems = await _returnService.GetInvoiceReturnableItemsAsync(detail.PurchaseInvoiceId.Value, detail.Id);
                foreach (var ii in invItems)
                    ActiveInvoiceItems.Add(ii);
            }

            Lines.Clear();
            for (var i = 0; i < detail.Items.Count; i++)
            {
                var src = detail.Items[i];
                var lineVm = CreateBlankLine(i + 1);
                var batches = await _returnService.GetProductBatchesForReturnAsync(src.ProductId, detail.PurchaseInvoiceId, detail.Id);
                foreach (var b in batches)
                    lineVm.RowBatches.Add(b);

                var cleanBatch = (src.Batch ?? string.Empty).Trim();
                var isUnb = string.IsNullOrEmpty(cleanBatch);
                var matchedBatchOpt = isUnb
                    ? lineVm.RowBatches.FirstOrDefault(b => b.IsUnbatched)
                    : lineVm.RowBatches.FirstOrDefault(b => !b.IsUnbatched && string.Equals(b.BatchNumber.Trim(), cleanBatch, StringComparison.OrdinalIgnoreCase));

                lineVm.HydrateSilent(l =>
                {
                    l.ProductId = src.ProductId;
                    l.ProductCode = src.ProductCode;
                    l.ProductName = src.ProductName;
                    l.Batch = cleanBatch;
                    l.IsUnbatched = isUnb;
                    l.SelectedBatchOption = matchedBatchOpt;
                    l.Expiry = src.Expiry;
                    l.PackSize = src.PackSize > 0 ? src.PackSize : 1;
                    l.PackPurchasedQuantity = src.PackPurchasedQuantity;
                    l.AvailableUnits = src.AvailableUnits;
                    l.ReturnPackQuantity = Math.Round(src.ReturnPackQuantity, 2);
                    l.ReturnUnitQuantity = src.ReturnUnitQuantity;
                    l.PackPurchasePrice = Math.Round(src.PackPurchasePrice, 2);
                    l.UnitPurchasePrice = Math.Round(src.UnitPurchasePrice, 4);
                    l.ItemDiscountPercentage = Math.Round(src.ItemDiscountPercentage, 2);
                    l.SubTotal = Math.Round(src.SubTotal, 2);
                });
                Lines.Add(lineVm);
            }

            if (Lines.Count == 0)
                Lines.Add(CreateBlankLine(1));

            try
            {
                _isFooterSyncing = true;
                FormGrossTotal = detail.GrossTotal;
                FormDiscountPercentage = detail.DiscountPercentage;
                FormDiscountAmount = detail.DiscountAmount;
                FormTaxPercentage = detail.TaxPercentage;
                FormTaxAmount = detail.TaxAmount;
                FormTotal = detail.Total;
                FooterDiscountPctText = detail.DiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture);
                FooterTaxPctText = detail.TaxPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            }
            finally
            {
                _isFooterSyncing = false;
            }

            ClearStatus();
            ActiveSubView = "Form";
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to load purchase return for editing: {ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CancelFormAsync()
    {
        ActiveSubView = "Directory";
        ClearStatus();
        await RefreshDirectoryAsync();
    }

    [RelayCommand]
    public void DismissStatusBanner()
    {
        ClearStatus();
    }

    // =========================================================================
    // 7. SUPPLIER & PURCHASE INVOICE SELECTION MODALS
    // =========================================================================

    [RelayCommand]
    public void OpenSupplierSearchModal(string? initialSeed = null)
    {
        SupplierSearchQuery = initialSeed ?? string.Empty;
        FilterSupplierModalList();
        IsSupplierSearchModalOpen = true;
    }

    [RelayCommand]
    public void ClearSupplierSearchQuery()
    {
        SupplierSearchQuery = string.Empty;
    }

    partial void OnSupplierSearchQueryChanged(string value)
    {
        FilterSupplierModalList();
    }

    private void FilterSupplierModalList()
    {
        var q = (SupplierSearchQuery ?? string.Empty).Trim().ToLowerInvariant();
        var matches = string.IsNullOrEmpty(q)
            ? AllSuppliers.ToList()
            : AllSuppliers.Where(s =>
                s.Name.ToLowerInvariant().Contains(q) ||
                (!string.IsNullOrWhiteSpace(s.Phone) && s.Phone.ToLowerInvariant().Contains(q)) ||
                (!string.IsNullOrWhiteSpace(s.Address) && s.Address.ToLowerInvariant().Contains(q))).ToList();

        FilteredSuppliers.Clear();
        foreach (var s in matches)
            FilteredSuppliers.Add(s);

        SelectedSupplierInModal = FilteredSuppliers.FirstOrDefault(s => s.Id == FormSupplierId)
            ?? FilteredSuppliers.FirstOrDefault();

        OnPropertyChanged(nameof(ShowEmptySupplierModalState));
        OnPropertyChanged(nameof(SupplierModalFooterText));
    }

    public void MoveSupplierModalHighlight(int delta)
    {
        if (FilteredSuppliers.Count == 0) return;
        var idx = SelectedSupplierInModal != null ? FilteredSuppliers.IndexOf(SelectedSupplierInModal) : -1;
        var next = Math.Clamp(idx + delta, 0, FilteredSuppliers.Count - 1);
        SelectedSupplierInModal = FilteredSuppliers[next];
    }

    [RelayCommand]
    public void SelectSupplierFromModal(PurchaseSupplierLookupDto? supplier)
    {
        var chosen = supplier ?? SelectedSupplierInModal;
        if (chosen == null) return;

        var changedSupplier = FormSupplierId != chosen.Id;
        FormSupplierId = chosen.Id;
        FormSupplierName = chosen.Name;
        FormSupplierPayableBalance = chosen.CurrentPayableBalance;
        IsSupplierSearchModalOpen = false;

        if (changedSupplier)
        {
            FormPurchaseInvoiceId = null;
            FormPurchaseInvoicePostedNumber = string.Empty;
            FormPurchaseInvoiceType = string.Empty;
            FormPurchaseInvoiceRemaining = 0m;
            ActiveInvoiceItems.Clear();
            FormSettlementMode = "cash_refund";

            Lines.Clear();
            Lines.Add(CreateBlankLine(1));
            RecalculateFooter(null);
        }

        RequestFocusField?.Invoke("invoice_trigger", 0);
    }

    [RelayCommand]
    public void CloseSupplierSearchModal()
    {
        IsSupplierSearchModalOpen = false;
    }

    [RelayCommand]
    public async Task OpenInvoiceSearchModalAsync()
    {
        InvoiceSearchQuery = string.Empty;
        IsInvoiceSearchModalOpen = true;
        await SearchSupplierInvoicesInternalAsync();
    }

    [RelayCommand]
    public void ClearInvoiceSearchQuery()
    {
        InvoiceSearchQuery = string.Empty;
    }

    partial void OnInvoiceSearchQueryChanged(string value)
    {
        _ = SearchSupplierInvoicesInternalAsync();
    }

    private async Task SearchSupplierInvoicesInternalAsync()
    {
        if (!IsInvoiceSearchModalOpen) return;
        var supId = FormSupplierId > 0 ? FormSupplierId : (long?)null;
        var list = await _returnService.SearchSupplierInvoicesAsync(supId, InvoiceSearchQuery, 50);

        InvoiceSearchResults.Clear();
        foreach (var inv in list)
            InvoiceSearchResults.Add(inv);

        SelectedInvoiceInModal = InvoiceSearchResults.FirstOrDefault(i => i.Id == FormPurchaseInvoiceId)
            ?? InvoiceSearchResults.FirstOrDefault();

        OnPropertyChanged(nameof(ShowEmptyInvoiceModalState));
        OnPropertyChanged(nameof(InvoiceModalFooterText));
    }

    public void MoveInvoiceModalHighlight(int delta)
    {
        if (InvoiceSearchResults.Count == 0) return;
        var idx = SelectedInvoiceInModal != null ? InvoiceSearchResults.IndexOf(SelectedInvoiceInModal) : -1;
        var next = Math.Clamp(idx + delta, 0, InvoiceSearchResults.Count - 1);
        SelectedInvoiceInModal = InvoiceSearchResults[next];
    }

    [RelayCommand]
    public async Task SelectInvoiceFromModalAsync(PurchaseReturnInvoiceLookupDto? invoice)
    {
        var chosen = invoice ?? SelectedInvoiceInModal;
        if (chosen == null) return;

        IsInvoiceSearchModalOpen = false;

        if (FormSupplierId != chosen.SupplierId)
        {
            FormSupplierId = chosen.SupplierId;
            FormSupplierName = chosen.SupplierName;
            var supObj = AllSuppliers.FirstOrDefault(s => s.Id == chosen.SupplierId);
            FormSupplierPayableBalance = supObj?.CurrentPayableBalance ?? 0m;
        }

        FormPurchaseInvoiceId = chosen.Id;
        FormPurchaseInvoicePostedNumber = chosen.PostedNumber;
        FormPurchaseInvoiceType = chosen.InvoiceType;
        FormPurchaseInvoiceRemaining = chosen.RemainingAmount;

        // Auto-select settlement mode from linked invoice type (matching PurchaseReturnForm.jsx:784)
        FormSettlementMode = chosen.IsCredit ? "credit_note" : "cash_refund";

        var returnableItems = await _returnService.GetInvoiceReturnableItemsAsync(chosen.Id, EditingReturnId);
        ActiveInvoiceItems.Clear();
        foreach (var item in returnableItems)
            ActiveInvoiceItems.Add(item);

        Lines.Clear();
        Lines.Add(CreateBlankLine(1));
        RecalculateFooter(null);

        RequestFocusField?.Invoke("product", 0);
    }

    [RelayCommand]
    public void ClearLinkedInvoice()
    {
        if (!HasSelectedInvoice) return;

        FormPurchaseInvoiceId = null;
        FormPurchaseInvoicePostedNumber = string.Empty;
        FormPurchaseInvoiceType = string.Empty;
        FormPurchaseInvoiceRemaining = 0m;
        ActiveInvoiceItems.Clear();
        FormSettlementMode = "cash_refund";

        Lines.Clear();
        Lines.Add(CreateBlankLine(1));
        RecalculateFooter(null);

        RequestFocusField?.Invoke("product", 0);
    }

    [RelayCommand]
    public void CloseInvoiceSearchModal()
    {
        IsInvoiceSearchModalOpen = false;
    }

    // =========================================================================
    // 8. PRODUCT & BATCH SELECTION + REPEATER ROWS
    // =========================================================================

    private PurchaseReturnLineEditorViewModel CreateBlankLine(int rowNumber)
    {
        return new PurchaseReturnLineEditorViewModel(OnLineChanged, OnLineBatchOptionPicked)
        {
            RowNumber = rowNumber,
            PackSize = 1
        };
    }

    private bool OnLineBatchOptionPicked(PurchaseReturnLineEditorViewModel row, PurchaseReturnBatchOptionDto chosen)
    {
        var rowIndex = Lines.IndexOf(row);
        if (rowIndex < 0) return false;

        var chosenKey = chosen.IsUnbatched ? "__unbatched__" : chosen.BatchNumber.Trim().ToUpperInvariant();
        var duplicateRowIdx = -1;
        for (var i = 0; i < Lines.Count; i++)
        {
            if (i == rowIndex || Lines[i].ProductId != row.ProductId)
                continue;

            var otherKey = Lines[i].IsUnbatched || string.IsNullOrWhiteSpace(Lines[i].Batch)
                ? "__unbatched__"
                : Lines[i].Batch.Trim().ToUpperInvariant();

            if (string.Equals(otherKey, chosenKey, StringComparison.OrdinalIgnoreCase))
            {
                duplicateRowIdx = i;
                break;
            }
        }

        if (duplicateRowIdx >= 0)
        {
            var errMsg = chosen.IsUnbatched
                ? $"Row {duplicateRowIdx + 1} already has unassigned stock for \"{row.ProductName}\". Each batch (or unassigned stock) can only be selected once per product."
                : $"Duplicate (product + batch \"{chosen.BatchNumber}\") in row {duplicateRowIdx + 1}. Use a unique batch for the same product.";
            SetStatus(errMsg, isError: true);
            row.HasRowError = true;
            row.RowErrorMessage = errMsg;
            return false;
        }

        row.HydrateSilent(l =>
        {
            l.SelectedBatchOption = chosen;
            l.Batch = chosen.IsUnbatched ? string.Empty : chosen.BatchNumber;
            l.IsUnbatched = chosen.IsUnbatched;
            l.Expiry = chosen.IsUnbatched ? null : chosen.ExpiryDate;
            l.PackSize = chosen.PackSize > 0 ? chosen.PackSize : l.PackSize;
            l.PackPurchasedQuantity = chosen.PackPurchasedQuantity;
            l.AvailableUnits = chosen.AvailableUnits;
            if (chosen.PackPurchasePrice > 0m)
                l.PackPurchasePrice = Math.Round(chosen.PackPurchasePrice, 2);
            if (chosen.UnitPurchasePrice > 0m)
                l.UnitPurchasePrice = Math.Round(chosen.UnitPurchasePrice, 4);
            l.ItemDiscountPercentage = Math.Round(chosen.ItemDiscountPercentage, 2);
            l.HasRowError = false;
            l.RowErrorMessage = string.Empty;
        });

        row.Recalculate("batch");
        ClearStatus();
        return true;
    }

    private void RenumberLines()
    {
        for (var i = 0; i < Lines.Count; i++)
            Lines[i].RowNumber = i + 1;
    }

    [RelayCommand]
    public void AddRow()
    {
        var newLine = CreateBlankLine(Lines.Count + 1);
        Lines.Add(newLine);
        ActiveRowIndex = Lines.Count - 1;
        RequestFocusField?.Invoke("product", ActiveRowIndex);
    }

    [RelayCommand]
    public void RemoveRow(PurchaseReturnLineEditorViewModel? line)
    {
        if (line == null) return;
        if (Lines.Count <= 1)
        {
            Lines[0].HydrateSilent(l =>
            {
                l.ProductId = 0;
                l.ProductCode = string.Empty;
                l.ProductName = string.Empty;
                l.BrandName = string.Empty;
                l.Rack = string.Empty;
                l.Batch = string.Empty;
                l.IsUnbatched = false;
                l.SelectedBatchOption = null;
                l.Expiry = null;
                l.PackSize = 1;
                l.PackPurchasedQuantity = 0m;
                l.AvailableUnits = 0;
                l.ReturnPackQuantity = 0m;
                l.ReturnUnitQuantity = 0;
                l.PackPurchasePrice = 0m;
                l.UnitPurchasePrice = 0m;
                l.ItemDiscountPercentage = 0m;
                l.SubTotal = 0m;
                l.RowBatches.Clear();
            });
            RecalculateFooter(null);
            return;
        }

        Lines.Remove(line);
        RenumberLines();
        RecalculateFooter(null);
    }

    public async Task OpenProductSearchForRowAsync(int rowIndex, string? initialQuery = null)
    {
        if (rowIndex < 0 || rowIndex >= Lines.Count) return;
        ActiveRowIndex = rowIndex;
        ProductSearchTargetRowIndex = rowIndex;
        ProductSearchQuery = initialQuery ?? string.Empty;
        IsProductSearchModalOpen = true;
        RequestApplyProductSearchModalLayout?.Invoke();
        await RefreshProductSearchResultsAsync();
    }

    [RelayCommand]
    public void ClearProductSearchQuery()
    {
        ProductSearchQuery = string.Empty;
    }

    partial void OnProductSearchQueryChanged(string value)
    {
        if (!IsProductSearchModalOpen) return;

        _productSearchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _productSearchDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, cts.Token);
                if (cts.IsCancellationRequested) return;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(RefreshProductSearchResultsAsync);
            }
            catch (TaskCanceledException)
            {
            }
        });
    }

    private async Task RefreshProductSearchResultsAsync()
    {
        var results = await _returnService.SearchProductsForReturnAsync(
            ProductSearchQuery,
            FormSupplierId > 0 ? FormSupplierId : null,
            FormPurchaseInvoiceId,
            EditingReturnId,
            limit: 150);

        ProductSearchResults.Clear();
        foreach (var p in results)
            ProductSearchResults.Add(p);

        SelectedProductInModal = ProductSearchResults.FirstOrDefault();

        OnPropertyChanged(nameof(ProductSearchResultsCount));
        OnPropertyChanged(nameof(ShowEmptyProductSearchState));
    }

    public void MoveProductSearchHighlight(int delta)
    {
        if (ProductSearchResults.Count == 0) return;
        var idx = SelectedProductInModal != null ? ProductSearchResults.IndexOf(SelectedProductInModal) : -1;
        var next = Math.Clamp(idx + delta, 0, ProductSearchResults.Count - 1);
        SelectedProductInModal = ProductSearchResults[next];
    }

    public async Task SaveProductSearchModalLayoutAsync(double x, double y, double width, double height)
    {
        var updated = new PurchaseModalWindowLayoutDto
        {
            X = Math.Round(x, 1),
            Y = Math.Round(y, 1),
            Width = Math.Round(Math.Max(640d, width), 1),
            Height = Math.Round(Math.Max(420d, height), 1)
        };
        ProductSearchModalLayout = updated;

        if (_purchaseInvoiceService != null)
        {
            try
            {
                await _purchaseInvoiceService.SaveModalWindowLayoutAsync("productSearchModal", updated);
            }
            catch
            {
                // Ignore persistence errors
            }
        }
    }

    [RelayCommand]
    public async Task SelectProductFromModalAsync(PurchaseReturnProductSearchDto? product)
    {
        var chosen = product ?? SelectedProductInModal;
        if (chosen == null) return;

        var rowIndex = ProductSearchTargetRowIndex;
        IsProductSearchModalOpen = false;

        if (rowIndex < 0 || rowIndex >= Lines.Count) return;
        var row = Lines[rowIndex];

        var batches = await _returnService.GetProductBatchesForReturnAsync(
            chosen.Id,
            FormPurchaseInvoiceId,
            EditingReturnId);

        // Filter out batches already selected for the same product on other rows
        var usedKeys = Lines
            .Where((l, idx) => idx != rowIndex && l.ProductId == chosen.Id)
            .Select(l => l.IsUnbatched || string.IsNullOrWhiteSpace(l.Batch) ? "__unbatched__" : l.Batch.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var availableBatches = batches
            .Where(b =>
            {
                var key = b.IsUnbatched ? "__unbatched__" : b.BatchNumber.Trim().ToUpperInvariant();
                return !usedKeys.Contains(key);
            })
            .ToList();

        if (availableBatches.Count == 0 && usedKeys.Count > 0)
        {
            SetStatus($"All batches/unassigned stock for '{chosen.Name}' are already added on earlier rows.", isError: true);
            RequestFocusField?.Invoke("product", rowIndex);
            return;
        }

        row.RowBatches.Clear();
        foreach (var b in batches)
            row.RowBatches.Add(b);

        var assignedBatches = batches.Where(b => !b.IsUnbatched && !string.IsNullOrWhiteSpace(b.BatchNumber)).ToList();

        if (assignedBatches.Count == 0 || availableBatches.Count == 1)
        {
            var autoOpt = availableBatches.FirstOrDefault() ?? batches.FirstOrDefault();
            var isUnb = autoOpt?.IsUnbatched ?? true;

            row.HydrateSilent(l =>
            {
                l.ProductId = chosen.Id;
                l.ProductCode = chosen.ProductCode;
                l.ProductName = chosen.Name;
                l.BrandName = chosen.BrandName;
                l.Rack = chosen.Rack ?? string.Empty;
                l.SelectedBatchOption = autoOpt;
                l.Batch = isUnb ? string.Empty : (autoOpt?.BatchNumber ?? string.Empty);
                l.IsUnbatched = isUnb;
                l.Expiry = isUnb ? null : autoOpt?.ExpiryDate;
                l.PackSize = autoOpt?.PackSize > 0 ? autoOpt.PackSize : (chosen.PackSize > 0 ? chosen.PackSize : 1);
                l.PackPurchasedQuantity = autoOpt?.PackPurchasedQuantity ?? 0m;
                l.AvailableUnits = autoOpt?.AvailableUnits ?? chosen.DisplayAvailableUnits;
                l.ReturnPackQuantity = 0m;
                l.ReturnUnitQuantity = 0;
                l.PackPurchasePrice = Math.Round(autoOpt?.PackPurchasePrice > 0m ? autoOpt.PackPurchasePrice : chosen.PackPurchasePrice, 2);
                l.UnitPurchasePrice = Math.Round(autoOpt?.UnitPurchasePrice > 0m ? autoOpt.UnitPurchasePrice : chosen.UnitPurchasePrice, 4);
                l.ItemDiscountPercentage = Math.Round(autoOpt?.ItemDiscountPercentage ?? 0m, 2);
                l.SubTotal = 0m;
                l.HasRowError = false;
                l.RowErrorMessage = string.Empty;
            });

            ClearStatus();
            RecalculateFooter(null);
            RequestFocusField?.Invoke("pack_quantity", rowIndex);
            return;
        }

        // Multiple batches available -> set product & focus the inline Batch SearchableSelect on this row!
        row.HydrateSilent(l =>
        {
            l.ProductId = chosen.Id;
            l.ProductCode = chosen.ProductCode;
            l.ProductName = chosen.Name;
            l.BrandName = chosen.BrandName;
            l.Rack = chosen.Rack ?? string.Empty;
            l.SelectedBatchOption = null;
            l.Batch = string.Empty;
            l.IsUnbatched = false;
            l.Expiry = null;
            l.PackSize = chosen.PackSize > 0 ? chosen.PackSize : 1;
            l.PackPurchasedQuantity = 0m;
            l.AvailableUnits = 0;
            l.ReturnPackQuantity = 0m;
            l.ReturnUnitQuantity = 0;
            l.PackPurchasePrice = Math.Round(chosen.PackPurchasePrice, 2);
            l.UnitPurchasePrice = Math.Round(chosen.UnitPurchasePrice, 4);
            l.ItemDiscountPercentage = 0m;
            l.SubTotal = 0m;
        });

        RecalculateFooter(null);
        RequestFocusField?.Invoke("batch", rowIndex);
    }

    [RelayCommand]
    public void CloseProductSearchModal()
    {
        IsProductSearchModalOpen = false;
    }

    public async Task OpenBatchSearchForRowAsync(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= Lines.Count) return;
        var row = Lines[rowIndex];
        if (row.ProductId <= 0)
        {
            await OpenProductSearchForRowAsync(rowIndex);
            return;
        }

        ActiveRowIndex = rowIndex;
        BatchSearchTargetRowIndex = rowIndex;
        BatchSearchQuery = string.Empty;

        if (row.RowBatches.Count == 0)
        {
            var fetched = await _returnService.GetProductBatchesForReturnAsync(
                row.ProductId,
                FormPurchaseInvoiceId,
                EditingReturnId);
            foreach (var b in fetched)
                row.RowBatches.Add(b);
        }

        FilterBatchModalList();
        IsBatchSearchModalOpen = true;
    }

    partial void OnBatchSearchQueryChanged(string value)
    {
        if (!IsBatchSearchModalOpen) return;
        FilterBatchModalList();
    }

    private void FilterBatchModalList()
    {
        var rowIndex = BatchSearchTargetRowIndex;
        if (rowIndex < 0 || rowIndex >= Lines.Count) return;
        var row = Lines[rowIndex];

        var usedKeys = Lines
            .Where((l, idx) => idx != rowIndex && l.ProductId == row.ProductId)
            .Select(l => l.IsUnbatched || string.IsNullOrWhiteSpace(l.Batch) ? "__unbatched__" : l.Batch.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var q = (BatchSearchQuery ?? string.Empty).Trim().ToLowerInvariant();
        var filtered = row.RowBatches
            .Where(b =>
            {
                var key = b.IsUnbatched ? "__unbatched__" : b.BatchNumber.Trim().ToUpperInvariant();
                if (usedKeys.Contains(key)) return false;
                if (string.IsNullOrEmpty(q)) return true;
                return b.DisplayBatchNumber.ToLowerInvariant().Contains(q) ||
                       b.ExpiryFormatted.ToLowerInvariant().Contains(q);
            })
            .ToList();

        BatchSearchResults.Clear();
        foreach (var b in filtered)
            BatchSearchResults.Add(b);

        SelectedBatchInModal = BatchSearchResults.FirstOrDefault();
    }

    [RelayCommand]
    public void SelectBatchFromModal(PurchaseReturnBatchOptionDto? batchOpt)
    {
        var chosen = batchOpt ?? SelectedBatchInModal;
        if (chosen == null) return;

        var rowIndex = BatchSearchTargetRowIndex;
        IsBatchSearchModalOpen = false;
        if (rowIndex < 0 || rowIndex >= Lines.Count) return;

        var row = Lines[rowIndex];
        if (OnLineBatchOptionPicked(row, chosen))
        {
            RequestFocusField?.Invoke("pack_quantity", rowIndex);
        }
    }

    [RelayCommand]
    public void CloseBatchSearchModal()
    {
        IsBatchSearchModalOpen = false;
    }

    // =========================================================================
    // 9. FOOTER CALCULATIONS & SETTLEMENT TOGGLES
    // =========================================================================

    private void OnLineChanged(PurchaseReturnLineEditorViewModel line, string? changedField)
    {
        OnPropertyChanged(nameof(ActiveProductLinesCount));
        RecalculateFooter(null);
    }

    partial void OnFooterDiscountPctTextChanged(string value)
    {
        if (_isFooterSyncing) return;
        if (decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d >= 0m)
            FormDiscountPercentage = Math.Clamp(d, 0m, 100m);
        else
            FormDiscountPercentage = 0m;
        RecalculateFooter("discount_percentage");
    }

    partial void OnFooterTaxPctTextChanged(string value)
    {
        if (_isFooterSyncing) return;
        if (decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d >= 0m)
            FormTaxPercentage = Math.Clamp(d, 0m, 100m);
        else
            FormTaxPercentage = 0m;
        RecalculateFooter("tax_percentage");
    }

    partial void OnFormDiscountPercentageChanged(decimal value)
    {
        if (_isFooterSyncing) return;
        RecalculateFooter("discount_percentage");
    }

    partial void OnFormDiscountAmountChanged(decimal value)
    {
        if (_isFooterSyncing) return;
        RecalculateFooter("discount_amount");
    }

    partial void OnFormTaxPercentageChanged(decimal value)
    {
        if (_isFooterSyncing) return;
        RecalculateFooter("tax_percentage");
    }

    partial void OnFormTaxAmountChanged(decimal value)
    {
        if (_isFooterSyncing) return;
        RecalculateFooter("tax_amount");
    }

    private void RecalculateFooter(string? changedField)
    {
        if (_isFooterSyncing) return;
        try
        {
            _isFooterSyncing = true;
            var lineModels = Lines
                .Where(l => l.ProductId > 0)
                .Select(l => new PurchaseReturnLineCalcModel
                {
                    PackSize = l.PackSize > 0 ? l.PackSize : 1,
                    ReturnPackQuantity = l.ReturnPackQuantity,
                    ReturnUnitQuantity = l.ReturnUnitQuantity,
                    PackPurchasePrice = l.PackPurchasePrice,
                    UnitPurchasePrice = l.UnitPurchasePrice,
                    ItemDiscountPercentage = l.ItemDiscountPercentage,
                    SubTotal = l.SubTotal
                })
                .ToList();

            var footer = new PurchaseReturnFooterCalcModel
            {
                DiscountPercentage = FormDiscountPercentage,
                DiscountAmount = FormDiscountAmount,
                TaxPercentage = FormTaxPercentage,
                TaxAmount = FormTaxAmount
            };

            InvoiceCalculators.RecalcPurchaseReturnFooter(lineModels, footer, changedField);

            FormGrossTotal = footer.GrossTotal;
            FormDiscountPercentage = footer.DiscountPercentage;
            FormDiscountAmount = footer.DiscountAmount;
            FormTaxPercentage = footer.TaxPercentage;
            FormTaxAmount = footer.TaxAmount;
            FormTotal = footer.Total;

            if (changedField != "discount_percentage")
                FooterDiscountPctText = FormDiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture);
            if (changedField != "tax_percentage")
                FooterTaxPctText = FormTaxPercentage.ToString("0.##", CultureInfo.InvariantCulture);
        }
        finally
        {
            _isFooterSyncing = false;
        }
    }

    [RelayCommand]
    public void SetSettlementMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return;
        FormSettlementMode = mode.Trim().ToLowerInvariant();
    }

    // =========================================================================
    // 10. SAVE, SAVE & PRINT, PRE-SHIFT PROMPT & DELETE
    // =========================================================================

    [RelayCommand]
    public async Task SaveReturnAsync()
    {
        _pendingPrintAfterSave = false;
        await SaveReturnCoreAsync(checkPreShift: true, updateDrawerIfPreShift: true);
    }

    [RelayCommand]
    public async Task SaveAndPrintReturnAsync()
    {
        _pendingPrintAfterSave = true;
        await SaveReturnCoreAsync(checkPreShift: true, updateDrawerIfPreShift: true);
    }

    [RelayCommand]
    public async Task ConfirmPreShiftUpdateDrawerAsync()
    {
        IsPreShiftModalOpen = false;
        await SaveReturnCoreAsync(checkPreShift: false, updateDrawerIfPreShift: true);
    }

    [RelayCommand]
    public async Task ConfirmPreShiftKeepHistoricalAsync()
    {
        IsPreShiftModalOpen = false;
        await SaveReturnCoreAsync(checkPreShift: false, updateDrawerIfPreShift: false);
    }

    [RelayCommand]
    public void ClosePreShiftModal()
    {
        IsPreShiftModalOpen = false;
    }

    private async Task SaveReturnCoreAsync(bool checkPreShift, bool updateDrawerIfPreShift)
    {
        if (IsBusy) return;
        ClearStatus();

        if (FormSupplierId <= 0)
        {
            SetStatus("Please select a supplier before saving.", isError: true);
            OpenSupplierSearchModal();
            return;
        }

        var activeLines = Lines.Where(l => l.ProductId > 0).ToList();
        if (activeLines.Count == 0)
        {
            SetStatus("Please add at least one product to the purchase return.", isError: true);
            RequestFocusField?.Invoke("product", 0);
            return;
        }

        for (var i = 0; i < Lines.Count; i++)
        {
            var l = Lines[i];
            if (l.ProductId <= 0) continue;

            var hasBatches = l.RowBatches.Any(b => !b.IsUnbatched && !string.IsNullOrWhiteSpace(b.BatchNumber));
            if (hasBatches && string.IsNullOrWhiteSpace(l.Batch) && !l.IsUnbatched)
            {
                SetStatus($"Row {i + 1}: Please pick a batch or unassigned stock for '{l.ProductName}'.", isError: true);
                RequestFocusField?.Invoke("batch", i);
                return;
            }

            if (l.ReturnUnitQuantity <= 0)
            {
                SetStatus($"Row {i + 1}: Return unit quantity must be greater than zero for '{l.ProductName}'.", isError: true);
                RequestFocusField?.Invoke("pack_quantity", i);
                return;
            }

            if (l.AvailableUnits <= 0)
            {
                SetStatus($"Row {i + 1}: '{l.ProductName}' has 0 available returnable units.", isError: true);
                RequestFocusField?.Invoke("product", i);
                return;
            }

            if (l.ReturnUnitQuantity > l.AvailableUnits)
            {
                SetStatus($"Row {i + 1}: Returned units ({l.ReturnUnitQuantity}) exceed available ({l.AvailableUnits}) for '{l.ProductName}'.", isError: true);
                RequestFocusField?.Invoke("pack_quantity", i);
                return;
            }
        }

        if (checkPreShift && EditingReturnId is > 0)
        {
            var preShift = await _returnService.CheckPreShiftCashEditImpactAsync(
                EditingReturnId.Value,
                FormSettlementMode,
                FormPaymentMethod,
                FormTotal);

            if (preShift.RequiresPrompt)
            {
                PreShiftCheck = preShift;
                IsPreShiftModalOpen = true;
                return;
            }
        }

        try
        {
            IsBusy = true;
            var req = new PurchaseReturnUpsertRequest
            {
                SupplierId = FormSupplierId,
                PurchaseInvoiceId = FormPurchaseInvoiceId,
                PostedNumber = EditingReturnId is > 0 ? FormPostedNumber : null,
                Date = FormDate,
                Remarks = FormRemarks,
                GrossTotal = FormGrossTotal,
                DiscountPercentage = FormDiscountPercentage,
                DiscountAmount = FormDiscountAmount,
                TaxPercentage = FormTaxPercentage,
                TaxAmount = FormTaxAmount,
                Total = FormTotal,
                SettlementMode = FormSettlementMode,
                PaymentMethod = FormPaymentMethod,
                AccountId = FormAccountId,
                UpdateDrawerIfPreShift = updateDrawerIfPreShift,
                Items = activeLines.Select(l => new PurchaseReturnLineInput
                {
                    ProductId = l.ProductId,
                    Batch = l.IsUnbatched ? string.Empty : l.Batch,
                    Expiry = l.Expiry,
                    IsUnbatched = l.IsUnbatched,
                    PackSize = l.PackSize > 0 ? l.PackSize : 1,
                    PackPurchasedQuantity = l.PackPurchasedQuantity,
                    AvailableUnits = l.AvailableUnits,
                    ReturnPackQuantity = l.ReturnPackQuantity,
                    ReturnUnitQuantity = l.ReturnUnitQuantity,
                    PackPurchasePrice = l.PackPurchasePrice,
                    UnitPurchasePrice = l.UnitPurchasePrice,
                    ItemDiscountPercentage = l.ItemDiscountPercentage,
                    SubTotal = l.SubTotal
                }).ToList()
            };

            var actorId = _authService.CurrentSession?.UserId;
            var res = EditingReturnId is > 0
                ? await _returnService.UpdateAsync(EditingReturnId.Value, req, actorId)
                : await _returnService.CreateAsync(req, actorId);

            if (!res.Succeeded)
            {
                SetStatus(res.Message, isError: true);
                if (res.RowIndex.HasValue && res.RowIndex.Value >= 0 && res.RowIndex.Value < Lines.Count)
                {
                    Lines[res.RowIndex.Value].HasRowError = true;
                    Lines[res.RowIndex.Value].RowErrorMessage = res.Message;
                }
                return;
            }

            _toast?.ShowSuccess(res.Message, IsEditMode ? "Return Updated" : "Return Created");
            ClearStatus();

            if (_pendingPrintAfterSave && res.ReturnId is > 0)
            {
                var savedDetail = await _returnService.GetByIdAsync(res.ReturnId.Value);
                if (savedDetail != null)
                {
                    RequestPrintDocument?.Invoke(savedDetail);
                }
            }

            ActiveSubView = "Directory";
            await RefreshDirectoryAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Error saving purchase return: {ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // 11. VIEW DETAIL, PRINT, HISTORY & DELETE MODALS
    // =========================================================================

    [RelayCommand]
    public async Task ViewReturnDetailAsync(PurchaseReturnDirectoryRowDto? row)
    {
        var target = row ?? SelectedDirectoryRow;
        if (target == null) return;

        try
        {
            IsBusy = true;
            var detail = await _returnService.GetByIdAsync(target.Id);
            if (detail == null)
            {
                SetStatus($"Purchase return #{target.Id} was not found.", isError: true);
                return;
            }

            ViewingDetail = detail;
            IsDetailModalOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void CloseDetailModal()
    {
        IsDetailModalOpen = false;
    }

    [RelayCommand]
    public async Task PrintReturnAsync(PurchaseReturnDirectoryRowDto? row)
    {
        if (row != null)
        {
            var detail = await _returnService.GetByIdAsync(row.Id);
            if (detail != null)
                RequestPrintDocument?.Invoke(detail);
            return;
        }

        if (ViewingDetail != null)
        {
            RequestPrintDocument?.Invoke(ViewingDetail);
        }
    }

    [RelayCommand]
    public async Task OpenHistoryModalAsync(PurchaseReturnDirectoryRowDto? row = null)
    {
        var targetId = row?.Id ?? ViewingDetail?.Id;
        var targetPosted = row?.PostedNumber ?? ViewingDetail?.PostedNumber ?? string.Empty;
        if (targetId is not > 0) return;

        HistoryModalTitle = $"Audit History — {targetPosted}";
        var logs = await _returnService.GetDocumentHistoryAsync(targetId.Value);
        HistoryEntries.Clear();
        foreach (var l in logs)
            HistoryEntries.Add(l);
        IsHistoryModalOpen = true;
    }

    [RelayCommand]
    public void CloseHistoryModal()
    {
        IsHistoryModalOpen = false;
    }

    [RelayCommand]
    public void PromptDeleteReturn(PurchaseReturnDirectoryRowDto? row)
    {
        var target = row ?? SelectedDirectoryRow;
        if (target == null) return;

        DeletingRow = target;
        DeletePassword = string.Empty;
        DeleteErrorMessage = string.Empty;
        IsDeleteModalOpen = true;
    }

    [RelayCommand]
    public async Task ConfirmDeleteReturnAsync()
    {
        if (DeletingRow == null) return;
        if (string.IsNullOrWhiteSpace(DeletePassword))
        {
            DeleteErrorMessage = "Please enter your password to confirm deletion.";
            return;
        }

        try
        {
            IsBusy = true;
            var actorId = _authService.CurrentSession?.UserId;
            var res = await _returnService.DeleteAsync(DeletingRow.Id, DeletePassword, actorId);
            if (!res.Succeeded)
            {
                DeleteErrorMessage = res.Message;
                return;
            }

            IsDeleteModalOpen = false;
            DeletingRow = null;
            DeletePassword = string.Empty;
            _toast?.ShowSuccess(res.Message, "Return Deleted");
            await RefreshDirectoryAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void CloseDeleteModal()
    {
        IsDeleteModalOpen = false;
        DeletingRow = null;
        DeletePassword = string.Empty;
        DeleteErrorMessage = string.Empty;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusBannerMessage = message;
        IsStatusBannerError = isError;
        if (isError)
            _toast?.ShowError(message, "Purchase Return");
    }

    private void ClearStatus()
    {
        StatusBannerMessage = string.Empty;
        IsStatusBannerError = false;
    }
}
