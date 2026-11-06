using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BDFR.PersianCalendar.Core;
using Windows.Security.Credentials;

namespace BDFR.PersianCalendar.Desktop;

internal sealed record ZurvanDeskFile(string Id, string Name, long Size,
    DateTimeOffset ModifiedAtUtc, string ContentType, string ClientObjectId);

/// <summary>Hosted Desk API client. Sessions and Vault Keys live in Windows Credential Locker.</summary>
internal sealed class ZurvanDeskDesktopClient
{
    private const string SessionResource = "Zurvan.Desk.Session.v1";
    private const string VaultResource = "Zurvan.Desk.Vault.v1";
    private const string DefaultBaseUrl = "https://yztithydgdvydaktuxlw.supabase.co/functions/v1/zurvan-desk-api";
    private const string DefaultPublishableKey = "sb_publishable_UAKFYbxgWOCVRDDdv_0JLg_oIIiubmT";
    private const string EncryptedMime = "application/vnd.zurvan.encrypted-file+json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PasswordVault _vault = new();
    private readonly string _baseUrl = (Environment.GetEnvironmentVariable("ZURVAN_DESK_API_BASE_URL") ?? DefaultBaseUrl).TrimEnd('/');
    private readonly string _publishableKey = Environment.GetEnvironmentVariable("ZURVAN_DESK_SUPABASE_PUBLISHABLE_KEY") ?? DefaultPublishableKey;

    internal string? Identifier => ReadSessionCredential()?.UserName;
    internal string? VaultId => ReadSessionValue("vaultId");
    internal bool IsAuthenticated => !string.IsNullOrWhiteSpace(ReadSessionValue("token"));
    private string? Token => ReadSessionValue("token");

    private PasswordCredential? ReadSessionCredential()
    {
        try { return _vault.FindAllByResource(SessionResource).FirstOrDefault(); }
        catch { return null; }
    }

    private string? ReadSessionValue(string name)
    {
        try
        {
            var item = ReadSessionCredential();
            if (item is null) return null;
            item.RetrievePassword();
            using var doc = JsonDocument.Parse(item.Password);
            return doc.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        }
        catch { return null; }
    }

    private void StoreSession(JsonElement json)
    {
        var token = json.GetProperty("token").GetString() ?? throw new InvalidOperationException("توکن حساب دریافت نشد.");
        var identifier = json.GetProperty("identifier").GetString() ?? "";
        var vaultId = json.GetProperty("vaultId").GetString() ?? throw new InvalidOperationException("شناسه Vault دریافت نشد.");
        var payload = JsonSerializer.Serialize(new { token, vaultId }, JsonOptions);
        try { foreach (var old in _vault.FindAllByResource(SessionResource)) _vault.Remove(old); } catch { }
        _vault.Add(new PasswordCredential(SessionResource, identifier, payload));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path,
        HttpContent? content = null, bool authenticated = false)
    {
        using var request = new HttpRequestMessage(method, _baseUrl + path) { Content = content };
        request.Headers.TryAddWithoutValidation("apikey", _publishableKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (authenticated)
        {
            var token = Token ?? throw new InvalidOperationException("ابتدا وارد حساب زروان دسک شوید.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (response.IsSuccessStatusCode) return response;
        var raw = await response.Content.ReadAsStringAsync();
        var message = ParseError(raw, (int)response.StatusCode);
        response.Dispose();
        throw new InvalidOperationException(message);
    }

    private static string ParseError(string raw, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var error))
                return error.GetString() ?? $"سرویس زروان دسک پاسخ {status} داد.";
        }
        catch { }
        return $"سرویس زروان دسک پاسخ {status} داد.";
    }

    internal async Task RegisterAsync(string identifier, string password, string? displayName)
    {
        using var body = JsonContent.Create(new { identifier, password, displayName }, options: JsonOptions);
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/register", body);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        StoreSession(doc.RootElement);
    }

    internal async Task LoginAsync(string identifier, string password)
    {
        using var body = JsonContent.Create(new { identifier, password }, options: JsonOptions);
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/login", body);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        StoreSession(doc.RootElement);
    }

