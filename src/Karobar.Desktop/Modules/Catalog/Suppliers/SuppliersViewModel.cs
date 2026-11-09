using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;
using Karobar.Infrastructure.Services;
using Microsoft.Win32;

namespace Karobar.Desktop.ViewModels;

public sealed class TaxStatusOption
{
    public string Key { get; init; } = "unregistered";
    public string Label { get; init; } = string.Empty;
}

public partial class SuppliersViewModel : ObservableObject
{
    private readonly ISupplierService _supplierService;
    private readonly IAuthService _authService;
    private readonly IToastNotificationService _toastService;
    private readonly Action<string>? _navigate;
    private readonly List<SupplierDto> _allSuppliers = [];
    private bool _suppressPresetSync;

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

    public bool HasRowActions => CanUpdate || CanDelete;

    // ------------------------------------------------------------------------
    // Page Mode State: Directory vs Create/Edit Form
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private bool _isFormMode;

    [ObservableProperty]
    private long? _editingId;

    [ObservableProperty]
    private string _editingSupplierOriginalName = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isExporting;

    public bool IsDirectoryMode => !IsFormMode;
    public bool IsEditing => EditingId.HasValue;
    public bool IsCreating => !EditingId.HasValue;
    public bool CanExportNow => !IsExporting;

    public string FormPageTitle => IsEditing
        ? $"Edit Supplier — {EditingSupplierOriginalName}"
        : "Add Supplier";

    public string FormPageSubtitle => IsEditing
        ? "Update supplier contact details, credit limits, and Expiry Return Policy"
        : "Create a supplier and configure its Expiry Return Policy";

    public string SaveButtonText => IsEditing
        ? (IsSaving ? "Updating..." : "Update Supplier")
        : (IsSaving ? "Saving..." : "Save Supplier");

    // ------------------------------------------------------------------------
    // Directory Filter & Pagination State
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private BrandPolicyFilterOption _selectedPolicyFilter;

    [ObservableProperty]
    private int _page = 1;

    [ObservableProperty]
    private int _pageSize = 25;

    [ObservableProperty]
    private int _pageCount = 1;

    [ObservableProperty]
    private int _totalSuppliersCount;

    [ObservableProperty]
    private int _filteredSuppliersCount;

    public ObservableCollection<BrandPolicyFilterOption> PolicyFilterOptions { get; } =
    [
        new BrandPolicyFilterOption { Key = "all", Label = "All Expiry Return Policies" },
        new BrandPolicyFilterOption { Key = "before_3", Label = "3 Months Before Expiry" },
        new BrandPolicyFilterOption { Key = "before_6", Label = "6 Months Before Expiry" },
        new BrandPolicyFilterOption { Key = "before_12", Label = "1 Year Before Expiry" },
        new BrandPolicyFilterOption { Key = "before_24", Label = "2 Years Before Expiry" },
        new BrandPolicyFilterOption { Key = "before_expiry", Label = "All Before-Expiry Policies" },
        new BrandPolicyFilterOption { Key = "accepts_expired", Label = "Accepts Expired Products" },
        new BrandPolicyFilterOption { Key = "non_returnable", Label = "Non-Returnable" }
    ];

    public ObservableCollection<int> PageSizeOptions { get; } = [10, 25, 50, 100];
    public ObservableCollection<SupplierDto> PagedSuppliers { get; } = [];

    public string DirectorySubtitleText =>
        $"{TotalSuppliersCount} suppliers configured with expiry return intimation & credit limits";

    public string PageStatusText => $"Page {Page} of {PageCount}";
    public bool CanGoPrevPage => Page > 1;
    public bool CanGoNextPage => Page < PageCount;
    public bool IsEmptyList => PagedSuppliers.Count == 0 && !IsLoading;

    // ------------------------------------------------------------------------
    // Create / Edit Supplier Form State (SupplierForm.jsx + ExpiryReturnPolicyFields.jsx)
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
    private string _formCreditLimitText = string.Empty;

    [ObservableProperty]
    private string _formCreditDaysText = "30";

