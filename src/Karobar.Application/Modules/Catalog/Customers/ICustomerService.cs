namespace Karobar.Application.Interfaces;

public sealed class CustomerCatalogProductDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public int PackSize { get; set; } = 1;
    public int OnHandUnits { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal WholeSalePackPrice { get; set; }
    public decimal WholeSaleUnitPrice { get; set; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(ProductCode)
        ? $"{Name} (Pack: {PackSize} • Avail: {OnHandUnits})"
        : $"{Name} [{ProductCode}] (Pack: {PackSize} • Avail: {OnHandUnits})";
}

public sealed class CustomerRegularItemDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public int PackSize { get; set; } = 1;
    public int OnHandUnits { get; set; }
    public int PackQuantity { get; set; }
    public int UnitQuantity { get; set; } = 1;
    public int RequiredTotalUnits { get; set; } = 1;
    public bool InStock { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public string? DosageNotes { get; set; }

    public bool HasProductCode => !string.IsNullOrWhiteSpace(ProductCode);
    public bool HasDosageNotes => !string.IsNullOrWhiteSpace(DosageNotes);
    public string DosageNotesDisplay => string.IsNullOrWhiteSpace(DosageNotes) ? "—" : DosageNotes!;
    public decimal LineEstimatedValue => Math.Round(RequiredTotalUnits * UnitSalePrice, 2);
    public string LineEstimatedValueDisplay => $"Rs. {LineEstimatedValue:N2}";

    public string StockStatusBadgeText => InStock
        ? $"✓ Ready ({OnHandUnits})"
        : $"⚠ Low ({OnHandUnits}/{RequiredTotalUnits})";
    public string StockStatusBg => InStock ? "#ECFDF5" : "#FFFBEB";
    public string StockStatusFg => InStock ? "#047857" : "#B45309";
    public string StockStatusBorder => InStock ? "#A7F3D0" : "#FCD34D";
}

public sealed class CustomerRegularItemUpsertDto
{
    public long ProductId { get; set; }
    public int PackQuantity { get; set; }
    public int UnitQuantity { get; set; } = 1;
    public string? DosageNotes { get; set; }
}

public sealed class CustomerWholeSalePriceDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public int PackSize { get; set; } = 1;
    public decimal StandardWholeSalePackPrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal PackPrice { get; set; }
    public decimal ImpliedUnitPrice { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public string ProductDisplay => string.IsNullOrWhiteSpace(ProductCode)
        ? ProductName
        : $"{ProductName} ({ProductCode})";
    public string StandardPackDisplay => $"Rs. {StandardWholeSalePackPrice:N2}";
    public string CustomPackDisplay => $"Rs. {PackPrice:N2}";
    public string ImpliedUnitDisplay => $"Rs. {ImpliedUnitPrice:N2} / unit";
}

public sealed class CustomerCreditSummaryDto
{
    public long CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string CreditStatus { get; set; } = "active";
    public decimal? CreditLimit { get; set; }
    public int? CreditDays { get; set; }
    public decimal NetBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal? AvailableCredit { get; set; }
    public bool IsLimitExceeded { get; set; }
    public decimal TotalOverdue { get; set; }
    public int OverdueInvoicesCount { get; set; }
    public string? OldestDueDate { get; set; }

    public string CurrentBalanceDisplay => $"Rs. {CurrentBalance:N2}";
    public string CreditLimitDisplay => CreditLimit.HasValue && CreditLimit.Value > 0m
        ? $"Rs. {CreditLimit.Value:N2}"
        : "Unlimited";
    public string AvailableCreditDisplay => AvailableCredit.HasValue
        ? $"Rs. {AvailableCredit.Value:N2}"
        : "Unlimited";
    public string CreditTermsDisplay => $"{CreditDays ?? 30} Days • {(string.IsNullOrWhiteSpace(CreditStatus) ? "ACTIVE" : CreditStatus.ToUpperInvariant())}";
    public string TotalOverdueDisplay => $"Rs. {TotalOverdue:N2}";
    public string OverdueInvoicesLabel => $"{OverdueInvoicesCount} invoice(s)";
    public bool HasOldestDueDate => !string.IsNullOrWhiteSpace(OldestDueDate);
    public string OldestDueDateDisplay => string.IsNullOrWhiteSpace(OldestDueDate) ? string.Empty : $"Oldest due: {OldestDueDate}";
}

public sealed class CustomerRefillReminderPreviewDto
{
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string NormalizedPhone { get; set; } = string.Empty;
    public string RefillCycle { get; set; } = "monthly";
    public string? NextRefillDate { get; set; }
    public bool StockReady { get; set; }
    public int OutOfStockCount { get; set; }
    public List<CustomerRegularItemDto> RegularItems { get; set; } = [];
    public string EnglishMessage { get; set; } = string.Empty;
    public string UrduMessage { get; set; } = string.Empty;
}

public sealed class CustomerDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? TaxNumber { get; set; }
    public string TaxStatus { get; set; } = "unregistered";
    public string? Address { get; set; }
    public decimal? CreditLimit { get; set; }
    public int? CreditDays { get; set; } = 30;
    public string CreditStatus { get; set; } = "active";

    public bool IsRegular { get; set; }
    public string RefillCycle { get; set; } = "monthly";
    public int? RefillCustomDays { get; set; }
    public int ReminderLeadDays { get; set; } = 3;
    public string? NextRefillDate { get; set; }
    public string? LastRefillRemindedAt { get; set; }
    public string? LastRefillCollectedAt { get; set; }
    public string ReminderLanguage { get; set; } = "english";
    public string? RegularNotes { get; set; }

    public int TransactionsCount { get; set; }
    public int RegularItemsCount { get; set; }
    public int OutOfStockCount { get; set; }
    public bool StockReady { get; set; }
    public int? DaysUntilRefill { get; set; }
    public bool IsDueForReminder { get; set; }
    public bool IsOverdueRefill { get; set; }
    public decimal CurrentReceivable { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<CustomerRegularItemDto> RegularItems { get; set; } = [];

    public bool HasAddress => !string.IsNullOrWhiteSpace(Address);
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);
    public bool HasRegularNotes => !string.IsNullOrWhiteSpace(RegularNotes);
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "—" : Phone!;
    public bool IsStandardCustomer => !IsRegular;
    public bool HasRegularItems => RegularItemsCount > 0;
    public bool HasNoRegularItems => RegularItemsCount == 0;

    public string RegularBadgeText => RefillCycle switch
    {
        "weekly" => "Regular • Weekly",
        "biweekly" => "Regular • Bi-Weekly",
        "custom" => $"Regular • Every {RefillCustomDays ?? 30}d",
        _ => "Regular • Monthly"
    };

    public string RefillCycleDetailedLabel => RefillCycle switch
    {
        "weekly" => "Weekly (Every 7 Days)",
        "biweekly" => "Bi-Weekly (Every 14 Days)",
        "custom" => $"Custom ({RefillCustomDays ?? 30} Days)",
        _ => "Monthly (Every 30 Days)"
    };

    public string NextRefillDateDisplay => string.IsNullOrWhiteSpace(NextRefillDate) ? "Not set" : NextRefillDate!;

    public bool HasDaysUntilRefill => DaysUntilRefill.HasValue;
    public string RefillDueChipText
    {
        get
        {
            if (!DaysUntilRefill.HasValue)
                return string.Empty;
            if (DaysUntilRefill.Value < 0)
                return $"Overdue {Math.Abs(DaysUntilRefill.Value)}d";
            if (DaysUntilRefill.Value == 0)
                return "Due Today";
            return $"In {DaysUntilRefill.Value}d";
        }
    }

    public string RefillDueChipBg => IsOverdueRefill
        ? "#FFF1F2"
        : (IsDueForReminder ? "#FFFBEB" : "#F1F5F9");

    public string RefillDueChipFg => IsOverdueRefill
        ? "#BE123C"
        : (IsDueForReminder ? "#B45309" : "#475569");

    public string RefillDueChipBorder => IsOverdueRefill
        ? "#FECDD3"
        : (IsDueForReminder ? "#FCD34D" : "#CBD5E1");

    public string StockReadinessText
    {
        get
        {
            if (RegularItemsCount <= 0)
                return "No regular medicines added";
            return StockReady
                ? $"✓ Ready ({RegularItemsCount} items in stock)"
                : $"⚠ {OutOfStockCount} of {RegularItemsCount} low stock";
        }
    }

    public string StockReadinessBg => RegularItemsCount <= 0
        ? "#F8FAFC"
        : (StockReady ? "#ECFDF5" : "#FFFBEB");

    public string StockReadinessFg => RegularItemsCount <= 0
        ? "#64748B"
        : (StockReady ? "#047857" : "#B45309");

    public string StockReadinessBorder => RegularItemsCount <= 0
        ? "#E2E8F0"
        : (StockReady ? "#A7F3D0" : "#FCD34D");

    public bool HasLastReminded => !string.IsNullOrWhiteSpace(LastRefillRemindedAt);
    public string LastRemindedBadgeText => HasLastReminded ? $"Reminded {LastRefillRemindedAt}" : "Never";
    public string LastCollectedDisplay => string.IsNullOrWhiteSpace(LastRefillCollectedAt) ? "—" : LastRefillCollectedAt!;

    public string CreditLimitDisplay => CreditLimit.HasValue && CreditLimit.Value > 0m
        ? $"Rs. {CreditLimit.Value:N0}"
        : "Unlimited";

    public string CreditTermsDisplay => $"{CreditDays ?? 30} days";
    public string CreditStatusUpper => (string.IsNullOrWhiteSpace(CreditStatus) ? "active" : CreditStatus).ToUpperInvariant();

    public string CreditStatusBg => CreditStatus?.ToLowerInvariant() switch
    {
        "blocked" => "#FEE2E2",
        "hold" => "#FEF3C7",
        _ => "#DCFCE7"
    };

    public string CreditStatusFg => CreditStatus?.ToLowerInvariant() switch
    {
        "blocked" => "#DC2626",
        "hold" => "#B45309",
        _ => "#15803D"
    };

    public bool IsWalkInCustomer => string.Equals(Name, "WALK-IN-CUSTOMER", StringComparison.OrdinalIgnoreCase);
    public bool HasTransactions => TransactionsCount > 0 || IsWalkInCustomer;
    public bool CanDelete => !HasTransactions;
    public string DeleteTooltip => IsWalkInCustomer
        ? "Cannot delete system default WALK-IN-CUSTOMER."
        : (TransactionsCount > 0
            ? "Cannot delete: customer has related sale invoices/returns."
            : "Delete");

    public decimal TotalEstimatedRegularValue => RegularItems.Sum(r => r.LineEstimatedValue);
    public string TotalEstimatedRegularValueDisplay => $"Rs. {TotalEstimatedRegularValue:N2}";
}

