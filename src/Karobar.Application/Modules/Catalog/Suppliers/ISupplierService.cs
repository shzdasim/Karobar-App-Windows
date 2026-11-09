namespace Karobar.Application.Interfaces;

public sealed class SupplierBatchIntimationDto
{
    public long BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public int PackSize { get; set; }
    public string BrandName { get; set; } = string.Empty;

    public int DaysRemaining { get; set; }
    public string? ReturnDeadlineDate { get; set; }
    public int? DaysUntilReturnDeadline { get; set; }
    public string IntimationStatus { get; set; } = "safe";
    public bool IsIntimationDue { get; set; }

    public string ProductMetaText
    {
        get
        {
            string code = string.IsNullOrWhiteSpace(ProductCode) ? "—" : ProductCode;
            return string.IsNullOrWhiteSpace(BrandName)
                ? code
                : $"{code} • Brand: {BrandName}";
        }
    }

    public string ExpirySubText => DaysRemaining < 0
        ? $"Expired {Math.Abs(DaysRemaining)}d ago"
        : $"{DaysRemaining}d until expiry";

    public string ReturnDeadlineDisplay => string.IsNullOrWhiteSpace(ReturnDeadlineDate) ? "—" : ReturnDeadlineDate;

    public string ReturnDeadlineSubText => string.IsNullOrWhiteSpace(ReturnDeadlineDate) || !DaysUntilReturnDeadline.HasValue
        ? string.Empty
        : DaysUntilReturnDeadline.Value < 0
            ? $"{Math.Abs(DaysUntilReturnDeadline.Value)}d past prior cutoff"
            : $"{DaysUntilReturnDeadline.Value}d left to intimate";

    public string StatusBadgeText => IntimationStatus switch
    {
        "intimate_now" => "⚠ Intimate Now (Cutoff Approaching)",
        "return_window_open" => "⚠ Inside Return Window — Return Immediately",
        "expired_returnable" => "✓ Expired — Returnable Under Policy",
        "expired_window_missed" => "⊘ Expired (Before-Expiry Cutoff Missed)",
        "non_returnable" or "expired_non_returnable" => "Non-Returnable",
        _ => "✓ Safe"
    };

    public string StatusBadgeBg => IntimationStatus switch
    {
        "intimate_now" => "#FFFBEB",
        "return_window_open" => "#FFF1F2",
        "expired_returnable" => "#F5F3FF",
        "expired_window_missed" => "#FEF2F2",
        "non_returnable" or "expired_non_returnable" => "#F1F5F9",
        _ => "#ECFDF5"
    };

    public string StatusBadgeFg => IntimationStatus switch
    {
        "intimate_now" => "#B45309",
        "return_window_open" => "#BE123C",
        "expired_returnable" => "#6D28D9",
        "expired_window_missed" => "#DC2626",
        "non_returnable" or "expired_non_returnable" => "#475569",
        _ => "#047857"
    };

    public string StatusBadgeBorder => IntimationStatus switch
    {
        "intimate_now" => "#FCD34D",
        "return_window_open" => "#FECDD3",
        "expired_returnable" => "#DDD6FE",
        "expired_window_missed" => "#FCA5A5",
        "non_returnable" or "expired_non_returnable" => "#CBD5E1",
        _ => "#A7F3D0"
    };
}

public sealed class SupplierCreditSummaryDto
{
    public long SupplierId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
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
    public string CreditTermsDisplay => CreditDays.HasValue && CreditDays.Value > 0
        ? $"{CreditDays.Value} Days"
        : "Immediate";
    public string TotalOverdueDisplay => $"Rs. {TotalOverdue:N2}";
    public string OverdueInvoicesLabel => $"{OverdueInvoicesCount} invoice(s)";
    public bool HasOldestDueDate => !string.IsNullOrWhiteSpace(OldestDueDate);
    public string OldestDueDateDisplay => string.IsNullOrWhiteSpace(OldestDueDate) ? string.Empty : $"Oldest due: {OldestDueDate}";
}