    [ObservableProperty]
    private string _formOpeningBalanceText = string.Empty;

    [ObservableProperty]
    private ExpiryPolicyPresetOption _selectedPolicyPreset;

    [ObservableProperty]
    private CustomTimeUnitOption _selectedCustomTimeUnit;

    [ObservableProperty]
    private string _customDurationText = "3";

    [ObservableProperty]
    private string _formPolicy = "before_expiry";

    [ObservableProperty]
    private int _formMonths = 3;

    [ObservableProperty]
    private int _formDays = 90;

    [ObservableProperty]
    private string _formNotes = string.Empty;

    public ObservableCollection<TaxStatusOption> TaxStatusOptions { get; } =
    [
        new TaxStatusOption { Key = "unregistered", Label = "Unregistered" },
        new TaxStatusOption { Key = "registered", Label = "Tax Registered (FBR / GST)" },
        new TaxStatusOption { Key = "exempt", Label = "Tax Exempt" }
    ];

    public ObservableCollection<ExpiryPolicyPresetOption> PolicyPresets { get; } =
    [
        new ExpiryPolicyPresetOption { Key = "before_1", Label = "1 Month Before Expiry (30 Days)", Policy = "before_expiry", Months = 1, Days = 30 },
        new ExpiryPolicyPresetOption { Key = "before_2", Label = "2 Months Before Expiry (60 Days)", Policy = "before_expiry", Months = 2, Days = 60 },
        new ExpiryPolicyPresetOption { Key = "before_3", Label = "3 Months Before Expiry (90 Days) — Standard", Policy = "before_expiry", Months = 3, Days = 90 },
        new ExpiryPolicyPresetOption { Key = "before_4", Label = "4 Months Before Expiry (120 Days)", Policy = "before_expiry", Months = 4, Days = 120 },
        new ExpiryPolicyPresetOption { Key = "before_6", Label = "6 Months Before Expiry (180 Days)", Policy = "before_expiry", Months = 6, Days = 180 },
        new ExpiryPolicyPresetOption { Key = "before_9", Label = "9 Months Before Expiry (270 Days)", Policy = "before_expiry", Months = 9, Days = 270 },
        new ExpiryPolicyPresetOption { Key = "before_12", Label = "1 Year Before Expiry (365 Days)", Policy = "before_expiry", Months = 12, Days = 365 },
        new ExpiryPolicyPresetOption { Key = "before_18", Label = "1.5 Years Before Expiry (545 Days)", Policy = "before_expiry", Months = 18, Days = 545 },
        new ExpiryPolicyPresetOption { Key = "before_24", Label = "2 Years Before Expiry (730 Days)", Policy = "before_expiry", Months = 24, Days = 730 },
        new ExpiryPolicyPresetOption { Key = "custom_before_expiry", Label = "Custom Period Before Expiry…", Policy = "before_expiry", Months = null, Days = null },
        new ExpiryPolicyPresetOption { Key = "accepts_expired", Label = "Accepts Expired Products (On or After Expiry)", Policy = "accepts_expired", Months = 0, Days = 0 },
        new ExpiryPolicyPresetOption { Key = "non_returnable", Label = "Non-Returnable (No Expiry Return Accepted)", Policy = "non_returnable", Months = 0, Days = 0 }
    ];

    public ObservableCollection<CustomTimeUnitOption> CustomTimeUnitOptions { get; } =
    [
        new CustomTimeUnitOption { Key = "months", Label = "Months Before Expiry" },
        new CustomTimeUnitOption { Key = "days", Label = "Days Before Expiry" }
    ];

    public ObservableCollection<SupplierBatchIntimationDto> EditingSupplierBatches { get; } = [];

    public bool IsCustomPolicySelected =>
        string.Equals(SelectedPolicyPreset?.Key, "custom_before_expiry", StringComparison.OrdinalIgnoreCase);

    public string CustomDurationFieldLabel =>
        string.Equals(SelectedCustomTimeUnit?.Key, "days", StringComparison.OrdinalIgnoreCase)
            ? "Days Before Expiry"
            : "Months Before Expiry";

