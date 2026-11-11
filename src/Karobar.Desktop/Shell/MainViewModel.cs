using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;

namespace Karobar.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IDatabaseInitializerService _dbInitializer;
    private bool _isDbInitialized;
    private bool _isBarcodeStudioLoaded;

    // ------------------------------------------------------------------------
    // Authentication State
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isLoginVisible = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _loggedInUserName = "Admin User";

    [ObservableProperty]
    private string _loggedInUserInitial = "A";

    // ------------------------------------------------------------------------
    // Persistent Shell: Sidebar & Topbar State
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _brandName = "Karobar App";

    [ObservableProperty]
    private string _brandInitial = "K";

    [ObservableProperty]
    private bool _isSidebarPinned = true;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    private string _sidebarFilterQuery = string.Empty;

    [ObservableProperty]
    private string _currentPath = "/dashboard";

    [ObservableProperty]
    private string _currentPageTitle = "Dashboard";

    [ObservableProperty]
    private string _currentSectionTitle = "Overview";

    [ObservableProperty]
    private string _cashDrawerStatusText = "Rs. 48,500.00";

    [ObservableProperty]
    private string _financialAccountsBalanceText = "Rs. 1,425,800.00";

    [ObservableProperty]
    private string _unreadNotificationsBadge = "7";

    [ObservableProperty]
    private string _licenseBadgeText = "365d 0h left";

    [ObservableProperty]
    private bool _hasFavorites;

    public bool IsSidebarExpanded => !IsSidebarCollapsed;
    public double SidebarWidth => IsSidebarCollapsed ? 76.0 : 286.0;
    public string SidebarPinStatusText => IsSidebarPinned ? "Pinned Sidebar" : "Floating Sidebar";
    public string CompactRailLabel => IsSidebarCollapsed ? "Expand" : "Compact Rail";

    public bool IsDashboardActive => string.Equals(CurrentPath, "/dashboard", StringComparison.OrdinalIgnoreCase);
    public bool IsDashboardPage => IsDashboardActive;
    public bool IsCategoriesPage => string.Equals(CurrentPath, "/categories", StringComparison.OrdinalIgnoreCase);
    public bool IsBrandsPage =>
        string.Equals(CurrentPath, "/brands", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentPath, "/brands/create", StringComparison.OrdinalIgnoreCase);
    public bool IsSuppliersPage =>
        string.Equals(CurrentPath, "/suppliers", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentPath, "/suppliers/create", StringComparison.OrdinalIgnoreCase);
    public bool IsCustomersPage =>
        string.Equals(CurrentPath, "/customers", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentPath, "/customers/create", StringComparison.OrdinalIgnoreCase);
    public bool IsProductsPage =>
        string.Equals(CurrentPath, "/products", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentPath, "/products/create", StringComparison.OrdinalIgnoreCase);
    public bool IsProductBatchesPage =>
        string.Equals(CurrentPath, "/product-batches", StringComparison.OrdinalIgnoreCase);
    public bool IsBarcodeStudioPage =>
        string.Equals(CurrentPath, "/barcode-labels", StringComparison.OrdinalIgnoreCase);
    public bool IsPurchaseInvoicesPage =>
        string.Equals(CurrentPath, "/purchase-invoices", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentPath, "/purchase-invoices/create", StringComparison.OrdinalIgnoreCase);
    public bool IsPurchaseReturnsPage =>
        string.Equals(CurrentPath, "/purchase-returns", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentPath, "/purchase-returns/create", StringComparison.OrdinalIgnoreCase);
    public bool IsSaleInvoicesPage =>
        string.Equals(CurrentPath, "/sale-invoices", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentPath, "/wholesale-invoices", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentPath, "/sale-invoices/create", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentPath, "/sale-invoices/create/retail", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CurrentPath, "/sale-invoices/create/wholesale", StringComparison.OrdinalIgnoreCase);
    public bool IsOtherPage =>
        !IsDashboardPage &&
        !IsCategoriesPage &&
        !IsBrandsPage &&
        !IsSuppliersPage &&
        !IsCustomersPage &&
        !IsProductsPage &&
        !IsProductBatchesPage &&
        !IsBarcodeStudioPage &&
        !IsPurchaseInvoicesPage &&
        !IsPurchaseReturnsPage &&
        !IsSaleInvoicesPage;

    public ToastNotificationService ToastService { get; }
    public DashboardViewModel Dashboard { get; }
    public CategoriesViewModel Categories { get; }
    public BrandsViewModel Brands { get; }
    public SuppliersViewModel Suppliers { get; }
    public CustomersViewModel Customers { get; }
    public ProductsViewModel Products { get; }
    public ProductBatchesViewModel ProductBatches { get; }
    public BarcodeStudioViewModel BarcodeStudio { get; }
    public PurchaseInvoicesViewModel PurchaseInvoices { get; }
    public PurchaseReturnsViewModel PurchaseReturns { get; }
    public SaleInvoicesViewModel SaleInvoices { get; }

    public ObservableCollection<SidebarSectionViewModel> Sections { get; } = [];
    public ObservableCollection<SidebarItemViewModel> FavoriteItems { get; } = [];

    public MainViewModel(
        IAuthService authService,
        IDatabaseInitializerService dbInitializer,
        ICategoryService categoryService,
        IBrandService brandService,
        ISupplierService supplierService,
        ICustomerService customerService,
        IProductService productService,
        IBarcodeStudioService barcodeStudioService,
        IPurchaseInvoiceService purchaseInvoiceService,
        IPurchaseReturnService purchaseReturnService,
        ISaleInvoiceService saleInvoiceService,
        ToastNotificationService toastService)
    {
        _authService = authService;
        _dbInitializer = dbInitializer;
        ToastService = toastService;
        Dashboard = new DashboardViewModel(Navigate, toastService);
        Categories = new CategoriesViewModel(categoryService, authService, toastService);
        Brands = new BrandsViewModel(brandService, authService, toastService, Navigate);
        Suppliers = new SuppliersViewModel(supplierService, authService, toastService, Navigate);
        Customers = new CustomersViewModel(customerService, authService, toastService, Navigate);
        Products = new ProductsViewModel(productService, authService, toastService, Navigate);
        ProductBatches = new ProductBatchesViewModel(productService, toastService, Navigate);
        BarcodeStudio = new BarcodeStudioViewModel(barcodeStudioService);
        PurchaseInvoices = new PurchaseInvoicesViewModel(purchaseInvoiceService, toastService, Navigate, productService);
        PurchaseReturns = new PurchaseReturnsViewModel(purchaseReturnService, authService, toastService, purchaseInvoiceService);
        SaleInvoices = new SaleInvoicesViewModel(saleInvoiceService, toastService, Navigate, productService);

        BuildSidebarNavigation();
        UpdateActiveNavigationState("/dashboard");
    }

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSidebarExpanded));
        OnPropertyChanged(nameof(SidebarWidth));
        OnPropertyChanged(nameof(CompactRailLabel));
    }

    partial void OnIsSidebarPinnedChanged(bool value)
    {
        OnPropertyChanged(nameof(SidebarPinStatusText));
    }

    partial void OnSidebarFilterQueryChanged(string value)
    {
        ApplySidebarFilter(value);
    }

    partial void OnCurrentPathChanged(string value)
    {
        OnPropertyChanged(nameof(IsDashboardActive));
        OnPropertyChanged(nameof(IsDashboardPage));
        OnPropertyChanged(nameof(IsCategoriesPage));
        OnPropertyChanged(nameof(IsBrandsPage));
        OnPropertyChanged(nameof(IsSuppliersPage));
        OnPropertyChanged(nameof(IsCustomersPage));
        OnPropertyChanged(nameof(IsProductsPage));
        OnPropertyChanged(nameof(IsProductBatchesPage));
        OnPropertyChanged(nameof(IsBarcodeStudioPage));
        OnPropertyChanged(nameof(IsPurchaseInvoicesPage));
        OnPropertyChanged(nameof(IsPurchaseReturnsPage));
        OnPropertyChanged(nameof(IsSaleInvoicesPage));
        OnPropertyChanged(nameof(IsOtherPage));
    }

    public async Task EnsureDatabaseInitializedAsync()
    {
        if (_isDbInitialized)
            return;

        await _dbInitializer.InitializeAndSeedAsync();
        _isDbInitialized = true;
    }

    [RelayCommand]
    public async Task LoginAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            await EnsureDatabaseInitializedAsync();
            var session = await _authService.LoginAsync(Username, Password);

            LoggedInUserName = string.IsNullOrWhiteSpace(session.Name) ? session.Username : session.Name;
            LoggedInUserInitial = string.IsNullOrWhiteSpace(LoggedInUserName)
                ? "A"
                : LoggedInUserName.Trim()[..1].ToUpperInvariant();

            Navigate("/dashboard");
            IsAuthenticated = true;
            IsLoginVisible = false;
            ToastService.ShowSuccess($"Welcome back, {LoggedInUserName}!", "Signed In");
        }
        catch (Exception ex)
        {
            IsAuthenticated = false;
            IsLoginVisible = true;
            ErrorMessage = ex.Message;
            HasError = true;
            ToastService.ShowError(ex.Message, "Sign In Failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Logout()
    {
        _authService.Logout();
        Password = string.Empty;
        HasError = false;
        ErrorMessage = string.Empty;
        IsAuthenticated = false;
        IsLoginVisible = true;
        ToastService.ShowInfo("You have been signed out.", "Signed Out");
    }

    [RelayCommand]
    public void Navigate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        UpdateActiveNavigationState(path.Trim());
    }

    [RelayCommand]
    public void ToggleSection(SidebarSectionViewModel? section)
    {
        if (section is null)
            return;

        if (IsSidebarCollapsed)
        {
            IsSidebarCollapsed = false;
            section.IsExpanded = true;
            return;
        }

        section.IsExpanded = !section.IsExpanded;
    }

    [RelayCommand]
    public void ToggleAllSections()
    {
        bool anyCollapsed = Sections.Any(s => s.IsVisible && !s.IsExpanded);
        foreach (var section in Sections)
        {
            section.IsExpanded = anyCollapsed;
        }
    }

    [RelayCommand]
    public void ToggleSidebarCollapsed()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
    }

    [RelayCommand]
    public void ToggleSidebarPinned()
    {
        IsSidebarPinned = !IsSidebarPinned;
    }

    [RelayCommand]
    public void ToggleFavorite(SidebarItemViewModel? item)
    {
        if (item is null)
            return;

        item.IsFavorite = !item.IsFavorite;
        if (item.IsFavorite)
        {
            if (!FavoriteItems.Contains(item))
                FavoriteItems.Add(item);
        }
        else
        {
            FavoriteItems.Remove(item);
        }

        HasFavorites = FavoriteItems.Count > 0;
    }

    private void UpdateActiveNavigationState(string targetPath)
    {
        long? editProductId = null;
        long? batchProductId = null;
        long? batchId = null;
        string? barcodeRouteWithQuery = null;
        long? editPurchaseInvoiceId = null;
        long? detailPurchaseInvoiceId = null;
        long? createFromPoId = null;
        long? editPurchaseReturnId = null;
        long? editSaleInvoiceId = null;
        long? detailSaleInvoiceId = null;

        if (targetPath.StartsWith("/products/edit/", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = targetPath["/products/edit/".Length..].Trim();
            if (long.TryParse(idPart, out var pid) && pid > 0)
            {
                editProductId = pid;
            }
            targetPath = "/products";
        }
        else if (targetPath.StartsWith("/sale-invoices/edit/", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = targetPath["/sale-invoices/edit/".Length..].Trim();
            if (long.TryParse(idPart, out var siId) && siId > 0)
            {
                editSaleInvoiceId = siId;
            }
            targetPath = "/sale-invoices";
        }
        else if (targetPath.StartsWith("/sale-invoices/", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(targetPath, "/sale-invoices/create", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(targetPath, "/sale-invoices/create/retail", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(targetPath, "/sale-invoices/create/wholesale", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = targetPath["/sale-invoices/".Length..].Trim();
            if (long.TryParse(idPart, out var siId) && siId > 0)
            {
                detailSaleInvoiceId = siId;
                targetPath = "/sale-invoices";
            }
        }
        else if (targetPath.StartsWith("/purchase-invoices/edit/", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = targetPath["/purchase-invoices/edit/".Length..].Trim();
            if (long.TryParse(idPart, out var piId) && piId > 0)
            {
                editPurchaseInvoiceId = piId;
            }
            targetPath = "/purchase-invoices";
        }
        else if (targetPath.StartsWith("/purchase-returns/edit/", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = targetPath["/purchase-returns/edit/".Length..].Trim();
            if (long.TryParse(idPart, out var prId) && prId > 0)
            {
                editPurchaseReturnId = prId;
            }
            targetPath = "/purchase-returns";
        }
        else if (targetPath.StartsWith("/purchase-invoices/create?", StringComparison.OrdinalIgnoreCase))
        {
            var queryPart = targetPath["/purchase-invoices/create?".Length..];
            foreach (var pair in queryPart.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 &&
                    string.Equals(kv[0], "purchase_order_id", StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(kv[1], out var poId) && poId > 0)
                {
                    createFromPoId = poId;
                }
            }
            targetPath = "/purchase-invoices/create";
        }
        else if (targetPath.StartsWith("/purchase-invoices/", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(targetPath, "/purchase-invoices/create", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = targetPath["/purchase-invoices/".Length..].Trim();
            if (long.TryParse(idPart, out var piId) && piId > 0)
            {
                detailPurchaseInvoiceId = piId;
                targetPath = "/purchase-invoices";
            }
        }
        else if (targetPath.StartsWith("/product-batches?", StringComparison.OrdinalIgnoreCase))
        {
            var queryPart = targetPath["/product-batches?".Length..];
            foreach (var pair in queryPart.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2)
                {
                    if (string.Equals(kv[0], "product_id", StringComparison.OrdinalIgnoreCase) &&
                        long.TryParse(kv[1], out var bpid) && bpid > 0)
                    {
                        batchProductId = bpid;
                    }
                    else if (string.Equals(kv[0], "batch_id", StringComparison.OrdinalIgnoreCase) &&
                             long.TryParse(kv[1], out var bid) && bid > 0)
                    {
                        batchId = bid;
                    }
                }
            }
            targetPath = "/product-batches";
        }
        else if (targetPath.StartsWith("/barcode-labels?", StringComparison.OrdinalIgnoreCase))
        {
            barcodeRouteWithQuery = targetPath;
            targetPath = "/barcode-labels";
        }

        CurrentPath = targetPath;

        if (string.Equals(targetPath, "/dashboard", StringComparison.OrdinalIgnoreCase))
        {
            CurrentPageTitle = "Dashboard";
            CurrentSectionTitle = "Business Overview";
            foreach (var sec in Sections)
            {
                foreach (var item in sec.Items)
                    item.IsActive = false;
            }
            return;
        }

        SidebarItemViewModel? matchedItem = null;
        SidebarSectionViewModel? matchedSection = null;

        foreach (var sec in Sections)
        {
            foreach (var item in sec.Items)
            {
                bool isMatch = string.Equals(item.Path, targetPath, StringComparison.OrdinalIgnoreCase) ||
                               (!string.Equals(targetPath, "/product-batches", StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(item.QuickCreatePath, targetPath, StringComparison.OrdinalIgnoreCase));
                item.IsActive = isMatch;
                if (isMatch && matchedItem == null)
                {
                    matchedItem = item;
                    matchedSection = sec;
                }
            }
        }

        if (matchedItem != null && matchedSection != null)
        {
            matchedSection.IsExpanded = true;
            CurrentPageTitle = string.Equals(matchedItem.QuickCreatePath, targetPath, StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(matchedItem.Path, targetPath, StringComparison.OrdinalIgnoreCase)
                ? $"Create {matchedItem.Name}"
                : matchedItem.Name;
            CurrentSectionTitle = matchedSection.Title;
        }
        else
        {
            CurrentSectionTitle = "Workspace Utility";
            CurrentPageTitle = targetPath switch
            {
                "/profile" => "User Profile",
                "/guide" => "Bilingual Guide Bot",
                "/shortcuts" => "Keyboard Shortcuts",
                "/notifications" => "System Notifications",
                "/search" => "Quick Page Search",
                _ => targetPath.Trim('/').Replace('-', ' ').ToUpperInvariant()
            };
        }

        if (string.Equals(targetPath, "/categories", StringComparison.OrdinalIgnoreCase))
        {
            _ = Categories.LoadCategoriesAsync();
        }
        else if (string.Equals(targetPath, "/brands", StringComparison.OrdinalIgnoreCase))
        {
            _ = Brands.OpenDirectoryModeAsync();
        }
        else if (string.Equals(targetPath, "/brands/create", StringComparison.OrdinalIgnoreCase))
        {
            Brands.OpenCreateMode();
        }
        else if (string.Equals(targetPath, "/suppliers", StringComparison.OrdinalIgnoreCase))
        {
            _ = Suppliers.OpenDirectoryModeAsync();
        }
        else if (string.Equals(targetPath, "/suppliers/create", StringComparison.OrdinalIgnoreCase))
        {
            Suppliers.OpenCreateMode();
        }
        else if (string.Equals(targetPath, "/customers", StringComparison.OrdinalIgnoreCase))
        {
            _ = Customers.OpenDirectoryModeAsync();
        }
        else if (string.Equals(targetPath, "/customers/create", StringComparison.OrdinalIgnoreCase))
        {
            _ = Customers.OpenCreateModeAsync();
        }
        else if (string.Equals(targetPath, "/products", StringComparison.OrdinalIgnoreCase))
        {
            if (editProductId.HasValue)
            {
                _ = Products.OpenEditProductByIdAsync(editProductId.Value);
            }
            else
            {
                _ = Products.OpenDirectoryModeAsync();
            }
        }
        else if (string.Equals(targetPath, "/products/create", StringComparison.OrdinalIgnoreCase))
        {
            _ = Products.OpenCreateProductAsync();
        }
        else if (string.Equals(targetPath, "/product-batches", StringComparison.OrdinalIgnoreCase))
        {
            _ = ProductBatches.OpenWorkbenchAsync(batchProductId, batchId);
        }
        else if (string.Equals(targetPath, "/barcode-labels", StringComparison.OrdinalIgnoreCase))
        {
            if (!_isBarcodeStudioLoaded)
            {
                _isBarcodeStudioLoaded = true;
                _ = BarcodeStudio.InitializeAsync(barcodeRouteWithQuery);
            }
            else if (!string.IsNullOrWhiteSpace(barcodeRouteWithQuery))
            {
                _ = BarcodeStudio.ApplyNavigationQueryAsync(barcodeRouteWithQuery);
            }
        }
        else if (string.Equals(targetPath, "/purchase-invoices", StringComparison.OrdinalIgnoreCase))
        {
            if (editPurchaseInvoiceId.HasValue)
            {
                _ = PurchaseInvoices.OpenEditModeAsync(editPurchaseInvoiceId.Value);
            }
            else if (detailPurchaseInvoiceId.HasValue)
            {
                _ = PurchaseInvoices.OpenDetailModeAsync(detailPurchaseInvoiceId.Value);
            }
            else
            {
                _ = PurchaseInvoices.OpenDirectoryModeAsync();
            }
        }
        else if (string.Equals(targetPath, "/purchase-invoices/create", StringComparison.OrdinalIgnoreCase))
        {
            _ = PurchaseInvoices.OpenCreateModeAsync(createFromPoId);
        }
        else if (string.Equals(targetPath, "/purchase-returns", StringComparison.OrdinalIgnoreCase))
        {
            if (editPurchaseReturnId.HasValue)
            {
                _ = PurchaseReturns.OpenEditFormByIdAsync(editPurchaseReturnId.Value);
            }
            else
            {
                _ = PurchaseReturns.InitializeAsync("/purchase-returns");
            }
        }
        else if (string.Equals(targetPath, "/purchase-returns/create", StringComparison.OrdinalIgnoreCase))
        {
            _ = PurchaseReturns.InitializeAsync("/purchase-returns/create");
        }
        else if (string.Equals(targetPath, "/sale-invoices", StringComparison.OrdinalIgnoreCase))
        {
            if (editSaleInvoiceId.HasValue)
            {
                _ = SaleInvoices.OpenEditModeAsync(editSaleInvoiceId.Value);
            }
            else if (detailSaleInvoiceId.HasValue)
            {
                _ = SaleInvoices.OpenDetailModeAsync(detailSaleInvoiceId.Value);
            }
            else
            {
                _ = SaleInvoices.OpenDirectoryModeAsync("all");
            }
        }
        else if (string.Equals(targetPath, "/wholesale-invoices", StringComparison.OrdinalIgnoreCase))
        {
            _ = SaleInvoices.OpenDirectoryModeAsync("wholesale");
        }
        else if (string.Equals(targetPath, "/sale-invoices/create", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(targetPath, "/sale-invoices/create/retail", StringComparison.OrdinalIgnoreCase))
        {
            _ = SaleInvoices.OpenCreateModeAsync("retail");
        }
        else if (string.Equals(targetPath, "/sale-invoices/create/wholesale", StringComparison.OrdinalIgnoreCase))
        {
            _ = SaleInvoices.OpenCreateModeAsync("wholesale");
        }
    }

    private void ApplySidebarFilter(string query)
    {
        string trimmed = query?.Trim() ?? string.Empty;
        bool hasQuery = !string.IsNullOrEmpty(trimmed);

        foreach (var section in Sections)
        {
            bool sectionTitleMatches = hasQuery &&
                section.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase);

            int visibleInSec = 0;
            string? lastVisibleSubGroup = null;

            foreach (var item in section.Items)
            {
                bool visible = !hasQuery ||
                               sectionTitleMatches ||
                               item.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                               (item.SubGroup?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false);

                item.IsVisible = visible;
                if (visible)
                {
                    visibleInSec++;
                    if (!string.IsNullOrWhiteSpace(item.SubGroup) &&
                        !string.Equals(item.SubGroup, lastVisibleSubGroup, StringComparison.Ordinal))
                    {
                        item.ShowSubGroupHeader = true;
                        lastVisibleSubGroup = item.SubGroup;
                    }
                    else
                    {
                        item.ShowSubGroupHeader = false;
                    }
                }
                else
                {
                    item.ShowSubGroupHeader = false;
                }
            }

            section.VisibleCount = visibleInSec;
            section.IsVisible = visibleInSec > 0;
            if (hasQuery && visibleInSec > 0)
            {
                section.IsExpanded = true;
            }
        }
    }

    private void BuildSidebarNavigation()
    {
        Sections.Clear();

        // 1. Items & Masters (Primary Blue #3B82F6)
        var itemsSection = new SidebarSectionViewModel
        {
            Key = "items",
            Title = "ITEMS & MASTERS",
            IconGlyph = "📦",
            AccentHex = "#3B82F6",
            AccentBgHex = "#EFF6FF",
            IsExpanded = true
        };
        itemsSection.Items.Add(new SidebarItemViewModel { Key = "products", Name = "Products", Path = "/products", QuickCreatePath = "/products/create", IconGlyph = "📦", AccentHex = "#3B82F6" });
        itemsSection.Items.Add(new SidebarItemViewModel { Key = "product-batches", Name = "Product Batches", Path = "/product-batches", IconGlyph = "🧪", AccentHex = "#3B82F6" });
        itemsSection.Items.Add(new SidebarItemViewModel { Key = "categories", Name = "Categories", Path = "/categories", IconGlyph = "🗂", AccentHex = "#3B82F6" });
        itemsSection.Items.Add(new SidebarItemViewModel { Key = "brands", Name = "Brands", Path = "/brands", QuickCreatePath = "/brands/create", IconGlyph = "🏷", AccentHex = "#3B82F6" });
        itemsSection.Items.Add(new SidebarItemViewModel { Key = "suppliers", Name = "Suppliers", Path = "/suppliers", QuickCreatePath = "/suppliers/create", IconGlyph = "🚚", AccentHex = "#3B82F6" });
        itemsSection.Items.Add(new SidebarItemViewModel { Key = "customers", Name = "Customers", Path = "/customers", QuickCreatePath = "/customers/create", IconGlyph = "👥", AccentHex = "#3B82F6" });
        itemsSection.Items.Add(new SidebarItemViewModel { Key = "barcode-labels", Name = "Barcode Studio", Path = "/barcode-labels", IconGlyph = "🖨", AccentHex = "#3B82F6" });
        Sections.Add(itemsSection);

        // 2. Invoices (Secondary Purple #8B5CF6)
        var invoicesSection = new SidebarSectionViewModel
        {
            Key = "invoices",
            Title = "INVOICES",
            IconGlyph = "🧾",
            AccentHex = "#8B5CF6",
            AccentBgHex = "#F5F3FF",
            IsExpanded = true
        };
        invoicesSection.Items.Add(new SidebarItemViewModel { Key = "pos", Name = "Barcode POS", Path = "/sale-invoices/pos", IconGlyph = "⚡", AccentHex = "#8B5CF6" });
        invoicesSection.Items.Add(new SidebarItemViewModel { Key = "sale-invoices", Name = "Sale Invoices", Path = "/sale-invoices", QuickCreatePath = "/sale-invoices/create/retail", IconGlyph = "🧾", AccentHex = "#8B5CF6" });
        invoicesSection.Items.Add(new SidebarItemViewModel { Key = "wholesale-invoices", Name = "Wholesale Invoices", Path = "/wholesale-invoices", QuickCreatePath = "/sale-invoices/create/wholesale", IconGlyph = "🏢", AccentHex = "#8B5CF6" });
        invoicesSection.Items.Add(new SidebarItemViewModel { Key = "purchase-invoices", Name = "Purchase Invoices", Path = "/purchase-invoices", QuickCreatePath = "/purchase-invoices/create", IconGlyph = "🛒", AccentHex = "#8B5CF6" });
        invoicesSection.Items.Add(new SidebarItemViewModel { Key = "purchase-orders", Name = "Purchase Orders", Path = "/purchase-orders", QuickCreatePath = "/purchase-orders/create", IconGlyph = "📑", AccentHex = "#8B5CF6" });
        Sections.Add(invoicesSection);

        // 3. Returns (Tertiary Cyan #06B6D4)
        var returnsSection = new SidebarSectionViewModel
        {
            Key = "returns",
            Title = "RETURNS",
            IconGlyph = "↩",
            AccentHex = "#06B6D4",
            AccentBgHex = "#ECFEFF",
            IsExpanded = true
        };
        returnsSection.Items.Add(new SidebarItemViewModel { Key = "sale-returns", Name = "Sale Returns", Path = "/sale-returns", QuickCreatePath = "/sale-returns/create", IconGlyph = "↩", AccentHex = "#06B6D4" });
        returnsSection.Items.Add(new SidebarItemViewModel { Key = "purchase-returns", Name = "Purchase Returns", Path = "/purchase-returns", QuickCreatePath = "/purchase-returns/create", IconGlyph = "↪", AccentHex = "#06B6D4" });
        Sections.Add(returnsSection);

        // 4. Stock & Inventory (Primary Blue #3B82F6)
        var inventorySection = new SidebarSectionViewModel
        {
            Key = "inventory",
            Title = "STOCK & INVENTORY",
            IconGlyph = "🏛",
            AccentHex = "#3B82F6",
            AccentBgHex = "#EFF6FF",
            IsExpanded = true
        };
        inventorySection.Items.Add(new SidebarItemViewModel { Key = "batch-expiry", Name = "Batch & Expiry", Path = "/batch-expiry", QuickCreatePath = "/product-batches", BadgeText = "9", IconGlyph = "⏳", AccentHex = "#3B82F6" });
        inventorySection.Items.Add(new SidebarItemViewModel { Key = "user-demands", Name = "User Demands", Path = "/user-demands", QuickCreatePath = "/user-demands/create", IconGlyph = "📋", AccentHex = "#3B82F6" });
        inventorySection.Items.Add(new SidebarItemViewModel { Key = "stock-adjustments", Name = "Stock Adjustments", Path = "/stock-adjustments", QuickCreatePath = "/stock-adjustments/create", IconGlyph = "⚖", AccentHex = "#3B82F6" });
        inventorySection.Items.Add(new SidebarItemViewModel { Key = "stocktake-sessions", Name = "Stocktake Sessions", Path = "/stocktake-sessions", QuickCreatePath = "/stocktake-sessions/create", IconGlyph = "✅", AccentHex = "#3B82F6" });
        inventorySection.Items.Add(new SidebarItemViewModel { Key = "stock-disposals", Name = "Quarantine & Disposal", Path = "/stock-disposals", QuickCreatePath = "/stock-disposals/create", IconGlyph = "🛡", AccentHex = "#3B82F6" });
        Sections.Add(inventorySection);

        // 5. Finance & Ledgers (Secondary Purple #8B5CF6)
        var financeSection = new SidebarSectionViewModel
        {
            Key = "finance",
            Title = "FINANCE & LEDGERS",
            IconGlyph = "🏦",
            AccentHex = "#8B5CF6",
            AccentBgHex = "#F5F3FF",
            IsExpanded = true
        };
        financeSection.Items.Add(new SidebarItemViewModel { Key = "customer-ledger", Name = "Customer Ledger", Path = "/customer-ledger", IconGlyph = "📖", AccentHex = "#8B5CF6" });
        financeSection.Items.Add(new SidebarItemViewModel { Key = "supplier-ledger", Name = "Supplier Ledger", Path = "/supplier-ledger", IconGlyph = "📘", AccentHex = "#8B5CF6" });
        financeSection.Items.Add(new SidebarItemViewModel { Key = "payments", Name = "Payment Vouchers", Path = "/payments", IconGlyph = "💳", AccentHex = "#8B5CF6" });
        financeSection.Items.Add(new SidebarItemViewModel { Key = "cash-drawer", Name = "Cash Drawer", Path = "/cash-drawer", IconGlyph = "🗄", AccentHex = "#8B5CF6" });
        financeSection.Items.Add(new SidebarItemViewModel { Key = "accounts", Name = "Financial Accounts", Path = "/accounts", IconGlyph = "🏦", AccentHex = "#8B5CF6" });
        financeSection.Items.Add(new SidebarItemViewModel { Key = "expenses", Name = "Operating Expenses", Path = "/expenses", IconGlyph = "💸", AccentHex = "#8B5CF6" });
        financeSection.Items.Add(new SidebarItemViewModel { Key = "accounting", Name = "Double-Entry & GL", Path = "/accounting", IconGlyph = "📊", AccentHex = "#8B5CF6" });
        financeSection.Items.Add(new SidebarItemViewModel { Key = "hr-payroll", Name = "HR, Payroll & Commissions", Path = "/hr-payroll", IconGlyph = "👔", AccentHex = "#8B5CF6" });
        Sections.Add(financeSection);

        // 6. Reports (Tertiary Cyan #06B6D4 with 3 Sub-Groups)
        var reportsSection = new SidebarSectionViewModel
        {
            Key = "reports",
            Title = "REPORTS",
            IconGlyph = "📈",
            AccentHex = "#06B6D4",
            AccentBgHex = "#ECFEFF",
            IsExpanded = false
        };
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "net-profit", Name = "Net Profit (P&L)", Path = "/reports/net-profit", SubGroup = "FINANCIAL & TAX", IconGlyph = "📈", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "tax-return", Name = "Statutory Tax Return", Path = "/reports/tax-return", SubGroup = "FINANCIAL & TAX", IconGlyph = "🏛", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "cost-of-sale", Name = "Cost of Sale", Path = "/reports/cost-of-sale", SubGroup = "FINANCIAL & TAX", IconGlyph = "🧮", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "cost-of-sale-detail", Name = "Cost of Sale Detail", Path = "/reports/cost-of-sale-detail", SubGroup = "FINANCIAL & TAX", IconGlyph = "📑", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "outstanding-aging", Name = "Outstanding Aging", Path = "/reports/outstanding-aging", SubGroup = "FINANCIAL & TAX", IconGlyph = "⏳", AccentHex = "#06B6D4" });

        reportsSection.Items.Add(new SidebarItemViewModel { Key = "sale-detail", Name = "Sale Detail", Path = "/reports/sale-detail", SubGroup = "SALES, PURCHASES & RETURNS", IconGlyph = "🧾", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "purchase-detail", Name = "Purchase Detail", Path = "/reports/purchase-detail", SubGroup = "SALES, PURCHASES & RETURNS", IconGlyph = "🛒", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "purchase-summary", Name = "Purchase Summary", Path = "/reports/purchase-summary", SubGroup = "SALES, PURCHASES & RETURNS", IconGlyph = "📋", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "sale-returns-report", Name = "Sale Returns Report", Path = "/reports/sale-returns", SubGroup = "SALES, PURCHASES & RETURNS", IconGlyph = "↩", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "purchase-returns-report", Name = "Purchase Returns Report", Path = "/reports/purchase-returns", SubGroup = "SALES, PURCHASES & RETURNS", IconGlyph = "↪", AccentHex = "#06B6D4" });

        reportsSection.Items.Add(new SidebarItemViewModel { Key = "current-stock", Name = "Current Stock", Path = "/reports/current-stock", SubGroup = "INVENTORY & STOCK", IconGlyph = "📦", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "reorder", Name = "Low Stock & Reorder", Path = "/reports/reorder", SubGroup = "INVENTORY & STOCK", IconGlyph = "⚠", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "product-comprehensive", Name = "Product Comprehensive", Path = "/reports/product-comprehensive", SubGroup = "INVENTORY & STOCK", IconGlyph = "🔍", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "near-expiry-product", Name = "Near Expiry Product", Path = "/reports/near-expiry-product", SubGroup = "INVENTORY & STOCK", IconGlyph = "⏰", AccentHex = "#06B6D4" });
        reportsSection.Items.Add(new SidebarItemViewModel { Key = "stock-adjustment-report", Name = "Stock Adjustment Report", Path = "/reports/stock-adjustment", SubGroup = "INVENTORY & STOCK", IconGlyph = "⚖", AccentHex = "#06B6D4" });
        Sections.Add(reportsSection);

        // 7. System & Settings (Primary Blue #3B82F6)
        var systemSection = new SidebarSectionViewModel
        {
            Key = "system",
            Title = "SYSTEM & SETTINGS",
            IconGlyph = "⚙",
            AccentHex = "#3B82F6",
            AccentBgHex = "#EFF6FF",
            IsExpanded = true
        };
        systemSection.Items.Add(new SidebarItemViewModel { Key = "settings", Name = "Settings", Path = "/settings", IconGlyph = "⚙", AccentHex = "#3B82F6" });
        systemSection.Items.Add(new SidebarItemViewModel { Key = "users", Name = "Users", Path = "/users", QuickCreatePath = "/users/create", IconGlyph = "👤", AccentHex = "#3B82F6" });
        systemSection.Items.Add(new SidebarItemViewModel { Key = "roles", Name = "Roles", Path = "/roles", QuickCreatePath = "/roles/create", IconGlyph = "🔐", AccentHex = "#3B82F6" });
        systemSection.Items.Add(new SidebarItemViewModel { Key = "backups", Name = "Backups & Storage", Path = "/backups", IconGlyph = "💾", AccentHex = "#3B82F6" });
        Sections.Add(systemSection);

        ApplySidebarFilter(string.Empty);
    }
}
