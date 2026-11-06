using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BDFR.PersianCalendar.Core;

namespace BDFR.PersianCalendar.Desktop;

internal sealed record CelebrationEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("account_id")] string AccountId,
    [property: JsonPropertyName("occasion_type")] string OccasionType,
    [property: JsonPropertyName("persian_month")] int Month,
    [property: JsonPropertyName("persian_day")] int Day,
    [property: JsonPropertyName("theme_id")] string ThemeId,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("display_title")] string? Title);

internal sealed record CelebrationAccount(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username);

internal sealed record CelebrationDirectory(
    [property: JsonPropertyName("is_creator")] bool IsCreator,
    [property: JsonPropertyName("account_id")] string CurrentAccountId,
    [property: JsonPropertyName("accounts")] List<CelebrationAccount>? Accounts,
    [property: JsonPropertyName("schedules")] List<CelebrationEntry>? Schedules);

internal sealed class ZurvanCelebrationClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ZurvanAuthClient _auth = new();
    private readonly string _url =
        (Environment.GetEnvironmentVariable("ZURVAN_SUPABASE_URL") ?? "").TrimEnd('/') +
        "/functions/v1/zurvan-celebration-schedules";
    private readonly string _key =
        Environment.GetEnvironmentVariable("ZURVAN_SUPABASE_PUBLISHABLE_KEY") ?? "";

    internal bool Configured => _auth.Configured && !string.IsNullOrWhiteSpace(_key);

    internal async Task<CelebrationDirectory> LoadAsync()
    {
        using var request = await CreateRequestAsync(HttpMethod.Get);
        using var response = await Http.SendAsync(request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<CelebrationDirectory>()
            ?? throw new InvalidOperationException("Empty celebration response");
    }

    internal async Task SaveAsync(string accountId, string type, int month, int day,
        string themeId, string? title, bool enabled)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post);
        request.Content = JsonContent.Create(new
        {
            account_id = accountId, occasion_type = type,
            persian_month = month, persian_day = day,
            theme_id = themeId, display_title = title, enabled
        });
        using var response = await Http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    internal async Task DeleteAsync(string id)
    {
        using var request = await CreateRequestAsync(HttpMethod.Delete,
            "?id=" + Uri.EscapeDataString(id));
        using var response = await Http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string suffix = "")
    {
        if (!Configured) throw new InvalidOperationException("Supabase is not configured.");
        var jwt = await _auth.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(jwt))
            throw new InvalidOperationException("Sign in with your Supabase account first.");

        var request = new HttpRequestMessage(method, _url + suffix);
        request.Headers.TryAddWithoutValidation("apikey", _key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var reason = $"Celebration service HTTP {(int)response.StatusCode}";
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.TryGetProperty("error", out var error))
                reason = error.GetString() ?? reason;
        }
        catch { }
        throw new InvalidOperationException(reason);
    }

    internal static CelebrationEntry? Today(CelebrationDirectory data)
    {
        var today = PersianDate.Today();
        return data.Schedules?.FirstOrDefault(x => x.Enabled &&
            x.AccountId == data.CurrentAccountId &&
            x.Month == today.Month && x.Day == today.Day &&
            AppearanceCatalog.Themes.Any(theme => theme.Id == x.ThemeId));
    }
}
