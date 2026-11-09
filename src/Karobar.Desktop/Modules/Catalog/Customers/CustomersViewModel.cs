using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;
using Karobar.Infrastructure.Services;
using Microsoft.Win32;

namespace Karobar.Desktop.ViewModels;

public sealed class CustomerFilterOption
{
    public string Key { get; init; } = "all";
    public string Label { get; init; } = string.Empty;
}

public sealed class CreditStatusOption
{
    public string Key { get; init; } = "active";
    public string Label { get; init; } = string.Empty;
}

public sealed class RefillCycleOption
{
    public string Key { get; init; } = "monthly";
    public string Label { get; init; } = string.Empty;
    public string SubText { get; init; } = string.Empty;
}

public sealed class ReminderLanguageOption
{
    public string Key { get; init; } = "english";
    public string Label { get; init; } = string.Empty;
}

public partial class CustomerRegularItemFormRowViewModel : ObservableObject
{
    private bool _suppressQuantitySync;

    [ObservableProperty]
    private int _rowNumber = 1;

    [ObservableProperty]
    private CustomerCatalogProductDto? _selectedProduct;

    [ObservableProperty]
    private string _packQuantityText = "0";

    [ObservableProperty]
    private string _unitQuantityText = "1";

    [ObservableProperty]
    private string _dosageNotes = string.Empty;

    public int PackSize => Math.Max(1, SelectedProduct?.PackSize ?? 1);
    public int AvailableUnits => SelectedProduct?.OnHandUnits ?? 0;
    public string PackSizeDisplay => SelectedProduct != null ? PackSize.ToString() : "—";
    public string AvailableUnitsDisplay => SelectedProduct != null ? AvailableUnits.ToString() : "—";

    public int PackQuantityValue => int.TryParse(PackQuantityText?.Trim(), out int pq) && pq >= 0 ? pq : 0;
    public int UnitQuantityValue => int.TryParse(UnitQuantityText?.Trim(), out int uq) && uq >= 1 ? uq : 1;

    public string AvailableUnitsFg => SelectedProduct == null
        ? "#64748B"
        : (AvailableUnits >= UnitQuantityValue ? "#059669" : "#D97706");

    partial void OnSelectedProductChanged(CustomerCatalogProductDto? value)
    {
        OnPropertyChanged(nameof(PackSize));
        OnPropertyChanged(nameof(AvailableUnits));
        OnPropertyChanged(nameof(PackSizeDisplay));
        OnPropertyChanged(nameof(AvailableUnitsDisplay));
        OnPropertyChanged(nameof(AvailableUnitsFg));

        if (value != null && PackQuantityValue > 0)
        {
            _suppressQuantitySync = true;
            UnitQuantityText = Math.Max(1, PackQuantityValue * PackSize).ToString();
            _suppressQuantitySync = false;
        }
    }

    partial void OnPackQuantityTextChanged(string value)
    {
        if (_suppressQuantitySync)
            return;

        int pq = PackQuantityValue;
        if (pq > 0)
        {
            _suppressQuantitySync = true;
            UnitQuantityText = Math.Max(1, pq * PackSize).ToString();
            _suppressQuantitySync = false;
        }
        OnPropertyChanged(nameof(AvailableUnitsFg));
    }

    partial void OnUnitQuantityTextChanged(string value)
    {
        OnPropertyChanged(nameof(AvailableUnitsFg));
    }
}

public partial class CustomersViewModel : ObservableObject
{
    private readonly ICustomerService _customerService;
    private readonly IAuthService _authService;
    private readonly IToastNotificationService _toastService;
    private readonly Action<string>? _navigate;
    private readonly List<CustomerDto> _allCustomers = [];
    private bool _suppressRefillDateAutoCalc;

    // ------------------------------------------------------------------------
    // Permissions State
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private bool _canView = true;

    [ObservableProperty]
    private bool _canCreate = true;

    [ObservableProperty]
    private bool _canUpdate = true;

    [ObservableProperty]
    private bool _canDelete = true;

    [ObservableProperty]
    private bool _canImport = true;

    [ObservableProperty]
    private bool _canExport = true;

    public bool HasRowActions => CanView || CanUpdate || CanDelete;

    // ------------------------------------------------------------------------
    // Page Mode State: Directory vs Create/Edit Form vs Customer Detail View
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _activeMode = "directory"; // "directory", "form", "detail"

    [ObservableProperty]
    private long? _editingId;

    [ObservableProperty]
    private string _editingCustomerOriginalName = string.Empty;

    [ObservableProperty]
    private CustomerDto? _selectedDetailCustomer;

    [ObservableProperty]
    private CustomerCreditSummaryDto? _detailCreditSummary;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isExporting;

    public bool IsDirectoryMode => string.Equals(ActiveMode, "directory", StringComparison.OrdinalIgnoreCase);
    public bool IsFormMode => string.Equals(ActiveMode, "form", StringComparison.OrdinalIgnoreCase);
    public bool IsDetailMode => string.Equals(ActiveMode, "detail", StringComparison.OrdinalIgnoreCase);
    public bool IsEditing => EditingId.HasValue;
    public bool IsCreating => !EditingId.HasValue;
    public bool CanExportNow => !IsExporting;

    public string FormPageTitle => IsEditing
        ? $"Edit Customer — {EditingCustomerOriginalName}"
        : "Add Customer";

    public string FormPageSubtitle => IsEditing
        ? "Update customer profile, credit limits, and regular medicine refill reminders"
        : "Create a customer profile, configure credit terms, or set up regular medicine reminders";

    public string SaveButtonText => IsEditing
        ? (IsSaving ? "Updating..." : "Update Customer")
        : (IsSaving ? "Saving..." : "Save Customer");

    // ------------------------------------------------------------------------
    // Directory Filter, Summary Stats & Pagination State
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private CustomerFilterOption _selectedCustomerFilter;

    [ObservableProperty]
    private int _page = 1;

    [ObservableProperty]
    private int _pageSize = 25;

    [ObservableProperty]
    private int _pageCount = 1;

    [ObservableProperty]
    private int _totalCustomersCount;

    [ObservableProperty]
    private int _regularCustomersCount;

    [ObservableProperty]
    private int _dueRegularCustomersCount;

    [ObservableProperty]
    private int _filteredCustomersCount;

    public ObservableCollection<CustomerFilterOption> CustomerFilterOptions { get; } =
    [
        new CustomerFilterOption { Key = "all", Label = "All Customers" },
        new CustomerFilterOption { Key = "regular", Label = "Regular Customers (All)" },
        new CustomerFilterOption { Key = "due_reminder", Label = "Due for WhatsApp Reminder" },
        new CustomerFilterOption { Key = "weekly", Label = "Weekly Regular Customers" },
        new CustomerFilterOption { Key = "monthly", Label = "Monthly Regular Customers" },
        new CustomerFilterOption { Key = "non_regular", Label = "Standard (Non-Regular) Customers" }
    ];

