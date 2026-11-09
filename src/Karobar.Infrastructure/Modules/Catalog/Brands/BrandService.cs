using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Karobar.Application.Interfaces;
using Karobar.Domain.Entities;
using Karobar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Services;

public sealed class BrandService : IBrandService
{
    public const int PreviewInvalidLimit = 20;
    public const int PreviewValidLimit = 10;

    private static readonly string[] NameHeaderAliases =
    [
        "name", "brand", "brand_name", "label", "company"
    ];

    private static readonly string[] ImageHeaderAliases =
    [
        "image", "image_path", "image_url", "logo", "logo_url", "picture", "photo"
    ];

    private static readonly Regex NumericRegex = new(@"^[+-]?\d+(\.\d+)?$", RegexOptions.Compiled);
    private static readonly Regex BeforeMonthsFilterRegex = new(@"^before_(\d+)$", RegexOptions.Compiled);

    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;
    private readonly IDatabaseInitializerService _dbInitializer;

    public BrandService(
        IDbContextFactory<KarobarDbContext> dbFactory,
        IDatabaseInitializerService dbInitializer)
    {
        _dbFactory = dbFactory;
        _dbInitializer = dbInitializer;
    }

    public static string FormatExpiryPolicyLabel(string? policy, int? months, int? days)
    {
        string normalizedPolicy = string.IsNullOrWhiteSpace(policy) ? "before_expiry" : policy.Trim();

        if (normalizedPolicy == "accepts_expired")
            return "Accepts Expired Products";

        if (normalizedPolicy == "non_returnable")
            return "Non-Returnable";

        int m = months ?? 0;
        int d = days ?? 0;

        if (m > 0)
        {
            if (m % 12 == 0)
            {
                int years = m / 12;
                return years == 1 ? "1 Year Before Expiry" : $"{years} Years Before Expiry";
            }

            return m == 1 ? "1 Month Before Expiry" : $"{m} Months Before Expiry";
        }

        if (d > 0)
        {
            if (d == 365)
                return "1 Year Before Expiry";
            if (d == 730)
                return "2 Years Before Expiry";
            if (d % 30 == 0)
            {
                int calcMonths = d / 30;
                return calcMonths == 1 ? "1 Month Before Expiry" : $"{calcMonths} Months Before Expiry";
            }

            return $"{d} Days Before Expiry";
        }

        return "3 Months Before Expiry";
    }

    public static (string Policy, int Months, int Days, string? Notes) NormalizeExpiryPolicy(
        string? rawPolicy,
        int? rawMonths,
        int? rawDays,
        string? rawNotes)
    {
        string policy = string.IsNullOrWhiteSpace(rawPolicy) ? "before_expiry" : rawPolicy.Trim();
        if (policy is not ("before_expiry" or "accepts_expired" or "non_returnable"))
        {
            policy = "before_expiry";
        }

        int months;
        int days;

        if (policy is "accepts_expired" or "non_returnable")
        {
            months = 0;
            days = 0;
        }
        else
        {
            int? m = rawMonths.HasValue ? Math.Max(0, rawMonths.Value) : null;
            int? d = rawDays.HasValue ? Math.Max(0, rawDays.Value) : null;

            if (m.HasValue && m.Value > 0 && (!d.HasValue || d.Value == 0))
            {
                months = m.Value;
                days = months == 12 ? 365 : (months == 24 ? 730 : months * 30);
            }
            else if (d.HasValue && d.Value > 0 && (!m.HasValue || m.Value == 0))
            {
                days = d.Value;
                months = days == 365 ? 12 : (days == 730 ? 24 : (int)Math.Round(days / 30.0));
            }
            else if ((!m.HasValue || m.Value == 0) && (!d.HasValue || d.Value == 0))
            {
                months = 3;
                days = 90;
            }
            else
            {
                months = m ?? 3;
                days = d ?? 90;
            }
        }

        string? notes = string.IsNullOrWhiteSpace(rawNotes) ? null : rawNotes.Trim();
        return (policy, months, days, notes);
    }

