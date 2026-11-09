using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karobar.Domain.Entities;

[Table("products")]
public class Product
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("product_code")]
    public string ProductCode { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("formulation")]
    public string? Formulation { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("image")]
    public string? Image { get; set; }

    [Column("pack_size")]
    public int PackSize { get; set; } = 1;

    [Column("quantity")]
    public int Quantity { get; set; }

    [Column("pack_purchase_price")]
    public decimal PackPurchasePrice { get; set; }

    [Column("unit_purchase_price")]
    public decimal UnitPurchasePrice { get; set; }

    [Column("pack_sale_price")]
    public decimal PackSalePrice { get; set; }

    [Column("unit_sale_price")]
    public decimal UnitSalePrice { get; set; }

    [Column("avg_price")]
    public decimal AvgPrice { get; set; }

    [Column("whole_sale_pack_price")]
    public decimal? WholeSalePackPrice { get; set; }

    [Column("whole_sale_unit_price")]
    public decimal? WholeSaleUnitPrice { get; set; }

    [Column("whole_sale_margin")]
    public decimal? WholeSaleMargin { get; set; }

    [Column("whole_sale_discount_percentage")]
    public decimal? WholeSaleDiscountPercentage { get; set; }

    [Column("max_discount")]
    public decimal? MaxDiscount { get; set; }

    [Column("margin")]
    public decimal? Margin { get; set; }

    [Column("narcotic")]
    public string Narcotic { get; set; } = "no";

    [Column("batch_enabled")]
    public string? BatchEnabled { get; set; } = "yes";

    [Column("expiry_warning_days")]
    public int? ExpiryWarningDays { get; set; }

    [Column("rack")]
    public string? Rack { get; set; }

    [Column("barcode")]
    public string? Barcode { get; set; }

    [Column("category_id")]
    public long? CategoryId { get; set; }

    [Column("brand_id")]
    public long? BrandId { get; set; }

    [Column("supplier_id")]
    public long? SupplierId { get; set; }

    [Column("storage_location_id")]
    public long? StorageLocationId { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("product_suppliers")]
public class ProductSupplier
{
    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("supplier_id")]
    public long SupplierId { get; set; }
}

[Table("supplier_product_positions")]
public class SupplierProductPosition
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("supplier_id")]
    public long SupplierId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("position")]
    public int Position { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
