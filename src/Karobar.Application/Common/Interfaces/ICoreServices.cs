using Karobar.Domain.Entities;

namespace Karobar.Application.Interfaces;

public sealed class DatabaseOverviewStats
{
    public string DatabaseFilePath { get; set; } = string.Empty;
    public long DatabaseSizeBytes { get; set; }
    public string StoreName { get; set; } = "Karobar App";
    public string ShopType { get; set; } = "pharmacy";
    public string SaleSystem { get; set; } = "retail_wholesale";
    public string CurrencySymbol { get; set; } = "Rs.";
    public bool IsSetupCompleted { get; set; }

    public int UsersCount { get; set; }
    public int RolesCount { get; set; }
    public int PermissionsCount { get; set; }
    public int CategoriesCount { get; set; }
    public int BrandsCount { get; set; }
    public int FormulationsCount { get; set; }
    public int StorageLocationsCount { get; set; }
    public int SuppliersCount { get; set; }
    public int CustomersCount { get; set; }
    public int ProductsCount { get; set; }
    public int BatchesCount { get; set; }
    public long TotalUnitsInStock { get; set; }
    public decimal InventoryCostValue { get; set; }
    public decimal InventoryRetailValue { get; set; }

    public int PurchaseOrdersCount { get; set; }
    public int PurchaseInvoicesCount { get; set; }
    public decimal TotalPurchasesAmount { get; set; }
    public int SaleInvoicesCount { get; set; }
    public decimal TotalSalesAmount { get; set; }
    public int SaleReturnsCount { get; set; }
    public int PurchaseReturnsCount { get; set; }

    public int AccountsCount { get; set; }
    public decimal TotalLiquidBalance { get; set; }
    public int ChartOfAccountsCount { get; set; }
    public int PaymentsCount { get; set; }
    public int EmployeesCount { get; set; }
}

public sealed class LicenseStatusResult
{
    public bool Valid { get; set; }
    public string Status { get; set; } = "unlicensed";
    public string? Reason { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? LicenseKeyId { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? RemainingDays { get; set; }
    public int? RemainingHours { get; set; }
    public string? RawPayloadJson { get; set; }
}

public sealed class ImportProgressUpdate
{
    public int Percentage { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class MySqlConnectionOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "karobar";
    public string Username { get; set; } = "root";
    public string Password { get; set; } = string.Empty;
}

public sealed class ImportVerificationReport
{
    public bool Success { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public TimeSpan Elapsed { get; set; }
    public Dictionary<string, int> ImportedTableRowCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int TotalRowsImported => ImportedTableRowCounts.Values.Sum();
    public DatabaseOverviewStats? PostImportStats { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class AuthenticatedUserSession
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public List<string> Roles { get; set; } = [];
    public HashSet<string> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsAdmin => Roles.Any(r =>
        r.Equals("Super Admin", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("super-admin", StringComparison.OrdinalIgnoreCase));
}

public interface IDatabaseInitializerService
{
    string DatabaseFilePath { get; }
    string StorageDirectoryPath { get; }
    Task InitializeAndSeedAsync(CancellationToken cancellationToken = default);
    Task<DatabaseOverviewStats> GetOverviewStatsAsync(CancellationToken cancellationToken = default);
    Task<List<Product>> GetRecentProductsAsync(int limit = 100, string? search = null, CancellationToken cancellationToken = default);
    Task<List<SaleInvoice>> GetRecentSaleInvoicesAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<List<PurchaseInvoice>> GetRecentPurchaseInvoicesAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<List<Customer>> GetRecentCustomersAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<List<Supplier>> GetRecentSuppliersAsync(int limit = 100, CancellationToken cancellationToken = default);
}

public interface ILicenseService
{
    string ComputeMachineId();
    LicenseStatusResult VerifyKey(string rawKey, string? expectedMachineId = null);
    Task<LicenseStatusResult> GetCurrentStatusAsync(CancellationToken cancellationToken = default);
    Task<LicenseStatusResult> ActivateAsync(string rawKey, CancellationToken cancellationToken = default);
    Task DeactivateAsync(CancellationToken cancellationToken = default);
    Task<bool> ClearLicenseIfMachineChangedAsync(CancellationToken cancellationToken = default);
}

public interface ILegacyKarobarImporterService
{
    Task<ImportVerificationReport> ImportFromFileAsync(
        string filePath,
        IProgress<ImportProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ImportVerificationReport> ImportFromMySqlAsync(
        MySqlConnectionOptions options,
        IProgress<ImportProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IAuthService
{
    AuthenticatedUserSession? CurrentSession { get; }
    Task<AuthenticatedUserSession> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<bool> VerifyPasswordAsync(long userId, string password, CancellationToken cancellationToken = default);
    void Logout();
}

public interface ILedgerRebuildService
{
    Task<int> RebuildAllCustomerLedgersAsync(CancellationToken cancellationToken = default);
    Task<int> RebuildAllSupplierLedgersAsync(CancellationToken cancellationToken = default);
}
