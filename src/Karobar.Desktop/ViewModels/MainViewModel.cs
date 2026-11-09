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
    public bool IsOtherPage => !IsDashboardActive;

    public DashboardViewModel Dashboard { get; }

    public ObservableCollection<SidebarSectionViewModel> Sections { get; } = [];
    public ObservableCollection<SidebarItemViewModel> FavoriteItems { get; } = [];

    public MainViewModel(IAuthService authService, IDatabaseInitializerService dbInitializer)
    {
        _authService = authService;
        _dbInitializer = dbInitializer;
        Dashboard = new DashboardViewModel(Navigate);

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
        }
        catch (Exception ex)
        {
            IsAuthenticated = false;
            IsLoginVisible = true;
            ErrorMessage = ex.Message;
            HasError = true;
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
                               string.Equals(item.QuickCreatePath, targetPath, StringComparison.OrdinalIgnoreCase);
                item.IsActive = isMatch;
                if (isMatch)
                {
                    matchedItem = item;
                    matchedSection = sec;
                }
            }
        }

        if (matchedItem != null && matchedSection != null)
        {
            matchedSection.IsExpanded = true;
            CurrentPageTitle = string.Equals(matchedItem.QuickCreatePath, targetPath, StringComparison.OrdinalIgnoreCase)
                ? $"Create {matchedItem.Name}"
                : matchedItem.Name;
            CurrentSectionTitle = matchedSection.Title;
        }
        else
        {
            // Handle Topbar utility routes (e.g., /profile, /guide, /shortcuts, /notifications)
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
        itemsSection.Items.Add(new SidebarItemViewModel { Key = "customers", Name = "Customers", Path = "/customers", IconGlyph = "👥", AccentHex = "#3B82F6" });
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