    public string CustomDurationComputedHint =>
        $"≈ {FormDays} days prior to batch expiry date";

    public string FormPolicyFormattedLabel =>
        BrandService.FormatExpiryPolicyLabel(FormPolicy, FormMonths, FormDays);

    public string FormPolicyBadgeDisplay => FormPolicy switch
    {
        "accepts_expired" => $"✓ {FormPolicyFormattedLabel}",
        "non_returnable" => $"⊘ {FormPolicyFormattedLabel}",
        _ => $"🕒 {FormPolicyFormattedLabel}"
    };

    public string FormPolicyBadgeBg => FormPolicy switch
    {
        "accepts_expired" => "#ECFDF5",
        "non_returnable" => "#FFF1F2",
        _ => "#EFF6FF"
    };

    public string FormPolicyBadgeFg => FormPolicy switch
    {
        "accepts_expired" => "#047857",
        "non_returnable" => "#BE123C",
        _ => "#1D4ED8"
    };

    public string FormPolicyBadgeBorder => FormPolicy switch
    {
        "accepts_expired" => "#A7F3D0",
        "non_returnable" => "#FECDD3",
        _ => "#BFDBFE"
    };

    public string FormPolicyExplanationText => FormPolicy switch
    {
        "accepts_expired" =>
            "This supplier accepts near-expiry and already-expired stock for return or replacement.",
        "non_returnable" =>
            "This supplier does not accept expiry returns. Near-expiry batches should be prioritized for clearance sale.",
        _ =>
            $"Products from this supplier must be intimated / returned at least {FormPolicyFormattedLabel.ToLowerInvariant()} ({FormDays} days prior to the batch expiry date)."
    };

    public bool HasEditingBatches => EditingSupplierBatches.Count > 0;
    public bool HasNoEditingBatches => EditingSupplierBatches.Count == 0;

    // ------------------------------------------------------------------------
    // Credit Summary Modal State
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private bool _isCreditModalOpen;

    [ObservableProperty]
    private bool _isLoadingCreditSummary;

    [ObservableProperty]
    private SupplierCreditSummaryDto? _creditSummary;

    public bool HasCreditSummary => CreditSummary != null && !IsLoadingCreditSummary;

    // ------------------------------------------------------------------------
    // CSV Import Modal State
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
    private SupplierCsvValidationResult? _validationResult;

    public ObservableCollection<DelimiterOptionModel> DelimiterOptions { get; } =
    [
        new DelimiterOptionModel { Label = "Comma (,)", Value = "," },
        new DelimiterOptionModel { Label = "Semicolon (;)", Value = ";" },
        new DelimiterOptionModel { Label = "Tab (\\t)", Value = "\\t" },
        new DelimiterOptionModel { Label = "Pipe (|)", Value = "|" }
    ];

    public ObservableCollection<SupplierCsvSampleRow> InvalidSamples { get; } = [];
    public ObservableCollection<SupplierCsvSampleRow> ValidSamples { get; } = [];

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

    public SuppliersViewModel(
        ISupplierService supplierService,
        IAuthService authService,
        IToastNotificationService toastService,
        Action<string>? navigate = null)
    {
        _supplierService = supplierService;
        _authService = authService;
        _toastService = toastService;
        _navigate = navigate;

        _selectedPolicyFilter = PolicyFilterOptions[0];
        _selectedTaxStatus = TaxStatusOptions[0];
        _selectedPolicyPreset = PolicyPresets[2];
        _selectedCustomTimeUnit = CustomTimeUnitOptions[0];
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
            CanView = session.Permissions.Contains("supplier.view");
            CanCreate = session.Permissions.Contains("supplier.create");
            CanUpdate = session.Permissions.Contains("supplier.update");
            CanDelete = session.Permissions.Contains("supplier.delete");
            CanImport = session.Permissions.Contains("supplier.import") || session.Permissions.Contains("supplier.create");
            CanExport = session.Permissions.Contains("supplier.export") || session.Permissions.Contains("supplier.view");
        }

