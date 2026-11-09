using System.Text;
using System.Text.RegularExpressions;
using Karobar.Application.Interfaces;
using Karobar.Domain.Entities;
using Karobar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Services;

public sealed class CategoryService : ICategoryService
{
    public const int PreviewInvalidLimit = 20;
    public const int PreviewValidLimit = 10;

    private static readonly string[] NameHeaderAliases =
    [
        "name", "category", "category_name", "cat", "title", "label"
    ];

    private static readonly Regex NumericRegex = new(@"^[+-]?\d+(\.\d+)?$", RegexOptions.Compiled);

    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;

    public CategoryService(IDbContextFactory<KarobarDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var productCounts = await db.Products.AsNoTracking()
            .Where(p => p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, cancellationToken);

        var categories = await db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return categories.Select(c => new CategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            ProductsCount = productCounts.GetValueOrDefault(c.Id, 0),
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        }).ToList();
    }

    public async Task<List<CategoryDto>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        int clampedLimit = Math.Clamp(limit, 1, 2000);
        string q = query?.Trim() ?? string.Empty;

        var dbQuery = db.Categories.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(q))
        {
            dbQuery = dbQuery.Where(c => EF.Functions.Like(c.Name, $"%{q}%"));
        }

        return await dbQuery
            .OrderBy(c => c.Name)
            .Take(clampedLimit)
            .Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<CategoryDto> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidOperationException("Name is required");

        if (trimmed.Length > 255)
            throw new InvalidOperationException("Name too long (max 255).");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        string lower = trimmed.ToLowerInvariant();

        bool exists = await db.Categories.AsNoTracking()
            .AnyAsync(c => c.Name.ToLower() == lower, cancellationToken);

        if (exists)
            throw new InvalidOperationException("The name has already been taken.");

        var now = DateTime.UtcNow;
        var entity = new Category
        {
            Name = trimmed,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Categories.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new CategoryDto
        {
            Id = entity.Id,
            Name = entity.Name,
            ProductsCount = 0,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    public async Task<CategoryDto> UpdateAsync(long id, string name, CancellationToken cancellationToken = default)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidOperationException("Name is required");

        if (trimmed.Length > 255)
            throw new InvalidOperationException("Name too long (max 255).");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Category not found.");

        string lower = trimmed.ToLowerInvariant();
        bool duplicate = await db.Categories.AsNoTracking()
            .AnyAsync(c => c.Id != id && c.Name.ToLower() == lower, cancellationToken);

        if (duplicate)
            throw new InvalidOperationException("The name has already been taken.");

        entity.Name = trimmed;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        int productsCount = await db.Products.AsNoTracking()
            .CountAsync(p => p.CategoryId == id, cancellationToken);

        return new CategoryDto
        {
            Id = entity.Id,
            Name = entity.Name,
            ProductsCount = productsCount,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity == null)
            return;

        bool usedByProducts = await db.Products.AsNoTracking()
            .AnyAsync(p => p.CategoryId == id, cancellationToken);

        if (usedByProducts)
            throw new InvalidOperationException("Cannot delete: category is used by one or more products.");

        db.Categories.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ExportCsvAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var names = await db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);

        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await using var writer = new StreamWriter(filePath, append: false, utf8WithBom);

        await writer.WriteLineAsync("name");
        foreach (var rawName in names)
        {
            string safe = SanitizeCsvValue(rawName);
            await writer.WriteLineAsync(EscapeCsvField(safe));
        }
    }

    public async Task SaveTemplateCsvAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var content = "name\nAnalgesics\nVitamins\n";
        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(false), cancellationToken);
    }

    public async Task<CategoryCsvValidationResult> ValidateCsvAsync(
        string filePath,
        string delimiter = ",",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new CategoryCsvValidationResult
            {
                Ok = false,
                Message = "Selected CSV file does not exist."
            };
        }

        char delimChar = NormalizeDelimiter(delimiter);
        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8, cancellationToken);
        var nonEmptyLines = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (nonEmptyLines.Count == 0)
        {
            return new CategoryCsvValidationResult
            {
                Ok = false,
                Message = "CSV must contain a header with column: name (any order/alias okay)."
            };
        }

        int? nameColIdx = TryFindNameColumnIndex(ParseCsvLine(nonEmptyLines[0], delimChar));
        if (nameColIdx == null)
        {
            // Auto-detect delimiter from sample if user delimiter didn't match
            char guessed = GuessDelimiterFromSample(nonEmptyLines[0]);
            if (guessed != delimChar)
            {
                int? guessedIdx = TryFindNameColumnIndex(ParseCsvLine(nonEmptyLines[0], guessed));
                if (guessedIdx != null)
                {
                    delimChar = guessed;
                    nameColIdx = guessedIdx;
                }
            }
        }

        if (nameColIdx == null)
        {
            return new CategoryCsvValidationResult
            {
                Ok = false,
                Message = "CSV must contain a header with column: name (accepted: name, category, category_name, title, label)."
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existingLower = (await db.Categories.AsNoTracking()
                .Select(c => c.Name)
                .ToListAsync(cancellationToken))
            .Select(n => n.Trim().ToLowerInvariant())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new CategoryCsvValidationResult
        {
            Ok = true,
            UsedDelimiter = delimChar == '\t' ? "\\t" : delimChar.ToString()
        };

        for (int i = 1; i < nonEmptyLines.Count; i++)
        {
            var cols = ParseCsvLine(nonEmptyLines[i], delimChar);
            if (cols.All(string.IsNullOrWhiteSpace))
                continue;

            int rowNo = i + 1;
            string rawName = nameColIdx.Value < cols.Count ? cols[nameColIdx.Value] : string.Empty;
            string cleanName = NormalizeIncomingCell(rawName, isFirstColumn: nameColIdx.Value == 0);

            var (errors, warnings) = EvaluateRowIssues(cleanName, existingLower, seenInFile, strictDuplicateInFile: true);
            result.Total++;

            if (errors.Count > 0)
            {
                result.Invalid++;
                if (result.InvalidSamples.Count < PreviewInvalidLimit)
                {
                    result.InvalidSamples.Add(new CategoryCsvSampleRow
                    {
                        Row = rowNo,
                        Name = cleanName,
                        Errors = errors,
                        Warnings = warnings
                    });
                }
            }
            else
            {
                result.Valid++;
                seenInFile.Add(cleanName.ToLowerInvariant());
                if (result.ValidSamples.Count < PreviewValidLimit)
                {
                    result.ValidSamples.Add(new CategoryCsvSampleRow
                    {
                        Row = rowNo,
                        Name = cleanName,
                        Warnings = warnings
                    });
                }
            }
        }

        result.Message = result.Invalid > 0
            ? $"Found {result.Invalid} invalid row(s)."
            : $"All {result.Valid} row(s) are valid.";

        return result;
    }

    public async Task<CategoryCsvCommitStats> CommitCsvAsync(
        string filePath,
        bool insertValidOnly = true,
        string delimiter = ",",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new InvalidOperationException("CSV file not found.");

        char delimChar = NormalizeDelimiter(delimiter);
        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8, cancellationToken);
        var nonEmptyLines = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (nonEmptyLines.Count == 0)
            throw new InvalidOperationException("CSV header not recognized (need name).");

        int? nameColIdx = TryFindNameColumnIndex(ParseCsvLine(nonEmptyLines[0], delimChar));
        if (nameColIdx == null)
        {
            char guessed = GuessDelimiterFromSample(nonEmptyLines[0]);
            int? guessedIdx = TryFindNameColumnIndex(ParseCsvLine(nonEmptyLines[0], guessed));
            if (guessedIdx != null)
            {
                delimChar = guessed;
                nameColIdx = guessedIdx;
            }
        }

        if (nameColIdx == null)
            throw new InvalidOperationException("CSV header not recognized (need name).");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existingEntities = await db.Categories.ToListAsync(cancellationToken);
        var existingByLower = existingEntities
            .GroupBy(c => c.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var existingLowerSet = new HashSet<string>(existingByLower.Keys, StringComparer.OrdinalIgnoreCase);
        var stats = new CategoryCsvCommitStats();
        var now = DateTime.UtcNow;
        var namesToInsert = new List<string>();

        for (int i = 1; i < nonEmptyLines.Count; i++)
        {
            var cols = ParseCsvLine(nonEmptyLines[i], delimChar);
            if (cols.All(string.IsNullOrWhiteSpace))
                continue;

            string rawName = nameColIdx.Value < cols.Count ? cols[nameColIdx.Value] : string.Empty;
            string cleanName = NormalizeIncomingCell(rawName, isFirstColumn: nameColIdx.Value == 0);
            stats.Total++;

            var (errors, _) = EvaluateRowIssues(cleanName, existingLowerSet, seenInFile: null, strictDuplicateInFile: false);
            if (errors.Count > 0)
            {
                stats.Invalid++;
                if (insertValidOnly)
                    continue;

                throw new InvalidOperationException("Import aborted due to validation error.");
            }

            stats.Valid++;
            string lower = cleanName.ToLowerInvariant();
            if (existingLowerSet.Contains(lower))
            {
                stats.ExistingCount++;
            }
            else
            {
                stats.NewCount++;
                existingLowerSet.Add(lower);
                namesToInsert.Add(cleanName);
            }

            stats.Committed++;
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var newName in namesToInsert)
        {
            db.Categories.Add(new Category
            {
                Name = newName,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return stats;
    }

    private static (List<string> Errors, List<string> Warnings) EvaluateRowIssues(
        string name,
        HashSet<string> existingLower,
        HashSet<string>? seenInFile,
        bool strictDuplicateInFile)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Name is required.");
            return (errors, warnings);
        }

        if (name.Length > 255)
        {
            errors.Add("Name too long (max 255).");
        }

        string lower = name.ToLowerInvariant();
        if (strictDuplicateInFile && seenInFile != null && seenInFile.Contains(lower))
        {
            errors.Add("Duplicate name within file.");
        }

        if (existingLower.Contains(lower))
        {
            warnings.Add("Name exists (will upsert).");
        }

        return (errors, warnings);
    }

    private static char NormalizeDelimiter(string delimiter)
    {
        if (string.IsNullOrEmpty(delimiter))
            return ',';
        if (delimiter == "\\t" || delimiter == "\t")
            return '\t';
        return delimiter[0];
    }

    private static char GuessDelimiterFromSample(string sampleLine)
    {
        char[] candidates = [',', ';', '\t', '|'];
        char best = ',';
        int bestCount = -1;
        foreach (char c in candidates)
        {
            int count = sampleLine.Count(ch => ch == c);
            if (count > bestCount)
            {
                best = c;
                bestCount = count;
            }
        }
        return best;
    }

    private static int? TryFindNameColumnIndex(List<string> headerRow)
    {
        for (int i = 0; i < headerRow.Count; i++)
        {
            string h = headerRow[i].Trim().TrimStart('\uFEFF').ToLowerInvariant();
            if (NameHeaderAliases.Contains(h, StringComparer.OrdinalIgnoreCase))
                return i;
        }
        return null;
    }

    private static string NormalizeIncomingCell(string? raw, bool isFirstColumn)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        string s = raw.Trim();
        if (isFirstColumn)
        {
            s = s.TrimStart('\uFEFF');
        }

        if (s.Length > 0 && (s[0] == '=' || s[0] == '+' || s[0] == '-' || s[0] == '@') && !NumericRegex.IsMatch(s))
        {
            s = "'" + s;
        }

        return s;
    }

    private static string SanitizeCsvValue(string? val)
    {
        string str = val ?? string.Empty;
        if (str.Length > 0 &&
            (str[0] == '=' || str[0] == '+' || str[0] == '-' || str[0] == '@' || str[0] == '\t' || str[0] == '\r') &&
            !NumericRegex.IsMatch(str))
        {
            return "'" + str;
        }
        return str;
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains('"') || field.Contains(',') || field.Contains('\n') || field.Contains('\r'))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        return field;
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        result.Add(current.ToString());
        return result;
    }
}
