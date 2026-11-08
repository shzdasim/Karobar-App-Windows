using Karobar.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Persistence;

public sealed class KarobarDbContext : DbContext
{
    public KarobarDbContext(DbContextOptions<KarobarDbContext> options) : base(options)
    {
    }

    // Auth, RBAC & System
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<DismissedNotification> DismissedNotifications => Set<DismissedNotification>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<ThemeSetting> ThemeSettings => Set<ThemeSetting>();
    public DbSet<BarcodeTemplate> BarcodeTemplates => Set<BarcodeTemplate>();
    public DbSet<BackupLog> BackupLogs => Set<BackupLog>();
    public DbSet<BackupSchedule> BackupSchedules => Set<BackupSchedule>();
    public DbSet<CubeProviderUsage> CubeProviderUsages => Set<CubeProviderUsage>();

    // Catalog & Inventory
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Formulation> Formulations => Set<Formulation>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerRegularItem> CustomerRegularItems => Set<CustomerRegularItem>();
    public DbSet<CustomerWholeSalePrice> CustomerWholeSalePrices => Set<CustomerWholeSalePrice>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductSupplier> ProductSuppliers => Set<ProductSupplier>();
    public DbSet<SupplierProductPosition> SupplierProductPositions => Set<SupplierProductPosition>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<UserDemand> UserDemands => Set<UserDemand>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentItem> StockAdjustmentItems => Set<StockAdjustmentItem>();
    public DbSet<StocktakeSession> StocktakeSessions => Set<StocktakeSession>();
    public DbSet<StocktakeItem> StocktakeItems => Set<StocktakeItem>();
    public DbSet<StockDisposal> StockDisposals => Set<StockDisposal>();
    public DbSet<StockDisposalItem> StockDisposalItems => Set<StockDisposalItem>();

    // Invoicing, Orders, Returns, Drafts & Audit
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems => Set<PurchaseInvoiceItem>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnItem> PurchaseReturnItems => Set<PurchaseReturnItem>();
    public DbSet<SaleInvoice> SaleInvoices => Set<SaleInvoice>();
    public DbSet<SaleInvoiceItem> SaleInvoiceItems => Set<SaleInvoiceItem>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();
    public DbSet<InvoiceDraft> InvoiceDrafts => Set<InvoiceDraft>();
    public DbSet<DocumentHistory> DocumentHistories => Set<DocumentHistory>();

    // Finance, Treasury, Ledgers, Accounting & HR
    public DbSet<CustomerLedger> CustomerLedgers => Set<CustomerLedger>();
    public DbSet<SupplierLedger> SupplierLedgers => Set<SupplierLedger>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountTransaction> AccountTransactions => Set<AccountTransaction>();
    public DbSet<CashDrawerSession> CashDrawerSessions => Set<CashDrawerSession>();
    public DbSet<CashDrawerTransaction> CashDrawerTransactions => Set<CashDrawerTransaction>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeAttendance> EmployeeAttendances => Set<EmployeeAttendance>();
    public DbSet<EmployeeAdvance> EmployeeAdvances => Set<EmployeeAdvance>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollItem> PayrollItems => Set<PayrollItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Composite keys for Spatie RBAC & Pivot tables
        modelBuilder.Entity<RolePermission>()
            .HasKey(rp => new { rp.PermissionId, rp.RoleId });

        modelBuilder.Entity<UserRole>()
            .HasKey(ur => new { ur.RoleId, ur.ModelId, ur.ModelType });

        modelBuilder.Entity<UserPermission>()
            .HasKey(up => new { up.PermissionId, up.ModelId, up.ModelType });

        modelBuilder.Entity<ProductSupplier>()
            .HasKey(ps => new { ps.ProductId, ps.SupplierId });

        // High-performance indexes matching Laravel migrations
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Product>()
            .HasIndex(p => p.ProductCode)
            .IsUnique();

        modelBuilder.Entity<Product>()
            .HasIndex(p => p.Name);

        modelBuilder.Entity<Product>()
            .HasIndex(p => p.Barcode);

        // Non-unique composite index on batches so a batch can span multiple StorageLocations
        modelBuilder.Entity<Batch>()
            .HasIndex(b => new { b.ProductId, b.BatchNumber, b.StorageLocationId });

        modelBuilder.Entity<Batch>()
            .HasIndex(b => b.ExpiryDate);

        modelBuilder.Entity<PurchaseInvoice>()
            .HasIndex(pi => pi.PostedNumber)
            .IsUnique();

        modelBuilder.Entity<PurchaseInvoice>()
            .HasIndex(pi => pi.PostedDate);

        modelBuilder.Entity<SaleInvoice>()
            .HasIndex(si => si.PostedNumber)
            .IsUnique();

        modelBuilder.Entity<SaleInvoice>()
            .HasIndex(si => si.Date);

        modelBuilder.Entity<CustomerLedger>()
            .HasIndex(cl => new { cl.CustomerId, cl.EntryDate });

        modelBuilder.Entity<SupplierLedger>()
            .HasIndex(sl => new { sl.SupplierId, sl.EntryDate });

        modelBuilder.Entity<Payment>()
            .HasIndex(p => p.VoucherNumber)
            .IsUnique();

        modelBuilder.Entity<ChartOfAccount>()
            .HasIndex(coa => coa.Code)
            .IsUnique();

        modelBuilder.Entity<JournalEntry>()
            .HasIndex(je => je.EntryNumber)
            .IsUnique();
    }
}
