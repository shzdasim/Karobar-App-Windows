namespace Karobar.Domain.Formulas;

public sealed class PurchaseLineCalcModel
{
    public int PackSize { get; set; } = 1;
    public decimal PackQuantity { get; set; }
    public int UnitQuantity { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal PackSalePrice { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal WholeSalePackPrice { get; set; }
    public decimal WholeSaleUnitPrice { get; set; }
    public decimal PackBonus { get; set; }
    public int UnitBonus { get; set; }
    public decimal ItemDiscountPercentage { get; set; }
    public decimal WholeSaleDiscountPercentage { get; set; }
    public int Quantity { get; set; }
    public decimal SubTotal { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal EffectiveAvgPrice4 { get; set; }
    public decimal? Margin { get; set; }
    public decimal? WholeSaleMargin { get; set; }
}

public sealed class PurchaseFooterCalcModel
{
    public decimal RowsTotal { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

public sealed class SaleLineCalcModel
{
    public int PackSize { get; set; } = 1;
    public decimal PackQuantity { get; set; }
    public int UnitQuantity { get; set; }
    public int Quantity { get; set; }
    public decimal PackBonus { get; set; }
    public int UnitBonus { get; set; }
    public int Bonus { get; set; }
    public decimal PackPrice { get; set; }
    public decimal Price { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal ItemDiscountPercentage { get; set; }
    public decimal? Margin { get; set; }
    public decimal SubTotal { get; set; }
}

public sealed class SaleFooterCalcModel
{
    public string InvoiceType { get; set; } = "debit";
    public decimal GrossAmount { get; set; }
    public decimal ItemDiscount { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal TotalReceive { get; set; }
    public decimal RemainThisBill { get; set; }
    public decimal PreviousBalance { get; set; }
    public decimal TotalBalance { get; set; }
}

public sealed class PurchaseReturnLineCalcModel
{
    public int PackSize { get; set; } = 1;
    public decimal PackPurchasedQuantity { get; set; }
    public decimal ReturnPackQuantity { get; set; }
    public int ReturnUnitQuantity { get; set; }
    public decimal PackPurchasePrice { get; set; }
    public decimal UnitPurchasePrice { get; set; }
    public decimal ItemDiscountPercentage { get; set; }
    public decimal SubTotal { get; set; }
}

public sealed class PurchaseReturnFooterCalcModel
{
    public decimal GrossTotal { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
}

/// <summary>
/// 100% Penny-for-Penny Port of app/Services/InvoiceCalculationService.php and resources/js/Formula/*.js
/// </summary>
public static class InvoiceCalculators
{
    public static decimal R2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    public static decimal R4(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
    public static decimal R6(decimal value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    public static PurchaseLineCalcModel RecalcPurchaseItem(
        PurchaseLineCalcModel item,
        string? changedField = null,
        decimal? previousPackSalePrice = null)
    {
        var packSize = item.PackSize;
        var packQty = item.PackQuantity;
        var unitQty = (decimal)item.UnitQuantity;
        var packPurchase = item.PackPurchasePrice;
        var unitPurchase = item.UnitPurchasePrice;
        var packSale = item.PackSalePrice;
        var unitSale = item.UnitSalePrice;
        var wholeSalePack = item.WholeSalePackPrice;
        var wholeSaleUnit = item.WholeSaleUnitPrice;
        var packBonus = item.PackBonus;
        var unitBonus = (decimal)item.UnitBonus;
        var discountPct = item.ItemDiscountPercentage;

        // Retail -> Wholesale Smart Sync (PurchaseInvoiceForm.jsx:629-659)
        var syncWholesaleFromRetailPack = changedField == "pack_sale_price" &&
            (wholeSalePack == 0m || (previousPackSalePrice.HasValue && R2(wholeSalePack) == R2(previousPackSalePrice.Value)));

        if (syncWholesaleFromRetailPack)
        {
            wholeSalePack = packSale;
            wholeSaleUnit = 0m;
        }

        if (wholeSalePack == 0m && packSale > 0m) wholeSalePack = packSale;
        if (wholeSaleUnit == 0m && unitSale > 0m) wholeSaleUnit = unitSale;

        if (packSize > 0)
        {
            if (changedField == "pack_quantity")
                unitQty = Math.Round(packQty * packSize, 0, MidpointRounding.AwayFromZero);
            else if (changedField == "unit_quantity")
                packQty = unitQty / packSize;
            else if (changedField == "pack_size")
            {
                if (packQty > 0m && unitQty == 0m)
                    unitQty = Math.Round(packQty * packSize, 0, MidpointRounding.AwayFromZero);
                else if (unitQty > 0m && packQty == 0m)
                    packQty = unitQty / packSize;
            }

            if (changedField == "pack_purchase_price")
                unitPurchase = packPurchase / packSize;
            else if (changedField == "unit_purchase_price")
                packPurchase = unitPurchase * packSize;
            else if (changedField == "pack_size")
            {
                if (packPurchase > 0m && unitPurchase == 0m)
                    unitPurchase = packPurchase / packSize;
                else if (unitPurchase > 0m && packPurchase == 0m)
                    packPurchase = unitPurchase * packSize;
            }

            if (changedField == "pack_sale_price")
                unitSale = packSale / packSize;
            else if (changedField == "unit_sale_price")
                packSale = unitSale * packSize;
            else if (changedField == "pack_size")
            {
                if (packSale > 0m && unitSale == 0m)
                    unitSale = packSale / packSize;
                else if (unitSale > 0m && packSale == 0m)
                    packSale = unitSale * packSize;
            }

            if (changedField == "whole_sale_pack_price" || syncWholesaleFromRetailPack)
                wholeSaleUnit = wholeSalePack / packSize;
            else if (changedField == "whole_sale_unit_price")
                wholeSalePack = wholeSaleUnit * packSize;
            else if (changedField == "pack_size" || (wholeSalePack > 0m && wholeSaleUnit == 0m))
            {
                if (wholeSalePack > 0m && wholeSaleUnit == 0m)
                    wholeSaleUnit = wholeSalePack / packSize;
                else if (wholeSaleUnit > 0m && wholeSalePack == 0m)
                    wholeSalePack = wholeSaleUnit * packSize;
            }

            if (changedField == "pack_bonus")
                unitBonus = Math.Round(packBonus * packSize, 0, MidpointRounding.AwayFromZero);
            else if (changedField == "unit_bonus")
                packBonus = unitBonus / packSize;
            else if (changedField == "pack_size")
            {
                if (packBonus > 0m && unitBonus == 0m)
                    unitBonus = Math.Round(packBonus * packSize, 0, MidpointRounding.AwayFromZero);
                else if (unitBonus > 0m && packBonus == 0m)
                    packBonus = unitBonus / packSize;
            }
        }
        else
        {
            unitQty = Math.Round(unitQty, 0, MidpointRounding.AwayFromZero);
            unitBonus = Math.Round(unitBonus, 0, MidpointRounding.AwayFromZero);
        }

        // Re-check wholesale fallback after unitSale may have been derived from packSale
        if (wholeSalePack == 0m && packSale > 0m) wholeSalePack = packSale;
        if (wholeSaleUnit == 0m && unitSale > 0m) wholeSaleUnit = unitSale;

        var paidUnits = (int)Math.Round(unitQty, 0, MidpointRounding.AwayFromZero);
        var bonusUnits = (int)Math.Round(unitBonus, 0, MidpointRounding.AwayFromZero);
        var totalUnits = paidUnits + bonusUnits;

        var subTotal = paidUnits * unitPurchase;
        var discountAmount = (subTotal * discountPct) / 100m;
        var netTotal = subTotal - discountAmount;

        var avgPrice = totalUnits > 0 ? netTotal / totalUnits : 0m;
        var costForMargin = avgPrice > 0m ? avgPrice : unitPurchase;

        decimal? margin = unitSale > 0m
            ? R2(((unitSale - costForMargin) / unitSale) * 100m)
            : null;

        decimal? wholeSaleMargin = wholeSaleUnit > 0m
            ? R2(((wholeSaleUnit - costForMargin) / wholeSaleUnit) * 100m)
            : null;

        item.PackQuantity = R6(packQty);
        item.UnitQuantity = paidUnits;
        item.Quantity = totalUnits;
        item.PackBonus = R6(packBonus);
        item.UnitBonus = bonusUnits;
        item.PackPurchasePrice = R6(packPurchase);
        item.UnitPurchasePrice = R6(unitPurchase);
        item.PackSalePrice = R6(packSale);
        item.UnitSalePrice = R6(unitSale);
        item.WholeSalePackPrice = R6(wholeSalePack);
        item.WholeSaleUnitPrice = R6(wholeSaleUnit);
        item.SubTotal = R2(netTotal);
        item.AvgPrice = R2(avgPrice);
        item.EffectiveAvgPrice4 = R4(avgPrice);
        item.Margin = margin;
        item.WholeSaleMargin = wholeSaleMargin;

        return item;
    }

    public static PurchaseFooterCalcModel RecalcPurchaseFooter(
        IEnumerable<PurchaseLineCalcModel> items,
        PurchaseFooterCalcModel footer,
        string? changedField = null)
    {
        var rowsTotal = R2(items.Sum(i => i.SubTotal));
        var discountPct = footer.DiscountPercentage;
        var discountAmt = footer.DiscountAmount;

        if (changedField == "discount_percentage")
            discountAmt = (rowsTotal * discountPct) / 100m;
        else if (changedField == "discount_amount")
            discountPct = rowsTotal > 0m ? (discountAmt / rowsTotal) * 100m : 0m;
        else if (changedField == null && discountAmt > 0m)
            discountPct = rowsTotal > 0m ? (discountAmt / rowsTotal) * 100m : 0m;
        else if (changedField == null && discountPct > 0m)
            discountAmt = (rowsTotal * discountPct) / 100m;

        if (discountAmt > rowsTotal)
        {
            discountAmt = rowsTotal;
            discountPct = rowsTotal > 0m ? 100m : 0m;
        }
        if (discountAmt < 0m)
        {
            discountAmt = 0m;
            discountPct = 0m;
        }

        var afterDiscount = rowsTotal - discountAmt;
        var taxPct = footer.TaxPercentage;
        var taxAmt = footer.TaxAmount;

        if (changedField == "tax_percentage")
            taxAmt = (afterDiscount * taxPct) / 100m;
        else if (changedField == "tax_amount")
            taxPct = afterDiscount > 0m ? (taxAmt / afterDiscount) * 100m : 0m;
        else if (changedField == null && taxAmt > 0m)
            taxPct = afterDiscount > 0m ? (taxAmt / afterDiscount) * 100m : 0m;
        else if (changedField == null && taxPct > 0m)
            taxAmt = (afterDiscount * taxPct) / 100m;

        if (taxAmt < 0m) taxAmt = 0m;

        footer.RowsTotal = rowsTotal;
        footer.DiscountPercentage = R2(discountPct);
        footer.DiscountAmount = R2(discountAmt);
        footer.TaxPercentage = R2(taxPct);
        footer.TaxAmount = R2(taxAmt);
        footer.TotalAmount = R2(afterDiscount + taxAmt);
        return footer;
    }

    /// <summary>
    /// Exact port of Product::applyPurchaseFromItem (current-on-hand moving weighted average rounded to 2 decimals).
    /// </summary>
    public static decimal ApplyPurchaseWeightedAverageCost(
        int currentStockUnits,
        decimal currentAvgPrice,
        int incomingTotalUnits,
        decimal incomingEffectiveUnitAvg)
    {
        if (incomingTotalUnits <= 0)
            return currentAvgPrice;

        var totalUnits = currentStockUnits + incomingTotalUnits;
        if (currentStockUnits < 0)
        {
            return totalUnits <= 0 ? 0m : R2(Math.Max(0m, incomingEffectiveUnitAvg));
        }

        if (totalUnits <= 0)
            return 0m;

        var weightedAvg = ((currentStockUnits * currentAvgPrice) + (incomingTotalUnits * incomingEffectiveUnitAvg)) / totalUnits;
        return R2(Math.Max(0m, weightedAvg));
    }

    /// <summary>
    /// Exact port of Product::revertPurchaseFromItem (inverse moving weighted average when reversing/reducing stock).
    /// </summary>
    public static decimal RevertPurchaseWeightedAverageCost(
        int currentStockUnits,
        decimal currentAvgPrice,
        int removedTotalUnits,
        decimal removedEffectiveUnitAvg)
    {
        if (removedTotalUnits <= 0)
            return currentAvgPrice;

        var afterUnits = currentStockUnits - removedTotalUnits;
        if (afterUnits <= 0)
            return 0m;

        var remainingValue = Math.Max(0m, (currentStockUnits * currentAvgPrice) - (removedTotalUnits * removedEffectiveUnitAvg));
        return R2(remainingValue / afterUnits);
    }

    /// <summary>
    /// Exact port of Product::refreshMargins (Gross Margin % on selling price against product's moving avg_price).
    /// </summary>
    public static decimal ComputeProductMargin(decimal unitSalePrice, decimal productAvgPrice)
    {
        if (unitSalePrice <= 0m || productAvgPrice <= 0m)
            return 0m;

        return R2(((unitSalePrice - productAvgPrice) / unitSalePrice) * 100m);
    }

    /// <summary>
    /// Computes moving weighted-average cost (Product.AvgPrice) when receiving purchase stock.
    /// </summary>
    public static decimal ComputeMovingWeightedAverageCost(
        int oldStockUnits,
        decimal oldAvgPrice,
        int incomingTotalUnits,
        decimal incomingNetSubTotal)
    {
        if (incomingTotalUnits <= 0)
            return oldAvgPrice;

        var incomingUnitAvg = incomingNetSubTotal / incomingTotalUnits;
        if (oldStockUnits <= 0 || oldAvgPrice <= 0m)
            return R6(incomingUnitAvg);

        var combinedValue = (oldStockUnits * oldAvgPrice) + incomingNetSubTotal;
        var combinedUnits = oldStockUnits + incomingTotalUnits;
        return combinedUnits > 0 ? R6(combinedValue / combinedUnits) : R6(incomingUnitAvg);
    }

    public static SaleLineCalcModel RecalcRetailSaleItem(SaleLineCalcModel item)
    {
        var qty = item.Quantity;
        var price = item.Price;
        var discPct = item.ItemDiscountPercentage; // May be negative for markup

        var gross = qty * price;
        var itemDisc = (gross * discPct) / 100m;
        item.SubTotal = R2(gross - itemDisc);
        return item;
    }

    public static SaleLineCalcModel RecalcWholesaleSaleItem(SaleLineCalcModel item, string? changedField = null)
    {
        var packSize = Math.Max(1, item.PackSize);
        var packQty = item.PackQuantity;
        var unitQty = item.UnitQuantity;
        var packBonus = item.PackBonus;
        var unitBonus = item.UnitBonus;
        var packPrice = item.PackPrice;
        var unitPrice = item.Price;
        var discPct = item.ItemDiscountPercentage;

        if (changedField == "pack_quantity")
            unitQty = (int)Math.Round(packQty * packSize, 0, MidpointRounding.AwayFromZero);
        else if (changedField == "unit_quantity" || changedField == "quantity")
            packQty = R2((decimal)unitQty / packSize);
        else if (packQty > 0m && unitQty == 0)
            unitQty = (int)Math.Round(packQty * packSize, 0, MidpointRounding.AwayFromZero);
        else if (unitQty > 0 && packQty == 0m)
            packQty = R2((decimal)unitQty / packSize);

        if (changedField == "pack_bonus")
            unitBonus = (int)Math.Round(packBonus * packSize, 0, MidpointRounding.AwayFromZero);
        else if (changedField == "unit_bonus" || changedField == "bonus")
            packBonus = R2((decimal)unitBonus / packSize);
        else if (packBonus > 0m && unitBonus == 0)
            unitBonus = (int)Math.Round(packBonus * packSize, 0, MidpointRounding.AwayFromZero);
        else if (unitBonus > 0 && packBonus == 0m)
            packBonus = R2((decimal)unitBonus / packSize);

        if (changedField == "pack_price")
            unitPrice = R2(packPrice / packSize);
        else if (changedField == "price")
            packPrice = R2(unitPrice * packSize);
        else if (packPrice > 0m && unitPrice == 0m)
            unitPrice = R2(packPrice / packSize);
        else if (unitPrice > 0m && packPrice == 0m)
            packPrice = R2(unitPrice * packSize);

        var gross = unitQty * unitPrice;
        var itemDisc = (gross * discPct) / 100m;
        var subTotal = R2(gross - itemDisc);

        var unitCost = item.AvgPrice > 0m ? item.AvgPrice : item.UnitPurchasePrice;
        decimal? margin = null;
        if (unitQty > 0 && unitBonus > 0 && subTotal > 0m)
        {
            var totalRowCost = (unitQty + unitBonus) * unitCost;
            margin = R2(((subTotal - totalRowCost) / subTotal) * 100m);
        }
        else
        {
            var netUnitPrice = unitPrice * (1m - (discPct / 100m));
            if (netUnitPrice > 0m)
                margin = R2(((netUnitPrice - unitCost) / netUnitPrice) * 100m);
        }

        item.PackQuantity = packQty;
        item.UnitQuantity = unitQty;
        item.Quantity = unitQty;
        item.PackBonus = packBonus;
        item.UnitBonus = unitBonus;
        item.Bonus = unitBonus;
        item.PackPrice = packPrice;
        item.Price = unitPrice;
        item.Margin = margin;
        item.SubTotal = subTotal;
        return item;
    }

    public static SaleFooterCalcModel RecalcSaleFooter(
        IReadOnlyCollection<SaleLineCalcModel> items,
        SaleFooterCalcModel footer,
        string? changedField = null)
    {
        var grossSum = items.Sum(i => i.Quantity * i.Price);
        var itemDiscSum = items.Sum(i => (i.Quantity * i.Price * i.ItemDiscountPercentage) / 100m);

        var discountPct = footer.DiscountPercentage;
        var discountAmt = footer.DiscountAmount;

        if (changedField == "discount_percentage")
            discountAmt = (grossSum * discountPct) / 100m;
        else if (changedField == "discount_amount")
            discountPct = grossSum != 0m ? (discountAmt / grossSum) * 100m : 0m;
        else if (discountPct != 0m)
            discountAmt = (grossSum * discountPct) / 100m;

        var taxableBase = grossSum - itemDiscSum - discountAmt;
        var taxPct = footer.TaxPercentage;
        var taxAmt = footer.TaxAmount;

        if (changedField == "tax_percentage")
            taxAmt = (taxableBase * taxPct) / 100m;
        else if (changedField == "tax_amount")
            taxPct = taxableBase != 0m ? (taxAmt / taxableBase) * 100m : 0m;
        else if (taxPct != 0m)
            taxAmt = (taxableBase * taxPct) / 100m;

        var totalFloat = taxableBase + taxAmt;
        var totalRounded = Math.Round(totalFloat, 0, MidpointRounding.AwayFromZero);

        footer.GrossAmount = R2(grossSum);
        footer.ItemDiscount = R2(itemDiscSum);
        footer.DiscountPercentage = R2(discountPct);
        footer.DiscountAmount = R2(discountAmt);
        footer.TaxPercentage = R2(taxPct);
        footer.TaxAmount = R2(taxAmt);
        footer.Total = totalRounded;

        if (!string.Equals(footer.InvoiceType, "credit", StringComparison.OrdinalIgnoreCase) && changedField != "total_receive")
        {
            footer.TotalReceive = totalRounded;
        }

        footer.RemainThisBill = R2(footer.Total - footer.TotalReceive);
        footer.TotalBalance = R2(footer.PreviousBalance + footer.RemainThisBill);
        return footer;
    }

    /// <summary>
    /// Exact port of resources/js/Formula/PurchaseReturn.js::recalcItem and InvoiceCalculationService::calculateAndValidatePurchaseReturn.
    /// </summary>
    public static PurchaseReturnLineCalcModel RecalcPurchaseReturnItem(
        PurchaseReturnLineCalcModel item,
        string? changedField = null)
    {
        var packSize = item.PackSize;
        var returnPackQty = item.ReturnPackQuantity;
        var returnUnitQty = (decimal)item.ReturnUnitQuantity;
        var packPurchase = item.PackPurchasePrice;
        var unitPurchase = item.UnitPurchasePrice;
        var discountPct = Math.Clamp(item.ItemDiscountPercentage, 0m, 100m);

        if (packSize > 0)
        {
            if (changedField == "return_pack_quantity" || changedField == "pack_quantity")
            {
                returnUnitQty = Math.Round(returnPackQty * packSize, 0, MidpointRounding.AwayFromZero);
            }
            else if (changedField == "return_unit_quantity" || changedField == "unit_quantity")
            {
                returnPackQty = returnUnitQty / packSize;
            }
            else
            {
                if (returnPackQty > 0m && returnUnitQty == 0m)
                    returnUnitQty = Math.Round(returnPackQty * packSize, 0, MidpointRounding.AwayFromZero);
                else if (returnUnitQty > 0m && returnPackQty == 0m)
                    returnPackQty = returnUnitQty / packSize;
            }

            if (changedField == "pack_purchase_price")
            {
                unitPurchase = packPurchase / packSize;
            }
            else if (changedField == "unit_purchase_price")
            {
                packPurchase = unitPurchase * packSize;
            }
            else
            {
                if (packPurchase > 0m && unitPurchase == 0m)
                    unitPurchase = packPurchase / packSize;
                else if (unitPurchase > 0m && packPurchase == 0m)
                    packPurchase = unitPurchase * packSize;
            }
        }
        else
        {
            returnUnitQty = Math.Round(returnUnitQty, 0, MidpointRounding.AwayFromZero);
        }

        var unitQtyInt = Math.Max(0, (int)Math.Round(returnUnitQty, 0, MidpointRounding.AwayFromZero));
        returnPackQty = Math.Max(0m, returnPackQty);
        packPurchase = Math.Max(0m, packPurchase);
        unitPurchase = Math.Max(0m, unitPurchase);

        decimal subTotal;
        if (unitQtyInt > 0 && unitPurchase > 0m)
            subTotal = unitQtyInt * unitPurchase;
        else
            subTotal = returnPackQty * packPurchase;

        var discountAmount = (subTotal * discountPct) / 100m;
        var netSubTotal = Math.Max(0m, subTotal - discountAmount);

        item.ReturnPackQuantity = R6(returnPackQty);
        item.ReturnUnitQuantity = unitQtyInt;
        item.PackPurchasePrice = R6(packPurchase);
        item.UnitPurchasePrice = R6(unitPurchase);
        item.ItemDiscountPercentage = R2(discountPct);
        item.SubTotal = R2(netSubTotal);
        return item;
    }

    /// <summary>
    /// Exact port of resources/js/Formula/PurchaseReturn.js::recalcFooter and InvoiceCalculationService::calculateAndValidatePurchaseReturn.
    /// </summary>
    public static PurchaseReturnFooterCalcModel RecalcPurchaseReturnFooter(
        IEnumerable<PurchaseReturnLineCalcModel> items,
        PurchaseReturnFooterCalcModel footer,
        string? changedField = null)
    {
        var grossTotal = R2(items.Sum(i => i.SubTotal));
        var discountPct = footer.DiscountPercentage;
        var discountAmt = footer.DiscountAmount;

        if (changedField == "discount_percentage")
            discountAmt = (grossTotal * discountPct) / 100m;
        else if (changedField == "discount_amount")
            discountPct = grossTotal > 0m ? (discountAmt / grossTotal) * 100m : 0m;
        else if (changedField == null && discountAmt > 0m && discountPct == 0m)
            discountPct = grossTotal > 0m ? (discountAmt / grossTotal) * 100m : 0m;
        else if (discountPct > 0m)
            discountAmt = (grossTotal * discountPct) / 100m;

        if (discountAmt > grossTotal)
        {
            discountAmt = grossTotal;
            discountPct = grossTotal > 0m ? 100m : 0m;
        }
        if (discountAmt < 0m)
        {
            discountAmt = 0m;
            discountPct = 0m;
        }

        var afterDiscount = Math.Max(0m, grossTotal - discountAmt);
        var taxPct = footer.TaxPercentage;
        var taxAmt = footer.TaxAmount;

        if (changedField == "tax_percentage")
            taxAmt = (afterDiscount * taxPct) / 100m;
        else if (changedField == "tax_amount")
            taxPct = afterDiscount > 0m ? (taxAmt / afterDiscount) * 100m : 0m;
        else if (changedField == null && taxAmt > 0m && taxPct == 0m)
            taxPct = afterDiscount > 0m ? (taxAmt / afterDiscount) * 100m : 0m;
        else if (taxPct > 0m)
            taxAmt = (afterDiscount * taxPct) / 100m;

        if (taxAmt < 0m)
        {
            taxAmt = 0m;
            taxPct = 0m;
        }

        footer.GrossTotal = grossTotal;
        footer.DiscountPercentage = R2(discountPct);
        footer.DiscountAmount = R2(discountAmt);
        footer.TaxPercentage = R2(taxPct);
        footer.TaxAmount = R2(taxAmt);
        footer.Total = R2(afterDiscount + taxAmt);
        return footer;
    }
}
