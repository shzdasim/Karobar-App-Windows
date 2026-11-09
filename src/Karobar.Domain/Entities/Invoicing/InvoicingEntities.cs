using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karobar.Domain.Entities;

[Table("purchase_orders")]
public class PurchaseOrder
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("order_number")]
    public string OrderNumber { get; set; } = string.Empty;

    [Column("supplier_id")]
    public long SupplierId { get; set; }

    [Column("order_date")]
    public DateTime OrderDate { get; set; }

    [Column("expected_delivery_date")]
    public DateTime? ExpectedDeliveryDate { get; set; }

    [Column("status")]
    public string Status { get; set; } = "draft";

    [Column("subtotal")]
    public decimal Subtotal { get; set; }

    [Column("discount_amount")]
    public decimal DiscountAmount { get; set; }

    [Column("tax_percentage")]
    public decimal TaxPercentage { get; set; }

    [Column("tax_amount")]
    public decimal TaxAmount { get; set; }

    [Column("total")]
    public decimal Total { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("arrangement")]
    public string? Arrangement { get; set; } = "original";

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("received_by")]
    public long? ReceivedBy { get; set; }

    [Column("received_at")]
    public DateTime? ReceivedAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("purchase_order_items")]
public class PurchaseOrderItem
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("purchase_order_id")]
    public long PurchaseOrderId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("pack_size")]
    public int PackSize { get; set; } = 1;

    [Column("quantity_ordered")]
    public int QuantityOrdered { get; set; }

    [Column("quantity_received")]
    public int QuantityReceived { get; set; }

    [Column("unit_bonus")]
    public int UnitBonus { get; set; }

    [Column("unit_cost")]
    public decimal UnitCost { get; set; }

    [Column("discount_percentage")]
    public decimal DiscountPercentage { get; set; }

    [Column("tax_percentage")]
    public decimal TaxPercentage { get; set; }

    [Column("line_total")]
    public decimal LineTotal { get; set; }

    [Column("position")]
    public int Position { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("purchase_invoices")]
public class PurchaseInvoice
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("supplier_id")]
    public long SupplierId { get; set; }

    [Column("purchase_order_id")]
    public long? PurchaseOrderId { get; set; }

    [Column("receipt_key")]
    public string? ReceiptKey { get; set; }

    [Column("posted_number")]
    public string PostedNumber { get; set; } = string.Empty;

    [Column("posted_date")]
    public DateTime PostedDate { get; set; }

    [Column("remarks")]
    public string? Remarks { get; set; }

    [Column("invoice_number")]
    public string? InvoiceNumber { get; set; }

    [Column("invoice_date")]
    public DateTime? InvoiceDate { get; set; }

    [Column("due_date")]
    public DateTime? DueDate { get; set; }

    [Column("credit_days")]
    public int? CreditDays { get; set; }

    [Column("invoice_type")]
    public string InvoiceType { get; set; } = "debit";

    [Column("status")]
    public string Status { get; set; } = "posted";

    [Column("reversed_at")]
    public DateTime? ReversedAt { get; set; }

    [Column("reversed_by")]
    public long? ReversedBy { get; set; }

    [Column("reversal_reason")]
    public string? ReversalReason { get; set; }

    [Column("invoice_amount")]
    public decimal InvoiceAmount { get; set; }

    [Column("gross_total")]
    public decimal GrossTotal { get; set; }

    [Column("discount_percentage")]
    public decimal DiscountPercentage { get; set; }

    [Column("discount_amount")]
    public decimal DiscountAmount { get; set; }

    [Column("tax_percentage")]
    public decimal TaxPercentage { get; set; }

    [Column("tax_amount")]
    public decimal TaxAmount { get; set; }

    [Column("total_amount")]
    public decimal TotalAmount { get; set; }

    [Column("total_paid")]
    public decimal TotalPaid { get; set; }

    [Column("payment_method")]
    public string? PaymentMethod { get; set; } = "cash";

    [Column("account_id")]
    public long? AccountId { get; set; }

    [Column("expiry_return_policy")]
    public string? ExpiryReturnPolicy { get; set; }

    [Column("expiry_return_months")]
    public int? ExpiryReturnMonths { get; set; }

    [Column("expiry_return_days")]
    public int? ExpiryReturnDays { get; set; }

    [Column("expiry_return_notes")]
    public string? ExpiryReturnNotes { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("purchase_invoice_items")]
