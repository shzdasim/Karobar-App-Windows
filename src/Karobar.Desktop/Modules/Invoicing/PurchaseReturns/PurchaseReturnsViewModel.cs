using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;
using Karobar.Domain.Formulas;

namespace Karobar.Desktop.ViewModels;

public partial class PurchaseReturnLineEditorViewModel : ObservableObject
{
    private readonly Action<PurchaseReturnLineEditorViewModel, string?> _onChanged;
    private bool _isInternalSync;

    public PurchaseReturnLineEditorViewModel(Action<PurchaseReturnLineEditorViewModel, string?> onChanged)
    {
        _onChanged = onChanged;
    }

    [ObservableProperty]
    private int _rowNumber = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProduct))]
    [NotifyPropertyChangedFor(nameof(ProductDisplayText))]
    private long _productId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductDisplayText))]
    private string _productCode = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductDisplayText))]
    private string _productName = string.Empty;

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

    [ObservableProperty]
    private bool _hasRowError;

    [ObservableProperty]
    private string _rowErrorMessage = string.Empty;

    public ObservableCollection<PurchaseReturnBatchOptionDto> RowBatches { get; } = [];

    public bool HasProduct => ProductId > 0;

    public string ProductDisplayText => HasProduct
        ? (!string.IsNullOrWhiteSpace(ProductCode) ? $"{ProductName} ({ProductCode})" : ProductName)
        : "Click or press Enter to select product...";

    public string BatchDisplayText => !string.IsNullOrWhiteSpace(Batch)
        ? Batch
        : (IsUnbatched ? "[Unassigned Stock]" : "Select batch...");

    public string ExpiryFormatted => Expiry.HasValue ? Expiry.Value.ToString("MM/yyyy") : string.Empty;

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
        }
        finally
        {
            _isInternalSync = false;
        }

        _onChanged(this, changedField);
    }

    public void HydrateSilent(Action<PurchaseReturnLineEditorViewModel> mutator)
    {
        try
        {
            _isInternalSync = true;
            mutator(this);
        }
        finally
        {
            _isInternalSync = false;
        }
    }
}

public partial class PurchaseReturnsViewModel : ObservableObject
{
    private readonly IPurchaseReturnService _returnService;
    private readonly IAuthService _authService;
    private bool _isFooterSyncing;
    private bool _pendingPrintAfterSave;

    public event Action<string, int>? RequestFocusField;
    public event Action<PurchaseReturnDetailDto>? RequestPrintDocument;

    public PurchaseReturnsViewModel(
        IPurchaseReturnService returnService,
        IAuthService authService)
    {
        _returnService = returnService;
        _authService = authService;
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
    private bool _isBusy;

    [ObservableProperty]
    private string _statusBannerMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusBannerError;

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

    [ObservableProperty]
    private string _filterPostedNumber = string.Empty;

    [ObservableProperty]
    private string _filterSupplierSearch = string.Empty;

    [ObservableProperty]
    private string _filterSettlementMode = "all";

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _pageSize = 25;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
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

    public ObservableCollection<PurchaseReturnDirectoryRowDto> DirectoryRows { get; } = [];

    [ObservableProperty]
    private PurchaseReturnDirectoryRowDto? _selectedDirectoryRow;

    // =========================================================================
    // 3. FORM STATE (CREATE / EDIT)
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(FormModeBadgeText))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private long? _editingReturnId;

    public bool IsEditMode => EditingReturnId is > 0;
    public string FormModeBadgeText => IsEditMode ? "EDIT" : "CREATE";
    public string SaveButtonText => IsEditMode ? "Update Return (Alt+S)" : "Create Return (Alt+S)";

    [ObservableProperty]
    private string _formPostedNumber = string.Empty;

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
        : "Click to search supplier... (Alt+U)";

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
        : "Click to search invoice (optional)... (Alt+I)";
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
    [NotifyPropertyChangedFor(nameof(IsCashRefundSettlement))]
    [NotifyPropertyChangedFor(nameof(IsCreditNoteSettlement))]
    [NotifyPropertyChangedFor(nameof(IsInvoiceOffsetSettlement))]
    private string _formSettlementMode = "cash_refund";

    public bool IsCashRefundSettlement => FormSettlementMode == "cash_refund";
    public bool IsCreditNoteSettlement => FormSettlementMode == "credit_note";
    public bool IsInvoiceOffsetSettlement => FormSettlementMode == "invoice_offset";

    [ObservableProperty]
    private string _formPaymentMethod = "cash";

    [ObservableProperty]
    private long? _formAccountId;

    public ObservableCollection<PurchaseSupplierLookupDto> AllSuppliers { get; } = [];
    public ObservableCollection<PurchaseAccountLookupDto> PaymentAccounts { get; } = [];
    public ObservableCollection<PurchaseReturnInvoiceItemOptionDto> ActiveInvoiceItems { get; } = [];
    public ObservableCollection<PurchaseReturnLineEditorViewModel> Lines { get; } = [];

    [ObservableProperty]
    private int _activeRowIndex;

    // =========================================================================
    // 4. MODALS STATE
    // =========================================================================

    // 4.1 Supplier Search Modal
    [ObservableProperty]
    private bool _isSupplierSearchModalOpen;

    [ObservableProperty]
    private string _supplierSearchQuery = string.Empty;

    public ObservableCollection<PurchaseSupplierLookupDto> FilteredSuppliers { get; } = [];

