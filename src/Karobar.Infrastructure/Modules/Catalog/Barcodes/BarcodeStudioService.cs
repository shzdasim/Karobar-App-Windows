using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Karobar.Application.Interfaces;
using Karobar.Domain.Constants;
using Karobar.Domain.Entities;
using Karobar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Infrastructure.Services;

public sealed class BarcodeStudioService : IBarcodeStudioService
{
    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public BarcodeStudioService(IDbContextFactory<KarobarDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<BarcodeStudioBootstrapDto> GetStudioBootstrapAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var templates = await db.BarcodeTemplates
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

        if (templates.Count == 0)
        {
            var seeded = DomainSeedConstants.GetDefaultBarcodeTemplates(DateTime.UtcNow);
            db.BarcodeTemplates.AddRange(seeded);
            await db.SaveChangesAsync(cancellationToken);
            templates = await db.BarcodeTemplates
                .OrderByDescending(t => t.IsDefault)
                .ThenBy(t => t.Id)
                .ToListAsync(cancellationToken);
        }

        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        var suppliers = await db.Suppliers.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new BarcodeLookupItemDto { Id = s.Id, Name = s.Name })
            .ToListAsync(cancellationToken);

        var brands = await db.Brands.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new BarcodeLookupItemDto { Id = b.Id, Name = b.Name })
            .ToListAsync(cancellationToken);

        return new BarcodeStudioBootstrapDto
        {
            Templates = templates.Select(MapTemplateToDto).ToList(),
            StoreInfo = new BarcodeStoreInfoDto
            {
                StoreName = !string.IsNullOrWhiteSpace(setting?.StoreName) ? setting.StoreName : "Karobar Pharmacy",
                PhoneNumber = setting?.PhoneNumber ?? string.Empty,
                CurrencySymbol = !string.IsNullOrWhiteSpace(setting?.CurrencySymbol) ? setting.CurrencySymbol : "Rs.",
                CurrencyPosition = !string.IsNullOrWhiteSpace(setting?.CurrencyPosition) ? setting.CurrencyPosition : "before",
                CurrencyDecimalPlaces = setting?.CurrencyDecimalPlaces ?? 2
            },
            Suppliers = suppliers,
            Brands = brands
        };
    }

    public async Task<BarcodeTemplateDto> SaveTemplateAsync(
        BarcodeTemplateSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var name = string.IsNullOrWhiteSpace(request.Name) ? "Custom Label" : request.Name.Trim();
        var now = DateTime.UtcNow;

        if (request.IsDefault)
        {
            var currentDefaults = await db.BarcodeTemplates
                .Where(t => t.IsDefault)
                .ToListAsync(cancellationToken);
            foreach (var d in currentDefaults)
            {
                d.IsDefault = false;
                d.UpdatedAt = now;
            }
        }

        var entity = new BarcodeTemplate
        {
            CreatedAt = now
        };
        ApplySaveRequestToEntity(entity, request, name, now);

        db.BarcodeTemplates.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return MapTemplateToDto(entity);
    }

    public async Task<BarcodeTemplateDto> UpdateTemplateAsync(
        long id,
        BarcodeTemplateSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.BarcodeTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Barcode template #{id} was not found.");

        var name = string.IsNullOrWhiteSpace(request.Name) ? entity.Name : request.Name.Trim();
        var now = DateTime.UtcNow;

        if (request.IsDefault)
        {
            var otherDefaults = await db.BarcodeTemplates
                .Where(t => t.Id != id && t.IsDefault)
                .ToListAsync(cancellationToken);
            foreach (var d in otherDefaults)
            {
                d.IsDefault = false;
                d.UpdatedAt = now;
            }
        }

        ApplySaveRequestToEntity(entity, request, name, now);
        await db.SaveChangesAsync(cancellationToken);

        return MapTemplateToDto(entity);
    }

    public async Task<BarcodeTemplateDto> SetDefaultTemplateAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var all = await db.BarcodeTemplates.ToListAsync(cancellationToken);
        var target = all.FirstOrDefault(t => t.Id == id)
            ?? throw new InvalidOperationException($"Barcode template #{id} was not found.");

        var now = DateTime.UtcNow;
        foreach (var t in all)
        {
            var shouldBeDefault = t.Id == id;
            if (t.IsDefault != shouldBeDefault)
            {
                t.IsDefault = shouldBeDefault;
                t.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return MapTemplateToDto(target);
    }

    public async Task<(bool Deleted, string Message)> DeleteTemplateAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var totalCount = await db.BarcodeTemplates.CountAsync(cancellationToken);
        if (totalCount <= 1)
        {
            return (false, "Cannot delete the only remaining barcode template.");
        }

        var entity = await db.BarcodeTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity == null)
        {
            return (false, "Template not found.");
        }

        var wasDefault = entity.IsDefault;
        db.BarcodeTemplates.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);

        if (wasDefault)
        {
            var nextDefault = await db.BarcodeTemplates.OrderBy(t => t.Id).FirstOrDefaultAsync(cancellationToken);
            if (nextDefault != null)
            {
                nextDefault.IsDefault = true;
                nextDefault.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return (true, "Template deleted successfully.");
    }

    public async Task<List<BarcodeProductLookupDto>> SearchProductsAsync(
        string? search = null,
        long? categoryId = null,
        IReadOnlyList<long>? productIds = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Products.AsNoTracking().AsQueryable();

        if (productIds is { Count: > 0 })
        {
            var idSet = productIds.Distinct().ToList();
            query = query.Where(p => idSet.Contains(p.Id));
        }
        else if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{term}%") ||
                EF.Functions.Like(p.ProductCode, $"%{term}%") ||
                (p.Barcode != null && EF.Functions.Like(p.Barcode, $"%{term}%")) ||
                (p.Formulation != null && EF.Functions.Like(p.Formulation, $"%{term}%")) ||
                (p.Rack != null && EF.Functions.Like(p.Rack, $"%{term}%")));
        }
        else if (categoryId is > 0)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        var safeLimit = Math.Clamp(limit, 1, 250);
        var products = await query
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Take(safeLimit)
            .ToListAsync(cancellationToken);

        return await BuildProductLookupDtosAsync(db, products, cancellationToken);
    }

    public async Task<List<BarcodeImportDocumentDto>> GetImportDocumentsAsync(
        string docType,
        long? supplierId = null,
        long? brandId = null,
        string? search = null,
        long? specificDocumentId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var safeLimit = Math.Clamp(limit, 1, 100);
        var normalizedType = string.Equals(docType, "purchase_orders", StringComparison.OrdinalIgnoreCase)
            ? "purchase_orders"
            : "purchase_invoices";

        var supplierMap = await db.Suppliers.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        if (normalizedType == "purchase_orders")
        {
            var poQuery = db.PurchaseOrders.AsNoTracking().AsQueryable();

            if (specificDocumentId is > 0)
            {
                poQuery = poQuery.Where(po => po.Id == specificDocumentId.Value);
            }
            else
            {
                if (supplierId is > 0)
                {
                    poQuery = poQuery.Where(po => po.SupplierId == supplierId.Value);
                }

                if (brandId is > 0)
                {
                    var brandVal = brandId.Value;
                    var matchingPoIds = await (
                        from item in db.PurchaseOrderItems.AsNoTracking()
                        join prod in db.Products.AsNoTracking() on item.ProductId equals prod.Id
                        where prod.BrandId == brandVal
                        select item.PurchaseOrderId
                    ).Distinct().ToListAsync(cancellationToken);

                    poQuery = poQuery.Where(po => matchingPoIds.Contains(po.Id));
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim();
                    var matchingSupplierIds = supplierMap
                        .Where(kv => kv.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
                        .Select(kv => kv.Key)
                        .ToList();

                    poQuery = poQuery.Where(po =>
                        EF.Functions.Like(po.OrderNumber, $"%{term}%") ||
                        matchingSupplierIds.Contains(po.SupplierId));
                }
            }

            var orders = await poQuery
                .OrderByDescending(po => po.Id)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);

            if (orders.Count == 0)
                return [];

            var orderIds = orders.Select(o => o.Id).ToList();
            var orderItems = await db.PurchaseOrderItems.AsNoTracking()
                .Where(i => orderIds.Contains(i.PurchaseOrderId))
                .OrderBy(i => i.Position)
                .ThenBy(i => i.Id)
                .ToListAsync(cancellationToken);

            var productIds = orderItems.Select(i => i.ProductId).Distinct().ToList();
            var productEntities = await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken);
            var productLookupMap = (await BuildProductLookupDtosAsync(db, productEntities, cancellationToken))
                .ToDictionary(p => p.Id);

            var itemsByOrder = orderItems
                .GroupBy(i => i.PurchaseOrderId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var results = new List<BarcodeImportDocumentDto>(orders.Count);
            foreach (var po in orders)
            {
                var rawLines = itemsByOrder.TryGetValue(po.Id, out var list) ? list : [];
                var lineDtos = new List<BarcodeImportDocumentLineDto>();
                var totalUnits = 0;

                foreach (var line in rawLines)
                {
                    if (!productLookupMap.TryGetValue(line.ProductId, out var prodDto))
                        continue;

                    var packSize = line.PackSize > 0 ? line.PackSize : (prodDto.PackSize > 0 ? prodDto.PackSize : 1);
                    var orderedUnits = line.QuantityOrdered > 0
                        ? line.QuantityOrdered
                        : (line.QuantityReceived > 0 ? line.QuantityReceived : 1);
                    var orderedPacks = Math.Max(1, (int)Math.Ceiling((double)orderedUnits / packSize));
                    totalUnits += orderedUnits;

                    lineDtos.Add(new BarcodeImportDocumentLineDto
                    {
                        LineId = line.Id,
                        Product = prodDto,
                        PackSize = packSize,
                        OrderedUnits = orderedUnits,
                        OrderedPacks = orderedPacks,
                        GrnBatchNumber = string.Empty,
                        GrnExpiryDate = null
                    });
                }

                var refNo = !string.IsNullOrWhiteSpace(po.OrderNumber) ? po.OrderNumber : $"PO-{po.Id}";
                var supplierName = supplierMap.TryGetValue(po.SupplierId, out var sName) ? sName : "Unknown Supplier";

                results.Add(new BarcodeImportDocumentDto
                {
                    Id = po.Id,
                    DocType = "purchase_orders",
                    ReferenceNumber = refNo,
                    SubReferenceNumber = string.Empty,
                    DocumentDate = po.OrderDate,
                    SupplierId = po.SupplierId,
                    SupplierName = supplierName,
                    ItemsCount = lineDtos.Count,
                    TotalUnits = totalUnits,
                    Items = lineDtos
                });
            }

            return results;
        }
        else
        {
            var piQuery = db.PurchaseInvoices.AsNoTracking()
                .Where(pi => pi.Status == null || pi.Status != "reversed");

            if (specificDocumentId is > 0)
            {
                piQuery = piQuery.Where(pi => pi.Id == specificDocumentId.Value);
            }
            else
            {
                if (supplierId is > 0)
                {
                    piQuery = piQuery.Where(pi => pi.SupplierId == supplierId.Value);
                }

                if (brandId is > 0)
                {
                    var brandVal = brandId.Value;
                    var matchingPiIds = await (
                        from item in db.PurchaseInvoiceItems.AsNoTracking()
                        join prod in db.Products.AsNoTracking() on item.ProductId equals prod.Id
                        where prod.BrandId == brandVal
                        select item.PurchaseInvoiceId
                    ).Distinct().ToListAsync(cancellationToken);

                    piQuery = piQuery.Where(pi => matchingPiIds.Contains(pi.Id));
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim();
                    var matchingSupplierIds = supplierMap
                        .Where(kv => kv.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
                        .Select(kv => kv.Key)
                        .ToList();

                    piQuery = piQuery.Where(pi =>
                        EF.Functions.Like(pi.PostedNumber, $"%{term}%") ||
                        (pi.InvoiceNumber != null && EF.Functions.Like(pi.InvoiceNumber, $"%{term}%")) ||
                        matchingSupplierIds.Contains(pi.SupplierId));
                }
            }

            var invoices = await piQuery
                .OrderByDescending(pi => pi.Id)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);

            if (invoices.Count == 0)
                return [];

            var invoiceIds = invoices.Select(i => i.Id).ToList();
            var invoiceItems = await db.PurchaseInvoiceItems.AsNoTracking()
                .Where(i => invoiceIds.Contains(i.PurchaseInvoiceId))
                .OrderBy(i => i.LineOrder)
                .ThenBy(i => i.Id)
                .ToListAsync(cancellationToken);

            var productIds = invoiceItems.Select(i => i.ProductId).Distinct().ToList();
            var productEntities = await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken);
            var productLookupMap = (await BuildProductLookupDtosAsync(db, productEntities, cancellationToken))
                .ToDictionary(p => p.Id);

            var itemsByInvoice = invoiceItems
                .GroupBy(i => i.PurchaseInvoiceId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var results = new List<BarcodeImportDocumentDto>(invoices.Count);
            foreach (var pi in invoices)
            {
                var rawLines = itemsByInvoice.TryGetValue(pi.Id, out var list) ? list : [];
                var lineDtos = new List<BarcodeImportDocumentLineDto>();
                var totalUnits = 0;

                foreach (var line in rawLines)
                {
                    if (!productLookupMap.TryGetValue(line.ProductId, out var prodDto))
                        continue;

                    var packSize = line.PackSize > 0 ? line.PackSize : (prodDto.PackSize > 0 ? prodDto.PackSize : 1);
                    var computedFromPacks = (int)Math.Round((line.PackQuantity + line.PackBonus) * packSize) + line.UnitQuantity + line.UnitBonus;
                    var orderedUnits = line.Quantity > 0
                        ? line.Quantity
                        : (computedFromPacks > 0 ? computedFromPacks : 1);
                    var orderedPacks = line.PackQuantity > 0
                        ? (int)Math.Ceiling(line.PackQuantity + line.PackBonus)
                        : Math.Max(1, (int)Math.Ceiling((double)orderedUnits / packSize));

                    totalUnits += orderedUnits;

                    lineDtos.Add(new BarcodeImportDocumentLineDto
                    {
                        LineId = line.Id,
                        Product = prodDto,
                        PackSize = packSize,
                        OrderedUnits = orderedUnits,
                        OrderedPacks = orderedPacks,
                        GrnBatchNumber = line.Batch?.Trim() ?? string.Empty,
                        GrnExpiryDate = line.Expiry
                    });
                }

                var refNo = !string.IsNullOrWhiteSpace(pi.PostedNumber) ? pi.PostedNumber : $"GRN-{pi.Id}";
                var supplierName = supplierMap.TryGetValue(pi.SupplierId, out var sName) ? sName : "Unknown Supplier";

                results.Add(new BarcodeImportDocumentDto
                {
                    Id = pi.Id,
                    DocType = "purchase_invoices",
                    ReferenceNumber = refNo,
                    SubReferenceNumber = pi.InvoiceNumber?.Trim() ?? string.Empty,
                    DocumentDate = pi.PostedDate != default ? pi.PostedDate : (pi.InvoiceDate ?? DateTime.Today),
                    SupplierId = pi.SupplierId,
                    SupplierName = supplierName,
                    ItemsCount = lineDtos.Count,
                    TotalUnits = totalUnits,
                    Items = lineDtos
                });
            }

            return results;
        }
    }

    public async Task<BarcodeScanLookupResultDto> ScanProductAsync(
        string? codeOrBarcode,
        long? productId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var code = codeOrBarcode?.Trim() ?? string.Empty;
        if ((productId == null || productId <= 0) && string.IsNullOrEmpty(code))
        {
            return new BarcodeScanLookupResultDto
            {
                Found = false,
                Message = "Please provide a barcode, product code, or product ID."
            };
        }

        Product? product = null;
        var isWeightBarcode = false;
        var parsedQuantity = 1m;
        int? parsedWeightGrams = null;

        if (productId is > 0)
        {
            product = await db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productId.Value, cancellationToken);
        }
        else
        {
            product = await db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Barcode == code || p.ProductCode == code, cancellationToken);

            // Weight-Embedded EAN-13 Barcode Parsing (Prefixes 20–29: 2P IIIII WWWWW C)
            if (product == null && code.Length == 13)
            {
                var match = Regex.Match(code, @"^(2[0-9])(\d{5})(\d{5})(\d)$");
                if (match.Success)
                {
                    var prefix = match.Groups[1].Value;
                    var plu = match.Groups[2].Value;
                    var weightRaw = int.Parse(match.Groups[3].Value);
                    var pluStripped = plu.TrimStart('0');
                    if (string.IsNullOrEmpty(pluStripped))
                        pluStripped = "0";

                    var candidates = new[]
                    {
                        plu,
                        pluStripped,
                        $"PRD-{pluStripped.PadLeft(4, '0')}",
                        $"PRD-{plu}",
                        $"{prefix}{plu}"
                    }.Distinct().ToList();

                    product = await db.Products.AsNoTracking()
                        .FirstOrDefaultAsync(
                            p => (p.Barcode != null && candidates.Contains(p.Barcode)) ||
                                 candidates.Contains(p.ProductCode),
                            cancellationToken);

                    if (product != null)
                    {
                        isWeightBarcode = true;
                        parsedWeightGrams = weightRaw;
                        parsedQuantity = weightRaw > 0 ? Math.Round(weightRaw / 1000m, 3) : 1m;
                    }
                }
            }
        }

        if (product == null)
        {
            return new BarcodeScanLookupResultDto
            {
                Found = false,
                Message = productId is > 0
                    ? $"Product with ID '{productId}' not found."
                    : $"Product with barcode '{code}' not found."
            };
        }

        var dtoList = await BuildProductLookupDtosAsync(db, [product], cancellationToken);
        var productDto = dtoList[0];

        var selectedBatch = productDto.Batches.FirstOrDefault(b => b.IsFefo)
            ?? (productDto.UnassignedQuantity > 0 || productDto.Batches.Count == 0
                ? new BarcodeBatchOptionDto
                {
                    Id = null,
                    BatchNumber = string.Empty,
                    ExpiryDate = null,
                    Quantity = productDto.UnassignedQuantity,
                    IsUnbatched = true,
                    UnitSalePrice = productDto.UnitSalePrice,
                    WholeSaleUnitPrice = productDto.WholeSaleUnitPrice
                }
                : productDto.Batches[0]);

        return new BarcodeScanLookupResultDto
        {
            Found = true,
            Message = "Product found.",
            IsWeightBarcode = isWeightBarcode,
            ParsedQuantity = parsedQuantity,
            WeightGrams = parsedWeightGrams,
            Product = productDto,
            SelectedBatch = selectedBatch
        };
    }

    private static async Task<List<BarcodeProductLookupDto>> BuildProductLookupDtosAsync(
        KarobarDbContext db,
        List<Product> products,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0)
            return [];

        var productIds = products.Select(p => p.Id).ToList();
        var brandIds = products.Where(p => p.BrandId.HasValue).Select(p => p.BrandId!.Value).Distinct().ToList();
        var supplierIds = products.Where(p => p.SupplierId.HasValue).Select(p => p.SupplierId!.Value).Distinct().ToList();

        var brandMap = brandIds.Count > 0
            ? await db.Brands.AsNoTracking()
                .Where(b => brandIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken)
            : new Dictionary<long, string>();

        var supplierMap = supplierIds.Count > 0
            ? await db.Suppliers.AsNoTracking()
                .Where(s => supplierIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken)
            : new Dictionary<long, string>();

        var allBatches = await db.Batches.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId))
            .ToListAsync(cancellationToken);

        var batchesByProduct = allBatches
            .GroupBy(b => b.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(b => b.ExpiryDate == null ? 1 : 0)
                      .ThenBy(b => b.ExpiryDate)
                      .ThenBy(b => b.Id)
                      .ToList());

        var today = DateTime.Today;
        var results = new List<BarcodeProductLookupDto>(products.Count);

        foreach (var p in products)
        {
            var rawBatches = batchesByProduct.TryGetValue(p.Id, out var list) ? list : [];

            var fefoBatch = rawBatches.FirstOrDefault(b =>
                b.Quantity > 0 && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value.Date >= today));

            var batchDtos = new List<BarcodeBatchOptionDto>(rawBatches.Count);
            var totalBatchedQty = 0;

            foreach (var b in rawBatches)
            {
                totalBatchedQty += Math.Max(0, b.Quantity);
                var isExpired = b.ExpiryDate.HasValue && b.ExpiryDate.Value.Date < today;
                var isFefo = fefoBatch != null && b.Id == fefoBatch.Id;

                var unitSalePrice = b.UnitSalePrice is > 0m ? b.UnitSalePrice.Value : p.UnitSalePrice;
                var wsUnitPrice = b.WholeSaleUnitPrice is > 0m
                    ? b.WholeSaleUnitPrice.Value
                    : (p.WholeSaleUnitPrice ?? p.UnitSalePrice);

                batchDtos.Add(new BarcodeBatchOptionDto
                {
                    Id = b.Id,
                    BatchNumber = b.BatchNumber ?? string.Empty,
                    ExpiryDate = b.ExpiryDate,
                    Quantity = b.Quantity,
                    IsExpired = isExpired,
                    IsFefo = isFefo,
                    IsUnbatched = false,
                    UnitSalePrice = unitSalePrice,
                    WholeSaleUnitPrice = wsUnitPrice
                });
            }

            var unassignedQty = Math.Max(0, p.Quantity - totalBatchedQty);
            var effectiveBarcode = !string.IsNullOrWhiteSpace(p.Barcode)
                ? p.Barcode.Trim()
                : (!string.IsNullOrWhiteSpace(p.ProductCode) ? p.ProductCode.Trim() : $"PRD-{p.Id:D4}");

            results.Add(new BarcodeProductLookupDto
            {
                Id = p.Id,
                ProductCode = p.ProductCode ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Formulation = p.Formulation ?? string.Empty,
                Rack = p.Rack ?? string.Empty,
                Barcode = effectiveBarcode,
                RawBarcode = p.Barcode ?? string.Empty,
                UnitSalePrice = p.UnitSalePrice,
                WholeSaleUnitPrice = p.WholeSaleUnitPrice ?? p.UnitSalePrice,
                Quantity = p.Quantity,
                PackSize = p.PackSize > 0 ? p.PackSize : 1,
                CategoryId = p.CategoryId,
                BrandId = p.BrandId,
                BrandName = p.BrandId.HasValue && brandMap.TryGetValue(p.BrandId.Value, out var bName) ? bName : string.Empty,
                SupplierId = p.SupplierId,
                SupplierName = p.SupplierId.HasValue && supplierMap.TryGetValue(p.SupplierId.Value, out var sName) ? sName : string.Empty,
                UnassignedQuantity = unassignedQty,
                Batches = batchDtos
            });
        }

        return results;
    }

    private static void ApplySaveRequestToEntity(
        BarcodeTemplate entity,
        BarcodeTemplateSaveRequest request,
        string name,
        DateTime now)
    {
        var opts = request.Options ?? new BarcodeLayoutOptionsDto();
        var paperType = string.Equals(request.PaperType, "sheet", StringComparison.OrdinalIgnoreCase)
            ? "sheet"
            : "roll";
        var orientation = string.Equals(request.Orientation, "portrait", StringComparison.OrdinalIgnoreCase)
            ? "portrait"
            : "landscape";

        entity.Name = name;
        entity.Description = request.Description;
        entity.LayoutType = paperType;
        entity.LabelWidthMm = Math.Clamp(request.WidthMm, 15m, 300m);
        entity.LabelHeightMm = Math.Clamp(request.HeightMm, 10m, 300m);
        entity.Orientation = orientation;
        entity.Columns = paperType == "sheet" ? Math.Clamp(request.Columns, 1, 10) : 1;
        entity.Rows = paperType == "sheet" ? Math.Clamp(request.Rows, 1, 50) : 1;
        entity.GapXMm = Math.Clamp(request.GapXMm, 0m, 50m);
        entity.GapYMm = Math.Clamp(request.GapYMm, 0m, 50m);
        entity.MarginTopMm = Math.Clamp(request.MarginTopMm, 0m, 50m);
        entity.MarginLeftMm = Math.Clamp(request.MarginLeftMm, 0m, 50m);
        entity.MarginRightMm = Math.Clamp(request.MarginRightMm, 0m, 50m);
        entity.MarginBottomMm = Math.Clamp(request.MarginBottomMm, 0m, 50m);
        entity.BarcodeFormat = "CODE128";
        entity.BarcodeHeightMm = Math.Round((decimal)Math.Clamp(opts.BarcodeHeight, 16, 50) / 3.0m, 1);
        entity.ShowStoreName = opts.ShowStoreName;
        entity.ShowProductName = opts.ShowProductName;
        entity.ShowPrice = opts.ShowPrice;
        entity.ShowBatch = opts.ShowBatch;
        entity.ShowExpiry = opts.ShowExpiry;
        entity.ShowCode = opts.ShowBarcodeText;
        entity.LayoutOptions = JsonSerializer.Serialize(opts, JsonOptions);
        entity.IsDefault = request.IsDefault;
        entity.UpdatedAt = now;
    }

    private static BarcodeTemplateDto MapTemplateToDto(BarcodeTemplate entity)
    {
        BarcodeLayoutOptionsDto? parsedOptions = null;
        if (!string.IsNullOrWhiteSpace(entity.LayoutOptions))
        {
            try
            {
                parsedOptions = JsonSerializer.Deserialize<BarcodeLayoutOptionsDto>(entity.LayoutOptions, JsonOptions);
            }
            catch
            {
                parsedOptions = null;
            }
        }

        if (parsedOptions == null)
        {
            var approxPxHeight = (int)Math.Clamp(Math.Round(entity.BarcodeHeightMm * 2.8m), 16m, 50m);
            parsedOptions = new BarcodeLayoutOptionsDto
            {
                ShowStoreName = entity.ShowStoreName,
                StoreNameOverride = string.Empty,
                ShowProductName = entity.ShowProductName,
                NameLines = 2,
                ShowProductCode = false,
                ShowBarcode = true,
                BarcodeHeight = approxPxHeight > 0 ? approxPxHeight : 28,
                BarWidth = entity.LabelWidthMm <= 38m ? 1.2 : 1.3,
                ShowBarcodeText = entity.ShowCode,
                ShowPrice = entity.ShowPrice,
                PriceType = "retail",
                PricePrefix = "MRP:",
                ShowBatch = entity.ShowBatch,
                ShowExpiry = entity.ShowExpiry,
                ExpiryFormat = "MM/YY",
                CustomFooter = string.Empty,
                FontScale = entity.LabelHeightMm <= 21m ? 9 : 10,
                TextAlign = "center"
            };
        }

        var paperType = string.Equals(entity.LayoutType, "sheet", StringComparison.OrdinalIgnoreCase)
            ? "sheet"
            : "roll";
        var orientation = string.Equals(entity.Orientation, "portrait", StringComparison.OrdinalIgnoreCase)
            ? "portrait"
            : "landscape";

        return new BarcodeTemplateDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            PaperType = paperType,
            WidthMm = entity.LabelWidthMm > 0 ? entity.LabelWidthMm : 50m,
            HeightMm = entity.LabelHeightMm > 0 ? entity.LabelHeightMm : 25m,
            Orientation = orientation,
            Columns = entity.Columns > 0 ? entity.Columns : 1,
            Rows = entity.Rows > 0 ? entity.Rows : 1,
            GapXMm = entity.GapXMm,
            GapYMm = entity.GapYMm,
            MarginTopMm = entity.MarginTopMm,
            MarginLeftMm = entity.MarginLeftMm,
            MarginRightMm = entity.MarginRightMm,
            MarginBottomMm = entity.MarginBottomMm,
            IsDefault = entity.IsDefault,
            Options = parsedOptions
        };
    }
}