public class PurchaseInvoiceItem
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("purchase_invoice_id")]
    public long PurchaseInvoiceId { get; set; }

    [Column("purchase_order_item_id")]
    public long? PurchaseOrderItemId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("storage_location_id")]
    public long? StorageLocationId { get; set; }

    [Column("batch")]
    public string? Batch { get; set; }

    [Column("expiry")]
    public DateTime? Expiry { get; set; }

    [Column("pack_quantity")]
    public decimal PackQuantity { get; set; }

    [Column("pack_size")]
    public int PackSize { get; set; } = 1;

    [Column("unit_quantity")]
    public int UnitQuantity { get; set; }

    [Column("pack_purchase_price")]
    public decimal PackPurchasePrice { get; set; }

    [Column("unit_purchase_price")]
    public decimal UnitPurchasePrice { get; set; }

    [Column("pack_sale_price")]
    public decimal PackSalePrice { get; set; }

    [Column("unit_sale_price")]
    public decimal UnitSalePrice { get; set; }

    [Column("whole_sale_pack_price")]
    public decimal? WholeSalePackPrice { get; set; }

    [Column("whole_sale_unit_price")]
    public decimal? WholeSaleUnitPrice { get; set; }

    [Column("pack_bonus")]
    public decimal PackBonus { get; set; }

    [Column("unit_bonus")]
    public int UnitBonus { get; set; }

    [Column("item_discount_percentage")]
    public decimal ItemDiscountPercentage { get; set; }

    [Column("whole_sale_discount_percentage")]
    public decimal? WholeSaleDiscountPercentage { get; set; }

    [Column("margin")]
    public decimal? Margin { get; set; }

    [Column("whole_sale_margin")]
    public decimal? WholeSaleMargin { get; set; }

    [Column("avg_price")]
    public decimal AvgPrice { get; set; }

    [Column("sub_total")]
    public decimal SubTotal { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    [Column("line_order")]
    public int LineOrder { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("purchase_returns")]
public class PurchaseReturn
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("supplier_id")]
    public long SupplierId { get; set; }

    [Column("purchase_invoice_id")]
    public long? PurchaseInvoiceId { get; set; }

    [Column("posted_number")]
    public string PostedNumber { get; set; } = string.Empty;

    [Column("date")]
    public DateTime Date { get; set; }

    [Column("remarks")]
    public string? Remarks { get; set; }

    [Column("gross_total")]
    public decimal GrossTotal { get; set; }

    [Column("discount_percentage")]
    public decimal DiscountPercentage { get; set; }

    [Column("discount_amount")]
    public decimal DiscountAmount { get; set; }

    [Column("tax_percentage")]
    public decimal TaxPercentage { get; set; }

    [Column("tax_amount")]
    public decimal TaxAmount { get; set; }

    [Column("total")]
    public decimal Total { get; set; }

    [Column("settlement_mode")]
    public string? SettlementMode { get; set; } = "ledger_credit";

    [Column("payment_method")]
    public string? PaymentMethod { get; set; }

    [Column("account_id")]
    public long? AccountId { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("purchase_return_items")]
public class PurchaseReturnItem
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("purchase_return_id")]
    public long PurchaseReturnId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("batch")]
    public string? Batch { get; set; }

    [Column("expiry")]
    public DateTime? Expiry { get; set; }

    [Column("pack_size")]
    public int PackSize { get; set; } = 1;

    [Column("purchased_pack_quantity")]
    public decimal? PurchasedPackQuantity { get; set; }

    [Column("return_pack_quantity")]
    public decimal ReturnPackQuantity { get; set; }

    [Column("return_unit_quantity")]
    public int ReturnUnitQuantity { get; set; }

    [Column("pack_purchase_price")]
    public decimal PackPurchasePrice { get; set; }

    [Column("unit_purchase_price")]
    public decimal UnitPurchasePrice { get; set; }

    [Column("item_discount_percentage")]
    public decimal ItemDiscountPercentage { get; set; }

    [Column("sub_total")]
    public decimal SubTotal { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("sale_invoices")]
