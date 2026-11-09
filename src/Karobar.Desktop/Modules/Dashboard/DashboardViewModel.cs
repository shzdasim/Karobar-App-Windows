using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Karobar.Application.Interfaces;

namespace Karobar.Desktop.ViewModels;

public sealed class ModuleStatTileModel
{
    public string Title { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Subtext { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = "◈";
    public string IconColorHex { get; init; } = "#3B82F6";
    public string IconBgHex { get; init; } = "#EFF6FF";
    public string BorderAccentHex { get; init; } = "#BFDBFE";
}

public sealed class ChartDayPointModel
{
    public string DateLabel { get; init; } = string.Empty;
    public decimal Sales { get; init; }
    public decimal Purchases { get; init; }
    public decimal SaleReturns { get; init; }
    public decimal PurchaseReturns { get; init; }
    public double SalesBarHeight { get; init; }
    public double PurchasesBarHeight { get; init; }
    public string SalesFormatted => $"Rs {Sales:N0}";
    public string PurchasesFormatted => $"Rs {Purchases:N0}";
    public string TooltipText => $"{DateLabel}\nSales: Rs {Sales:N2}\nPurchases: Rs {Purchases:N2}";
}

public sealed class RankedItemModel
{
    public int Rank { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Meta { get; init; } = string.Empty;
    public string AmountFormatted { get; init; } = string.Empty;
    public double ProgressRatio { get; init; } // 0.0 to 1.0
    public double BarWidth => Math.Max(8, Math.Round(ProgressRatio * 220, 1));
    public string RankBgHex { get; init; } = "#EFF6FF";
    public string RankFgHex { get; init; } = "#2563EB";
    public string BarColorHex { get; init; } = "#3B82F6";
}

public sealed class SnapshotLineModel
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string DotColorHex { get; init; } = "#3B82F6";
}

public sealed class NearExpiryRowModel
{
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public string BrandName { get; init; } = string.Empty;
    public string BatchNumber { get; init; } = string.Empty;
    public string ExpiryDate { get; init; } = string.Empty;
    public int DaysRemaining { get; init; }
    public int Quantity { get; init; }
    public string QuantityFormatted => $"{Quantity:N0}";

    public string DaysLeftText =>
        DaysRemaining < 0
            ? $"{Math.Abs(DaysRemaining)}d ago"
            : $"{DaysRemaining}d left";

    public string DaysBadgeBg =>
        DaysRemaining <= 30 ? "#FFE4E6" :
        DaysRemaining <= 90 ? "#FEF3C7" : "#D1FAE5";

    public string DaysBadgeFg =>
        DaysRemaining <= 30 ? "#E11D48" :
        DaysRemaining <= 90 ? "#D97706" : "#059669";
}

public partial class DashboardViewModel : ObservableObject
{
    private readonly Action<string>? _navigateCallback;
    private readonly IToastNotificationService? _toastService;
    private readonly List<NearExpiryRowModel> _allNearExpiryRows = [];

    // ------------------------------------------------------------------------
    // Hero Band Properties
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _dateRangeLabel = "Nov 1, 2026 — Nov 9, 2026 · 9-day view";

    [ObservableProperty]
    private string _lastUpdatedLabel = "Updated 10:45 AM";

    [ObservableProperty]
    private string _heroNetSales = "Rs 842.5k";

    [ObservableProperty]
    private string _heroDailyAvg = "Rs 93.6k";

    [ObservableProperty]
    private string _heroReturnRate = "2.8%";

    // ------------------------------------------------------------------------
    // Date Range & Quick Presets
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _fromDateText = "2026-11-01";

    [ObservableProperty]
    private string _toDateText = "2026-11-09";

    [ObservableProperty]
    private string _activePreset = "this_month";

    public bool IsPresetToday => ActivePreset == "today";
    public bool IsPresetThisMonth => ActivePreset == "this_month";
    public bool IsPreset7Days => ActivePreset == "7_days";
    public bool IsPreset30Days => ActivePreset == "30_days";
    public bool IsPresetThisYear => ActivePreset == "this_year";

    // ------------------------------------------------------------------------
    // 5 KPI Money Cards
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _salesAmount = "Rs 866,850.00";

    [ObservableProperty]
    private string _salesTrend = "+14.2%";

    [ObservableProperty]
    private string _salesDebit = "Rs 694,200.00";

    [ObservableProperty]
    private string _salesCredit = "Rs 172,650.00";

    [ObservableProperty]
    private string _salesFooter = "Avg Rs 96,316.67/day · vs prev 9d";

    [ObservableProperty]
    private string _salesSparklineLine = string.Empty;

    [ObservableProperty]
    private string _salesSparklineArea = string.Empty;

    [ObservableProperty]
    private string _purchasesAmount = "Rs 542,400.00";

    [ObservableProperty]
    private string _purchasesTrend = "+8.6%";

    [ObservableProperty]
    private string _purchasesDebit = "Rs 380,000.00";

    [ObservableProperty]
    private string _purchasesCredit = "Rs 162,400.00";

    [ObservableProperty]
    private string _purchasesFooter = "Avg Rs 60,266.67/day · vs prev 9d";

    [ObservableProperty]
    private string _purchasesSparklineLine = string.Empty;

    [ObservableProperty]
    private string _purchasesSparklineArea = string.Empty;

    [ObservableProperty]
    private string _expectedOrdersAmount = "Rs 128,500.00";

    [ObservableProperty]
    private string _expectedOrdersFooter = "Expected receiving: 2026-11-01 to 2026-11-09";

    [ObservableProperty]
    private string _netSalesAmount = "Rs 842,530.00";

    [ObservableProperty]
    private string _netSalesTrend = "+15.1%";

    [ObservableProperty]
    private string _netSalesFooter = "Returns Rs 24,320.00 · vs prev 9d";

    [ObservableProperty]
    private string _netSalesSparklineLine = string.Empty;

    [ObservableProperty]
    private string _netSalesSparklineArea = string.Empty;

    [ObservableProperty]
    private string _saleReturnsAmount = "Rs 24,320.00";

    [ObservableProperty]
    private string _saleReturnsTrend = "-3.4%";

    [ObservableProperty]
    private string _saleReturnsFooter = "2.8% of sales · vs prev 9d";

    [ObservableProperty]
    private string _saleReturnsSparklineLine = string.Empty;

    [ObservableProperty]
    private string _saleReturnsSparklineArea = string.Empty;

    // ------------------------------------------------------------------------
    // Revenue Overview & Returns Trend Vector Chart Geometries
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private string _revenueSalesLinePath = string.Empty;

    [ObservableProperty]
    private string _revenueSalesAreaPath = string.Empty;

    [ObservableProperty]
    private string _revenuePurchasesLinePath = string.Empty;

    [ObservableProperty]
    private string _revenuePurchasesAreaPath = string.Empty;

    [ObservableProperty]
    private string _returnsSaleLinePath = string.Empty;

    [ObservableProperty]
    private string _returnsSaleAreaPath = string.Empty;

    [ObservableProperty]
    private string _returnsPurchaseLinePath = string.Empty;

    [ObservableProperty]
    private string _yAxisTopLabel = "140k";

    [ObservableProperty]
    private string _yAxisMidHighLabel = "105k";

    [ObservableProperty]
    private string _yAxisMidLowLabel = "70k";

    [ObservableProperty]
    private string _yAxisBotLabel = "35k";

    // ------------------------------------------------------------------------
    // Near Expiry Filters
    // ------------------------------------------------------------------------
    [ObservableProperty]
    private int _expiryDaysFilter = 90;

    public bool IsExpiry30 => ExpiryDaysFilter == 30;
    public bool IsExpiry90 => ExpiryDaysFilter == 90;
    public bool IsExpiry180 => ExpiryDaysFilter == 180;
    public bool IsExpiry365 => ExpiryDaysFilter == 365;
    public bool IsExpiry540 => ExpiryDaysFilter == 540;

    public ObservableCollection<string> SupplierFilterOptions { get; } =
    [
        "All Suppliers",
        "Premier Distributors",
        "Muller & Phipps (M&P)",
        "UDL Distribution",
        "Al-Noor Pharma",
        "Meditech Traders"
    ];

    public ObservableCollection<string> BrandFilterOptions { get; } =
    [
        "All Brands",
        "GlaxoSmithKline",
        "Getz Pharma",
        "Abbott Laboratories",
        "Searle Company",
        "Hilton Pharma",
        "Sanofi Aventis"
    ];

    [ObservableProperty]
    private string _selectedSupplierFilter = "All Suppliers";

    [ObservableProperty]
    private string _selectedBrandFilter = "All Brands";

    // ------------------------------------------------------------------------
    // Collections for UI Binding
    // ------------------------------------------------------------------------
    public ObservableCollection<ModuleStatTileModel> ModuleStats { get; } = [];
    public ObservableCollection<ChartDayPointModel> ChartPoints { get; } = [];
    public ObservableCollection<RankedItemModel> TopBrands { get; } = [];
    public ObservableCollection<RankedItemModel> TopProducts { get; } = [];
    public ObservableCollection<SnapshotLineModel> PeriodSnapshotLines { get; } = [];
    public ObservableCollection<NearExpiryRowModel> FilteredNearExpiryProducts { get; } = [];

    public DashboardViewModel(
        Action<string>? navigateCallback = null,
        IToastNotificationService? toastService = null)
    {
        _navigateCallback = navigateCallback;
        _toastService = toastService;
        SeedModuleStats();
        SeedNearExpiryData();
        ApplyPresetData("this_month");
        ApplyNearExpiryFilter();
    }

    partial void OnActivePresetChanged(string value)
    {
        OnPropertyChanged(nameof(IsPresetToday));
        OnPropertyChanged(nameof(IsPresetThisMonth));
        OnPropertyChanged(nameof(IsPreset7Days));
        OnPropertyChanged(nameof(IsPreset30Days));
        OnPropertyChanged(nameof(IsPresetThisYear));
    }

    partial void OnExpiryDaysFilterChanged(int value)
    {
        OnPropertyChanged(nameof(IsExpiry30));
        OnPropertyChanged(nameof(IsExpiry90));
        OnPropertyChanged(nameof(IsExpiry180));
        OnPropertyChanged(nameof(IsExpiry365));
        OnPropertyChanged(nameof(IsExpiry540));
        ApplyNearExpiryFilter();
    }

    partial void OnSelectedSupplierFilterChanged(string value) => ApplyNearExpiryFilter();
    partial void OnSelectedBrandFilterChanged(string value) => ApplyNearExpiryFilter();

    [RelayCommand]
    public void SelectPreset(string preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
            return;

        ActivePreset = preset;
        ApplyPresetData(preset);
    }

    [RelayCommand]
    public void RefreshDashboard()
    {
        LastUpdatedLabel = $"Updated {DateTime.Now:hh:mm tt}";
        ApplyPresetData(ActivePreset);
        ApplyNearExpiryFilter();
        _toastService?.ShowSuccess("Dashboard overview refreshed.", "Refreshed");
    }

    [RelayCommand]
    public void NavigateToModule(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            _navigateCallback?.Invoke(path);
        }
    }

    [RelayCommand]
    public void SetExpiryHorizon(string daysStr)
    {
        if (int.TryParse(daysStr, out var days))
        {
            ExpiryDaysFilter = days;
        }
    }

    [RelayCommand]
    public void ClearExpiryFilters()
    {
        SelectedSupplierFilter = "All Suppliers";
        SelectedBrandFilter = "All Brands";
        ExpiryDaysFilter = 90;
    }

    private void SeedModuleStats()
    {
        ModuleStats.Clear();
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Products",
            Value = "1,428",
            Subtext = "Catalog",
            Path = "/products",
            IconGlyph = "📦",
            IconColorHex = "#6366F1",
            IconBgHex = "#EEF2FF",
            BorderAccentHex = "#C7D2FE"
        });
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Customers",
            Value = "312",
            Subtext = "People",
            Path = "/customers",
            IconGlyph = "👥",
            IconColorHex = "#10B981",
            IconBgHex = "#ECFDF5",
            BorderAccentHex = "#A7F3D0"
        });
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Suppliers",
            Value = "61",
            Subtext = "Sources",
            Path = "/suppliers",
            IconGlyph = "🚚",
            IconColorHex = "#F59E0B",
            IconBgHex = "#FFFBEB",
            BorderAccentHex = "#FDE68A"
        });
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Brands",
            Value = "43",
            Subtext = "Labels",
            Path = "/brands",
            IconGlyph = "🏷",
            IconColorHex = "#06B6D4",
            IconBgHex = "#ECFEFF",
            BorderAccentHex = "#A5F3FC"
        });
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Categories",
            Value = "20",
            Subtext = "Groups",
            Path = "/categories",
            IconGlyph = "🗂",
            IconColorHex = "#EC4899",
            IconBgHex = "#FDF2F8",
            BorderAccentHex = "#FBCFE8"
        });
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Invoices",
            Value = "1,845",
            Subtext = "1,490 sales · 355 purch",
            Path = "/sale-invoices",
            IconGlyph = "🧾",
            IconColorHex = "#0EA5E9",
            IconBgHex = "#F0F9FF",
            BorderAccentHex = "#BAE6FD"
        });
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Low Stock",
            Value = "14",
            Subtext = "Needs reorder",
            Path = "/reports/reorder",
            IconGlyph = "⚠",
            IconColorHex = "#EF4444",
            IconBgHex = "#FEF2F2",
            BorderAccentHex = "#FECACA"
        });
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Near Expiry",
            Value = "9",
            Subtext = "Within 3 months",
            Path = "/batch-expiry",
            IconGlyph = "⏳",
            IconColorHex = "#F59E0B",
            IconBgHex = "#FFFBEB",
            BorderAccentHex = "#FDE68A"
        });
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Stock Adjustments",
            Value = "27",
            Subtext = "Documents",
            Path = "/stock-adjustments",
            IconGlyph = "⚖",
            IconColorHex = "#8B5CF6",
            IconBgHex = "#F5F3FF",
            BorderAccentHex = "#DDD6FE"
        });
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Pending Demands",
            Value = "6",
            Subtext = "User requests",
            Path = "/user-demands",
            IconGlyph = "📋",
            IconColorHex = "#F97316",
            IconBgHex = "#FFF7ED",
            BorderAccentHex = "#FED7AA"
        });
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Users",
            Value = "4",
            Subtext = "Accounts",
            Path = "/users",
            IconGlyph = "🛡",
            IconColorHex = "#14B8A6",
            IconBgHex = "#F0FDFA",
            BorderAccentHex = "#99F6E4"
        });
        ModuleStats.Add(new ModuleStatTileModel
        {
            Title = "Stock on Hand",
            Value = "84,620 units",
            Subtext = "Rs 4.24M value",
            Path = "/reports/current-stock",
            IconGlyph = "🏛",
            IconColorHex = "#3B82F6",
            IconBgHex = "#EFF6FF",
            BorderAccentHex = "#BFDBFE"
        });
    }

    private void SeedNearExpiryData()
    {
        _allNearExpiryRows.Clear();
        _allNearExpiryRows.AddRange(
        [
            new NearExpiryRowModel
            {
                ProductName = "Augmentin 625mg Film-Coated Tabs",
                ProductCode = "PRD-0101",
                SupplierName = "Premier Distributors",
                BrandName = "GlaxoSmithKline",
                BatchNumber = "BCH-2409A",
                ExpiryDate = "2026-11-24",
                DaysRemaining = 15,
                Quantity = 42
            },
            new NearExpiryRowModel
            {
                ProductName = "Amoxil 500mg Capsules",
                ProductCode = "PRD-0108",
                SupplierName = "Premier Distributors",
                BrandName = "GlaxoSmithKline",
                BatchNumber = "BCH-2410C",
                ExpiryDate = "2026-12-02",
                DaysRemaining = 23,
                Quantity = 85
            },
            new NearExpiryRowModel
            {
                ProductName = "Risek 40mg Delayed-Release Caps",
                ProductCode = "PRD-0204",
                SupplierName = "Muller & Phipps (M&P)",
                BrandName = "Getz Pharma",
                BatchNumber = "RSK-8821",
                ExpiryDate = "2026-12-19",
                DaysRemaining = 40,
                Quantity = 110
            },
            new NearExpiryRowModel
            {
                ProductName = "Brufen DS 200mg/5ml Suspension",
                ProductCode = "PRD-0312",
                SupplierName = "UDL Distribution",
                BrandName = "Abbott Laboratories",
                BatchNumber = "ABT-9940",
                ExpiryDate = "2027-01-05",
                DaysRemaining = 57,
                Quantity = 28
            },
            new NearExpiryRowModel
            {
                ProductName = "Nuberol Forte 50mg/650mg Tabs",
                ProductCode = "PRD-0419",
                SupplierName = "Al-Noor Pharma",
                BrandName = "Searle Company",
                BatchNumber = "SRL-5512",
                ExpiryDate = "2027-01-22",
                DaysRemaining = 74,
                Quantity = 160
            },
            new NearExpiryRowModel
            {
                ProductName = "Flagyl 400mg Tablets",
                ProductCode = "PRD-0502",
                SupplierName = "Meditech Traders",
                BrandName = "Sanofi Aventis",
                BatchNumber = "SNF-3091",
                ExpiryDate = "2027-02-04",
                DaysRemaining = 87,
                Quantity = 95
            },
            new NearExpiryRowModel
            {
                ProductName = "Klaricid 250mg Tablets",
                ProductCode = "PRD-0615",
                SupplierName = "UDL Distribution",
                BrandName = "Abbott Laboratories",
                BatchNumber = "ABT-4110",
                ExpiryDate = "2027-03-18",
                DaysRemaining = 129,
                Quantity = 64
            },
            new NearExpiryRowModel
            {
                ProductName = "Surbex Z High Potency Film Tabs",
                ProductCode = "PRD-0722",
                SupplierName = "UDL Distribution",
                BrandName = "Abbott Laboratories",
                BatchNumber = "ABT-7731",
                ExpiryDate = "2027-04-28",
                DaysRemaining = 170,
                Quantity = 140
            },
            new NearExpiryRowModel
            {
                ProductName = "Softin 10mg Loratadine Tablets",
                ProductCode = "PRD-0809",
                SupplierName = "Muller & Phipps (M&P)",
                BrandName = "Hilton Pharma",
                BatchNumber = "HLT-1204",
                ExpiryDate = "2027-08-14",
                DaysRemaining = 278,
                Quantity = 210
            }
        ]);
    }

    private void ApplyNearExpiryFilter()
    {
        FilteredNearExpiryProducts.Clear();
        foreach (var row in _allNearExpiryRows)
        {
            if (row.DaysRemaining > ExpiryDaysFilter)
                continue;

            if (SelectedSupplierFilter != "All Suppliers" &&
                !string.Equals(row.SupplierName, SelectedSupplierFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (SelectedBrandFilter != "All Brands" &&
                !string.Equals(row.BrandName, SelectedBrandFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            FilteredNearExpiryProducts.Add(row);
        }
    }

    private void ApplyPresetData(string preset)
    {
        decimal multiplier = preset switch
        {
            "today" => 0.12m,
            "7_days" => 0.78m,
            "this_month" => 1.00m,
            "30_days" => 3.15m,
            "this_year" => 14.80m,
            _ => 1.00m
        };

        switch (preset)
        {
            case "today":
                FromDateText = "2026-11-09";
                ToDateText = "2026-11-09";
                DateRangeLabel = "Nov 9, 2026 — Nov 9, 2026 · 1-day view";
                break;
            case "7_days":
                FromDateText = "2026-11-03";
                ToDateText = "2026-11-09";
                DateRangeLabel = "Nov 3, 2026 — Nov 9, 2026 · 7-day view";
                break;
            case "30_days":
                FromDateText = "2026-10-11";
                ToDateText = "2026-11-09";
                DateRangeLabel = "Oct 11, 2026 — Nov 9, 2026 · 30-day view";
                break;
            case "this_year":
                FromDateText = "2026-01-01";
                ToDateText = "2026-11-09";
                DateRangeLabel = "Jan 1, 2026 — Nov 9, 2026 · YTD view";
                break;
            default:
                FromDateText = "2026-11-01";
                ToDateText = "2026-11-09";
                DateRangeLabel = "Nov 1, 2026 — Nov 9, 2026 · 9-day view";
                break;
        }

        decimal totalSales = Math.Round(866850.00m * multiplier, 2);
        decimal salesDebit = Math.Round(totalSales * 0.801m, 2);
        decimal salesCredit = totalSales - salesDebit;

        decimal totalPurchases = Math.Round(542400.00m * multiplier, 2);
        decimal purchasesDebit = Math.Round(totalPurchases * 0.701m, 2);
        decimal purchasesCredit = totalPurchases - purchasesDebit;

        decimal saleReturns = Math.Round(24320.00m * multiplier, 2);
        decimal purchaseReturns = Math.Round(9850.00m * multiplier, 2);
        decimal netSales = totalSales - saleReturns;
        decimal expectedOrders = Math.Round(128500.00m * multiplier, 2);

        int daysCount = preset switch
        {
            "today" => 1,
            "7_days" => 7,
            "30_days" => 30,
            "this_year" => 313,
            _ => 9
        };

        decimal dailyAvgSales = Math.Round(totalSales / daysCount, 2);
        decimal dailyAvgPurchases = Math.Round(totalPurchases / daysCount, 2);

        HeroNetSales = FormatCompactRs(netSales);
        HeroDailyAvg = FormatCompactRs(Math.Round(netSales / daysCount, 2));
        HeroReturnRate = "2.8%";

        SalesAmount = $"Rs {totalSales:N2}";
        SalesDebit = $"Rs {salesDebit:N2}";
        SalesCredit = $"Rs {salesCredit:N2}";
        SalesFooter = $"Avg Rs {dailyAvgSales:N2}/day · vs prev {daysCount}d";

        PurchasesAmount = $"Rs {totalPurchases:N2}";
        PurchasesDebit = $"Rs {purchasesDebit:N2}";
        PurchasesCredit = $"Rs {purchasesCredit:N2}";
        PurchasesFooter = $"Avg Rs {dailyAvgPurchases:N2}/day · vs prev {daysCount}d";

        ExpectedOrdersAmount = $"Rs {expectedOrders:N2}";
        ExpectedOrdersFooter = $"Expected receiving: {FromDateText} to {ToDateText}";

        NetSalesAmount = $"Rs {netSales:N2}";
        NetSalesFooter = $"Returns Rs {saleReturns:N2} · vs prev {daysCount}d";

        SaleReturnsAmount = $"Rs {saleReturns:N2}";
        SaleReturnsFooter = $"2.8% of sales · vs prev {daysCount}d";

        // Build daily series points
        (string Label, decimal SalesBase, decimal PurchBase, decimal SRetBase, decimal PRetBase)[] rawSeries =
        [
            ("Nov 01", 78400m, 52000m, 2100m, 800m),
            ("Nov 02", 91200m, 44500m, 2850m, 1100m),
            ("Nov 03", 84600m, 68200m, 1950m, 650m),
            ("Nov 04", 104500m, 59000m, 3400m, 1450m),
            ("Nov 05", 96800m, 74300m, 2600m, 950m),
            ("Nov 06", 88900m, 49800m, 2200m, 1200m),
            ("Nov 07", 112400m, 63400m, 3820m, 1600m),
            ("Nov 08", 98250m, 58200m, 2450m, 900m),
            ("Nov 09", 111800m, 73000m, 2950m, 1200m)
        ];

        decimal maxBarValue = rawSeries.Max(r => Math.Max(r.SalesBase, r.PurchBase)) * multiplier;
        if (maxBarValue <= 0) maxBarValue = 1m;

        YAxisTopLabel = FormatAxisShort(maxBarValue);
        YAxisMidHighLabel = FormatAxisShort(maxBarValue * 0.75m);
        YAxisMidLowLabel = FormatAxisShort(maxBarValue * 0.50m);
        YAxisBotLabel = FormatAxisShort(maxBarValue * 0.25m);

        ChartPoints.Clear();
        var salesValues = new List<double>();
        var purchValues = new List<double>();
        var netValues = new List<double>();
        var sRetValues = new List<double>();
        var pRetValues = new List<double>();

        foreach (var pt in rawSeries)
        {
            var s = Math.Round(pt.SalesBase * multiplier, 2);
            var p = Math.Round(pt.PurchBase * multiplier, 2);
            var sr = Math.Round(pt.SRetBase * multiplier, 2);
            var pr = Math.Round(pt.PRetBase * multiplier, 2);

            salesValues.Add((double)s);
            purchValues.Add((double)p);
            netValues.Add((double)(s - sr));
            sRetValues.Add((double)sr);
            pRetValues.Add((double)pr);

            ChartPoints.Add(new ChartDayPointModel
            {
                DateLabel = pt.Label,
                Sales = s,
                Purchases = p,
                SaleReturns = sr,
                PurchaseReturns = pr,
                SalesBarHeight = Math.Max(8, (double)(s / maxBarValue) * 145.0),
                PurchasesBarHeight = Math.Max(8, (double)(p / maxBarValue) * 145.0)
            });
        }

        // Sparklines (110 x 36 canvas)
        (SalesSparklineLine, SalesSparklineArea) = BuildVectorPaths(salesValues, 110, 36, 4);
        (PurchasesSparklineLine, PurchasesSparklineArea) = BuildVectorPaths(purchValues, 110, 36, 4);
        (NetSalesSparklineLine, NetSalesSparklineArea) = BuildVectorPaths(netValues, 110, 36, 4);
        (SaleReturnsSparklineLine, SaleReturnsSparklineArea) = BuildVectorPaths(sRetValues, 110, 36, 4);

        // Revenue Overview Chart (760 x 210 shared scale between Sales & Purchases)
        double sharedMax = Math.Max(salesValues.Max(), purchValues.Max()) * 1.1;
        (RevenueSalesLinePath, RevenueSalesAreaPath) = BuildVectorPathsWithMax(salesValues, 760, 210, 12, sharedMax);
        (RevenuePurchasesLinePath, RevenuePurchasesAreaPath) = BuildVectorPathsWithMax(purchValues, 760, 210, 12, sharedMax);

        // Returns Trend Chart (320 x 150 shared scale between Sale Returns & Purchase Returns)
        double returnsMax = Math.Max(sRetValues.Max(), pRetValues.Max()) * 1.15;
        (ReturnsSaleLinePath, ReturnsSaleAreaPath) = BuildVectorPathsWithMax(sRetValues, 320, 150, 10, returnsMax);
        (ReturnsPurchaseLinePath, _) = BuildVectorPathsWithMax(pRetValues, 320, 150, 10, returnsMax);

        // Top Brands Ranked List
        TopBrands.Clear();
        (string Name, decimal BaseAmount)[] brands =
        [
            ("Getz Pharma", 218400m),
            ("Abbott Laboratories", 176200m),
            ("GlaxoSmithKline", 154000m),
            ("Searle Company", 118500m),
            ("Hilton Pharma", 94200m)
        ];
        decimal topBrandMax = brands[0].BaseAmount * multiplier;
        for (int i = 0; i < brands.Length; i++)
        {
            decimal amt = Math.Round(brands[i].BaseAmount * multiplier, 2);
            TopBrands.Add(new RankedItemModel
            {
                Rank = i + 1,
                Name = brands[i].Name,
                Meta = $"Rank #{i + 1} brand",
                AmountFormatted = FormatCompactRs(amt),
                ProgressRatio = topBrandMax > 0 ? (double)(amt / topBrandMax) : 0,
                RankBgHex = i == 0 ? "#FEF3C7" : i == 1 ? "#F1F5F9" : i == 2 ? "#FFEDD5" : "#EFF6FF",
                RankFgHex = i == 0 ? "#D97706" : i == 1 ? "#475569" : i == 2 ? "#EA580C" : "#2563EB",
                BarColorHex = i % 2 == 0 ? "#3B82F6" : "#8B5CF6"
            });
        }

        // Top Selling Products Ranked List
        TopProducts.Clear();
        (string Name, int BaseUnits, decimal BaseRevenue)[] products =
        [
            ("Augmentin 625mg Film-Coated Tabs", 412, 148320m),
            ("Panadol Advance 500mg Tablets", 1240, 112840m),
            ("Risek 40mg Capsules (14s)", 310, 96100m),
            ("Softin 10mg Tablets", 520, 78000m),
            ("Calamox 1g Tablets", 215, 68800m),
            ("Brufen 100ml Suspension", 390, 54600m)
        ];
        decimal topProdMax = products[0].BaseRevenue * multiplier;
        for (int i = 0; i < products.Length; i++)
        {
            int units = Math.Max(1, (int)Math.Round(products[i].BaseUnits * (double)multiplier));
            decimal rev = Math.Round(products[i].BaseRevenue * multiplier, 2);
            TopProducts.Add(new RankedItemModel
            {
                Rank = i + 1,
                Name = products[i].Name,
                Meta = $"{units:N0} units sold",
                AmountFormatted = $"Rs {rev:N2}",
                ProgressRatio = topProdMax > 0 ? (double)(rev / topProdMax) : 0,
                RankBgHex = i == 0 ? "#FEF3C7" : i == 1 ? "#F1F5F9" : i == 2 ? "#FFEDD5" : "#EFF6FF",
                RankFgHex = i == 0 ? "#D97706" : i == 1 ? "#475569" : i == 2 ? "#EA580C" : "#2563EB",
                BarColorHex = "#3B82F6"
            });
        }

        // Period Snapshot Lines
        PeriodSnapshotLines.Clear();
        PeriodSnapshotLines.Add(new SnapshotLineModel { Label = "Total Sales", Value = $"Rs {totalSales:N2}", DotColorHex = "#3B82F6" });
        PeriodSnapshotLines.Add(new SnapshotLineModel { Label = "Total Purchases", Value = $"Rs {totalPurchases:N2}", DotColorHex = "#8B5CF6" });
        PeriodSnapshotLines.Add(new SnapshotLineModel { Label = "Net Sales", Value = $"Rs {netSales:N2}", DotColorHex = "#10B981" });
        PeriodSnapshotLines.Add(new SnapshotLineModel { Label = "Sale Returns", Value = $"Rs {saleReturns:N2}", DotColorHex = "#06B6D4" });
        PeriodSnapshotLines.Add(new SnapshotLineModel { Label = "Purchase Returns", Value = $"Rs {purchaseReturns:N2}", DotColorHex = "#64748B" });
    }

    private static string FormatCompactRs(decimal value)
    {
        decimal abs = Math.Abs(value);
        if (abs >= 1_000_000m)
            return $"Rs {(value / 1_000_000m):0.00}M";
        if (abs >= 1_000m)
            return $"Rs {(value / 1_000m):0.0}k";
        return $"Rs {value:N0}";
    }

    private static string FormatAxisShort(decimal value)
    {
        if (value >= 1_000_000m)
            return $"{(value / 1_000_000m):0.0}M";
        if (value >= 1_000m)
            return $"{(value / 1_000m):0}k";
        return $"{value:0}";
    }

    private static (string LinePath, string AreaPath) BuildVectorPaths(
        IReadOnlyList<double> values,
        double width,
        double height,
        double verticalPadding)
    {
        if (values.Count == 0)
            return (string.Empty, string.Empty);

        double min = values.Min();
        var normalized = values.Select(v => v - min * 0.85).ToList();
        double normMax = Math.Max(1.0, normalized.Max());
        return BuildVectorPathsWithMax(normalized, width, height, verticalPadding, normMax);
    }

    private static (string LinePath, string AreaPath) BuildVectorPathsWithMax(
        IReadOnlyList<double> values,
        double width,
        double height,
        double verticalPadding,
        double maxValue)
    {
        if (values.Count == 0)
            return (string.Empty, string.Empty);

        var inv = CultureInfo.InvariantCulture;
        double usableHeight = Math.Max(10.0, height - verticalPadding * 2);
        double stepX = values.Count > 1 ? width / (values.Count - 1) : width;
        double safeMax = Math.Max(1.0, maxValue);

        var pts = new (double X, double Y)[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            double x = Math.Round(i * stepX, 1);
            double ratio = Math.Clamp(values[i] / safeMax, 0.0, 1.0);
            double y = Math.Round(height - verticalPadding - ratio * usableHeight, 1);
            pts[i] = (x, y);
        }

        var lineSb = new StringBuilder();
        lineSb.AppendFormat(inv, "M {0},{1}", pts[0].X, pts[0].Y);
        for (int i = 1; i < pts.Length; i++)
        {
            double midX = Math.Round((pts[i - 1].X + pts[i].X) / 2.0, 1);
            lineSb.AppendFormat(
                inv,
                " C {0},{1} {2},{3} {4},{5}",
                midX, pts[i - 1].Y,
                midX, pts[i].Y,
                pts[i].X, pts[i].Y);
        }

        string linePath = lineSb.ToString();
        string areaPath = string.Format(
            inv,
            "{0} L {1},{2} L 0,{2} Z",
            linePath,
            pts[^1].X,
            height);

        return (linePath, areaPath);
    }
}
