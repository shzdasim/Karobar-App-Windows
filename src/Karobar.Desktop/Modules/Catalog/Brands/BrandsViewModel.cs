using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;
using Karobar.Infrastructure.Services;
using Microsoft.Win32;

namespace Karobar.Desktop.ViewModels;

public sealed class BrandPolicyFilterOption
{
    public string Key { get; init; } = "all";
    public string Label { get; init; } = string.Empty;
}

public sealed class ExpiryPolicyPresetOption
{
    public string Key { get; init; } = "before_3";
    public string Label { get; init; } = string.Empty;
    public string Policy { get; init; } = "before_expiry";
    public int? Months { get; init; }
    public int? Days { get; init; }
}

public sealed class CustomTimeUnitOption
{
    public string Key { get; init; } = "months";
    public string Label { get; init; } = string.Empty;
}

public partial class BrandsViewModel : ObservableObject
{
    private readonly IBrandService _brandService;
    private readonly IAuthService _authService;
    private readonly IToastNotificationService _toastService;
    private readonly Action<string>? _navigate;
    private readonly List<BrandDto> _allBrands = [];
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
    private string _editingBrandOriginalName = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isExporting;

    public bool IsDirectoryMode => !IsFormMode;
    public bool IsEditing => EditingId.HasValue;
    public bool CanExportNow => !IsExporting;

    public string FormPageTitle => IsEditing
        ? $"Edit Brand — {EditingBrandOriginalName}"
        : "Add Brand";

    public string FormPageSubtitle => IsEditing
        ? "Update brand details and Expiry Return Policy"
        : "Create a brand and configure its Expiry Return Policy";

    public string SaveButtonText => IsEditing
        ? (IsSaving ? "Updating..." : "Update Brand")
        : (IsSaving ? "Saving..." : "Save Brand");

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
    private int _totalBrandsCount;

    [ObservableProperty]
    private int _filteredBrandsCount;

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
    public ObservableCollection<BrandDto> PagedBrands { get; } = [];

    public string DirectorySubtitleText =>
        $"{TotalBrandsCount} brands configured with expiry return intimation rules";

    public string PageStatusText => $"Page {Page} of {PageCount}";
    public bool CanGoPrevPage => Page > 1;
    public bool CanGoNextPage => Page < PageCount;
    public bool IsEmptyList => PagedBrands.Count == 0 && !IsLoading;

    // ------------------------------------------------------------------------
    // Create / Edit Brand Form State (BrandForm.jsx + ExpiryReturnPolicyFields.jsx)
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private string? _formSourceImagePath;

    [ObservableProperty]
    private string? _formExistingImagePath;

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

    public ObservableCollection<BrandBatchIntimationDto> EditingBrandBatches { get; } = [];

    public string? ActivePreviewImagePath => !string.IsNullOrWhiteSpace(FormSourceImagePath) && File.Exists(FormSourceImagePath)
        ? FormSourceImagePath
        : (!string.IsNullOrWhiteSpace(FormExistingImagePath) && File.Exists(FormExistingImagePath)
            ? FormExistingImagePath
            : null);

    public bool HasActivePreviewImage => !string.IsNullOrWhiteSpace(ActivePreviewImagePath);
    public bool HasNoActivePreviewImage => !HasActivePreviewImage;

