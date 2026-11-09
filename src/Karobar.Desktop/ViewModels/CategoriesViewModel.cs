using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;
using Microsoft.Win32;

namespace Karobar.Desktop.ViewModels;

public sealed class DelimiterOptionModel
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = ",";
}

public partial class CategoriesViewModel : ObservableObject
{
    private readonly ICategoryService _categoryService;
    private readonly IAuthService _authService;
    private readonly List<CategoryDto> _allCategories = [];

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

    public bool CanShowForm => CanCreate || (CanUpdate && IsEditing);
    public bool HasRowActions => CanUpdate || CanDelete;

    // ------------------------------------------------------------------------
    // Feedback Banner (Toast equivalent)
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private bool _isStatusError;

    public string StatusBannerBg => IsStatusError ? "#FEF2F2" : "#ECFDF5";
    public string StatusBannerBorder => IsStatusError ? "#FCA5A5" : "#A7F3D0";
    public string StatusBannerFg => IsStatusError ? "#DC2626" : "#059669";

    // ------------------------------------------------------------------------
    // Form Card State (Add / Edit Category)
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private long? _editingId;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isExporting;

    public bool IsEditing => EditingId.HasValue;
    public bool CanExportNow => !IsExporting;
    public string FormHeaderIcon => IsEditing ? "✎" : "+";
    public string FormHeaderTitle => IsEditing ? "Edit Category" : "Add Category";
    public string FormHeaderSubtitle => IsEditing ? "Update category information" : "Enter category details";
    public string SaveButtonText => IsEditing
        ? (IsSaving ? "Updating..." : "Update")
        : (IsSaving ? "Saving..." : "Save");

    // ------------------------------------------------------------------------
    // Search & Pagination State
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _page = 1;

    [ObservableProperty]
    private int _pageSize = 10;

    [ObservableProperty]
    private int _pageCount = 1;

    [ObservableProperty]
    private int _totalCategoriesCount;

    [ObservableProperty]
    private int _filteredCategoriesCount;

    public ObservableCollection<int> PageSizeOptions { get; } = [10, 25, 50, 100];
    public ObservableCollection<CategoryDto> PagedCategories { get; } = [];

    public string SubtitleText => $"{TotalCategoriesCount} categories";
    public string ListCountLabel => $"{PagedCategories.Count} items";
    public string PageStatusText => $"Page {Page} of {PageCount}";
    public bool CanGoPrevPage => Page > 1;
    public bool CanGoNextPage => Page < PageCount;
    public bool IsEmptyList => PagedCategories.Count == 0 && !IsLoading;

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
    private CategoryCsvValidationResult? _validationResult;

    public ObservableCollection<DelimiterOptionModel> DelimiterOptions { get; } =
    [
        new DelimiterOptionModel { Label = "Comma (,)", Value = "," },
        new DelimiterOptionModel { Label = "Semicolon (;)", Value = ";" },
        new DelimiterOptionModel { Label = "Tab (\\t)", Value = "\\t" },
        new DelimiterOptionModel { Label = "Pipe (|)", Value = "|" }
    ];

    public ObservableCollection<CategoryCsvSampleRow> InvalidSamples { get; } = [];
    public ObservableCollection<CategoryCsvSampleRow> ValidSamples { get; } = [];

    public string ImportFileNameDisplay => string.IsNullOrWhiteSpace(ImportFilePath)
        ? "No file chosen"
        : Path.GetFileName(ImportFilePath);

    public bool CanValidateImport => !string.IsNullOrWhiteSpace(ImportFilePath) && !IsValidatingImport;
    public bool HasValidationResult => ValidationResult != null;
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

    public CategoriesViewModel(ICategoryService categoryService, IAuthService authService)
    {
        _categoryService = categoryService;
        _authService = authService;
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
            CanView = session.Permissions.Contains("category.view");
            CanCreate = session.Permissions.Contains("category.create");
            CanUpdate = session.Permissions.Contains("category.update");
            CanDelete = session.Permissions.Contains("category.delete");
            CanImport = session.Permissions.Contains("category.import") || session.Permissions.Contains("category.create");
            CanExport = session.Permissions.Contains("category.export") || session.Permissions.Contains("category.view");
        }

