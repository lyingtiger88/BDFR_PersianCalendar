using System.Net.Http.Json;
using System.Text.Json;
using Windows.Security.Credentials;

namespace BDFR.PersianCalendar.Desktop;

/// <summary>Zurvan Desk account API. Separate from Supabase licensing and Supabase account auth.</summary>
internal sealed class ZurvanDeskDesktopClient
{
    private const string Resource = "Zurvan.Desk.Session.v1";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly PasswordVault _vault = new();
    private readonly string _baseUrl = (Environment.GetEnvironmentVariable("ZURVAN_DESK_API_BASE_URL") ?? "http://127.0.0.1:5088").TrimEnd('/');

    internal string? Identifier
    {
        get
        {
            try { return _vault.FindAllByResource(Resource).FirstOrDefault()?.UserName; }
            catch { return null; }
        }
    }

    private string? Token
    {
        get
        {
            try
            {
                var item = _vault.FindAllByResource(Resource).FirstOrDefault();
                if (item is null) return null;
                item.RetrievePassword();
                using var doc = JsonDocument.Parse(item.Password);
                return doc.RootElement.GetProperty("token").GetString();
            }
            catch { return null; }
        }
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body = null, bool authenticated = false)
    {
        using var request = new HttpRequestMessage(method, _baseUrl + path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (authenticated)
        {
            var token = Token ?? throw new InvalidOperationException("ابتدا وارد حساب زروان دسک شوید.");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        using var response = await Http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var message = $"سرویس زروان دسک پاسخ {((int)response.StatusCode)} داد.";
            try { using var error = JsonDocument.Parse(raw); if (error.RootElement.TryGetProperty("error", out var e)) message = e.GetString() ?? message; } catch { }
            throw new InvalidOperationException(message);
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
    }

    private void Store(JsonElement json)
    {
        var token = json.GetProperty("token").GetString() ?? throw new InvalidOperationException("توکن حساب دریافت نشد.");
        var identifier = json.GetProperty("identifier").GetString() ?? "";
        var payload = JsonSerializer.Serialize(new { token, vaultId = json.TryGetProperty("vaultId", out var v) ? v.GetString() : null });
        try { foreach (var old in _vault.FindAllByResource(Resource)) _vault.Remove(old); } catch { }
        _vault.Add(new PasswordCredential(Resource, identifier, payload));
    }

    internal async Task RegisterAsync(string identifier, string password, string? displayName)
    {
        using var result = await SendAsync(HttpMethod.Post, "/api/v1/auth/register", new { identifier, password, displayName });
        Store(result.RootElement);
    }

    internal async Task LoginAsync(string identifier, string password)
    {
        using var result = await SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { identifier, password });
        Store(result.RootElement);
    }

    internal async Task<JsonDocument> ListFilesAsync() => await SendAsync(HttpMethod.Get, "/api/v1/files", authenticated: true);

    internal async Task LogoutAsync()
    {
        try { using var _ = await SendAsync(HttpMethod.Post, "/api/v1/auth/logout", new { }, authenticated: true); } catch { }
        try { foreach (var item in _vault.FindAllByResource(Resource)) _vault.Remove(item); } catch { }
    }
}