    public static (int DaysRemaining, string? ReturnDeadlineDate, int? DaysUntilReturnDeadline, string IntimationStatus, bool IsIntimationDue)
        EvaluateBatchIntimation(
            DateTime? expiryDate,
            string? rawPolicy,
            int policyMonths,
            int policyDays,
            DateTime? referenceToday = null)
    {
        if (!expiryDate.HasValue)
            return (0, null, null, "safe", false);

        return EvaluateBatchIntimation(
            expiryDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            rawPolicy,
            policyMonths,
            policyDays,
            referenceToday);
    }

    public static (int DaysRemaining, string? ReturnDeadlineDate, int? DaysUntilReturnDeadline, string IntimationStatus, bool IsIntimationDue)
        EvaluateBatchIntimation(
            string? expiryDateStr,
            string? rawPolicy,
            int policyMonths,
            int policyDays,
            DateTime? referenceToday = null)
    {
        DateTime today = (referenceToday ?? DateTime.Today).Date;
        if (string.IsNullOrWhiteSpace(expiryDateStr) ||
            !DateTime.TryParse(expiryDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiryParsed))
        {
            return (0, null, null, "safe", false);
        }

        DateTime expiry = expiryParsed.Date;
        int daysRemaining = (expiry - today).Days;
        string policy = string.IsNullOrWhiteSpace(rawPolicy) ? "before_expiry" : rawPolicy.Trim();

        if (policy == "non_returnable")
        {
            string status = daysRemaining < 0 ? "expired_non_returnable" : "non_returnable";
            return (daysRemaining, null, null, status, false);
        }

        if (policy == "accepts_expired")
        {
            bool isDue = daysRemaining <= 30;
            string status = daysRemaining < 0
                ? "expired_returnable"
                : (daysRemaining <= 30 ? "intimate_now" : "safe");

            return (daysRemaining, expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), daysRemaining, status, isDue);
        }

        int effectiveDays = policyDays > 0
            ? policyDays
            : (policyMonths > 0 ? (policyMonths == 12 ? 365 : (policyMonths == 24 ? 730 : policyMonths * 30)) : 90);

        DateTime deadline = policyMonths > 0
            ? SubtractMonthsNoOverflow(expiry, policyMonths)
            : expiry.AddDays(-effectiveDays);

        int daysUntilDeadline = (deadline - today).Days;
        string beforeStatus;
        bool beforeDue;

        if (daysRemaining < 0)
        {
            beforeStatus = "expired_window_missed";
            beforeDue = true;
        }
        else if (daysUntilDeadline < 0)
        {
            beforeStatus = "return_window_open";
            beforeDue = true;
        }
        else if (daysUntilDeadline <= 30)
        {
            beforeStatus = "intimate_now";
            beforeDue = true;
        }
        else
        {
            beforeStatus = "safe";
            beforeDue = false;
        }

