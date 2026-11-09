using Karobar.Application.Interfaces;
using Karobar.Domain.Constants;
using Karobar.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Karobar.Infrastructure.Persistence;

public sealed class KarobarDatabaseInitializer : IDatabaseInitializerService
{
    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;
    private readonly ILogger<KarobarDatabaseInitializer> _logger;

    public string DatabaseFilePath { get; }
    public string StorageDirectoryPath { get; }

    public KarobarDatabaseInitializer(
        IDbContextFactory<KarobarDbContext> dbFactory,
        ILogger<KarobarDatabaseInitializer> logger,
        string? customDataDir = null)
    {
        _dbFactory = dbFactory;
        _logger = logger;

        var baseDir = !string.IsNullOrWhiteSpace(customDataDir)
            ? customDataDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Karobar");

        var dataDir = Path.Combine(baseDir, "Data");
        StorageDirectoryPath = Path.Combine(baseDir, "Storage");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(StorageDirectoryPath);
        Directory.CreateDirectory(Path.Combine(StorageDirectoryPath, "logos"));
        Directory.CreateDirectory(Path.Combine(StorageDirectoryPath, "products"));
        Directory.CreateDirectory(Path.Combine(StorageDirectoryPath, "backups"));

        DatabaseFilePath = Path.Combine(dataDir, "karobar.db");
    }

    public async Task InitializeAndSeedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        await db.Database.EnsureCreatedAsync(cancellationToken);