    public ObservableCollection<int> PageSizeOptions { get; } = [10, 25, 50, 100];
    public ObservableCollection<CustomerDto> PagedCustomers { get; } = [];
    public ObservableCollection<CustomerCatalogProductDto> CatalogProducts { get; } = [];

    public string DirectorySubtitleText =>
        $"{TotalCustomersCount} customers ({RegularCustomersCount} regular • {DueRegularCustomersCount} due for WhatsApp reminder)";

    public bool HasDueRegularCustomers => DueRegularCustomersCount > 0;
    public string DueBannerTitleText =>
        $"{DueRegularCustomersCount} Regular {(DueRegularCustomersCount == 1 ? "Customer is" : "Customers are")} due for medicine refill reminder";

    public string PageStatusText => $"Page {Page} of {PageCount}";
    public bool CanGoPrevPage => Page > 1;
    public bool CanGoNextPage => Page < PageCount;
    public bool IsEmptyList => PagedCustomers.Count == 0 && !IsLoading;

    // ------------------------------------------------------------------------
    // Create / Edit Customer Form State (CustomerForm.jsx)
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private string _formPhone = string.Empty;

    [ObservableProperty]
    private string _formEmail = string.Empty;

    [ObservableProperty]
    private string _formAddress = string.Empty;

    [ObservableProperty]
    private TaxStatusOption _selectedTaxStatus;

    [ObservableProperty]
    private string _formTaxNumber = string.Empty;

    [ObservableProperty]
    private CreditStatusOption _selectedCreditStatus;

    [ObservableProperty]
    private string _formCreditLimitText = string.Empty;

    [ObservableProperty]
    private string _formCreditDaysText = "30";

    [ObservableProperty]
    private string _formOpeningBalanceText = string.Empty;

    // Regular Customer & Recurring Medicine Schedule Fields
    [ObservableProperty]
    private bool _formIsRegular;

    [ObservableProperty]
    private RefillCycleOption _selectedRefillCycle;

    [ObservableProperty]
    private string _formRefillCustomDaysText = "30";

    [ObservableProperty]
    private string _formNextRefillDate = string.Empty;

    [ObservableProperty]
    private string _formReminderLeadDaysText = "3";

    [ObservableProperty]
    private ReminderLanguageOption _selectedReminderLanguage;

    [ObservableProperty]
    private string _formRegularNotes = string.Empty;

    public ObservableCollection<TaxStatusOption> TaxStatusOptions { get; } =
    [
        new TaxStatusOption { Key = "unregistered", Label = "Unregistered" },
        new TaxStatusOption { Key = "registered", Label = "Tax Registered (FBR / GST)" },
        new TaxStatusOption { Key = "exempt", Label = "Tax Exempt" }
    ];

    public ObservableCollection<CreditStatusOption> CreditStatusOptions { get; } =
    [
        new CreditStatusOption { Key = "active", Label = "Active" },
        new CreditStatusOption { Key = "hold", Label = "On Hold (Warn Only)" },
        new CreditStatusOption { Key = "blocked", Label = "Blocked (Disallow Credit)" }
    ];

    public ObservableCollection<RefillCycleOption> RefillCycleOptions { get; } =
    [
        new RefillCycleOption { Key = "weekly", Label = "Weekly (Every 7 Days)", SubText = "Every 7 days" },
        new RefillCycleOption { Key = "biweekly", Label = "Bi-Weekly (Every 14 Days)", SubText = "Every 14 days" },
        new RefillCycleOption { Key = "monthly", Label = "Monthly (Every 30 Days)", SubText = "Every 30 days" },
        new RefillCycleOption { Key = "custom", Label = "Custom Interval (Days)", SubText = "Custom days" }
    ];

    public ObservableCollection<ReminderLanguageOption> ReminderLanguageOptions { get; } =
    [
        new ReminderLanguageOption { Key = "english", Label = "English" },
        new ReminderLanguageOption { Key = "urdu", Label = "اردو (Urdu)" }
    ];

    public ObservableCollection<CustomerRegularItemFormRowViewModel> RegularItemRows { get; } = [];

    public bool IsCustomRefillCycleSelected =>
        FormIsRegular && string.Equals(SelectedRefillCycle?.Key, "custom", StringComparison.OrdinalIgnoreCase);

    public bool HasRegularItemRows => RegularItemRows.Count > 0;
    public bool HasNoRegularItemRows => RegularItemRows.Count == 0;
    public string RegularItemsCountBadgeText => $"{RegularItemRows.Count(r => r.SelectedProduct != null)} configured";

    // ------------------------------------------------------------------------
    // Regular Refill Reminder Modal State (RegularRefillReminderModal.jsx)
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private bool _isReminderModalOpen;

    [ObservableProperty]
    private bool _isLoadingReminderPreview;

    [ObservableProperty]
    private CustomerRefillReminderPreviewDto? _reminderPreview;

    [ObservableProperty]
    private string _reminderPhone = string.Empty;

    [ObservableProperty]
    private bool _reminderSavePhone = true;

    [ObservableProperty]
    private string _reminderLanguage = "english";

    [ObservableProperty]
    private string _reminderMessage = string.Empty;

    [ObservableProperty]
    private bool _reminderAdvanceCycle;

    [ObservableProperty]
    private bool _isSendingReminder;

    public bool HasReminderPreview => ReminderPreview != null && !IsLoadingReminderPreview;
    public bool IsReminderEnglish => string.Equals(ReminderLanguage, "english", StringComparison.OrdinalIgnoreCase);
    public bool IsReminderUrdu => string.Equals(ReminderLanguage, "urdu", StringComparison.OrdinalIgnoreCase);
    public string ReminderEnglishBtnBg => IsReminderEnglish ? "#059669" : "#FFFFFF";
    public string ReminderEnglishBtnFg => IsReminderEnglish ? "#FFFFFF" : "#334155";
    public string ReminderUrduBtnBg => IsReminderUrdu ? "#059669" : "#FFFFFF";
    public string ReminderUrduBtnFg => IsReminderUrdu ? "#FFFFFF" : "#334155";

    // ------------------------------------------------------------------------
    // Customer Credit Summary Modal State
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private bool _isCreditModalOpen;

    [ObservableProperty]
    private bool _isLoadingCreditSummary;

    [ObservableProperty]
    private CustomerCreditSummaryDto? _creditSummary;

    public bool HasCreditSummary => CreditSummary != null && !IsLoadingCreditSummary;

    // ------------------------------------------------------------------------
    // Custom Wholesale Price List Modal State
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private bool _isPriceModalOpen;

    [ObservableProperty]
    private bool _isLoadingPrices;

    [ObservableProperty]
    private bool _isSavingPrice;

    [ObservableProperty]
    private CustomerDto? _priceModalCustomer;

    [ObservableProperty]
    private CustomerCatalogProductDto? _selectedPriceProduct;

