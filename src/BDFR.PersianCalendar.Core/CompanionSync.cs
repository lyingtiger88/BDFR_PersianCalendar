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