public sealed class SupplierDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? TaxNumber { get; set; }
    public string TaxStatus { get; set; } = "unregistered";
    public decimal? CreditLimit { get; set; }
    public int? CreditDays { get; set; }

    public string ExpiryReturnPolicy { get; set; } = "before_expiry";
    public int ExpiryReturnMonths { get; set; } = 3;
    public int ExpiryReturnDays { get; set; } = 90;
    public string? ExpiryReturnNotes { get; set; }
    public string ExpiryPolicyLabel { get; set; } = "3 Months Before Expiry";

    public int ProductsCount { get; set; }
    public int IntimationDueCount { get; set; }
    public int IntimationDueUnits { get; set; }
    public decimal CurrentPayable { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<SupplierBatchIntimationDto> IntimationBatches { get; set; } = [];

    public bool HasAddress => !string.IsNullOrWhiteSpace(Address);
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);
    public bool HasNotes => !string.IsNullOrWhiteSpace(ExpiryReturnNotes);
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "—" : Phone;

    public string PayableDisplay => $"Payable: Rs. {CurrentPayable:N2}";
    public string PayableFg => CurrentPayable > 0m ? "#D97706" : "#0F172A";
    public string CreditLimitDisplay => CreditLimit.HasValue && CreditLimit.Value > 0m
        ? $"Limit: Rs. {CreditLimit.Value:N2}"
        : "Limit: Unlimited";
    public string CreditDaysDisplay => CreditDays.HasValue && CreditDays.Value > 0
        ? $"{CreditDays.Value} days"
        : "Immediate";

    public bool HasIntimationsDue => IntimationDueCount > 0;
    public bool NoIntimationsDue => IntimationDueCount == 0;
    public string IntimationDueText =>
        $"⚠ {IntimationDueCount} {(IntimationDueCount == 1 ? "Batch" : "Batches")} Due ({IntimationDueUnits} units)";

    public bool IsUsedByProducts => ProductsCount > 0;
    public bool CanDelete => ProductsCount == 0;
    public string DeleteTooltip => IsUsedByProducts
        ? "Cannot delete: supplier is used by products."
        : "Delete";

    public string PolicyBadgeDisplay => ExpiryReturnPolicy switch
    {
        "accepts_expired" => $"✓ {ExpiryPolicyLabel}",
        "non_returnable" => $"⊘ {ExpiryPolicyLabel}",
        _ => $"🕒 {ExpiryPolicyLabel}"
    };

    public string PolicyBadgeBg => ExpiryReturnPolicy switch
    {
        "accepts_expired" => "#ECFDF5",
        "non_returnable" => "#FFF1F2",
        _ => "#EFF6FF"
    };

    public string PolicyBadgeFg => ExpiryReturnPolicy switch
    {
        "accepts_expired" => "#047857",
        "non_returnable" => "#BE123C",
        _ => "#1D4ED8"
    };

    public string PolicyBadgeBorder => ExpiryReturnPolicy switch
    {
        "accepts_expired" => "#A7F3D0",
        "non_returnable" => "#FECDD3",
        _ => "#BFDBFE"
    };
}

public sealed class SupplierUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? TaxNumber { get; set; }
    public string? TaxStatus { get; set; } = "unregistered";
    public decimal? CreditLimit { get; set; }
    public int? CreditDays { get; set; } = 30;
    public decimal? OpeningBalance { get; set; }

    public string ExpiryReturnPolicy { get; set; } = "before_expiry";
    public int? ExpiryReturnMonths { get; set; } = 3;
    public int? ExpiryReturnDays { get; set; } = 90;
    public string? ExpiryReturnNotes { get; set; }
}

public sealed class SupplierCsvSampleRow
{
    public int Row { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public string OpeningBalanceDisplay => OpeningBalance > 0m ? $"Rs. {OpeningBalance:N2}" : "—";
    public string ErrorsDisplay => Errors.Count > 0 ? string.Join(" | ", Errors) : string.Empty;
    public string WarningsDisplay => Warnings.Count > 0 ? string.Join(" | ", Warnings) : string.Empty;
}

public sealed class SupplierCsvValidationResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public string UsedDelimiter { get; set; } = ",";
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public List<SupplierCsvSampleRow> InvalidSamples { get; set; } = [];
    public List<SupplierCsvSampleRow> ValidSamples { get; set; } = [];
}

public sealed class SupplierCsvCommitStats
{
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public int Committed { get; set; }
    public int NewCount { get; set; }
    public int ExistingCount { get; set; }
}

public interface ISupplierService
{
    Task<List<SupplierDto>> GetAllAsync(
        string? searchQuery = null,
        string? policyFilter = "all",
        CancellationToken cancellationToken = default);

    Task<List<SupplierDto>> SearchAsync(
        string? query,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<SupplierDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<SupplierCreditSummaryDto> GetCreditSummaryAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<SupplierDto> CreateAsync(
        SupplierUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<SupplierDto> UpdateAsync(
        long id,
        SupplierUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task ExportCsvAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task SaveTemplateCsvAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<SupplierCsvValidationResult> ValidateCsvAsync(
        string filePath,
        string delimiter = ",",
        CancellationToken cancellationToken = default);

    Task<SupplierCsvCommitStats> CommitCsvAsync(
        string filePath,
        bool insertValidOnly = true,
        string delimiter = ",",
        CancellationToken cancellationToken = default);
}
