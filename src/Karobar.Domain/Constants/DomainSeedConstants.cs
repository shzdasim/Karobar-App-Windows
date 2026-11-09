using Karobar.Domain.Entities;

namespace Karobar.Domain.Constants;

public static class DomainSeedConstants
{
    /// <summary>
    /// Topological dependency order of all 56 application tables for One-Click Legacy Import and Backup/Restore.
    /// </summary>
    public static readonly string[] OrderedTablesForImport =
    [
        "users",
        "roles",
        "permissions",
        "role_has_permissions",
        "model_has_roles",
        "model_has_permissions",
        "user_preferences",
        "dismissed_notifications",
        "settings",
        "theme_settings",
        "barcode_templates",
        "categories",
        "brands",
        "formulations",
        "storage_locations",
        "suppliers",
        "customers",
        "products",
        "product_suppliers",
        "supplier_product_positions",
        "customer_regular_items",
        "customer_whole_sale_prices",
        "batches",
        "user_demands",
        "accounts",
        "cash_drawer_sessions",
        "cash_drawer_transactions",
        "account_transactions",
        "expense_categories",
        "expenses",
        "chart_of_accounts",
        "tax_rates",
        "journal_entries",
        "journal_entry_lines",
        "purchase_orders",
        "purchase_order_items",
        "purchase_invoices",
        "purchase_invoice_items",
        "purchase_returns",
        "purchase_return_items",
        "sale_invoices",
        "sale_invoice_items",
        "sale_returns",
        "sale_return_items",
        "invoice_drafts",
        "payments",
        "payment_allocations",
        "customer_ledgers",
        "supplier_ledgers",
        "stock_adjustments",
        "stock_adjustment_items",
        "stocktake_sessions",
        "stocktake_items",
        "stock_disposals",
        "stock_disposal_items",
        "employees",
        "employee_attendances",
        "employee_advances",
        "payroll_runs",
        "payroll_items",
        "document_histories",
        "backup_logs",
        "backup_schedules",
        "cube_provider_usages"
    ];

    public static readonly string[] DefaultRoleNames =
    [
        "Super Admin",
        "Admin",
        "Pharmacist",
        "Salesman",
        "Cashier",
        "Accountant",
        "Inventory Manager"
    ];

