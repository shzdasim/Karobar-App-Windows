namespace Karobar.Application.Interfaces;

public sealed class CategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ProductsCount { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public bool IsUsedByProducts => ProductsCount > 0;
    public bool CanDelete => ProductsCount == 0;
    public string ProductsCountBadge => ProductsCount > 0 ? $"{ProductsCount} product{(ProductsCount == 1 ? "" : "s")}" : string.Empty;
    public string DeleteTooltip => IsUsedByProducts
        ? "Cannot delete: category is used by products."
        : "Delete";
}

public sealed class CategoryCsvSampleRow
{
    public int Row { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public string ErrorsDisplay => Errors.Count > 0 ? string.Join(" | ", Errors) : string.Empty;
    public string WarningsDisplay => Warnings.Count > 0 ? string.Join(" | ", Warnings) : string.Empty;
}

public sealed class CategoryCsvValidationResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public string UsedDelimiter { get; set; } = ",";
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public List<CategoryCsvSampleRow> InvalidSamples { get; set; } = [];
    public List<CategoryCsvSampleRow> ValidSamples { get; set; } = [];
}

public sealed class CategoryCsvCommitStats
{
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public int Committed { get; set; }
    public int NewCount { get; set; }
    public int ExistingCount { get; set; }
}

public interface ICategoryService
{
    Task<List<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<CategoryDto>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default);
    Task<CategoryDto> CreateAsync(string name, CancellationToken cancellationToken = default);
    Task<CategoryDto> UpdateAsync(long id, string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
    Task ExportCsvAsync(string filePath, CancellationToken cancellationToken = default);
    Task SaveTemplateCsvAsync(string filePath, CancellationToken cancellationToken = default);
    Task<CategoryCsvValidationResult> ValidateCsvAsync(string filePath, string delimiter = ",", CancellationToken cancellationToken = default);
    Task<CategoryCsvCommitStats> CommitCsvAsync(string filePath, bool insertValidOnly = true, string delimiter = ",", CancellationToken cancellationToken = default);
}
