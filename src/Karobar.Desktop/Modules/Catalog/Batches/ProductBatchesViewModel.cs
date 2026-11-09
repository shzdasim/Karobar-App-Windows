using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;

namespace Karobar.Desktop.ViewModels;

public partial class ProductBatchesViewModel : ObservableObject
{
    private readonly IProductService _productService;
    private readonly IToastNotificationService _toast;
    private readonly Action<string>? _navigate;

    private bool _isInitialized;
    private bool _suppressGlobalFilterReload;
    private bool _suppressProductSearchDebounce;
    private CancellationTokenSource? _productSearchCts;
    private CancellationTokenSource? _globalSearchDebounceCts;
    private CancellationTokenSource? _globalDirectoryLoadCts;

    // UI Focus & Scroll Callbacks wired by ProductBatchesView.xaml.cs
    public Action? RequestFocusProductSearch { get; set; }
    public Action? RequestFocusBatchNumber { get; set; }
    public Action? RequestFocusQuantity { get; set; }
    public Action? RequestFocusRack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveBatch))]
    private bool _isBusy;

    [ObservableProperty]
    private string _currencySymbol = "Rs.";

    // =========================================================================
    // STEP 1: PRODUCT SEARCH COMBOBOX STATE
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProductSearchQuery))]
    private string _productSearchQuery = string.Empty;

    public bool HasProductSearchQuery => !string.IsNullOrEmpty(ProductSearchQuery);

    [ObservableProperty]
    private bool _isProductDropdownOpen;

    [ObservableProperty]
    private bool _isSearchingProducts;

    public ObservableCollection<BatchWorkbenchProductSearchItemDto> ProductSearchResults { get; } = [];

    [ObservableProperty]
    private BatchWorkbenchProductSearchItemDto? _highlightedProductResult;

    public bool HasProductSearchResults => ProductSearchResults.Count > 0;
    public bool ShowNoProductSearchResults => !IsSearchingProducts && ProductSearchResults.Count == 0;

    // =========================================================================
    // ACTIVE PRODUCT SUMMARY & FEFO BATCHES STATE
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveProduct))]
    [NotifyPropertyChangedFor(nameof(HasNoActiveProduct))]
    [NotifyPropertyChangedFor(nameof(ActiveProductPackSize))]
    [NotifyPropertyChangedFor(nameof(HasMultiUnitPack))]
    [NotifyPropertyChangedFor(nameof(PlusOnePackButtonLabel))]
    [NotifyPropertyChangedFor(nameof(ActiveProductRackDisplay))]
    [NotifyPropertyChangedFor(nameof(ActiveProductRightHeaderTitle))]
    [NotifyPropertyChangedFor(nameof(IsNewBatchBlockedByZeroUnassigned))]
    [NotifyPropertyChangedFor(nameof(CanSaveBatch))]
    private ProductDetailDto? _activeProduct;

    public bool HasActiveProduct => ActiveProduct != null;
    public bool HasNoActiveProduct => ActiveProduct == null;
    public int ActiveProductPackSize => Math.Max(1, ActiveProduct?.PackSize ?? 1);
    public bool HasMultiUnitPack => ActiveProductPackSize > 1;
    public string PlusOnePackButtonLabel => $"+1P ({ActiveProductPackSize})";
    public string ActiveProductRackDisplay => string.IsNullOrWhiteSpace(ActiveProduct?.Rack) ? "—" : ActiveProduct!.Rack!;
    public string ActiveProductRightHeaderTitle => ActiveProduct != null
        ? $"Batches for {ActiveProduct.Name}"
        : "Selected Product Batches";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxAssignableQty))]
    [NotifyPropertyChangedFor(nameof(HasAssignableStock))]
    [NotifyPropertyChangedFor(nameof(MaxAssignableBadgeText))]
    [NotifyPropertyChangedFor(nameof(QuantityFieldLabel))]
    [NotifyPropertyChangedFor(nameof(MaxQuantityButtonLabel))]
    [NotifyPropertyChangedFor(nameof(IsNewBatchBlockedByZeroUnassigned))]
    [NotifyPropertyChangedFor(nameof(CanSaveBatch))]
    private int _totalStock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxAssignableQty))]
    [NotifyPropertyChangedFor(nameof(HasAssignableStock))]
    [NotifyPropertyChangedFor(nameof(MaxAssignableBadgeText))]
    [NotifyPropertyChangedFor(nameof(QuantityFieldLabel))]
    [NotifyPropertyChangedFor(nameof(MaxQuantityButtonLabel))]
    [NotifyPropertyChangedFor(nameof(IsNewBatchBlockedByZeroUnassigned))]
    [NotifyPropertyChangedFor(nameof(CanSaveBatch))]
    private int _batchedStock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnassignedStock))]
    private int _unassignedStock;

    public bool HasUnassignedStock => UnassignedStock > 0;

    public ObservableCollection<ProductBatchItemDto> ActiveProductBatches { get; } = [];
    public ObservableCollection<LocationStockPillDto> ActiveProductLocationPills { get; } = [];

    public bool HasActiveProductBatches => ActiveProductBatches.Count > 0;
    public bool ShowEmptyActiveProductBatches => HasActiveProduct && ActiveProductBatches.Count == 0;
    public string ActiveProductBatchesCountText => $"{ActiveProductBatches.Count} Batches";

    // =========================================================================
    // STEP 2: ADD / EDIT BATCH FORM STATE
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingBatch))]
    [NotifyPropertyChangedFor(nameof(Step2HeaderTitle))]
    [NotifyPropertyChangedFor(nameof(SaveBatchButtonText))]
    [NotifyPropertyChangedFor(nameof(ResetBatchButtonText))]
    [NotifyPropertyChangedFor(nameof(MaxAssignableQty))]
    [NotifyPropertyChangedFor(nameof(HasAssignableStock))]
    [NotifyPropertyChangedFor(nameof(MaxAssignableBadgeText))]
    [NotifyPropertyChangedFor(nameof(QuantityFieldLabel))]
    [NotifyPropertyChangedFor(nameof(MaxQuantityButtonLabel))]
    [NotifyPropertyChangedFor(nameof(IsNewBatchBlockedByZeroUnassigned))]
    [NotifyPropertyChangedFor(nameof(CanSaveBatch))]
    private long? _editingBatchId;

    public bool IsEditingBatch => EditingBatchId.HasValue && EditingBatchId.Value > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step2HeaderTitle))]
    private string _formBatchNumber = string.Empty;

    [ObservableProperty]
    private string _formExpiryDate = DateTime.Today.AddMonths(12).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string _formQuantity = string.Empty;

    [ObservableProperty]
    private string _formUnitPurchasePrice = string.Empty;

    [ObservableProperty]
    private string _formUnitSalePrice = string.Empty;

    [ObservableProperty]
    private List<StorageLocationDto> _activeStorageLocations = [];

    [ObservableProperty]
    private StorageLocationDto? _formSelectedLocation;

    [ObservableProperty]
    private string _formRack = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatchingExistingBatch))]
    [NotifyPropertyChangedFor(nameof(MatchingExistingBatchBadgeText))]
    [NotifyPropertyChangedFor(nameof(MaxAssignableQty))]
    [NotifyPropertyChangedFor(nameof(HasAssignableStock))]
    [NotifyPropertyChangedFor(nameof(MaxAssignableBadgeText))]
    [NotifyPropertyChangedFor(nameof(QuantityFieldLabel))]
    [NotifyPropertyChangedFor(nameof(MaxQuantityButtonLabel))]
    [NotifyPropertyChangedFor(nameof(IsNewBatchBlockedByZeroUnassigned))]
    [NotifyPropertyChangedFor(nameof(CanSaveBatch))]
    private ProductBatchItemDto? _matchingExistingBatch;

    public bool HasMatchingExistingBatch => MatchingExistingBatch != null;
    public string MatchingExistingBatchBadgeText => MatchingExistingBatch != null
        ? $"Matches existing batch ({MatchingExistingBatch.Quantity} units) — Click to Edit"
        : string.Empty;

    public int MaxAssignableQty
    {
        get
        {
            if (!HasActiveProduct)
                return 0;

            int editingExistingQty = 0;
            if (IsEditingBatch)
            {
                editingExistingQty = ActiveProductBatches
                    .FirstOrDefault(b => b.Id == EditingBatchId!.Value)?.Quantity ?? 0;
            }
            else if (MatchingExistingBatch != null)
            {
                editingExistingQty = MatchingExistingBatch.Quantity;
            }

            int otherBatched = Math.Max(0, BatchedStock - editingExistingQty);
            return Math.Max(0, TotalStock - otherBatched);
        }
    }

    public bool HasAssignableStock => MaxAssignableQty > 0;
    public string MaxAssignableBadgeText => $"Max Assignable: {MaxAssignableQty} units";
    public string QuantityFieldLabel => $"Quantity (Max: {MaxAssignableQty}) *";
    public string MaxQuantityButtonLabel => $"Max ({MaxAssignableQty})";

    public bool IsNewBatchBlockedByZeroUnassigned =>
        HasActiveProduct && !IsEditingBatch && !HasMatchingExistingBatch && MaxAssignableQty <= 0;

    public bool CanSaveBatch => HasActiveProduct && !IsBusy && !IsNewBatchBlockedByZeroUnassigned;

    public string Step2HeaderTitle => IsEditingBatch
        ? $"EDIT BATCH #{FormBatchNumber}"
        : "ADD BATCH";

    public string SaveBatchButtonText => IsEditingBatch
        ? "✎ Update Batch (Ctrl+S)"
        : "+ Save Batch (Ctrl+S)";

    public string ResetBatchButtonText => IsEditingBatch
        ? "Cancel Edit (Esc)"
        : "Clear (Ctrl+N)";

    // =========================================================================
    // GLOBAL ALL-PRODUCTS BATCH DIRECTORY STATE (BOTTOM SECTION)
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusAllSelected))]
    [NotifyPropertyChangedFor(nameof(IsStatusExpiredSelected))]
    [NotifyPropertyChangedFor(nameof(IsStatusCriticalSelected))]
    [NotifyPropertyChangedFor(nameof(IsStatusWarningSelected))]
    [NotifyPropertyChangedFor(nameof(IsStatusSafeSelected))]
    private string _globalFilterStatus = "all";

    public bool IsStatusAllSelected => GlobalFilterStatus == "all";
    public bool IsStatusExpiredSelected => GlobalFilterStatus == "expired";
    public bool IsStatusCriticalSelected => GlobalFilterStatus == "critical";
    public bool IsStatusWarningSelected => GlobalFilterStatus == "warning";
    public bool IsStatusSafeSelected => GlobalFilterStatus == "safe";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGlobalSearchText))]
    private string _globalSearchText = string.Empty;

    public bool HasGlobalSearchText => !string.IsNullOrEmpty(GlobalSearchText);

    [ObservableProperty]
    private List<StorageLocationDto> _globalLocationOptions = [];

    [ObservableProperty]
    private StorageLocationDto? _selectedGlobalLocation;

    [ObservableProperty]
    private bool _globalIncludeZeroStock = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusAllTabLabel))]
    [NotifyPropertyChangedFor(nameof(StatusExpiredTabLabel))]
    [NotifyPropertyChangedFor(nameof(StatusCriticalTabLabel))]
    [NotifyPropertyChangedFor(nameof(StatusWarningTabLabel))]
    [NotifyPropertyChangedFor(nameof(StatusSafeTabLabel))]
    [NotifyPropertyChangedFor(nameof(GlobalBatchesCountBadgeText))]
    private GlobalBatchSummaryDto _globalSummary = new();

    public string StatusAllTabLabel => $"All ({GlobalSummary.TotalBatchesTracked})";
    public string StatusExpiredTabLabel => $"● Expired ({GlobalSummary.TotalExpiredBatches})";
    public string StatusCriticalTabLabel => $"● Critical ≤30d ({GlobalSummary.TotalCriticalBatches})";
    public string StatusWarningTabLabel => $"● Warning ({GlobalSummary.TotalWarningBatches})";
    public string StatusSafeTabLabel => $"● Safe ({GlobalSummary.TotalSafeBatches})";
    public string GlobalBatchesCountBadgeText => $"{GlobalTotalCount} Batches";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGlobalBatches))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGlobalBatches))]
    private List<GlobalBatchDirectoryRowDto> _globalBatchRows = [];

    public bool HasGlobalBatches => GlobalBatchRows.Count > 0;
    public bool ShowEmptyGlobalBatches => GlobalBatchRows.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GlobalBatchesCountBadgeText))]
    private int _globalTotalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoGlobalPrevPage))]
    [NotifyPropertyChangedFor(nameof(CanGoGlobalNextPage))]
    private int _globalCurrentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoGlobalPrevPage))]
    [NotifyPropertyChangedFor(nameof(CanGoGlobalNextPage))]
    private int _globalLastPage = 1;

    [ObservableProperty]
    private int _globalFromItem;

    [ObservableProperty]
    private int _globalToItem;

    public bool CanGoGlobalPrevPage => GlobalCurrentPage > 1;
    public bool CanGoGlobalNextPage => GlobalCurrentPage < GlobalLastPage;

    // =========================================================================
    // MODAL 1: MOVE / SPLIT BATCH LOCATION MODAL
    // =========================================================================

    [ObservableProperty]
    private bool _isMoveBatchModalOpen;

    [ObservableProperty]
    private ProductBatchItemDto? _movingSourceBatch;

    [ObservableProperty]
    private StorageLocationDto? _moveTargetLocation;

    [ObservableProperty]
    private string _moveQuantityText = "1";

    [ObservableProperty]
    private string _moveTargetRack = string.Empty;

    public int MoveSourceBatchMaxQty => MovingSourceBatch?.Quantity ?? 0;
    public string MoveAllButtonLabel => $"All ({MoveSourceBatchMaxQty})";

    // =========================================================================
    // MODAL 2: MANAGE STORAGE LOCATIONS MODAL
    // =========================================================================

    [ObservableProperty]
    private bool _isLocationsModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingStorageLocation))]
    [NotifyPropertyChangedFor(nameof(StorageLocationFormTitle))]
    [NotifyPropertyChangedFor(nameof(SaveStorageLocationButtonLabel))]
    private long? _editingLocationId;

    public bool IsEditingStorageLocation => EditingLocationId.HasValue && EditingLocationId.Value > 0;
    public string StorageLocationFormTitle => IsEditingStorageLocation ? "Edit Storage Location" : "Add Storage Location";
    public string SaveStorageLocationButtonLabel => IsEditingStorageLocation ? "Update Location" : "+ Add Location";

    [ObservableProperty]
    private string _locFormName = string.Empty;

    [ObservableProperty]
    private string _locFormCode = string.Empty;

    public ObservableCollection<SimpleStringOption> LocationTypeOptions { get; } =
    [
        new() { Value = "shop", Label = "Shop Floor / Counter" },
        new() { Value = "godown", Label = "Godown / Warehouse" },
        new() { Value = "cold_storage", Label = "Cold Chain / Fridge" },
        new() { Value = "quarantine", Label = "Quarantine / Expired Hold" },
        new() { Value = "other", Label = "Other Area" }
    ];

    [ObservableProperty]
    private SimpleStringOption? _selectedLocTypeOption;

    [ObservableProperty]
    private string _locFormDefaultRack = string.Empty;

    [ObservableProperty]
    private string _locFormNotes = string.Empty;

    [ObservableProperty]
    private bool _locFormIsDefault;

    [ObservableProperty]
    private bool _locFormIsActive = true;

    public ObservableCollection<StorageLocationDto> ManagedLocationsList { get; } = [];

    // =========================================================================
    // MODAL 3: DELETE BATCH CONFIRMATION MODAL
    // =========================================================================

    [ObservableProperty]
    private bool _isDeleteBatchModalOpen;

    [ObservableProperty]
    private ProductBatchItemDto? _deletingBatch;

    public string DeleteBatchConfirmMessage => DeletingBatch != null
        ? $"Remove batch '{DeletingBatch.BatchNumber}' ({DeletingBatch.Quantity} units at {DeletingBatch.StorageLocationName})? Its units will be returned to the unassigned stock pool."
        : string.Empty;

    public ProductBatchesViewModel(
        IProductService productService,
        IToastNotificationService toast,
        Action<string>? navigate = null)
    {
        _productService = productService;
        _toast = toast;
        _navigate = navigate;
        _selectedLocTypeOption = LocationTypeOptions[0];
    }

    // =========================================================================
    // INITIALIZATION & NAVIGATION ENTRY POINTS
    // =========================================================================

    public async Task OpenWorkbenchAsync(long? preselectedProductId = null, long? preselectedBatchId = null)
    {
        if (!_isInitialized)
        {
            await LoadStorageLocationsAsync();
            _isInitialized = true;
        }

        await LoadGlobalBatchesPageAsync(resetPage: false);

        if (preselectedProductId.HasValue && preselectedProductId.Value > 0)
        {
            await LoadActiveProductByIdAsync(preselectedProductId.Value, preselectedBatchId, focusBatchInput: true);
        }
        else if (ActiveProduct != null)
        {
            await RefreshActiveProductBatchesAsync(ActiveProduct.Id, preselectedBatchId);
        }
        else
        {
            // Preload initial product search suggestions for instant combobox opening
            await RunProductSearchAsync(ProductSearchQuery, openDropdown: false);
        }
    }

    [RelayCommand]
    public async Task RefreshWorkbenchAsync()
    {
        await LoadStorageLocationsAsync();
        if (ActiveProduct != null)
        {
            var detail = await _productService.GetProductByIdAsync(ActiveProduct.Id);
            if (detail != null)
            {
                ActiveProduct = detail;
            }
            await RefreshActiveProductBatchesAsync(ActiveProduct.Id, EditingBatchId);
        }
        await LoadGlobalBatchesPageAsync(resetPage: false);
        _toast.ShowInfo("Product Batches workbench refreshed.");
    }

    private async Task LoadStorageLocationsAsync()
    {
        _suppressGlobalFilterReload = true;
        try
        {
            var lookups = await _productService.GetProductFormLookupsAsync();
            CurrencySymbol = lookups.CurrencySymbol;

            var allLocations = await _productService.GetStorageLocationsAsync(activeOnly: false);
            var activeLocs = allLocations.Where(l => l.IsActive).ToList();
            if (activeLocs.Count == 0)
            {
                activeLocs = allLocations;
            }

            ActiveStorageLocations = activeLocs;

            ManagedLocationsList.Clear();
            foreach (var loc in allLocations)
            {
                ManagedLocationsList.Add(loc);
            }

            var prevGlobalLocId = SelectedGlobalLocation?.Id ?? 0;
            var globalLocs = new List<StorageLocationDto>(allLocations.Count + 1)
            {
                new() { Id = 0, Name = "All Storage Locations", Code = "ALL" }
            };
            globalLocs.AddRange(allLocations);
            GlobalLocationOptions = globalLocs;
            SelectedGlobalLocation = globalLocs.FirstOrDefault(l => l.Id == prevGlobalLocId) ?? globalLocs[0];

            var prevFormLocId = FormSelectedLocation?.Id;
            FormSelectedLocation = activeLocs.FirstOrDefault(l => l.Id == prevFormLocId)
                                   ?? activeLocs.FirstOrDefault(l => l.IsDefault)
                                   ?? activeLocs.FirstOrDefault();
        }
        finally
        {
            _suppressGlobalFilterReload = false;
        }
    }

    // =========================================================================
    // STEP 1: PRODUCT SEARCH COMBOBOX LOGIC
    // =========================================================================

    partial void OnProductSearchQueryChanged(string value)
    {
        if (_suppressProductSearchDebounce)
            return;

        _ = TriggerDebouncedProductSearchAsync(value);
    }

    private async Task TriggerDebouncedProductSearchAsync(string query)
    {
        _productSearchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _productSearchCts = cts;

        try
        {
            IsSearchingProducts = true;
            OnPropertyChanged(nameof(ShowNoProductSearchResults));
            await Task.Delay(120, cts.Token);
            if (cts.Token.IsCancellationRequested)
                return;

            await RunProductSearchAsync(query, openDropdown: true, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer keystroke
        }
        finally
        {
            if (_productSearchCts == cts)
            {
                IsSearchingProducts = false;
                OnPropertyChanged(nameof(ShowNoProductSearchResults));
            }
        }
    }

    public async Task RunProductSearchAsync(string? query, bool openDropdown, CancellationToken cancellationToken = default)
    {
        var items = await _productService.SearchProductsForBatchWorkbenchAsync(query, 25, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return;

        ProductSearchResults.Clear();
        foreach (var item in items)
        {
            ProductSearchResults.Add(item);
        }

        HighlightedProductResult = ProductSearchResults.FirstOrDefault();
        OnPropertyChanged(nameof(HasProductSearchResults));
        OnPropertyChanged(nameof(ShowNoProductSearchResults));

        if (openDropdown)
        {
            IsProductDropdownOpen = true;
        }
    }

    public void MoveProductHighlight(int delta)
    {
        if (ProductSearchResults.Count == 0)
            return;

        IsProductDropdownOpen = true;
        var currentIndex = HighlightedProductResult != null
            ? ProductSearchResults.IndexOf(HighlightedProductResult)
            : -1;

        var nextIndex = Math.Clamp(currentIndex + delta, 0, ProductSearchResults.Count - 1);
        HighlightedProductResult = ProductSearchResults[nextIndex];
    }

    public async Task CommitHighlightedProductAsync()
    {
        var target = HighlightedProductResult ?? ProductSearchResults.FirstOrDefault();
        if (target != null)
        {
            await SelectProductFromSearchAsync(target);
        }
    }

    [RelayCommand]
    public async Task SelectProductFromSearchAsync(BatchWorkbenchProductSearchItemDto? item)
    {
        if (item == null)
            return;

        IsProductDropdownOpen = false;
        await LoadActiveProductByIdAsync(item.Id, preselectBatchId: null, focusBatchInput: true);
    }

    [RelayCommand]
    public void ClearProductSearch()
    {
        _suppressProductSearchDebounce = true;
        ProductSearchQuery = string.Empty;
        _suppressProductSearchDebounce = false;
        _ = RunProductSearchAsync(string.Empty, openDropdown: true);
        RequestFocusProductSearch?.Invoke();
    }

    [RelayCommand]
    public void OpenActiveProductInEdit()
    {
        if (ActiveProduct == null)
            return;

        _navigate?.Invoke($"/products/edit/{ActiveProduct.Id}");
    }

    public async Task LoadActiveProductByIdAsync(long productId, long? preselectBatchId = null, bool focusBatchInput = true)
    {
        var detail = await _productService.GetProductByIdAsync(productId);
        if (detail == null)
        {
            _toast.ShowError("Selected product could not be found.");
            return;
        }

        ActiveProduct = detail;
        _suppressProductSearchDebounce = true;
        ProductSearchQuery = $"{detail.Name} ({detail.ProductCode})";
        _suppressProductSearchDebounce = false;
        IsProductDropdownOpen = false;

        await RefreshActiveProductBatchesAsync(detail.Id, preselectBatchId);

        if (!preselectBatchId.HasValue)
        {
            ResetBatchFormInternal(detail, notifyFocus: false);
        }

        if (focusBatchInput)
        {
            RequestFocusBatchNumber?.Invoke();
        }
    }

    private async Task RefreshActiveProductBatchesAsync(long productId, long? batchIdToEdit = null)
    {
        var summary = await _productService.GetProductBatchesAsync(productId);
        TotalStock = summary.TotalStock;
        BatchedStock = summary.BatchedStock;
        UnassignedStock = summary.UnassignedStock;

        ActiveProductBatches.Clear();
        foreach (var b in summary.Batches)
        {
            ActiveProductBatches.Add(b);
        }

        ActiveProductLocationPills.Clear();
        foreach (var pill in summary.LocationBreakdown)
        {
            ActiveProductLocationPills.Add(pill);
        }

        OnPropertyChanged(nameof(HasActiveProductBatches));
        OnPropertyChanged(nameof(ShowEmptyActiveProductBatches));
        OnPropertyChanged(nameof(ActiveProductBatchesCountText));

        if (batchIdToEdit.HasValue && batchIdToEdit.Value > 0)
        {
            var found = ActiveProductBatches.FirstOrDefault(b => b.Id == batchIdToEdit.Value);
            if (found != null)
            {
                PopulateFormFromBatch(found);
                return;
            }
        }

        UpdateMatchingExistingBatch();
    }

    // =========================================================================
    // STEP 2: ADD / EDIT BATCH FORM LOGIC
    // =========================================================================

    partial void OnFormBatchNumberChanged(string value)
    {
        UpdateMatchingExistingBatch();
    }

    partial void OnFormSelectedLocationChanged(StorageLocationDto? value)
    {
        if (value != null && string.IsNullOrWhiteSpace(FormRack) && !string.IsNullOrWhiteSpace(value.DefaultRack))
        {
            FormRack = value.DefaultRack;
        }
        UpdateMatchingExistingBatch();
    }

    partial void OnFormQuantityChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        // Smart "2p" or "1.5p" Pack-to-Unit Conversion matching ProductBatchWorkbench.jsx
        var packMatch = Regex.Match(value.Trim(), @"^(\d+(?:\.\d+)?)\s*[pP]$");
        if (packMatch.Success && double.TryParse(packMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var packs))
        {
            var packSize = ActiveProductPackSize;
            var units = (int)Math.Round(packs * packSize);
            var maxQty = MaxAssignableQty;

            if (maxQty > 0 && units > maxQty)
            {
                FormQuantity = maxQty.ToString(CultureInfo.InvariantCulture);
                _toast.ShowWarning($"Capped to maximum available unassigned stock ({maxQty} units).");
            }
            else
            {
                FormQuantity = units.ToString(CultureInfo.InvariantCulture);
                if (packSize > 1)
                {
                    _toast.ShowSuccess($"{packs:0.##} pack(s) × {packSize} = {units} units");
                }
            }
        }
    }

    private void UpdateMatchingExistingBatch()
    {
        if (IsEditingBatch || ActiveProduct == null || string.IsNullOrWhiteSpace(FormBatchNumber))
        {
            MatchingExistingBatch = null;
            return;
        }

        var normalizedNo = FormBatchNumber.Trim();
        var defaultLocId = ActiveStorageLocations.FirstOrDefault(l => l.IsDefault)?.Id
                           ?? ActiveStorageLocations.FirstOrDefault()?.Id;
        var selectedLocId = FormSelectedLocation?.Id ?? ActiveProduct.StorageLocationId ?? defaultLocId;

        MatchingExistingBatch = ActiveProductBatches.FirstOrDefault(b =>
            string.Equals(b.BatchNumber.Trim(), normalizedNo, StringComparison.OrdinalIgnoreCase) &&
            (b.StorageLocationId ?? defaultLocId) == selectedLocId);
    }

    private void ResetBatchFormInternal(ProductDetailDto? product, bool notifyFocus)
    {
        var p = product ?? ActiveProduct;
        var defaultLoc = ActiveStorageLocations.FirstOrDefault(l => l.Id == p?.StorageLocationId)
                         ?? ActiveStorageLocations.FirstOrDefault(l => l.IsDefault)
                         ?? ActiveStorageLocations.FirstOrDefault();

        EditingBatchId = null;
        FormBatchNumber = string.Empty;
        FormExpiryDate = DateTime.Today.AddMonths(12).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        FormSelectedLocation = defaultLoc;
        FormRack = !string.IsNullOrWhiteSpace(p?.Rack)
            ? p!.Rack!
            : (defaultLoc?.DefaultRack ?? string.Empty);
        FormUnitPurchasePrice = p != null
            ? p.UnitPurchasePrice.ToString("0.00", CultureInfo.InvariantCulture)
            : string.Empty;
        FormUnitSalePrice = p != null
            ? p.UnitSalePrice.ToString("0.00", CultureInfo.InvariantCulture)
            : string.Empty;
        FormQuantity = UnassignedStock > 0
            ? UnassignedStock.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        UpdateMatchingExistingBatch();

        if (notifyFocus)
        {
            RequestFocusBatchNumber?.Invoke();
        }
    }

    private void PopulateFormFromBatch(ProductBatchItemDto batch)
    {
        EditingBatchId = batch.Id;
        FormBatchNumber = batch.BatchNumber;
        FormExpiryDate = (batch.ExpiryDate ?? DateTime.Today.AddMonths(12)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        FormQuantity = batch.Quantity.ToString(CultureInfo.InvariantCulture);

        var targetLoc = ActiveStorageLocations.FirstOrDefault(l => l.Id == batch.StorageLocationId)
                        ?? ActiveStorageLocations.FirstOrDefault(l => l.IsDefault)
                        ?? ActiveStorageLocations.FirstOrDefault();
        FormSelectedLocation = targetLoc;
        FormRack = batch.Rack ?? targetLoc?.DefaultRack ?? ActiveProduct?.Rack ?? string.Empty;

        var cost = batch.UnitPurchasePrice ?? ActiveProduct?.UnitPurchasePrice ?? 0m;
        var mrp = batch.UnitSalePrice ?? ActiveProduct?.UnitSalePrice ?? 0m;
        FormUnitPurchasePrice = cost.ToString("0.00", CultureInfo.InvariantCulture);
        FormUnitSalePrice = mrp.ToString("0.00", CultureInfo.InvariantCulture);

        MatchingExistingBatch = null;
    }

    [RelayCommand]
    public void ResetBatchForm()
    {
        ResetBatchFormInternal(ActiveProduct, notifyFocus: true);
    }

    [RelayCommand]
    public void StartEditBatch(ProductBatchItemDto? batch)
    {
        if (batch == null)
            return;

        PopulateFormFromBatch(batch);
        RequestFocusBatchNumber?.Invoke();
    }

    [RelayCommand]
    public void LoadMatchingExistingBatch()
    {
        if (MatchingExistingBatch == null)
            return;

        StartEditBatch(MatchingExistingBatch);
    }

    [RelayCommand]
    public void ApplyQuickExpiryMonths(string? monthsStr)
    {
        if (!int.TryParse(monthsStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var months) || months <= 0)
            months = 12;

        FormExpiryDate = DateTime.Today.AddMonths(months).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        RequestFocusQuantity?.Invoke();
    }

    [RelayCommand]
    public void AddOnePackQuantity()
    {
        var current = int.TryParse(FormQuantity, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) ? q : 0;
        var next = current + ActiveProductPackSize;
        if (MaxAssignableQty > 0 && next > MaxAssignableQty)
        {
            next = MaxAssignableQty;
            _toast.ShowWarning($"Capped to maximum assignable stock ({MaxAssignableQty} units).");
        }
        FormQuantity = next.ToString(CultureInfo.InvariantCulture);
        RequestFocusRack?.Invoke();
    }

    [RelayCommand]
    public void FillMaxQuantity()
    {
        FormQuantity = MaxAssignableQty.ToString(CultureInfo.InvariantCulture);
        RequestFocusRack?.Invoke();
    }

    [RelayCommand]
    public async Task SaveBatchAsync()
    {
        if (ActiveProduct == null)
        {
            _toast.ShowError("Please select a product first (Ctrl+F).");
            RequestFocusProductSearch?.Invoke();
            return;
        }

        if (string.IsNullOrWhiteSpace(FormBatchNumber))
        {
            _toast.ShowError("Please enter a Batch Number.");
            RequestFocusBatchNumber?.Invoke();
            return;
        }

        if (!DateTime.TryParse(FormExpiryDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiryDate))
        {
            _toast.ShowError("Please enter a valid Expiry Date (YYYY-MM-DD).");
            return;
        }

        if (!int.TryParse(FormQuantity, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) || qty < 0)
        {
            _toast.ShowError("Please enter a valid batch quantity.");
            RequestFocusQuantity?.Invoke();
            return;
        }

        if (!IsEditingBatch && MatchingExistingBatch == null)
        {
            if (MaxAssignableQty <= 0)
            {
                _toast.ShowError("Cannot add a new batch because this product has 0 unassigned stock available.");
                return;
            }
            if (qty <= 0)
            {
                _toast.ShowError($"Batch quantity must be between 1 and {MaxAssignableQty} unassigned units.");
                RequestFocusQuantity?.Invoke();
                return;
            }
        }

        if (qty > MaxAssignableQty)
        {
            _toast.ShowError($"Cannot assign {qty} units. Maximum assignable quantity for this batch is {MaxAssignableQty} units.");
            RequestFocusQuantity?.Invoke();
            return;
        }

        decimal? unitCost = decimal.TryParse(FormUnitPurchasePrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var uc) && uc >= 0m
            ? uc
            : null;
        decimal? unitMrp = decimal.TryParse(FormUnitSalePrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var us) && us >= 0m
            ? us
            : null;

        IsBusy = true;
        try
        {
            var req = new BatchSaveRequest
            {
                BatchId = EditingBatchId,
                BatchNumber = FormBatchNumber.Trim(),
                ExpiryDate = expiryDate,
                Quantity = qty,
                StorageLocationId = FormSelectedLocation?.Id,
                Rack = string.IsNullOrWhiteSpace(FormRack) ? null : FormRack.Trim(),
                UnitPurchasePrice = unitCost,
                UnitSalePrice = unitMrp
            };

            var res = await _productService.SaveProductBatchAsync(ActiveProduct.Id, req);
            if (!res.Succeeded)
            {
                _toast.ShowError(res.Message);
                return;
            }

            _toast.ShowSuccess(res.Message);
            await RefreshActiveProductBatchesAsync(ActiveProduct.Id, batchIdToEdit: null);
            ResetBatchFormInternal(ActiveProduct, notifyFocus: true);
            await LoadGlobalBatchesPageAsync(resetPage: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // MODAL 1: MOVE / SPLIT BATCH LOCATION LOGIC
    // =========================================================================

    [RelayCommand]
    public void OpenMoveBatchModal(ProductBatchItemDto? batch)
    {
        if (batch == null)
            return;

        MovingSourceBatch = batch;
        var otherLoc = ActiveStorageLocations.FirstOrDefault(l => l.Id != batch.StorageLocationId)
                       ?? ActiveStorageLocations.FirstOrDefault();
        MoveTargetLocation = otherLoc;
        MoveQuantityText = Math.Max(1, batch.Quantity).ToString(CultureInfo.InvariantCulture);
        MoveTargetRack = otherLoc?.DefaultRack ?? batch.Rack ?? string.Empty;

        OnPropertyChanged(nameof(MoveSourceBatchMaxQty));
        OnPropertyChanged(nameof(MoveAllButtonLabel));
        IsMoveBatchModalOpen = true;
    }

    partial void OnMoveTargetLocationChanged(StorageLocationDto? value)
    {
        if (value != null && !string.IsNullOrWhiteSpace(value.DefaultRack))
        {
            MoveTargetRack = value.DefaultRack;
        }
    }

    [RelayCommand]
    public void FillMoveAllQuantity()
    {
        MoveQuantityText = MoveSourceBatchMaxQty.ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    public void CloseMoveBatchModal()
    {
        IsMoveBatchModalOpen = false;
        MovingSourceBatch = null;
    }

    [RelayCommand]
    public async Task ConfirmMoveBatchLocationAsync()
    {
        if (MovingSourceBatch == null || MoveTargetLocation == null)
            return;

        if (!int.TryParse(MoveQuantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var moveQty) || moveQty <= 0)
        {
            _toast.ShowError("Please enter a valid quantity to move.");
            return;
        }

        IsBusy = true;
        try
        {
            var res = await _productService.MoveBatchLocationAsync(
                MovingSourceBatch.Id,
                new MoveBatchLocationRequest
                {
                    ToStorageLocationId = MoveTargetLocation.Id,
                    Quantity = moveQty,
                    Rack = string.IsNullOrWhiteSpace(MoveTargetRack) ? null : MoveTargetRack.Trim()
                });

            if (!res.Succeeded)
            {
                _toast.ShowError(res.Message);
                return;
            }

            _toast.ShowSuccess(res.Message);
            IsMoveBatchModalOpen = false;
            MovingSourceBatch = null;

            if (ActiveProduct != null)
            {
                await RefreshActiveProductBatchesAsync(ActiveProduct.Id, EditingBatchId);
            }
            await LoadGlobalBatchesPageAsync(resetPage: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // MODAL 2: MANAGE STORAGE LOCATIONS LOGIC
    // =========================================================================

    [RelayCommand]
    public async Task OpenLocationsModalAsync()
    {
        await LoadStorageLocationsAsync();
        ResetStorageLocationForm();
        IsLocationsModalOpen = true;
    }

    [RelayCommand]
    public void CloseLocationsModal()
    {
        IsLocationsModalOpen = false;
    }

    [RelayCommand]
    public void ResetStorageLocationForm()
    {
        EditingLocationId = null;
        LocFormName = string.Empty;
        LocFormCode = string.Empty;
        SelectedLocTypeOption = LocationTypeOptions[0];
        LocFormDefaultRack = string.Empty;
        LocFormNotes = string.Empty;
        LocFormIsDefault = false;
        LocFormIsActive = true;
    }

    [RelayCommand]
    public void StartEditStorageLocation(StorageLocationDto? loc)
    {
        if (loc == null)
            return;

        EditingLocationId = loc.Id;
        LocFormName = loc.Name;
        LocFormCode = loc.Code;
        SelectedLocTypeOption = LocationTypeOptions.FirstOrDefault(o => o.Value == loc.Type) ?? LocationTypeOptions[0];
        LocFormDefaultRack = loc.DefaultRack ?? string.Empty;
        LocFormNotes = loc.Notes ?? string.Empty;
        LocFormIsDefault = loc.IsDefault;
        LocFormIsActive = loc.IsActive;
    }

    [RelayCommand]
    public async Task SaveStorageLocationAsync()
    {
        if (string.IsNullOrWhiteSpace(LocFormName))
        {
            _toast.ShowError("Please enter a storage location name.");
            return;
        }

        IsBusy = true;
        try
        {
            var res = await _productService.SaveStorageLocationAsync(new StorageLocationSaveRequest
            {
                Id = EditingLocationId,
                Name = LocFormName.Trim(),
                Code = string.IsNullOrWhiteSpace(LocFormCode) ? null : LocFormCode.Trim().ToUpperInvariant(),
                Type = SelectedLocTypeOption?.Value ?? "shop",
                DefaultRack = string.IsNullOrWhiteSpace(LocFormDefaultRack) ? null : LocFormDefaultRack.Trim(),
                Notes = string.IsNullOrWhiteSpace(LocFormNotes) ? null : LocFormNotes.Trim(),
                IsDefault = LocFormIsDefault,
                IsActive = LocFormIsActive
            });

            if (!res.Succeeded)
            {
                _toast.ShowError(res.Message);
                return;
            }

            _toast.ShowSuccess(res.Message);
            await LoadStorageLocationsAsync();
            ResetStorageLocationForm();
            await LoadGlobalBatchesPageAsync(resetPage: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteStorageLocationAsync(StorageLocationDto? loc)
    {
        if (loc == null)
            return;

        IsBusy = true;
        try
        {
            var res = await _productService.DeleteStorageLocationAsync(loc.Id);
            if (!res.Succeeded)
            {
                _toast.ShowError(res.Message);
                return;
            }

            _toast.ShowSuccess(res.Message);
            await LoadStorageLocationsAsync();
            if (EditingLocationId == loc.Id)
            {
                ResetStorageLocationForm();
            }
            if (ActiveProduct != null)
            {
                await RefreshActiveProductBatchesAsync(ActiveProduct.Id, EditingBatchId);
            }
            await LoadGlobalBatchesPageAsync(resetPage: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // MODAL 3: DELETE BATCH CONFIRMATION LOGIC
    // =========================================================================

    [RelayCommand]
    public void OpenDeleteBatchModal(ProductBatchItemDto? batch)
    {
        if (batch == null)
            return;

        DeletingBatch = batch;
        OnPropertyChanged(nameof(DeleteBatchConfirmMessage));
        IsDeleteBatchModalOpen = true;
    }

    [RelayCommand]
    public void CloseDeleteBatchModal()
    {
        IsDeleteBatchModalOpen = false;
        DeletingBatch = null;
    }

    [RelayCommand]
    public async Task ConfirmDeleteBatchAsync()
    {
        if (DeletingBatch == null)
            return;

        var deletedId = DeletingBatch.Id;
        IsBusy = true;
        try
        {
            var res = await _productService.DeleteBatchAsync(deletedId);
            if (!res.Succeeded)
            {
                _toast.ShowError(res.Message);
                return;
            }

            _toast.ShowSuccess(res.Message);
            IsDeleteBatchModalOpen = false;
            DeletingBatch = null;

            if (ActiveProduct != null)
            {
                await RefreshActiveProductBatchesAsync(ActiveProduct.Id, batchIdToEdit: null);
                if (EditingBatchId == deletedId)
                {
                    ResetBatchFormInternal(ActiveProduct, notifyFocus: false);
                }
            }
            await LoadGlobalBatchesPageAsync(resetPage: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // GLOBAL ALL-PRODUCTS BATCH DIRECTORY LOGIC
    // =========================================================================

    [RelayCommand]
    public async Task SetGlobalStatusFilterAsync(string? status)
    {
        var normalized = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant();
        if (GlobalFilterStatus == normalized)
            return;

        GlobalFilterStatus = normalized;
        await LoadGlobalBatchesPageAsync(resetPage: true);
    }

    partial void OnGlobalSearchTextChanged(string value)
    {
        if (_suppressGlobalFilterReload)
            return;

        _ = TriggerDebouncedGlobalSearchAsync(value);
    }

    private async Task TriggerDebouncedGlobalSearchAsync(string query)
    {
        _globalSearchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _globalSearchDebounceCts = cts;

        try
        {
            if (!string.IsNullOrEmpty(query))
            {
                await Task.Delay(150, cts.Token);
            }
            if (cts.Token.IsCancellationRequested)
                return;

            await LoadGlobalBatchesPageAsync(resetPage: true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer keystroke
        }
    }

    [RelayCommand]
    public void ClearGlobalSearch()
    {
        GlobalSearchText = string.Empty;
    }

    partial void OnSelectedGlobalLocationChanged(StorageLocationDto? value)
    {
        if (_suppressGlobalFilterReload) return;
        _ = LoadGlobalBatchesPageAsync(resetPage: true);
    }

    partial void OnGlobalIncludeZeroStockChanged(bool value)
    {
        if (_suppressGlobalFilterReload) return;
        _ = LoadGlobalBatchesPageAsync(resetPage: true);
    }

    [RelayCommand]
    public async Task GlobalPrevPageAsync()
    {
        if (!CanGoGlobalPrevPage) return;
        GlobalCurrentPage--;
        await LoadGlobalBatchesPageAsync(resetPage: false);
    }

    [RelayCommand]
    public async Task GlobalNextPageAsync()
    {
        if (!CanGoGlobalNextPage) return;
        GlobalCurrentPage++;
        await LoadGlobalBatchesPageAsync(resetPage: false);
    }

    public async Task LoadGlobalBatchesPageAsync(bool resetPage)
    {
        if (resetPage)
            GlobalCurrentPage = 1;

        _globalDirectoryLoadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _globalDirectoryLoadCts = cts;

        try
        {
            var filter = new GlobalBatchQueryFilter
            {
                Search = string.IsNullOrWhiteSpace(GlobalSearchText) ? null : GlobalSearchText.Trim(),
                Status = GlobalFilterStatus,
                StorageLocationId = SelectedGlobalLocation is { Id: > 0 } ? SelectedGlobalLocation.Id : null,
                IncludeZeroStock = GlobalIncludeZeroStock,
                Page = GlobalCurrentPage,
                PerPage = 25
            };

            var response = await _productService.GetGlobalBatchesDirectoryAsync(filter, cts.Token);
            if (cts.Token.IsCancellationRequested)
                return;

            GlobalSummary = response.Summary;
            GlobalTotalCount = response.TotalCount;
            GlobalCurrentPage = response.CurrentPage;
            GlobalLastPage = response.LastPage;
            GlobalFromItem = response.FromItem;
            GlobalToItem = response.ToItem;
            GlobalBatchRows = response.Items;
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer filter query
        }
    }

    [RelayCommand]
    public async Task EditGlobalBatchRowAsync(GlobalBatchDirectoryRowDto? row)
    {
        if (row == null)
            return;

        await LoadActiveProductByIdAsync(row.ProductId, preselectBatchId: row.Id, focusBatchInput: true);
    }

    [RelayCommand]
    public async Task NewBatchForGlobalRowProductAsync(GlobalBatchDirectoryRowDto? row)
    {
        if (row == null)
            return;

        await LoadActiveProductByIdAsync(row.ProductId, preselectBatchId: null, focusBatchInput: true);
    }

    [RelayCommand]
    public void OpenExpiryRiskDashboard()
    {
        _navigate?.Invoke("/batch-expiry");
    }
}
