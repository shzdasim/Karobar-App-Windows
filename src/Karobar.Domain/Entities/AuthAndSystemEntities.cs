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

[Table("dismissed_notifications")]
public class DismissedNotification
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("notification_type")]
    public string NotificationType { get; set; } = string.Empty;

    [Column("reference_id")]
    public long ReferenceId { get; set; }

    [Column("dismissed_at")]
    public DateTime? DismissedAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("settings")]
public class Setting
{
    [Key]
    [Column("id")]
    public long Id { get; set; } = 1;

    [Column("store_name")]
    public string? StoreName { get; set; } = "Karobar App";

    [Column("phone_number")]
    public string? PhoneNumber { get; set; }

    [Column("address")]
    public string? Address { get; set; }

    [Column("license_number")]
    public string? LicenseNumber { get; set; }

    [Column("logo_path")]
    public string? LogoPath { get; set; }

    [Column("note")]
    public string? Note { get; set; }

    [Column("note_en")]
    public string? NoteEn { get; set; }

    [Column("note_ur")]
    public string? NoteUr { get; set; }

    [Column("whatsapp_message_en")]
    public string? WhatsappMessageEn { get; set; }

    [Column("whatsapp_message_ur")]
    public string? WhatsappMessageUr { get; set; }

    [Column("printer_type")]
    public string PrinterType { get; set; } = "thermal";

    [Column("thermal_template")]
    public string ThermalTemplate { get; set; } = "standard";

    [Column("a4_template")]
    public string A4Template { get; set; } = "standard";

    [Column("share_paper_size")]
    public string SharePaperSize { get; set; } = "a4";

    [Column("share_show_logo")]
    public bool ShareShowLogo { get; set; } = true;

    [Column("navigation_style")]
    public string NavigationStyle { get; set; } = "sidebar";

    [Column("sale_system")]
    public string SaleSystem { get; set; } = "retail_wholesale";

    [Column("shop_type")]
    public string ShopType { get; set; } = "pharmacy";

    [Column("currency_code")]
    public string CurrencyCode { get; set; } = "PKR";

    [Column("currency_symbol")]
    public string CurrencySymbol { get; set; } = "Rs.";

    [Column("currency_position")]
    public string CurrencyPosition { get; set; } = "before";

    [Column("currency_decimal_places")]
    public int CurrencyDecimalPlaces { get; set; } = 2;

    [Column("expiry_warning_days")]
    public int ExpiryWarningDays { get; set; } = 90;

    [Column("smtp_enabled")]
    public bool SmtpEnabled { get; set; }

    [Column("smtp_host")]
    public string? SmtpHost { get; set; }

    [Column("smtp_port")]
    public int? SmtpPort { get; set; } = 587;

    [Column("smtp_username")]
    public string? SmtpUsername { get; set; }

    [Column("smtp_password")]
    public string? SmtpPassword { get; set; }

    [Column("smtp_encryption")]
    public string? SmtpEncryption { get; set; } = "tls";

    [Column("smtp_from_address")]
    public string? SmtpFromAddress { get; set; }

    [Column("smtp_from_name")]
    public string? SmtpFromName { get; set; }

    [Column("whatsapp_enabled")]
    public bool WhatsappEnabled { get; set; }

    [Column("whatsapp_provider")]
    public string? WhatsappProvider { get; set; } = "local_agent";

    [Column("whatsapp_access_token")]
    public string? WhatsappAccessToken { get; set; }

    [Column("whatsapp_phone_number_id")]
    public string? WhatsappPhoneNumberId { get; set; }

    [Column("whatsapp_business_account_id")]
    public string? WhatsappBusinessAccountId { get; set; }

    [Column("whatsapp_api_version")]
    public string? WhatsappApiVersion { get; set; } = "v21.0";

    [Column("whatsapp_api_url")]
    public string? WhatsappApiUrl { get; set; }

    [Column("whatsapp_default_country_code")]
    public string? WhatsappDefaultCountryCode { get; set; } = "92";

    [Column("whatsapp_template_name")]
    public string? WhatsappTemplateName { get; set; }

