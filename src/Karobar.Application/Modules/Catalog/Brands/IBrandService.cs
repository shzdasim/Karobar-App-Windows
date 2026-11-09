using System.IO;

namespace Karobar.Application.Interfaces;

public sealed class BrandBatchIntimationDto
{
    public long BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public int PackSize { get; set; }
    public string SupplierName { get; set; } = string.Empty;

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
            return string.IsNullOrWhiteSpace(SupplierName)
                ? code
                : $"{code} • Supplier: {SupplierName}";
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

public sealed class BrandDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string? ImageAbsolutePath { get; set; }
    public string ExpiryReturnPolicy { get; set; } = "before_expiry";
    public int ExpiryReturnMonths { get; set; } = 3;
    public int ExpiryReturnDays { get; set; } = 90;
    public string? ExpiryReturnNotes { get; set; }
    public string ExpiryPolicyLabel { get; set; } = "3 Months Before Expiry";

    public int ProductsCount { get; set; }
    public int IntimationDueCount { get; set; }
    public int IntimationDueUnits { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<BrandBatchIntimationDto> IntimationBatches { get; set; } = [];

    public bool HasImage => !string.IsNullOrWhiteSpace(ImageAbsolutePath) && File.Exists(ImageAbsolutePath);
    public bool HasNoImage => !HasImage;
    public bool HasNotes => !string.IsNullOrWhiteSpace(ExpiryReturnNotes);
    public bool IsUsedByProducts => ProductsCount > 0;
    public bool CanDelete => ProductsCount == 0;
    public string ProductsCountLabel => $"{ProductsCount} Products";
    public string DeleteTooltip => IsUsedByProducts
        ? "Cannot delete: brand is used by products."
        : "Delete";

    public bool HasIntimationsDue => IntimationDueCount > 0;
    public bool NoIntimationsDue => IntimationDueCount == 0;
    public string IntimationDueText =>
        $"⚠ {IntimationDueCount} {(IntimationDueCount == 1 ? "Batch" : "Batches")} Due ({IntimationDueUnits} units)";

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

public sealed class BrandUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string? SourceImageFilePath { get; set; }
    public string ExpiryReturnPolicy { get; set; } = "before_expiry";
    public int? ExpiryReturnMonths { get; set; } = 3;
    public int? ExpiryReturnDays { get; set; } = 90;
    public string? ExpiryReturnNotes { get; set; }
}

public sealed class BrandCsvSampleRow
{
    public int Row { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public string ErrorsDisplay => Errors.Count > 0 ? string.Join(" | ", Errors) : string.Empty;
    public string WarningsDisplay => Warnings.Count > 0 ? string.Join(" | ", Warnings) : string.Empty;
}

public sealed class BrandCsvValidationResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public string UsedDelimiter { get; set; } = ",";
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public List<BrandCsvSampleRow> InvalidSamples { get; set; } = [];
    public List<BrandCsvSampleRow> ValidSamples { get; set; } = [];
}

public sealed class BrandCsvCommitStats
{
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public int Committed { get; set; }
    public int NewCount { get; set; }
    public int ExistingCount { get; set; }
}

public interface IBrandService
{
    Task<List<BrandDto>> GetAllAsync(
        string? searchQuery = null,
        string? policyFilter = "all",
        CancellationToken cancellationToken = default);

    Task<List<BrandDto>> SearchAsync(
        string? query,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<BrandDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<BrandDto> CreateAsync(
        BrandUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<BrandDto> UpdateAsync(
        long id,
        BrandUpsertRequest request,
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

    Task<BrandCsvValidationResult> ValidateCsvAsync(
        string filePath,
        string delimiter = ",",
        CancellationToken cancellationToken = default);

    Task<BrandCsvCommitStats> CommitCsvAsync(
        string filePath,
        bool insertValidOnly = true,
        string delimiter = ",",
        CancellationToken cancellationToken = default);
}
