using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Management;
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

    internal async Task<bool> ValidateReferralIdentifierAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return true;
        code = code.Trim().ToUpperInvariant();
        if (code.Length != 5 || code.Any(x => !(x is >= 'A' and <= 'Z' or >= '0' and <= '9')))
            return false;
        using var reply = await RequestAsync("/functions/v1/zurvan-referral-validate",
            new { code });
        return reply.RootElement.TryGetProperty("valid", out var valid) &&
            valid.ValueKind == JsonValueKind.True;
    }

    internal async Task<bool> SignUpAsync(string email, string password,
        string? referralCode = null)
    {
        if (string.IsNullOrWhiteSpace(email) || password.Length < 8)
            throw new ArgumentException("Use an email and password of at least 8 characters.");
        var code = referralCode?.Trim().ToUpperInvariant() ?? "";
        if (!await ValidateReferralIdentifierAsync(code))
            throw new InvalidOperationException("شناسه معرف معتبر نیست.");
        using var reply = await RequestAsync("/auth/v1/signup",
            string.IsNullOrWhiteSpace(code)
                ? new { email = email.Trim(), password, data = (object?)null }
                : new { email = email.Trim(), password,
                    data = (object?)new { zurvan_referral_code = code } });
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

    internal sealed record LicenseStatus(
        string AccountState, string LicenseAccountState, int ActiveLicenses,
        bool CanWrite, bool TrialAvailable, string Mode, string? ExpiresAt, int DaysRemaining,
        string? AccountUsername, string? SpecialRankCode, string? SpecialRankName, string? SpecialRankNameFa, string? SpecialRankBadgeKey,
        bool PahlaviOccasions);

    private async Task<JsonDocument> LicenseFunctionAsync(string functionName, object body)
    {
        if (!await RefreshAsync())
            throw new InvalidOperationException("برای استعلام لایسنس ابتدا وارد حساب تأییدشده زروان شوید.");
        var session = Load() ?? throw new InvalidOperationException("نشست حساب پیدا نشد.");
        using var request = new HttpRequestMessage(HttpMethod.Post, _url + "/functions/v1/" + functionName)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("apikey", _key);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await Http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            string detail = $"درخواست لایسنس ناموفق بود (HTTP {(int)response.StatusCode}).";
            try
            {
                using var error = JsonDocument.Parse(raw);
                if (error.RootElement.TryGetProperty("error", out var value))
                    detail = value.GetString() switch
                    {
                        "account_not_registered" => "ابتدا ثبت‌نام دستگاه و اتصال حساب زروان را کامل کنید.",
                        "license_account_inactive" => "حساب لایسنس فعال نیست.",
                        "email_not_verified" => "ابتدا ایمیل حساب زروان را تأیید کنید.",
                        "trial_unavailable" => "شروع دوره آزمایشی موقتاً ممکن نیست.",
                        "device_trial_already_used" => "دوره رایگان این دستگاه قبلاً برای یک حساب فعال شده است. برای استفاده با حساب جدید، اشتراک تهیه کنید.",
                        "device_identity_required" => "برای شروع دوره رایگان، شناسه دستگاه الزامی است.",
                        "account_inactive" => "حساب شما غیرفعال است؛ با پشتیبانی تماس بگیرید.",
                        _ => detail
                    };
            }
            catch { }
            throw new InvalidOperationException(detail);
        }
        return JsonDocument.Parse(raw);
    }

    // Use only a validated motherboard serial for a NEW free trial.
    // Do not fall back to BIOS/Windows installation identifiers: reinstalling
    // Windows must not mint another free trial for the same motherboard.
    // This is a deterrent, NOT cryptographic proof of physical hardware.
    private static string GetDeviceTrialFingerprint()
    {
        var boardSerial = ReadHardwareSerial("Win32_BaseBoard", "SerialNumber");
        if (boardSerial is null)
            throw new InvalidOperationException(
                "سریال معتبر مادربرد پیدا نشد؛ صدور دوره رایگان نیازمند بررسی دستگاه توسط پشتیبانی است.");

        var manufacturer = ReadHardwareLabel("Win32_BaseBoard", "Manufacturer") ?? "UNKNOWN";
        var product = ReadHardwareLabel("Win32_BaseBoard", "Product") ?? "UNKNOWN";
        var identity = "motherboard:" + manufacturer + "|" + product + "|" + boardSerial;
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("zurvan-free-trial-device-v1|" + identity)))
            .ToLowerInvariant();
    }

    private static string? ReadHardwareLabel(string wmiClass, string property)
    {
        try
        {
            // Fixed internal WMI class/property names, not user-provided query text.
            using var search = new ManagementObjectSearcher(
                "SELECT " + property + " FROM " + wmiClass);
            using var items = search.Get();
            foreach (ManagementBaseObject item in items)
            {
                using (item)
                {
                    var raw = item[property]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(raw))
                        return raw.ToUpperInvariant();
                }
            }
        }
        catch (Exception) { /* WMI may be disabled or unavailable. */ }
        return null;
    }

    private static string? ReadHardwareSerial(string wmiClass, string property)
    {
        var serial = ReadHardwareLabel(wmiClass, property);
        if (serial is null || serial.Length < 6 || serial.Length > 128)
            return null;
        var compact = new string(serial.Where(char.IsLetterOrDigit).ToArray());
        if (compact.Length < 6 || compact.All(c => c == compact[0]))
            return null;
        // Common placeholders and firmware defaults are not device identifiers.
        string[] invalid = [
            "TOBEFILLEDBYOEM", "DEFAULTSTRING", "SYSTEMSERIALNUMBER",
            "BASEBOARDSERIALNUMBER", "SERIALNUMBER", "NOTAPPLICABLE",
            "NOTSPECIFIED", "NOTAVAILABLE", "UNKNOWN", "NONE",
            "OEM", "INVALID", "UNDEFINED"
        ];
        if (invalid.Any(x => compact == x || compact.Contains("TOBEFILLEDBYOEM")))
            return null;
        return serial;
    }

    internal async Task<JsonDocument> StartTrialAsync()
        => await LicenseFunctionAsync("zurvan-license-start-trial",
            new { key_fingerprint = GetDeviceTrialFingerprint() });

    internal async Task<LicenseStatus> ReadLicenseStatusAsync()
    {
        // Paid licenses can still be checked if Windows cannot expose a
        // device identity; only issuance of a *new free trial* fails closed.
        string? fingerprint;
        try { fingerprint = GetDeviceTrialFingerprint(); }
        catch { fingerprint = null; }
        using var document = await LicenseFunctionAsync("zurvan-license-status",
            new { key_fingerprint = fingerprint });
        var root = document.RootElement;
        string ReadString(string key, string fallback = "") =>
            root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback : fallback;
        bool ReadBool(string key) =>
            root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
        int ReadInt(string key) =>
            root.TryGetProperty(key, out var value) && value.TryGetInt32(out var number) ? number : 0;
        var activeCount = 0;
        if (root.TryGetProperty("licenses", out var licenses) && licenses.ValueKind == JsonValueKind.Array)
            foreach (var item in licenses.EnumerateArray())
                if (item.TryGetProperty("status", out var status) &&
                    string.Equals(status.GetString(), "active", StringComparison.OrdinalIgnoreCase) &&
                    item.TryGetProperty("product_id", out var product) &&
                    string.Equals(product.GetString(), "zurvan", StringComparison.OrdinalIgnoreCase))
                    activeCount++;
        var expiryText = ReadString("expires_at");
        var accountUsername = ReadString("account_username");
        string? rankCode = null, rankName = null, rankNameFa = null, badgeKey = null;
        if (root.TryGetProperty("special_rank", out var rank) && rank.ValueKind == JsonValueKind.Object)
        {
            string? ReadRankString(string key) =>
                rank.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() : null;
            rankCode = ReadRankString("code");
            rankName = ReadRankString("display_name");
            rankNameFa = ReadRankString("display_name_fa");
            badgeKey = ReadRankString("badge_key");
        }
        var pahlaviGrant = root.TryGetProperty("features", out var featureObject)
            && featureObject.ValueKind == JsonValueKind.Object
            && featureObject.TryGetProperty("pahlavi_occasions", out var pahlaviValue)
            && pahlaviValue.ValueKind == JsonValueKind.True;
        return new LicenseStatus(ReadString("account_state", "unknown"),
            ReadString("license_account_state", "unknown"), activeCount,
            ReadBool("can_write"), ReadBool("trial_available"),
            ReadString("mode", "unknown"), string.IsNullOrWhiteSpace(expiryText) ? null : expiryText,
            ReadInt("days_remaining"), string.IsNullOrWhiteSpace(accountUsername) ? null : accountUsername,
            rankCode, rankName, rankNameFa, badgeKey, pahlaviGrant);
    }

    // User points and badges are always fetched from the trusted Supabase
    // function. The device never supplies the awarded XP amount.
    internal Task<JsonDocument> ReadUserProgressAsync(string action = "status", bool? visible = null)
        => LicenseFunctionAsync("zurvan-user-progress",
            visible.HasValue ? new { action, visible = visible.Value } : (object)new { action });

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