    [ObservableProperty]
    private string _customPackPriceText = string.Empty;

    public ObservableCollection<CustomerWholeSalePriceDto> CustomerWholeSalePrices { get; } = [];

    public bool HasCustomerWholeSalePrices => CustomerWholeSalePrices.Count > 0;
    public bool HasNoCustomerWholeSalePrices => CustomerWholeSalePrices.Count == 0 && !IsLoadingPrices;
    public string PriceModalSubtitle => PriceModalCustomer != null
        ? $"Customer: {PriceModalCustomer.Name} — Overrides standard wholesale pack price on Wholesale Invoices"
        : string.Empty;
    public string SelectedPriceProductHint => SelectedPriceProduct != null
        ? $"Pack Size: {SelectedPriceProduct.PackSize} units • Std Wholesale Pack: Rs. {SelectedPriceProduct.WholeSalePackPrice:N2} • Retail Pack: Rs. {SelectedPriceProduct.PackSalePrice:N2}"
        : "Select a product above to see its standard pack price";

    // ------------------------------------------------------------------------
    // Customer CSV Import Modal State
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private bool _isImportModalOpen;

    [ObservableProperty]
    private string _importFilePath = string.Empty;

    [ObservableProperty]
    private DelimiterOptionModel _selectedDelimiter;

    [ObservableProperty]
    private bool _isValidatingImport;

    [ObservableProperty]
    private bool _isCommittingImport;

    [ObservableProperty]
    private CustomerCsvValidationResult? _validationResult;

    public ObservableCollection<DelimiterOptionModel> DelimiterOptions { get; } =
    [
        new DelimiterOptionModel { Label = "Comma (,)", Value = "," },
        new DelimiterOptionModel { Label = "Semicolon (;)", Value = ";" },
        new DelimiterOptionModel { Label = "Tab (\\t)", Value = "\\t" },
        new DelimiterOptionModel { Label = "Pipe (|)", Value = "|" }
    ];

    public ObservableCollection<CustomerCsvSampleRow> InvalidSamples { get; } = [];
    public ObservableCollection<CustomerCsvSampleRow> ValidSamples { get; } = [];

    public string ImportFileNameDisplay => string.IsNullOrWhiteSpace(ImportFilePath)
        ? "No file chosen"
        : Path.GetFileName(ImportFilePath);

    public bool CanValidateImport => !string.IsNullOrWhiteSpace(ImportFilePath) && !IsValidatingImport;
    public bool IsImportStep1Visible => ValidationResult == null;
    public bool IsImportStep2Visible => ValidationResult != null;
    public bool HasInvalidSamples => InvalidSamples.Count > 0;
    public bool HasValidSamples => ValidSamples.Count > 0;
    public bool HasInvalidRows => (ValidationResult?.Invalid ?? 0) > 0;

    public string TotalBadgeText => $"Total: {ValidationResult?.Total ?? 0}";
    public string ValidBadgeText => $"Valid: {ValidationResult?.Valid ?? 0}";
    public string InvalidBadgeText => $"Invalid: {ValidationResult?.Invalid ?? 0}";
    public string InvalidBadgeBg => HasInvalidRows ? "#FEE2E2" : "#DCFCE7";
    public string InvalidBadgeFg => HasInvalidRows ? "#DC2626" : "#16A34A";
    public string CommitPrimaryButtonText => IsCommittingImport
        ? "Importing..."
        : $"Import {(HasInvalidRows ? "Valid Rows Only" : "All")}";

    public CustomersViewModel(
        ICustomerService customerService,
        IAuthService authService,
        IToastNotificationService toastService,
        Action<string>? navigate = null)
    {
        _customerService = customerService;
        _authService = authService;
        _toastService = toastService;
        _navigate = navigate;

        _selectedCustomerFilter = CustomerFilterOptions[0];
        _selectedTaxStatus = TaxStatusOptions[0];
        _selectedCreditStatus = CreditStatusOptions[0];
        _selectedRefillCycle = RefillCycleOptions[2]; // Monthly
        _selectedReminderLanguage = ReminderLanguageOptions[0];
        _selectedDelimiter = DelimiterOptions[0];

        RefreshPermissions();
    }

    public void RefreshPermissions()
    {
        var session = _authService.CurrentSession;
        if (session == null || session.IsAdmin)
        {
            CanView = true;
            CanCreate = true;
            CanUpdate = true;
            CanDelete = true;
            CanImport = true;
            CanExport = true;
        }
        else
        {
            CanView = session.Permissions.Contains("customer.view");
            CanCreate = session.Permissions.Contains("customer.create");
            CanUpdate = session.Permissions.Contains("customer.update");
            CanDelete = session.Permissions.Contains("customer.delete");
            CanImport = session.Permissions.Contains("customer.import") || session.Permissions.Contains("customer.create");
            CanExport = session.Permissions.Contains("customer.export") || session.Permissions.Contains("customer.view");
        }

        OnPropertyChanged(nameof(HasRowActions));
    }

