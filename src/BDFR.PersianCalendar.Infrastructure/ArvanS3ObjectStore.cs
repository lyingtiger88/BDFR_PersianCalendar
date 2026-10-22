using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using BDFR.PersianCalendar.Core;

namespace BDFR.PersianCalendar.Infrastructure;

public sealed class ArvanS3ObjectStore : IZurvanCloudObjectStore
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _region;
    private readonly string _bucket;
    private readonly ZurvanCloudCredentials _credentials;

    public ArvanS3ObjectStore(
        HttpClient httpClient,
        ZurvanCloudProfile profile,
        ZurvanCloudCredentials credentials)
    {
        _httpClient = httpClient;
        _endpoint = new Uri(profile.Endpoint.TrimEnd('/') + "/", UriKind.Absolute);
        _region = profile.Region.Trim();
        _bucket = profile.Bucket.Trim();
        _credentials = credentials;

        if (_endpoint.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Zurvan Cloud requires an HTTPS S3 endpoint.");

        if (string.IsNullOrWhiteSpace(_region))
            throw new ArgumentException("S3 region is required.");

        if (string.IsNullOrWhiteSpace(_bucket))
            throw new ArgumentException("S3 bucket is required.");
    }

    public async Task PutAsync(
        string key,
        ReadOnlyMemory<byte> payload,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default)
    {
        using var request = BuildSignedRequest(
            HttpMethod.Put,
            key,
            payload.Span);

        request.Content = new ByteArrayContent(payload.ToArray());
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<byte[]?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        using var request = BuildSignedRequest(
            HttpMethod.Get,
            key,
            ReadOnlySpan<byte>.Empty);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        using var request = BuildSignedRequest(
            HttpMethod.Delete,
            key,
            ReadOnlySpan<byte>.Empty);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        using var request = BuildSignedRequest(
            HttpMethod.Head,
            key,
            ReadOnlySpan<byte>.Empty);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    private HttpRequestMessage BuildSignedRequest(
        HttpMethod method,
        string key,
        ReadOnlySpan<byte> payload)
    {
        var now = DateTimeOffset.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var shortDate = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var payloadHash = Hex(SHA256.HashData(payload));

        var canonicalUri = BuildCanonicalPath(_bucket, key);
        var requestUri = new Uri(
            _endpoint,
            canonicalUri.TrimStart('/'));

        var canonicalHeaders =
            $"host:{requestUri.Host}\n" +
            $"x-amz-content-sha256:{payloadHash}\n" +
            $"x-amz-date:{amzDate}\n";

        const string signedHeaders =
            "host;x-amz-content-sha256;x-amz-date";

        var canonicalRequest =
            $"{method.Method}\n" +
            $"{canonicalUri}\n" +
            "\n" +
            $"{canonicalHeaders}\n" +
            $"{signedHeaders}\n" +
            $"{payloadHash}";

        var credentialScope =
            $"{shortDate}/{_region}/s3/aws4_request";

        var stringToSign =
            "AWS4-HMAC-SHA256\n" +
            $"{amzDate}\n" +
            $"{credentialScope}\n" +
            $"{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";

        var signingKey = DeriveSigningKey(
            _credentials.SecretAccessKey,
            shortDate,
            _region,
            "s3");

        string signature;
        try
        {
            signature = Hex(
                HmacSha256(
                    signingKey,
                    Encoding.UTF8.GetBytes(stringToSign)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signingKey);
        }

        var authorization =
            "AWS4-HMAC-SHA256 " +
            $"Credential={_credentials.AccessKeyId}/{credentialScope}, " +
            $"SignedHeaders={signedHeaders}, " +
            $"Signature={signature}";

        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return request;
    }

    private static string BuildCanonicalPath(
        string bucket,
        string key)
    {
        static string EncodeSegment(string value)
            => Uri.EscapeDataString(value)
                .Replace("%7E", "~", StringComparison.OrdinalIgnoreCase);

        var encodedBucket = EncodeSegment(bucket);
        var encodedKey = string.Join(
            "/",
            (key ?? string.Empty)
                .Trim('/')
                .Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(EncodeSegment));

        return string.IsNullOrEmpty(encodedKey)
            ? $"/{encodedBucket}"
            : $"/{encodedBucket}/{encodedKey}";
    }

    private static byte[] DeriveSigningKey(
        string secret,
        string shortDate,
        string region,
        string service)
    {
        var kDate = HmacSha256(
            Encoding.UTF8.GetBytes("AWS4" + secret),
            Encoding.UTF8.GetBytes(shortDate));
        var kRegion = HmacSha256(kDate, Encoding.UTF8.GetBytes(region));
        var kService = HmacSha256(kRegion, Encoding.UTF8.GetBytes(service));
        var kSigning = HmacSha256(
            kService,
            Encoding.UTF8.GetBytes("aws4_request"));

        CryptographicOperations.ZeroMemory(kDate);
        CryptographicOperations.ZeroMemory(kRegion);
        CryptographicOperations.ZeroMemory(kService);

        return kSigning;
    }

    private static byte[] HmacSha256(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> data)
    {
        using var hmac = new HMACSHA256(key.ToArray());
        return hmac.ComputeHash(data.ToArray());
    }

    private static string Hex(ReadOnlySpan<byte> value)
        => Convert.ToHexString(value).ToLowerInvariant();

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        if (body.Length > 1000)
            body = body[..1000];

        throw new HttpRequestException(
            $"Arvan S3 request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}",
            null,
            response.StatusCode);
    }
}

public sealed class ZurvanCloudClient
{
    private readonly IZurvanCloudObjectStore _objectStore;
    private readonly ZurvanCloudProfile _profile;
    private readonly byte[] _vaultKey;

    public ZurvanCloudClient(
        IZurvanCloudObjectStore objectStore,
        ZurvanCloudProfile profile,
        ReadOnlySpan<byte> vaultKey)
    {
        _objectStore = objectStore;
        _profile = profile;
        _vaultKey = vaultKey.ToArray();

        if (_vaultKey.Length != 32)
            throw new ArgumentException("Zurvan Cloud vault key must be 32 bytes.");
    }

    public async Task<string> UploadEncryptedAsync(
        string entityKind,
        string objectId,
        long revision,
        string originDeviceId,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default)
    {
        var metadata = new ZurvanCloudObjectMetadata(
            ZurvanCloudProtocol.CurrentVersion,
            _profile.VaultId,
            objectId,
            entityKind,
            revision,
            DateTimeOffset.UtcNow,
            originDeviceId);

        var encrypted = ZurvanCloudCrypto.Encrypt(
            plaintext.Span,
            _vaultKey,
            metadata);

        var payload = ZurvanCloudCrypto.SerializeEncrypted(encrypted);
        var key = ZurvanCloudProtocol.BuildObjectKey(
            _profile.Prefix,
            _profile.VaultId,
            objectId);

        await _objectStore.PutAsync(
            key,
            payload,
            "application/vnd.zurvan.cloud+json",
            cancellationToken);

        return key;
    }

    public async Task<byte[]?> DownloadDecryptedAsync(
        string objectId,
        CancellationToken cancellationToken = default)
    {
        var key = ZurvanCloudProtocol.BuildObjectKey(
            _profile.Prefix,
            _profile.VaultId,
            objectId);

        var payload = await _objectStore.GetAsync(
            key,
            cancellationToken);

        if (payload is null)
            return null;

        var encrypted = ZurvanCloudCrypto.DeserializeEncrypted(payload);
        return ZurvanCloudCrypto.Decrypt(encrypted, _vaultKey);
    }

    public async Task DeleteAsync(
        string objectId,
        CancellationToken cancellationToken = default)
    {
        var key = ZurvanCloudProtocol.BuildObjectKey(
            _profile.Prefix,
            _profile.VaultId,
            objectId);

        await _objectStore.DeleteAsync(key, cancellationToken);
    }

    public async Task ProbeRoundTripAsync(
        string originDeviceId,
        CancellationToken cancellationToken = default)
    {
        var objectId = $"probe_{Guid.NewGuid():N}";
        var challenge = RandomNumberGenerator.GetBytes(32);

        try
        {
            await UploadEncryptedAsync(
                "probe",
                objectId,
                1,
                originDeviceId,
                challenge,
                cancellationToken);

            var downloaded = await DownloadDecryptedAsync(
                objectId,
                cancellationToken);

            if (downloaded is null ||
                !CryptographicOperations.FixedTimeEquals(
                    downloaded,
                    challenge))
            {
                throw new CryptographicException(
                    "Zurvan Cloud encrypted round-trip verification failed.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(challenge);

            try
            {
                await DeleteAsync(objectId, cancellationToken);
            }
            catch
            {
                // Connectivity probe should not hide its primary result if
                // cleanup fails; stale probe objects contain only ciphertext.
            }
        }
    }
}
