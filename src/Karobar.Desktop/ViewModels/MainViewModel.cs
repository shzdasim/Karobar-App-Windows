using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;
using Karobar.Domain.Entities;
using Microsoft.Win32;

namespace Karobar.Desktop.ViewModels;

public sealed record ImportedTableRowCountItem(string TableName, int RowCount);

public partial class MainViewModel : ObservableObject
{
    private readonly IDatabaseInitializerService _dbInitializer;
    private readonly ILicenseService _licenseService;
    private readonly ILegacyKarobarImporterService _importerService;
    private readonly IAuthService _authService;
    private readonly ILedgerRebuildService _ledgerRebuildService;

    [ObservableProperty]
    private string _storeName = "Karobar App";

    [ObservableProperty]
    private string _shopType = "pharmacy";

    [ObservableProperty]
    private string _saleSystem = "retail_wholesale";

    [ObservableProperty]
    private string _currencySymbol = "Rs.";

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _databaseSizeDisplay = "0 KB";

    [ObservableProperty]
    private string _machineId = "----";

    [ObservableProperty]
    private string _licenseStatusText = "UNLICENSED";

    [ObservableProperty]
    private bool _isLicenseValid;

    [ObservableProperty]
    private string _licenseDetailText = string.Empty;

    [ObservableProperty]
    private string _licenseKeyInput = string.Empty;

    // Overview KPI properties
    [ObservableProperty]
    private int _productsCount;

    [ObservableProperty]
    private int _batchesCount;

    [ObservableProperty]
    private long _totalUnitsInStock;

    [ObservableProperty]
    private string _inventoryCostDisplay = "Rs. 0.00";

    [ObservableProperty]
    private string _inventoryRetailDisplay = "Rs. 0.00";

    [ObservableProperty]
    private int _saleInvoicesCount;

    [ObservableProperty]
    private string _totalSalesDisplay = "Rs. 0.00";

    [ObservableProperty]
    private int _purchaseInvoicesCount;

    [ObservableProperty]
    private string _totalPurchasesDisplay = "Rs. 0.00";

    [ObservableProperty]
    private int _customersCount;

    [ObservableProperty]
    private int _suppliersCount;

    [ObservableProperty]
    private int _usersCount;

    [ObservableProperty]
    private int _rolesCount;

    [ObservableProperty]
    private int _permissionsCount;

    [ObservableProperty]
    private int _accountsCount;

    [ObservableProperty]
    private string _totalLiquidDisplay = "Rs. 0.00";

    [ObservableProperty]
    private int _chartOfAccountsCount;

    // Importer properties
    [ObservableProperty]
    private string _selectedImportFilePath = string.Empty;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private int _importProgressPercentage;

    [ObservableProperty]
    private string _importStageText = "Ready to import .zip, .sql.gz, .sql, .sqlite, or .json backup.";

    [ObservableProperty]
    private string _importDetailText = "Select any Karobar web app backup file or connect to a local MySQL database.";

    [ObservableProperty]
    private string _mySqlHost = "127.0.0.1";

    [ObservableProperty]
    private int _mySqlPort = 3306;

    [ObservableProperty]
    private string _mySqlDatabase = "karobar";

    [ObservableProperty]
    private string _mySqlUsername = "root";

    [ObservableProperty]
    private string _mySqlPassword = string.Empty;

    // Explorer search
    [ObservableProperty]
    private string _productSearchQuery = string.Empty;

    // Auth test panel
    [ObservableProperty]
    private string _loginEmail = "admin@karobar.app";

    [ObservableProperty]
    private string _loginPassword = "password";

    [ObservableProperty]
    private string _authStatusMessage = "Not signed in. Test login with admin@karobar.app (or any imported user).";

    public ObservableCollection<ImportedTableRowCountItem> ImportedTableCounts { get; } = [];
    public ObservableCollection<Product> ExplorerProducts { get; } = [];
    public ObservableCollection<SaleInvoice> ExplorerSaleInvoices { get; } = [];
    public ObservableCollection<PurchaseInvoice> ExplorerPurchaseInvoices { get; } = [];
    public ObservableCollection<Customer> ExplorerCustomers { get; } = [];
    public ObservableCollection<Supplier> ExplorerSuppliers { get; } = [];