    partial void OnActiveModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsDirectoryMode));
        OnPropertyChanged(nameof(IsFormMode));
        OnPropertyChanged(nameof(IsDetailMode));
    }

    partial void OnEditingIdChanged(long? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsCreating));
        OnPropertyChanged(nameof(FormPageTitle));
        OnPropertyChanged(nameof(FormPageSubtitle));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    partial void OnEditingCustomerOriginalNameChanged(string value)
    {
        OnPropertyChanged(nameof(FormPageTitle));
    }

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(SaveButtonText));
    }

    partial void OnIsExportingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExportNow));
    }

    partial void OnSearchQueryChanged(string value)
    {
        Page = 1;
        RecomputeFilteredAndPaged();
    }

    partial void OnSelectedCustomerFilterChanged(CustomerFilterOption value)
    {
        Page = 1;
        RecomputeFilteredAndPaged();
    }

    partial void OnPageSizeChanged(int value)
    {
        Page = 1;
        RecomputeFilteredAndPaged();
    }

    partial void OnPageChanged(int value)
    {
        OnPropertyChanged(nameof(PageStatusText));
        OnPropertyChanged(nameof(CanGoPrevPage));
        OnPropertyChanged(nameof(CanGoNextPage));
    }

    partial void OnPageCountChanged(int value)
    {
        OnPropertyChanged(nameof(PageStatusText));
        OnPropertyChanged(nameof(CanGoPrevPage));
        OnPropertyChanged(nameof(CanGoNextPage));
    }

    partial void OnTotalCustomersCountChanged(int value)
    {
        OnPropertyChanged(nameof(DirectorySubtitleText));
    }

    partial void OnRegularCustomersCountChanged(int value)
    {
        OnPropertyChanged(nameof(DirectorySubtitleText));
    }

    partial void OnDueRegularCustomersCountChanged(int value)
    {
        OnPropertyChanged(nameof(DirectorySubtitleText));
        OnPropertyChanged(nameof(HasDueRegularCustomers));
        OnPropertyChanged(nameof(DueBannerTitleText));
    }

    partial void OnFormIsRegularChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCustomRefillCycleSelected));
        if (value && string.IsNullOrWhiteSpace(FormNextRefillDate) && !_suppressRefillDateAutoCalc)
        {
            RecalculateDefaultNextRefillDate();
        }
        if (value && RegularItemRows.Count == 0 && !_suppressRefillDateAutoCalc)
        {
            AddRegularItemRow();
        }
    }

    partial void OnSelectedRefillCycleChanged(RefillCycleOption value)
    {
        OnPropertyChanged(nameof(IsCustomRefillCycleSelected));
        if (FormIsRegular && !IsEditing && !_suppressRefillDateAutoCalc)
        {
            RecalculateDefaultNextRefillDate();
        }
    }

    partial void OnFormRefillCustomDaysTextChanged(string value)
    {
        if (FormIsRegular && !IsEditing && !_suppressRefillDateAutoCalc)
        {
            RecalculateDefaultNextRefillDate();
        }
    }

    partial void OnIsLoadingReminderPreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(HasReminderPreview));
    }

    partial void OnReminderPreviewChanged(CustomerRefillReminderPreviewDto? value)
    {
        OnPropertyChanged(nameof(HasReminderPreview));
    }

    partial void OnReminderLanguageChanged(string value)
    {
        OnPropertyChanged(nameof(IsReminderEnglish));
        OnPropertyChanged(nameof(IsReminderUrdu));
        OnPropertyChanged(nameof(ReminderEnglishBtnBg));
        OnPropertyChanged(nameof(ReminderEnglishBtnFg));
        OnPropertyChanged(nameof(ReminderUrduBtnBg));
        OnPropertyChanged(nameof(ReminderUrduBtnFg));
    }

    partial void OnIsLoadingCreditSummaryChanged(bool value)
    {
        OnPropertyChanged(nameof(HasCreditSummary));
    }

    partial void OnCreditSummaryChanged(CustomerCreditSummaryDto? value)
    {
        OnPropertyChanged(nameof(HasCreditSummary));
    }

    partial void OnPriceModalCustomerChanged(CustomerDto? value)
    {
        OnPropertyChanged(nameof(PriceModalSubtitle));
    }

    partial void OnSelectedPriceProductChanged(CustomerCatalogProductDto? value)
    {
        OnPropertyChanged(nameof(SelectedPriceProductHint));
    }

    partial void OnIsLoadingPricesChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoCustomerWholeSalePrices));
    }

    partial void OnImportFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(ImportFileNameDisplay));
        OnPropertyChanged(nameof(CanValidateImport));
    }

    partial void OnIsValidatingImportChanged(bool value)
    {
        OnPropertyChanged(nameof(CanValidateImport));
    }

    partial void OnIsCommittingImportChanged(bool value)
    {
        OnPropertyChanged(nameof(CommitPrimaryButtonText));
    }

    partial void OnValidationResultChanged(CustomerCsvValidationResult? value)
    {
        OnPropertyChanged(nameof(IsImportStep1Visible));
        OnPropertyChanged(nameof(IsImportStep2Visible));
        OnPropertyChanged(nameof(HasInvalidRows));
        OnPropertyChanged(nameof(TotalBadgeText));
        OnPropertyChanged(nameof(ValidBadgeText));
        OnPropertyChanged(nameof(InvalidBadgeText));
        OnPropertyChanged(nameof(InvalidBadgeBg));
        OnPropertyChanged(nameof(InvalidBadgeFg));
        OnPropertyChanged(nameof(CommitPrimaryButtonText));
    }

    // ------------------------------------------------------------------------
    // Mode Navigation & Directory Commands
    // ------------------------------------------------------------------------
    public async Task OpenDirectoryModeAsync()
    {
        ActiveMode = "directory";
        await LoadCustomersAsync();
    }

    [RelayCommand]
    public async Task LoadCustomersAsync()
    {
        RefreshPermissions();
        if (!CanView)
        {
            _toastService.ShowError("You don't have permission to view customers.", "Access Denied");
            return;
        }

        IsLoading = true;
        try
        {
            var list = await _customerService.GetAllAsync();
            _allCustomers.Clear();
            _allCustomers.AddRange(list);

            TotalCustomersCount = _allCustomers.Count;
            RegularCustomersCount = _allCustomers.Count(c => c.IsRegular);
            DueRegularCustomersCount = _allCustomers.Count(c => c.IsRegular && c.IsDueForReminder);

            RecomputeFilteredAndPaged();
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to load customers: {ex.Message}", "Load Error");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmptyList));
        }
    }

    [RelayCommand]
    public async Task OpenCreateModeAsync()
    {
        RefreshPermissions();
        if (!CanCreate)
        {
            _toastService.ShowError("You don't have permission to create customers.", "Access Denied");
            return;
        }

        await EnsureCatalogProductsLoadedAsync();
        ResetFormFields();
        ActiveMode = "form";
    }

    [RelayCommand]
    public async Task EditCustomerAsync(CustomerDto? customer)
    {
        if (customer == null)
            return;

        RefreshPermissions();
        if (!CanUpdate)
        {
            _toastService.ShowError("You don't have permission to edit customers.", "Access Denied");
            return;
        }

        try
        {
            await EnsureCatalogProductsLoadedAsync();
            var detailed = await _customerService.GetByIdAsync(customer.Id) ?? customer;
            PopulateFormFromCustomer(detailed);
            ActiveMode = "form";
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to load customer details: {ex.Message}", "Load Error");
        }
    }

    [RelayCommand]
    public async Task ViewCustomerDetailsAsync(CustomerDto? customer)
    {
        if (customer == null)
            return;

        try
        {
            var detailed = await _customerService.GetByIdAsync(customer.Id) ?? customer;
            var credit = await _customerService.GetCreditSummaryAsync(customer.Id);
            SelectedDetailCustomer = detailed;
            DetailCreditSummary = credit;
            ActiveMode = "detail";
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to open customer details: {ex.Message}", "Load Error");
        }
    }

    [RelayCommand]
    public async Task BackToDirectoryAsync()
    {
        ResetFormFields();
        SelectedDetailCustomer = null;
        DetailCreditSummary = null;
        ActiveMode = "directory";
        await LoadCustomersAsync();
    }

    [RelayCommand]
    public void FilterDueCustomers()
    {
        SelectedCustomerFilter = CustomerFilterOptions.First(f => f.Key == "due_reminder");
    }

    [RelayCommand]
    public async Task WhatsAppFirstDueCustomerAsync()
    {
        var firstDue = _allCustomers.FirstOrDefault(c => c.IsRegular && c.IsDueForReminder && c.StockReady)
                       ?? _allCustomers.FirstOrDefault(c => c.IsRegular && c.IsDueForReminder);
        if (firstDue == null)
        {
            _toastService.ShowInfo("No regular customers are currently due for a WhatsApp reminder.", "Reminders");
            return;
        }

        await OpenReminderModalAsync(firstDue);
    }

    [RelayCommand]
    public void ShiftToRetailInvoice(CustomerDto? customer)
    {
        if (customer == null)
            return;

        if (!customer.IsRegular || customer.RegularItemsCount == 0)
        {
            _toastService.ShowWarning("This customer has no regular medicines configured yet.", "Regular Medicines");
            return;
        }

        _toastService.ShowSuccess(
            $"Prepared {customer.RegularItemsCount} regular item(s) for {customer.Name}.",
            "Shift to Retail Invoice");
        _navigate?.Invoke("/sale-invoices/create/retail");
    }

    [RelayCommand]
    public async Task MarkCollectedAndAdvanceAsync(CustomerDto? customer)
    {
        if (customer == null)
            return;

        try
        {
            var updated = await _customerService.AdvanceRefillCycleAsync(customer.Id);
            _toastService.ShowSuccess(
                $"Next refill date advanced to {updated.NextRefillDateDisplay}.",
                "Refill Cycle Advanced");

            if (IsDetailMode && SelectedDetailCustomer?.Id == customer.Id)
            {
                SelectedDetailCustomer = updated;
            }
            await LoadCustomersAsync();
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Advance Cycle Failed");
        }
    }

    // ------------------------------------------------------------------------
    // Regular Items Repeater Commands
    // ------------------------------------------------------------------------
    [RelayCommand]
    public void AddRegularItemRow()
    {
        var row = new CustomerRegularItemFormRowViewModel
        {
            RowNumber = RegularItemRows.Count + 1,
            PackQuantityText = "0",
            UnitQuantityText = "1"
        };
        row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(RegularItemsCountBadgeText));
        RegularItemRows.Add(row);
        RenumberRegularItemRows();
    }

    [RelayCommand]
    public void RemoveRegularItemRow(CustomerRegularItemFormRowViewModel? row)
    {
        if (row == null)
            return;

        RegularItemRows.Remove(row);
        RenumberRegularItemRows();
    }

    private void RenumberRegularItemRows()
    {
        for (int i = 0; i < RegularItemRows.Count; i++)
        {
            RegularItemRows[i].RowNumber = i + 1;
        }
        OnPropertyChanged(nameof(HasRegularItemRows));
        OnPropertyChanged(nameof(HasNoRegularItemRows));
        OnPropertyChanged(nameof(RegularItemsCountBadgeText));
    }

    // ------------------------------------------------------------------------
    // Save & Delete Customer Commands
    // ------------------------------------------------------------------------
    [RelayCommand]
    public async Task SaveCustomerAsync()
    {
        if (IsSaving)
            return;

        if (IsEditing ? !CanUpdate : !CanCreate)
        {
            _toastService.ShowError("You don't have permission to save customers.", "Access Denied");
            return;
        }

        string trimmedName = FormName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            _toastService.ShowError("Customer name is required.", "Validation Error");
            return;
        }

        decimal? creditLimit = null;
        if (!string.IsNullOrWhiteSpace(FormCreditLimitText))
        {
            if (decimal.TryParse(FormCreditLimitText.Replace(",", string.Empty).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cl) && cl >= 0m)
                creditLimit = cl;
        }

        int? creditDays = 30;
        if (!string.IsNullOrWhiteSpace(FormCreditDaysText))
        {
            if (int.TryParse(FormCreditDaysText.Trim(), out int cd) && cd >= 0)
                creditDays = cd;
        }

        decimal? openingBalance = null;
        if (!IsEditing && !string.IsNullOrWhiteSpace(FormOpeningBalanceText))
        {
            if (decimal.TryParse(FormOpeningBalanceText.Replace(",", string.Empty).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal ob) && ob > 0m)
                openingBalance = ob;
        }

        int? customDays = null;
        if (int.TryParse(FormRefillCustomDaysText?.Trim(), out int rcd) && rcd >= 1)
            customDays = rcd;

        int? leadDays = 3;
        if (int.TryParse(FormReminderLeadDaysText?.Trim(), out int rld) && rld >= 0)
            leadDays = rld;

        var regularItems = new List<CustomerRegularItemUpsertDto>();
        if (FormIsRegular)
        {
            foreach (var row in RegularItemRows)
            {
                if (row.SelectedProduct == null || row.SelectedProduct.Id <= 0)
                    continue;

                regularItems.Add(new CustomerRegularItemUpsertDto
                {
                    ProductId = row.SelectedProduct.Id,
                    PackQuantity = row.PackQuantityValue,
                    UnitQuantity = row.UnitQuantityValue,
                    DosageNotes = row.DosageNotes
                });
            }
        }

        IsSaving = true;
        try
        {
            var request = new CustomerUpsertRequest
            {
                Name = trimmedName,
                Phone = FormPhone,
                Email = FormEmail,
                Address = FormAddress,
                TaxStatus = SelectedTaxStatus?.Key ?? "unregistered",
                TaxNumber = FormTaxNumber,
                CreditStatus = SelectedCreditStatus?.Key ?? "active",
                CreditLimit = creditLimit,
                CreditDays = creditDays,
                OpeningBalance = openingBalance,
                IsRegular = FormIsRegular,
                RefillCycle = SelectedRefillCycle?.Key ?? "monthly",
                RefillCustomDays = customDays,
                ReminderLeadDays = leadDays,
                NextRefillDate = FormNextRefillDate,
                ReminderLanguage = SelectedReminderLanguage?.Key ?? "english",
                RegularNotes = FormRegularNotes,
                RegularItems = regularItems
            };

            if (IsEditing && EditingId.HasValue)
            {
                await _customerService.UpdateAsync(EditingId.Value, request);
                _toastService.ShowSuccess("Customer updated", "Customers");
            }
            else
            {
                await _customerService.CreateAsync(request);
                _toastService.ShowSuccess("Customer created", "Customers");
            }

            ResetFormFields();
            ActiveMode = "directory";
            await LoadCustomersAsync();
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Save Failed");
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    public async Task DeleteCustomerAsync(CustomerDto? customer)
    {
        if (customer == null)
            return;

        if (!CanDelete)
        {
            _toastService.ShowError("You don't have permission to delete customers.", "Access Denied");
            return;
        }

        if (!customer.CanDelete)
        {
            _toastService.ShowError(customer.DeleteTooltip, "Cannot Delete Customer");
            return;
        }

        var confirm = MessageBox.Show(
            $"Delete customer \"{customer.Name}\"?",
            "Delete Customer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            await _customerService.DeleteAsync(customer.Id);
            _toastService.ShowSuccess("Customer deleted", "Customers");
            await LoadCustomersAsync();
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Delete Failed");
        }
    }

    // ------------------------------------------------------------------------
    // Regular Refill Reminder Modal Commands
    // ------------------------------------------------------------------------
    [RelayCommand]
    public async Task OpenReminderModalAsync(CustomerDto? customer)
    {
        if (customer == null)
            return;

        IsReminderModalOpen = true;
        IsLoadingReminderPreview = true;
        ReminderPreview = null;

        try
        {
            var preview = await _customerService.GetRefillReminderPreviewAsync(customer.Id);
            ReminderPreview = preview;
            ReminderPhone = preview.Phone;
            ReminderSavePhone = string.IsNullOrWhiteSpace(preview.Phone);
            ReminderAdvanceCycle = false;

            string prefLang = string.Equals(customer.ReminderLanguage, "urdu", StringComparison.OrdinalIgnoreCase)
                ? "urdu"
                : "english";
            ReminderLanguage = prefLang;
            ReminderMessage = prefLang == "urdu" ? preview.UrduMessage : preview.EnglishMessage;
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to load reminder preview: {ex.Message}", "Reminder Error");
            IsReminderModalOpen = false;
        }
        finally
        {
            IsLoadingReminderPreview = false;
        }
    }

    [RelayCommand]
    public void CloseReminderModal()
    {
        IsReminderModalOpen = false;
        ReminderPreview = null;
    }

    [RelayCommand]
    public void SetReminderLanguageEnglish()
    {
        ReminderLanguage = "english";
        if (ReminderPreview != null)
            ReminderMessage = ReminderPreview.EnglishMessage;
    }

    [RelayCommand]
    public void SetReminderLanguageUrdu()
    {
        ReminderLanguage = "urdu";
        if (ReminderPreview != null)
            ReminderMessage = ReminderPreview.UrduMessage;
    }

    [RelayCommand]
    public void ResetReminderTemplate()
    {
        if (ReminderPreview == null)
            return;
        ReminderMessage = IsReminderUrdu ? ReminderPreview.UrduMessage : ReminderPreview.EnglishMessage;
    }

    [RelayCommand]
    public void CopyReminderMessage()
    {
        if (string.IsNullOrWhiteSpace(ReminderMessage))
            return;

        try
        {
            Clipboard.SetText(ReminderMessage);
            _toastService.ShowSuccess("Reminder message copied to clipboard.", "Copied");
        }
        catch
        {
            _toastService.ShowError("Could not copy message to clipboard.", "Copy Error");
        }
    }

    [RelayCommand]
    public async Task SendViaWhatsAppAsync()
    {
        if (IsSendingReminder || ReminderPreview == null)
            return;

        string normalized = CustomerService.NormalizeWhatsAppPhone(ReminderPhone);
        if (string.IsNullOrEmpty(normalized))
        {
            _toastService.ShowError("Please enter a valid WhatsApp phone number (e.g., 03001234567).", "Invalid Phone");
            return;
        }

        IsSendingReminder = true;
        try
        {
            var (waUrl, updated) = await _customerService.RecordRefillReminderSentAsync(
                ReminderPreview.CustomerId,
                ReminderPhone,
                ReminderLanguage,
                ReminderMessage,
                ReminderSavePhone,
                ReminderAdvanceCycle);

            try
            {
                Process.Start(new ProcessStartInfo(waUrl) { UseShellExecute = true });
            }
            catch
            {
                // Fallback if running in headless test
            }

            _toastService.ShowSuccess($"Opening WhatsApp reminder for {updated.Name}...", "WhatsApp Reminder");
            IsReminderModalOpen = false;
            ReminderPreview = null;

            if (IsDetailMode && SelectedDetailCustomer?.Id == updated.Id)
            {
                SelectedDetailCustomer = updated;
            }
            await LoadCustomersAsync();
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Reminder Failed");
        }
        finally
        {
            IsSendingReminder = false;
        }
    }

    // ------------------------------------------------------------------------
    // Credit Summary Modal Commands
    // ------------------------------------------------------------------------
    [RelayCommand]
    public async Task OpenCreditSummaryAsync(CustomerDto? customer)
    {
        if (customer == null)
            return;

        IsCreditModalOpen = true;
        IsLoadingCreditSummary = true;
        CreditSummary = null;

        try
        {
            CreditSummary = await _customerService.GetCreditSummaryAsync(customer.Id);
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to load credit summary: {ex.Message}", "Credit Summary Error");
            IsCreditModalOpen = false;
        }
        finally
        {
            IsLoadingCreditSummary = false;
        }
    }

    [RelayCommand]
    public void CloseCreditModal()
    {
        IsCreditModalOpen = false;
        CreditSummary = null;
    }

    // ------------------------------------------------------------------------
    // Custom Wholesale Price List Modal Commands
    // ------------------------------------------------------------------------
    [RelayCommand]
    public async Task OpenWholesalePriceModalAsync(CustomerDto? customer)
    {
        if (customer == null)
            return;

        PriceModalCustomer = customer;
        SelectedPriceProduct = null;
        CustomPackPriceText = string.Empty;
        CustomerWholeSalePrices.Clear();
        IsPriceModalOpen = true;
        IsLoadingPrices = true;

        try
        {
            await EnsureCatalogProductsLoadedAsync();
            var prices = await _customerService.GetWholeSalePricesAsync(customer.Id);
            CustomerWholeSalePrices.Clear();
            foreach (var p in prices)
                CustomerWholeSalePrices.Add(p);
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to load custom wholesale prices: {ex.Message}", "Load Error");
        }
        finally
        {
            IsLoadingPrices = false;
            OnPropertyChanged(nameof(HasCustomerWholeSalePrices));
            OnPropertyChanged(nameof(HasNoCustomerWholeSalePrices));
        }
    }

    [RelayCommand]
    public void CloseWholesalePriceModal()
    {
        IsPriceModalOpen = false;
        PriceModalCustomer = null;
        SelectedPriceProduct = null;
        CustomPackPriceText = string.Empty;
        CustomerWholeSalePrices.Clear();
    }

    [RelayCommand]
    public async Task SaveCustomWholesalePriceAsync()
    {
        if (IsSavingPrice || PriceModalCustomer == null)
            return;

        if (SelectedPriceProduct == null || SelectedPriceProduct.Id <= 0)
        {
            _toastService.ShowError("Please select a product.", "Validation Error");
            return;
        }

        if (!decimal.TryParse(CustomPackPriceText?.Replace(",", string.Empty).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal packPrice) || packPrice < 0m)
        {
            _toastService.ShowError("Please enter a valid custom pack price.", "Validation Error");
            return;
        }

        IsSavingPrice = true;
        try
        {
            await _customerService.SaveWholeSalePriceAsync(PriceModalCustomer.Id, SelectedPriceProduct.Id, packPrice);
            _toastService.ShowSuccess("Custom wholesale pack price saved.", "Wholesale Price List");

            SelectedPriceProduct = null;
            CustomPackPriceText = string.Empty;

            var prices = await _customerService.GetWholeSalePricesAsync(PriceModalCustomer.Id);
            CustomerWholeSalePrices.Clear();
            foreach (var p in prices)
                CustomerWholeSalePrices.Add(p);

            OnPropertyChanged(nameof(HasCustomerWholeSalePrices));
            OnPropertyChanged(nameof(HasNoCustomerWholeSalePrices));
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Save Price Failed");
        }
        finally
        {
            IsSavingPrice = false;
        }
    }

    [RelayCommand]
    public void EditCustomWholesalePrice(CustomerWholeSalePriceDto? item)
    {
        if (item == null)
            return;

        SelectedPriceProduct = CatalogProducts.FirstOrDefault(p => p.Id == item.ProductId);
        CustomPackPriceText = item.PackPrice.ToString("0.##", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    public async Task DeleteCustomWholesalePriceAsync(CustomerWholeSalePriceDto? item)
    {
        if (item == null || PriceModalCustomer == null)
            return;

        try
        {
            await _customerService.DeleteWholeSalePriceAsync(PriceModalCustomer.Id, item.Id);
            CustomerWholeSalePrices.Remove(item);
            OnPropertyChanged(nameof(HasCustomerWholeSalePrices));
            OnPropertyChanged(nameof(HasNoCustomerWholeSalePrices));
            _toastService.ShowSuccess("Custom price removed.", "Wholesale Price List");
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Delete Failed");
        }
    }

    // ------------------------------------------------------------------------
    // Pagination Commands
    // ------------------------------------------------------------------------
    [RelayCommand]
    public void PrevPage()
    {
        if (Page > 1)
        {
            Page--;
            RecomputeFilteredAndPaged();
        }
    }

    [RelayCommand]
    public void NextPage()
    {
        if (Page < PageCount)
        {
            Page++;
            RecomputeFilteredAndPaged();
        }
    }

    // ------------------------------------------------------------------------
    // Export & CSV Import Commands
    // ------------------------------------------------------------------------
    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        if (!CanExport)
        {
            _toastService.ShowError("You don't have permission to export customers.", "Access Denied");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Customers CSV",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            FileName = $"customers_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsExporting = true;
        try
        {
            await _customerService.ExportCsvAsync(dialog.FileName);
            _toastService.ShowSuccess("Customers exported", "Export Complete");
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Export failed: {ex.Message}", "Export Error");
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    public void OpenImportModal()
    {
        if (!CanImport)
        {
            _toastService.ShowError("You don't have permission to import customers.", "Access Denied");
            return;
        }

        ResetImportModalState();
        IsImportModalOpen = true;
    }

    [RelayCommand]
    public void CloseImportModal()
    {
        IsImportModalOpen = false;
        ResetImportModalState();
    }

    [RelayCommand]
    public void ChooseImportCsvFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Customers CSV File",
            Filter = "CSV / Text Files (*.csv;*.txt)|*.csv;*.txt|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            ImportFilePath = dialog.FileName;
        }
    }

    [RelayCommand]
    public void ResetImportStep()
    {
        ValidationResult = null;
        InvalidSamples.Clear();
        ValidSamples.Clear();
        OnPropertyChanged(nameof(HasInvalidSamples));
        OnPropertyChanged(nameof(HasValidSamples));
    }

    [RelayCommand]
    public async Task DownloadTemplateAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Customers CSV Template",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            FileName = "customers_template.csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _customerService.SaveTemplateCsvAsync(dialog.FileName);
            _toastService.ShowSuccess("Template downloaded", "CSV Template");
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to save template: {ex.Message}", "Template Error");
        }
    }

    [RelayCommand]
    public async Task ValidateImportAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportFilePath))
        {
            _toastService.ShowError("Please choose a CSV file first.", "Validation Error");
            return;
        }

        IsValidatingImport = true;
        try
        {
            string delim = SelectedDelimiter?.Value ?? ",";
            var res = await _customerService.ValidateCsvAsync(ImportFilePath, delim);
            if (!res.Ok)
            {
                _toastService.ShowError(
                    string.IsNullOrWhiteSpace(res.Message) ? "Validation failed." : res.Message,
                    "CSV Validation Failed");
                return;
            }

            InvalidSamples.Clear();
            foreach (var r in res.InvalidSamples)
                InvalidSamples.Add(r);

            ValidSamples.Clear();
            foreach (var r in res.ValidSamples)
                ValidSamples.Add(r);

            ValidationResult = res;
            OnPropertyChanged(nameof(HasInvalidSamples));
            OnPropertyChanged(nameof(HasValidSamples));

            if (res.Invalid > 0)
            {
                _toastService.ShowWarning(
                    $"Found {res.Invalid} invalid row(s) — review before importing.",
                    "Validation Warning");
            }
            else
            {
                _toastService.ShowSuccess(
                    $"All {res.Valid} row(s) are valid.",
                    "Validation Passed");
            }
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Validation failed: {ex.Message}", "Validation Error");
        }
        finally
        {
            IsValidatingImport = false;
        }
    }

    [RelayCommand]
    public async Task CommitImportValidOnlyAsync()
    {
        await ExecuteCommitImportAsync(insertValidOnly: true);
    }

    [RelayCommand]
    public async Task CommitImportAllAsync()
    {
        await ExecuteCommitImportAsync(insertValidOnly: false);
    }

    private async Task ExecuteCommitImportAsync(bool insertValidOnly)
    {
        if (IsCommittingImport || string.IsNullOrWhiteSpace(ImportFilePath))
            return;

        IsCommittingImport = true;
        try
        {
            string delim = SelectedDelimiter?.Value ?? ",";
            var stats = await _customerService.CommitCsvAsync(ImportFilePath, insertValidOnly, delim);
            _toastService.ShowSuccess(
                $"Imported {stats.Committed} customer(s) ({stats.NewCount} new, {stats.ExistingCount} existing).",
                "Import Complete");

            IsImportModalOpen = false;
            ResetImportModalState();
            await LoadCustomersAsync();
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Import failed: {ex.Message}", "Import Error");
        }
        finally
        {
            IsCommittingImport = false;
        }
    }

    // ------------------------------------------------------------------------
    // Private Helpers
    // ------------------------------------------------------------------------
    private async Task EnsureCatalogProductsLoadedAsync()
    {
        var products = await _customerService.SearchProductsForCatalogAsync(null, 250);
        CatalogProducts.Clear();
        foreach (var p in products)
            CatalogProducts.Add(p);
    }

    private void RecalculateDefaultNextRefillDate()
    {
        int customDays = int.TryParse(FormRefillCustomDaysText?.Trim(), out int cd) && cd >= 1 ? cd : 30;
        var next = CustomerService.CalculateAdvancedRefillDate(DateTime.Today, SelectedRefillCycle?.Key, customDays);
        FormNextRefillDate = next.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private void ResetFormFields()
    {
        _suppressRefillDateAutoCalc = true;
        EditingId = null;
        EditingCustomerOriginalName = string.Empty;
        FormName = string.Empty;
        FormPhone = string.Empty;
        FormEmail = string.Empty;
        FormAddress = string.Empty;
        SelectedTaxStatus = TaxStatusOptions[0];
        FormTaxNumber = string.Empty;
        SelectedCreditStatus = CreditStatusOptions[0];
        FormCreditLimitText = string.Empty;
        FormCreditDaysText = "30";
        FormOpeningBalanceText = string.Empty;

        FormIsRegular = false;
        SelectedRefillCycle = RefillCycleOptions.First(r => r.Key == "monthly");
        FormRefillCustomDaysText = "30";
        FormNextRefillDate = string.Empty;
        FormReminderLeadDaysText = "3";
        SelectedReminderLanguage = ReminderLanguageOptions[0];
        FormRegularNotes = string.Empty;
        RegularItemRows.Clear();
        RenumberRegularItemRows();
        _suppressRefillDateAutoCalc = false;
    }

    private void PopulateFormFromCustomer(CustomerDto customer)
    {
        _suppressRefillDateAutoCalc = true;
        EditingId = customer.Id;
        EditingCustomerOriginalName = customer.Name;
        FormName = customer.Name;
        FormPhone = customer.Phone ?? string.Empty;
        FormEmail = customer.Email ?? string.Empty;
        FormAddress = customer.Address ?? string.Empty;
        SelectedTaxStatus = TaxStatusOptions.FirstOrDefault(t =>
            string.Equals(t.Key, customer.TaxStatus, StringComparison.OrdinalIgnoreCase)) ?? TaxStatusOptions[0];
        FormTaxNumber = customer.TaxNumber ?? string.Empty;
        SelectedCreditStatus = CreditStatusOptions.FirstOrDefault(c =>
            string.Equals(c.Key, customer.CreditStatus, StringComparison.OrdinalIgnoreCase)) ?? CreditStatusOptions[0];
        FormCreditLimitText = customer.CreditLimit.HasValue && customer.CreditLimit.Value > 0m
            ? customer.CreditLimit.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;
        FormCreditDaysText = (customer.CreditDays ?? 30).ToString(CultureInfo.InvariantCulture);
        FormOpeningBalanceText = string.Empty;

        FormIsRegular = customer.IsRegular;
        SelectedRefillCycle = RefillCycleOptions.FirstOrDefault(r =>
            string.Equals(r.Key, customer.RefillCycle, StringComparison.OrdinalIgnoreCase)) ?? RefillCycleOptions[2];
        FormRefillCustomDaysText = (customer.RefillCustomDays ?? 30).ToString(CultureInfo.InvariantCulture);
        FormNextRefillDate = customer.NextRefillDate ?? string.Empty;
        FormReminderLeadDaysText = customer.ReminderLeadDays.ToString(CultureInfo.InvariantCulture);
        SelectedReminderLanguage = ReminderLanguageOptions.FirstOrDefault(l =>
            string.Equals(l.Key, customer.ReminderLanguage, StringComparison.OrdinalIgnoreCase)) ?? ReminderLanguageOptions[0];
        FormRegularNotes = customer.RegularNotes ?? string.Empty;

        RegularItemRows.Clear();
        foreach (var item in customer.RegularItems)
        {
            var matchedProd = CatalogProducts.FirstOrDefault(p => p.Id == item.ProductId);
            if (matchedProd == null)
            {
                matchedProd = new CustomerCatalogProductDto
                {
                    Id = item.ProductId,
                    Name = item.ProductName,
                    ProductCode = item.ProductCode,
                    PackSize = item.PackSize,
                    OnHandUnits = item.OnHandUnits,
                    UnitSalePrice = item.UnitSalePrice,
                    PackSalePrice = item.PackSalePrice,
                    WholeSalePackPrice = item.PackSalePrice,
                    WholeSaleUnitPrice = item.UnitSalePrice
                };
                CatalogProducts.Add(matchedProd);
            }

            var row = new CustomerRegularItemFormRowViewModel
            {
                SelectedProduct = matchedProd,
                PackQuantityText = item.PackQuantity.ToString(CultureInfo.InvariantCulture),
                UnitQuantityText = item.UnitQuantity.ToString(CultureInfo.InvariantCulture),
                DosageNotes = item.DosageNotes ?? string.Empty
            };
            row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(RegularItemsCountBadgeText));
            RegularItemRows.Add(row);
        }

        if (FormIsRegular && RegularItemRows.Count == 0)
        {
            AddRegularItemRow();
        }
        else
        {
            RenumberRegularItemRows();
        }

        _suppressRefillDateAutoCalc = false;
        OnPropertyChanged(nameof(IsCustomRefillCycleSelected));
    }

    private void ResetImportModalState()
    {
        ImportFilePath = string.Empty;
        SelectedDelimiter = DelimiterOptions[0];
        ValidationResult = null;
        InvalidSamples.Clear();
        ValidSamples.Clear();
        OnPropertyChanged(nameof(HasInvalidSamples));
        OnPropertyChanged(nameof(HasValidSamples));
    }

    private void RecomputeFilteredAndPaged()
    {
        string q = SearchQuery?.Trim() ?? string.Empty;
        string filterKey = SelectedCustomerFilter?.Key ?? "all";

        IEnumerable<CustomerDto> filtered = _allCustomers;

        if (!string.IsNullOrEmpty(q))
        {
            filtered = filtered.Where(c =>
                c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(c.Phone) && c.Phone.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(c.Email) && c.Email.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(c.Address) && c.Address.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        filtered = filterKey switch
        {
            "regular" => filtered.Where(c => c.IsRegular),
            "due_reminder" => filtered.Where(c => c.IsRegular && c.IsDueForReminder),
            "weekly" => filtered.Where(c => c.IsRegular && string.Equals(c.RefillCycle, "weekly", StringComparison.OrdinalIgnoreCase)),
            "monthly" => filtered.Where(c => c.IsRegular && string.Equals(c.RefillCycle, "monthly", StringComparison.OrdinalIgnoreCase)),
            "non_regular" => filtered.Where(c => !c.IsRegular),
            _ => filtered
        };

        var filteredList = filtered.ToList();
        FilteredCustomersCount = filteredList.Count;

        int safePageSize = PageSize > 0 ? PageSize : 25;
        PageCount = Math.Max(1, (int)Math.Ceiling(filteredList.Count / (double)safePageSize));
        if (Page > PageCount)
            Page = PageCount;
        if (Page < 1)
            Page = 1;

        var slice = filteredList
            .Skip((Page - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();

        PagedCustomers.Clear();
        foreach (var item in slice)
        {
            PagedCustomers.Add(item);
        }

        OnPropertyChanged(nameof(PageStatusText));
        OnPropertyChanged(nameof(CanGoPrevPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        OnPropertyChanged(nameof(IsEmptyList));
    }
}