public sealed class CustomerUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? TaxNumber { get; set; }
    public string? TaxStatus { get; set; } = "unregistered";
    public string? Address { get; set; }
    public decimal? CreditLimit { get; set; }
    public int? CreditDays { get; set; } = 30;
    public string? CreditStatus { get; set; } = "active";
    public decimal? OpeningBalance { get; set; }

    public bool IsRegular { get; set; }
    public string? RefillCycle { get; set; } = "monthly";
    public int? RefillCustomDays { get; set; }
    public int? ReminderLeadDays { get; set; } = 3;
    public string? NextRefillDate { get; set; }
    public string? ReminderLanguage { get; set; } = "english";
    public string? RegularNotes { get; set; }
    public List<CustomerRegularItemUpsertDto> RegularItems { get; set; } = [];
}

public sealed class CustomerCsvSampleRow
{
    public int Row { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public string OpeningBalanceDisplay => OpeningBalance > 0m ? $"Rs. {OpeningBalance:N2}" : "—";
    public string ErrorsDisplay => Errors.Count > 0 ? string.Join(" | ", Errors) : string.Empty;
    public string WarningsDisplay => Warnings.Count > 0 ? string.Join(" | ", Warnings) : string.Empty;
}

public sealed class CustomerCsvValidationResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public string UsedDelimiter { get; set; } = ",";
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public List<CustomerCsvSampleRow> InvalidSamples { get; set; } = [];
    public List<CustomerCsvSampleRow> ValidSamples { get; set; } = [];
}