    public MainViewModel(
        IDatabaseInitializerService dbInitializer,
        ILicenseService licenseService,
        ILegacyKarobarImporterService importerService,
        IAuthService authService,
        ILedgerRebuildService ledgerRebuildService)
    {
        _dbInitializer = dbInitializer;
        _licenseService = licenseService;
        _importerService = importerService;
        _authService = authService;
        _ledgerRebuildService = ledgerRebuildService;
    }

    public async Task InitializeAsync()
    {
        MachineId = _licenseService.ComputeMachineId();
        await RefreshDashboardAndExplorerAsync();
        await RefreshLicenseStatusAsync();
    }

    [RelayCommand]
    public async Task RefreshDashboardAndExplorerAsync()
    {
        var stats = await _dbInitializer.GetOverviewStatsAsync();
        StoreName = stats.StoreName;
        ShopType = stats.ShopType;
        SaleSystem = stats.SaleSystem;
        CurrencySymbol = stats.CurrencySymbol;
        DatabasePath = stats.DatabaseFilePath;
        DatabaseSizeDisplay = $"{stats.DatabaseSizeBytes / 1024.0:N1} KB";

        ProductsCount = stats.ProductsCount;
        BatchesCount = stats.BatchesCount;
        TotalUnitsInStock = stats.TotalUnitsInStock;
        InventoryCostDisplay = $"{stats.CurrencySymbol} {stats.InventoryCostValue:N2}";
        InventoryRetailDisplay = $"{stats.CurrencySymbol} {stats.InventoryRetailValue:N2}";

        SaleInvoicesCount = stats.SaleInvoicesCount;
        TotalSalesDisplay = $"{stats.CurrencySymbol} {stats.TotalSalesAmount:N2}";
        PurchaseInvoicesCount = stats.PurchaseInvoicesCount;
        TotalPurchasesDisplay = $"{stats.CurrencySymbol} {stats.TotalPurchasesAmount:N2}";

        CustomersCount = stats.CustomersCount;
        SuppliersCount = stats.SuppliersCount;
        UsersCount = stats.UsersCount;
        RolesCount = stats.RolesCount;
        PermissionsCount = stats.PermissionsCount;
        AccountsCount = stats.AccountsCount;
        TotalLiquidDisplay = $"{stats.CurrencySymbol} {stats.TotalLiquidBalance:N2}";
        ChartOfAccountsCount = stats.ChartOfAccountsCount;

        await SearchExplorerProductsAsync();

        var sales = await _dbInitializer.GetRecentSaleInvoicesAsync(100);
        ExplorerSaleInvoices.Clear();
        foreach (var s in sales) ExplorerSaleInvoices.Add(s);

        var purchases = await _dbInitializer.GetRecentPurchaseInvoicesAsync(100);
        ExplorerPurchaseInvoices.Clear();
        foreach (var p in purchases) ExplorerPurchaseInvoices.Add(p);

        var customers = await _dbInitializer.GetRecentCustomersAsync(100);
        ExplorerCustomers.Clear();
        foreach (var c in customers) ExplorerCustomers.Add(c);

        var suppliers = await _dbInitializer.GetRecentSuppliersAsync(100);
        ExplorerSuppliers.Clear();
        foreach (var sup in suppliers) ExplorerSuppliers.Add(sup);
    }

    [RelayCommand]
    public async Task SearchExplorerProductsAsync()
    {
        var products = await _dbInitializer.GetRecentProductsAsync(150, ProductSearchQuery);
        ExplorerProducts.Clear();
        foreach (var p in products)
        {
            ExplorerProducts.Add(p);
        }
    }

