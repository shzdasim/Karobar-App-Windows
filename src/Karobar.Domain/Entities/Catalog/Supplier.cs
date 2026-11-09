using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karobar.Domain.Entities;

[Table("suppliers")]
public class Supplier
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("contact_person")]
    public string? ContactPerson { get; set; }

    [Column("phone")]
    public string? Phone { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("address")]
    public string? Address { get; set; }

    [Column("tax_number")]
    public string? TaxNumber { get; set; }

    [Column("tax_status")]
    public string? TaxStatus { get; set; }

    [Column("credit_limit")]
    public decimal? CreditLimit { get; set; }

    [Column("credit_days")]
    public int? CreditDays { get; set; }

    [Column("expiry_return_policy")]
    public string? ExpiryReturnPolicy { get; set; } = "before_expiry";

    [Column("expiry_return_months")]
    public int? ExpiryReturnMonths { get; set; } = 3;

    [Column("expiry_return_days")]
    public int? ExpiryReturnDays { get; set; } = 0;

    [Column("expiry_return_notes")]
    public string? ExpiryReturnNotes { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