        OnPropertyChanged(nameof(CanShowForm));
        OnPropertyChanged(nameof(HasRowActions));
    }

    partial void OnEditingIdChanged(long? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(CanShowForm));
        OnPropertyChanged(nameof(FormHeaderIcon));
        OnPropertyChanged(nameof(FormHeaderTitle));
        OnPropertyChanged(nameof(FormHeaderSubtitle));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(SaveButtonText));
    }

    partial void OnIsExportingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExportNow));
    }

    partial void OnIsStatusErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusBannerBg));
        OnPropertyChanged(nameof(StatusBannerBorder));
        OnPropertyChanged(nameof(StatusBannerFg));
    }

    partial void OnSearchQueryChanged(string value)
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

    partial void OnTotalCategoriesCountChanged(int value)
    {
        OnPropertyChanged(nameof(SubtitleText));
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

    partial void OnValidationResultChanged(CategoryCsvValidationResult? value)
    {
        OnPropertyChanged(nameof(HasValidationResult));
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

    [RelayCommand]
    public async Task LoadCategoriesAsync()
    {
        RefreshPermissions();
        if (!CanView)
        {
            ShowStatus("You don't have permission to view categories.", isError: true);
            return;
        }

        IsLoading = true;
        try
        {
            var list = await _categoryService.GetAllAsync();
            _allCategories.Clear();
            _allCategories.AddRange(list);
            TotalCategoriesCount = _allCategories.Count;
            RecomputeFilteredAndPaged();
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to load categories: {ex.Message}", isError: true);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmptyList));
        }
    }

    [RelayCommand]
    public async Task SaveCategoryAsync()
    {
        if (IsSaving)
            return;

        if (IsEditing ? !CanUpdate : !CanCreate)
        {
            ShowStatus("You don't have permission to save categories.", isError: true);
            return;
        }

        string trimmed = FormName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            ShowStatus("Name is required", isError: true);
            return;
        }

        IsSaving = true;
        try
        {
            if (EditingId.HasValue)
            {
                await _categoryService.UpdateAsync(EditingId.Value, trimmed);
                ShowStatus("Category updated", isError: false);
            }
            else
            {
                await _categoryService.CreateAsync(trimmed);
                ShowStatus("Category saved", isError: false);
            }

            ResetForm();
            await LoadCategoriesAsync();
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, isError: true);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    public void EditCategory(CategoryDto? category)
    {
        if (category == null)
            return;

        if (!CanUpdate)
        {
            ShowStatus("You don't have permission to edit categories.", isError: true);
            return;
        }

        EditingId = category.Id;
        FormName = category.Name;
        DismissStatus();
    }

    [RelayCommand]
    public async Task DeleteCategoryAsync(CategoryDto? category)
    {
        if (category == null)
            return;

        if (!CanDelete)
        {
            ShowStatus("You don't have permission to delete categories.", isError: true);
            return;
        }

        if (category.IsUsedByProducts)
        {
            ShowStatus("Cannot delete: category is used by products.", isError: true);
            return;
        }

        try
        {
            await _categoryService.DeleteAsync(category.Id);
            if (EditingId == category.Id)
            {
                ResetForm();
            }

            await LoadCategoriesAsync();
            ShowStatus("Category deleted", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, isError: true);
        }
    }

    [RelayCommand]
    public void ResetForm()
    {
        FormName = string.Empty;
        EditingId = null;
    }

    [RelayCommand]
    public void DismissStatus()
    {
        HasStatusMessage = false;
        StatusMessage = string.Empty;
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
    // Export & Import Commands
    // ------------------------------------------------------------------------
    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        if (!CanExport)
        {
            ShowStatus("You don't have permission to export categories.", isError: true);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Categories to CSV",
            FileName = $"categories_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv",
            DefaultExt = ".csv",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsExporting = true;
        try
        {
            await _categoryService.ExportCsvAsync(dialog.FileName);
            ShowStatus($"Exported {_allCategories.Count} categories to {Path.GetFileName(dialog.FileName)}", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Export failed: {ex.Message}", isError: true);
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
            ShowStatus("You don't have permission to import categories.", isError: true);
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
            Title = "Choose Categories CSV File",
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
            Title = "Save Categories CSV Template",
            FileName = "categories_template.csv",
            DefaultExt = ".csv",
            Filter = "CSV Files (*.csv)|*.csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _categoryService.SaveTemplateCsvAsync(dialog.FileName);
            ShowStatus($"Saved CSV template to {Path.GetFileName(dialog.FileName)}", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not save template: {ex.Message}", isError: true);
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
            var res = await _categoryService.ValidateCsvAsync(ImportFilePath, delim);
            if (!res.Ok)
            {
                ShowStatus(res.Message, isError: true);
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

            ShowStatus(res.Message, isError: res.Invalid > 0);
        }
        catch (Exception ex)
        {
            ShowStatus($"Validation failed: {ex.Message}", isError: true);
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
            var stats = await _categoryService.CommitCsvAsync(ImportFilePath, insertValidOnly, delim);
            CloseImportModal();
            await LoadCategoriesAsync();
            ShowStatus($"Import complete ({stats.NewCount} new, {stats.ExistingCount} existing).", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, isError: true);
        }
        finally
        {
            IsCommittingImport = false;
        }
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
        string needle = SearchQuery?.Trim().ToLowerInvariant() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(needle)
            ? _allCategories
            : _allCategories.Where(c => c.Name.ToLowerInvariant().Contains(needle)).ToList();

        FilteredCategoriesCount = filtered.Count;
        int safePageSize = Math.Max(1, PageSize);
        int computedPageCount = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)safePageSize));
        PageCount = computedPageCount;

        if (Page > PageCount)
            Page = PageCount;
        if (Page < 1)
            Page = 1;

        int skip = (Page - 1) * safePageSize;
        var pageSlice = filtered.Skip(skip).Take(safePageSize).ToList();

        PagedCategories.Clear();
        foreach (var item in pageSlice)
        {
            PagedCategories.Add(item);
        }

        OnPropertyChanged(nameof(ListCountLabel));
        OnPropertyChanged(nameof(PageStatusText));
        OnPropertyChanged(nameof(CanGoPrevPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        OnPropertyChanged(nameof(IsEmptyList));
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
        HasStatusMessage = !string.IsNullOrWhiteSpace(message);
    }
}