    [RelayCommand]
    public void BrowseImportFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Karobar Web App Backup or Database File",
            Filter = "All Karobar Backups (*.zip;*.gz;*.sql;*.sqlite;*.db;*.json)|*.zip;*.gz;*.sql;*.sqlite;*.db;*.json|Full Backup Archive (*.zip)|*.zip|Database Dump (*.sql.gz;*.sql)|*.gz;*.sql|SQLite Database (*.sqlite;*.db)|*.sqlite;*.db|Settings JSON (*.json)|*.json|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            SelectedImportFilePath = dlg.FileName;
            ImportStageText = $"Selected: {Path.GetFileName(dlg.FileName)}";
            ImportDetailText = "Click 'Run One-Click Import Now' to import into the native Windows SQLite database.";
        }
    }

    [RelayCommand]
    public async Task RunOneClickFileImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedImportFilePath))
        {
            BrowseImportFile();
            if (string.IsNullOrWhiteSpace(SelectedImportFilePath))
                return;
        }

        await ExecuteImportAsync(progress =>
            _importerService.ImportFromFileAsync(SelectedImportFilePath, progress));
    }

    [RelayCommand]
    public async Task RunMySqlImportAsync()
    {
        var options = new MySqlConnectionOptions
        {
            Host = MySqlHost,
            Port = MySqlPort,
            Database = MySqlDatabase,
            Username = MySqlUsername,
            Password = MySqlPassword
        };

        await ExecuteImportAsync(progress =>
            _importerService.ImportFromMySqlAsync(options, progress));
    }

    private async Task ExecuteImportAsync(Func<IProgress<ImportProgressUpdate>, Task<ImportVerificationReport>> importAction)
    {
        if (IsImporting) return;

        IsImporting = true;
        ImportProgressPercentage = 0;
        ImportedTableCounts.Clear();

        var progress = new Progress<ImportProgressUpdate>(update =>
        {
            ImportProgressPercentage = update.Percentage;
            ImportStageText = update.Stage;
            ImportDetailText = update.Detail;
        });

        try
        {
            var report = await importAction(progress);
            ImportStageText = report.Success ? $"Import Succeeded ({report.SourceType})" : "Import Failed";
            ImportDetailText = report.Message;
            ImportProgressPercentage = report.Success ? 100 : 0;

            foreach (var kvp in report.ImportedTableRowCounts.OrderByDescending(k => k.Value))
            {
                ImportedTableCounts.Add(new ImportedTableRowCountItem(kvp.Key, kvp.Value));
            }

            await RefreshDashboardAndExplorerAsync();
            await RefreshLicenseStatusAsync();
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    public async Task RebuildLedgersAsync()
    {
        var custEntries = await _ledgerRebuildService.RebuildAllCustomerLedgersAsync();
        var supEntries = await _ledgerRebuildService.RebuildAllSupplierLedgersAsync();
        ImportStageText = "Ledgers Rebuilt Successfully";
        ImportDetailText = $"Recomputed running balances for {custEntries:N0} customer ledger entries and {supEntries:N0} supplier ledger entries.";
    }

    [RelayCommand]
    public void CopyMachineId()
    {
        if (!string.IsNullOrWhiteSpace(MachineId))
        {
            Clipboard.SetText(MachineId);
            LicenseDetailText = $"Copied Machine ID ({MachineId}) to clipboard.";
        }
    }

    [RelayCommand]
    public async Task ActivateLicenseAsync()
    {
        if (string.IsNullOrWhiteSpace(LicenseKeyInput))
        {
            LicenseDetailText = "Please paste a valid KRBL. license key first.";
            return;
        }

        var result = await _licenseService.ActivateAsync(LicenseKeyInput);
        await RefreshLicenseStatusAsync();
        await RefreshDashboardAndExplorerAsync();

        if (!result.Valid)
        {
            LicenseDetailText = result.Reason ?? "License activation failed.";
        }
    }

    [RelayCommand]
    public async Task RefreshLicenseStatusAsync()
    {
        var status = await _licenseService.GetCurrentStatusAsync();
        MachineId = status.MachineId;
        IsLicenseValid = status.Valid;
        LicenseStatusText = status.Valid
            ? $"VALID ({status.RemainingDays}d remaining)"
            : status.Status.ToUpperInvariant();
        LicenseDetailText = status.Valid
            ? $"Licensed to: {status.CustomerName ?? StoreName} | Expires: {status.ExpiresAt:yyyy-MM-dd HH:mm} UTC"
            : (status.Reason ?? "Unlicensed machine.");
    }

    [RelayCommand]
    public async Task TestLoginAsync()
    {
        try
        {
            var session = await _authService.LoginAsync(LoginEmail, LoginPassword);
            AuthStatusMessage = $"Authenticated as '{session.Name}' ({session.Email}) | Roles: [{string.Join(", ", session.Roles)}] | Permissions loaded: {session.Permissions.Count}";
        }
        catch (Exception ex)
        {
            AuthStatusMessage = $"Login failed: {ex.Message}";
        }
    }
}