    [ObservableProperty]
    private PurchaseSupplierLookupDto? _selectedSupplierInModal;

    // 4.2 Purchase Invoice Search Modal
    [ObservableProperty]
    private bool _isInvoiceSearchModalOpen;

    [ObservableProperty]
    private string _invoiceSearchQuery = string.Empty;

    public ObservableCollection<PurchaseReturnInvoiceLookupDto> InvoiceSearchResults { get; } = [];

    [ObservableProperty]
    private PurchaseReturnInvoiceLookupDto? _selectedInvoiceInModal;

    // 4.3 Product Search Modal (Draggable & Resizable)
    [ObservableProperty]
    private bool _isProductSearchModalOpen;

    [ObservableProperty]
    private int _productSearchTargetRowIndex;

    [ObservableProperty]
    private string _productSearchQuery = string.Empty;

    public ObservableCollection<PurchaseReturnProductSearchDto> ProductSearchResults { get; } = [];

    [ObservableProperty]
    private PurchaseReturnProductSearchDto? _selectedProductInModal;

    // 4.4 Batch Search Modal
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
        FilterPostedNumber = string.Empty;
        FilterSupplierSearch = string.Empty;
        FilterSettlementMode = "all";
        CurrentPage = 1;
        await RefreshDirectoryAsync();
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
        FormSettlementMode = "cash_refund";
        FormPaymentMethod = "cash";
        FormAccountId = null;

        ActiveInvoiceItems.Clear();
        Lines.Clear();
        Lines.Add(CreateBlankLine(1));
        ActiveRowIndex = 0;
        ClearStatus();

        ActiveSubView = "Form";

        // Auto-open Supplier Search Modal on new return load (matching PurchaseReturnForm.jsx line 330)
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

                lineVm.HydrateSilent(l =>
                {
                    l.ProductId = src.ProductId;
                    l.ProductCode = src.ProductCode;
                    l.ProductName = src.ProductName;
                    l.Batch = src.Batch ?? string.Empty;
                    l.IsUnbatched = string.IsNullOrWhiteSpace(src.Batch);
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

    // =========================================================================
    // 7. SUPPLIER & PURCHASE INVOICE SELECTION MODALS
    // =========================================================================

    [RelayCommand]
    public void OpenSupplierSearchModal()
    {
        SupplierSearchQuery = string.Empty;
        FilterSupplierModalList();
        IsSupplierSearchModalOpen = true;
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
                (s.Phone != null && s.Phone.ToLowerInvariant().Contains(q))).ToList();

        FilteredSuppliers.Clear();
        foreach (var s in matches)
            FilteredSuppliers.Add(s);

        SelectedSupplierInModal = FilteredSuppliers.FirstOrDefault();
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
            // Clear linked invoice and reset lines when switching supplier
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

        RequestFocusField?.Invoke("product", 0);
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

        SelectedInvoiceInModal = InvoiceSearchResults.FirstOrDefault();
    }

    [RelayCommand]
    public async Task SelectInvoiceFromModalAsync(PurchaseReturnInvoiceLookupDto? invoice)
    {
        var chosen = invoice ?? SelectedInvoiceInModal;
        if (chosen == null) return;

        IsInvoiceSearchModalOpen = false;

        // Also auto-set supplier if not yet selected or different
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

        // Auto-select settlement mode from linked invoice type (matching PurchaseReturnForm.jsx line 798)
        FormSettlementMode = chosen.IsCredit ? "credit_note" : "cash_refund";

        var returnableItems = await _returnService.GetInvoiceReturnableItemsAsync(chosen.Id, EditingReturnId);
        ActiveInvoiceItems.Clear();
        foreach (var item in returnableItems)
            ActiveInvoiceItems.Add(item);

        // Reset line items for the newly linked invoice
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
    // 8. PRODUCT & BATCH SELECTION MODALS + REPEATER ROWS
    // =========================================================================

    private PurchaseReturnLineEditorViewModel CreateBlankLine(int rowNumber)
    {
        return new PurchaseReturnLineEditorViewModel(OnLineChanged)
        {
            RowNumber = rowNumber,
            PackSize = 1
        };
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
                l.Batch = string.Empty;
                l.IsUnbatched = false;
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
        await RefreshProductSearchResultsAsync();
    }

    partial void OnProductSearchQueryChanged(string value)
    {
        if (!IsProductSearchModalOpen) return;
        _ = RefreshProductSearchResultsAsync();
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

        // Multiple batches available -> set product & open Batch Search Modal for this row
        row.HydrateSilent(l =>
        {
            l.ProductId = chosen.Id;
            l.ProductCode = chosen.ProductCode;
            l.ProductName = chosen.Name;
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
        await OpenBatchSearchForRowAsync(rowIndex);
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
        row.HydrateSilent(l =>
        {
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
        RequestFocusField?.Invoke("pack_quantity", rowIndex);
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
        RecalculateFooter(null);
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

        // Check pre-shift cash drawer impact when editing an existing return
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

            SetStatus(res.Message, isError: false);

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
    public async Task OpenHistoryModalAsync()
    {
        if (ViewingDetail == null) return;
        var logs = await _returnService.GetDocumentHistoryAsync(ViewingDetail.Id);
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
            SetStatus(res.Message, isError: false);
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
    }

    private void ClearStatus()
    {
        StatusBannerMessage = string.Empty;
        IsStatusBannerError = false;
    }
}