public sealed class CustomerCsvCommitStats
{
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public int Committed { get; set; }
    public int NewCount { get; set; }
    public int ExistingCount { get; set; }
}

public interface ICustomerService
{
    Task<List<CustomerDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<List<CustomerDto>> SearchAsync(
        string? query,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<CustomerDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<CustomerDto> CreateAsync(
        CustomerUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<CustomerDto> UpdateAsync(
        long id,
        CustomerUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<CustomerDto> AdvanceRefillCycleAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<CustomerCreditSummaryDto> GetCreditSummaryAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<CustomerRefillReminderPreviewDto> GetRefillReminderPreviewAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<(string WhatsAppUrl, CustomerDto UpdatedCustomer)> RecordRefillReminderSentAsync(
        long id,
        string phone,
        string language,
        string message,
        bool savePhone,
        bool advanceCycle,
        CancellationToken cancellationToken = default);

    Task<List<CustomerWholeSalePriceDto>> GetWholeSalePricesAsync(
        long customerId,
        CancellationToken cancellationToken = default);

    Task<CustomerWholeSalePriceDto> SaveWholeSalePriceAsync(
        long customerId,
        long productId,
        decimal packPrice,
        CancellationToken cancellationToken = default);

    Task DeleteWholeSalePriceAsync(
        long customerId,
        long priceId,
        CancellationToken cancellationToken = default);

    Task<List<CustomerCatalogProductDto>> SearchProductsForCatalogAsync(
        string? searchQuery = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task ExportCsvAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task SaveTemplateCsvAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<CustomerCsvValidationResult> ValidateCsvAsync(
        string filePath,
        string delimiter = ",",
        CancellationToken cancellationToken = default);

    Task<CustomerCsvCommitStats> CommitCsvAsync(
        string filePath,
        bool insertValidOnly = true,
        string delimiter = ",",
        CancellationToken cancellationToken = default);
}