    public static readonly string[] AllPermissions =
    [
        // Core Modules
        "dashboard.view",
        "settings.view", "settings.update",
        "theme.view", "theme.create", "theme.update", "theme.delete", "theme.activate",
        "backup.view", "backup.create", "backup.restore", "backup.delete", "backup.download",
        "user.view", "user.create", "user.update", "user.delete",
        "role.view", "role.create", "role.update", "role.delete",
        "permission.view", "permission.create", "permission.update", "permission.delete",

        // Catalog & People
        "category.view", "category.create", "category.update", "category.delete", "category.import", "category.export",
        "brand.view", "brand.create", "brand.update", "brand.delete", "brand.import", "brand.export",
        "formulation.view", "formulation.create", "formulation.update", "formulation.delete",
        "storage-location.view", "storage-location.create", "storage-location.update", "storage-location.delete", "storage-location.transfer",
        "supplier.view", "supplier.create", "supplier.update", "supplier.delete", "supplier.import", "supplier.export",
        "customer.view", "customer.create", "customer.update", "customer.delete", "customer.import", "customer.export",
        "product.view", "product.create", "product.update", "product.delete", "product.import", "product.export", "product.bulk-price",

        // Inventory, Batches & Barcodes
        "batch.view", "batch.create", "batch.update", "batch.delete", "batch.transfer", "batch.reconcile",
        "batch-expiry.view", "batch-expiry.update", "batch-expiry.export",
        "barcode.view", "barcode.print", "barcode.manage-templates",
        "user-demand.view", "user-demand.create", "user-demand.update", "user-demand.delete",
        "stock-adjustment.view", "stock-adjustment.create", "stock-adjustment.update", "stock-adjustment.delete", "stock-adjustment.export",
        "stock-disposal.view", "stock-disposal.create", "stock-disposal.approve", "stock-disposal.restore", "stock-disposal.delete",
        "stocktake.view", "stocktake.create", "stocktake.count", "stocktake.finalize", "stocktake.delete",

        // Purchasing
        "purchase-order.view", "purchase-order.create", "purchase-order.update", "purchase-order.delete", "purchase-order.receive", "purchase-order.export",
        "purchase-invoice.view", "purchase-invoice.create", "purchase-invoice.update", "purchase-invoice.delete", "purchase-invoice.reverse", "purchase-invoice.export",
        "purchase-return.view", "purchase-return.create", "purchase-return.update", "purchase-return.delete", "purchase-return.export",

        // Sales, POS & Drafts
        "sale-invoice.view", "sale-invoice.create", "sale-invoice.update", "sale-invoice.delete", "sale-invoice.reverse", "sale-invoice.export",
        "sale-invoice.wholesale", "sale-invoice.override-discount", "sale-invoice.override-credit", "sale-invoice.override-expired",
        "pos.view", "pos.create",
        "sale-return.view", "sale-return.create", "sale-return.update", "sale-return.delete", "sale-return.export",
        "invoice-draft.view", "invoice-draft.create", "invoice-draft.update", "invoice-draft.delete", "quotation.convert",

        // Finance, Treasury, Ledgers, Accounting & HR
        "customer-ledger.view", "customer-ledger.create", "customer-ledger.update", "customer-ledger.delete", "customer-ledger.rebuild", "customer-ledger.export",
        "supplier-ledger.view", "supplier-ledger.create", "supplier-ledger.update", "supplier-ledger.delete", "supplier-ledger.rebuild", "supplier-ledger.export",
        "payment.view", "payment.create", "payment.update", "payment.delete", "payment.reverse", "payment.allocate",
        "account.view", "account.create", "account.update", "account.delete", "account.transfer", "account.adjust",
        "cash-drawer.view", "cash-drawer.open", "cash-drawer.close", "cash-drawer.transaction", "cash-drawer.history",
        "expense.view", "expense.create", "expense.update", "expense.delete", "expense-category.manage",
        "accounting.view", "accounting.create", "accounting.reverse", "accounting.export",
        "hr-payroll.view", "hr-payroll.create", "hr-payroll.update", "hr-payroll.delete", "hr-payroll.approve", "hr-payroll.pay",
        "document-history.view",

        // Analytical & Statutory Reports
        "report.cost-of-sale.view", "report.cost-of-sale.export",
        "report.cost-of-sale-detail.view", "report.cost-of-sale-detail.export",
        "report.purchase-detail.view", "report.purchase-detail.export",
        "report.sale-detail.view", "report.sale-detail.export",
        "report.current-stock.view", "report.current-stock.export",
        "report.stock-adjustment.view", "report.stock-adjustment.export",
        "report.product-comprehensive.view", "report.product-comprehensive.export",
        "report.near-expiry-product.view", "report.near-expiry-product.export",
        "report.purchase-returns.view", "report.purchase-returns.export",
        "report.sale-returns.view", "report.sale-returns.export",
        "report.purchase-summary.view", "report.purchase-summary.export",
        "report.outstanding-aging.view", "report.outstanding-aging.export",
        "report.reorder.view", "report.reorder.export",
        "report.net-profit.view", "report.net-profit.export",
        "report.tax-return.view", "report.tax-return.export"
    ];

    public static List<StorageLocation> GetDefaultStorageLocations(DateTime now) =>
    [
        new StorageLocation
        {
            Name = "Shop Counter",
            Code = "SHOP-COUNTER",
            Type = "shop",
            DefaultRack = "A-1",
            IsDefault = true,
            IsActive = true,
            Description = "Primary retail dispensing counter and display shelves",
            Notes = "Default retail dispensing location",
            CreatedAt = now,
            UpdatedAt = now
        },
        new StorageLocation
        {
            Name = "Main Godown",
            Code = "MAIN-GODOWN",
            Type = "godown",
            DefaultRack = "G-1",
            IsDefault = false,
            IsActive = true,
            Description = "Bulk reserve warehouse / backroom storage",
            Notes = "Bulk reserve stockroom",
            CreatedAt = now,
            UpdatedAt = now
        },
        new StorageLocation
        {
            Name = "Cold Fridge",
            Code = "COLD-FRIDGE",
            Type = "cold_storage",
            DefaultRack = "CF-1",
            IsDefault = false,
            IsActive = true,
            Description = "Temperature-controlled refrigerator (2°C - 8°C) for insulin, vaccines & biologics",
            Notes = "Cold chain storage",
            CreatedAt = now,
            UpdatedAt = now
        }
    ];

