using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BDFR.PersianCalendar.Core;

/// <summary>
/// Offline, device-bound license format. Signing is performed ONLY by the issuer.
/// Clients require the corresponding public key, never a signing private key.
/// </summary>
public static class ZurvanLicense
{
    public sealed record Payload(string Product, string DeviceCode, string Edition, string? ExpiresUtc);
    public const string ProductId = "zurvan";
    public static string DeviceCode(string platform, string installationId)
    {
        var input = Encoding.UTF8.GetBytes("zurvan-license-device-v1|" + platform.Trim().ToLowerInvariant() + "|" + installationId);
        return "ZRV-" + Convert.ToHexString(SHA256.HashData(input))[..24];
    }

    public static string Issue(Payload payload, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (payload.Product != ProductId || string.IsNullOrWhiteSpace(payload.DeviceCode))
            throw new ArgumentException("Invalid product or device");
        var raw = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = privateKey.SignData(raw, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return "ZRV1." + Base64Url(raw) + "." + Base64Url(signature);
    }

    public static bool TryVerify(string? activationCode, string deviceCode, RSA publicKey, out Payload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(activationCode) || activationCode.Length > 8192)
            return false;
        var parts = activationCode.Split('.');
        if (parts.Length != 3 || parts[0] != "ZRV1") return false;
        try
        {
            var raw = FromBase64Url(parts[1]);
            var sig = FromBase64Url(parts[2]);
            if (!publicKey.VerifyData(raw, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                return false;
            var parsed = JsonSerializer.Deserialize<Payload>(raw);
            if (parsed is null || parsed.Product != ProductId ||
                !string.Equals(parsed.DeviceCode, deviceCode, StringComparison.Ordinal))
                return false;
            if (parsed.ExpiresUtc is not null &&
                (!DateTimeOffset.TryParse(parsed.ExpiresUtc, out var expiry) || expiry <= DateTimeOffset.UtcNow))
                return false;
            payload = parsed;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var value = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '='));
    }
}
