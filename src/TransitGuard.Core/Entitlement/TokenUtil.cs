using System.Security.Cryptography;

namespace TransitGuard.Core.Entitlement;

public enum EntitlementTier { Free, Pro }

/// <summary>Token-/Code-Werkzeuge (ADR-0005): Besitz-Modell — Server speichert NUR SHA-256-Hashes.</summary>
public static class TokenUtil
{
    public static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Neues Entitlement-Token: „et_" + 32 Byte Zufall (base64url).</summary>
    public static (string Token, string TokenHash) NewAccessToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = "et_" + Base64Url(bytes);
        return (token, Sha256Hex(token));
    }

    /// <summary>Restore-Code im Format XXXX-XXXX-XXXX-XXXX (16 Zeichen, base32-ähnlich, verwechselungssicher).</summary>
    public static (string Code, string CodeHash) NewRestoreCode()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";   // ohne I,L,O,0,1
        var bytes = RandomNumberGenerator.GetBytes(16);
        var chars = bytes.Select(b => alphabet[b % alphabet.Length]).ToArray();
        var code = new string(chars).Chunk(4).Select(c => new string(c)).Aggregate((a, b) => a + "-" + b);
        return (code, Sha256Hex(code));
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
