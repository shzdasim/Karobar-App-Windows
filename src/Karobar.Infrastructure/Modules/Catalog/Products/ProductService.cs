using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Karobar.Application.Interfaces;
using Karobar.Domain.Entities;
using Karobar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Services;

public sealed class ProductService : IProductService
{
    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;
    private readonly IDatabaseInitializerService _dbInitializer;

    private static readonly Dictionary<string, string[]> CsvHeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["product_code"] = ["product_code", "code", "sku", "productcode", "prod_code"],
        ["name"] = ["name", "product", "product_name", "title"],
        ["formulation"] = ["formulation", "formula"],
        ["description"] = ["description", "desc", "details"],
        ["pack_size"] = ["pack_size", "units_per_pack", "units", "uom_qty", "pack"],
        ["quantity"] = ["quantity", "qty", "stock", "onhand"],
        ["pack_purchase_price"] = ["pack_purchase_price", "ppp", "purchase_price_pack", "pack_cost"],
        ["pack_sale_price"] = ["pack_sale_price", "psp", "sale_price_pack"],
        ["unit_purchase_price"] = ["unit_purchase_price", "upp", "purchase_price_unit", "unit_cost", "cost"],
        ["unit_sale_price"] = ["unit_sale_price", "usp", "sale_price_unit", "unit_price", "price"],
        ["whole_sale_pack_price"] = ["whole_sale_pack_price", "wspp", "wholesale_pack_price"],
        ["whole_sale_unit_price"] = ["whole_sale_unit_price", "wsup", "wholesale_unit_price"],
        ["avg_price"] = ["avg_price", "average_price", "weighted_avg", "wac", "wa_cost"],
        ["margin"] = ["margin", "gross_margin_pct"],
        ["narcotic"] = ["narcotic", "is_narcotic", "controlled"],
        ["max_discount"] = ["max_discount", "maxdisc", "max_discount_pct"],
        ["category_id"] = ["category_id", "cat_id"],
        ["brand_id"] = ["brand_id"],
        ["supplier_id"] = ["supplier_id", "vendor_id"],
        ["category"] = ["category", "category_name", "cat", "cat_name"],
        ["brand"] = ["brand", "brand_name"],
        ["supplier"] = ["supplier", "supplier_name", "vendor", "vendor_name"],
        ["rack"] = ["rack", "shelf", "bin"],
        ["barcode"] = ["barcode", "ean", "upc"]
    };

    public ProductService(
        IDbContextFactory<KarobarDbContext> dbFactory,
        IDatabaseInitializerService dbInitializer)
    {
        _dbFactory = dbFactory;
        _dbInitializer = dbInitializer;
    }

    public async Task<ProductListResponse> GetProductsAsync(
        ProductQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var locations = await db.StorageLocations.AsNoTracking()
            .ToDictionaryAsync(l => l.Id, l => l, cancellationToken);

        var defaultLoc = locations.Values.FirstOrDefault(l => l.IsDefault)
                         ?? locations.Values.OrderBy(l => l.Id).FirstOrDefault();

        var query = db.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            var matchingBrandIds = await db.Brands.AsNoTracking()
                .Where(b => EF.Functions.Like(b.Name, $"%{term}%"))
                .Select(b => (long?)b.Id)
                .ToListAsync(cancellationToken);
            var matchingSupplierIds = await db.Suppliers.AsNoTracking()
                .Where(s => EF.Functions.Like(s.Name, $"%{term}%"))
                .Select(s => (long?)s.Id)
                .ToListAsync(cancellationToken);

            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{term}%") ||
                EF.Functions.Like(p.ProductCode, $"%{term}%") ||
                (p.Barcode != null && EF.Functions.Like(p.Barcode, $"%{term}%")) ||
                (p.Formulation != null && EF.Functions.Like(p.Formulation, $"%{term}%")) ||
                (p.Rack != null && EF.Functions.Like(p.Rack, $"%{term}%")) ||
                (p.BrandId != null && matchingBrandIds.Contains(p.BrandId)) ||
                (p.SupplierId != null && matchingSupplierIds.Contains(p.SupplierId)));
        }

        if (filter.CategoryId.HasValue && filter.CategoryId.Value > 0)
        {
            query = query.Where(p => p.CategoryId == filter.CategoryId.Value);
        }

        if (filter.BrandId.HasValue && filter.BrandId.Value > 0)
        {
            query = query.Where(p => p.BrandId == filter.BrandId.Value);
        }

        if (filter.SupplierId.HasValue && filter.SupplierId.Value > 0)
        {
            var supId = filter.SupplierId.Value;
            var secondaryProductIds = await db.ProductSuppliers.AsNoTracking()
                .Where(ps => ps.SupplierId == supId)
                .Select(ps => ps.ProductId)
                .ToListAsync(cancellationToken);

            query = query.Where(p => p.SupplierId == supId || secondaryProductIds.Contains(p.Id));
        }

        if (filter.StorageLocationId.HasValue && filter.StorageLocationId.Value > 0)
        {
            query = query.Where(p => p.StorageLocationId == filter.StorageLocationId.Value);
        }

        if (filter.LowStockOnly)
        {
            var everPurchasedIds = await db.PurchaseInvoiceItems.AsNoTracking()
                .Select(pii => pii.ProductId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (everPurchasedIds.Count > 0)
            {
                query = query.Where(p => everPurchasedIds.Contains(p.Id) && p.Quantity < p.PackSize);
            }
            else
            {
                query = query.Where(p => p.Quantity < p.PackSize);
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var perPage = Math.Clamp(filter.PerPage <= 0 ? 25 : filter.PerPage, 1, 200);
        var lastPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)perPage));
        var currentPage = Math.Clamp(filter.Page <= 0 ? 1 : filter.Page, 1, lastPage);
        var skip = (currentPage - 1) * perPage;

        var pageProducts = await query
            .OrderByDescending(p => p.Id)
            .Skip(skip)
            .Take(perPage)
            .ToListAsync(cancellationToken);

        var pageIds = pageProducts.Select(p => p.Id).ToList();
        var pageCategoryIds = pageProducts.Where(p => p.CategoryId.HasValue).Select(p => p.CategoryId!.Value).Distinct().ToList();
        var pageBrandIds = pageProducts.Where(p => p.BrandId.HasValue).Select(p => p.BrandId!.Value).Distinct().ToList();
        var pageSupplierIds = pageProducts.Where(p => p.SupplierId.HasValue).Select(p => p.SupplierId!.Value).Distinct().ToList();

        var categories = pageCategoryIds.Count > 0
            ? await db.Categories.AsNoTracking()
                .Where(c => pageCategoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            : new Dictionary<long, string>();

        var brands = pageBrandIds.Count > 0
            ? await db.Brands.AsNoTracking()
                .Where(b => pageBrandIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken)
            : new Dictionary<long, string>();

        var suppliers = pageSupplierIds.Count > 0
            ? await db.Suppliers.AsNoTracking()
                .Where(s => pageSupplierIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken)
            : new Dictionary<long, string>();

        var batchStats = pageIds.Count > 0
            ? await db.Batches.AsNoTracking()
                .Where(b => pageIds.Contains(b.ProductId))
                .GroupBy(b => b.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Count = g.Count(),
                    TotalQty = g.Sum(x => x.Quantity)
                })
                .ToDictionaryAsync(x => x.ProductId, cancellationToken)
            : [];

        var items = pageProducts.Select(p =>
        {
            batchStats.TryGetValue(p.Id, out var bStat);
            var loc = p.StorageLocationId.HasValue && locations.TryGetValue(p.StorageLocationId.Value, out var l)
                ? l
                : defaultLoc;

            return new ProductListItemDto
            {
                Id = p.Id,
                ProductCode = p.ProductCode,
                Barcode = p.Barcode ?? string.Empty,
                Name = p.Name,
                Initials = ComputeInitials(p.Name),
                Formulation = p.Formulation,
                Rack = p.Rack,
                ImagePath = ResolveFullImagePath(p.Image),
                Narcotic = string.IsNullOrWhiteSpace(p.Narcotic) ? "no" : p.Narcotic,
                CategoryId = p.CategoryId,
                CategoryName = p.CategoryId.HasValue && categories.TryGetValue(p.CategoryId.Value, out var cName) ? cName : "—",
                BrandId = p.BrandId,
                BrandName = p.BrandId.HasValue && brands.TryGetValue(p.BrandId.Value, out var bName) ? bName : "—",
                SupplierId = p.SupplierId,
                SupplierName = p.SupplierId.HasValue && suppliers.TryGetValue(p.SupplierId.Value, out var sName) ? sName : "—",
                StorageLocationId = p.StorageLocationId ?? loc?.Id,
                StorageLocationName = loc?.Name ?? "Shop Counter",
                StorageLocationCode = loc?.Code ?? "SHOP",
                PackSize = Math.Max(1, p.PackSize),
                Quantity = p.Quantity,
                BatchesCount = bStat?.Count ?? 0,
                BatchedQuantity = bStat?.TotalQty ?? 0,
                PackPurchasePrice = p.PackPurchasePrice,
                UnitPurchasePrice = p.UnitPurchasePrice,
                PackSalePrice = p.PackSalePrice,
                UnitSalePrice = p.UnitSalePrice,
                WholeSalePackPrice = p.WholeSalePackPrice,
                WholeSaleUnitPrice = p.WholeSaleUnitPrice,
                AvgPrice = p.AvgPrice,
                Margin = p.Margin ?? 0m
            };
        }).ToList();

        return new ProductListResponse
        {
            Items = items,
            TotalCount = totalCount,
            CurrentPage = currentPage,
            PerPage = perPage,
            LastPage = lastPage,
            FromItem = totalCount == 0 ? 0 : skip + 1,
            ToItem = Math.Min(totalCount, skip + items.Count)
        };
    }

    public async Task<ProductFormLookupsDto> GetProductFormLookupsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var categories = await db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new LookupOptionDto { Id = c.Id, Name = c.Name })
            .ToListAsync(cancellationToken);

        var brands = await db.Brands.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new LookupOptionDto { Id = b.Id, Name = b.Name })
            .ToListAsync(cancellationToken);

        var suppliers = await db.Suppliers.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new LookupOptionDto { Id = s.Id, Name = s.Name })
            .ToListAsync(cancellationToken);

        var formulations = await db.Formulations.AsNoTracking()
            .OrderBy(f => f.Name)
            .Select(f => new FormulationOptionDto { Id = f.Id, Name = f.Name })
            .ToListAsync(cancellationToken);

        if (formulations.Count == 0)
        {
            var existingFormSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var productForms = await db.Products.AsNoTracking()
                .Where(p => p.Formulation != null && p.Formulation != "")
                .Select(p => p.Formulation!)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var pf in productForms)
            {
                var norm = NormalizeFormulationName(pf);
                if (!string.IsNullOrWhiteSpace(norm) && existingFormSet.Add(norm))
                {
                    formulations.Add(new FormulationOptionDto { Id = 0, Name = norm });
                }
            }
            formulations = formulations.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var rawLocations = await db.StorageLocations.AsNoTracking()
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.Name)
            .ToListAsync(cancellationToken);

        var locations = rawLocations.Select(l => new StorageLocationDto
        {
            Id = l.Id,
            Name = l.Name,
            Code = l.Code,
            Type = string.IsNullOrWhiteSpace(l.Type) ? "shop" : l.Type,
            DefaultRack = l.DefaultRack,
            IsDefault = l.IsDefault,
            IsActive = l.IsActive,
            Notes = l.Notes
        }).ToList();

        return new ProductFormLookupsDto
        {
            Categories = categories,
            Brands = brands,
            Suppliers = suppliers,
            Formulations = formulations,
            StorageLocations = locations,
            ShopType = setting?.ShopType ?? "pharmacy",
            CurrencySymbol = setting?.CurrencySymbol ?? "Rs."
        };
    }

    public async Task<GeneratedProductCodesDto> GenerateNewCodeAndBarcodeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var maxId = await db.Products.AsNoTracking()
            .MaxAsync(p => (long?)p.Id, cancellationToken) ?? 0L;

        var recentCodes = await db.Products.AsNoTracking()
            .OrderByDescending(p => p.Id)
            .Take(30)
            .Select(p => p.ProductCode)
            .ToListAsync(cancellationToken);

        var maxNum = (int)maxId;
        foreach (var code in recentCodes)
        {
            var m = Regex.Match(code ?? string.Empty, @"PRD-(\d+)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > maxNum)
            {
                maxNum = n;
            }
        }

        var nextNum = maxNum + 1;
        string candidateCode;
        do
        {
            candidateCode = $"PRD-{nextNum:D4}";
            nextNum++;
        } while (await db.Products.AsNoTracking().AnyAsync(p => p.ProductCode == candidateCode, cancellationToken));

        string candidateBarcode;
        do
        {
            var rand = Random.Shared.NextInt64(0, 10_000_000_000L);
            candidateBarcode = $"PRD{rand:D10}";
        } while (await db.Products.AsNoTracking().AnyAsync(p => p.Barcode == candidateBarcode, cancellationToken));

        return new GeneratedProductCodesDto
        {
            ProductCode = candidateCode,
            Barcode = candidateBarcode
        };
    }

    public async Task<ProductDetailDto?> GetProductByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (p == null)
            return null;

        return new ProductDetailDto
        {
            Id = p.Id,
            ProductCode = p.ProductCode,
            Barcode = p.Barcode ?? string.Empty,
            Name = p.Name,
            Formulation = p.Formulation,
            Description = p.Description,
            ImagePath = p.Image,
            FullImagePath = ResolveFullImagePath(p.Image),
            PackSize = Math.Max(1, p.PackSize),
            Quantity = p.Quantity,
            PackPurchasePrice = p.PackPurchasePrice,
            UnitPurchasePrice = p.UnitPurchasePrice,
            PackSalePrice = p.PackSalePrice,
            UnitSalePrice = p.UnitSalePrice,
            WholeSalePackPrice = p.WholeSalePackPrice,
            WholeSaleUnitPrice = p.WholeSaleUnitPrice,
            WholeSaleMargin = p.WholeSaleMargin,
            AvgPrice = p.AvgPrice,
            Margin = p.Margin,
            Narcotic = string.IsNullOrWhiteSpace(p.Narcotic) ? "no" : p.Narcotic,
            MaxDiscount = p.MaxDiscount,
            ExpiryWarningDays = p.ExpiryWarningDays,
            Rack = p.Rack,
            CategoryId = p.CategoryId,
            BrandId = p.BrandId,
            SupplierId = p.SupplierId,
            StorageLocationId = p.StorageLocationId
        };
    }

    public async Task<ProductResult> CreateProductAsync(
        ProductUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateProductRequest(request);
        if (validationError != null)
            return ProductResult.Failure(validationError);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var code = request.ProductCode.Trim();
        var name = request.Name.Trim();
        var barcode = request.Barcode.Trim();

        if (await db.Products.AnyAsync(p => p.ProductCode.ToLower() == code.ToLower(), cancellationToken))
            return ProductResult.Failure($"Product code '{code}' is already in use.");

        if (await db.Products.AnyAsync(p => p.Name.ToLower() == name.ToLower(), cancellationToken))
            return ProductResult.Failure($"Product name '{name}' already exists.");

        if (await db.Products.AnyAsync(p => p.Barcode != null && p.Barcode.ToLower() == barcode.ToLower(), cancellationToken))
            return ProductResult.Failure($"Barcode '{barcode}' is already assigned to another product.");

        var canonicalFormulation = await ResolveOrCreateFormulationInternalAsync(db, request.Formulation, cancellationToken);

        var defaultLocId = request.StorageLocationId;
        if (!defaultLocId.HasValue || defaultLocId.Value <= 0)
        {
            defaultLocId = await db.StorageLocations
                .OrderByDescending(l => l.IsDefault)
                .ThenBy(l => l.Id)
                .Select(l => (long?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? storedImageRelPath = null;
        if (!string.IsNullOrWhiteSpace(request.SourceImageFilePath) && File.Exists(request.SourceImageFilePath))
        {
            storedImageRelPath = CopyProductImageToStorage(request.SourceImageFilePath);
        }

        var now = DateTime.UtcNow;
        var product = new Product
        {
            ProductCode = code,
            Barcode = barcode,
            Name = name,
            Formulation = canonicalFormulation,
            Description = NullIfEmpty(request.Description),
            Image = storedImageRelPath,
            PackSize = Math.Max(1, request.PackSize),
            Quantity = Math.Max(0, request.Quantity),
            PackPurchasePrice = Math.Max(0m, request.PackPurchasePrice),
            UnitPurchasePrice = Math.Max(0m, request.UnitPurchasePrice),
            PackSalePrice = Math.Max(0m, request.PackSalePrice),
            UnitSalePrice = Math.Max(0m, request.UnitSalePrice),
            WholeSalePackPrice = request.WholeSalePackPrice,
            WholeSaleUnitPrice = request.WholeSaleUnitPrice,
            WholeSaleMargin = request.WholeSaleMargin,
            AvgPrice = request.AvgPrice > 0m ? request.AvgPrice : Math.Max(0m, request.UnitPurchasePrice),
            Margin = request.Margin,
            Narcotic = string.Equals(request.Narcotic, "yes", StringComparison.OrdinalIgnoreCase) ? "yes" : "no",
            MaxDiscount = request.MaxDiscount,
            ExpiryWarningDays = request.ExpiryWarningDays,
            Rack = NullIfEmpty(request.Rack),
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            SupplierId = request.SupplierId,
            StorageLocationId = defaultLocId,
            CreatedAt = now,
            UpdatedAt = now
        };

        NormalizeDerivedProductPrices(product);

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        if (product.SupplierId.HasValue && product.SupplierId.Value > 0)
        {
            await SyncPrimarySupplierMappingAsync(db, product, now, cancellationToken);
        }

        return ProductResult.Success($"Product '{product.Name}' added!", product.Id);
    }

    public async Task<ProductResult> UpdateProductAsync(
        long id,
        ProductUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateProductRequest(request);
        if (validationError != null)
            return ProductResult.Failure(validationError);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product == null)
            return ProductResult.Failure("Product not found.");

        var code = request.ProductCode.Trim();
        var name = request.Name.Trim();
        var barcode = request.Barcode.Trim();

        if (await db.Products.AnyAsync(p => p.Id != id && p.ProductCode.ToLower() == code.ToLower(), cancellationToken))
            return ProductResult.Failure($"Product code '{code}' is already in use.");

        if (await db.Products.AnyAsync(p => p.Id != id && p.Name.ToLower() == name.ToLower(), cancellationToken))
            return ProductResult.Failure($"Product name '{name}' already exists.");

        if (await db.Products.AnyAsync(p => p.Id != id && p.Barcode != null && p.Barcode.ToLower() == barcode.ToLower(), cancellationToken))
            return ProductResult.Failure($"Barcode '{barcode}' is already assigned to another product.");

        var canonicalFormulation = await ResolveOrCreateFormulationInternalAsync(db, request.Formulation, cancellationToken);

        if (request.ClearExistingImage && !string.IsNullOrWhiteSpace(product.Image))
        {
            DeleteStoredProductImage(product.Image);
            product.Image = null;
        }

        if (!string.IsNullOrWhiteSpace(request.SourceImageFilePath) && File.Exists(request.SourceImageFilePath))
        {
            var currentFull = ResolveFullImagePath(product.Image);
            if (!string.Equals(currentFull, request.SourceImageFilePath, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(product.Image))
                    DeleteStoredProductImage(product.Image);
                product.Image = CopyProductImageToStorage(request.SourceImageFilePath);
            }
        }

        var now = DateTime.UtcNow;
        product.ProductCode = code;
        product.Barcode = barcode;
        product.Name = name;
        product.Formulation = canonicalFormulation;
        product.Description = NullIfEmpty(request.Description);
        product.PackSize = Math.Max(1, request.PackSize);
        product.PackPurchasePrice = Math.Max(0m, request.PackPurchasePrice);
        product.UnitPurchasePrice = Math.Max(0m, request.UnitPurchasePrice);
        product.PackSalePrice = Math.Max(0m, request.PackSalePrice);
        product.UnitSalePrice = Math.Max(0m, request.UnitSalePrice);
        product.WholeSalePackPrice = request.WholeSalePackPrice;
        product.WholeSaleUnitPrice = request.WholeSaleUnitPrice;
        product.WholeSaleMargin = request.WholeSaleMargin;
        product.AvgPrice = request.AvgPrice > 0m ? request.AvgPrice : Math.Max(0m, request.UnitPurchasePrice);
        product.Margin = request.Margin;
        product.Narcotic = string.Equals(request.Narcotic, "yes", StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
        product.MaxDiscount = request.MaxDiscount;
        product.ExpiryWarningDays = request.ExpiryWarningDays;
        product.Rack = NullIfEmpty(request.Rack);
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.SupplierId = request.SupplierId;
        if (request.StorageLocationId.HasValue && request.StorageLocationId.Value > 0)
        {
            product.StorageLocationId = request.StorageLocationId;
        }
        product.UpdatedAt = now;

        NormalizeDerivedProductPrices(product);

        await db.SaveChangesAsync(cancellationToken);

        if (product.SupplierId.HasValue && product.SupplierId.Value > 0)
        {
            await SyncPrimarySupplierMappingAsync(db, product, now, cancellationToken);
        }

        return ProductResult.Success($"Product '{product.Name}' updated!", product.Id);
    }

    public async Task<ProductResult> DeleteProductAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product == null)
            return ProductResult.Failure("Product not found.");

        var blockReason = await CheckProductDeleteBlockersAsync(db, product, cancellationToken);
        if (blockReason != null)
            return ProductResult.Failure(blockReason);

        await CleanupAndRemoveProductAsync(db, product, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return ProductResult.Success($"Product '{product.Name}' deleted.");
    }

    public async Task<BulkDeleteProductsResult> BulkDeleteProductsAsync(
        IReadOnlyCollection<long> productIds,
        CancellationToken cancellationToken = default)
    {
        var result = new BulkDeleteProductsResult();
        if (productIds.Count == 0)
            return result;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        foreach (var id in productIds.Distinct())
        {
            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (product == null)
            {
                result.FailedItems.Add(new BulkDeleteFailedItemDto
                {
                    Id = id,
                    Name = $"#{id}",
                    Reason = "Not found"
                });
                continue;
            }

            var blockReason = await CheckProductDeleteBlockersAsync(db, product, cancellationToken);
            if (blockReason != null)
            {
                result.FailedItems.Add(new BulkDeleteFailedItemDto
                {
                    Id = id,
                    Name = product.Name,
                    Reason = blockReason
                });
                continue;
            }

            await CleanupAndRemoveProductAsync(db, product, cancellationToken);
            result.DeletedCount++;
        }

        if (result.DeletedCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<ProductResult> BulkUpdateProductMetaAsync(
        BulkUpdateProductMetaRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProductIds.Count == 0)
            return ProductResult.Failure("Please select at least one product.");

        if (!request.CategoryId.HasValue && !request.BrandId.HasValue && !request.SupplierId.HasValue)
            return ProductResult.Failure("Provide at least one of Category, Brand, or Supplier to update.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = request.ProductIds.Distinct().ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        if (products.Count == 0)
            return ProductResult.Failure("No matching products found to update.");

        var now = DateTime.UtcNow;
        foreach (var p in products)
        {
            if (request.CategoryId.HasValue && request.CategoryId.Value > 0)
                p.CategoryId = request.CategoryId.Value;
            if (request.BrandId.HasValue && request.BrandId.Value > 0)
                p.BrandId = request.BrandId.Value;
            if (request.SupplierId.HasValue && request.SupplierId.Value > 0)
                p.SupplierId = request.SupplierId.Value;
            p.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (request.SupplierId.HasValue && request.SupplierId.Value > 0)
        {
            foreach (var p in products)
            {
                await SyncPrimarySupplierMappingAsync(db, p, now, cancellationToken);
            }
        }

        return ProductResult.Success($"Updated {products.Count} product(s).");
    }

    public async Task<FormulationOptionDto?> ResolveOrCreateFormulationAsync(
        string? rawName,
        CancellationToken cancellationToken = default)
    {
        var clean = NormalizeFormulationName(rawName);
        if (string.IsNullOrWhiteSpace(clean))
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var lower = clean.ToLowerInvariant();
        var existing = await db.Formulations
            .FirstOrDefaultAsync(f => f.NormalizedName.ToLower() == lower || f.Name.ToLower() == lower, cancellationToken);

        if (existing != null)
        {
            return new FormulationOptionDto { Id = existing.Id, Name = existing.Name };
        }

        var now = DateTime.UtcNow;
        var entity = new Formulation
        {
            Name = clean,
            NormalizedName = lower,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Formulations.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new FormulationOptionDto { Id = entity.Id, Name = entity.Name };
    }

    // =========================================================================
    // STORAGE LOCATIONS
    // =========================================================================

    public async Task<List<StorageLocationDto>> GetStorageLocationsAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.StorageLocations.AsNoTracking().AsQueryable();
        if (activeOnly)
            query = query.Where(l => l.IsActive);

        var locs = await query
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.Name)
            .ToListAsync(cancellationToken);

        var prodCounts = await db.Products.AsNoTracking()
            .Where(p => p.StorageLocationId != null)
            .GroupBy(p => p.StorageLocationId!.Value)
            .Select(g => new { LocId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LocId, x => x.Count, cancellationToken);

        var batchCounts = await db.Batches.AsNoTracking()
            .Where(b => b.StorageLocationId != null)
            .GroupBy(b => b.StorageLocationId!.Value)
            .Select(g => new { LocId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LocId, x => x.Count, cancellationToken);

        return locs.Select(l => new StorageLocationDto
        {
            Id = l.Id,
            Name = l.Name,
            Code = l.Code,
            Type = string.IsNullOrWhiteSpace(l.Type) ? "shop" : l.Type,
            DefaultRack = l.DefaultRack,
            IsDefault = l.IsDefault,
            IsActive = l.IsActive,
            Notes = l.Notes,
            ProductsCount = prodCounts.GetValueOrDefault(l.Id),
            BatchesCount = batchCounts.GetValueOrDefault(l.Id)
        }).ToList();
    }

    public async Task<ProductResult> SaveStorageLocationAsync(
        StorageLocationSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ProductResult.Failure("Please enter a location name.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var name = request.Name.Trim();
        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code))
        {
            var slug = Regex.Replace(name.ToUpperInvariant(), @"[^A-Z0-9]+", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(slug)) slug = "LOC";
            if (slug.Length > 16) slug = slug[..16];
            code = slug;
            var counter = 1;
            while (await db.StorageLocations.AnyAsync(l => l.Code.ToLower() == code.ToLower() && l.Id != (request.Id ?? 0), cancellationToken))
            {
                counter++;
                code = $"{slug}-{counter}";
            }
        }
        else
        {
            var conflict = await db.StorageLocations.AnyAsync(
                l => l.Code.ToLower() == code.ToLower() && l.Id != (request.Id ?? 0),
                cancellationToken);
            if (conflict)
                return ProductResult.Failure($"Location code '{code}' is already in use.");
        }

        var now = DateTime.UtcNow;
        if (request.IsDefault)
        {
            var currentDefaults = await db.StorageLocations.Where(l => l.IsDefault).ToListAsync(cancellationToken);
            foreach (var d in currentDefaults)
            {
                d.IsDefault = false;
            }
        }

        if (request.Id.HasValue && request.Id.Value > 0)
        {
            var existing = await db.StorageLocations.FirstOrDefaultAsync(l => l.Id == request.Id.Value, cancellationToken);
            if (existing == null)
                return ProductResult.Failure("Storage location not found.");

            existing.Name = name;
            existing.Code = code;
            existing.Type = string.IsNullOrWhiteSpace(request.Type) ? "shop" : request.Type;
            existing.DefaultRack = NullIfEmpty(request.DefaultRack);
            existing.IsDefault = request.IsDefault;
            existing.IsActive = request.IsActive;
            existing.Notes = NullIfEmpty(request.Notes);
            existing.UpdatedAt = now;

            await db.SaveChangesAsync(cancellationToken);
            return ProductResult.Success("Storage location updated.", existing.Id);
        }
        else
        {
            var entity = new StorageLocation
            {
                Name = name,
                Code = code,
                Type = string.IsNullOrWhiteSpace(request.Type) ? "shop" : request.Type,
                DefaultRack = NullIfEmpty(request.DefaultRack),
                IsDefault = request.IsDefault,
                IsActive = request.IsActive,
                Notes = NullIfEmpty(request.Notes),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.StorageLocations.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            return ProductResult.Success("Storage location created.", entity.Id);
        }
    }

    public async Task<ProductResult> DeleteStorageLocationAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var loc = await db.StorageLocations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (loc == null)
            return ProductResult.Failure("Storage location not found.");

        if (loc.IsDefault)
            return ProductResult.Failure("Cannot delete the default storage location. Mark another location as default first.");

        var fallbackId = await db.StorageLocations
            .Where(l => l.Id != id)
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.Id)
            .Select(l => (long?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var productsToMove = await db.Products.Where(p => p.StorageLocationId == id).ToListAsync(cancellationToken);
        foreach (var p in productsToMove)
        {
            p.StorageLocationId = fallbackId;
        }

        var batchesToMove = await db.Batches.Where(b => b.StorageLocationId == id).ToListAsync(cancellationToken);
        foreach (var b in batchesToMove)
        {
            b.StorageLocationId = fallbackId;
        }

        db.StorageLocations.Remove(loc);
        await db.SaveChangesAsync(cancellationToken);

        return ProductResult.Success("Storage location deleted.");
    }

    // =========================================================================
    // BATCHES & EXPIRY MANAGEMENT (FEFO & WORKBENCH)
    // =========================================================================

    public async Task<List<BatchWorkbenchProductSearchItemDto>> SearchProductsForBatchWorkbenchAsync(
        string? query,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var safeLimit = Math.Clamp(limit <= 0 ? 25 : limit, 1, 50);
        var q = (query ?? string.Empty).Trim();

        List<Product> products;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var matching = await db.Products.AsNoTracking()
                .Where(p =>
                    EF.Functions.Like(p.Name, $"%{q}%") ||
                    EF.Functions.Like(p.ProductCode, $"%{q}%") ||
                    (p.Barcode != null && EF.Functions.Like(p.Barcode, $"%{q}%")) ||
                    (p.Formulation != null && EF.Functions.Like(p.Formulation, $"%{q}%")) ||
                    (p.Rack != null && EF.Functions.Like(p.Rack, $"%{q}%")))
                .Take(150)
                .ToListAsync(cancellationToken);

            var qLower = q.ToLowerInvariant();
            products = matching
                .OrderBy(p =>
                    string.Equals(p.ProductCode, q, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Barcode, q, StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : p.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                            ? 1
                            : p.ProductCode.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                                ? 2
                                : 3)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Take(safeLimit)
                .ToList();
        }
        else
        {
            products = await db.Products.AsNoTracking()
                .OrderByDescending(p => p.Id)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);
        }

        if (products.Count == 0)
            return [];

        var productIds = products.Select(p => p.Id).ToList();
        var batchTotals = await db.Batches.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId))
            .GroupBy(b => b.ProductId)
            .Select(g => new { ProductId = g.Key, TotalQty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.TotalQty, cancellationToken);

        return products.Select(p =>
        {
            var totalQty = Math.Max(0, p.Quantity);
            var batchedQty = Math.Max(0, batchTotals.GetValueOrDefault(p.Id));
            return new BatchWorkbenchProductSearchItemDto
            {
                Id = p.Id,
                Name = p.Name,
                ProductCode = p.ProductCode,
                Barcode = p.Barcode ?? string.Empty,
                Formulation = p.Formulation,
                Rack = p.Rack,
                PackSize = Math.Max(1, p.PackSize),
                UnitPurchasePrice = p.UnitPurchasePrice,
                UnitSalePrice = p.UnitSalePrice,
                StorageLocationId = p.StorageLocationId,
                Quantity = totalQty,
                BatchedQuantity = batchedQty,
                UnassignedQuantity = Math.Max(0, totalQty - batchedQty)
            };
        }).ToList();
    }

    public async Task<GlobalBatchDirectoryResponse> GetGlobalBatchesDirectoryAsync(
        GlobalBatchQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var globalWarningDays = Math.Clamp(
            filter.WarningDays ?? setting?.ExpiryWarningDays ?? 90,
            1,
            730);

        var locations = await db.StorageLocations.AsNoTracking()
            .ToDictionaryAsync(l => l.Id, l => l, cancellationToken);
        var defaultLoc = locations.Values.FirstOrDefault(l => l.IsDefault)
                         ?? locations.Values.OrderBy(l => l.Id).FirstOrDefault();

        var productsDict = await db.Products.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p, cancellationToken);

        var categoriesDict = await db.Categories.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var brandsDict = await db.Brands.AsNoTracking()
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var suppliersDict = await db.Suppliers.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var batchQuery = db.Batches.AsNoTracking().AsQueryable();
        if (!filter.IncludeZeroStock)
        {
            batchQuery = batchQuery.Where(b => b.Quantity > 0);
        }

        if (filter.StorageLocationId.HasValue && filter.StorageLocationId.Value > 0)
        {
            batchQuery = batchQuery.Where(b => b.StorageLocationId == filter.StorageLocationId.Value);
        }

        var rawBatches = await batchQuery.ToListAsync(cancellationToken);
        var today = DateTime.Today;
        var searchTerm = (filter.Search ?? string.Empty).Trim();
        var hasSearch = !string.IsNullOrWhiteSpace(searchTerm);

        var summary = new GlobalBatchSummaryDto
        {
            GlobalWarningDays = globalWarningDays
        };

        var allRows = new List<GlobalBatchDirectoryRowDto>(rawBatches.Count);

        foreach (var b in rawBatches)
        {
            if (!productsDict.TryGetValue(b.ProductId, out var product))
                continue;

            if (filter.CategoryId.HasValue && filter.CategoryId.Value > 0 && product.CategoryId != filter.CategoryId.Value)
                continue;

            if (filter.BrandId.HasValue && filter.BrandId.Value > 0 && product.BrandId != filter.BrandId.Value)
                continue;

            if (filter.SupplierId.HasValue && filter.SupplierId.Value > 0 && product.SupplierId != filter.SupplierId.Value)
                continue;

            var catName = product.CategoryId.HasValue && categoriesDict.TryGetValue(product.CategoryId.Value, out var cn) ? cn : "—";
            var brandName = product.BrandId.HasValue && brandsDict.TryGetValue(product.BrandId.Value, out var bn) ? bn : "—";
            var supName = product.SupplierId.HasValue && suppliersDict.TryGetValue(product.SupplierId.Value, out var sn) ? sn : "—";

            var loc = b.StorageLocationId.HasValue && locations.TryGetValue(b.StorageLocationId.Value, out var bl)
                ? bl
                : (product.StorageLocationId.HasValue && locations.TryGetValue(product.StorageLocationId.Value, out var pl) ? pl : defaultLoc);

            var rack = !string.IsNullOrWhiteSpace(b.Rack)
                ? b.Rack
                : (!string.IsNullOrWhiteSpace(product.Rack) ? product.Rack : loc?.DefaultRack);

            if (hasSearch)
            {
                bool matchesSearch =
                    b.BatchNumber.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    product.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    product.ProductCode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(product.Barcode) && product.Barcode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(product.Formulation) && product.Formulation.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(rack) && rack.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                    brandName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    supName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);

                if (!matchesSearch)
                    continue;
            }

            var effectiveWarningDays = Math.Clamp(product.ExpiryWarningDays ?? globalWarningDays, 1, 730);
            int? daysRem = b.ExpiryDate.HasValue
                ? (int)(b.ExpiryDate.Value.Date - today).TotalDays
                : null;

            string status;
            if (!daysRem.HasValue)
            {
                status = "safe";
            }
            else if (daysRem.Value < 0)
            {
                status = "expired";
            }
            else if (daysRem.Value <= 30)
            {
                status = "critical";
            }
            else if (daysRem.Value <= effectiveWarningDays)
            {
                status = "warning";
            }
            else
            {
                status = "safe";
            }

            var qty = Math.Max(0, b.Quantity);
            var unitCost = b.UnitPurchasePrice ?? product.UnitPurchasePrice;
            var unitMrp = b.UnitSalePrice ?? product.UnitSalePrice;
            var packSize = Math.Max(1, b.PackSize > 0 ? b.PackSize : product.PackSize);
            var costValue = Math.Round(qty * unitCost, 2);
            var saleValue = Math.Round(qty * unitMrp, 2);

            summary.TotalBatchesTracked++;
            switch (status)
            {
                case "expired":
                    summary.TotalExpiredBatches++;
                    summary.ExpiredStockUnits += qty;
                    summary.ExpiredCostValue += costValue;
                    summary.ExpiredSaleValue += saleValue;
                    break;
                case "critical":
                    summary.TotalCriticalBatches++;
                    summary.CriticalStockUnits += qty;
                    summary.CriticalCostValue += costValue;
                    summary.AtRiskStockUnits += qty;
                    summary.AtRiskCostValue += costValue;
                    summary.AtRiskSaleValue += saleValue;
                    break;
                case "warning":
                    summary.TotalWarningBatches++;
                    summary.WarningStockUnits += qty;
                    summary.WarningCostValue += costValue;
                    summary.AtRiskStockUnits += qty;
                    summary.AtRiskCostValue += costValue;
                    summary.AtRiskSaleValue += saleValue;
                    break;
                default:
                    summary.TotalSafeBatches++;
                    summary.SafeStockUnits += qty;
                    break;
            }

            allRows.Add(new GlobalBatchDirectoryRowDto
            {
                Id = b.Id,
                ProductId = product.Id,
                ProductName = product.Name,
                ProductCode = product.ProductCode,
                Barcode = product.Barcode ?? string.Empty,
                Formulation = product.Formulation,
                PackSize = packSize,
                ProductTotalQuantity = Math.Max(0, product.Quantity),
                CategoryId = product.CategoryId,
                CategoryName = catName,
                BrandId = product.BrandId,
                BrandName = brandName,
                SupplierId = product.SupplierId,
                SupplierName = supName,
                BatchNumber = b.BatchNumber,
                ExpiryDate = b.ExpiryDate,
                DaysRemaining = daysRem,
                EffectiveWarningDays = effectiveWarningDays,
                ExpiryStatus = status,
                Quantity = qty,
                StorageLocationId = b.StorageLocationId ?? loc?.Id,
                StorageLocationName = loc?.Name ?? "Shop Counter",
                StorageLocationCode = loc?.Code ?? "SHOP",
                StorageLocationType = loc?.Type ?? "shop",
                Rack = rack,
                UnitPurchasePrice = unitCost,
                UnitSalePrice = unitMrp,
                PackPurchasePrice = b.PackPurchasePrice ?? Math.Round(unitCost * packSize, 2),
                PackSalePrice = b.PackSalePrice ?? Math.Round(unitMrp * packSize, 2)
            });
        }

        var statusFilter = (filter.Status ?? "all").Trim().ToLowerInvariant();
        IEnumerable<GlobalBatchDirectoryRowDto> filteredRows = statusFilter switch
        {
            "expired" => allRows.Where(r => r.ExpiryStatus == "expired"),
            "critical" => allRows.Where(r => r.ExpiryStatus == "critical"),
            "warning" => allRows.Where(r => r.ExpiryStatus == "warning"),
            "safe" => allRows.Where(r => r.ExpiryStatus == "safe"),
            _ => allRows
        };

        var orderedRows = filteredRows
            .OrderBy(r => r.ExpiryDate ?? DateTime.MaxValue)
            .ThenByDescending(r => r.Quantity)
            .ThenBy(r => r.Id)
            .ToList();

        var totalCount = orderedRows.Count;
        var perPage = Math.Clamp(filter.PerPage <= 0 ? 25 : filter.PerPage, 1, 200);
        var lastPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)perPage));
        var currentPage = Math.Clamp(filter.Page <= 0 ? 1 : filter.Page, 1, lastPage);
        var skip = (currentPage - 1) * perPage;
        var pageItems = orderedRows.Skip(skip).Take(perPage).ToList();

        return new GlobalBatchDirectoryResponse
        {
            Items = pageItems,
            Summary = summary,
            TotalCount = totalCount,
            CurrentPage = currentPage,
            PerPage = perPage,
            LastPage = lastPage,
            FromItem = totalCount == 0 ? 0 : skip + 1,
            ToItem = Math.Min(totalCount, skip + pageItems.Count)
        };
    }

    public async Task<ProductBatchSummaryDto> GetProductBatchesAsync(
        long productId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product == null)
            return new ProductBatchSummaryDto { ProductId = productId };

        var locations = await db.StorageLocations.AsNoTracking()
            .ToDictionaryAsync(l => l.Id, l => l, cancellationToken);
        var defaultLoc = locations.Values.FirstOrDefault(l => l.IsDefault)
                         ?? locations.Values.OrderBy(l => l.Id).FirstOrDefault();

        var rawBatches = await db.Batches.AsNoTracking()
            .Where(b => b.ProductId == productId)
            .ToListAsync(cancellationToken);

        var today = DateTime.Today;

        // Sort using exact FEFO rules from BatchController.php:
        // 1. Non-expired first
        // 2. Earliest expiry date
        // 3. Location priority: shop (0) -> cold_storage (1) -> others (2)
        // 4. Batch ID ascending
        var sorted = rawBatches
            .OrderBy(b => b.ExpiryDate.HasValue && b.ExpiryDate.Value.Date >= today ? 0 : 1)
            .ThenBy(b => b.ExpiryDate ?? DateTime.MaxValue)
            .ThenBy(b =>
            {
                var locType = b.StorageLocationId.HasValue && locations.TryGetValue(b.StorageLocationId.Value, out var l)
                    ? l.Type
                    : "shop";
                return locType == "shop" ? 0 : locType == "cold_storage" ? 1 : 2;
            })
            .ThenBy(b => b.Id)
            .ToList();

        var fefoAssigned = false;
        var batchDtos = new List<ProductBatchItemDto>();

        foreach (var b in sorted)
        {
            var isExpired = b.ExpiryDate.HasValue && b.ExpiryDate.Value.Date < today;
            int? daysRem = b.ExpiryDate.HasValue
                ? (int)(b.ExpiryDate.Value.Date - today).TotalDays
                : null;

            var isFefo = false;
            if (!isExpired && b.Quantity > 0 && !fefoAssigned)
            {
                isFefo = true;
                fefoAssigned = true;
            }

            var loc = b.StorageLocationId.HasValue && locations.TryGetValue(b.StorageLocationId.Value, out var l)
                ? l
                : (product.StorageLocationId.HasValue && locations.TryGetValue(product.StorageLocationId.Value, out var pl) ? pl : defaultLoc);

            batchDtos.Add(new ProductBatchItemDto
            {
                Id = b.Id,
                ProductId = b.ProductId,
                BatchNumber = b.BatchNumber,
                ExpiryDate = b.ExpiryDate,
                Quantity = b.Quantity,
                StorageLocationId = b.StorageLocationId ?? loc?.Id,
                StorageLocationName = loc?.Name ?? "Shop Counter",
                StorageLocationCode = loc?.Code ?? "SHOP",
                StorageLocationType = loc?.Type ?? "shop",
                Rack = !string.IsNullOrWhiteSpace(b.Rack) ? b.Rack : (!string.IsNullOrWhiteSpace(product.Rack) ? product.Rack : loc?.DefaultRack),
                UnitPurchasePrice = b.UnitPurchasePrice ?? product.UnitPurchasePrice,
                UnitSalePrice = b.UnitSalePrice ?? product.UnitSalePrice,
                PackPurchasePrice = b.PackPurchasePrice ?? product.PackPurchasePrice,
                PackSalePrice = b.PackSalePrice ?? product.PackSalePrice,
                WholeSaleUnitPrice = b.WholeSaleUnitPrice ?? product.WholeSaleUnitPrice,
                WholeSalePackPrice = b.WholeSalePackPrice ?? product.WholeSalePackPrice,
                IsExpired = isExpired,
                DaysRemaining = daysRem,
                IsFefoRecommended = isFefo
            });
        }

        var totalStock = Math.Max(0, product.Quantity);
        var batchedStock = batchDtos.Sum(b => b.Quantity);
        var unassignedStock = Math.Max(0, totalStock - batchedStock);

        var locationBreakdown = batchDtos
            .GroupBy(b => new { b.StorageLocationId, b.StorageLocationName, b.StorageLocationCode, b.StorageLocationType })
            .Select(g => new LocationStockPillDto
            {
                LocationId = g.Key.StorageLocationId,
                Label = $"{g.Key.StorageLocationName} ({g.Key.StorageLocationCode})",
                Type = g.Key.StorageLocationType,
                Quantity = g.Sum(x => x.Quantity),
                BatchCount = g.Count()
            })
            .ToList();

        return new ProductBatchSummaryDto
        {
            ProductId = productId,
            TotalStock = totalStock,
            BatchedStock = batchedStock,
            UnassignedStock = unassignedStock,
            Batches = batchDtos,
            LocationBreakdown = locationBreakdown
        };
    }

    public async Task<ProductResult> SaveProductBatchAsync(
        long productId,
        BatchSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BatchNumber))
            return ProductResult.Failure("Please enter a Batch Number.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product == null)
            return ProductResult.Failure("Product not found.");

        var batchNumber = request.BatchNumber.Trim();
        var expiryDate = request.ExpiryDate.Date;
        var qty = Math.Max(0, request.Quantity);
        var now = DateTime.UtcNow;

        var defaultLocId = await db.StorageLocations
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.Id)
            .Select(l => (long?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var targetLocId = request.StorageLocationId ?? product.StorageLocationId ?? defaultLocId;
        var rack = !string.IsNullOrWhiteSpace(request.Rack) ? request.Rack.Trim() : product.Rack;

        var currentTotal = Math.Max(0, product.Quantity);
        var allBatches = await db.Batches.Where(b => b.ProductId == productId).ToListAsync(cancellationToken);

        if (request.BatchId.HasValue && request.BatchId.Value > 0)
        {
            var existingById = allBatches.FirstOrDefault(b => b.Id == request.BatchId.Value);
            if (existingById == null)
                return ProductResult.Failure("Batch not found.");

            var dupConflict = allBatches.Any(b =>
                b.Id != existingById.Id &&
                string.Equals(b.BatchNumber, batchNumber, StringComparison.OrdinalIgnoreCase) &&
                b.StorageLocationId == targetLocId);

            if (dupConflict)
                return ProductResult.Failure($"Batch '{batchNumber}' already exists at the selected location.");

            var otherBatched = allBatches.Where(b => b.Id != existingById.Id).Sum(b => b.Quantity);
            var availableUnassigned = Math.Max(0, currentTotal - otherBatched);

            if (qty > availableUnassigned)
            {
                var unbatchedPool = Math.Max(0, currentTotal - (otherBatched + existingById.Quantity));
                return ProductResult.Failure(
                    $"Cannot assign {qty} units to batch. Maximum assignable quantity is {availableUnassigned} units (Current batch: {existingById.Quantity}, unassigned stock: {unbatchedPool}).");
            }

            var packSize = Math.Max(1, existingById.PackSize > 0 ? existingById.PackSize : product.PackSize);
            existingById.BatchNumber = batchNumber;
            existingById.ExpiryDate = expiryDate;
            existingById.Quantity = qty;
            existingById.StorageLocationId = targetLocId;
            existingById.Rack = rack;
            if (request.UnitSalePrice.HasValue)
            {
                existingById.UnitSalePrice = request.UnitSalePrice.Value;
                existingById.PackSalePrice = Math.Round(request.UnitSalePrice.Value * packSize, 2);
            }
            if (request.UnitPurchasePrice.HasValue)
            {
                existingById.UnitPurchasePrice = request.UnitPurchasePrice.Value;
                existingById.PackPurchasePrice = Math.Round(request.UnitPurchasePrice.Value * packSize, 2);
            }
            existingById.UpdatedAt = now;

            await db.SaveChangesAsync(cancellationToken);
            return ProductResult.Success($"Batch '{batchNumber}' updated successfully.", existingById.Id);
        }
        else
        {
            var existingMatch = allBatches.FirstOrDefault(b =>
                string.Equals(b.BatchNumber, batchNumber, StringComparison.OrdinalIgnoreCase) &&
                b.StorageLocationId == targetLocId);

            var existingBatchQty = existingMatch?.Quantity ?? 0;
            var otherBatched = Math.Max(0, allBatches.Sum(b => b.Quantity) - existingBatchQty);
            var availableUnassigned = Math.Max(0, currentTotal - otherBatched);

            if (existingMatch == null)
            {
                if (availableUnassigned <= 0)
                {
                    return ProductResult.Failure(
                        $"Cannot add a new batch because this product has 0 unassigned stock available (Total on-hand: {currentTotal}, already batched: {otherBatched}).");
                }
                if (qty <= 0)
                {
                    return ProductResult.Failure(
                        $"Batch quantity must be between 1 and {availableUnassigned} unassigned units.");
                }
            }

            if (qty > availableUnassigned)
            {
                return ProductResult.Failure(
                    $"Cannot assign {qty} units to batch. Only {availableUnassigned} unassigned units are available for this batch.");
            }

            if (existingMatch != null)
            {
                var matchPackSize = Math.Max(1, existingMatch.PackSize > 0 ? existingMatch.PackSize : product.PackSize);
                existingMatch.ExpiryDate = expiryDate;
                existingMatch.Quantity = qty;
                existingMatch.StorageLocationId = targetLocId;
                existingMatch.Rack = rack;
                if (request.UnitSalePrice.HasValue)
                {
                    existingMatch.UnitSalePrice = request.UnitSalePrice.Value;
                    existingMatch.PackSalePrice = Math.Round(request.UnitSalePrice.Value * matchPackSize, 2);
                }
                if (request.UnitPurchasePrice.HasValue)
                {
                    existingMatch.UnitPurchasePrice = request.UnitPurchasePrice.Value;
                    existingMatch.PackPurchasePrice = Math.Round(request.UnitPurchasePrice.Value * matchPackSize, 2);
                }
                existingMatch.UpdatedAt = now;

                await db.SaveChangesAsync(cancellationToken);
                return ProductResult.Success($"Batch '{batchNumber}' updated successfully.", existingMatch.Id);
            }

            var prodPackSize = Math.Max(1, product.PackSize);
            var unitSale = request.UnitSalePrice ?? product.UnitSalePrice;
            var unitPurchase = request.UnitPurchasePrice ?? product.UnitPurchasePrice;
            var newBatch = new Batch
            {
                ProductId = productId,
                BatchNumber = batchNumber,
                ExpiryDate = expiryDate,
                Quantity = qty,
                PackSize = prodPackSize,
                StorageLocationId = targetLocId,
                Rack = rack,
                UnitSalePrice = unitSale,
                PackSalePrice = request.UnitSalePrice.HasValue ? Math.Round(unitSale * prodPackSize, 2) : product.PackSalePrice,
                UnitPurchasePrice = unitPurchase,
                PackPurchasePrice = request.UnitPurchasePrice.HasValue ? Math.Round(unitPurchase * prodPackSize, 2) : product.PackPurchasePrice,
                WholeSaleUnitPrice = product.WholeSaleUnitPrice,
                WholeSalePackPrice = product.WholeSalePackPrice,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Batches.Add(newBatch);
            await db.SaveChangesAsync(cancellationToken);
            return ProductResult.Success($"Batch '{batchNumber}' added successfully.", newBatch.Id);
        }
    }

    public async Task<ProductResult> MoveBatchLocationAsync(
        long batchId,
        MoveBatchLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
            return ProductResult.Failure("Quantity to move must be at least 1.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var sourceBatch = await db.Batches.FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (sourceBatch == null)
            return ProductResult.Failure("Source batch not found.");

        var toLocation = await db.StorageLocations.FirstOrDefaultAsync(l => l.Id == request.ToStorageLocationId, cancellationToken);
        if (toLocation == null)
            return ProductResult.Failure("Destination storage location not found.");

        var sourceQty = Math.Max(0, sourceBatch.Quantity);
        var moveQty = request.Quantity;
        if (moveQty > sourceQty)
            return ProductResult.Failure($"Cannot move {moveQty} units. Only {sourceQty} units are available in this batch location.");

        var targetRack = !string.IsNullOrWhiteSpace(request.Rack)
            ? request.Rack.Trim()
            : (toLocation.DefaultRack ?? sourceBatch.Rack);

        var now = DateTime.UtcNow;

        if (sourceBatch.StorageLocationId == toLocation.Id)
        {
            sourceBatch.Rack = targetRack;
            sourceBatch.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return ProductResult.Success("Batch rack/location updated.", sourceBatch.Id);
        }

        var targetBatch = await db.Batches.FirstOrDefaultAsync(b =>
            b.ProductId == sourceBatch.ProductId &&
            b.BatchNumber.ToLower() == sourceBatch.BatchNumber.ToLower() &&
            b.StorageLocationId == toLocation.Id &&
            b.Id != sourceBatch.Id,
            cancellationToken);

        if (targetBatch != null)
        {
            targetBatch.Quantity += moveQty;
            if (!string.IsNullOrWhiteSpace(targetRack))
                targetBatch.Rack = targetRack;
            targetBatch.UpdatedAt = now;

            sourceBatch.Quantity = sourceQty - moveQty;
            if (sourceBatch.Quantity <= 0)
            {
                db.Batches.Remove(sourceBatch);
            }
            else
            {
                sourceBatch.UpdatedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
            return ProductResult.Success($"Moved {moveQty} units to {toLocation.Name}.", targetBatch.Id);
        }

        if (moveQty == sourceQty)
        {
            sourceBatch.StorageLocationId = toLocation.Id;
            sourceBatch.Rack = targetRack;
            sourceBatch.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return ProductResult.Success($"Moved all {moveQty} units to {toLocation.Name}.", sourceBatch.Id);
        }

        // Partial move -> split batch
        sourceBatch.Quantity = sourceQty - moveQty;
        sourceBatch.UpdatedAt = now;

        var splitBatch = new Batch
        {
            ProductId = sourceBatch.ProductId,
            BatchNumber = sourceBatch.BatchNumber,
            ExpiryDate = sourceBatch.ExpiryDate,
            Quantity = moveQty,
            PackSize = sourceBatch.PackSize,
            StorageLocationId = toLocation.Id,
            Rack = targetRack,
            PackPurchasePrice = sourceBatch.PackPurchasePrice,
            UnitPurchasePrice = sourceBatch.UnitPurchasePrice,
            PackSalePrice = sourceBatch.PackSalePrice,
            UnitSalePrice = sourceBatch.UnitSalePrice,
            WholeSalePackPrice = sourceBatch.WholeSalePackPrice,
            WholeSaleUnitPrice = sourceBatch.WholeSaleUnitPrice,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Batches.Add(splitBatch);
        await db.SaveChangesAsync(cancellationToken);
        return ProductResult.Success($"Split and moved {moveQty} units to {toLocation.Name}.", splitBatch.Id);
    }

    public async Task<ProductResult> DeleteBatchAsync(long batchId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (batch == null)
            return ProductResult.Failure("Batch not found.");

        var hasSales = await db.SaleInvoiceItems.AnyAsync(
            sii => sii.ProductId == batch.ProductId && sii.BatchNumber == batch.BatchNumber,
            cancellationToken);

        if (hasSales)
        {
            return ProductResult.Failure(
                $"Cannot delete batch '{batch.BatchNumber}' because sale transactions are recorded for it. Adjust its quantity to 0 instead.");
        }

        db.Batches.Remove(batch);
        await db.SaveChangesAsync(cancellationToken);
        return ProductResult.Success("Batch removed (units returned to unassigned stock).");
    }

    // =========================================================================
    // MULTI-SUPPLIER CATALOG
    // =========================================================================

    public async Task<List<ProductSupplierLinkDto>> GetProductSuppliersAsync(
        long productId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product == null)
            return [];

        var suppliers = await db.Suppliers.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s, cancellationToken);

        var links = await db.ProductSuppliers.AsNoTracking()
            .Where(ps => ps.ProductId == productId)
            .OrderByDescending(ps => ps.IsPreferred)
            .ThenBy(ps => ps.SupplierId)
            .ToListAsync(cancellationToken);

        return links.Select(ps =>
        {
            suppliers.TryGetValue(ps.SupplierId, out var sup);
            return new ProductSupplierLinkDto
            {
                ProductId = ps.ProductId,
                SupplierId = ps.SupplierId,
                SupplierName = sup?.Name ?? $"Supplier #{ps.SupplierId}",
                SupplierPhone = sup?.Phone,
                SupplierSku = ps.SupplierSku,
                LastPurchasePrice = ps.LastPurchasePrice,
                LeadTimeDays = ps.LeadTimeDays,
                MinOrderQty = Math.Max(1, ps.MinOrderQty),
                IsPreferred = ps.IsPreferred,
                IsPrimarySupplier = product.SupplierId == ps.SupplierId,
                Notes = ps.Notes
            };
        }).ToList();
    }

    public async Task<ProductResult> SaveProductSupplierAsync(
        long productId,
        ProductSupplierSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SupplierId <= 0)
            return ProductResult.Failure("Please select a supplier.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product == null)
            return ProductResult.Failure("Product not found.");

        var supplierExists = await db.Suppliers.AnyAsync(s => s.Id == request.SupplierId, cancellationToken);
        if (!supplierExists)
            return ProductResult.Failure("Selected supplier does not exist.");

        var now = DateTime.UtcNow;
        if (request.IsPreferred)
        {
            var others = await db.ProductSuppliers
                .Where(ps => ps.ProductId == productId && ps.SupplierId != request.SupplierId)
                .ToListAsync(cancellationToken);
            foreach (var o in others)
            {
                o.IsPreferred = false;
                o.UpdatedAt = now;
            }
        }

        var existing = await db.ProductSuppliers
            .FirstOrDefaultAsync(ps => ps.ProductId == productId && ps.SupplierId == request.SupplierId, cancellationToken);

        if (existing != null)
        {
            existing.SupplierSku = NullIfEmpty(request.SupplierSku);
            existing.LastPurchasePrice = request.LastPurchasePrice ?? product.UnitPurchasePrice;
            existing.LeadTimeDays = request.LeadTimeDays;
            existing.MinOrderQty = Math.Max(1, request.MinOrderQty);
            existing.IsPreferred = request.IsPreferred;
            existing.Notes = NullIfEmpty(request.Notes);
            existing.UpdatedAt = now;
        }
        else
        {
            db.ProductSuppliers.Add(new ProductSupplier
            {
                ProductId = productId,
                SupplierId = request.SupplierId,
                SupplierSku = NullIfEmpty(request.SupplierSku),
                LastPurchasePrice = request.LastPurchasePrice ?? product.UnitPurchasePrice,
                LeadTimeDays = request.LeadTimeDays,
                MinOrderQty = Math.Max(1, request.MinOrderQty),
                IsPreferred = request.IsPreferred,
                Notes = NullIfEmpty(request.Notes),
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return ProductResult.Success("Supplier linked to product.");
    }

    public async Task<ProductResult> RemoveProductSupplierAsync(
        long productId,
        long supplierId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product == null)
            return ProductResult.Failure("Product not found.");

        if (product.SupplierId == supplierId)
            return ProductResult.Failure("Cannot remove the primary supplier. Change the product primary supplier first.");

        var link = await db.ProductSuppliers
            .FirstOrDefaultAsync(ps => ps.ProductId == productId && ps.SupplierId == supplierId, cancellationToken);

        if (link != null)
        {
            db.ProductSuppliers.Remove(link);
            await db.SaveChangesAsync(cancellationToken);
        }

        return ProductResult.Success("Supplier mapping removed.");
    }

    // =========================================================================
    // SUBSTITUTE LOOKUP
    // =========================================================================

    public async Task<SubstituteProductsResult> GetSubstituteProductsAsync(
        long productId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product == null)
            return new SubstituteProductsResult();

        var result = new SubstituteProductsResult
        {
            SourceProductId = product.Id,
            SourceProductName = product.Name,
            SourceFormulation = product.Formulation,
            SourceUnitSalePrice = product.UnitSalePrice,
            SourceQuantity = product.Quantity
        };

        var formulation = (product.Formulation ?? string.Empty).Trim();
        var query = db.Products.AsNoTracking().Where(p => p.Id != product.Id);

        if (!string.IsNullOrWhiteSpace(formulation))
        {
            result.MatchType = "formulation";
            var primarySalt = Regex.Replace(
                formulation,
                @"\b\d+(\.\d+)?\s*(mg|ml|mcg|g|iu|%)\b",
                "",
                RegexOptions.IgnoreCase).Trim();

            if (!string.IsNullOrWhiteSpace(primarySalt) &&
                primarySalt.Length >= 3 &&
                !string.Equals(primarySalt, formulation, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(p =>
                    p.Formulation != null &&
                    (EF.Functions.Like(p.Formulation, $"%{formulation}%") ||
                     EF.Functions.Like(p.Formulation, $"%{primarySalt}%")));
            }
            else
            {
                query = query.Where(p => p.Formulation != null && EF.Functions.Like(p.Formulation, $"%{formulation}%"));
            }
        }
        else if (product.CategoryId.HasValue && product.CategoryId.Value > 0)
        {
            result.MatchType = "category";
            query = query.Where(p => p.CategoryId == product.CategoryId.Value);
        }
        else
        {
            result.MatchType = "none";
            return result;
        }

        var rawCandidates = await query.ToListAsync(cancellationToken);
        var candidates = rawCandidates
            .OrderByDescending(p => p.Quantity > 0)
            .ThenBy(p => p.UnitSalePrice)
            .Take(25)
            .ToList();

        var candidateIds = candidates.Select(c => c.Id).ToList();
        var brands = await db.Brands.AsNoTracking()
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
        var categories = await db.Categories.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var today = DateTime.Today;
        var candidateBatches = await db.Batches.AsNoTracking()
            .Where(b => candidateIds.Contains(b.ProductId) && b.Quantity > 0)
            .ToListAsync(cancellationToken);

        foreach (var sub in candidates)
        {
            var subBatches = candidateBatches
                .Where(b => b.ProductId == sub.Id)
                .OrderBy(b => b.ExpiryDate.HasValue && b.ExpiryDate.Value.Date >= today ? 0 : 1)
                .ThenBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                .ToList();

            var fefoBatch = subBatches.FirstOrDefault(b => !b.ExpiryDate.HasValue || b.ExpiryDate.Value.Date >= today)
                            ?? subBatches.FirstOrDefault();

            var effectiveUnitPrice = fefoBatch?.UnitSalePrice is > 0m
                ? fefoBatch.UnitSalePrice.Value
                : sub.UnitSalePrice;

            var priceDiff = Math.Round(effectiveUnitPrice - product.UnitSalePrice, 2);
            var isExact = !string.IsNullOrWhiteSpace(formulation) &&
                          string.Equals((sub.Formulation ?? string.Empty).Trim(), formulation, StringComparison.OrdinalIgnoreCase);

            result.Substitutes.Add(new SubstituteProductItemDto
            {
                Id = sub.Id,
                Name = sub.Name,
                ProductCode = sub.ProductCode,
                Barcode = sub.Barcode,
                Formulation = sub.Formulation,
                IsExactFormulation = isExact,
                Rack = sub.Rack,
                PackSize = Math.Max(1, sub.PackSize),
                Quantity = sub.Quantity,
                UnitSalePrice = effectiveUnitPrice,
                PackSalePrice = sub.PackSalePrice,
                PriceDiff = priceDiff,
                BrandName = sub.BrandId.HasValue && brands.TryGetValue(sub.BrandId.Value, out var bName) ? bName : "—",
                CategoryName = sub.CategoryId.HasValue && categories.TryGetValue(sub.CategoryId.Value, out var cName) ? cName : "—",
                FefoBatchNumber = fefoBatch?.BatchNumber,
                FefoExpiryDate = fefoBatch?.ExpiryDate?.ToString("yyyy-MM-dd"),
                FefoBatchQuantity = fefoBatch?.Quantity
            });
        }

        return result;
    }

    // =========================================================================
    // CSV EXPORT & IMPORT
    // =========================================================================

    public string GetCsvTemplateContent()
    {
        return "product_code,name,pack_size,unit_purchase_price,unit_sale_price,category,brand,supplier,quantity,barcode,narcotic,max_discount,formulation,description,rack\n" +
               "PRD-0001,Paracetamol 500mg,10,3.5,5,Analgesics,Acme,ABC Pharma,0,1234567890123,no,10,Paracetamol 500mg,Pain reliever,R1\n";
    }

    public async Task<string> ExportProductsCsvAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var brands = await db.Brands.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var products = await db.Products.AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("product_code,name,pack_size,unit_purchase_price,unit_sale_price,pack_purchase_price,pack_sale_price,whole_sale_pack_price,whole_sale_unit_price,avg_price,margin,quantity,category,brand,supplier,barcode,narcotic,max_discount,formulation,description,rack");

        foreach (var p in products)
        {
            var catName = p.CategoryId.HasValue && categories.TryGetValue(p.CategoryId.Value, out var c) ? c : "";
            var brName = p.BrandId.HasValue && brands.TryGetValue(p.BrandId.Value, out var b) ? b : "";
            var supName = p.SupplierId.HasValue && suppliers.TryGetValue(p.SupplierId.Value, out var s) ? s : "";

            var row = new[]
            {
                EscapeCsvCell(SanitizeFormulaInjection(p.ProductCode)),
                EscapeCsvCell(SanitizeFormulaInjection(p.Name)),
                p.PackSize.ToString(CultureInfo.InvariantCulture),
                p.UnitPurchasePrice.ToString("0.####", CultureInfo.InvariantCulture),
                p.UnitSalePrice.ToString("0.####", CultureInfo.InvariantCulture),
                p.PackPurchasePrice.ToString("0.####", CultureInfo.InvariantCulture),
                p.PackSalePrice.ToString("0.####", CultureInfo.InvariantCulture),
                p.WholeSalePackPrice?.ToString("0.####", CultureInfo.InvariantCulture) ?? "",
                p.WholeSaleUnitPrice?.ToString("0.####", CultureInfo.InvariantCulture) ?? "",
                p.AvgPrice.ToString("0.####", CultureInfo.InvariantCulture),
                p.Margin?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
                p.Quantity.ToString(CultureInfo.InvariantCulture),
                EscapeCsvCell(SanitizeFormulaInjection(catName)),
                EscapeCsvCell(SanitizeFormulaInjection(brName)),
                EscapeCsvCell(SanitizeFormulaInjection(supName)),
                EscapeCsvCell(SanitizeFormulaInjection(p.Barcode ?? "")),
                EscapeCsvCell(SanitizeFormulaInjection(p.Narcotic)),
                p.MaxDiscount?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
                EscapeCsvCell(SanitizeFormulaInjection(p.Formulation ?? "")),
                EscapeCsvCell(SanitizeFormulaInjection(p.Description ?? "")),
                EscapeCsvCell(SanitizeFormulaInjection(p.Rack ?? ""))
            };
            sb.AppendLine(string.Join(",", row));
        }

        return sb.ToString();
    }

    public async Task<ProductCsvValidationResult> ValidateCsvImportAsync(
        string filePath,
        string delimiter = ",",
        bool createMissingRefs = true,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return new ProductCsvValidationResult { Ok = false, Message = "CSV file not found." };

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonEmptyLines.Count == 0)
            return new ProductCsvValidationResult { Ok = false, Message = "CSV file is empty." };

        var delimChar = ResolveCsvDelimiter(nonEmptyLines[0], delimiter);
        var headerCells = ParseCsvLine(nonEmptyLines[0], delimChar);
        var colMap = MapHeaderColumns(headerCells);

        if (!colMap.ContainsKey("product_code") || !colMap.ContainsKey("name") || !colMap.ContainsKey("pack_size"))
        {
            return new ProductCsvValidationResult
            {
                Ok = false,
                Message = "CSV must contain a header with columns: product_code, name, pack_size (order/aliases allowed)."
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existingProducts = await db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.ProductCode, p.Barcode })
            .ToListAsync(cancellationToken);

        var barcodeToCode = existingProducts
            .Where(p => !string.IsNullOrWhiteSpace(p.Barcode))
            .GroupBy(p => p.Barcode!.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().ProductCode.Trim().ToLowerInvariant());

        var categoriesByName = await db.Categories.AsNoTracking()
            .ToDictionaryAsync(c => c.Name.Trim().ToLowerInvariant(), c => c.Id, cancellationToken);
        var brandsByName = await db.Brands.AsNoTracking()
            .ToDictionaryAsync(b => b.Name.Trim().ToLowerInvariant(), b => b.Id, cancellationToken);
        var suppliersByName = await db.Suppliers.AsNoTracking()
            .ToDictionaryAsync(s => s.Name.Trim().ToLowerInvariant(), s => s.Id, cancellationToken);

        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var res = new ProductCsvValidationResult
        {
            Ok = true,
            UsedDelimiter = delimChar.ToString()
        };

        for (var i = 1; i < nonEmptyLines.Count; i++)
        {
            var rowCells = ParseCsvLine(nonEmptyLines[i], delimChar);
            var rowData = ExtractCsvRowData(rowCells, colMap);
            var errors = ValidateCsvRow(
                rowData,
                barcodeToCode,
                seenCodes,
                seenBarcodes,
                categoriesByName,
                brandsByName,
                suppliersByName,
                createMissingRefs,
                strictDuplicateCheck: true);

            res.TotalRows++;
            var sample = new ProductCsvSampleRowDto
            {
                RowNumber = i + 1,
                ProductCode = rowData.GetValueOrDefault("product_code") ?? "",
                Name = rowData.GetValueOrDefault("name") ?? "",
                PackSize = rowData.GetValueOrDefault("pack_size") ?? "",
                UnitPurchasePrice = rowData.GetValueOrDefault("unit_purchase_price") ?? "",
                UnitSalePrice = rowData.GetValueOrDefault("unit_sale_price") ?? "",
                Category = rowData.GetValueOrDefault("category") ?? rowData.GetValueOrDefault("category_id") ?? "",
                Brand = rowData.GetValueOrDefault("brand") ?? rowData.GetValueOrDefault("brand_id") ?? "",
                Supplier = rowData.GetValueOrDefault("supplier") ?? rowData.GetValueOrDefault("supplier_id") ?? "",
                ErrorsSummary = string.Join("; ", errors)
            };

            if (errors.Count > 0)
            {
                res.InvalidRows++;
                if (res.InvalidSamples.Count < 20)
                    res.InvalidSamples.Add(sample);
            }
            else
            {
                res.ValidRows++;
                seenCodes.Add(sample.ProductCode.Trim());
                var bc = rowData.GetValueOrDefault("barcode")?.Trim();
                if (!string.IsNullOrWhiteSpace(bc))
                    seenBarcodes.Add(bc);

                if (res.ValidSamples.Count < 10)
                    res.ValidSamples.Add(sample);
            }
        }

        res.Message = res.InvalidRows > 0
            ? $"Found {res.InvalidRows} invalid row(s) out of {res.TotalRows}."
            : $"All {res.ValidRows} row(s) are valid.";

        return res;
    }

    public async Task<ProductCsvCommitResult> CommitCsvImportAsync(
        string filePath,
        bool insertValidOnly = true,
        string delimiter = ",",
        bool createMissingRefs = true,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return new ProductCsvCommitResult { Succeeded = false, Message = "CSV file not found." };

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonEmptyLines.Count == 0)
            return new ProductCsvCommitResult { Succeeded = false, Message = "CSV file is empty." };

        var delimChar = ResolveCsvDelimiter(nonEmptyLines[0], delimiter);
        var headerCells = ParseCsvLine(nonEmptyLines[0], delimChar);
        var colMap = MapHeaderColumns(headerCells);

        if (!colMap.ContainsKey("product_code") || !colMap.ContainsKey("name") || !colMap.ContainsKey("pack_size"))
        {
            return new ProductCsvCommitResult
            {
                Succeeded = false,
                Message = "CSV header not recognized (need product_code, name, pack_size)."
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existingProducts = await db.Products.ToListAsync(cancellationToken);
        var codeToProduct = existingProducts
            .GroupBy(p => p.ProductCode.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var barcodeToCode = existingProducts
            .Where(p => !string.IsNullOrWhiteSpace(p.Barcode))
            .GroupBy(p => p.Barcode!.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().ProductCode.Trim().ToLowerInvariant());

        var categoriesByName = await db.Categories.AsNoTracking()
            .ToDictionaryAsync(c => c.Name.Trim().ToLowerInvariant(), c => c.Id, cancellationToken);
        var brandsByName = await db.Brands.AsNoTracking()
            .ToDictionaryAsync(b => b.Name.Trim().ToLowerInvariant(), b => b.Id, cancellationToken);
        var suppliersByName = await db.Suppliers.AsNoTracking()
            .ToDictionaryAsync(s => s.Name.Trim().ToLowerInvariant(), s => s.Id, cancellationToken);

        var defaultLocId = await db.StorageLocations.AsNoTracking()
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.Id)
            .Select(l => (long?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var result = new ProductCsvCommitResult();
        var now = DateTime.UtcNow;

        for (var i = 1; i < nonEmptyLines.Count; i++)
        {
            var rowCells = ParseCsvLine(nonEmptyLines[i], delimChar);
            var rowData = ExtractCsvRowData(rowCells, colMap);
            result.Total++;

            var errors = ValidateCsvRow(
                rowData,
                barcodeToCode,
                new HashSet<string>(),
                new HashSet<string>(),
                categoriesByName,
                brandsByName,
                suppliersByName,
                createMissingRefs,
                strictDuplicateCheck: false);

            if (errors.Count > 0)
            {
                result.Invalid++;
                if (insertValidOnly)
                    continue;
                return new ProductCsvCommitResult
                {
                    Succeeded = false,
                    Message = $"Import aborted at row {i + 1}: {string.Join("; ", errors)}"
                };
            }

            result.Valid++;

            var code = rowData["product_code"]!.Trim();
            var name = rowData["name"]!.Trim();
            var packSize = int.TryParse(rowData.GetValueOrDefault("pack_size"), out var ps) && ps > 0 ? ps : 1;

            var catId = await ResolveCsvRelationIdAsync(
                db, rowData.GetValueOrDefault("category_id"), rowData.GetValueOrDefault("category"),
                categoriesByName, createMissingRefs, "category", now, cancellationToken);

            var brandId = await ResolveCsvRelationIdAsync(
                db, rowData.GetValueOrDefault("brand_id"), rowData.GetValueOrDefault("brand"),
                brandsByName, createMissingRefs, "brand", now, cancellationToken);

            var supplierId = await ResolveCsvRelationIdAsync(
                db, rowData.GetValueOrDefault("supplier_id"), rowData.GetValueOrDefault("supplier"),
                suppliersByName, createMissingRefs, "supplier", now, cancellationToken);

            var canonicalFormulation = await ResolveOrCreateFormulationInternalAsync(
                db, rowData.GetValueOrDefault("formulation"), cancellationToken);

            decimal? upp = ParseNullableDecimal(rowData.GetValueOrDefault("unit_purchase_price"));
            decimal? usp = ParseNullableDecimal(rowData.GetValueOrDefault("unit_sale_price"));
            decimal? ppp = ParseNullableDecimal(rowData.GetValueOrDefault("pack_purchase_price"));
            decimal? psp = ParseNullableDecimal(rowData.GetValueOrDefault("pack_sale_price"));
            decimal? wspp = ParseNullableDecimal(rowData.GetValueOrDefault("whole_sale_pack_price"));
            decimal? wsup = ParseNullableDecimal(rowData.GetValueOrDefault("whole_sale_unit_price"));
            decimal? avg = ParseNullableDecimal(rowData.GetValueOrDefault("avg_price"));
            decimal? maxDisc = ParseNullableDecimal(rowData.GetValueOrDefault("max_discount"));
            int? qtyExplicit = ParseNullableInt(rowData.GetValueOrDefault("quantity"));

            if (upp == null && ppp != null) upp = Math.Round(ppp.Value / packSize, 4);
            if (usp == null && psp != null) usp = Math.Round(psp.Value / packSize, 4);
            if (ppp == null && upp != null) ppp = Math.Round(upp.Value * packSize, 4);
            if (psp == null && usp != null) psp = Math.Round(usp.Value * packSize, 4);

            var finalUpp = upp ?? 0m;
            var finalUsp = usp ?? 0m;
            var finalPpp = ppp ?? 0m;
            var finalPsp = psp ?? 0m;
            var finalAvg = avg ?? finalUpp;
            var finalMargin = finalUsp > 0m ? Math.Round(((finalUsp - finalAvg) / finalUsp) * 100m, 2) : 0m;

            var narcRaw = (rowData.GetValueOrDefault("narcotic") ?? "").Trim().ToLowerInvariant();
            var narcotic = narcRaw is "1" or "y" or "yes" or "true" or "t" ? "yes" : "no";
            var barcode = NullIfEmpty(rowData.GetValueOrDefault("barcode"));

            var lc = code.ToLowerInvariant();
            if (codeToProduct.TryGetValue(lc, out var existing))
            {
                existing.Name = name;
                existing.Formulation = canonicalFormulation;
                existing.Description = NullIfEmpty(rowData.GetValueOrDefault("description"));
                existing.PackSize = packSize;
                if (qtyExplicit.HasValue)
                    existing.Quantity = Math.Max(0, qtyExplicit.Value);
                existing.PackPurchasePrice = finalPpp;
                existing.PackSalePrice = finalPsp;
                existing.UnitPurchasePrice = finalUpp;
                existing.UnitSalePrice = finalUsp;
                existing.WholeSalePackPrice = wspp;
                existing.WholeSaleUnitPrice = wsup;
                existing.AvgPrice = finalAvg;
                existing.Margin = finalMargin;
                existing.Narcotic = narcotic;
                existing.MaxDiscount = maxDisc;
                if (catId.HasValue) existing.CategoryId = catId;
                if (brandId.HasValue) existing.BrandId = brandId;
                if (supplierId.HasValue) existing.SupplierId = supplierId;
                existing.Rack = NullIfEmpty(rowData.GetValueOrDefault("rack"));
                if (!string.IsNullOrWhiteSpace(barcode))
                    existing.Barcode = barcode;
                existing.UpdatedAt = now;

                result.UpdatedCount++;
                result.Committed++;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(barcode))
                {
                    barcode = $"PRD{Random.Shared.NextInt64(0, 10_000_000_000L):D10}";
                }

                var newProd = new Product
                {
                    ProductCode = code,
                    Barcode = barcode,
                    Name = name,
                    Formulation = canonicalFormulation,
                    Description = NullIfEmpty(rowData.GetValueOrDefault("description")),
                    PackSize = packSize,
                    Quantity = Math.Max(0, qtyExplicit ?? 0),
                    PackPurchasePrice = finalPpp,
                    PackSalePrice = finalPsp,
                    UnitPurchasePrice = finalUpp,
                    UnitSalePrice = finalUsp,
                    WholeSalePackPrice = wspp,
                    WholeSaleUnitPrice = wsup,
                    AvgPrice = finalAvg,
                    Margin = finalMargin,
                    Narcotic = narcotic,
                    MaxDiscount = maxDisc,
                    CategoryId = catId,
                    BrandId = brandId,
                    SupplierId = supplierId,
                    StorageLocationId = defaultLocId,
                    Rack = NullIfEmpty(rowData.GetValueOrDefault("rack")),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Products.Add(newProd);
                codeToProduct[lc] = newProd;
                result.NewCount++;
                result.Committed++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        result.Succeeded = true;
        result.Message = $"Import complete: {result.Committed} committed ({result.NewCount} new, {result.UpdatedCount} updated).";
        return result;
    }

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    private static string? ValidateProductRequest(ProductUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProductCode))
            return "Product Code is required.";
        if (string.IsNullOrWhiteSpace(request.Barcode))
            return "Barcode is required.";
        if (string.IsNullOrWhiteSpace(request.Name))
            return "Product Name is required.";
        if (request.PackSize <= 0)
            return "Pack Size must be a positive integer (>= 1).";
        if (!request.CategoryId.HasValue || request.CategoryId.Value <= 0)
            return "Please select a Category.";
        if (!request.BrandId.HasValue || request.BrandId.Value <= 0)
            return "Please select a Brand.";
        if (!request.SupplierId.HasValue || request.SupplierId.Value <= 0)
            return "Please select a Supplier.";
        if (request.ExpiryWarningDays.HasValue && (request.ExpiryWarningDays.Value < 1 || request.ExpiryWarningDays.Value > 3650))
            return "Expiry Warning Days must be between 1 and 3650.";

        return null;
    }

    private static void NormalizeDerivedProductPrices(Product p)
    {
        var pack = Math.Max(1, p.PackSize);
        if (p.UnitPurchasePrice <= 0m && p.PackPurchasePrice > 0m)
            p.UnitPurchasePrice = Math.Round(p.PackPurchasePrice / pack, 2);
        else if (p.PackPurchasePrice <= 0m && p.UnitPurchasePrice > 0m)
            p.PackPurchasePrice = Math.Round(p.UnitPurchasePrice * pack, 2);

        if (p.UnitSalePrice <= 0m && p.PackSalePrice > 0m)
            p.UnitSalePrice = Math.Round(p.PackSalePrice / pack, 2);
        else if (p.PackSalePrice <= 0m && p.UnitSalePrice > 0m)
            p.PackSalePrice = Math.Round(p.UnitSalePrice * pack, 2);

        if (p.WholeSaleUnitPrice == null && p.WholeSalePackPrice is > 0m)
            p.WholeSaleUnitPrice = Math.Round(p.WholeSalePackPrice.Value / pack, 2);
        else if (p.WholeSalePackPrice == null && p.WholeSaleUnitPrice is > 0m)
            p.WholeSalePackPrice = Math.Round(p.WholeSaleUnitPrice.Value * pack, 2);

        if (p.AvgPrice <= 0m)
            p.AvgPrice = p.UnitPurchasePrice;

        if (!p.Margin.HasValue && p.UnitSalePrice > 0m)
            p.Margin = Math.Round(((p.UnitSalePrice - p.AvgPrice) / p.UnitSalePrice) * 100m, 2);

        if (!p.WholeSaleMargin.HasValue && p.WholeSaleUnitPrice is > 0m)
            p.WholeSaleMargin = Math.Round(((p.WholeSaleUnitPrice.Value - p.AvgPrice) / p.WholeSaleUnitPrice.Value) * 100m, 2);
    }

    private static async Task<string?> ResolveOrCreateFormulationInternalAsync(
        KarobarDbContext db,
        string? rawFormulation,
        CancellationToken cancellationToken)
    {
        var clean = NormalizeFormulationName(rawFormulation);
        if (string.IsNullOrWhiteSpace(clean))
            return null;

        var lower = clean.ToLowerInvariant();
        var existing = await db.Formulations
            .FirstOrDefaultAsync(f => f.NormalizedName.ToLower() == lower || f.Name.ToLower() == lower, cancellationToken);

        if (existing != null)
            return existing.Name;

        var now = DateTime.UtcNow;
        var entity = new Formulation
        {
            Name = clean,
            NormalizedName = lower,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Formulations.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Name;
    }

    private static async Task SyncPrimarySupplierMappingAsync(
        KarobarDbContext db,
        Product product,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!product.SupplierId.HasValue || product.SupplierId.Value <= 0)
            return;

        var supId = product.SupplierId.Value;
        var existing = await db.ProductSuppliers
            .FirstOrDefaultAsync(ps => ps.ProductId == product.Id && ps.SupplierId == supId, cancellationToken);

        if (existing != null)
        {
            existing.IsPreferred = true;
            existing.LastPurchasePrice = product.UnitPurchasePrice;
            existing.UpdatedAt = now;
        }
        else
        {
            db.ProductSuppliers.Add(new ProductSupplier
            {
                ProductId = product.Id,
                SupplierId = supId,
                LastPurchasePrice = product.UnitPurchasePrice,
                MinOrderQty = 1,
                IsPreferred = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string?> CheckProductDeleteBlockersAsync(
        KarobarDbContext db,
        Product product,
        CancellationToken cancellationToken)
    {
        if (product.Quantity > 0)
            return "Cannot delete: product has on-hand quantity.";

        if (await db.Batches.AnyAsync(b => b.ProductId == product.Id, cancellationToken))
            return "Cannot delete: product has batch records.";

        const int cap = 8;

        var piNos = await (from item in db.PurchaseInvoiceItems.AsNoTracking()
                           join inv in db.PurchaseInvoices.AsNoTracking() on item.PurchaseInvoiceId equals inv.Id
                           where item.ProductId == product.Id
                           select inv.PostedNumber)
            .Distinct()
            .ToListAsync(cancellationToken);

        var siNos = await (from item in db.SaleInvoiceItems.AsNoTracking()
                           join inv in db.SaleInvoices.AsNoTracking() on item.SaleInvoiceId equals inv.Id
                           where item.ProductId == product.Id
                           select inv.PostedNumber)
            .Distinct()
            .ToListAsync(cancellationToken);

        var prNos = await (from item in db.PurchaseReturnItems.AsNoTracking()
                           join ret in db.PurchaseReturns.AsNoTracking() on item.PurchaseReturnId equals ret.Id
                           where item.ProductId == product.Id
                           select ret.PostedNumber)
            .Distinct()
            .ToListAsync(cancellationToken);

        var srNos = await (from item in db.SaleReturnItems.AsNoTracking()
                           join ret in db.SaleReturns.AsNoTracking() on item.SaleReturnId equals ret.Id
                           where item.ProductId == product.Id
                           select ret.PostedNumber)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (piNos.Count == 0 && siNos.Count == 0 && prNos.Count == 0 && srNos.Count == 0)
            return null;

        static string? FormatRefGroup(string label, List<string> list)
        {
            if (list.Count == 0) return null;
            var shown = string.Join(", ", list.Take(cap));
            var more = Math.Max(0, list.Count - cap);
            return more > 0 ? $"{label}: {shown} +{more} more" : $"{label}: {shown}";
        }

        var parts = new[]
        {
            FormatRefGroup("Purchase Invoices", piNos),
            FormatRefGroup("Sale Invoices", siNos),
            FormatRefGroup("Purchase Returns", prNos),
            FormatRefGroup("Sale Returns", srNos)
        }.Where(x => x != null);

        return "Product cannot be deleted because it exists in the following documents: " + string.Join(" | ", parts);
    }

    private async Task CleanupAndRemoveProductAsync(
        KarobarDbContext db,
        Product product,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(product.Image))
        {
            DeleteStoredProductImage(product.Image);
        }

        var supLinks = await db.ProductSuppliers.Where(ps => ps.ProductId == product.Id).ToListAsync(cancellationToken);
        if (supLinks.Count > 0)
            db.ProductSuppliers.RemoveRange(supLinks);

        var regItems = await db.CustomerRegularItems.Where(cri => cri.ProductId == product.Id).ToListAsync(cancellationToken);
        if (regItems.Count > 0)
            db.CustomerRegularItems.RemoveRange(regItems);

        var wsPrices = await db.CustomerWholeSalePrices.Where(cwp => cwp.ProductId == product.Id).ToListAsync(cancellationToken);
        if (wsPrices.Count > 0)
            db.CustomerWholeSalePrices.RemoveRange(wsPrices);

        db.Products.Remove(product);
    }

    private string? ResolveFullImagePath(string? relativeOrFull)
    {
        if (string.IsNullOrWhiteSpace(relativeOrFull))
            return null;
        if (File.Exists(relativeOrFull))
            return relativeOrFull;

        var candidate = Path.Combine(_dbInitializer.StorageDirectoryPath, relativeOrFull.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(candidate) ? candidate : null;
    }

    private string CopyProductImageToStorage(string sourceFilePath)
    {
        var productsDir = Path.Combine(_dbInitializer.StorageDirectoryPath, "products");
        Directory.CreateDirectory(productsDir);
        var ext = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var fileName = $"prd_{Guid.NewGuid():N}{ext}";
        var destPath = Path.Combine(productsDir, fileName);
        File.Copy(sourceFilePath, destPath, overwrite: true);
        return $"products/{fileName}";
    }

    private void DeleteStoredProductImage(string relativePath)
    {
        try
        {
            var full = ResolveFullImagePath(relativePath);
            if (!string.IsNullOrWhiteSpace(full) && File.Exists(full))
                File.Delete(full);
        }
        catch
        {
            // Ignore file lock errors
        }
    }

    private static string NormalizeFormulationName(string? val)
    {
        if (string.IsNullOrWhiteSpace(val))
            return string.Empty;
        return Regex.Replace(val.Trim(), @"\s+", " ");
    }

    private static string ComputeInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "PR";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant();
        return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
    }

    private static string? NullIfEmpty(string? val) =>
        string.IsNullOrWhiteSpace(val) ? null : val.Trim();

    // CSV Helpers
    private static char ResolveCsvDelimiter(string sampleLine, string requested)
    {
        var norm = requested == "\\t" ? '\t' : (string.IsNullOrEmpty(requested) ? ',' : requested[0]);
        if (sampleLine.Contains(norm))
            return norm;

        char[] candidates = [',', ';', '\t', '|'];
        var best = norm;
        var bestCount = -1;
        foreach (var c in candidates)
        {
            var count = sampleLine.Count(ch => ch == c);
            if (count > bestCount)
            {
                bestCount = count;
                best = c;
            }
        }
        return best;
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (i == 0 && ch == '\uFEFF')
                continue;

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == delimiter && !inQuotes)
            {
                cells.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }
        cells.Add(sb.ToString().Trim());
        return cells;
    }

    private static Dictionary<string, int> MapHeaderColumns(List<string> headerCells)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lowerHeaders = headerCells.Select(h => h.Trim().ToLowerInvariant()).ToList();

        foreach (var (canonical, aliases) in CsvHeaderAliases)
        {
            foreach (var alias in aliases)
            {
                var idx = lowerHeaders.IndexOf(alias.ToLowerInvariant());
                if (idx >= 0)
                {
                    map[canonical] = idx;
                    break;
                }
            }
        }
        return map;
    }

    private static Dictionary<string, string?> ExtractCsvRowData(List<string> rowCells, Dictionary<string, int> colMap)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, idx) in colMap)
        {
            var val = idx < rowCells.Count ? rowCells[idx]?.Trim() : null;
            dict[key] = string.IsNullOrWhiteSpace(val) ? null : SanitizeFormulaInjection(val);
        }
        return dict;
    }

    private static List<string> ValidateCsvRow(
        Dictionary<string, string?> data,
        Dictionary<string, string> barcodeToCode,
        HashSet<string> seenCodes,
        HashSet<string> seenBarcodes,
        Dictionary<string, long> categoriesByName,
        Dictionary<string, long> brandsByName,
        Dictionary<string, long> suppliersByName,
        bool allowCreateRefs,
        bool strictDuplicateCheck)
    {
        var errors = new List<string>();
        var code = data.GetValueOrDefault("product_code")?.Trim() ?? "";
        var name = data.GetValueOrDefault("name")?.Trim() ?? "";
        var packRaw = data.GetValueOrDefault("pack_size")?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(code))
            errors.Add("product_code is required.");
        if (string.IsNullOrWhiteSpace(name))
            errors.Add("name is required.");
        if (!int.TryParse(packRaw, out var packSize) || packSize <= 0)
            errors.Add("pack_size must be a positive integer.");

        if (strictDuplicateCheck && !string.IsNullOrWhiteSpace(code) && seenCodes.Contains(code))
            errors.Add("Duplicate product_code within file.");

        var barcode = data.GetValueOrDefault("barcode")?.Trim();
        if (!string.IsNullOrWhiteSpace(barcode))
        {
            if (strictDuplicateCheck && seenBarcodes.Contains(barcode))
                errors.Add("Duplicate barcode within file.");
            if (barcodeToCode.TryGetValue(barcode.ToLowerInvariant(), out var ownerCode) &&
                !string.Equals(ownerCode, code, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Barcode already used by another product.");
            }
        }

        if (!allowCreateRefs)
        {
            var catName = data.GetValueOrDefault("category")?.Trim();
            if (!string.IsNullOrWhiteSpace(catName) && !categoriesByName.ContainsKey(catName.ToLowerInvariant()))
                errors.Add($"Category not found: {catName}");

            var brName = data.GetValueOrDefault("brand")?.Trim();
            if (!string.IsNullOrWhiteSpace(brName) && !brandsByName.ContainsKey(brName.ToLowerInvariant()))
                errors.Add($"Brand not found: {brName}");

            var supName = data.GetValueOrDefault("supplier")?.Trim();
            if (!string.IsNullOrWhiteSpace(supName) && !suppliersByName.ContainsKey(supName.ToLowerInvariant()))
                errors.Add($"Supplier not found: {supName}");
        }

        return errors;
    }

    private static async Task<long?> ResolveCsvRelationIdAsync(
        KarobarDbContext db,
        string? idRaw,
        string? nameRaw,
        Dictionary<string, long> nameCache,
        bool allowCreate,
        string entityType,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (long.TryParse(idRaw, out var parsedId) && parsedId > 0)
            return parsedId;

        var name = NullIfEmpty(nameRaw);
        if (name == null)
            return null;

        var key = name.ToLowerInvariant();
        if (nameCache.TryGetValue(key, out var cachedId))
            return cachedId;

        if (!allowCreate)
            return null;

        switch (entityType)
        {
            case "category":
                var cat = new Category { Name = name, CreatedAt = now, UpdatedAt = now };
                db.Categories.Add(cat);
                await db.SaveChangesAsync(cancellationToken);
                nameCache[key] = cat.Id;
                return cat.Id;
            case "brand":
                var br = new Brand { Name = name, CreatedAt = now, UpdatedAt = now };
                db.Brands.Add(br);
                await db.SaveChangesAsync(cancellationToken);
                nameCache[key] = br.Id;
                return br.Id;
            case "supplier":
                var sup = new Supplier { Name = name, CreatedAt = now, UpdatedAt = now };
                db.Suppliers.Add(sup);
                await db.SaveChangesAsync(cancellationToken);
                nameCache[key] = sup.Id;
                return sup.Id;
            default:
                return null;
        }
    }

    private static decimal? ParseNullableDecimal(string? val) =>
        decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? ParseNullableInt(string? val) =>
        int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string SanitizeFormulaInjection(string? val)
    {
        if (string.IsNullOrEmpty(val))
            return string.Empty;
        var s = val.Trim();
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' && !decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
        {
            return "'" + s;
        }
        return s;
    }

    private static string EscapeCsvCell(string val)
    {
        if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
        {
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }
        return val;
    }
}
