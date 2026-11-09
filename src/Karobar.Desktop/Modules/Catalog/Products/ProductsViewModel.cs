using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;
using Microsoft.Win32;

namespace Karobar.Desktop.ViewModels;

public partial class SelectableProductRowViewModel : ObservableObject
{
    public ProductListItemDto Item { get; }
    private readonly Action _onSelectionChanged;

    [ObservableProperty]
    private bool _isSelected;

    public SelectableProductRowViewModel(ProductListItemDto item, Action onSelectionChanged)
    {
        Item = item;
        _onSelectionChanged = onSelectionChanged;
    }

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();
}

public sealed class SimpleStringOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class BatchPickerOption
{
    public long? BatchId { get; init; }
    public string Label { get; init; } = string.Empty;
    public ProductBatchItemDto? Batch { get; init; }
}

public partial class ProductsViewModel : ObservableObject
{
    private readonly IProductService _productService;
    private readonly IAuthService _authService;
    private readonly IToastNotificationService _toast;

    private bool _isCalculatingPrices;
    private bool _suppressSelectionCallback;
    private List<ProductBatchItemDto> _allProductBatches = [];
    private List<SubstituteProductItemDto> _allSubstitutes = [];

    // =========================================================================
    // VIEW MODE ("Directory" | "Form")
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirectoryMode))]
    [NotifyPropertyChangedFor(nameof(IsFormMode))]
    private string _viewMode = "Directory";

    public bool IsDirectoryMode => ViewMode == "Directory";
    public bool IsFormMode => ViewMode == "Form";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _currencySymbol = "Rs.";

    [ObservableProperty]
    private bool _isPharmacyShop = true;

    // =========================================================================
    // DIRECTORY FILTER & PAGINATION STATE
    // =========================================================================

    [ObservableProperty]
    private string _searchText = string.Empty;

    public ObservableCollection<LookupOptionDto> FilterCategoryOptions { get; } = [];
    [ObservableProperty]
    private LookupOptionDto? _selectedFilterCategory;

    public ObservableCollection<LookupOptionDto> FilterBrandOptions { get; } = [];
    [ObservableProperty]
    private LookupOptionDto? _selectedFilterBrand;

    public ObservableCollection<LookupOptionDto> FilterSupplierOptions { get; } = [];
    [ObservableProperty]
    private LookupOptionDto? _selectedFilterSupplier;

    public ObservableCollection<StorageLocationDto> FilterLocationOptions { get; } = [];
    [ObservableProperty]
    private StorageLocationDto? _selectedFilterLocation;

    [ObservableProperty]
    private bool _lowStockOnly;

    public ObservableCollection<int> PageSizeOptions { get; } = [10, 25, 50, 100];

    [ObservableProperty]
    private int _selectedPageSize = 25;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrevPage))]
    [NotifyPropertyChangedFor(nameof(CanGoNextPage))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrevPage))]
    [NotifyPropertyChangedFor(nameof(CanGoNextPage))]
    private int _lastPage = 1;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _fromItem;

    [ObservableProperty]
    private int _toItem;

    public bool CanGoPrevPage => CurrentPage > 1;
    public bool CanGoNextPage => CurrentPage < LastPage;

    public ObservableCollection<SelectableProductRowViewModel> ProductRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private int _selectedCount;

    public bool HasSelection => SelectedCount > 0;

    [ObservableProperty]
    private bool _allPageSelected;

    // =========================================================================
    // CREATE / EDIT FORM STATE
    // =========================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(IsCreateMode))]
    [NotifyPropertyChangedFor(nameof(FormHeadingTitle))]
    [NotifyPropertyChangedFor(nameof(FormHeadingSubtitle))]
    [NotifyPropertyChangedFor(nameof(SaveButtonLabel))]
    private long? _editingProductId;

    public bool IsEditMode => EditingProductId.HasValue && EditingProductId.Value > 0;
    public bool IsCreateMode => !IsEditMode;

    public string FormHeadingTitle => IsEditMode ? "Edit Product" : "Create Product";
    public string FormHeadingSubtitle => IsEditMode
        ? "Update core product details, pricing, margins, batches (FEFO), and multi-supplier catalog"
        : "Configure product identifiers, category, brand, supplier, and default unit/pack pricing";
    public string SaveButtonLabel => IsEditMode ? "Save Changes" : "Save Product";

    public ObservableCollection<LookupOptionDto> FormCategories { get; } = [];
    public ObservableCollection<LookupOptionDto> FormBrands { get; } = [];
    public ObservableCollection<LookupOptionDto> FormSuppliers { get; } = [];
    public ObservableCollection<FormulationOptionDto> FormFormulations { get; } = [];
    public ObservableCollection<StorageLocationDto> FormStorageLocations { get; } = [];

    public ObservableCollection<SimpleStringOption> NarcoticOptions { get; } =
    [
        new() { Value = "no", Label = "Standard (Non-Narcotic)" },
        new() { Value = "yes", Label = "Narcotic / Controlled" }
    ];

    [ObservableProperty]
    private string _formProductCode = string.Empty;

    [ObservableProperty]
    private string _formBarcode = string.Empty;

    [ObservableProperty]
    private StorageLocationDto? _formSelectedLocation;

    [ObservableProperty]
    private string _formRack = string.Empty;

    [ObservableProperty]
    private SimpleStringOption? _formSelectedNarcotic;

    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private string _formFormulation = string.Empty;

    [ObservableProperty]
    private string _formPackSize = "1";

    [ObservableProperty]
    private LookupOptionDto? _formSelectedCategory;

    [ObservableProperty]
    private LookupOptionDto? _formSelectedBrand;

    [ObservableProperty]
    private LookupOptionDto? _formSelectedSupplier;

    [ObservableProperty]
    private string _formDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormImage))]
    private string? _formImagePreviewPath;

    private string? _formSourceImageFilePath;
    private bool _formClearExistingImage;
    public bool HasFormImage => !string.IsNullOrWhiteSpace(FormImagePreviewPath);

    // Pricing & Margin Fields
    [ObservableProperty]
    private string _formQuantity = "0";

    [ObservableProperty]
    private string _formPackPurchasePrice = "0.00";

    [ObservableProperty]
    private string _formPackSalePrice = "0.00";

    [ObservableProperty]
    private string _formUnitPurchasePrice = "0.00";

    [ObservableProperty]
    private string _formUnitSalePrice = "0.00";

    [ObservableProperty]
    private string _formMargin = "0.00";

    [ObservableProperty]
    private string _formWholeSalePackPrice = string.Empty;

    [ObservableProperty]
    private string _formWholeSaleUnitPrice = string.Empty;

    [ObservableProperty]
    private string _formWholeSaleMargin = string.Empty;

    [ObservableProperty]
    private string _formAvgPrice = "0.00";

    [ObservableProperty]
    private string _formMaxDiscount = string.Empty;

    [ObservableProperty]
    private string _formExpiryWarningDays = string.Empty;

    // =========================================================================
    // SECTION 2: BATCHES & EXPIRY (FEFO) STATE
    // =========================================================================

    [ObservableProperty]
    private int _batchTotalStock;

    [ObservableProperty]
    private int _batchAllocatedStock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxBatchAssignable))]
    [NotifyPropertyChangedFor(nameof(BatchAllocationHint))]
    private int _batchUnassignedStock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingBatch))]
    [NotifyPropertyChangedFor(nameof(MaxBatchAssignable))]
    [NotifyPropertyChangedFor(nameof(BatchFormHeading))]
    [NotifyPropertyChangedFor(nameof(BatchAllocationHint))]
    [NotifyPropertyChangedFor(nameof(BatchSaveButtonLabel))]
    private long? _editingBatchId;

    private int _editingBatchOriginalQty;

    public bool IsEditingBatch => EditingBatchId.HasValue && EditingBatchId.Value > 0;

    public int MaxBatchAssignable => IsEditingBatch
        ? BatchUnassignedStock + _editingBatchOriginalQty
        : BatchUnassignedStock;

    public string BatchFormHeading => IsEditingBatch
        ? $"Editing Batch: {BatchNumberInput}"
        : "Add New Batch or Update Existing";

    public string BatchAllocationHint => MaxBatchAssignable > 0
        ? $"Up to {MaxBatchAssignable} unassigned units available to allocate to this batch"
        : "0 unassigned units available — select an existing batch to edit or add stock via Purchase Invoice / Stock Adjustment";

    public string BatchSaveButtonLabel => IsEditingBatch ? "Update Batch" : "Save Batch";

    public ObservableCollection<BatchPickerOption> BatchPickerOptions { get; } = [];
    [ObservableProperty]
    private BatchPickerOption? _selectedBatchPickerOption;

    public ObservableCollection<LocationStockPillDto> LocationBreakdownPills { get; } = [];
    public ObservableCollection<ProductBatchItemDto> FilteredBatches { get; } = [];

    [ObservableProperty]
    private string _batchNumberInput = string.Empty;

    [ObservableProperty]
    private DateTime? _batchExpiryDateInput = DateTime.Today.AddMonths(12);

    [ObservableProperty]
    private string _batchQuantityInput = string.Empty;

    [ObservableProperty]
    private StorageLocationDto? _selectedBatchLocation;

    [ObservableProperty]
    private string _batchRackInput = string.Empty;

    [ObservableProperty]
    private string _batchUnitSalePriceInput = string.Empty;

    [ObservableProperty]
    private string _batchUnitPurchasePriceInput = string.Empty;

    // Batch Filter Toolbar
    [ObservableProperty]
    private string _batchSearchQuery = string.Empty;

    public ObservableCollection<StorageLocationDto> BatchFilterLocations { get; } = [];
    [ObservableProperty]
    private StorageLocationDto? _selectedBatchFilterLocation;

    public ObservableCollection<SimpleStringOption> BatchStatusOptions { get; } =
    [
        new() { Value = "all", Label = "All Batches" },
        new() { Value = "fefo", Label = "FEFO Recommended" },
        new() { Value = "active", Label = "Active / Non-Expired" },
        new() { Value = "expired", Label = "Expired Batches" }
    ];
    [ObservableProperty]
    private SimpleStringOption? _selectedBatchStatusOption;

    public ObservableCollection<SimpleStringOption> BatchSortOptions { get; } =
    [
        new() { Value = "fefo", Label = "Sort: Earliest Expiry First (FEFO)" },
        new() { Value = "expiry_desc", Label = "Sort: Latest Expiry First" },
        new() { Value = "qty_desc", Label = "Sort: Highest Quantity First" },
        new() { Value = "batch_asc", Label = "Sort: Batch Number (A–Z)" }
    ];
    [ObservableProperty]
    private SimpleStringOption? _selectedBatchSortOption;

    // =========================================================================
    // SECTION 3: MULTI-SUPPLIER CATALOG STATE
    // =========================================================================

    public ObservableCollection<ProductSupplierLinkDto> ProductSupplierLinks { get; } = [];

    public ObservableCollection<SimpleStringOption> SupplierPriorityOptions { get; } =
    [
        new() { Value = "secondary", Label = "Secondary / Backup Distributor" },
        new() { Value = "preferred", Label = "Preferred Primary Distributor" }
    ];

    [ObservableProperty]
    private LookupOptionDto? _selectedMultiSupplier;

    [ObservableProperty]
    private SimpleStringOption? _selectedSupplierPriority;

    [ObservableProperty]
    private string _multiSupplierLastRate = string.Empty;

    [ObservableProperty]
    private string _multiSupplierLeadTimeDays = string.Empty;

    [ObservableProperty]
    private string _multiSupplierMinOrderQty = "1";

    [ObservableProperty]
    private string _multiSupplierSku = string.Empty;

    // =========================================================================
    // MODAL 1: MOVE / SPLIT BATCH LOCATION MODAL
    // =========================================================================

    [ObservableProperty]
    private bool _isMoveBatchModalOpen;

    [ObservableProperty]
    private ProductBatchItemDto? _movingBatch;

    [ObservableProperty]
    private StorageLocationDto? _selectedMoveTargetLocation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoveRemainingPreviewText))]
    [NotifyPropertyChangedFor(nameof(MoveTargetPreviewText))]
    private string _moveQuantityInput = string.Empty;

    [ObservableProperty]
    private string _moveTargetRackInput = string.Empty;

    public string MoveRemainingPreviewText
    {
        get
        {
            if (MovingBatch == null) return "0 units";
            var avail = Math.Max(0, MovingBatch.Quantity);
            var move = Math.Clamp(int.TryParse(MoveQuantityInput, out var q) ? q : 0, 0, avail);
            var rem = Math.Max(0, avail - move);
            return $"{rem} units {(rem == 0 ? "(Full move)" : "(Split batch)")}";
        }
    }

    public string MoveTargetPreviewText
    {
        get
        {
            if (MovingBatch == null) return "+0 units";
            var avail = Math.Max(0, MovingBatch.Quantity);
            var move = Math.Clamp(int.TryParse(MoveQuantityInput, out var q) ? q : 0, 0, avail);
            return $"+{move} units";
        }
    }

    // =========================================================================
    // MODAL 2: MANAGE STORAGE LOCATIONS MODAL
    // =========================================================================

    [ObservableProperty]
    private bool _isManageLocationsModalOpen;

    public ObservableCollection<StorageLocationDto> ManagedLocationsList { get; } = [];

    public ObservableCollection<SimpleStringOption> LocationTypeOptions { get; } =
    [
        new() { Value = "shop", Label = "Shop Counter / Retail Shelf (FEFO Priority)" },
        new() { Value = "godown", Label = "Main Godown / Backroom Reserve" },
        new() { Value = "cold_storage", Label = "Cold Storage / Fridge" },
        new() { Value = "quarantine", Label = "Quarantine / Damage Hold" },
        new() { Value = "other", Label = "Other Storage Area" }
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationSaveButtonText))]
    private long? _editingLocationId;

    public string LocationSaveButtonText => EditingLocationId.HasValue ? "Update Location" : "Add Location";

    [ObservableProperty]
    private string _locNameInput = string.Empty;

    [ObservableProperty]
    private string _locCodeInput = string.Empty;

    [ObservableProperty]
    private SimpleStringOption? _selectedLocTypeOption;

    [ObservableProperty]
    private string _locDefaultRackInput = string.Empty;

    [ObservableProperty]
    private bool _locIsDefaultInput;

    // =========================================================================
    // MODAL 3: THERAPEUTIC / GENERIC SUBSTITUTES MODAL
    // =========================================================================

    [ObservableProperty]
    private bool _isSubstituteModalOpen;

    [ObservableProperty]
    private string _substituteSourceName = string.Empty;

    [ObservableProperty]
    private string? _substituteSourceFormulation;

    [ObservableProperty]
    private decimal _substituteSourcePrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormulationMatchBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(IsCategoryMatchBadgeVisible))]
    private string _substituteMatchType = "none";

    public bool IsFormulationMatchBadgeVisible => SubstituteMatchType == "formulation";
    public bool IsCategoryMatchBadgeVisible => SubstituteMatchType == "category";

    [ObservableProperty]
    private string _substituteSearchFilter = string.Empty;

    [ObservableProperty]
    private bool _substituteInStockOnly = true;

    public ObservableCollection<SubstituteProductItemDto> FilteredSubstitutes { get; } = [];

    // =========================================================================
    // MODAL 4: BULK EDIT MODAL
    // =========================================================================

    [ObservableProperty]
    private bool _isBulkEditModalOpen;

    [ObservableProperty]
    private LookupOptionDto? _bulkEditCategory;

    [ObservableProperty]
    private LookupOptionDto? _bulkEditBrand;

    [ObservableProperty]
    private LookupOptionDto? _bulkEditSupplier;

    // =========================================================================
    // MODAL 5: DELETE / BULK DELETE PASSWORD CONFIRMATION MODAL
    // =========================================================================

    [ObservableProperty]
    private bool _isDeleteModalOpen;

    [ObservableProperty]
    private bool _isBulkDeleteMode;

    [ObservableProperty]
    private long? _deletingSingleProductId;

    [ObservableProperty]
    private string _deleteTargetTitle = string.Empty;

    [ObservableProperty]
    private string _deletePasswordInput = string.Empty;

    [ObservableProperty]
    private string? _deleteErrorMessage;

    public ObservableCollection<BulkDeleteFailedItemDto> BulkDeleteFailedItems { get; } = [];

    // =========================================================================
    // MODAL 6: 2-STEP CSV IMPORT MODAL
    // =========================================================================

    [ObservableProperty]
    private bool _isCsvImportModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanValidateCsv))]
    private string? _csvFilePath;

    [ObservableProperty]
    private string _csvFileName = "No CSV file selected";

    public ObservableCollection<SimpleStringOption> CsvDelimiterOptions { get; } =
    [
        new() { Value = ",", Label = "Comma (,)" },
        new() { Value = ";", Label = "Semicolon (;)" },
        new() { Value = "\\t", Label = "Tab" },
        new() { Value = "|", Label = "Pipe (|)" }
    ];

    [ObservableProperty]
    private SimpleStringOption? _selectedCsvDelimiter;

    [ObservableProperty]
    private bool _csvCreateMissingRefs = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCsvValidationResult))]
    [NotifyPropertyChangedFor(nameof(HasCsvInvalidRows))]
    private ProductCsvValidationResult? _csvValidationResult;

    public bool CanValidateCsv => !string.IsNullOrWhiteSpace(CsvFilePath) && !IsBusy;
    public bool HasCsvValidationResult => CsvValidationResult != null;
    public bool HasCsvInvalidRows => CsvValidationResult?.InvalidRows > 0;

    public ObservableCollection<ProductCsvSampleRowDto> CsvInvalidSampleRows { get; } = [];
    public ObservableCollection<ProductCsvSampleRowDto> CsvValidSampleRows { get; } = [];

    public ProductsViewModel(
        IProductService productService,
        IAuthService authService,
        IToastNotificationService toast)
    {
        _productService = productService;
        _authService = authService;
        _toast = toast;

        _formSelectedNarcotic = NarcoticOptions[0];
        _selectedBatchStatusOption = BatchStatusOptions[0];
        _selectedBatchSortOption = BatchSortOptions[0];
        _selectedSupplierPriority = SupplierPriorityOptions[0];
        _selectedLocTypeOption = LocationTypeOptions[0];
        _selectedCsvDelimiter = CsvDelimiterOptions[0];
    }

    public async Task InitializeAsync()
    {
        await LoadLookupsAsync();
        await LoadProductsPageAsync(resetPage: true);
    }

    public async Task OpenDirectoryModeAsync()
    {
        ViewMode = "Directory";
        await InitializeAsync();
    }

    private async Task LoadLookupsAsync()
    {
        var lookups = await _productService.GetProductFormLookupsAsync();
        CurrencySymbol = lookups.CurrencySymbol;
        IsPharmacyShop = !string.Equals(lookups.ShopType, "general", StringComparison.OrdinalIgnoreCase);

        // Directory filters (with "All ..." option at Id = 0)
        FilterCategoryOptions.Clear();
        FilterCategoryOptions.Add(new LookupOptionDto { Id = 0, Name = "All Categories" });
        FormCategories.Clear();
        foreach (var c in lookups.Categories)
        {
            FilterCategoryOptions.Add(c);
            FormCategories.Add(c);
        }
        SelectedFilterCategory ??= FilterCategoryOptions.FirstOrDefault();

        FilterBrandOptions.Clear();
        FilterBrandOptions.Add(new LookupOptionDto { Id = 0, Name = "All Brands" });
        FormBrands.Clear();
        foreach (var b in lookups.Brands)
        {
            FilterBrandOptions.Add(b);
            FormBrands.Add(b);
        }
        SelectedFilterBrand ??= FilterBrandOptions.FirstOrDefault();

        FilterSupplierOptions.Clear();
        FilterSupplierOptions.Add(new LookupOptionDto { Id = 0, Name = "All Suppliers" });
        FormSuppliers.Clear();
        foreach (var s in lookups.Suppliers)
        {
            FilterSupplierOptions.Add(s);
            FormSuppliers.Add(s);
        }
        SelectedFilterSupplier ??= FilterSupplierOptions.FirstOrDefault();

        FormFormulations.Clear();
        foreach (var f in lookups.Formulations)
        {
            FormFormulations.Add(f);
        }

        PopulateLocationCollections(lookups.StorageLocations);
    }

    private void PopulateLocationCollections(List<StorageLocationDto> locations)
    {
        FilterLocationOptions.Clear();
        FilterLocationOptions.Add(new StorageLocationDto { Id = 0, Name = "All Locations", Code = "ALL" });

        FormStorageLocations.Clear();
        ManagedLocationsList.Clear();
        BatchFilterLocations.Clear();
        BatchFilterLocations.Add(new StorageLocationDto { Id = 0, Name = "All Locations", Code = "ALL" });

        foreach (var loc in locations)
        {
            FilterLocationOptions.Add(loc);
            FormStorageLocations.Add(loc);
            ManagedLocationsList.Add(loc);
            BatchFilterLocations.Add(loc);
        }

        SelectedFilterLocation ??= FilterLocationOptions.FirstOrDefault();
        SelectedBatchFilterLocation ??= BatchFilterLocations.FirstOrDefault();
    }

    // =========================================================================
    // DIRECTORY FILTER & PAGINATION HANDLERS
    // =========================================================================

    partial void OnSelectedFilterCategoryChanged(LookupOptionDto? value)
    {
        if (IsDirectoryMode && !IsBusy)
            _ = LoadProductsPageAsync(resetPage: true);
    }

    partial void OnSelectedFilterBrandChanged(LookupOptionDto? value)
    {
        if (IsDirectoryMode && !IsBusy)
            _ = LoadProductsPageAsync(resetPage: true);
    }

    partial void OnSelectedFilterSupplierChanged(LookupOptionDto? value)
    {
        if (IsDirectoryMode && !IsBusy)
            _ = LoadProductsPageAsync(resetPage: true);
    }

    partial void OnSelectedFilterLocationChanged(StorageLocationDto? value)
    {
        if (IsDirectoryMode && !IsBusy)
            _ = LoadProductsPageAsync(resetPage: true);
    }

    partial void OnSelectedPageSizeChanged(int value)
    {
        if (IsDirectoryMode && !IsBusy)
            _ = LoadProductsPageAsync(resetPage: true);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await LoadProductsPageAsync(resetPage: true);
    }

    [RelayCommand]
    private async Task ToggleLowStockAsync()
    {
        LowStockOnly = !LowStockOnly;
        await LoadProductsPageAsync(resetPage: true);
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        IsBusy = true;
        SearchText = string.Empty;
        LowStockOnly = false;
        SelectedFilterCategory = FilterCategoryOptions.FirstOrDefault();
        SelectedFilterBrand = FilterBrandOptions.FirstOrDefault();
        SelectedFilterSupplier = FilterSupplierOptions.FirstOrDefault();
        SelectedFilterLocation = FilterLocationOptions.FirstOrDefault();
        IsBusy = false;
        await LoadProductsPageAsync(resetPage: true);
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanGoNextPage) return;
        CurrentPage++;
        await LoadProductsPageAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (!CanGoPrevPage) return;
        CurrentPage--;
        await LoadProductsPageAsync(resetPage: false);
    }

    public async Task LoadProductsPageAsync(bool resetPage = false)
    {
        if (resetPage)
            CurrentPage = 1;

        IsBusy = true;
        try
        {
            var response = await _productService.GetProductsAsync(new ProductQueryFilter
            {
                Search = SearchText,
                CategoryId = SelectedFilterCategory?.Id > 0 ? SelectedFilterCategory.Id : null,
                BrandId = SelectedFilterBrand?.Id > 0 ? SelectedFilterBrand.Id : null,
                SupplierId = SelectedFilterSupplier?.Id > 0 ? SelectedFilterSupplier.Id : null,
                StorageLocationId = SelectedFilterLocation?.Id > 0 ? SelectedFilterLocation.Id : null,
                LowStockOnly = LowStockOnly,
                Page = CurrentPage,
                PerPage = SelectedPageSize
            });

            ProductRows.Clear();
            foreach (var item in response.Items)
            {
                ProductRows.Add(new SelectableProductRowViewModel(item, OnRowSelectionChanged));
            }

            TotalCount = response.TotalCount;
            CurrentPage = response.CurrentPage;
            LastPage = response.LastPage;
            FromItem = response.FromItem;
            ToItem = response.ToItem;
            UpdateSelectionCounts();
        }
        catch (Exception ex)
        {
            _toast.ShowError($"Failed to load products: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnRowSelectionChanged()
    {
        if (_suppressSelectionCallback) return;
        UpdateSelectionCounts();
    }

    private void UpdateSelectionCounts()
    {
        var count = ProductRows.Count(r => r.IsSelected);
        SelectedCount = count;
        _suppressSelectionCallback = true;
        AllPageSelected = ProductRows.Count > 0 && count == ProductRows.Count;
        _suppressSelectionCallback = false;
    }

    [RelayCommand]
    private void ToggleSelectAllPage()
    {
        _suppressSelectionCallback = true;
        var target = AllPageSelected;
        foreach (var row in ProductRows)
        {
            row.IsSelected = target;
        }
        _suppressSelectionCallback = false;
        UpdateSelectionCounts();
    }

    // =========================================================================
    // CREATE / EDIT FORM NAVIGATION & LIVE PRICING CALCULATORS
    // =========================================================================

    [RelayCommand]
    public async Task OpenCreateProductAsync()
    {
        await LoadLookupsAsync();
        await ResetProductFormForCreateAsync();
        ViewMode = "Form";
    }

    private async Task ResetProductFormForCreateAsync()
    {
        EditingProductId = null;
        var codes = await _productService.GenerateNewCodeAndBarcodeAsync();

        _isCalculatingPrices = true;
        FormProductCode = codes.ProductCode;
        FormBarcode = codes.Barcode;
        FormName = string.Empty;
        FormFormulation = string.Empty;
        FormPackSize = "1";
        FormQuantity = "0";
        FormDescription = string.Empty;
        FormSelectedNarcotic = NarcoticOptions[0];

        var defaultLoc = FormStorageLocations.FirstOrDefault(l => l.IsDefault) ?? FormStorageLocations.FirstOrDefault();
        FormSelectedLocation = defaultLoc;
        FormRack = defaultLoc?.DefaultRack ?? string.Empty;

        FormSelectedCategory = null;
        FormSelectedBrand = null;
        FormSelectedSupplier = null;

        FormImagePreviewPath = null;
        _formSourceImageFilePath = null;
        _formClearExistingImage = false;

        FormPackPurchasePrice = "0.00";
        FormPackSalePrice = "0.00";
        FormUnitPurchasePrice = "0.00";
        FormUnitSalePrice = "0.00";
        FormMargin = "0.00";
        FormWholeSalePackPrice = string.Empty;
        FormWholeSaleUnitPrice = string.Empty;
        FormWholeSaleMargin = string.Empty;
        FormAvgPrice = "0.00";
        FormMaxDiscount = string.Empty;
        FormExpiryWarningDays = string.Empty;
        _isCalculatingPrices = false;

        FilteredBatches.Clear();
        LocationBreakdownPills.Clear();
        ProductSupplierLinks.Clear();
    }

    [RelayCommand]
    private async Task OpenEditProductAsync(SelectableProductRowViewModel? row)
    {
        if (row == null) return;
        await OpenEditProductByIdAsync(row.Item.Id);
    }

    public async Task OpenEditProductByIdAsync(long productId)
    {
        IsBusy = true;
        try
        {
            await LoadLookupsAsync();
            var detail = await _productService.GetProductByIdAsync(productId);
            if (detail == null)
            {
                _toast.ShowError("Product not found.");
                return;
            }

            EditingProductId = detail.Id;
            _isCalculatingPrices = true;

            FormProductCode = detail.ProductCode;
            FormBarcode = detail.Barcode;
            FormName = detail.Name;
            FormFormulation = detail.Formulation ?? string.Empty;
            FormPackSize = detail.PackSize.ToString(CultureInfo.InvariantCulture);
            FormQuantity = detail.Quantity.ToString(CultureInfo.InvariantCulture);
            FormDescription = detail.Description ?? string.Empty;
            FormRack = detail.Rack ?? string.Empty;
            FormSelectedNarcotic = NarcoticOptions.FirstOrDefault(o => o.Value == detail.Narcotic) ?? NarcoticOptions[0];

            FormSelectedLocation = FormStorageLocations.FirstOrDefault(l => l.Id == detail.StorageLocationId)
                                   ?? FormStorageLocations.FirstOrDefault(l => l.IsDefault)
                                   ?? FormStorageLocations.FirstOrDefault();
            FormSelectedCategory = FormCategories.FirstOrDefault(c => c.Id == detail.CategoryId);
            FormSelectedBrand = FormBrands.FirstOrDefault(b => b.Id == detail.BrandId);
            FormSelectedSupplier = FormSuppliers.FirstOrDefault(s => s.Id == detail.SupplierId);

            FormImagePreviewPath = detail.FullImagePath;
            _formSourceImageFilePath = null;
            _formClearExistingImage = false;

            FormPackPurchasePrice = detail.PackPurchasePrice.ToString("0.00", CultureInfo.InvariantCulture);
            FormPackSalePrice = detail.PackSalePrice.ToString("0.00", CultureInfo.InvariantCulture);
            FormUnitPurchasePrice = detail.UnitPurchasePrice.ToString("0.00", CultureInfo.InvariantCulture);
            FormUnitSalePrice = detail.UnitSalePrice.ToString("0.00", CultureInfo.InvariantCulture);
            FormAvgPrice = detail.AvgPrice.ToString("0.00", CultureInfo.InvariantCulture);
            FormMargin = (detail.Margin ?? 0m).ToString("0.00", CultureInfo.InvariantCulture);
            FormWholeSalePackPrice = detail.WholeSalePackPrice?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;
            FormWholeSaleUnitPrice = detail.WholeSaleUnitPrice?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;
            FormWholeSaleMargin = detail.WholeSaleMargin?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;
            FormMaxDiscount = detail.MaxDiscount?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
            FormExpiryWarningDays = detail.ExpiryWarningDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

            _isCalculatingPrices = false;

            await RefreshBatchesAndSuppliersForEditingProductAsync();
            ViewMode = "Form";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BackToDirectoryAsync()
    {
        ViewMode = "Directory";
        await LoadProductsPageAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task AutoGenerateBarcodeAsync()
    {
        var codes = await _productService.GenerateNewCodeAndBarcodeAsync();
        FormBarcode = codes.Barcode;
        _toast.ShowInfo($"Generated internal barcode: {codes.Barcode}");
    }

    [RelayCommand]
    private void BrowseProductImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Product Image",
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
        };
        if (dlg.ShowDialog() == true)
        {
            _formSourceImageFilePath = dlg.FileName;
            _formClearExistingImage = false;
            FormImagePreviewPath = dlg.FileName;
        }
    }

    [RelayCommand]
    private void ClearProductImage()
    {
        _formSourceImageFilePath = null;
        _formClearExistingImage = true;
        FormImagePreviewPath = null;
    }

    partial void OnFormSelectedLocationChanged(StorageLocationDto? value)
    {
        if (value != null && string.IsNullOrWhiteSpace(FormRack) && !string.IsNullOrWhiteSpace(value.DefaultRack))
        {
            FormRack = value.DefaultRack;
        }
    }

    // Live Pricing & Margin Bi-Directional Auto-Calculation
    partial void OnFormPackSizeChanged(string value)
    {
        if (_isCalculatingPrices) return;
        var pack = GetSafePackSize();
        if (pack <= 0) return;

        _isCalculatingPrices = true;
        if (TryParseDec(FormPackPurchasePrice, out var ppp) && ppp > 0m)
        {
            var upp = Math.Round(ppp / pack, 2);
            FormUnitPurchasePrice = upp.ToString("0.00", CultureInfo.InvariantCulture);
            FormAvgPrice = upp.ToString("0.00", CultureInfo.InvariantCulture);
        }
        if (TryParseDec(FormPackSalePrice, out var psp) && psp > 0m)
        {
            var usp = Math.Round(psp / pack, 2);
            FormUnitSalePrice = usp.ToString("0.00", CultureInfo.InvariantCulture);
        }
        if (TryParseDec(FormWholeSalePackPrice, out var wspp) && wspp > 0m)
        {
            var wsup = Math.Round(wspp / pack, 2);
            FormWholeSaleUnitPrice = wsup.ToString("0.00", CultureInfo.InvariantCulture);
        }
        RecalculateMarginsInternal();
        _isCalculatingPrices = false;
    }

    partial void OnFormPackPurchasePriceChanged(string value)
    {
        if (_isCalculatingPrices) return;
        if (!TryParseDec(value, out var ppp)) return;

        _isCalculatingPrices = true;
        var pack = GetSafePackSize();
        var upp = Math.Round(ppp / pack, 2);
        FormUnitPurchasePrice = upp.ToString("0.00", CultureInfo.InvariantCulture);
        FormAvgPrice = upp.ToString("0.00", CultureInfo.InvariantCulture);
        RecalculateMarginsInternal();
        _isCalculatingPrices = false;
    }

    partial void OnFormUnitPurchasePriceChanged(string value)
    {
        if (_isCalculatingPrices) return;
        if (!TryParseDec(value, out var upp)) return;

        _isCalculatingPrices = true;
        var pack = GetSafePackSize();
        var ppp = Math.Round(upp * pack, 2);
        FormPackPurchasePrice = ppp.ToString("0.00", CultureInfo.InvariantCulture);
        FormAvgPrice = upp.ToString("0.00", CultureInfo.InvariantCulture);
        RecalculateMarginsInternal();
        _isCalculatingPrices = false;
    }

    partial void OnFormPackSalePriceChanged(string value)
    {
        if (_isCalculatingPrices) return;
        if (!TryParseDec(value, out var psp)) return;

        _isCalculatingPrices = true;
        var pack = GetSafePackSize();
        var usp = Math.Round(psp / pack, 2);
        FormUnitSalePrice = usp.ToString("0.00", CultureInfo.InvariantCulture);
        RecalculateMarginsInternal();
        _isCalculatingPrices = false;
    }

    partial void OnFormUnitSalePriceChanged(string value)
    {
        if (_isCalculatingPrices) return;
        if (!TryParseDec(value, out var usp)) return;

        _isCalculatingPrices = true;
        var pack = GetSafePackSize();
        var psp = Math.Round(usp * pack, 2);
        FormPackSalePrice = psp.ToString("0.00", CultureInfo.InvariantCulture);
        RecalculateMarginsInternal();
        _isCalculatingPrices = false;
    }

    partial void OnFormWholeSalePackPriceChanged(string value)
    {
        if (_isCalculatingPrices) return;
        if (!TryParseDec(value, out var wspp)) return;

        _isCalculatingPrices = true;
        var pack = GetSafePackSize();
        var wsup = Math.Round(wspp / pack, 2);
        FormWholeSaleUnitPrice = wsup.ToString("0.00", CultureInfo.InvariantCulture);
        RecalculateMarginsInternal();
        _isCalculatingPrices = false;
    }

    partial void OnFormWholeSaleUnitPriceChanged(string value)
    {
        if (_isCalculatingPrices) return;
        if (!TryParseDec(value, out var wsup)) return;

        _isCalculatingPrices = true;
        var pack = GetSafePackSize();
        var wspp = Math.Round(wsup * pack, 2);
        FormWholeSalePackPrice = wspp.ToString("0.00", CultureInfo.InvariantCulture);
        RecalculateMarginsInternal();
        _isCalculatingPrices = false;
    }

    private void RecalculateMarginsInternal()
    {
        TryParseDec(FormAvgPrice, out var avg);
        if (avg <= 0m) TryParseDec(FormUnitPurchasePrice, out avg);

        if (TryParseDec(FormUnitSalePrice, out var usp) && usp > 0m)
        {
            var m = Math.Round(((usp - avg) / usp) * 100m, 2);
            FormMargin = m.ToString("0.00", CultureInfo.InvariantCulture);
        }

        if (TryParseDec(FormWholeSaleUnitPrice, out var wsup) && wsup > 0m)
        {
            var wm = Math.Round(((wsup - avg) / wsup) * 100m, 2);
            FormWholeSaleMargin = wm.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    private int GetSafePackSize() =>
        int.TryParse(FormPackSize, out var ps) && ps > 0 ? ps : 1;

    private static bool TryParseDec(string? val, out decimal result) =>
        decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out result);

    private static decimal? ParseOptionalDec(string? val) =>
        TryParseDec(val, out var d) ? d : null;

    private static int? ParseOptionalInt(string? val) =>
        int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : null;

    [RelayCommand]
    private async Task SaveProductAsync()
    {
        await SaveProductInternalAsync(closeAfterSave: false);
    }

    [RelayCommand]
    private async Task SaveAndCloseProductAsync()
    {
        await SaveProductInternalAsync(closeAfterSave: true);
    }

    private async Task SaveProductInternalAsync(bool closeAfterSave)
    {
        var req = new ProductUpsertRequest
        {
            ProductCode = FormProductCode.Trim(),
            Barcode = FormBarcode.Trim(),
            Name = FormName.Trim(),
            Formulation = FormFormulation.Trim(),
            Description = FormDescription.Trim(),
            SourceImageFilePath = _formSourceImageFilePath,
            ClearExistingImage = _formClearExistingImage,
            PackSize = GetSafePackSize(),
            Quantity = int.TryParse(FormQuantity, out var q) ? Math.Max(0, q) : 0,
            PackPurchasePrice = TryParseDec(FormPackPurchasePrice, out var ppp) ? ppp : 0m,
            UnitPurchasePrice = TryParseDec(FormUnitPurchasePrice, out var upp) ? upp : 0m,
            PackSalePrice = TryParseDec(FormPackSalePrice, out var psp) ? psp : 0m,
            UnitSalePrice = TryParseDec(FormUnitSalePrice, out var usp) ? usp : 0m,
            WholeSalePackPrice = ParseOptionalDec(FormWholeSalePackPrice),
            WholeSaleUnitPrice = ParseOptionalDec(FormWholeSaleUnitPrice),
            WholeSaleMargin = ParseOptionalDec(FormWholeSaleMargin),
            AvgPrice = TryParseDec(FormAvgPrice, out var avg) ? avg : 0m,
            Margin = ParseOptionalDec(FormMargin),
            Narcotic = FormSelectedNarcotic?.Value ?? "no",
            MaxDiscount = ParseOptionalDec(FormMaxDiscount),
            ExpiryWarningDays = ParseOptionalInt(FormExpiryWarningDays),
            Rack = FormRack.Trim(),
            CategoryId = FormSelectedCategory?.Id,
            BrandId = FormSelectedBrand?.Id,
            SupplierId = FormSelectedSupplier?.Id,
            StorageLocationId = FormSelectedLocation?.Id
        };

        IsBusy = true;
        try
        {
            if (IsEditMode && EditingProductId.HasValue)
            {
                var res = await _productService.UpdateProductAsync(EditingProductId.Value, req);
                if (!res.Succeeded)
                {
                    _toast.ShowError(res.Message);
                    return;
                }
                _toast.ShowSuccess(res.Message);
                await RefreshBatchesAndSuppliersForEditingProductAsync();
                if (closeAfterSave)
                {
                    await BackToDirectoryAsync();
                }
            }
            else
            {
                var res = await _productService.CreateProductAsync(req);
                if (!res.Succeeded)
                {
                    _toast.ShowError(res.Message);
                    return;
                }
                _toast.ShowSuccess(res.Message);
                if (closeAfterSave)
                {
                    await BackToDirectoryAsync();
                }
                else
                {
                    await LoadLookupsAsync();
                    await ResetProductFormForCreateAsync();
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // SECTION 2: BATCHES & EXPIRY (FEFO) COMMANDS
    // =========================================================================

    private async Task RefreshBatchesAndSuppliersForEditingProductAsync()
    {
        if (!EditingProductId.HasValue) return;
        var pid = EditingProductId.Value;

        var batchSummary = await _productService.GetProductBatchesAsync(pid);
        BatchTotalStock = batchSummary.TotalStock;
        BatchAllocatedStock = batchSummary.BatchedStock;
        BatchUnassignedStock = batchSummary.UnassignedStock;

        _allProductBatches = batchSummary.Batches;

        LocationBreakdownPills.Clear();
        foreach (var pill in batchSummary.LocationBreakdown)
        {
            LocationBreakdownPills.Add(pill);
        }

        BatchPickerOptions.Clear();
        BatchPickerOptions.Add(new BatchPickerOption { BatchId = null, Label = "+ Create New Batch", Batch = null });
        foreach (var b in _allProductBatches)
        {
            BatchPickerOptions.Add(new BatchPickerOption { BatchId = b.Id, Label = b.PickerLabel, Batch = b });
        }

        ResetBatchForm();
        ApplyBatchFilterAndSort();

        var supLinks = await _productService.GetProductSuppliersAsync(pid);
        ProductSupplierLinks.Clear();
        foreach (var sl in supLinks)
        {
            ProductSupplierLinks.Add(sl);
        }
    }

    partial void OnSelectedBatchPickerOptionChanged(BatchPickerOption? value)
    {
        if (value == null || !value.BatchId.HasValue || value.Batch == null)
        {
            if (EditingBatchId != null)
                ResetBatchForm();
            return;
        }

        if (EditingBatchId != value.BatchId)
        {
            LoadBatchIntoEditor(value.Batch);
        }
    }

    private void LoadBatchIntoEditor(ProductBatchItemDto batch)
    {
        EditingBatchId = batch.Id;
        _editingBatchOriginalQty = batch.Quantity;
        BatchNumberInput = batch.BatchNumber;
        BatchExpiryDateInput = batch.ExpiryDate ?? DateTime.Today.AddMonths(6);
        BatchQuantityInput = batch.Quantity.ToString(CultureInfo.InvariantCulture);
        SelectedBatchLocation = FormStorageLocations.FirstOrDefault(l => l.Id == batch.StorageLocationId)
                                ?? FormSelectedLocation
                                ?? FormStorageLocations.FirstOrDefault();
        BatchRackInput = batch.Rack ?? string.Empty;
        BatchUnitSalePriceInput = batch.UnitSalePrice?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;
        BatchUnitPurchasePriceInput = batch.UnitPurchasePrice?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;

        OnPropertyChanged(nameof(MaxBatchAssignable));
        OnPropertyChanged(nameof(BatchFormHeading));
        OnPropertyChanged(nameof(BatchAllocationHint));
    }

    private void ResetBatchForm()
    {
        EditingBatchId = null;
        _editingBatchOriginalQty = 0;
        BatchNumberInput = string.Empty;
        BatchExpiryDateInput = DateTime.Today.AddMonths(12);
        BatchQuantityInput = BatchUnassignedStock > 0 ? BatchUnassignedStock.ToString(CultureInfo.InvariantCulture) : "0";
        SelectedBatchLocation = FormSelectedLocation ?? FormStorageLocations.FirstOrDefault();
        BatchRackInput = FormRack;
        BatchUnitSalePriceInput = string.Empty;
        BatchUnitPurchasePriceInput = string.Empty;
        SelectedBatchPickerOption = BatchPickerOptions.FirstOrDefault();

        OnPropertyChanged(nameof(MaxBatchAssignable));
        OnPropertyChanged(nameof(BatchFormHeading));
        OnPropertyChanged(nameof(BatchAllocationHint));
    }

    [RelayCommand]
    private void StartEditBatch(ProductBatchItemDto? batch)
    {
        if (batch == null) return;
        LoadBatchIntoEditor(batch);
        SelectedBatchPickerOption = BatchPickerOptions.FirstOrDefault(o => o.BatchId == batch.Id);
    }

    [RelayCommand]
    private void CancelEditBatch()
    {
        ResetBatchForm();
    }

    [RelayCommand]
    private async Task SaveBatchAsync()
    {
        if (!EditingProductId.HasValue) return;
        if (string.IsNullOrWhiteSpace(BatchNumberInput) || !BatchExpiryDateInput.HasValue)
        {
            _toast.ShowWarning("Please enter both Batch Number and Expiry Date.");
            return;
        }

        var qty = int.TryParse(BatchQuantityInput, out var parsedQty) ? parsedQty : 0;
        var res = await _productService.SaveProductBatchAsync(EditingProductId.Value, new BatchSaveRequest
        {
            BatchId = EditingBatchId,
            BatchNumber = BatchNumberInput.Trim(),
            ExpiryDate = BatchExpiryDateInput.Value,
            Quantity = qty,
            StorageLocationId = SelectedBatchLocation?.Id,
            Rack = BatchRackInput.Trim(),
            UnitSalePrice = ParseOptionalDec(BatchUnitSalePriceInput),
            UnitPurchasePrice = ParseOptionalDec(BatchUnitPurchasePriceInput)
        });

        if (!res.Succeeded)
        {
            _toast.ShowError(res.Message);
            return;
        }

        _toast.ShowSuccess(res.Message);
        await RefreshBatchesAndSuppliersForEditingProductAsync();
    }

    [RelayCommand]
    private async Task DeleteBatchAsync(ProductBatchItemDto? batch)
    {
        if (batch == null) return;
        var res = await _productService.DeleteBatchAsync(batch.Id);
        if (!res.Succeeded)
        {
            _toast.ShowError(res.Message);
            return;
        }
        _toast.ShowSuccess(res.Message);
        await RefreshBatchesAndSuppliersForEditingProductAsync();
    }

    partial void OnBatchSearchQueryChanged(string value) => ApplyBatchFilterAndSort();
    partial void OnSelectedBatchFilterLocationChanged(StorageLocationDto? value) => ApplyBatchFilterAndSort();
    partial void OnSelectedBatchStatusOptionChanged(SimpleStringOption? value) => ApplyBatchFilterAndSort();
    partial void OnSelectedBatchSortOptionChanged(SimpleStringOption? value) => ApplyBatchFilterAndSort();

    private void ApplyBatchFilterAndSort()
    {
        var q = (BatchSearchQuery ?? string.Empty).Trim();
        var status = SelectedBatchStatusOption?.Value ?? "all";
        var locId = SelectedBatchFilterLocation?.Id ?? 0;
        var sort = SelectedBatchSortOption?.Value ?? "fefo";

        var query = _allProductBatches.AsEnumerable();

        if (status == "fefo")
            query = query.Where(b => b.IsFefoRecommended);
        else if (status == "active")
            query = query.Where(b => !b.IsExpired);
        else if (status == "expired")
            query = query.Where(b => b.IsExpired);

        if (locId > 0)
            query = query.Where(b => b.StorageLocationId == locId);

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(b =>
                b.BatchNumber.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.ExpiryDateFormatted.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (b.Rack != null && b.Rack.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        query = sort switch
        {
            "expiry_desc" => query.OrderByDescending(b => b.ExpiryDate ?? DateTime.MinValue),
            "qty_desc" => query.OrderByDescending(b => b.Quantity),
            "batch_asc" => query.OrderBy(b => b.BatchNumber, StringComparer.OrdinalIgnoreCase),
            _ => query // Already sorted by FEFO from service
        };

        FilteredBatches.Clear();
        foreach (var item in query)
        {
            FilteredBatches.Add(item);
        }
    }

    // Move / Split Batch Location Modal Commands
    [RelayCommand]
    private void OpenMoveBatchModal(ProductBatchItemDto? batch)
    {
        if (batch == null) return;
        MovingBatch = batch;
        var altLoc = FormStorageLocations.FirstOrDefault(l => l.IsActive && l.Id != batch.StorageLocationId)
                     ?? FormStorageLocations.FirstOrDefault();
        SelectedMoveTargetLocation = altLoc;
        MoveQuantityInput = batch.Quantity.ToString(CultureInfo.InvariantCulture);
        MoveTargetRackInput = batch.Rack ?? altLoc?.DefaultRack ?? string.Empty;
        IsMoveBatchModalOpen = true;
    }

    [RelayCommand]
    private void SetMoveQtyOnePack()
    {
        if (MovingBatch == null) return;
        var pack = Math.Min(MovingBatch.Quantity, GetSafePackSize());
        MoveQuantityInput = pack.ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void SetMoveQtyHalf()
    {
        if (MovingBatch == null) return;
        var half = Math.Max(1, MovingBatch.Quantity / 2);
        MoveQuantityInput = half.ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void SetMoveQtyAll()
    {
        if (MovingBatch == null) return;
        MoveQuantityInput = MovingBatch.Quantity.ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private async Task ConfirmMoveBatchAsync()
    {
        if (MovingBatch == null || SelectedMoveTargetLocation == null)
        {
            _toast.ShowWarning("Please select a destination storage location.");
            return;
        }

        if (!int.TryParse(MoveQuantityInput, out var moveQty) || moveQty <= 0 || moveQty > MovingBatch.Quantity)
        {
            _toast.ShowWarning($"Please enter a quantity between 1 and {MovingBatch.Quantity}.");
            return;
        }

        var res = await _productService.MoveBatchLocationAsync(MovingBatch.Id, new MoveBatchLocationRequest
        {
            ToStorageLocationId = SelectedMoveTargetLocation.Id,
            Quantity = moveQty,
            Rack = MoveTargetRackInput.Trim()
        });

        if (!res.Succeeded)
        {
            _toast.ShowError(res.Message);
            return;
        }

        _toast.ShowSuccess(res.Message);
        IsMoveBatchModalOpen = false;
        MovingBatch = null;
        await RefreshBatchesAndSuppliersForEditingProductAsync();
    }

    [RelayCommand]
    private void CloseMoveBatchModal()
    {
        IsMoveBatchModalOpen = false;
        MovingBatch = null;
    }

    // =========================================================================
    // SECTION 3: MULTI-SUPPLIER CATALOG COMMANDS
    // =========================================================================

    [RelayCommand]
    private async Task SaveProductSupplierLinkAsync()
    {
        if (!EditingProductId.HasValue) return;
        if (SelectedMultiSupplier == null || SelectedMultiSupplier.Id <= 0)
        {
            _toast.ShowWarning("Please select a supplier to link.");
            return;
        }

        var res = await _productService.SaveProductSupplierAsync(EditingProductId.Value, new ProductSupplierSaveRequest
        {
            SupplierId = SelectedMultiSupplier.Id,
            SupplierSku = MultiSupplierSku.Trim(),
            LastPurchasePrice = ParseOptionalDec(MultiSupplierLastRate),
            LeadTimeDays = ParseOptionalInt(MultiSupplierLeadTimeDays),
            MinOrderQty = int.TryParse(MultiSupplierMinOrderQty, out var moq) && moq > 0 ? moq : 1,
            IsPreferred = SelectedSupplierPriority?.Value == "preferred"
        });

        if (!res.Succeeded)
        {
            _toast.ShowError(res.Message);
            return;
        }

        _toast.ShowSuccess(res.Message);
        SelectedMultiSupplier = null;
        SelectedSupplierPriority = SupplierPriorityOptions[0];
        MultiSupplierLastRate = string.Empty;
        MultiSupplierLeadTimeDays = string.Empty;
        MultiSupplierMinOrderQty = "1";
        MultiSupplierSku = string.Empty;

        var supLinks = await _productService.GetProductSuppliersAsync(EditingProductId.Value);
        ProductSupplierLinks.Clear();
        foreach (var sl in supLinks)
        {
            ProductSupplierLinks.Add(sl);
        }
    }

    [RelayCommand]
    private async Task RemoveProductSupplierLinkAsync(ProductSupplierLinkDto? link)
    {
        if (link == null || !EditingProductId.HasValue) return;
        var res = await _productService.RemoveProductSupplierAsync(EditingProductId.Value, link.SupplierId);
        if (!res.Succeeded)
        {
            _toast.ShowError(res.Message);
            return;
        }

        _toast.ShowSuccess(res.Message);
        var supLinks = await _productService.GetProductSuppliersAsync(EditingProductId.Value);
        ProductSupplierLinks.Clear();
        foreach (var sl in supLinks)
        {
            ProductSupplierLinks.Add(sl);
        }
    }

    // =========================================================================
    // MANAGE STORAGE LOCATIONS MODAL COMMANDS
    // =========================================================================

    [RelayCommand]
    private async Task OpenManageLocationsModalAsync()
    {
        var locs = await _productService.GetStorageLocationsAsync(activeOnly: false);
        PopulateLocationCollections(locs);
        ResetLocationForm();
        IsManageLocationsModalOpen = true;
    }

    [RelayCommand]
    private void EditStorageLocation(StorageLocationDto? loc)
    {
        if (loc == null) return;
        EditingLocationId = loc.Id;
        LocNameInput = loc.Name;
        LocCodeInput = loc.Code;
        SelectedLocTypeOption = LocationTypeOptions.FirstOrDefault(o => o.Value == loc.Type) ?? LocationTypeOptions[0];
        LocDefaultRackInput = loc.DefaultRack ?? string.Empty;
        LocIsDefaultInput = loc.IsDefault;
    }

    [RelayCommand]
    private void ResetLocationForm()
    {
        EditingLocationId = null;
        LocNameInput = string.Empty;
        LocCodeInput = string.Empty;
        SelectedLocTypeOption = LocationTypeOptions[0];
        LocDefaultRackInput = string.Empty;
        LocIsDefaultInput = false;
    }

    [RelayCommand]
    private async Task SaveStorageLocationAsync()
    {
        var res = await _productService.SaveStorageLocationAsync(new StorageLocationSaveRequest
        {
            Id = EditingLocationId,
            Name = LocNameInput.Trim(),
            Code = LocCodeInput.Trim(),
            Type = SelectedLocTypeOption?.Value ?? "shop",
            DefaultRack = LocDefaultRackInput.Trim(),
            IsDefault = LocIsDefaultInput,
            IsActive = true
        });

        if (!res.Succeeded)
        {
            _toast.ShowError(res.Message);
            return;
        }

        _toast.ShowSuccess(res.Message);
        var locs = await _productService.GetStorageLocationsAsync(activeOnly: false);
        PopulateLocationCollections(locs);
        ResetLocationForm();
    }

    [RelayCommand]
    private async Task DeleteStorageLocationAsync(StorageLocationDto? loc)
    {
        if (loc == null) return;
        var res = await _productService.DeleteStorageLocationAsync(loc.Id);
        if (!res.Succeeded)
        {
            _toast.ShowError(res.Message);
            return;
        }

        _toast.ShowSuccess(res.Message);
        var locs = await _productService.GetStorageLocationsAsync(activeOnly: false);
        PopulateLocationCollections(locs);
    }

    [RelayCommand]
    private void CloseManageLocationsModal()
    {
        IsManageLocationsModalOpen = false;
    }

    // =========================================================================
    // THERAPEUTIC / GENERIC SUBSTITUTES MODAL COMMANDS
    // =========================================================================

    [RelayCommand]
    private async Task OpenSubstituteModalAsync(SelectableProductRowViewModel? row)
    {
        if (row == null) return;
        var res = await _productService.GetSubstituteProductsAsync(row.Item.Id);
        SubstituteSourceName = res.SourceProductName;
        SubstituteSourceFormulation = res.SourceFormulation;
        SubstituteSourcePrice = res.SourceUnitSalePrice;
        SubstituteMatchType = res.MatchType;
        SubstituteSearchFilter = string.Empty;
        SubstituteInStockOnly = true;
        _allSubstitutes = res.Substitutes;
        ApplySubstituteFilter();
        IsSubstituteModalOpen = true;
    }

    partial void OnSubstituteSearchFilterChanged(string value) => ApplySubstituteFilter();
    partial void OnSubstituteInStockOnlyChanged(bool value) => ApplySubstituteFilter();

    private void ApplySubstituteFilter()
    {
        var q = (SubstituteSearchFilter ?? string.Empty).Trim();
        var query = _allSubstitutes.AsEnumerable();

        if (SubstituteInStockOnly)
            query = query.Where(s => s.Quantity > 0);

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(s =>
                s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (s.Formulation != null && s.Formulation.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                s.BrandName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (s.Rack != null && s.Rack.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        FilteredSubstitutes.Clear();
        foreach (var item in query)
        {
            FilteredSubstitutes.Add(item);
        }
    }

    [RelayCommand]
    private async Task InspectSubstituteProductAsync(SubstituteProductItemDto? sub)
    {
        if (sub == null) return;
        IsSubstituteModalOpen = false;
        await OpenEditProductByIdAsync(sub.Id);
    }

    [RelayCommand]
    private void CloseSubstituteModal()
    {
        IsSubstituteModalOpen = false;
    }

    // =========================================================================
    // BULK EDIT MODAL COMMANDS
    // =========================================================================

    [RelayCommand]
    private void OpenBulkEditModal()
    {
        if (!HasSelection)
        {
            _toast.ShowWarning("Select at least one product to bulk edit.");
            return;
        }
        BulkEditCategory = null;
        BulkEditBrand = null;
        BulkEditSupplier = null;
        IsBulkEditModalOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmBulkEditAsync()
    {
        var selectedIds = ProductRows.Where(r => r.IsSelected).Select(r => r.Item.Id).ToList();
        var res = await _productService.BulkUpdateProductMetaAsync(new BulkUpdateProductMetaRequest
        {
            ProductIds = selectedIds,
            CategoryId = BulkEditCategory?.Id,
            BrandId = BulkEditBrand?.Id,
            SupplierId = BulkEditSupplier?.Id
        });

        if (!res.Succeeded)
        {
            _toast.ShowError(res.Message);
            return;
        }

        _toast.ShowSuccess(res.Message);
        IsBulkEditModalOpen = false;
        await LoadProductsPageAsync(resetPage: false);
    }

    [RelayCommand]
    private void CloseBulkEditModal()
    {
        IsBulkEditModalOpen = false;
    }

    // =========================================================================
    // SINGLE & BULK DELETE WITH PASSWORD CONFIRMATION MODAL
    // =========================================================================

    [RelayCommand]
    private void OpenDeleteSingleModal(SelectableProductRowViewModel? row)
    {
        if (row == null) return;
        if (!row.Item.CanDelete)
        {
            _toast.ShowWarning(row.Item.DeleteTooltip);
            return;
        }

        IsBulkDeleteMode = false;
        DeletingSingleProductId = row.Item.Id;
        DeleteTargetTitle = $"Delete product \"{row.Item.Name}\"?";
        DeletePasswordInput = string.Empty;
        DeleteErrorMessage = null;
        BulkDeleteFailedItems.Clear();
        IsDeleteModalOpen = true;
    }

    [RelayCommand]
    private void OpenBulkDeleteModal()
    {
        if (!HasSelection)
        {
            _toast.ShowWarning("Select at least one product to delete.");
            return;
        }

        IsBulkDeleteMode = true;
        DeletingSingleProductId = null;
        DeleteTargetTitle = $"Delete {SelectedCount} selected product(s)?";
        DeletePasswordInput = string.Empty;
        DeleteErrorMessage = null;
        BulkDeleteFailedItems.Clear();
        IsDeleteModalOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteWithPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(DeletePasswordInput))
        {
            DeleteErrorMessage = "Please enter your password to confirm deletion.";
            return;
        }

        var userId = _authService.CurrentSession?.UserId ?? 1;
        var verified = await _authService.VerifyPasswordAsync(userId, DeletePasswordInput);
        if (!verified)
        {
            DeleteErrorMessage = "Incorrect password. Please try again.";
            return;
        }

        DeleteErrorMessage = null;

        if (!IsBulkDeleteMode && DeletingSingleProductId.HasValue)
        {
            var res = await _productService.DeleteProductAsync(DeletingSingleProductId.Value);
            if (!res.Succeeded)
            {
                DeleteErrorMessage = res.Message;
                _toast.ShowError(res.Message);
                return;
            }

            _toast.ShowSuccess(res.Message);
            IsDeleteModalOpen = false;
            await LoadProductsPageAsync(resetPage: false);
        }
        else if (IsBulkDeleteMode)
        {
            var selectedIds = ProductRows.Where(r => r.IsSelected).Select(r => r.Item.Id).ToList();
            var bulkRes = await _productService.BulkDeleteProductsAsync(selectedIds);

            BulkDeleteFailedItems.Clear();
            foreach (var f in bulkRes.FailedItems)
            {
                BulkDeleteFailedItems.Add(f);
            }

            if (bulkRes.DeletedCount > 0)
            {
                _toast.ShowSuccess($"Deleted {bulkRes.DeletedCount} product(s).");
            }

            if (bulkRes.FailedItems.Count > 0)
            {
                DeleteErrorMessage = $"{bulkRes.FailedItems.Count} product(s) could not be deleted (see reasons below).";
                await LoadProductsPageAsync(resetPage: false);
            }
            else
            {
                IsDeleteModalOpen = false;
                await LoadProductsPageAsync(resetPage: false);
            }
        }
    }

    [RelayCommand]
    private void CloseDeleteModal()
    {
        IsDeleteModalOpen = false;
        DeletePasswordInput = string.Empty;
        DeleteErrorMessage = null;
        BulkDeleteFailedItems.Clear();
    }

    // =========================================================================
    // CSV EXPORT & 2-STEP CSV IMPORT COMMANDS
    // =========================================================================

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export Products to CSV",
            FileName = $"products_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv",
            Filter = "CSV Files (*.csv)|*.csv"
        };

        if (dlg.ShowDialog() != true)
            return;

        IsBusy = true;
        try
        {
            var csvContent = await _productService.ExportProductsCsvAsync();
            await File.WriteAllTextAsync(dlg.FileName, csvContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _toast.ShowSuccess($"Exported products to {Path.GetFileName(dlg.FileName)}.");
        }
        catch (Exception ex)
        {
            _toast.ShowError($"CSV export failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenCsvImportModal()
    {
        ResetCsvImport();
        IsCsvImportModalOpen = true;
    }

    [RelayCommand]
    private void ResetCsvImport()
    {
        CsvFilePath = null;
        CsvFileName = "No CSV file selected";
        SelectedCsvDelimiter = CsvDelimiterOptions[0];
        CsvCreateMissingRefs = true;
        CsvValidationResult = null;
        CsvInvalidSampleRows.Clear();
        CsvValidSampleRows.Clear();
    }

    [RelayCommand]
    private void BrowseCsvFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Products CSV File",
            Filter = "CSV Files (*.csv;*.txt)|*.csv;*.txt"
        };
        if (dlg.ShowDialog() == true)
        {
            CsvFilePath = dlg.FileName;
            CsvFileName = Path.GetFileName(dlg.FileName);
            CsvValidationResult = null;
            CsvInvalidSampleRows.Clear();
            CsvValidSampleRows.Clear();
        }
    }

    [RelayCommand]
    private async Task DownloadCsvTemplateAsync()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save Products CSV Template",
            FileName = "products_template.csv",
            Filter = "CSV Files (*.csv)|*.csv"
        };
        if (dlg.ShowDialog() == true)
        {
            var content = _productService.GetCsvTemplateContent();
            await File.WriteAllTextAsync(dlg.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _toast.ShowSuccess("CSV template saved.");
        }
    }

    [RelayCommand]
    private async Task ValidateCsvAsync()
    {
        if (string.IsNullOrWhiteSpace(CsvFilePath))
        {
            _toast.ShowWarning("Please choose a CSV file first.");
            return;
        }

        IsBusy = true;
        try
        {
            var delim = SelectedCsvDelimiter?.Value ?? ",";
            var res = await _productService.ValidateCsvImportAsync(CsvFilePath, delim, CsvCreateMissingRefs);
            if (!res.Ok)
            {
                _toast.ShowError(res.Message);
                return;
            }

            CsvValidationResult = res;
            CsvInvalidSampleRows.Clear();
            foreach (var r in res.InvalidSamples)
                CsvInvalidSampleRows.Add(r);

            CsvValidSampleRows.Clear();
            foreach (var r in res.ValidSamples)
                CsvValidSampleRows.Add(r);

            if (res.InvalidRows > 0)
                _toast.ShowWarning(res.Message);
            else
                _toast.ShowSuccess(res.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CommitCsvValidOnlyAsync()
    {
        await CommitCsvInternalAsync(insertValidOnly: true);
    }

    [RelayCommand]
    private async Task CommitCsvStrictAsync()
    {
        await CommitCsvInternalAsync(insertValidOnly: false);
    }

    private async Task CommitCsvInternalAsync(bool insertValidOnly)
    {
        if (string.IsNullOrWhiteSpace(CsvFilePath)) return;

        IsBusy = true;
        try
        {
            var delim = SelectedCsvDelimiter?.Value ?? ",";
            var res = await _productService.CommitCsvImportAsync(CsvFilePath, insertValidOnly, delim, CsvCreateMissingRefs);
            if (!res.Succeeded)
            {
                _toast.ShowError(res.Message);
                return;
            }

            _toast.ShowSuccess(res.Message);
            IsCsvImportModalOpen = false;
            ResetCsvImport();
            await LoadLookupsAsync();
            await LoadProductsPageAsync(resetPage: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CloseCsvImportModal()
    {
        IsCsvImportModalOpen = false;
    }
}