    public static List<ChartOfAccount> GetDefaultChartOfAccounts(DateTime now) =>
    [
        new() { Code = "1010", Name = "Cash & Bank Equivalents", AccountType = "asset", SubType = "current_asset", NormalBalance = "debit", IsSystem = true, IsActive = true, Description = "Physical cash drawer, bank accounts, and mobile wallets", CreatedAt = now, UpdatedAt = now },
        new() { Code = "1200", Name = "Accounts Receivable (Customers)", AccountType = "asset", SubType = "current_asset", NormalBalance = "debit", IsSystem = true, IsActive = true, Description = "Trade receivables due from credit customers", CreatedAt = now, UpdatedAt = now },
        new() { Code = "1300", Name = "Merchandise Inventory", AccountType = "asset", SubType = "current_asset", NormalBalance = "debit", IsSystem = true, IsActive = true, Description = "On-hand stock valued at moving weighted-average cost", CreatedAt = now, UpdatedAt = now },
        new() { Code = "1400", Name = "Input GST / Sales Tax Receivable", AccountType = "asset", SubType = "current_asset", NormalBalance = "debit", IsSystem = true, IsActive = true, Description = "Input tax paid on supplier purchases eligible for credit", CreatedAt = now, UpdatedAt = now },
        new() { Code = "2100", Name = "Accounts Payable (Suppliers)", AccountType = "liability", SubType = "current_liability", NormalBalance = "credit", IsSystem = true, IsActive = true, Description = "Trade payables owed to pharmaceutical & general suppliers", CreatedAt = now, UpdatedAt = now },
        new() { Code = "2200", Name = "Output GST / Sales Tax Payable", AccountType = "liability", SubType = "current_liability", NormalBalance = "credit", IsSystem = true, IsActive = true, Description = "Output tax collected from customers on sales", CreatedAt = now, UpdatedAt = now },
        new() { Code = "3100", Name = "Owner's Capital & Opening Equity", AccountType = "equity", SubType = "equity", NormalBalance = "credit", IsSystem = true, IsActive = true, Description = "Owner capital injections and opening balance equity plug", CreatedAt = now, UpdatedAt = now },
        new() { Code = "3300", Name = "Retained Earnings", AccountType = "equity", SubType = "equity", NormalBalance = "credit", IsSystem = true, IsActive = true, Description = "Accumulated net operating earnings retained in the business", CreatedAt = now, UpdatedAt = now },
        new() { Code = "4100", Name = "Gross Sales Revenue", AccountType = "revenue", SubType = "operating_revenue", NormalBalance = "credit", IsSystem = true, IsActive = true, Description = "Gross retail and wholesale merchandise sales", CreatedAt = now, UpdatedAt = now },
        new() { Code = "4200", Name = "Sales Returns & Allowances", AccountType = "revenue", SubType = "contra_revenue", NormalBalance = "debit", IsSystem = true, IsActive = true, Description = "Customer sale returns and credit notes", CreatedAt = now, UpdatedAt = now },
        new() { Code = "4300", Name = "Sales Discounts Allowed", AccountType = "revenue", SubType = "contra_revenue", NormalBalance = "debit", IsSystem = true, IsActive = true, Description = "Line-item and invoice-level flat discounts granted", CreatedAt = now, UpdatedAt = now },
        new() { Code = "5100", Name = "Cost of Goods Sold (COGS)", AccountType = "expense", SubType = "cost_of_sales", NormalBalance = "debit", IsSystem = true, IsActive = true, Description = "Direct cost of inventory sold (net of returned stock cost)", CreatedAt = now, UpdatedAt = now },
        new() { Code = "5200", Name = "Inventory Shrinkage & Expiry Write-Offs", AccountType = "expense", SubType = "cost_of_sales", NormalBalance = "debit", IsSystem = true, IsActive = true, Description = "Stock disposal write-offs and negative stock adjustments", CreatedAt = now, UpdatedAt = now },
        new() { Code = "5300", Name = "Operating & Administrative Expenses", AccountType = "expense", SubType = "operating_expense", NormalBalance = "debit", IsSystem = true, IsActive = true, Description = "Rent, utilities, salaries, freight, and general store expenses", CreatedAt = now, UpdatedAt = now }
    ];

    public static List<TaxRate> GetDefaultTaxRates(DateTime now) =>
    [
        new() { Name = "Standard GST (18%)", Code = "GST-18", Rate = 18.00m, TaxType = "gst", AppliesTo = "both", IsDefault = false, IsActive = true, Description = "Pakistan Federal/Provincial Standard Sales Tax (18%)", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Legacy GST (17%)", Code = "GST-17", Rate = 17.00m, TaxType = "gst", AppliesTo = "both", IsDefault = false, IsActive = true, Description = "Historical 17% GST rate", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Reduced Pharma Sales Tax (1%)", Code = "PHARMA-REDUCED-1", Rate = 1.00m, TaxType = "gst", AppliesTo = "both", IsDefault = false, IsActive = true, Description = "Reduced pharmaceutical supply tax rate", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Zero-Rated Supply (0%)", Code = "ZERO-RATED", Rate = 0.00m, TaxType = "zero_rated", AppliesTo = "both", IsDefault = true, IsActive = true, Description = "Zero-rated essential goods", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Exempt Medicines & Supplies (0%)", Code = "EXEMPT", Rate = 0.00m, TaxType = "exempt", AppliesTo = "both", IsDefault = false, IsActive = true, Description = "Tax-exempt pharmaceutical & retail supplies", CreatedAt = now, UpdatedAt = now }
    ];