    [Column("whatsapp_template_language")]
    public string? WhatsappTemplateLanguage { get; set; } = "en_US";

    [Column("whatsapp_auto_fallback")]
    public bool WhatsappAutoFallback { get; set; } = true;

    [Column("cube_ai_enabled")]
    public bool CubeAiEnabled { get; set; } = true;

    [Column("cube_groq_enabled")]
    public bool CubeGroqEnabled { get; set; } = true;

    [Column("cube_groq_api_key")]
    public string? CubeGroqApiKey { get; set; }

    [Column("cube_groq_model")]
    public string? CubeGroqModel { get; set; } = "llama-3.3-70b-versatile";

    [Column("cube_groq_daily_limit")]
    public int CubeGroqDailyLimit { get; set; } = 250;

    [Column("cube_gemini_enabled")]
    public bool CubeGeminiEnabled { get; set; } = true;

    [Column("cube_gemini_api_key")]
    public string? CubeGeminiApiKey { get; set; }

    [Column("cube_gemini_model")]
    public string? CubeGeminiModel { get; set; } = "gemini-2.5-flash";

    [Column("cube_gemini_daily_limit")]
    public int CubeGeminiDailyLimit { get; set; } = 250;

    [Column("license_key")]
    public string? LicenseKey { get; set; }

    [Column("license_status")]
    public string? LicenseStatus { get; set; } = "unlicensed";

    [Column("license_payload")]
    public string? LicensePayload { get; set; }

    [Column("license_verified_at")]
    public DateTime? LicenseVerifiedAt { get; set; }

    [Column("license_expires_at")]
    public DateTime? LicenseExpiresAt { get; set; }

    [Column("license_machine_id")]
    public string? LicenseMachineId { get; set; }

    [Column("is_setup_completed")]
    public bool IsSetupCompleted { get; set; }

    [Column("setup_completed_at")]
    public DateTime? SetupCompletedAt { get; set; }

    [Column("terms_accepted_at")]
    public DateTime? TermsAcceptedAt { get; set; }

    [Column("accepted_terms_version")]
    public string? AcceptedTermsVersion { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("theme_settings")]
public class ThemeSetting
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("primary_color")]
    public string PrimaryColor { get; set; } = "#2563eb";

    [Column("primary_hover")]
    public string PrimaryHover { get; set; } = "#1d4ed8";

    [Column("primary_light")]
    public string PrimaryLight { get; set; } = "#dbeafe";

    [Column("secondary_color")]
    public string SecondaryColor { get; set; } = "#7c3aed";

    [Column("secondary_hover")]
    public string SecondaryHover { get; set; } = "#6d28d9";

    [Column("secondary_light")]
    public string SecondaryLight { get; set; } = "#ede9fe";

    [Column("tertiary_color")]
    public string TertiaryColor { get; set; } = "#06b6d4";

    [Column("tertiary_hover")]
    public string TertiaryHover { get; set; } = "#0891b2";

    [Column("tertiary_light")]
    public string TertiaryLight { get; set; } = "#cffafe";

    [Column("background_color")]
    public string BackgroundColor { get; set; } = "#f8fafc";

    [Column("surface_color")]
    public string SurfaceColor { get; set; } = "#ffffff";

    [Column("text_primary")]
    public string TextPrimary { get; set; } = "#0f172a";

    [Column("text_secondary")]
    public string TextSecondary { get; set; } = "#475569";

    [Column("success_color")]
    public string SuccessColor { get; set; } = "#10b981";

    [Column("warning_color")]
    public string WarningColor { get; set; } = "#f59e0b";

    [Column("danger_color")]
    public string DangerColor { get; set; } = "#ef4444";

    [Column("border_color")]
    public string BorderColor { get; set; } = "#e2e8f0";

    [Column("shadow_color")]
    public string ShadowColor { get; set; } = "rgba(15, 23, 42, 0.08)";

    [Column("button_style")]
    public string ButtonStyle { get; set; } = "modern";

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("is_default")]
    public bool IsDefault { get; set; }

    [Column("sidebar_template")]
    public string SidebarTemplate { get; set; } = "classic";

    [Column("topbar_template")]
    public string TopbarTemplate { get; set; } = "classic";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("barcode_templates")]
