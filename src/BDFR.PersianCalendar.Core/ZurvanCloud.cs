using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BDFR.PersianCalendar.Core;

public static class ZurvanCloudProtocol
{
    public const int CurrentVersion = 1;
    public const string DefaultProvider = "arvancloud";
    public const string DefaultEndpoint = "https://s3.ir-thr-at1.arvanstorage.ir";
    public const string DefaultRegion = "ir-thr-at1";

    public static string NewVaultId() => $"vault_{Guid.NewGuid():N}";
    public static string NewObjectId() => $"obj_{Guid.NewGuid():N}";

    public static string NormalizePrefix(string? prefix)
    {
        var value = (prefix ?? "zurvan").Trim().Trim('/');
        return string.IsNullOrWhiteSpace(value) ? "zurvan" : value;
    }

    public static string BuildObjectKey(
        string prefix,
        string vaultId,
        string objectId)
        => $"{NormalizePrefix(prefix)}/v{CurrentVersion}/{vaultId}/objects/{objectId}.zcv";
}

public sealed record ZurvanCloudProfile(
    string Provider,
    string Endpoint,
    string Region,
    string Bucket,
    string Prefix,
    string VaultId,
    bool Enabled = false);

public sealed record ZurvanCloudCredentials(
    string AccessKeyId,
    string SecretAccessKey);

public sealed record ZurvanCloudObjectMetadata(
    int ProtocolVersion,
    string VaultId,
    string ObjectId,
    string EntityKind,
    long Revision,
    DateTimeOffset ModifiedAtUtc,
    string OriginDeviceId);

public sealed record ZurvanCloudPlainObject(
    ZurvanCloudObjectMetadata Metadata,
    string PayloadBase64);

public sealed record ZurvanCloudEncryptedObject(
    int ProtocolVersion,
    string VaultId,
    string ObjectId,
    string NonceBase64,
    string CiphertextBase64,
    string TagBase64);

public sealed record ZurvanCloudObjectInfo(
    string Key,
    long Size,
    string? ETag = null,
    DateTimeOffset? LastModifiedUtc = null);

public interface IZurvanCloudObjectStore
{
    Task PutAsync(
        string key,
        ReadOnlyMemory<byte> payload,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default);

    Task<byte[]?> GetAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default);
}

public interface IZurvanCloudSecretStore
{
    Task<string?> GetAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        string name,
        string value,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        string name,
        CancellationToken cancellationToken = default);
}

public static class ZurvanCloudSecretNames
{
    public const string AccessKeyId = "zurvan-cloud-arvan-access-key";
    public const string SecretAccessKey = "zurvan-cloud-arvan-secret-key";
    public const string VaultKey = "zurvan-cloud-vault-key";
}

public static class ZurvanCloudCrypto
{
    public static byte[] CreateVaultKey()
        => RandomNumberGenerator.GetBytes(32);

    public static string EncodeRecoveryKey(ReadOnlySpan<byte> vaultKey)
    {
        ValidateVaultKey(vaultKey);
        return Convert.ToBase64String(vaultKey)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] DecodeRecoveryKey(string recoveryKey)
    {
        var normalized = (recoveryKey ?? string.Empty)
            .Trim()
            .Replace('-', '+')
            .Replace('_', '/');

        normalized = normalized.PadRight(
            normalized.Length + ((4 - normalized.Length % 4) % 4),
            '=');

        var key = Convert.FromBase64String(normalized);
        ValidateVaultKey(key);
        return key;
    }