        // Configure SQLite WAL mode for concurrent read/write desktop speed and crash resilience
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);

        // Safely upgrade 'users' table in-place if an older database file exists without 'username'
        await EnsureUserColumnsExistAsync(db, cancellationToken);
        await EnsureRegularCustomerColumnsExistAsync(db, cancellationToken);
        await EnsureProductSupplierColumnsExistAsync(db, cancellationToken);

        var now = DateTime.UtcNow;

        // 1. Seed Roles
        var existingRoles = await db.Roles.ToListAsync(cancellationToken);
        foreach (var roleName in DomainSeedConstants.DefaultRoleNames)
        {
            if (!existingRoles.Any(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase)))
            {
                var role = new Role { Name = roleName, GuardName = "sanctum", CreatedAt = now, UpdatedAt = now };
                db.Roles.Add(role);
                existingRoles.Add(role);
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        // 2. Seed All Granular Permissions
        var existingPerms = await db.Permissions.Select(p => p.Name).ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);
        var newPerms = new List<Permission>();
        foreach (var permName in DomainSeedConstants.AllPermissions)
        {
            if (!existingPerms.Contains(permName))
            {
                newPerms.Add(new Permission { Name = permName, GuardName = "sanctum", CreatedAt = now, UpdatedAt = now });
            }
        }
        if (newPerms.Count > 0)
        {
            db.Permissions.AddRange(newPerms);
            await db.SaveChangesAsync(cancellationToken);
        }

        // Assign all permissions to Super Admin and Admin roles
        var allPermissionIds = await db.Permissions.Select(p => p.Id).ToListAsync(cancellationToken);
        var adminRoleIds = await db.Roles
            .Where(r => r.Name == "Super Admin" || r.Name == "Admin")
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var existingRolePerms = await db.RolePermissions
            .Where(rp => adminRoleIds.Contains(rp.RoleId))
            .ToListAsync(cancellationToken);

        var existingRolePermSet = existingRolePerms
            .Select(rp => (rp.RoleId, rp.PermissionId))
            .ToHashSet();

        foreach (var roleId in adminRoleIds)
        {
            foreach (var permId in allPermissionIds)
            {
                if (!existingRolePermSet.Contains((roleId, permId)))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permId });
                }
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        // 3. Backfill any empty usernames (e.g. after legacy import) and ensure built-in 'admin' user exists
        var allUsers = await db.Users.ToListAsync(cancellationToken);
        var usedUsernames = allUsers
            .Where(u => !string.IsNullOrWhiteSpace(u.Username))
            .Select(u => u.Username.ToLowerInvariant())
            .ToHashSet();

        foreach (var u in allUsers.Where(u => string.IsNullOrWhiteSpace(u.Username)))
        {
            if (!usedUsernames.Contains("admin"))
            {
                u.Username = "admin";
                usedUsernames.Add("admin");
            }
            else
            {
                var prefix = !string.IsNullOrWhiteSpace(u.Email) && u.Email.Contains('@')
                    ? u.Email.Split('@')[0].Trim().ToLowerInvariant()
                    : $"user{u.Id}";
                var candidate = string.IsNullOrWhiteSpace(prefix) ? $"user{u.Id}" : prefix;
                if (usedUsernames.Contains(candidate))
                    candidate = $"{candidate}_{u.Id}";
                u.Username = candidate;
                usedUsernames.Add(candidate);
            }
        }
        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // Ensure built-in admin user (username: "admin", password: "password") exists
        var adminUser = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == "admin", cancellationToken);
        if (adminUser == null)
        {
            adminUser = new User
            {
                Username = "admin",
                Name = "Admin User",
                Email = "admin@karobar.app",
                Status = "active",
                IsAdmin = true,
                Password = BCrypt.Net.BCrypt.HashPassword("password"),
                EmailVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Users.Add(adminUser);
            await db.SaveChangesAsync(cancellationToken);

            var superAdminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Super Admin" || r.Name == "Admin", cancellationToken);
            if (superAdminRole != null)
            {
                db.UserRoles.Add(new UserRole
                {
                    RoleId = superAdminRole.Id,
                    ModelId = adminUser.Id,
                    ModelType = "App\\Models\\User"
                });
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        else if (string.IsNullOrWhiteSpace(adminUser.Status))
        {
            adminUser.Status = "active";
            adminUser.IsAdmin = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_users_username\" ON \"users\" (\"username\");",
            cancellationToken);

        // 4. Seed Singleton Settings (Id = 1)
        if (!await db.Settings.AnyAsync(cancellationToken))
        {
            db.Settings.Add(new Setting
            {
                Id = 1,
                StoreName = "Karobar App",
                PrinterType = "thermal",
                ThermalTemplate = "standard",
                A4Template = "standard",
                SharePaperSize = "a4",
                ShareShowLogo = true,
                NavigationStyle = "sidebar",
                SaleSystem = "retail_wholesale",
                ShopType = "pharmacy",
                CurrencyCode = "PKR",
                CurrencySymbol = "Rs.",
                CurrencyPosition = "before",
                CurrencyDecimalPlaces = 2,
                ExpiryWarningDays = 90,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        // 5. Seed Default Walk-In Customer
        if (!await db.Customers.AnyAsync(c => c.Name == "WALK-IN-CUSTOMER", cancellationToken))
        {
            db.Customers.Add(new Customer
            {
                Name = "WALK-IN-CUSTOMER",
                CustomerType = "retail",
                CreditStatus = "active",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        // 6. Seed Default Storage Locations (Shop Counter, Main Godown, Cold Fridge)
        if (!await db.StorageLocations.AnyAsync(cancellationToken))
        {
            db.StorageLocations.AddRange(DomainSeedConstants.GetDefaultStorageLocations(now));
            await db.SaveChangesAsync(cancellationToken);
        }

        // 7. Seed Default Cash Drawer Account
        if (!await db.Accounts.AnyAsync(a => a.Code == "CASH-DRAWER", cancellationToken))
        {
            db.Accounts.Add(new Account
            {
                Name = "Cash in Hand (POS Drawer)",
                Code = "CASH-DRAWER",
                AccountType = "cash_drawer",
                ProviderKey = "cash_drawer",
                Currency = "PKR",
                IsDefault = true,
                IsActive = true,
                SyncWithCashDrawer = true,
                Notes = "Primary physical cash register drawer",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        // 8. Seed Default Chart of Accounts (1010 - 5300)
        var existingCoaCodes = await db.ChartOfAccounts.Select(c => c.Code).ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);
        foreach (var coa in DomainSeedConstants.GetDefaultChartOfAccounts(now))
        {
            if (!existingCoaCodes.Contains(coa.Code))
            {
                db.ChartOfAccounts.Add(coa);
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        // 9. Seed Default Tax Rates
        if (!await db.TaxRates.AnyAsync(cancellationToken))
        {
            db.TaxRates.AddRange(DomainSeedConstants.GetDefaultTaxRates(now));
            await db.SaveChangesAsync(cancellationToken);
        }

        // 10. Seed Default Expense Categories
        var existingExpSlugs = await db.ExpenseCategories.Select(e => e.Slug).ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);
        foreach (var cat in DomainSeedConstants.GetDefaultExpenseCategories(now))
        {
            if (!existingExpSlugs.Contains(cat.Slug))
            {
                db.ExpenseCategories.Add(cat);
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        // 11. Seed Default Themes
        if (!await db.ThemeSettings.AnyAsync(cancellationToken))
        {
            db.ThemeSettings.AddRange(DomainSeedConstants.GetDefaultThemes(now));
            await db.SaveChangesAsync(cancellationToken);
        }

        // 12. Seed Default Barcode Templates
        if (!await db.BarcodeTemplates.AnyAsync(cancellationToken))
        {
            db.BarcodeTemplates.AddRange(DomainSeedConstants.GetDefaultBarcodeTemplates(now));
            await db.SaveChangesAsync(cancellationToken);
        }

        // 13. Seed Default Backup Schedule
        if (!await db.BackupSchedules.AnyAsync(cancellationToken))
        {
            db.BackupSchedules.Add(new BackupSchedule
            {
                Id = 1,
                IsAutomated = false,
                Frequency = "daily",
                TimeOfDay = "23:00",
                BackupType = "full",
                RetentionDays = 30,
                MaxBackups = 10,
                StorageDestination = "local",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Karobar SQLite database initialized and seeded at {Path}", DatabaseFilePath);
    }

    public async Task<DatabaseOverviewStats> GetOverviewStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        var fileInfo = File.Exists(DatabaseFilePath) ? new FileInfo(DatabaseFilePath) : null;

        var products = await db.Products.AsNoTracking()
            .Select(p => new
            {
                p.Quantity,
                p.PackSize,
                p.AvgPrice,
                p.UnitPurchasePrice,
                p.PackPurchasePrice,
                p.UnitSalePrice
            })
            .ToListAsync(cancellationToken);

        long totalUnits = 0;
        decimal invCost = 0m;
        decimal invRetail = 0m;

        foreach (var p in products)
        {
            if (p.Quantity <= 0) continue;
            totalUnits += p.Quantity;
            var effectiveCost = p.AvgPrice > 0m
                ? p.AvgPrice
                : (p.UnitPurchasePrice > 0m
                    ? p.UnitPurchasePrice
                    : (p.PackSize > 0 ? p.PackPurchasePrice / p.PackSize : 0m));

            invCost += p.Quantity * effectiveCost;
            invRetail += p.Quantity * p.UnitSalePrice;
        }

        var totalPurchases = await db.PurchaseInvoices.AsNoTracking()
            .Where(pi => pi.Status != "reversed")
            .SumAsync(pi => (decimal?)pi.TotalAmount, cancellationToken) ?? 0m;

        var totalSales = await db.SaleInvoices.AsNoTracking()
            .Where(si => si.Status != "reversed")
            .SumAsync(si => (decimal?)si.Total, cancellationToken) ?? 0m;

        var totalLiquid = await db.Accounts.AsNoTracking()
            .Where(a => a.IsActive)
            .SumAsync(a => (decimal?)a.CurrentBalance, cancellationToken) ?? 0m;

        return new DatabaseOverviewStats
        {
            DatabaseFilePath = DatabaseFilePath,
            DatabaseSizeBytes = fileInfo?.Length ?? 0,
            StoreName = setting?.StoreName ?? "Karobar App",
            ShopType = setting?.ShopType ?? "pharmacy",
            SaleSystem = setting?.SaleSystem ?? "retail_wholesale",
            CurrencySymbol = setting?.CurrencySymbol ?? "Rs.",
            IsSetupCompleted = setting?.IsSetupCompleted ?? false,

            UsersCount = await db.Users.CountAsync(cancellationToken),
            RolesCount = await db.Roles.CountAsync(cancellationToken),
            PermissionsCount = await db.Permissions.CountAsync(cancellationToken),
            CategoriesCount = await db.Categories.CountAsync(cancellationToken),
            BrandsCount = await db.Brands.CountAsync(cancellationToken),
            FormulationsCount = await db.Formulations.CountAsync(cancellationToken),
            StorageLocationsCount = await db.StorageLocations.CountAsync(cancellationToken),
            SuppliersCount = await db.Suppliers.CountAsync(cancellationToken),
            CustomersCount = await db.Customers.CountAsync(cancellationToken),
            ProductsCount = products.Count,
            BatchesCount = await db.Batches.CountAsync(cancellationToken),
            TotalUnitsInStock = totalUnits,
            InventoryCostValue = Math.Round(invCost, 2),
            InventoryRetailValue = Math.Round(invRetail, 2),

            PurchaseOrdersCount = await db.PurchaseOrders.CountAsync(cancellationToken),
            PurchaseInvoicesCount = await db.PurchaseInvoices.CountAsync(cancellationToken),
            TotalPurchasesAmount = Math.Round(totalPurchases, 2),
            SaleInvoicesCount = await db.SaleInvoices.CountAsync(cancellationToken),
            TotalSalesAmount = Math.Round(totalSales, 2),
            SaleReturnsCount = await db.SaleReturns.CountAsync(cancellationToken),
            PurchaseReturnsCount = await db.PurchaseReturns.CountAsync(cancellationToken),

            AccountsCount = await db.Accounts.CountAsync(cancellationToken),
            TotalLiquidBalance = Math.Round(totalLiquid, 2),
            ChartOfAccountsCount = await db.ChartOfAccounts.CountAsync(cancellationToken),
            PaymentsCount = await db.Payments.CountAsync(cancellationToken),
            EmployeesCount = await db.Employees.CountAsync(cancellationToken)
        };
    }

    public async Task<List<Product>> GetRecentProductsAsync(int limit = 100, string? search = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{term}%") ||
                EF.Functions.Like(p.ProductCode, $"%{term}%") ||
                (p.Formulation != null && EF.Functions.Like(p.Formulation, $"%{term}%")));
        }

        return await query
            .OrderBy(p => p.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SaleInvoice>> GetRecentSaleInvoicesAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.SaleInvoices.AsNoTracking()
            .OrderByDescending(s => s.Date)
            .ThenByDescending(s => s.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PurchaseInvoice>> GetRecentPurchaseInvoicesAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.PurchaseInvoices.AsNoTracking()
            .OrderByDescending(p => p.PostedDate)
            .ThenByDescending(p => p.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Customer>> GetRecentCustomersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Customers.AsNoTracking()
            .OrderBy(c => c.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Supplier>> GetRecentSuppliersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Suppliers.AsNoTracking()
            .OrderBy(s => s.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private static async Task EnsureUserColumnsExistAsync(KarobarDbContext db, CancellationToken cancellationToken)
    {
        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await conn.OpenAsync(cancellationToken);
        }

        try
        {
            var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"users\");";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingCols.Add(reader.GetString(1));
                }
            }

            if (existingCols.Count == 0)
                return;

            var alterCommands = new List<string>();
            if (!existingCols.Contains("username"))
                alterCommands.Add("ALTER TABLE \"users\" ADD COLUMN \"username\" TEXT NOT NULL DEFAULT '';");
            if (!existingCols.Contains("status"))
                alterCommands.Add("ALTER TABLE \"users\" ADD COLUMN \"status\" TEXT NOT NULL DEFAULT 'active';");
            if (!existingCols.Contains("is_admin"))
                alterCommands.Add("ALTER TABLE \"users\" ADD COLUMN \"is_admin\" INTEGER NOT NULL DEFAULT 0;");
            if (!existingCols.Contains("pin_code"))
                alterCommands.Add("ALTER TABLE \"users\" ADD COLUMN \"pin_code\" TEXT NULL;");
            if (!existingCols.Contains("remember_token_expires_at"))
                alterCommands.Add("ALTER TABLE \"users\" ADD COLUMN \"remember_token_expires_at\" TEXT NULL;");
            if (!existingCols.Contains("preferences"))
                alterCommands.Add("ALTER TABLE \"users\" ADD COLUMN \"preferences\" TEXT NULL;");

            foreach (var sql in alterCommands)
            {
                await using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = sql;
                await alterCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await conn.CloseAsync();
            }
        }
    }

    private static async Task EnsureRegularCustomerColumnsExistAsync(KarobarDbContext db, CancellationToken cancellationToken)
    {
        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await conn.OpenAsync(cancellationToken);
        }

        try
        {
            var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"customer_regular_items\");";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingCols.Add(reader.GetString(1));
                }
            }

            if (existingCols.Count == 0)
                return;

            var alterCommands = new List<string>();
            if (!existingCols.Contains("pack_quantity"))
                alterCommands.Add("ALTER TABLE \"customer_regular_items\" ADD COLUMN \"pack_quantity\" INTEGER NOT NULL DEFAULT 0;");
            if (!existingCols.Contains("unit_quantity"))
                alterCommands.Add("ALTER TABLE \"customer_regular_items\" ADD COLUMN \"unit_quantity\" INTEGER NOT NULL DEFAULT 1;");
            if (!existingCols.Contains("dosage_notes"))
                alterCommands.Add("ALTER TABLE \"customer_regular_items\" ADD COLUMN \"dosage_notes\" TEXT NULL;");

            foreach (var sql in alterCommands)
            {
                await using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = sql;
                await alterCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await conn.CloseAsync();
            }
        }
    }

    private static async Task EnsureProductSupplierColumnsExistAsync(KarobarDbContext db, CancellationToken cancellationToken)
    {
        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await conn.OpenAsync(cancellationToken);
        }

        try
        {
            var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"product_suppliers\");";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingCols.Add(reader.GetString(1));
                }
            }

            if (existingCols.Count == 0)
                return;

            var alterCommands = new List<string>();
            if (!existingCols.Contains("supplier_sku"))
                alterCommands.Add("ALTER TABLE \"product_suppliers\" ADD COLUMN \"supplier_sku\" TEXT NULL;");
            if (!existingCols.Contains("last_purchase_price"))
                alterCommands.Add("ALTER TABLE \"product_suppliers\" ADD COLUMN \"last_purchase_price\" TEXT NULL;");
            if (!existingCols.Contains("lead_time_days"))
                alterCommands.Add("ALTER TABLE \"product_suppliers\" ADD COLUMN \"lead_time_days\" INTEGER NULL;");
            if (!existingCols.Contains("min_order_qty"))
                alterCommands.Add("ALTER TABLE \"product_suppliers\" ADD COLUMN \"min_order_qty\" INTEGER NOT NULL DEFAULT 1;");
            if (!existingCols.Contains("is_preferred"))
                alterCommands.Add("ALTER TABLE \"product_suppliers\" ADD COLUMN \"is_preferred\" INTEGER NOT NULL DEFAULT 0;");
            if (!existingCols.Contains("notes"))
                alterCommands.Add("ALTER TABLE \"product_suppliers\" ADD COLUMN \"notes\" TEXT NULL;");
            if (!existingCols.Contains("created_at"))
                alterCommands.Add("ALTER TABLE \"product_suppliers\" ADD COLUMN \"created_at\" TEXT NULL;");
            if (!existingCols.Contains("updated_at"))
                alterCommands.Add("ALTER TABLE \"product_suppliers\" ADD COLUMN \"updated_at\" TEXT NULL;");

            foreach (var sql in alterCommands)
            {
                await using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = sql;
                await alterCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await conn.CloseAsync();
            }
        }
    }
}