        OnPropertyChanged(nameof(HasRowActions));
    }

    partial void OnIsFormModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDirectoryMode));
    }

    partial void OnEditingIdChanged(long? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsCreating));
        OnPropertyChanged(nameof(FormPageTitle));
        OnPropertyChanged(nameof(FormPageSubtitle));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    partial void OnEditingSupplierOriginalNameChanged(string value)
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

    partial void OnSelectedPolicyFilterChanged(BrandPolicyFilterOption value)
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

    partial void OnTotalSuppliersCountChanged(int value)
    {
        OnPropertyChanged(nameof(DirectorySubtitleText));
    }

    partial void OnIsLoadingCreditSummaryChanged(bool value)
    {
        OnPropertyChanged(nameof(HasCreditSummary));
    }

    partial void OnCreditSummaryChanged(SupplierCreditSummaryDto? value)
    {
        OnPropertyChanged(nameof(HasCreditSummary));
    }

    partial void OnSelectedPolicyPresetChanged(ExpiryPolicyPresetOption value)
    {
        OnPropertyChanged(nameof(IsCustomPolicySelected));
        if (_suppressPresetSync || value == null)
            return;

        if (string.Equals(value.Key, "accepts_expired", StringComparison.OrdinalIgnoreCase))
        {
            FormPolicy = "accepts_expired";
            FormMonths = 0;
            FormDays = 0;
        }
        else if (string.Equals(value.Key, "non_returnable", StringComparison.OrdinalIgnoreCase))
        {
            FormPolicy = "non_returnable";
            FormMonths = 0;
            FormDays = 0;
        }
        else if (string.Equals(value.Key, "custom_before_expiry", StringComparison.OrdinalIgnoreCase))
        {
            FormPolicy = "before_expiry";
            if (FormMonths <= 0 && FormDays <= 0)
            {
                FormMonths = 3;
                FormDays = 90;
            }

            _suppressPresetSync = true;
            SelectedCustomTimeUnit = CustomTimeUnitOptions[0];
            CustomDurationText = Math.Max(1, FormMonths).ToString();
            _suppressPresetSync = false;
        }
        else
        {
            FormPolicy = "before_expiry";
            FormMonths = value.Months ?? 3;
            FormDays = value.Days ?? (FormMonths * 30);
        }

        NotifyPolicyPreviewPropertiesChanged();
    }

    partial void OnSelectedCustomTimeUnitChanged(CustomTimeUnitOption value)
    {
        OnPropertyChanged(nameof(CustomDurationFieldLabel));
        if (_suppressPresetSync || value == null)
            return;

        _suppressPresetSync = true;
        if (string.Equals(value.Key, "days", StringComparison.OrdinalIgnoreCase))
        {
            int days = FormDays > 0 ? FormDays : Math.Max(1, FormMonths * 30);
            FormDays = days;
            CustomDurationText = days.ToString();
        }
        else
        {
            int months = FormMonths > 0 ? FormMonths : Math.Max(1, (int)Math.Round(FormDays / 30.0));
            FormMonths = months;
            FormDays = months * 30;
            CustomDurationText = months.ToString();
        }
        _suppressPresetSync = false;

        NotifyPolicyPreviewPropertiesChanged();
    }

    partial void OnCustomDurationTextChanged(string value)
    {
        if (_suppressPresetSync)
            return;

        if (!int.TryParse(value?.Trim(), out int num) || num < 0)
            num = 0;

        if (string.Equals(SelectedCustomTimeUnit?.Key, "days", StringComparison.OrdinalIgnoreCase))
        {
            FormDays = num;
            FormMonths = num > 0 && num % 30 == 0 ? num / 30 : 0;
        }
        else
        {
            FormMonths = num;
            FormDays = num * 30;
        }

        NotifyPolicyPreviewPropertiesChanged();
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

    partial void OnValidationResultChanged(SupplierCsvValidationResult? value)
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

    private void NotifyPolicyPreviewPropertiesChanged()
    {
        OnPropertyChanged(nameof(CustomDurationComputedHint));
        OnPropertyChanged(nameof(FormPolicyFormattedLabel));
        OnPropertyChanged(nameof(FormPolicyBadgeDisplay));
        OnPropertyChanged(nameof(FormPolicyBadgeBg));
        OnPropertyChanged(nameof(FormPolicyBadgeFg));
        OnPropertyChanged(nameof(FormPolicyBadgeBorder));
        OnPropertyChanged(nameof(FormPolicyExplanationText));
    }

    // ------------------------------------------------------------------------
    // Navigation & Mode Switching Commands
    // ------------------------------------------------------------------------
    public async Task OpenDirectoryModeAsync()
    {
        IsFormMode = false;
        await LoadSuppliersAsync();
    }

    [RelayCommand]
    public async Task LoadSuppliersAsync()
    {
        RefreshPermissions();
        if (!CanView)
        {
            _toastService.ShowError("You don't have permission to view suppliers.", "Access Denied");
            return;
        }

        IsLoading = true;
        try
        {
            var list = await _supplierService.GetAllAsync();
            _allSuppliers.Clear();
            _allSuppliers.AddRange(list);
            TotalSuppliersCount = _allSuppliers.Count;
            RecomputeFilteredAndPaged();
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to load suppliers: {ex.Message}", "Load Error");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmptyList));
        }
    }

    [RelayCommand]
    public void OpenCreateMode()
    {
        RefreshPermissions();
        if (!CanCreate)
        {
            _toastService.ShowError("You don't have permission to create suppliers.", "Access Denied");
            return;
        }

        ResetFormFields();
        IsFormMode = true;
    }

    [RelayCommand]
    public async Task EditSupplierAsync(SupplierDto? supplier)
    {
        if (supplier == null)
            return;

        RefreshPermissions();
        if (!CanUpdate)
        {
            _toastService.ShowError("You don't have permission to edit suppliers.", "Access Denied");
            return;
        }

        try
        {
            var detailed = await _supplierService.GetByIdAsync(supplier.Id) ?? supplier;
            PopulateFormFromSupplier(detailed);
            IsFormMode = true;
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to load supplier details: {ex.Message}", "Load Error");
        }
    }

    [RelayCommand]
    public async Task BackToDirectoryAsync()
    {
        ResetFormFields();
        IsFormMode = false;
        await LoadSuppliersAsync();
    }

    [RelayCommand]
    public async Task SaveSupplierAsync()
    {
        if (IsSaving)
            return;

        if (IsEditing ? !CanUpdate : !CanCreate)
        {
            _toastService.ShowError("You don't have permission to save suppliers.", "Access Denied");
            return;
        }

        string trimmedName = FormName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            _toastService.ShowError("Supplier name is required.", "Validation Error");
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

        IsSaving = true;
        try
        {
            var request = new SupplierUpsertRequest
            {
                Name = trimmedName,
                Phone = FormPhone,
                Email = FormEmail,
                Address = FormAddress,
                TaxStatus = SelectedTaxStatus?.Key ?? "unregistered",
                TaxNumber = FormTaxNumber,
                CreditLimit = creditLimit,
                CreditDays = creditDays,
                OpeningBalance = openingBalance,
                ExpiryReturnPolicy = FormPolicy,
                ExpiryReturnMonths = FormMonths,
                ExpiryReturnDays = FormDays,
                ExpiryReturnNotes = FormNotes
            };

            if (IsEditing && EditingId.HasValue)
            {
                await _supplierService.UpdateAsync(EditingId.Value, request);
                _toastService.ShowSuccess("Supplier updated", "Suppliers");
            }
            else
            {
                await _supplierService.CreateAsync(request);
                _toastService.ShowSuccess("Supplier created", "Suppliers");
            }

            ResetFormFields();
            IsFormMode = false;
            await LoadSuppliersAsync();
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
    public async Task DeleteSupplierAsync(SupplierDto? supplier)
    {
        if (supplier == null)
            return;

        if (!CanDelete)
        {
            _toastService.ShowError("You don't have permission to delete suppliers.", "Access Denied");
            return;
        }

        if (supplier.IsUsedByProducts)
        {
            _toastService.ShowError(
                $"Cannot delete \"{supplier.Name}\" because it is used by {supplier.ProductsCount} product(s).",
                "Cannot Delete Supplier");
            return;
        }

        var confirm = MessageBox.Show(
            $"Delete supplier \"{supplier.Name}\"?",
            "Delete Supplier",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            await _supplierService.DeleteAsync(supplier.Id);
            _toastService.ShowSuccess("Supplier deleted", "Suppliers");
            await LoadSuppliersAsync();
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Delete Failed");
        }
    }

    // ------------------------------------------------------------------------
    // Credit Summary Modal Commands
    // ------------------------------------------------------------------------
    [RelayCommand]
    public async Task OpenCreditSummaryAsync(SupplierDto? supplier)
    {
        if (supplier == null)
            return;

        IsCreditModalOpen = true;
        IsLoadingCreditSummary = true;
        CreditSummary = null;

        try
        {
            CreditSummary = await _supplierService.GetCreditSummaryAsync(supplier.Id);
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
            _toastService.ShowError("You don't have permission to export suppliers.", "Access Denied");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Suppliers CSV",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            FileName = $"suppliers_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsExporting = true;
        try
        {
            await _supplierService.ExportCsvAsync(dialog.FileName);
            _toastService.ShowSuccess("Suppliers exported", "Export Complete");
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
            _toastService.ShowError("You don't have permission to import suppliers.", "Access Denied");
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
            Title = "Select Suppliers CSV File",
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
            Title = "Save Suppliers CSV Template",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            FileName = "suppliers_template.csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _supplierService.SaveTemplateCsvAsync(dialog.FileName);
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
            var res = await _supplierService.ValidateCsvAsync(ImportFilePath, delim);
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
            var stats = await _supplierService.CommitCsvAsync(ImportFilePath, insertValidOnly, delim);
            _toastService.ShowSuccess(
                $"Imported {stats.Committed} supplier(s) ({stats.NewCount} new, {stats.ExistingCount} existing).",
                "Import Complete");

            IsImportModalOpen = false;
            ResetImportModalState();
            await LoadSuppliersAsync();
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
    private void ResetFormFields()
    {
        EditingId = null;
        EditingSupplierOriginalName = string.Empty;
        FormName = string.Empty;
        FormPhone = string.Empty;
        FormEmail = string.Empty;
        FormAddress = string.Empty;
        SelectedTaxStatus = TaxStatusOptions[0];
        FormTaxNumber = string.Empty;
        FormCreditLimitText = string.Empty;
        FormCreditDaysText = "30";
        FormOpeningBalanceText = string.Empty;
        FormNotes = string.Empty;
        EditingSupplierBatches.Clear();
        OnPropertyChanged(nameof(HasEditingBatches));
        OnPropertyChanged(nameof(HasNoEditingBatches));

        _suppressPresetSync = true;
        FormPolicy = "before_expiry";
        FormMonths = 3;
        FormDays = 90;
        SelectedPolicyPreset = PolicyPresets.First(p => p.Key == "before_3");
        SelectedCustomTimeUnit = CustomTimeUnitOptions[0];
        CustomDurationText = "3";
        _suppressPresetSync = false;

        NotifyPolicyPreviewPropertiesChanged();
    }

    private void PopulateFormFromSupplier(SupplierDto supplier)
    {
        EditingId = supplier.Id;
        EditingSupplierOriginalName = supplier.Name;
        FormName = supplier.Name;
        FormPhone = supplier.Phone ?? string.Empty;
        FormEmail = supplier.Email ?? string.Empty;
        FormAddress = supplier.Address ?? string.Empty;
        SelectedTaxStatus = TaxStatusOptions.FirstOrDefault(t =>
            string.Equals(t.Key, supplier.TaxStatus, StringComparison.OrdinalIgnoreCase)) ?? TaxStatusOptions[0];
        FormTaxNumber = supplier.TaxNumber ?? string.Empty;
        FormCreditLimitText = supplier.CreditLimit.HasValue && supplier.CreditLimit.Value > 0m
            ? supplier.CreditLimit.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;
        FormCreditDaysText = (supplier.CreditDays ?? 30).ToString(CultureInfo.InvariantCulture);
        FormOpeningBalanceText = string.Empty;
        FormNotes = supplier.ExpiryReturnNotes ?? string.Empty;

        EditingSupplierBatches.Clear();
        foreach (var b in supplier.IntimationBatches)
            EditingSupplierBatches.Add(b);

        OnPropertyChanged(nameof(HasEditingBatches));
        OnPropertyChanged(nameof(HasNoEditingBatches));

        string policy = string.IsNullOrWhiteSpace(supplier.ExpiryReturnPolicy) ? "before_expiry" : supplier.ExpiryReturnPolicy;
        int months = supplier.ExpiryReturnMonths;
        int days = supplier.ExpiryReturnDays;

        _suppressPresetSync = true;
        FormPolicy = policy;
        FormMonths = months;
        FormDays = days;

        if (policy == "accepts_expired")
        {
            SelectedPolicyPreset = PolicyPresets.First(p => p.Key == "accepts_expired");
        }
        else if (policy == "non_returnable")
        {
            SelectedPolicyPreset = PolicyPresets.First(p => p.Key == "non_returnable");
        }
        else
        {
            var matchedPreset = PolicyPresets.FirstOrDefault(p =>
                p.Policy == "before_expiry" &&
                p.Months.HasValue &&
                p.Months.Value == months);

            if (matchedPreset != null)
            {
                SelectedPolicyPreset = matchedPreset;
            }
            else
            {
                SelectedPolicyPreset = PolicyPresets.First(p => p.Key == "custom_before_expiry");
                if (months <= 0 && days > 0)
                {
                    SelectedCustomTimeUnit = CustomTimeUnitOptions.First(u => u.Key == "days");
                    CustomDurationText = days.ToString();
                }
                else
                {
                    SelectedCustomTimeUnit = CustomTimeUnitOptions.First(u => u.Key == "months");
                    CustomDurationText = Math.Max(1, months).ToString();
                }
            }
        }
        _suppressPresetSync = false;

        OnPropertyChanged(nameof(IsCustomPolicySelected));
        OnPropertyChanged(nameof(CustomDurationFieldLabel));
        NotifyPolicyPreviewPropertiesChanged();
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
        string filterKey = SelectedPolicyFilter?.Key ?? "all";

        IEnumerable<SupplierDto> filtered = _allSuppliers;

        if (!string.IsNullOrEmpty(q))
        {
            filtered = filtered.Where(s =>
                s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(s.Phone) && s.Phone.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(s.Address) && s.Address.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.Equals(filterKey, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (filterKey is "before_expiry" or "accepts_expired" or "non_returnable")
            {
                filtered = filtered.Where(s =>
                    string.Equals(s.ExpiryReturnPolicy, filterKey, StringComparison.OrdinalIgnoreCase));
            }
            else if (filterKey.StartsWith("before_", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(filterKey["before_".Length..], out int targetMonths))
            {
                filtered = filtered.Where(s =>
                    string.Equals(s.ExpiryReturnPolicy, "before_expiry", StringComparison.OrdinalIgnoreCase) &&
                    s.ExpiryReturnMonths == targetMonths);
            }
        }

        var filteredList = filtered.ToList();
        FilteredSuppliersCount = filteredList.Count;

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

        PagedSuppliers.Clear();
        foreach (var item in slice)
        {
            PagedSuppliers.Add(item);
        }

        OnPropertyChanged(nameof(PageStatusText));
        OnPropertyChanged(nameof(CanGoPrevPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        OnPropertyChanged(nameof(IsEmptyList));
    }
}
