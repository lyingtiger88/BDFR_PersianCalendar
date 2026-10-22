using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BDFR.PersianCalendar.Core;

public static class CompanionProtocol
{
    public const int CurrentVersion = 1;
    public static readonly TimeSpan DefaultPairingLifetime = TimeSpan.FromMinutes(5);

    public static string NewDeviceId() => $"dev_{Guid.NewGuid():N}";
    public static string NewSessionId() => $"pair_{Guid.NewGuid():N}";
    public static string NewBatchId() => $"sync_{Guid.NewGuid():N}";

    public static string CreateSixDigitCode()
        => RandomNumberGenerator.GetInt32(0, 1_000_000)
            .ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public static string EncodePairingPayload(CompanionPairingOffer offer)
    {
        var json = JsonSerializer.Serialize(offer);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static CompanionPairingOffer DecodePairingPayload(string payload)
    {
        var normalized = payload.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(
            normalized.Length + ((4 - normalized.Length % 4) % 4),
            '=');

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
        return JsonSerializer.Deserialize<CompanionPairingOffer>(json)
            ?? throw new InvalidOperationException("Pairing payload is empty.");
    }
}

public sealed record EncryptedCompanionFrame(
    string NonceBase64,
    string CiphertextBase64,
    string TagBase64);

public sealed class CompanionEphemeralKey : IDisposable
{
    private static readonly byte[] AssociatedData =
        Encoding.UTF8.GetBytes("zurvan-companion-v1");

    private readonly ECDiffieHellman _key =
        ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public string PublicKeyBase64 =>
        Convert.ToBase64String(ExportX963PublicKey());

    public string PublicKeyFingerprint
    {
        get
        {
            var hash = SHA256.HashData(ExportX963PublicKey());
            return Convert.ToHexString(hash.AsSpan(0, 8));
        }
    }