public class BarcodeTemplate
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("layout_type")]
    public string LayoutType { get; set; } = "roll";

    [Column("label_width_mm")]
    public decimal LabelWidthMm { get; set; } = 38;

    [Column("label_height_mm")]
    public decimal LabelHeightMm { get; set; } = 25;

    [Column("columns")]
    public int Columns { get; set; } = 1;

    [Column("rows")]
    public int Rows { get; set; } = 1;

    [Column("gap_x_mm")]
    public decimal GapXMm { get; set; } = 2;

    [Column("gap_y_mm")]
    public decimal GapYMm { get; set; } = 2;

    [Column("margin_top_mm")]
    public decimal MarginTopMm { get; set; } = 1;

    [Column("margin_left_mm")]
    public decimal MarginLeftMm { get; set; } = 1;

    [Column("barcode_format")]
    public string BarcodeFormat { get; set; } = "CODE128";

    [Column("barcode_height_mm")]
    public decimal BarcodeHeightMm { get; set; } = 10;

    [Column("show_store_name")]
    public bool ShowStoreName { get; set; } = true;

    [Column("show_product_name")]
    public bool ShowProductName { get; set; } = true;

    [Column("show_price")]
    public bool ShowPrice { get; set; } = true;

    [Column("show_batch")]
    public bool ShowBatch { get; set; }

    [Column("show_expiry")]
    public bool ShowExpiry { get; set; }

    [Column("show_code")]
    public bool ShowCode { get; set; } = true;

    [Column("is_default")]
    public bool IsDefault { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("backup_logs")]
public class BackupLog
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("filename")]
    public string Filename { get; set; } = string.Empty;

    [Column("type")]
    public string Type { get; set; } = "full";

    [Column("size")]
    public long Size { get; set; }

    [Column("path")]
    public string Path { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("status")]
    public string Status { get; set; } = "completed";

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("backup_schedules")]
public class BackupSchedule
{
    [Key]
    [Column("id")]
    public long Id { get; set; } = 1;

    [Column("is_automated")]
    public bool IsAutomated { get; set; }

    [Column("frequency")]
    public string Frequency { get; set; } = "daily";

    [Column("time_of_day")]
    public string TimeOfDay { get; set; } = "23:00";

    [Column("day_of_week")]
    public int? DayOfWeek { get; set; }

    [Column("day_of_month")]
    public int? DayOfMonth { get; set; }

    [Column("backup_type")]
    public string BackupType { get; set; } = "full";

    [Column("retention_days")]
    public int RetentionDays { get; set; } = 30;

    [Column("max_backups")]
    public int MaxBackups { get; set; } = 10;

    [Column("storage_destination")]
    public string StorageDestination { get; set; } = "local";

    [Column("custom_backup_path")]
    public string? CustomBackupPath { get; set; }

    [Column("last_run_at")]
    public DateTime? LastRunAt { get; set; }

    [Column("last_run_status")]
    public string? LastRunStatus { get; set; }

    [Column("last_run_message")]
    public string? LastRunMessage { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("cube_provider_usages")]
public class CubeProviderUsage
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("provider")]
    public string Provider { get; set; } = string.Empty;

    [Column("model")]
    public string? Model { get; set; }

    [Column("usage_date")]
    public DateTime UsageDate { get; set; }

    [Column("request_count")]
    public int RequestCount { get; set; }

    [Column("success_count")]
    public int SuccessCount { get; set; }

    [Column("failure_count")]
    public int FailureCount { get; set; }

    [Column("status")]
    public string Status { get; set; } = "ready";

    [Column("disabled_reason")]
    public string? DisabledReason { get; set; }

    [Column("cooldown_until")]
    public DateTime? CooldownUntil { get; set; }

    [Column("last_http_status")]
    public int? LastHttpStatus { get; set; }

    [Column("last_error_code")]
    public string? LastErrorCode { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
