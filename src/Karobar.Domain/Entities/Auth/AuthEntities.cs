using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karobar.Domain.Entities;

[Table("users")]
public class User
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("email")]
    public string? Email { get; set; }

    [Column("status")]
    public string Status { get; set; } = "active";

    [Column("is_admin")]
    public bool IsAdmin { get; set; }

    [Column("pin_code")]
    public string? PinCode { get; set; }

    [Column("email_verified_at")]
    public DateTime? EmailVerifiedAt { get; set; }

    [Column("password")]
    public string Password { get; set; } = string.Empty;

    [Column("avatar")]
    public string? Avatar { get; set; }

    [Column("max_discount_percentage")]
    public decimal? MaxDiscountPercentage { get; set; }

    [Column("remember_token")]
    public string? RememberToken { get; set; }

    [Column("remember_token_expires_at")]
    public DateTime? RememberTokenExpiresAt { get; set; }

    [Column("preferences")]
    public string? Preferences { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("roles")]
public class Role
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("guard_name")]
    public string GuardName { get; set; } = "sanctum";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("permissions")]
public class Permission
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("guard_name")]
    public string GuardName { get; set; } = "sanctum";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("role_has_permissions")]
public class RolePermission
{
    [Column("permission_id")]
    public long PermissionId { get; set; }

    [Column("role_id")]
    public long RoleId { get; set; }
}

[Table("model_has_roles")]
public class UserRole
{
    [Column("role_id")]
    public long RoleId { get; set; }

    [Column("model_type")]
    public string ModelType { get; set; } = "App\\Models\\User";

    [Column("model_id")]
    public long ModelId { get; set; }
}

[Table("model_has_permissions")]
public class UserPermission
{
    [Column("permission_id")]
    public long PermissionId { get; set; }

    [Column("model_type")]
    public string ModelType { get; set; } = "App\\Models\\User";

    [Column("model_id")]
    public long ModelId { get; set; }
}

[Table("user_preferences")]
public class UserPreference
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("key")]
    public string Key { get; set; } = string.Empty;

    [Column("value")]
    public string? Value { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
