using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karobar.Domain.Entities;

[Table("customers")]
public class Customer
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("customer_type")]
    public string CustomerType { get; set; } = "retail";

    [Column("phone")]
    public string? Phone { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("address")]
    public string? Address { get; set; }

    [Column("area")]
    public string? Area { get; set; }

    [Column("cnic")]
    public string? Cnic { get; set; }

    [Column("tax_number")]
    public string? TaxNumber { get; set; }

    [Column("tax_status")]
    public string? TaxStatus { get; set; }

    [Column("credit_limit")]
    public decimal? CreditLimit { get; set; }

    [Column("credit_days")]
    public int? CreditDays { get; set; } = 30;

    [Column("credit_status")]
    public string CreditStatus { get; set; } = "active";

    [Column("discount_percentage")]
    public decimal? DiscountPercentage { get; set; }

    [Column("is_regular")]
    public bool IsRegular { get; set; }

    [Column("refill_cycle")]
    public string? RefillCycle { get; set; } = "monthly";

    [Column("refill_custom_days")]
    public int? RefillCustomDays { get; set; }

    [Column("reminder_lead_days")]
    public int? ReminderLeadDays { get; set; } = 3;

    [Column("next_refill_date")]
    public DateTime? NextRefillDate { get; set; }

    [Column("last_refill_reminded_at")]
    public DateTime? LastRefillRemindedAt { get; set; }

    [Column("last_refill_collected_at")]
    public DateTime? LastRefillCollectedAt { get; set; }

    [Column("regular_notes")]
    public string? RegularNotes { get; set; }

    [Column("reminder_language")]
    public string? ReminderLanguage { get; set; } = "english";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("customer_regular_items")]
public class CustomerRegularItem
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("customer_id")]
    public long CustomerId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("dosage_quantity")]
    public int DosageQuantity { get; set; } = 1;

    [Column("dosage_instruction")]
    public string? DosageInstruction { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("customer_whole_sale_prices")]
public class CustomerWholeSalePrice
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("customer_id")]
    public long CustomerId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("pack_price")]
    public decimal? PackPrice { get; set; }

    [Column("unit_price")]
    public decimal? UnitPrice { get; set; }

    [Column("discount_percentage")]
    public decimal? DiscountPercentage { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