    internal async Task<string> CreateVaultAndRecoveryPhraseAsync()
    {
        var vaultId = VaultId ?? throw new InvalidOperationException("شناسه Vault حساب در دسترس نیست.");
        var phrase = ZurvanRecoveryPhrase.Generate();
        var key = ZurvanCloudCrypto.CreateVaultKey();
        try
        {
            var envelope = ZurvanRecoveryPhrase.WrapVaultKey(phrase, vaultId, key);
            using var body = JsonContent.Create(new
            {
                payload = JsonSerializer.Serialize(envelope, JsonOptions)
            }, options: JsonOptions);
            using var response = await SendAsync(HttpMethod.Put, "/api/v1/recovery", body, true);
            StoreVaultKey(vaultId, key);
            return phrase;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal bool HasLocalVaultKey()
    {
        var vaultId = VaultId;
        if (string.IsNullOrWhiteSpace(vaultId)) return false;
        try
        {
            var item = _vault.Retrieve(VaultResource, vaultId);
            item.RetrievePassword();
            var key = ZurvanCloudCrypto.DecodeRecoveryKey(item.Password);
            try { return key.Length == 32; }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        catch { return false; }
    }

    internal async Task RestoreVaultAsync(string phrase)
    {
        var vaultId = VaultId ?? throw new InvalidOperationException("شناسه Vault حساب در دسترس نیست.");
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/recovery", authenticated: true);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var payload = doc.RootElement.GetProperty("payload").GetString()
            ?? throw new InvalidOperationException("اطلاعات بازیابی دریافت نشد.");
        var envelope = JsonSerializer.Deserialize<ZurvanCloudRecoveryEnvelope>(payload, JsonOptions)
            ?? throw new CryptographicException("Recovery envelope is invalid.");
        var key = ZurvanRecoveryPhrase.UnwrapVaultKey(phrase, envelope);
        try
        {
            if (!string.Equals(envelope.VaultId, vaultId, StringComparison.Ordinal))
                throw new CryptographicException("عبارت بازیابی متعلق به این حساب نیست.");
            StoreVaultKey(vaultId, key);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private void StoreVaultKey(string vaultId, ReadOnlySpan<byte> key)
    {
        var encoded = ZurvanCloudCrypto.EncodeRecoveryKey(key);
        try
        {
            try { _vault.Remove(_vault.Retrieve(VaultResource, vaultId)); } catch { }
            _vault.Add(new PasswordCredential(VaultResource, vaultId, encoded));
        }
        finally { encoded = string.Empty; }
    }

    private byte[] GetVaultKey()
    {
        var vaultId = VaultId ?? throw new InvalidOperationException("شناسه Vault حساب در دسترس نیست.");
        var item = _vault.Retrieve(VaultResource, vaultId);
        item.RetrievePassword();
        return ZurvanCloudCrypto.DecodeRecoveryKey(item.Password);
    }

    internal async Task<IReadOnlyList<ZurvanDeskFile>> ListFilesAsync()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/files", authenticated: true);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var vaultId = VaultId ?? throw new InvalidOperationException("شناسه Vault حساب در دسترس نیست.");
        var key = GetVaultKey();
        try
        {
            var files = new List<ZurvanDeskFile>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? "";
                var encryptedName = item.GetProperty("encryptedName").GetString() ?? "";
                var name = "فایل رمزگذاری‌شده";
                var objectId = "";
                try
                {
                    name = ZurvanDeskCrypto.DecryptName(encryptedName, key, vaultId);
                    using var meta = JsonDocument.Parse(Convert.FromBase64String(encryptedName));
                    objectId = meta.RootElement.GetProperty("objectId").GetString() ?? "";
                }
                catch { }
                files.Add(new ZurvanDeskFile(
                    id, name, item.GetProperty("size").GetInt64(),
                    item.GetProperty("modifiedAtUtc").GetDateTimeOffset(),
                    item.TryGetProperty("contentType", out var ct) ? ct.GetString() ?? EncryptedMime : EncryptedMime,
                    objectId));
            }
            return files;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal async Task UploadFileAsync(string name, string? mimeType, byte[] plaintext)
    {
        var vaultId = VaultId ?? throw new InvalidOperationException("شناسه Vault حساب در دسترس نیست.");
        var key = GetVaultKey();
        byte[]? payload = null;
        try
        {
            var objectId = "desk_" + Guid.NewGuid().ToString("N");
            var encryptedName = ZurvanDeskCrypto.EncryptName(name, key, vaultId, objectId);
            payload = ZurvanDeskCrypto.EncryptFile(plaintext, key, vaultId, objectId, mimeType);
            using var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue(EncryptedMime);
            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/v1/files") { Content = content };
            request.Headers.TryAddWithoutValidation("apikey", _publishableKey);
            request.Headers.TryAddWithoutValidation("X-Zurvan-Encrypted-Name", encryptedName);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                Token ?? throw new InvalidOperationException("ابتدا وارد حساب زروان دسک شوید."));
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(ParseError(await response.Content.ReadAsStringAsync(), (int)response.StatusCode));
        }
        finally
        {
            if (payload is not null) CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal async Task<(byte[] Bytes, string MimeType)> DownloadFileAsync(ZurvanDeskFile file)
    {
        var vaultId = VaultId ?? throw new InvalidOperationException("شناسه Vault حساب در دسترس نیست.");
        var key = GetVaultKey();
        try
        {
            using var response = await SendAsync(HttpMethod.Get,
                "/api/v1/files/" + Uri.EscapeDataString(file.Id), authenticated: true);
            var encrypted = await response.Content.ReadAsByteArrayAsync();
            try
            {
                var bytes = ZurvanDeskCrypto.DecryptFile(encrypted, key, vaultId, out var mime);
                return (bytes, mime);
            }
            finally { CryptographicOperations.ZeroMemory(encrypted); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal async Task RenameFileAsync(ZurvanDeskFile file, string newName)
    {
        if (string.IsNullOrWhiteSpace(file.ClientObjectId))
            throw new InvalidOperationException("نام فایل رمزگشایی نشده است؛ ابتدا Vault را بازیابی کنید.");
        var vaultId = VaultId ?? throw new InvalidOperationException("شناسه Vault حساب در دسترس نیست.");
        var key = GetVaultKey();
        try
        {
            var encryptedName = ZurvanDeskCrypto.EncryptName(newName.Trim(), key, vaultId, file.ClientObjectId);
            using var body = JsonContent.Create(new { encryptedName }, options: JsonOptions);
            using var response = await SendAsync(HttpMethod.Put,
                $"/api/v1/files/{Uri.EscapeDataString(file.Id)}/metadata", body, true);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal async Task DeleteFileAsync(ZurvanDeskFile file)
    {
        using var response = await SendAsync(HttpMethod.Delete,
            "/api/v1/files/" + Uri.EscapeDataString(file.Id), authenticated: true);
    }

    internal async Task LogoutAsync()
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/logout",
                new ByteArrayContent(Array.Empty<byte>()), authenticated: true);
        }
        catch { /* Local sign-out remains available offline. */ }
        try { foreach (var item in _vault.FindAllByResource(SessionResource)) _vault.Remove(item); } catch { }
    }
}