    public byte[] DeriveSessionKey(
        string peerPublicKeyBase64,
        string sessionId)
    {
        var peerBytes = Convert.FromBase64String(peerPublicKeyBase64);
        if (peerBytes.Length != 65 || peerBytes[0] != 0x04)
            throw new CryptographicException(
                "Companion public key must be a 65-byte P-256 X9.63 point.");

        using var peer = ECDiffieHellman.Create();
        peer.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = peerBytes.AsSpan(1, 32).ToArray(),
                Y = peerBytes.AsSpan(33, 32).ToArray()
            }
        });

        var sharedSecret = _key.DeriveRawSecretAgreement(peer.PublicKey);

        try
        {
            using var hmac = new HMACSHA256(sharedSecret);
            return hmac.ComputeHash(
                Encoding.UTF8.GetBytes(
                    $"zurvan-companion-v1|{sessionId}"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    public static string ComputeSixDigitSas(
        ReadOnlySpan<byte> sessionKey)
    {
        var hash = SHA256.HashData(sessionKey);

        var value =
            ((uint)hash[0] << 24) |
            ((uint)hash[1] << 16) |
            ((uint)hash[2] << 8) |
            hash[3];

        return (value % 1_000_000)
            .ToString(
                "D6",
                System.Globalization.CultureInfo.InvariantCulture);
    }

    public static EncryptedCompanionFrame EncryptJson<T>(
        T value,
        ReadOnlySpan<byte> sessionKey)
    {
        if (sessionKey.Length != 32)
            throw new ArgumentException(
                "Companion session key must be 32 bytes.",
                nameof(sessionKey));

        var plaintext = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(sessionKey, tag.Length);
        aes.Encrypt(
            nonce,
            plaintext,
            ciphertext,
            tag,
            AssociatedData);

        CryptographicOperations.ZeroMemory(plaintext);

        return new EncryptedCompanionFrame(
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(tag));
    }

    public static T DecryptJson<T>(
        EncryptedCompanionFrame frame,
        ReadOnlySpan<byte> sessionKey)
    {
        if (sessionKey.Length != 32)
            throw new ArgumentException(
                "Companion session key must be 32 bytes.",
                nameof(sessionKey));

        var nonce = Convert.FromBase64String(frame.NonceBase64);
        var ciphertext = Convert.FromBase64String(frame.CiphertextBase64);
        var tag = Convert.FromBase64String(frame.TagBase64);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(sessionKey, tag.Length);
        aes.Decrypt(
            nonce,
            ciphertext,
            tag,
            plaintext,
            AssociatedData);

        try
        {
            return JsonSerializer.Deserialize<T>(plaintext)
                ?? throw new InvalidOperationException(
                    "Decrypted companion payload is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private byte[] ExportX963PublicKey()
    {
        var parameters = _key.ExportParameters(false);
        var x = parameters.Q.X
            ?? throw new CryptographicException("Missing P-256 X coordinate.");
        var y = parameters.Q.Y
            ?? throw new CryptographicException("Missing P-256 Y coordinate.");

        if (x.Length != 32 || y.Length != 32)
            throw new CryptographicException(
                "Unexpected P-256 public-key coordinate size.");

        var output = new byte[65];
        output[0] = 0x04;
        x.CopyTo(output, 1);
        y.CopyTo(output, 33);
        return output;
    }

    public void Dispose() => _key.Dispose();
}

public enum CompanionPlatform
{
    Windows,
    Android,
    IOS
}

public enum CompanionTrustState
{
    Pending,
    Trusted,
    Revoked
}

public enum SyncEntityKind
{
    Event,
    Task,
    Note,
    SpecialOccasion,
    Reminder,
    Setting,
    OccasionPictureMapping
}

public enum SyncMutationKind
{
    Upsert,
    Delete
}

public enum SyncConflictResolution
{
    ApplyIncoming,
    KeepLocal,
    CreateConflictCopy
}

public sealed record CompanionDevice(
    string DeviceId,
    string DisplayName,
    CompanionPlatform Platform,
    CompanionTrustState TrustState,
    DateTimeOffset PairedAtUtc,
    DateTimeOffset? LastSeenUtc = null,
    string? PublicKeyFingerprint = null);

public sealed record CompanionPairingOffer(
    int ProtocolVersion,
    string SessionId,
    string HostDeviceId,
    string HostDisplayName,
    CompanionPlatform HostPlatform,
    string Endpoint,
    string PairingCode,
    DateTimeOffset ExpiresAtUtc,
    string? HostPublicKey = null);

public sealed record CompanionPairingJoin(
    int ProtocolVersion,
    string SessionId,
    string DeviceId,
    string DeviceDisplayName,
    CompanionPlatform Platform,
    string PairingCode,
    string? DevicePublicKey = null);

public sealed record CompanionPairingResult(
    bool Accepted,
    string DeviceId,
    string? TrustedDeviceId,
    DateTimeOffset CompletedAtUtc,
    string? Error = null);

public sealed record SyncEnvelope(
    string EnvelopeId,
    SyncEntityKind EntityKind,
    string EntityId,
    SyncMutationKind Mutation,
    long Revision,
    DateTimeOffset ModifiedAtUtc,
    string OriginDeviceId,
    string? PayloadJson,
    string? ContentHash = null);

public sealed record SyncBatch(
    int ProtocolVersion,
    string BatchId,
    string SourceDeviceId,
    string TargetDeviceId,
    string? SinceCheckpoint,
    IReadOnlyList<SyncEnvelope> Items,
    DateTimeOffset CreatedAtUtc);

public sealed record SyncConflict(
    string EntityId,
    SyncEntityKind EntityKind,
    SyncEnvelope Local,
    SyncEnvelope Incoming,
    SyncConflictResolution Resolution,
    string Reason);

public sealed record SyncAck(
    int ProtocolVersion,
    string BatchId,
    string ReceiverDeviceId,
    string Checkpoint,
    IReadOnlyList<string> AppliedEnvelopeIds,
    IReadOnlyList<SyncConflict> Conflicts,
    DateTimeOffset AppliedAtUtc);

public static class SyncMergePolicy
{
    public static SyncConflictResolution Decide(
        SyncEnvelope local,
        SyncEnvelope incoming)
    {
        if (!string.Equals(local.EntityId, incoming.EntityId, StringComparison.Ordinal) ||
            local.EntityKind != incoming.EntityKind)
        {
            throw new ArgumentException(
                "Sync envelopes must refer to the same logical entity.");
        }

        if (incoming.Revision > local.Revision)
            return SyncConflictResolution.ApplyIncoming;

        if (incoming.Revision < local.Revision)
            return SyncConflictResolution.KeepLocal;

        if (incoming.ModifiedAtUtc > local.ModifiedAtUtc)
            return SyncConflictResolution.ApplyIncoming;

        if (incoming.ModifiedAtUtc < local.ModifiedAtUtc)
            return SyncConflictResolution.KeepLocal;

        if (incoming.Mutation == SyncMutationKind.Delete &&
            local.Mutation != SyncMutationKind.Delete)
        {
            return SyncConflictResolution.ApplyIncoming;
        }

        if (local.Mutation == SyncMutationKind.Delete &&
            incoming.Mutation != SyncMutationKind.Delete)
        {
            return SyncConflictResolution.KeepLocal;
        }

        if (string.Equals(
                local.ContentHash,
                incoming.ContentHash,
                StringComparison.Ordinal))
        {
            return SyncConflictResolution.KeepLocal;
        }

        // Same revision and timestamp but different payload means both devices
        // edited independently. Preserve both rather than silently losing data.
        return SyncConflictResolution.CreateConflictCopy;
    }
}

public interface ICompanionDeviceRegistry
{
    Task<IReadOnlyList<CompanionDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default);

    Task UpsertDeviceAsync(
        CompanionDevice device,
        CancellationToken cancellationToken = default);

    Task RevokeDeviceAsync(
        string deviceId,
        CancellationToken cancellationToken = default);
}

public interface ICompanionSyncStore
{
    Task<IReadOnlyList<SyncEnvelope>> GetChangesAsync(
        string? sinceCheckpoint,
        int limit = 500,
        CancellationToken cancellationToken = default);

    Task<SyncAck> ApplyBatchAsync(
        SyncBatch batch,
        CancellationToken cancellationToken = default);
}

public interface ICompanionTransport
{
    Task<CompanionPairingResult> PairAsync(
        CompanionPairingOffer offer,
        CompanionPairingJoin join,
        CancellationToken cancellationToken = default);

    Task<SyncAck> PushAsync(
        SyncBatch batch,
        CancellationToken cancellationToken = default);
}
