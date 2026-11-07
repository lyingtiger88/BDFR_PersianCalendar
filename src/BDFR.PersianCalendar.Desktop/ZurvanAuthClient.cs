using System.Net.Http.Json;
using System.Text.Json;
using Windows.Security.Credentials;

namespace BDFR.PersianCalendar.Desktop;

/// <summary>Supabase account auth only. Does not activate a license.</summary>
internal sealed class ZurvanAuthClient
{
    private const string VaultResource = "Zurvan.Supabase.Auth.v1";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _url = (Environment.GetEnvironmentVariable("ZURVAN_SUPABASE_URL") ?? "https://yztithydgdvydaktuxlw.supabase.co").TrimEnd('/');
    private readonly string _key = Environment.GetEnvironmentVariable("ZURVAN_SUPABASE_PUBLISHABLE_KEY") ?? "sb_publishable_UAKFYbxgWOCVRDDdv_0JLg_oIIiubmT";
    private readonly PasswordVault _vault = new();

    internal bool Configured => _url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(_key);

    internal string? SignedInEmail
    {
        get
        {
            try
            {
                var value = _vault.FindAllByResource(VaultResource);
                return value.Count > 0 ? value[0].UserName : null;
            }
            catch { return null; }
        }
    }

    private void Store(string email, JsonElement reply)
    {
        var access = reply.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Missing session");
        var refresh = reply.GetProperty("refresh_token").GetString() ?? throw new InvalidOperationException("Missing refresh token");
        var expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() +
            (reply.TryGetProperty("expires_in", out var lifespan) ? lifespan.GetInt64() : 3600);
        Clear();
        _vault.Add(new PasswordCredential(VaultResource, email,
            JsonSerializer.Serialize(new Session(access, refresh, expires))));
    }

    private sealed record Session(string AccessToken, string RefreshToken, long ExpiresAt);

    private void Clear()
    {
        try { foreach (var entry in _vault.FindAllByResource(VaultResource)) _vault.Remove(entry); }
        catch { /* Nothing stored. */ }
    }

    private Session? Load()
    {
        try
        {
            var entry = _vault.FindAllByResource(VaultResource).FirstOrDefault();
            if (entry is null) return null;
            entry.RetrievePassword();
            return JsonSerializer.Deserialize<Session>(entry.Password);
        }
        catch { return null; }
    }

    private async Task<JsonDocument> RequestAsync(string path, object data, string? access = null)
    {
        if (!Configured) throw new InvalidOperationException("Supabase has not been configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, _url + path)
        {
            Content = JsonContent.Create(data)
        };
        request.Headers.TryAddWithoutValidation("apikey", _key);
        if (!string.IsNullOrEmpty(access))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            string detail = "Authentication request failed.";
            try
            {
                using var error = JsonDocument.Parse(body);
                if (error.RootElement.TryGetProperty("msg", out var msg)) detail = msg.GetString() ?? detail;
                else if (error.RootElement.TryGetProperty("error_description", out var description))
                    detail = description.GetString() ?? detail;
            }
            catch { /* Do not include untrusted raw response in UI. */ }
            throw new InvalidOperationException(detail);
        }
        return JsonDocument.Parse(body);
    }

    internal async Task SignInAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Email and password are required.");
        using var reply = await RequestAsync("/auth/v1/token?grant_type=password",
            new { email = email.Trim(), password });
        Store(email.Trim(), reply.RootElement);
    }

    internal async Task<bool> SignUpAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || password.Length < 8)
            throw new ArgumentException("Use an email and password of at least 8 characters.");
        using var reply = await RequestAsync("/auth/v1/signup",
            new { email = email.Trim(), password });
        if (!reply.RootElement.TryGetProperty("access_token", out _)) return false;
        Store(email.Trim(), reply.RootElement);
        return true;
    }

    internal async Task SendPasswordRecoveryAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required.");
        using var _ = await RequestAsync("/auth/v1/recover", new { email = email.Trim() });
    }

    internal async Task<bool> RefreshAsync()
    {
        var session = Load();
        if (session is null) return false;
        if (session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60) return true;
        try
        {
            using var reply = await RequestAsync("/auth/v1/token?grant_type=refresh_token",
                new { refresh_token = session.RefreshToken });
            Store(SignedInEmail ?? "", reply.RootElement);
            return true;
        }
        catch { return false; }
    }

    internal async Task<(string AccountState, string LicenseAccountState, int ActiveLicenses)> ReadLicenseStatusAsync()
    {
        if (!await RefreshAsync())
            throw new InvalidOperationException("نشست حساب زروان معتبر نیست؛ دوباره وارد شوید.");
        var session = Load()
            ?? throw new InvalidOperationException("نشست حساب پیدا نشد.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post, _url + "/functions/v1/zurvan-license-status")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.TryAddWithoutValidation("apikey", _key);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", session.AccessToken);
        using var response = await Http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "برای استعلام لایسنس، ابتدا ایمیل حساب را تأیید کنید."
                    : $"استعلام وضعیت مجوز ناموفق بود (HTTP {(int)response.StatusCode}).");
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        var accountState = root.TryGetProperty("account_state", out var state)
            ? state.GetString() ?? "unknown" : "unknown";
        var licenseState = root.TryGetProperty("license_account_state", out var license)
            ? license.GetString() ?? "unknown" : "unknown";
        var activeCount = 0;
        if (root.TryGetProperty("licenses", out var licenses) && licenses.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in licenses.EnumerateArray())
            {
                if (item.TryGetProperty("status", out var status) &&
                    string.Equals(status.GetString(), "active", StringComparison.OrdinalIgnoreCase) &&
                    item.TryGetProperty("product_id", out var product) &&
                    string.Equals(product.GetString(), "zurvan", StringComparison.OrdinalIgnoreCase))
                    activeCount++;
            }
        }
        return (accountState, licenseState, activeCount);
    }

    internal async Task SignOutAsync()
    {
        var session = Load();
        try
        {
            if (session is not null && Configured)
            {
                using var _ = await RequestAsync("/auth/v1/logout", new { }, session.AccessToken);
            }
        }
        catch { /* Local logout must be available even when offline. */ }
        finally { Clear(); }
    }
}