    public static ZurvanCloudEncryptedObject Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> vaultKey,
        ZurvanCloudObjectMetadata metadata)
    {
        ValidateVaultKey(vaultKey);

        var objectKey = DeriveObjectKey(
            vaultKey,
            metadata.VaultId,
            metadata.ObjectId);

        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plainObject = new ZurvanCloudPlainObject(
                metadata,
                Convert.ToBase64String(plaintext));

            var plainBytes = JsonSerializer.SerializeToUtf8Bytes(plainObject);
            var ciphertext = new byte[plainBytes.Length];
            var tag = new byte[16];
            var aad = BuildAssociatedData(
                metadata.ProtocolVersion,
                metadata.VaultId,
                metadata.ObjectId);

            using var aes = new AesGcm(objectKey, tag.Length);
            aes.Encrypt(
                nonce,
                plainBytes,
                ciphertext,
                tag,
                aad);

            CryptographicOperations.ZeroMemory(plainBytes);

            return new ZurvanCloudEncryptedObject(
                metadata.ProtocolVersion,
                metadata.VaultId,
                metadata.ObjectId,
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(tag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(objectKey);
        }
    }

    public static byte[] Decrypt(
        ZurvanCloudEncryptedObject encrypted,
        ReadOnlySpan<byte> vaultKey)
    {
        ValidateVaultKey(vaultKey);

        if (encrypted.ProtocolVersion != ZurvanCloudProtocol.CurrentVersion)
        {
            throw new NotSupportedException(
                $"Unsupported Zurvan Cloud protocol version: {encrypted.ProtocolVersion}");
        }

        var objectKey = DeriveObjectKey(
            vaultKey,
            encrypted.VaultId,
            encrypted.ObjectId);

        try
        {
            var nonce = Convert.FromBase64String(encrypted.NonceBase64);
            var ciphertext = Convert.FromBase64String(encrypted.CiphertextBase64);
            var tag = Convert.FromBase64String(encrypted.TagBase64);
            var plainBytes = new byte[ciphertext.Length];
            var aad = BuildAssociatedData(
                encrypted.ProtocolVersion,
                encrypted.VaultId,
                encrypted.ObjectId);

            using var aes = new AesGcm(objectKey, tag.Length);
            aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                plainBytes,
                aad);

            try
            {
                var plainObject =
                    JsonSerializer.Deserialize<ZurvanCloudPlainObject>(plainBytes)
                    ?? throw new InvalidOperationException(
                        "Zurvan Cloud decrypted object is empty.");

                if (plainObject.Metadata.ProtocolVersion != encrypted.ProtocolVersion ||
                    !string.Equals(
                        plainObject.Metadata.VaultId,
                        encrypted.VaultId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        plainObject.Metadata.ObjectId,
                        encrypted.ObjectId,
                        StringComparison.Ordinal))
                {
                    throw new CryptographicException(
                        "Zurvan Cloud encrypted header does not match its protected metadata.");
                }

                return Convert.FromBase64String(plainObject.PayloadBase64);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(objectKey);
        }
    }

    public static byte[] SerializeEncrypted(
        ZurvanCloudEncryptedObject encrypted)
        => JsonSerializer.SerializeToUtf8Bytes(encrypted);

    public static ZurvanCloudEncryptedObject DeserializeEncrypted(
        ReadOnlySpan<byte> payload)
        => JsonSerializer.Deserialize<ZurvanCloudEncryptedObject>(payload)
            ?? throw new InvalidOperationException(
                "Zurvan Cloud encrypted object is empty.");

    private static byte[] DeriveObjectKey(
        ReadOnlySpan<byte> vaultKey,
        string vaultId,
        string objectId)
    {
        using var hmac = new HMACSHA256(vaultKey.ToArray());
        return hmac.ComputeHash(
            Encoding.UTF8.GetBytes(
                $"zurvan-cloud-v1|{vaultId}|{objectId}"));
    }

    private static byte[] BuildAssociatedData(
        int protocolVersion,
        string vaultId,
        string objectId)
        => Encoding.UTF8.GetBytes(
            $"zurvan-cloud-v1|{protocolVersion}|{vaultId}|{objectId}");

    private static void ValidateVaultKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32)
            throw new ArgumentException(
                "Zurvan Cloud vault key must be exactly 32 bytes.");
    }
}
