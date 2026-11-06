using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BDFR.PersianCalendar.Desktop;

/// <summary>
/// Implements the exact AES-256-GCM/HMAC-SHA256 envelope used by Android Zurvan Desk.
/// Do not replace this with ZurvanCloudCrypto: that is a different object protocol.
/// </summary>
internal static class ZurvanDeskCrypto
{
    private static readonly byte[] AssociatedData =
        Encoding.UTF8.GetBytes("zurvan-cloud-v1");

    private sealed record EncryptedName(
        string ObjectId,
        string Nonce,
        string Ciphertext,
        string Tag);

    private sealed record EncryptedFile(
        string ObjectId,
        string Nonce,
        string Ciphertext,
        string Tag,
        string Mime);

    internal static string EncryptName(
        string name,
        ReadOnlySpan<byte> vaultKey,
        string vaultId,
        string objectId)
    {
        var encrypted = Encrypt(
            Encoding.UTF8.GetBytes(name),
            vaultKey,
            vaultId,
            objectId + ":name");

        var metadata = new EncryptedName(
            objectId,
            encrypted.Nonce,
            encrypted.Ciphertext,
            encrypted.Tag);

        return Convert.ToBase64String(
            JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions));
    }

    internal static string DecryptName(
        string encryptedName,
        ReadOnlySpan<byte> vaultKey,
        string vaultId)
    {
        var json = Encoding.UTF8.GetString(
            Convert.FromBase64String(encryptedName));
        var metadata = JsonSerializer.Deserialize<EncryptedName>(
            json,
            JsonOptions) ?? throw new CryptographicException(
                "Encrypted filename metadata is invalid.");

        var plaintext = Decrypt(
            metadata.Nonce,
            metadata.Ciphertext,
            metadata.Tag,
            vaultKey,
            vaultId,
            metadata.ObjectId + ":name");

        try
        {
            return new UTF8Encoding(false, true).GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal static byte[] EncryptFile(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> vaultKey,
        string vaultId,
        string objectId,
        string? mimeType)
    {
        var encrypted = Encrypt(
            plaintext,
            vaultKey,
            vaultId,
            objectId);

        var envelope = new EncryptedFile(
            objectId,
            encrypted.Nonce,
            encrypted.Ciphertext,
            encrypted.Tag,
            string.IsNullOrWhiteSpace(mimeType)
                ? "application/octet-stream"
                : mimeType);

        return JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            JsonOptions);
    }

    internal static byte[] DecryptFile(
        ReadOnlySpan<byte> envelopeBytes,
        ReadOnlySpan<byte> vaultKey,
        string vaultId,
        out string mimeType)
    {
        var envelope = JsonSerializer.Deserialize<EncryptedFile>(
            envelopeBytes,
            JsonOptions) ?? throw new CryptographicException(
                "Encrypted file envelope is invalid.");

        mimeType = string.IsNullOrWhiteSpace(envelope.Mime)
            ? "application/octet-stream"
            : envelope.Mime;

        return Decrypt(
            envelope.Nonce,
            envelope.Ciphertext,
            envelope.Tag,
            vaultKey,
            vaultId,
            envelope.ObjectId);
    }

    private static (string Nonce, string Ciphertext, string Tag) Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> vaultKey,
        string vaultId,
        string objectId)
    {
        ValidateKey(vaultKey);
        var objectKey = DeriveObjectKey(vaultKey, vaultId, objectId);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        try
        {
            using var aes = new AesGcm(objectKey, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
            return (
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(tag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(objectKey);
        }
    }

    private static byte[] Decrypt(
        string nonceBase64,
        string ciphertextBase64,
        string tagBase64,
        ReadOnlySpan<byte> vaultKey,
        string vaultId,
        string objectId)
    {
        ValidateKey(vaultKey);
        var objectKey = DeriveObjectKey(vaultKey, vaultId, objectId);
        var nonce = Convert.FromBase64String(nonceBase64);
        var ciphertext = Convert.FromBase64String(ciphertextBase64);
        var tag = Convert.FromBase64String(tagBase64);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(objectKey, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(objectKey);
        }
    }

    private static byte[] DeriveObjectKey(
        ReadOnlySpan<byte> vaultKey,
        string vaultId,
        string objectId)
    {
        using var hmac = new HMACSHA256(vaultKey.ToArray());
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(
            $"zurvan-cloud-v1|{vaultId}|{objectId}"));
    }

    private static void ValidateKey(ReadOnlySpan<byte> vaultKey)
    {
        if (vaultKey.Length != 32)
            throw new CryptographicException(
                "Zurvan Desk Vault Key must contain exactly 32 bytes.");
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
}
