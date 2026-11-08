using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Karobar.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using NSec.Cryptography;

namespace Karobar.Infrastructure.Security;

/// <summary>
/// 100% Exact Port of app/Services/LicenseService.php
/// Verifies KRBL.{base64url_sig}.{base64url_payload} using pinned Ed25519 public key and WMI/Registry fingerprint.
/// </summary>
public sealed class LicenseService : ILicenseService
{
    public const string KeyPrefix = "KRBL.";
    public const int SignatureLength = 64;
    public const int PublicKeyLength = 32;

    // Pinned Ed25519 Public Key from app/Services/LicenseService.php
    public const string DefaultPublicKeyB64 = "Fo2gpFWlgBCUcUZ2hUfwCizcT8EwxsrTuFL10gNpC0w";
    public const string DefaultPublicKeySha256 = "b4a91e9a0cc6aa627bd035e7a5407e9c9caffec2c9218cf8cdeb2f9bac0b5b27";

    private static readonly HashSet<string> InvalidTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "none", "null", "n/a", "na", "unknown", "to be filled by o.e.m.",
        "default string", "system serial number", "system product name",
        "not specified", "0", "00000000-0000-0000-0000-000000000000",
        "ffffffff-ffff-ffff-ffff-ffffffffffff"
    };

    private readonly IDbContextFactory<Persistence.KarobarDbContext> _dbFactory;
    private string? _cachedMachineId;

    public LicenseService(IDbContextFactory<Persistence.KarobarDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string ComputeMachineId()
    {
        if (!string.IsNullOrEmpty(_cachedMachineId))
            return _cachedMachineId;

        var parts = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            parts.AddRange(CollectWindowsIdentifiers());
        }

        if (parts.Count == 0)
        {
            parts.Add($"host:{Environment.MachineName.ToLowerInvariant()}|os:{RuntimeInformation.OSArchitecture}");
        }

        var raw = string.Join("|", parts);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes("karobar-machine-v1|" + raw));
        var hex16 = Convert.ToHexString(hashBytes)[..16].ToUpperInvariant();

        _cachedMachineId = $"{hex16[..4]}-{hex16[4..8]}-{hex16[8..12]}-{hex16[12..16]}";
        return _cachedMachineId;
    }

    private static List<string> CollectWindowsIdentifiers()
    {
        var outParts = new List<string>();

        // 1. Registry HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var cryptoKey = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var guid = NormalizeToken(cryptoKey?.GetValue("MachineGuid")?.ToString());
            if (!string.IsNullOrEmpty(guid))
            {
                outParts.Add("win-guid:" + guid);
            }
        }
        catch
        {
            // Ignore registry read restrictions
        }

        // 2. WMI UUID, BaseBoard SerialNumber, and ProcessorId
        TryAddWmiProperty(outParts, "Win32_ComputerSystemProduct", "UUID", "win-uuid:");
        TryAddWmiProperty(outParts, "Win32_BaseBoard", "SerialNumber", "win-board:");
        TryAddWmiProperty(outParts, "Win32_Processor", "ProcessorId", "win-cpu:");

        return outParts;
    }

    private static void TryAddWmiProperty(List<string> target, string wmiClass, string propertyName, string prefix)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {wmiClass}");
            using var collection = searcher.Get();
            foreach (var item in collection)
            {
                var val = NormalizeToken(item[propertyName]?.ToString());
                if (!string.IsNullOrEmpty(val))
                {
                    target.Add(prefix + val);
                    break;
                }
            }
        }
        catch
        {
            // Ignore WMI unavailable in restricted sandboxes
        }
    }

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
        return InvalidTokens.Contains(trimmed) ? null : trimmed;
    }

    public static string NormalizeMachineId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var hex = Regex.Replace(value.ToUpperInvariant(), "[^A-F0-9]", "");
        if (hex.Length != 16)
            return value.Trim().ToUpperInvariant();

        return $"{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
    }

    public LicenseStatusResult VerifyKey(string rawKey, string? expectedMachineId = null)
    {
        var currentMid = NormalizeMachineId(expectedMachineId ?? ComputeMachineId());
        var key = Regex.Replace(rawKey ?? string.Empty, @"\s+", "");

        if (string.IsNullOrEmpty(key))
            return Invalid("empty", "License key is empty.", currentMid);

        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            return Invalid("format", "License key must start with KRBL.", currentMid);

        var body = key[KeyPrefix.Length..];
        var chunks = body.Split('.');
        if (chunks.Length != 2)
            return Invalid("format", "License key is malformed.", currentMid);

        var sigBytes = Base64UrlDecode(chunks[0]);
        var payloadBytes = Base64UrlDecode(chunks[1]);

        if (sigBytes == null || sigBytes.Length != SignatureLength)
            return Invalid("signature_length", "License signature is corrupted.", currentMid);

        if (payloadBytes == null || payloadBytes.Length == 0)
            return Invalid("payload_encoding", "License payload cannot be decoded.", currentMid);

        var pubKeyBytes = GetPublicKeyBytes();
        if (pubKeyBytes == null)
            return Invalid("no_public_key", "Application public key is not configured.", currentMid);

        // Verify Ed25519 signature over raw payload bytes
        var algorithm = SignatureAlgorithm.Ed25519;
        var publicKey = PublicKey.Import(algorithm, pubKeyBytes, KeyBlobFormat.RawPublicKey);
        if (!algorithm.Verify(publicKey, payloadBytes, sigBytes))
            return Invalid("bad_signature", "License signature verification failed.", currentMid);

        var payloadJson = Encoding.UTF8.GetString(payloadBytes);
        JsonElement payload;
        try
        {
            payload = JsonSerializer.Deserialize<JsonElement>(payloadJson);
            if (payload.ValueKind != JsonValueKind.Object)
                return Invalid("payload_json", "License payload is not valid JSON.", currentMid);
        }
        catch
        {
            return Invalid("payload_json", "License payload is not valid JSON.", currentMid);
        }

        if (payload.TryGetProperty("v", out var vProp) && vProp.TryGetInt32(out var vInt) && vInt != 1)
            return Invalid("unsupported_version", "Unsupported license version.", currentMid);

        if (!payload.TryGetProperty("mid", out var midProp) || string.IsNullOrWhiteSpace(midProp.GetString()))
            return Invalid("missing_mid", "License is missing machine binding.", currentMid);

        var keyMid = NormalizeMachineId(midProp.GetString());
        if (!string.Equals(keyMid, currentMid, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(
                "machine_mismatch",
                $"This license key belongs to machine {keyMid}, not this machine ({currentMid}).",
                currentMid,
                payloadJson);
        }

        if (!payload.TryGetProperty("exp", out var expProp) || !expProp.TryGetInt64(out var expTs) || expTs <= 0)
            return Invalid("missing_exp", "License is missing expiration timestamp.", currentMid, payloadJson);

        var nowTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (payload.TryGetProperty("iat", out var iatProp) && iatProp.TryGetInt64(out var iatTs))
        {
            // Allow 24h tolerance for clock skew
            if (iatTs > nowTs + 86400)
                return Invalid("future_iat", "License issue date is in the future. Check system clock.", currentMid, payloadJson);
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expTs).UtcDateTime;
        DateTime? issuedAt = payload.TryGetProperty("iat", out var iatVal) && iatVal.TryGetInt64(out var iatSec)
            ? DateTimeOffset.FromUnixTimeSeconds(iatSec).UtcDateTime
            : null;

        var customerName = payload.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        var keyId = payload.TryGetProperty("kid", out var kidProp) ? kidProp.GetString() : null;

        if (nowTs >= expTs)
        {
            return new LicenseStatusResult
            {
                Valid = false,
                Status = "expired",
                Reason = $"License expired on {expiresAt:yyyy-MM-dd HH:mm} UTC.",
                MachineId = currentMid,
                CustomerName = customerName,
                LicenseKeyId = keyId,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt,
                RemainingDays = 0,
                RemainingHours = 0,
                RawPayloadJson = payloadJson
            };
        }

        var secondsRemaining = Math.Max(0, expTs - nowTs);
        return new LicenseStatusResult
        {
            Valid = true,
            Status = "valid",
            Reason = null,
            MachineId = currentMid,
            CustomerName = customerName,
            LicenseKeyId = keyId,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            RemainingDays = (int)Math.Floor(secondsRemaining / 86400.0),
            RemainingHours = (int)Math.Floor(secondsRemaining / 3600.0),
            RawPayloadJson = payloadJson
        };
    }

    public async Task<LicenseStatusResult> GetCurrentStatusAsync(CancellationToken cancellationToken = default)
    {
        var mid = ComputeMachineId();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        if (setting == null || string.IsNullOrWhiteSpace(setting.LicenseKey))
        {
            return new LicenseStatusResult
            {
                Valid = false,
                Status = "unlicensed",
                Reason = "No license key has been activated on this machine.",
                MachineId = mid
            };
        }

        // Clock rollback guard
        if (setting.LicenseVerifiedAt.HasValue && DateTime.UtcNow.AddHours(1) < setting.LicenseVerifiedAt.Value)
        {
            return new LicenseStatusResult
            {
                Valid = false,
                Status = "clock_tampered",
                Reason = "System clock appears to have been rolled back. Restore accurate date/time.",
                MachineId = mid
            };
        }

        return VerifyKey(setting.LicenseKey, mid);
    }

    public async Task<LicenseStatusResult> ActivateAsync(string rawKey, CancellationToken cancellationToken = default)
    {
        var result = VerifyKey(rawKey);
        if (!result.Valid)
            return result;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var setting = await db.Settings.FirstOrDefaultAsync(cancellationToken);
        if (setting == null)
        {
            setting = new Domain.Entities.Setting { Id = 1 };
            db.Settings.Add(setting);
        }

        setting.LicenseKey = Regex.Replace(rawKey.Trim(), @"\s+", "");
        setting.LicenseStatus = "valid";
        setting.LicensePayload = result.RawPayloadJson;
        setting.LicenseVerifiedAt = DateTime.UtcNow;
        setting.LicenseExpiresAt = result.ExpiresAt;
        setting.LicenseMachineId = result.MachineId;

        if ((string.IsNullOrWhiteSpace(setting.StoreName) || setting.StoreName == "Karobar App")
            && !string.IsNullOrWhiteSpace(result.CustomerName))
        {
            setting.StoreName = result.CustomerName.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task DeactivateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var setting = await db.Settings.FirstOrDefaultAsync(cancellationToken);
        if (setting != null)
        {
            setting.LicenseKey = null;
            setting.LicenseStatus = "unlicensed";
            setting.LicensePayload = null;
            setting.LicenseVerifiedAt = null;
            setting.LicenseExpiresAt = null;
            setting.LicenseMachineId = null;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ClearLicenseIfMachineChangedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var setting = await db.Settings.FirstOrDefaultAsync(cancellationToken);
        if (setting == null || string.IsNullOrWhiteSpace(setting.LicenseKey))
            return false;

        var status = VerifyKey(setting.LicenseKey);
        if (status.Valid)
            return false;

        if (status.Reason != null && (status.Reason.Contains("machine", StringComparison.OrdinalIgnoreCase) || status.Status != "valid"))
        {
            setting.LicenseKey = null;
            setting.LicenseStatus = "unlicensed";
            setting.LicensePayload = null;
            setting.LicenseVerifiedAt = null;
            setting.LicenseExpiresAt = null;
            setting.LicenseMachineId = null;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    private static byte[]? GetPublicKeyBytes()
    {
        var raw = Base64UrlDecode(DefaultPublicKeyB64);
        if (raw == null || raw.Length != PublicKeyLength)
            return null;

        var shaHex = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        if (!string.Equals(shaHex, DefaultPublicKeySha256, StringComparison.OrdinalIgnoreCase))
            return null;

        return raw;
    }

    public static byte[]? Base64UrlDecode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var s = input.Replace('-', '+').Replace('_', '/');
        var pad = s.Length % 4;
        if (pad > 0)
            s += new string('=', 4 - pad);

        try
        {
            return Convert.FromBase64String(s);
        }
        catch
        {
            return null;
        }
    }

    public static string Base64UrlEncode(ReadOnlySpan<byte> data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static LicenseStatusResult Invalid(string code, string reason, string machineId, string? payloadJson = null) =>
        new()
        {
            Valid = false,
            Status = "invalid",
            Reason = $"{reason} [{code}]",
            MachineId = machineId,
            RawPayloadJson = payloadJson
        };
}