    public static List<ExpenseCategory> GetDefaultExpenseCategories(DateTime now) =>
    [
        new() { Name = "Shop Rent & Lease", Slug = "shop_rent", Color = "#6366f1", IsSystem = true, IsActive = true, Description = "Monthly store and godown rent", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Electricity, Gas & Utilities", Slug = "utilities", Color = "#f59e0b", IsSystem = true, IsActive = true, Description = "WAPDA/K-Electric, generator fuel, gas, water, and internet bills", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Staff Salaries & Wages", Slug = "salaries_wages", Color = "#10b981", IsSystem = true, IsActive = true, Description = "Monthly payroll, overtime, and salesman commissions", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Freight, Delivery & Logistics", Slug = "freight_delivery", Color = "#0ea5e9", IsSystem = true, IsActive = true, Description = "Rider fuel, transport, and supplier cartage charges", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Packaging & POS Supplies", Slug = "packaging_pos", Color = "#8b5cf6", IsSystem = true, IsActive = true, Description = "Shopping bags, thermal paper rolls, barcode stickers, and stationery", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Store Repairs & Maintenance", Slug = "repairs_maintenance", Color = "#ec4899", IsSystem = true, IsActive = true, Description = "AC/fridge servicing, shelving, electrical, and IT repairs", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Tea, Refreshments & Daily Petty Cash", Slug = "refreshments_petty", Color = "#14b8a6", IsSystem = true, IsActive = true, Description = "Daily counter hospitality, staff tea/meals, and miscellaneous petty expenses", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Licensing, Tax & Legal Fees", Slug = "licensing_legal", Color = "#64748b", IsSystem = true, IsActive = true, Description = "Drug license renewal, trade association dues, and accounting fees", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Miscellaneous Operating Expense", Slug = "miscellaneous", Color = "#94a3b8", IsSystem = true, IsActive = true, Description = "General store operating expenses", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Stock Disposal & Expiry Write-Off", Slug = "stock_disposal_writeoff", Color = "#ef4444", IsSystem = true, IsActive = true, Description = "Automated cost write-off from finalized expired/damaged stock disposals", CreatedAt = now, UpdatedAt = now },
        new() { Name = "Stock Shrinkage & Adjustment Loss", Slug = "stock_shrinkage_writeoff", Color = "#dc2626", IsSystem = true, IsActive = true, Description = "Automated inventory shrinkage expense from negative stock adjustments", CreatedAt = now, UpdatedAt = now }
    ];

    public static List<ThemeSetting> GetDefaultThemes(DateTime now) =>
    [
        new()
        {
            Name = "Blue & Purple (Default)",
            PrimaryColor = "#2563eb", PrimaryHover = "#1d4ed8", PrimaryLight = "#dbeafe",
            SecondaryColor = "#7c3aed", SecondaryHover = "#6d28d9", SecondaryLight = "#ede9fe",
            TertiaryColor = "#06b6d4", TertiaryHover = "#0891b2", TertiaryLight = "#cffafe",
            ButtonStyle = "modern", IsActive = true, IsDefault = true,
            CreatedAt = now, UpdatedAt = now
        },
        new()
        {
            Name = "Emerald & Teal",
            PrimaryColor = "#059669", PrimaryHover = "#047857", PrimaryLight = "#d1fae5",
            SecondaryColor = "#0d9488", SecondaryHover = "#0f766e", SecondaryLight = "#ccfbf1",
            TertiaryColor = "#f59e0b", TertiaryHover = "#d97706", TertiaryLight = "#fef3c7",
            ButtonStyle = "modern", IsActive = false, IsDefault = false,
            CreatedAt = now, UpdatedAt = now
        },
        new()
        {
            Name = "Indigo & Cyan",
            PrimaryColor = "#4f46e5", PrimaryHover = "#4338ca", PrimaryLight = "#e0e7ff",
            SecondaryColor = "#0891b2", SecondaryHover = "#0e7490", SecondaryLight = "#cffafe",
            TertiaryColor = "#8b5cf6", TertiaryHover = "#7c3aed", TertiaryLight = "#ede9fe",
            ButtonStyle = "rounded-sm", IsActive = false, IsDefault = false,
            CreatedAt = now, UpdatedAt = now
        }
    ];

    public static List<BarcodeTemplate> GetDefaultBarcodeTemplates(DateTime now) =>
    [
        new() { Name = "Thermal Roll 50x25mm (1-Up)", Description = "Wide thermal sticker for pharmacy shelves (Batch & Expiry)", LayoutType = "roll", LabelWidthMm = 50, LabelHeightMm = 25, Orientation = "landscape", Columns = 1, Rows = 1, GapXMm = 0, GapYMm = 3, MarginTopMm = 1, MarginLeftMm = 1, MarginRightMm = 1, MarginBottomMm = 1, BarcodeFormat = "CODE128", BarcodeHeightMm = 10, ShowStoreName = true, ShowProductName = true, ShowPrice = true, ShowBatch = true, ShowExpiry = true, ShowCode = true, IsDefault = true, CreatedAt = now, UpdatedAt = now },
        new() { Name = "Thermal Roll 38x25mm (1-Up)", Description = "Standard single-column thermal barcode roll", LayoutType = "roll", LabelWidthMm = 38, LabelHeightMm = 25, Orientation = "landscape", Columns = 1, Rows = 1, GapXMm = 0, GapYMm = 3, MarginTopMm = 1, MarginLeftMm = 1, MarginRightMm = 1, MarginBottomMm = 1, BarcodeFormat = "CODE128", BarcodeHeightMm = 9, ShowStoreName = true, ShowProductName = true, ShowPrice = true, ShowBatch = false, ShowExpiry = true, ShowCode = true, IsDefault = false, CreatedAt = now, UpdatedAt = now },
        new() { Name = "Small Tag 30x20mm (1-Up)", Description = "Compact retail / cosmetics thermal sticker", LayoutType = "roll", LabelWidthMm = 30, LabelHeightMm = 20, Orientation = "landscape", Columns = 1, Rows = 1, GapXMm = 0, GapYMm = 2, MarginTopMm = 1, MarginLeftMm = 1, MarginRightMm = 1, MarginBottomMm = 1, BarcodeFormat = "CODE128", BarcodeHeightMm = 7, ShowStoreName = false, ShowProductName = true, ShowPrice = true, ShowBatch = false, ShowExpiry = false, ShowCode = true, IsDefault = false, CreatedAt = now, UpdatedAt = now },
        new() { Name = "Large Shelf Label 60x40mm (1-Up)", Description = "Large warehouse / shelf bin barcode label", LayoutType = "roll", LabelWidthMm = 60, LabelHeightMm = 40, Orientation = "landscape", Columns = 1, Rows = 1, GapXMm = 0, GapYMm = 3, MarginTopMm = 2, MarginLeftMm = 2, MarginRightMm = 2, MarginBottomMm = 2, BarcodeFormat = "CODE128", BarcodeHeightMm = 13, ShowStoreName = true, ShowProductName = true, ShowPrice = true, ShowBatch = true, ShowExpiry = true, ShowCode = true, IsDefault = false, CreatedAt = now, UpdatedAt = now },
        new() { Name = "A4 Sheet 40 Labels (4x10)", Description = "Standard A4 adhesive sheet (48.5x25.4mm)", LayoutType = "sheet", LabelWidthMm = 48.5m, LabelHeightMm = 25.4m, Orientation = "portrait", Columns = 4, Rows = 10, GapXMm = 2, GapYMm = 0, MarginTopMm = 10, MarginLeftMm = 5, MarginRightMm = 5, MarginBottomMm = 10, BarcodeFormat = "CODE128", BarcodeHeightMm = 9, ShowStoreName = true, ShowProductName = true, ShowPrice = true, ShowBatch = true, ShowExpiry = true, ShowCode = true, IsDefault = false, CreatedAt = now, UpdatedAt = now },
        new() { Name = "A4 Sheet 65 Labels (5x13)", Description = "Compact A4 retail price stickers (38x21.2mm)", LayoutType = "sheet", LabelWidthMm = 38m, LabelHeightMm = 21.2m, Orientation = "portrait", Columns = 5, Rows = 13, GapXMm = 2.5m, GapYMm = 0, MarginTopMm = 10, MarginLeftMm = 5, MarginRightMm = 5, MarginBottomMm = 10, BarcodeFormat = "CODE128", BarcodeHeightMm = 8, ShowStoreName = true, ShowProductName = true, ShowPrice = true, ShowBatch = false, ShowExpiry = false, ShowCode = true, IsDefault = false, CreatedAt = now, UpdatedAt = now }
    ];
}