public class SaleInvoice
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long? UserId { get; set; }

    [Column("salesman_id")]
    public long? SalesmanId { get; set; }

    [Column("customer_id")]
    public long CustomerId { get; set; }

    [Column("posted_number")]
    public string PostedNumber { get; set; } = string.Empty;

    [Column("date")]
    public DateTime Date { get; set; }

    [Column("remarks")]
    public string? Remarks { get; set; }

    [Column("doctor_name")]
    public string? DoctorName { get; set; }

    [Column("patient_name")]
    public string? PatientName { get; set; }

    [Column("sale_type")]
    public string SaleType { get; set; } = "retail";

    [Column("wholesale_type")]
    public string? WholesaleType { get; set; } = "unit";

    [Column("invoice_type")]
    public string InvoiceType { get; set; } = "debit";

    [Column("status")]
    public string Status { get; set; } = "posted";

    [Column("reversed_at")]
    public DateTime? ReversedAt { get; set; }

    [Column("reversed_by")]
    public long? ReversedBy { get; set; }

    [Column("reversal_reason")]
    public string? ReversalReason { get; set; }

    [Column("due_date")]
    public DateTime? DueDate { get; set; }

    [Column("credit_days")]
    public int? CreditDays { get; set; }

    [Column("gross_amount")]
    public decimal GrossAmount { get; set; }

    [Column("item_discount")]
    public decimal ItemDiscount { get; set; }

    [Column("discount_percentage")]
    public decimal DiscountPercentage { get; set; }

    [Column("discount_amount")]
    public decimal DiscountAmount { get; set; }

    [Column("tax_percentage")]
    public decimal TaxPercentage { get; set; }

    [Column("tax_amount")]
    public decimal TaxAmount { get; set; }

    [Column("total")]
    public decimal Total { get; set; }

    [Column("total_receive")]
    public decimal TotalReceive { get; set; }

    [Column("remain_this_bill")]
    public decimal RemainThisBill { get; set; }

    [Column("total_balance")]
    public decimal TotalBalance { get; set; }

    [Column("payment_method")]
    public string? PaymentMethod { get; set; } = "cash";

    [Column("account_id")]
    public long? AccountId { get; set; }

    [Column("payment_reference")]
    public string? PaymentReference { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("sale_invoice_items")]