    public string FormImageFileNameDisplay => !string.IsNullOrWhiteSpace(FormSourceImagePath)
        ? Path.GetFileName(FormSourceImagePath)
        : (!string.IsNullOrWhiteSpace(FormExistingImagePath)
            ? Path.GetFileName(FormExistingImagePath)
            : "No file chosen");

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
            "This brand/company accepts near-expiry and already-expired stock for return or replacement.",
        "non_returnable" =>
            "This brand/company does not accept expiry returns. Near-expiry batches should be prioritized for clearance sale.",
        _ =>
            $"Products of this brand must be intimated / returned to the supplier at least {FormPolicyFormattedLabel.ToLowerInvariant()} ({FormDays} days prior to the batch expiry date)."
    };

    public bool HasEditingBatches => EditingBrandBatches.Count > 0;
    public bool HasNoEditingBatches => EditingBrandBatches.Count == 0;

    // ------------------------------------------------------------------------
    // CSV Import Modal State (BrandImportModal.jsx)
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
    private BrandCsvValidationResult? _validationResult;

    public ObservableCollection<DelimiterOptionModel> DelimiterOptions { get; } =
    [
        new DelimiterOptionModel { Label = "Comma (,)", Value = "," },
        new DelimiterOptionModel { Label = "Semicolon (;)", Value = ";" },
        new DelimiterOptionModel { Label = "Tab (\\t)", Value = "\\t" },
        new DelimiterOptionModel { Label = "Pipe (|)", Value = "|" }
    ];

    public ObservableCollection<BrandCsvSampleRow> InvalidSamples { get; } = [];
    public ObservableCollection<BrandCsvSampleRow> ValidSamples { get; } = [];

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

    public BrandsViewModel(
        IBrandService brandService,
        IAuthService authService,
        IToastNotificationService toastService,
        Action<string>? navigate = null)
    {
        _brandService = brandService;
        _authService = authService;
        _toastService = toastService;
        _navigate = navigate;

        _selectedPolicyFilter = PolicyFilterOptions[0];
        _selectedPolicyPreset = PolicyPresets[2]; // 3 Months Before Expiry (90 Days) — Standard
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
            CanView = session.Permissions.Contains("brand.view");
            CanCreate = session.Permissions.Contains("brand.create");
            CanUpdate = session.Permissions.Contains("brand.update");
            CanDelete = session.Permissions.Contains("brand.delete");
            CanImport = session.Permissions.Contains("brand.import") || session.Permissions.Contains("brand.create");
            CanExport = session.Permissions.Contains("brand.export") || session.Permissions.Contains("brand.view");
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
        OnPropertyChanged(nameof(FormPageTitle));
        OnPropertyChanged(nameof(FormPageSubtitle));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    partial void OnEditingBrandOriginalNameChanged(string value)
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

    partial void OnTotalBrandsCountChanged(int value)
    {
        OnPropertyChanged(nameof(DirectorySubtitleText));
    }

    partial void OnFormSourceImagePathChanged(string? value)
    {
        OnPropertyChanged(nameof(ActivePreviewImagePath));
        OnPropertyChanged(nameof(HasActivePreviewImage));
        OnPropertyChanged(nameof(HasNoActivePreviewImage));
        OnPropertyChanged(nameof(FormImageFileNameDisplay));
    }

    partial void OnFormExistingImagePathChanged(string? value)
    {
        OnPropertyChanged(nameof(ActivePreviewImagePath));
        OnPropertyChanged(nameof(HasActivePreviewImage));
        OnPropertyChanged(nameof(HasNoActivePreviewImage));
        OnPropertyChanged(nameof(FormImageFileNameDisplay));
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

    partial void OnValidationResultChanged(BrandCsvValidationResult? value)
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
        await LoadBrandsAsync();
    }

    [RelayCommand]
    public async Task LoadBrandsAsync()
    {
        RefreshPermissions();
        if (!CanView)
        {
            _toastService.ShowError("You don't have permission to view brands.", "Access Denied");
            return;
        }

        IsLoading = true;
        try
        {
            var list = await _brandService.GetAllAsync();
            _allBrands.Clear();
            _allBrands.AddRange(list);
            TotalBrandsCount = _allBrands.Count;
            RecomputeFilteredAndPaged();
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to load brands: {ex.Message}", "Load Error");
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
            _toastService.ShowError("You don't have permission to create brands.", "Access Denied");
            return;
        }

        ResetFormFields();
        IsFormMode = true;
    }

    [RelayCommand]
    public async Task EditBrandAsync(BrandDto? brand)
    {
        if (brand == null)
            return;

        RefreshPermissions();
        if (!CanUpdate)
        {
            _toastService.ShowError("You don't have permission to edit brands.", "Access Denied");
            return;
        }

        try
        {
            var detailed = await _brandService.GetByIdAsync(brand.Id) ?? brand;
            PopulateFormFromBrand(detailed);
            IsFormMode = true;
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to load brand details: {ex.Message}", "Load Error");
        }
    }

    [RelayCommand]
    public async Task BackToDirectoryAsync()
    {
        ResetFormFields();
        IsFormMode = false;
        await LoadBrandsAsync();
    }

    [RelayCommand]
    public void ChooseBrandImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose Brand Logo / Image",
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            FormSourceImagePath = dialog.FileName;
        }
    }

    [RelayCommand]
    public void ClearChosenBrandImage()
    {
        FormSourceImagePath = null;
    }

    [RelayCommand]
    public async Task SaveBrandAsync()
    {
        if (IsSaving)
            return;

        if (IsEditing ? !CanUpdate : !CanCreate)
        {
            _toastService.ShowError("You don't have permission to save brands.", "Access Denied");
            return;
        }

        string trimmedName = FormName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedName))
        {
            _toastService.ShowError("Brand name is required.", "Validation Error");
            return;
        }

        IsSaving = true;
        try
        {
            var request = new BrandUpsertRequest
            {
                Name = trimmedName,
                SourceImageFilePath = FormSourceImagePath,
                ExpiryReturnPolicy = FormPolicy,
                ExpiryReturnMonths = FormMonths,
                ExpiryReturnDays = FormDays,
                ExpiryReturnNotes = string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim()
            };

            if (EditingId.HasValue)
            {
                await _brandService.UpdateAsync(EditingId.Value, request);
                _toastService.ShowSuccess("Brand updated", "Updated");
            }
            else
            {
                await _brandService.CreateAsync(request);
                _toastService.ShowSuccess("Brand created", "Created");
            }

            ResetFormFields();
            IsFormMode = false;
            await LoadBrandsAsync();
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
    public async Task DeleteBrandAsync(BrandDto? brand)
    {
        if (brand == null)
            return;

        if (!CanDelete)
        {
            _toastService.ShowError("You don't have permission to delete brands.", "Access Denied");
            return;
        }

        if (brand.IsUsedByProducts)
        {
            _toastService.ShowError("Cannot delete: brand is used by one or more products.", "Delete Blocked");
            return;
        }

        try
        {
            await _brandService.DeleteAsync(brand.Id);
            await LoadBrandsAsync();
            _toastService.ShowSuccess("Brand deleted", "Deleted");
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Delete Failed");
        }
    }

    [RelayCommand]
    public void ManageBatch(BrandBatchIntimationDto? batch)
    {
        if (batch != null)
        {
            _navigate?.Invoke($"/product-batches?product_id={batch.ProductId}&batch_id={batch.BatchId}");
            _toastService.ShowInfo(
                $"Opened Product Batches for {batch.ProductName} (Batch #{batch.BatchNumber}).",
                "Manage Batch");
        }
        else
        {
            _navigate?.Invoke("/product-batches");
        }
    }

    // ------------------------------------------------------------------------
    // Pagination Commands
    // ------------------------------------------------------------------------
    [RelayCommand]
    public void FirstPage()
    {
        if (Page > 1)
        {
            Page = 1;
            RecomputeFilteredAndPaged();
        }
    }

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

    [RelayCommand]
    public void LastPage()
    {
        if (Page < PageCount)
        {
            Page = PageCount;
            RecomputeFilteredAndPaged();
        }
    }

    // ------------------------------------------------------------------------
    // CSV Export & Import Commands
    // ------------------------------------------------------------------------
    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        if (!CanExport)
        {
            _toastService.ShowError("You don't have permission to export brands.", "Access Denied");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Brands to CSV",
            FileName = $"brands_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv",
            DefaultExt = ".csv",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsExporting = true;
        try
        {
            await _brandService.ExportCsvAsync(dialog.FileName);
            _toastService.ShowSuccess(
                $"Exported {_allBrands.Count} brands to {Path.GetFileName(dialog.FileName)}",
                "CSV Exported");
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
            _toastService.ShowError("You don't have permission to import brands.", "Access Denied");
            return;
        }

        ResetImportState();
        IsImportModalOpen = true;
    }

    [RelayCommand]
    public void CloseImportModal()
    {
        ResetImportState();
        IsImportModalOpen = false;
    }

    [RelayCommand]
    public void StartOverImport()
    {
        ValidationResult = null;
        InvalidSamples.Clear();
        ValidSamples.Clear();
        OnPropertyChanged(nameof(HasInvalidSamples));
        OnPropertyChanged(nameof(HasValidSamples));
    }

    [RelayCommand]
    public void ChooseImportFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose Brands CSV File",
            Filter = "CSV Files (*.csv;*.txt)|*.csv;*.txt|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            ImportFilePath = dialog.FileName;
        }
    }

    [RelayCommand]
    public async Task DownloadTemplateAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Brands CSV Template",
            FileName = "brands_template.csv",
            DefaultExt = ".csv",
            Filter = "CSV Files (*.csv)|*.csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _brandService.SaveTemplateCsvAsync(dialog.FileName);
            _toastService.ShowSuccess(
                $"Saved CSV template to {Path.GetFileName(dialog.FileName)}",
                "Template Saved");
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Could not save template: {ex.Message}", "Template Error");
        }
    }

    [RelayCommand]
    public async Task ValidateImportCsvAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportFilePath) || IsValidatingImport)
            return;

        IsValidatingImport = true;
        try
        {
            string delim = SelectedDelimiter?.Value ?? ",";
            var res = await _brandService.ValidateCsvAsync(ImportFilePath, delim);
            if (!res.Ok)
            {
                _toastService.ShowError(res.Message, "CSV Validation Failed");
                return;
            }

            InvalidSamples.Clear();
            foreach (var s in res.InvalidSamples)
                InvalidSamples.Add(s);

            ValidSamples.Clear();
            foreach (var s in res.ValidSamples)
                ValidSamples.Add(s);

            ValidationResult = res;
            OnPropertyChanged(nameof(HasInvalidSamples));
            OnPropertyChanged(nameof(HasValidSamples));

            if (res.Invalid > 0)
            {
                _toastService.ShowWarning(res.Message, "CSV Validated with Issues");
            }
            else
            {
                _toastService.ShowSuccess(res.Message, "CSV Validated");
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
        await CommitImportCoreAsync(insertValidOnly: true);
    }

    [RelayCommand]
    public async Task CommitImportStrictAsync()
    {
        await CommitImportCoreAsync(insertValidOnly: false);
    }

    private async Task CommitImportCoreAsync(bool insertValidOnly)
    {
        if (string.IsNullOrWhiteSpace(ImportFilePath) || IsCommittingImport)
            return;

        IsCommittingImport = true;
        try
        {
            string delim = SelectedDelimiter?.Value ?? ",";
            var stats = await _brandService.CommitCsvAsync(ImportFilePath, insertValidOnly, delim);
            CloseImportModal();
            await LoadBrandsAsync();
            _toastService.ShowSuccess(
                $"Import complete ({stats.NewCount} new, {stats.ExistingCount} existing).",
                "CSV Imported");
        }
        catch (Exception ex)
        {
            _toastService.ShowError(ex.Message, "Import Failed");
        }
        finally
        {
            IsCommittingImport = false;
        }
    }

    // ------------------------------------------------------------------------
    // Internal Helpers
    // ------------------------------------------------------------------------
    private void ResetFormFields()
    {
        EditingId = null;
        EditingBrandOriginalName = string.Empty;
        FormName = string.Empty;
        FormSourceImagePath = null;
        FormExistingImagePath = null;
        FormNotes = string.Empty;

        _suppressPresetSync = true;
        FormPolicy = "before_expiry";
        FormMonths = 3;
        FormDays = 90;
        SelectedPolicyPreset = PolicyPresets[2]; // 3 Months Before Expiry
        SelectedCustomTimeUnit = CustomTimeUnitOptions[0];
        CustomDurationText = "3";
        _suppressPresetSync = false;

        EditingBrandBatches.Clear();
        OnPropertyChanged(nameof(HasEditingBatches));
        OnPropertyChanged(nameof(HasNoEditingBatches));
        NotifyPolicyPreviewPropertiesChanged();
    }

    private void PopulateFormFromBrand(BrandDto brand)
    {
        EditingId = brand.Id;
        EditingBrandOriginalName = brand.Name;
        FormName = brand.Name;
        FormSourceImagePath = null;
        FormExistingImagePath = brand.ImageAbsolutePath;
        FormNotes = brand.ExpiryReturnNotes ?? string.Empty;

        _suppressPresetSync = true;
        FormPolicy = string.IsNullOrWhiteSpace(brand.ExpiryReturnPolicy) ? "before_expiry" : brand.ExpiryReturnPolicy;
        FormMonths = brand.ExpiryReturnMonths;
        FormDays = brand.ExpiryReturnDays;

        string presetKey = DerivePresetKey(FormPolicy, FormMonths, FormDays);
        SelectedPolicyPreset = PolicyPresets.FirstOrDefault(p => p.Key == presetKey) ?? PolicyPresets[2];

        if (presetKey == "custom_before_expiry")
        {
            if (FormMonths <= 0 && FormDays > 0 && FormDays % 30 != 0)
            {
                SelectedCustomTimeUnit = CustomTimeUnitOptions[1]; // days
                CustomDurationText = FormDays.ToString();
            }
            else
            {
                SelectedCustomTimeUnit = CustomTimeUnitOptions[0]; // months
                int months = FormMonths > 0 ? FormMonths : Math.Max(1, (int)Math.Round(FormDays / 30.0));
                CustomDurationText = months.ToString();
            }
        }
        else
        {
            SelectedCustomTimeUnit = CustomTimeUnitOptions[0];
            CustomDurationText = Math.Max(1, FormMonths).ToString();
        }
        _suppressPresetSync = false;

        EditingBrandBatches.Clear();
        foreach (var batch in brand.IntimationBatches)
        {
            EditingBrandBatches.Add(batch);
        }

        OnPropertyChanged(nameof(HasEditingBatches));
        OnPropertyChanged(nameof(HasNoEditingBatches));
        NotifyPolicyPreviewPropertiesChanged();
    }

    private static string DerivePresetKey(string policy, int months, int days)
    {
        if (string.Equals(policy, "accepts_expired", StringComparison.OrdinalIgnoreCase))
            return "accepts_expired";

        if (string.Equals(policy, "non_returnable", StringComparison.OrdinalIgnoreCase))
            return "non_returnable";

        int resolvedMonths = months > 0
            ? months
            : (days > 0 && days % 30 == 0 ? days / 30 : 0);

        int[] presetMonths = [1, 2, 3, 4, 6, 9, 12, 18, 24];
        if (presetMonths.Contains(resolvedMonths) && (days == 0 || days == resolvedMonths * 30 || (resolvedMonths == 12 && days == 365) || (resolvedMonths == 18 && days == 545) || (resolvedMonths == 24 && days == 730)))
        {
            return $"before_{resolvedMonths}";
        }

        return "custom_before_expiry";
    }

    private void ResetImportState()
    {
        ImportFilePath = string.Empty;
        SelectedDelimiter = DelimiterOptions[0];
        IsValidatingImport = false;
        IsCommittingImport = false;
        ValidationResult = null;
        InvalidSamples.Clear();
        ValidSamples.Clear();
        OnPropertyChanged(nameof(HasInvalidSamples));
        OnPropertyChanged(nameof(HasValidSamples));
    }

    private void RecomputeFilteredAndPaged()
    {
        string needle = SearchQuery?.Trim() ?? string.Empty;
        string filterKey = SelectedPolicyFilter?.Key ?? "all";

        IEnumerable<BrandDto> query = _allBrands;

        if (!string.IsNullOrEmpty(needle))
        {
            query = query.Where(b => b.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        query = filterKey switch
        {
            "accepts_expired" => query.Where(b => b.ExpiryReturnPolicy == "accepts_expired"),
            "non_returnable" => query.Where(b => b.ExpiryReturnPolicy == "non_returnable"),
            "before_expiry" => query.Where(b => b.ExpiryReturnPolicy == "before_expiry"),
            "before_3" => query.Where(b => b.ExpiryReturnPolicy == "before_expiry" && b.ExpiryReturnMonths == 3),
            "before_6" => query.Where(b => b.ExpiryReturnPolicy == "before_expiry" && b.ExpiryReturnMonths == 6),
            "before_12" => query.Where(b => b.ExpiryReturnPolicy == "before_expiry" && b.ExpiryReturnMonths == 12),
            "before_24" => query.Where(b => b.ExpiryReturnPolicy == "before_expiry" && b.ExpiryReturnMonths == 24),
            _ => query
        };

        var filtered = query.ToList();
        FilteredBrandsCount = filtered.Count;

        int safePageSize = Math.Max(1, PageSize);
        int computedPageCount = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)safePageSize));
        PageCount = computedPageCount;

        if (Page > PageCount)
            Page = PageCount;
        if (Page < 1)
            Page = 1;

        int skip = (Page - 1) * safePageSize;
        var pageSlice = filtered.Skip(skip).Take(safePageSize).ToList();

        PagedBrands.Clear();
        foreach (var item in pageSlice)
        {
            PagedBrands.Add(item);
        }

        OnPropertyChanged(nameof(PageStatusText));
        OnPropertyChanged(nameof(CanGoPrevPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        OnPropertyChanged(nameof(IsEmptyList));
    }
}
