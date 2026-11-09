using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karobar.Domain.Entities;

[Table("batches")]
public class Batch
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("batch_number")]
    public string BatchNumber { get; set; } = string.Empty;

    [Column("expiry_date")]
    public DateTime? ExpiryDate { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    [Column("pack_size")]
    public int? PackSize { get; set; }

    [Column("storage_location_id")]
    public long? StorageLocationId { get; set; }

    [Column("rack")]
    public string? Rack { get; set; }

    [Column("pack_purchase_price")]
    public decimal? PackPurchasePrice { get; set; }

    [Column("unit_purchase_price")]
    public decimal? UnitPurchasePrice { get; set; }

    [Column("pack_sale_price")]
    public decimal? PackSalePrice { get; set; }

    [Column("unit_sale_price")]
    public decimal? UnitSalePrice { get; set; }

    [Column("whole_sale_pack_price")]
    public decimal? WholeSalePackPrice { get; set; }

    [Column("whole_sale_unit_price")]
    public decimal? WholeSaleUnitPrice { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("user_demands")]
public class UserDemand
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("product_id")]
    public long? ProductId { get; set; }

    [Column("product_name")]
    public string? ProductName { get; set; }

    [Column("customer_name")]
    public string? CustomerName { get; set; }

    [Column("customer_phone")]
    public string? CustomerPhone { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; } = 1;

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("demand_date")]
    public DateTime? DemandDate { get; set; }

    [Column("status")]
    public string Status { get; set; } = "pending";

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("stock_adjustments")]
public class StockAdjustment
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long? UserId { get; set; }

    [Column("posted_number")]
    public string PostedNumber { get; set; } = string.Empty;

    [Column("posted_date")]
    public DateTime PostedDate { get; set; }

    [Column("note")]
    public string? Note { get; set; }

    [Column("total_worth")]
    public decimal TotalWorth { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("stock_adjustment_items")]
public class StockAdjustmentItem
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("stock_adjustment_id")]
    public long StockAdjustmentId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("batch_id")]
    public long? BatchId { get; set; }

    [Column("batch_number")]
    public string? BatchNumber { get; set; }

    [Column("expiry")]
    public DateTime? Expiry { get; set; }

    [Column("pack_size")]
    public int PackSize { get; set; } = 1;

    [Column("previous_qty")]
    public decimal PreviousQty { get; set; }

    [Column("actual_qty")]
    public decimal ActualQty { get; set; }

    [Column("diff_qty")]
    public decimal DiffQty { get; set; }

    [Column("unit_purchase_price")]
    public decimal UnitPurchasePrice { get; set; }

    [Column("worth_adjusted")]
    public decimal WorthAdjusted { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("stocktake_sessions")]
public class StocktakeSession
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("session_number")]
    public string SessionNumber { get; set; } = string.Empty;

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("session_type")]
    public string SessionType { get; set; } = "full";

    [Column("status")]
    public string Status { get; set; } = "draft";

    [Column("is_blind")]
    public bool IsBlind { get; set; }

    [Column("category_id")]
    public long? CategoryId { get; set; }

    [Column("brand_id")]
    public long? BrandId { get; set; }

    [Column("location_filter")]
    public string? LocationFilter { get; set; }

    [Column("total_items")]
    public int TotalItems { get; set; }

    [Column("counted_items")]
    public int CountedItems { get; set; }

    [Column("variance_items")]
    public int VarianceItems { get; set; }

    [Column("net_variance_value")]
    public decimal NetVarianceValue { get; set; }

    [Column("stock_adjustment_id")]
    public long? StockAdjustmentId { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("finalized_by")]
    public long? FinalizedBy { get; set; }

    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("finalized_at")]
    public DateTime? FinalizedAt { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("stocktake_items")]
public class StocktakeItem
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("stocktake_session_id")]
    public long StocktakeSessionId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("batch_id")]
    public long? BatchId { get; set; }

    [Column("batch_number")]
    public string? BatchNumber { get; set; }

    [Column("expiry_date")]
    public DateTime? ExpiryDate { get; set; }

    [Column("pack_size")]
    public int PackSize { get; set; } = 1;

    [Column("rack_location")]
    public string? RackLocation { get; set; }

    [Column("system_qty")]
    public int SystemQty { get; set; }

    [Column("counted_qty")]
    public int? CountedQty { get; set; }

    [Column("variance_qty")]
    public int VarianceQty { get; set; }

    [Column("unit_cost")]
    public decimal UnitCost { get; set; }

    [Column("variance_value")]
    public decimal VarianceValue { get; set; }

    [Column("variance_reason")]
    public string? VarianceReason { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("counted_by")]
    public long? CountedBy { get; set; }

    [Column("counted_at")]
    public DateTime? CountedAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("stock_disposals")]
public class StockDisposal
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("disposal_number")]
    public string DisposalNumber { get; set; } = string.Empty;

    [Column("disposal_date")]
    public DateTime DisposalDate { get; set; }

    [Column("status")]
    public string Status { get; set; } = "quarantined";

    [Column("cause")]
    public string Cause { get; set; } = "shelf_expiry";

    [Column("disposal_method")]
    public string DisposalMethod { get; set; } = "quarantine_holding";

    [Column("quarantine_location")]
    public string? QuarantineLocation { get; set; }

    [Column("supplier_id")]
    public long? SupplierId { get; set; }

    [Column("witness_name")]
    public string? WitnessName { get; set; }

    [Column("witness_designation")]
    public string? WitnessDesignation { get; set; }

    [Column("witness_cnic")]
    public string? WitnessCnic { get; set; }

    [Column("regulatory_reference")]
    public string? RegulatoryReference { get; set; }

    [Column("has_narcotics")]
    public bool HasNarcotics { get; set; }

    [Column("total_items")]
    public int TotalItems { get; set; }

    [Column("total_quantity")]
    public int TotalQuantity { get; set; }

    [Column("total_cost_value")]
    public decimal TotalCostValue { get; set; }

    [Column("stock_adjustment_id")]
    public long? StockAdjustmentId { get; set; }

    [Column("expense_id")]
    public long? ExpenseId { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("approved_by")]
    public long? ApprovedBy { get; set; }

    [Column("disposed_at")]
    public DateTime? DisposedAt { get; set; }

    [Column("restored_at")]
    public DateTime? RestoredAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("stock_disposal_items")]
public class StockDisposalItem
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("stock_disposal_id")]
    public long StockDisposalId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("batch_id")]
    public long? BatchId { get; set; }

    [Column("batch_number")]
    public string? BatchNumber { get; set; }

    [Column("expiry_date")]
    public DateTime? ExpiryDate { get; set; }

    [Column("pack_size")]
    public int PackSize { get; set; } = 1;

    [Column("quantity_disposed")]
    public int QuantityDisposed { get; set; }

    [Column("unit_cost")]
    public decimal UnitCost { get; set; }

    [Column("line_cost_value")]
    public decimal LineCostValue { get; set; }

    [Column("is_narcotic")]
    public bool IsNarcotic { get; set; }

    [Column("item_notes")]
    public string? ItemNotes { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