public class SaleInvoiceItem
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("sale_invoice_id")]
    public long SaleInvoiceId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("batch_id")]
    public long? BatchId { get; set; }

    [Column("pack_size")]
    public int PackSize { get; set; } = 1;

    [Column("batch_number")]
    public string? BatchNumber { get; set; }

    [Column("expiry")]
    public DateTime? Expiry { get; set; }

    [Column("current_quantity")]
    public int CurrentQuantity { get; set; }

    [Column("pack_quantity")]
    public decimal PackQuantity { get; set; }

    [Column("unit_quantity")]
    public int UnitQuantity { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    [Column("pack_bonus")]
    public decimal PackBonus { get; set; }

    [Column("unit_bonus")]
    public int UnitBonus { get; set; }

    [Column("bonus")]
    public int Bonus { get; set; }

    [Column("pack_price")]
    public decimal PackPrice { get; set; }

    [Column("price")]
    public decimal Price { get; set; }

    [Column("cost_price_snapshot")]
    public decimal? CostPriceSnapshot { get; set; }

    [Column("item_discount_percentage")]
    public decimal ItemDiscountPercentage { get; set; }

    [Column("margin")]
    public decimal? Margin { get; set; }

    [Column("sub_total")]
    public decimal SubTotal { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("sale_returns")]
public class SaleReturn
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long? UserId { get; set; }

    [Column("customer_id")]
    public long CustomerId { get; set; }

    [Column("sale_invoice_id")]
    public long? SaleInvoiceId { get; set; }

    [Column("posted_number")]
    public string PostedNumber { get; set; } = string.Empty;

    [Column("date")]
    public DateTime Date { get; set; }

    [Column("remarks")]
    public string? Remarks { get; set; }

    [Column("gross_total")]
    public decimal GrossTotal { get; set; }

    [Column("discount_percentage")]
    public decimal DiscountPercentage { get; set; }

    [Column("discount_amount")]
    public decimal DiscountAmount { get; set; }

    [Column("tax_percentage")]
    public decimal TaxPercentage { get; set; }

    [Column("tax_amount")]
    public decimal TaxAmount { get; set; }

    [Column("total")]
    public decimal Total { get; set; }

    [Column("settlement_mode")]
    public string SettlementMode { get; set; } = "cash_refund";

    [Column("refunded_amount")]
    public decimal RefundedAmount { get; set; }

    [Column("credited_amount")]
    public decimal CreditedAmount { get; set; }

    [Column("payment_method")]
    public string? PaymentMethod { get; set; } = "cash";

    [Column("account_id")]
    public long? AccountId { get; set; }

    [Column("payment_id")]
    public long? PaymentId { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("sale_return_items")]
public class SaleReturnItem
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("sale_return_id")]
    public long SaleReturnId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("batch_id")]
    public long? BatchId { get; set; }

    [Column("pack_size")]
    public int PackSize { get; set; } = 1;

    [Column("batch_number")]
    public string? BatchNumber { get; set; }

    [Column("expiry")]
    public DateTime? Expiry { get; set; }

    [Column("unit_sale_quantity")]
    public int UnitSaleQuantity { get; set; }

    [Column("unit_return_quantity")]
    public int UnitReturnQuantity { get; set; }

    [Column("unit_sale_price")]
    public decimal UnitSalePrice { get; set; }

    [Column("cost_price_snapshot")]
    public decimal? CostPriceSnapshot { get; set; }

    [Column("item_discount_percentage")]
    public decimal ItemDiscountPercentage { get; set; }

    [Column("sub_total")]
    public decimal SubTotal { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("invoice_drafts")]
public class InvoiceDraft
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("draft_number")]
    public string DraftNumber { get; set; } = string.Empty;

    [Column("invoice_kind")]
    public string InvoiceKind { get; set; } = "sale_retail";

    [Column("user_id")]
    public long? UserId { get; set; }

    [Column("party_id")]
    public long? PartyId { get; set; }

    [Column("party_name")]
    public string? PartyName { get; set; }

    [Column("title")]
    public string? Title { get; set; }

    [Column("valid_until")]
    public DateTime? ValidUntil { get; set; }

    [Column("total_amount")]
    public decimal TotalAmount { get; set; }

    [Column("item_count")]
    public int ItemCount { get; set; }

    [Column("payload")]
    public string Payload { get; set; } = "{}";

    [Column("version")]
    public int Version { get; set; } = 1;

    [Column("converted_invoice_id")]
    public long? ConvertedInvoiceId { get; set; }

    [Column("converted_at")]
    public DateTime? ConvertedAt { get; set; }

    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("document_histories")]
public class DocumentHistory
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("document_type")]
    public string DocumentType { get; set; } = string.Empty;

    [Column("document_id")]
    public long DocumentId { get; set; }

    [Column("posted_number")]
    public string? PostedNumber { get; set; }

    [Column("action")]
    public string Action { get; set; } = "created";

    [Column("user_id")]
    public long? UserId { get; set; }

    [Column("reason")]
    public string? Reason { get; set; }

    [Column("before_snapshot")]
    public string? BeforeSnapshot { get; set; }

    [Column("after_snapshot")]
    public string? AfterSnapshot { get; set; }

    [Column("changes_summary")]
    public string? ChangesSummary { get; set; }

    [Column("ip_address")]
    public string? IpAddress { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