        return (
            daysRemaining,
            deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            daysUntilDeadline,
            beforeStatus,
            beforeDue);
    }

    public async Task<List<BrandDto>> GetAllAsync(
        string? searchQuery = null,
        string? policyFilter = "all",
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        string qName = searchQuery?.Trim() ?? string.Empty;
        string filter = string.IsNullOrWhiteSpace(policyFilter) ? "all" : policyFilter.Trim();

        var query = db.Brands.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(qName))
        {
            query = query.Where(b => EF.Functions.Like(b.Name, $"%{qName}%"));
        }

        if (!string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (filter is "before_expiry" or "accepts_expired" or "non_returnable")
            {
                query = query.Where(b => b.ExpiryReturnPolicy == filter);
            }
            else
            {
                var mMatch = BeforeMonthsFilterRegex.Match(filter);
                if (mMatch.Success && int.TryParse(mMatch.Groups[1].Value, out int filterMonths))
                {
                    query = query.Where(b =>
                        (b.ExpiryReturnPolicy == null || b.ExpiryReturnPolicy == "before_expiry") &&
                        b.ExpiryReturnMonths == filterMonths);
                }
            }
        }

        var brands = await query
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        if (brands.Count == 0)
            return [];

        var brandIds = brands.Select(b => b.Id).ToList();

        var productCounts = await db.Products.AsNoTracking()
            .Where(p => p.BrandId != null && brandIds.Contains(p.BrandId.Value))
            .GroupBy(p => p.BrandId!.Value)
            .Select(g => new { BrandId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BrandId, x => x.Count, cancellationToken);

        var activeBatches = await (
                from b in db.Batches.AsNoTracking()
                join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
                where p.BrandId != null
                      && brandIds.Contains(p.BrandId.Value)
                      && b.ExpiryDate != null
                      && b.Quantity > 0
                select new
                {
                    BrandId = p.BrandId!.Value,
                    b.ExpiryDate,
                    b.Quantity
                })
            .ToListAsync(cancellationToken);

        var batchesByBrand = activeBatches
            .GroupBy(x => x.BrandId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<BrandDto>(brands.Count);
        foreach (var b in brands)
        {
            string policy = string.IsNullOrWhiteSpace(b.ExpiryReturnPolicy) ? "before_expiry" : b.ExpiryReturnPolicy!;
            int months = b.ExpiryReturnMonths ?? (policy == "before_expiry" ? 3 : 0);
            int days = b.ExpiryReturnDays ?? (policy == "before_expiry" ? 90 : 0);

            int dueCount = 0;
            int dueUnits = 0;

            if (batchesByBrand.TryGetValue(b.Id, out var brandBatches))
            {
                foreach (var batchRow in brandBatches)
                {
                    var eval = EvaluateBatchIntimation(batchRow.ExpiryDate, policy, months, days);
                    if (eval.IsIntimationDue)
                    {
                        dueCount++;
                        dueUnits += batchRow.Quantity;
                    }
                }
            }

            result.Add(MapToDto(
                b,
                productCounts.GetValueOrDefault(b.Id, 0),
                dueCount,
                dueUnits));
        }

        return result;
    }

    public async Task<List<BrandDto>> SearchAsync(
        string? query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        int clampedLimit = Math.Clamp(limit, 1, 2000);
        string q = query?.Trim() ?? string.Empty;

        var dbQuery = db.Brands.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(q))
        {
            dbQuery = dbQuery.Where(b => EF.Functions.Like(b.Name, $"%{q}%"));
        }

        var list = await dbQuery
            .OrderBy(b => b.Name)
            .Take(clampedLimit)
            .ToListAsync(cancellationToken);

        return list.Select(b => MapToDto(b, 0, 0, 0)).ToList();
    }

    public async Task<BrandDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var brand = await db.Brands.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (brand == null)
            return null;

        int productsCount = await db.Products.AsNoTracking()
            .CountAsync(p => p.BrandId == id, cancellationToken);

        string policy = string.IsNullOrWhiteSpace(brand.ExpiryReturnPolicy) ? "before_expiry" : brand.ExpiryReturnPolicy!;
        int months = brand.ExpiryReturnMonths ?? (policy == "before_expiry" ? 3 : 0);
        int days = brand.ExpiryReturnDays ?? (policy == "before_expiry" ? 90 : 0);

        var batchRows = await (
                from b in db.Batches.AsNoTracking()
                join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
                join s in db.Suppliers.AsNoTracking() on p.SupplierId equals s.Id into supGroup
                from s in supGroup.DefaultIfEmpty()
                where p.BrandId == id && b.ExpiryDate != null && b.Quantity > 0
                orderby b.ExpiryDate ascending
                select new
                {
                    BatchId = b.Id,
                    b.BatchNumber,
                    b.ExpiryDate,
                    b.Quantity,
                    ProductId = p.Id,
                    ProductName = p.Name,
                    ProductCode = p.ProductCode ?? string.Empty,
                    p.PackSize,
                    SupplierName = s != null ? s.Name : string.Empty
                })
            .Take(150)
            .ToListAsync(cancellationToken);

        var intimationBatches = new List<BrandBatchIntimationDto>(batchRows.Count);
        int dueCount = 0;
        int dueUnits = 0;

        foreach (var r in batchRows)
        {
            var eval = EvaluateBatchIntimation(r.ExpiryDate, policy, months, days);
            if (eval.IsIntimationDue)
            {
                dueCount++;
                dueUnits += r.Quantity;
            }

            intimationBatches.Add(new BrandBatchIntimationDto
            {
                BatchId = r.BatchId,
                BatchNumber = r.BatchNumber,
                ExpiryDate = r.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                Quantity = r.Quantity,
                ProductId = r.ProductId,
                ProductName = r.ProductName,
                ProductCode = r.ProductCode,
                PackSize = r.PackSize,
                SupplierName = r.SupplierName,
                DaysRemaining = eval.DaysRemaining,
                ReturnDeadlineDate = eval.ReturnDeadlineDate,
                DaysUntilReturnDeadline = eval.DaysUntilReturnDeadline,
                IntimationStatus = eval.IntimationStatus,
                IsIntimationDue = eval.IsIntimationDue
            });
        }

        var dto = MapToDto(brand, productsCount, dueCount, dueUnits);
        dto.IntimationBatches = intimationBatches;
        return dto;
    }

    public async Task<BrandDto> CreateAsync(
        BrandUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string trimmedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedName))
            throw new InvalidOperationException("Brand name is required.");

        if (trimmedName.Length > 255)
            throw new InvalidOperationException("Brand name too long (max 255).");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        string lower = trimmedName.ToLowerInvariant();

        bool exists = await db.Brands.AsNoTracking()
            .AnyAsync(b => b.Name.ToLower() == lower, cancellationToken);

        if (exists)
            throw new InvalidOperationException("The name has already been taken.");

        var (policy, months, days, notes) = NormalizeExpiryPolicy(
            request.ExpiryReturnPolicy,
            request.ExpiryReturnMonths,
            request.ExpiryReturnDays,
            request.ExpiryReturnNotes);

        string? storedRelativeImage = CopyBrandImageIfProvided(request.SourceImageFilePath);
        var now = DateTime.UtcNow;

        var entity = new Brand
        {
            Name = trimmedName,
            Image = storedRelativeImage,
            ExpiryReturnPolicy = policy,
            ExpiryReturnMonths = months,
            ExpiryReturnDays = days,
            ExpiryReturnNotes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Brands.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return MapToDto(entity, 0, 0, 0);
    }

    public async Task<BrandDto> UpdateAsync(
        long id,
        BrandUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string trimmedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedName))
            throw new InvalidOperationException("Brand name is required.");

        if (trimmedName.Length > 255)
            throw new InvalidOperationException("Brand name too long (max 255).");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Brand not found.");

        string lower = trimmedName.ToLowerInvariant();
        bool duplicate = await db.Brands.AsNoTracking()
            .AnyAsync(b => b.Id != id && b.Name.ToLower() == lower, cancellationToken);

        if (duplicate)
            throw new InvalidOperationException("The name has already been taken.");

        var (policy, months, days, notes) = NormalizeExpiryPolicy(
            request.ExpiryReturnPolicy,
            request.ExpiryReturnMonths,
            request.ExpiryReturnDays,
            request.ExpiryReturnNotes);

        if (!string.IsNullOrWhiteSpace(request.SourceImageFilePath))
        {
            string? newRelImage = CopyBrandImageIfProvided(request.SourceImageFilePath);
            if (!string.IsNullOrWhiteSpace(newRelImage))
            {
                DeleteStoredBrandImageIfExists(entity.Image);
                entity.Image = newRelImage;
            }
        }

        entity.Name = trimmedName;
        entity.ExpiryReturnPolicy = policy;
        entity.ExpiryReturnMonths = months;
        entity.ExpiryReturnDays = days;
        entity.ExpiryReturnNotes = notes;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(id, cancellationToken))!;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (entity == null)
            return;

        bool usedByProducts = await db.Products.AsNoTracking()
            .AnyAsync(p => p.BrandId == id, cancellationToken);

        if (usedByProducts)
            throw new InvalidOperationException("Cannot delete: brand is used by one or more products.");

        DeleteStoredBrandImageIfExists(entity.Image);
        db.Brands.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ExportCsvAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var brands = await db.Brands.AsNoTracking()
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await using var writer = new StreamWriter(filePath, append: false, utf8WithBom);

        await writer.WriteLineAsync("name,expiry_return_policy,expiry_return_months,expiry_return_days,expiry_return_notes,image");
        foreach (var b in brands)
        {
            string line = string.Join(",",
                EscapeCsvField(SanitizeCsvValue(b.Name)),
                EscapeCsvField(SanitizeCsvValue(b.ExpiryReturnPolicy ?? "before_expiry")),
                EscapeCsvField((b.ExpiryReturnMonths ?? 3).ToString(CultureInfo.InvariantCulture)),
                EscapeCsvField((b.ExpiryReturnDays ?? 90).ToString(CultureInfo.InvariantCulture)),
                EscapeCsvField(SanitizeCsvValue(b.ExpiryReturnNotes ?? string.Empty)),
                EscapeCsvField(SanitizeCsvValue(b.Image ?? string.Empty)));

            await writer.WriteLineAsync(line);
        }
    }

    public async Task SaveTemplateCsvAsync(string filePath, CancellationToken cancellationToken = default)
    {
        const string content =
            "name,image\n" +
            "Acme,https://example.com/logo-acme.png\n" +
            "Nova Brands,\n";

        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(false), cancellationToken);
    }

    public async Task<BrandCsvValidationResult> ValidateCsvAsync(
        string filePath,
        string delimiter = ",",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new BrandCsvValidationResult
            {
                Ok = false,
                Message = "Selected CSV file does not exist."
            };
        }

        char delimChar = NormalizeDelimiter(delimiter);
        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8, cancellationToken);
        var nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        if (nonEmptyLines.Count == 0)
        {
            return new BrandCsvValidationResult
            {
                Ok = false,
                Message = "CSV must contain header columns: name[, image] (any order)."
            };
        }

        var (nameColIdx, imageColIdx, resolvedDelim) = ResolveColumnsAndDelimiter(nonEmptyLines[0], delimChar);
        if (nameColIdx == null)
        {
            return new BrandCsvValidationResult
            {
                Ok = false,
                Message = "CSV must contain header columns: name[, image] (accepted: name|brand|brand_name and image|image_url|logo)."
            };
        }

        delimChar = resolvedDelim;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existingLower = (await db.Brands.AsNoTracking()
                .Select(b => b.Name)
                .ToListAsync(cancellationToken))
            .Select(n => n.Trim().ToLowerInvariant())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new BrandCsvValidationResult
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

            string rawImage = imageColIdx.HasValue && imageColIdx.Value < cols.Count
                ? cols[imageColIdx.Value]
                : string.Empty;
            string cleanImage = NormalizeIncomingCell(rawImage, isFirstColumn: imageColIdx == 0);

            var (errors, warnings) = EvaluateRowIssues(cleanName, cleanImage, existingLower, seenInFile, strictDuplicateInFile: true);
            result.Total++;

            if (errors.Count > 0)
            {
                result.Invalid++;
                if (result.InvalidSamples.Count < PreviewInvalidLimit)
                {
                    result.InvalidSamples.Add(new BrandCsvSampleRow
                    {
                        Row = rowNo,
                        Name = cleanName,
                        Image = cleanImage,
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
                    result.ValidSamples.Add(new BrandCsvSampleRow
                    {
                        Row = rowNo,
                        Name = cleanName,
                        Image = cleanImage,
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

    public async Task<BrandCsvCommitStats> CommitCsvAsync(
        string filePath,
        bool insertValidOnly = true,
        string delimiter = ",",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new InvalidOperationException("CSV file not found.");

        char delimChar = NormalizeDelimiter(delimiter);
        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8, cancellationToken);
        var nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        if (nonEmptyLines.Count == 0)
            throw new InvalidOperationException("CSV header not recognized. Expected: name[, image].");

        var (nameColIdx, imageColIdx, resolvedDelim) = ResolveColumnsAndDelimiter(nonEmptyLines[0], delimChar);
        if (nameColIdx == null)
            throw new InvalidOperationException("CSV header not recognized. Expected: name[, image].");

        delimChar = resolvedDelim;
        var headerCells = ParseCsvLine(nonEmptyLines[0], delimChar)
            .Select(h => h.Trim().TrimStart('\uFEFF').ToLowerInvariant())
            .ToList();

        int? policyIdx = IndexOfHeader(headerCells, "expiry_return_policy");
        int? monthsIdx = IndexOfHeader(headerCells, "expiry_return_months");
        int? daysIdx = IndexOfHeader(headerCells, "expiry_return_days");
        int? notesIdx = IndexOfHeader(headerCells, "expiry_return_notes");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existingEntities = await db.Brands.ToListAsync(cancellationToken);
        var existingByLower = existingEntities
            .GroupBy(b => b.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var existingLowerSet = new HashSet<string>(existingByLower.Keys, StringComparer.OrdinalIgnoreCase);
        var stats = new BrandCsvCommitStats();
        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        for (int i = 1; i < nonEmptyLines.Count; i++)
        {
            var cols = ParseCsvLine(nonEmptyLines[i], delimChar);
            if (cols.All(string.IsNullOrWhiteSpace))
                continue;

            string rawName = nameColIdx.Value < cols.Count ? cols[nameColIdx.Value] : string.Empty;
            string cleanName = NormalizeIncomingCell(rawName, isFirstColumn: nameColIdx.Value == 0);

            string rawImage = imageColIdx.HasValue && imageColIdx.Value < cols.Count
                ? cols[imageColIdx.Value]
                : string.Empty;
            string cleanImage = NormalizeIncomingCell(rawImage, isFirstColumn: imageColIdx == 0);

            stats.Total++;

            var (errors, _) = EvaluateRowIssues(cleanName, cleanImage, existingLowerSet, seenInFile: null, strictDuplicateInFile: false);
            if (errors.Count > 0)
            {
                stats.Invalid++;
                if (insertValidOnly)
                    continue;

                throw new InvalidOperationException("Import aborted due to validation error.");
            }

            stats.Valid++;
            string lower = cleanName.ToLowerInvariant();

            string? rawPolicy = policyIdx.HasValue && policyIdx.Value < cols.Count ? cols[policyIdx.Value].Trim() : null;
            int? rawMonths = monthsIdx.HasValue && monthsIdx.Value < cols.Count && int.TryParse(cols[monthsIdx.Value].Trim(), out int mVal) ? mVal : null;
            int? rawDays = daysIdx.HasValue && daysIdx.Value < cols.Count && int.TryParse(cols[daysIdx.Value].Trim(), out int dVal) ? dVal : null;
            string? rawNotes = notesIdx.HasValue && notesIdx.Value < cols.Count ? cols[notesIdx.Value].Trim() : null;

            if (existingByLower.TryGetValue(lower, out var existingBrand))
            {
                stats.ExistingCount++;
                if (!string.IsNullOrWhiteSpace(cleanImage))
                {
                    existingBrand.Image = cleanImage;
                }
                if (!string.IsNullOrWhiteSpace(rawPolicy))
                {
                    var norm = NormalizeExpiryPolicy(rawPolicy, rawMonths, rawDays, rawNotes);
                    existingBrand.ExpiryReturnPolicy = norm.Policy;
                    existingBrand.ExpiryReturnMonths = norm.Months;
                    existingBrand.ExpiryReturnDays = norm.Days;
                    if (notesIdx.HasValue)
                        existingBrand.ExpiryReturnNotes = norm.Notes;
                }
                existingBrand.UpdatedAt = now;
            }
            else
            {
                stats.NewCount++;
                var norm = NormalizeExpiryPolicy(rawPolicy, rawMonths, rawDays, rawNotes);
                var created = new Brand
                {
                    Name = cleanName,
                    Image = string.IsNullOrWhiteSpace(cleanImage) ? null : cleanImage,
                    ExpiryReturnPolicy = norm.Policy,
                    ExpiryReturnMonths = norm.Months,
                    ExpiryReturnDays = norm.Days,
                    ExpiryReturnNotes = norm.Notes,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Brands.Add(created);
                existingByLower[lower] = created;
                existingLowerSet.Add(lower);
            }

            stats.Committed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return stats;
    }

    private BrandDto MapToDto(Brand b, int productsCount, int dueCount, int dueUnits)
    {
        string policy = string.IsNullOrWhiteSpace(b.ExpiryReturnPolicy) ? "before_expiry" : b.ExpiryReturnPolicy!;
        int months = b.ExpiryReturnMonths ?? (policy == "before_expiry" ? 3 : 0);
        int days = b.ExpiryReturnDays ?? (policy == "before_expiry" ? 90 : 0);

        return new BrandDto
        {
            Id = b.Id,
            Name = b.Name,
            Image = b.Image,
            ImageAbsolutePath = ResolveImageAbsolutePath(b.Image),
            ExpiryReturnPolicy = policy,
            ExpiryReturnMonths = months,
            ExpiryReturnDays = days,
            ExpiryReturnNotes = b.ExpiryReturnNotes,
            ExpiryPolicyLabel = FormatExpiryPolicyLabel(policy, months, days),
            ProductsCount = productsCount,
            IntimationDueCount = dueCount,
            IntimationDueUnits = dueUnits,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt
        };
    }

    private string? ResolveImageAbsolutePath(string? imageRelativeOrFull)
    {
        if (string.IsNullOrWhiteSpace(imageRelativeOrFull))
            return null;

        if (Path.IsPathRooted(imageRelativeOrFull) && File.Exists(imageRelativeOrFull))
            return imageRelativeOrFull;

        string normalizedRel = imageRelativeOrFull.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        string candidate = Path.Combine(_dbInitializer.StorageDirectoryPath, normalizedRel);
        return File.Exists(candidate) ? candidate : null;
    }

    private string? CopyBrandImageIfProvided(string? sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            return null;

        string ext = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".png";

        string brandsDir = Path.Combine(_dbInitializer.StorageDirectoryPath, "brands");
        Directory.CreateDirectory(brandsDir);

        string fileName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        string destPath = Path.Combine(brandsDir, fileName);
        File.Copy(sourceFilePath, destPath, overwrite: true);

        return $"brands/{fileName}";
    }

    private void DeleteStoredBrandImageIfExists(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        try
        {
            string? full = ResolveImageAbsolutePath(relativePath);
            if (!string.IsNullOrWhiteSpace(full) &&
                full.StartsWith(_dbInitializer.StorageDirectoryPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(full))
            {
                File.Delete(full);
            }
        }
        catch
        {
            // Ignore file cleanup errors
        }
    }

    private static DateTime SubtractMonthsNoOverflow(DateTime date, int months)
    {
        DateTime targetFirstOfMonth = new DateTime(date.Year, date.Month, 1).AddMonths(-months);
        int daysInTargetMonth = DateTime.DaysInMonth(targetFirstOfMonth.Year, targetFirstOfMonth.Month);
        int clampedDay = Math.Min(date.Day, daysInTargetMonth);
        return new DateTime(targetFirstOfMonth.Year, targetFirstOfMonth.Month, clampedDay);
    }

    private static (int? NameIdx, int? ImageIdx, char Delimiter) ResolveColumnsAndDelimiter(string headerLine, char initialDelim)
    {
        var (nameIdx, imageIdx) = MapHeaderColumns(ParseCsvLine(headerLine, initialDelim));
        if (nameIdx.HasValue)
            return (nameIdx, imageIdx, initialDelim);

        char guessed = GuessDelimiterFromSample(headerLine);
        if (guessed != initialDelim)
        {
            var (gName, gImg) = MapHeaderColumns(ParseCsvLine(headerLine, guessed));
            if (gName.HasValue)
                return (gName, gImg, guessed);
        }

        return (null, null, initialDelim);
    }

    private static (int? NameIdx, int? ImageIdx) MapHeaderColumns(List<string> headerRow)
    {
        int? nameIdx = null;
        int? imageIdx = null;

        for (int i = 0; i < headerRow.Count; i++)
        {
            string h = headerRow[i].Trim().TrimStart('\uFEFF').ToLowerInvariant();
            if (!nameIdx.HasValue && NameHeaderAliases.Contains(h, StringComparer.OrdinalIgnoreCase))
            {
                nameIdx = i;
            }
            else if (!imageIdx.HasValue && ImageHeaderAliases.Contains(h, StringComparer.OrdinalIgnoreCase))
            {
                imageIdx = i;
            }
        }

        return (nameIdx, imageIdx);
    }

    private static int? IndexOfHeader(List<string> lowerHeaders, string target)
    {
        for (int i = 0; i < lowerHeaders.Count; i++)
        {
            if (string.Equals(lowerHeaders[i], target, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return null;
    }

    private static (List<string> Errors, List<string> Warnings) EvaluateRowIssues(
        string name,
        string image,
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

        if (!string.IsNullOrEmpty(image) && image.Length > 255)
        {
            errors.Add("Image path/url too long.");
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
