using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karobar.Domain.Entities;

[Table("brands")]
public class Brand
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("image")]
    public string? Image { get; set; }

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
