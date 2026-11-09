using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace BDFR.PersianCalendar.Desktop;

internal sealed record WeatherPlace(string Name, string Country, double Latitude, double Longitude, string TimeZone)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Country) ? Name : $"{Name}, {Country}";
}

internal sealed record WeatherHour(DateTime LocalTime, double Temperature, int Code);

internal sealed record WeatherDay(DateOnly Date, int Code, double High, double Low, int PrecipitationChance);

internal sealed record WeatherSnapshot(
    WeatherPlace Place,
    DateTimeOffset FetchedAt,
    double CurrentTemperature,
    int WeatherCode,
    bool IsDay,
    int PrecipitationChance,
    IReadOnlyList<WeatherDay> Days,
    IReadOnlyList<WeatherHour> Hours,
    DateTime ForecastLocalTime);

/// <summary>
/// Weather data from Open-Meteo, with no external API key and no OS location permission.
/// Only user-entered place names and the selected place coordinates are sent.
/// </summary>
internal sealed class ZurvanWeatherService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private WeatherSnapshot? _cached;
    private bool _cachedFahrenheit;

    internal async Task<WeatherPlace> FindCityAsync(string city)
    {
        if (string.IsNullOrWhiteSpace(city) || city.Trim().Length < 2)
            throw new InvalidOperationException("نام شهر باید حداقل دو حرف داشته باشد.");

        var url = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                  Uri.EscapeDataString(city.Trim()) + "&count=5&language=en&format=json";
        using var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        if (!document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            throw new InvalidOperationException("شهری با این نام پیدا نشد. نام انگلیسی شهر را امتحان کنید.");

        var place = results[0];
        return new WeatherPlace(
            place.GetProperty("name").GetString() ?? city.Trim(),
            place.TryGetProperty("country_code", out var country) ? country.GetString() ?? "" : "",
            place.GetProperty("latitude").GetDouble(),
            place.GetProperty("longitude").GetDouble(),
            place.TryGetProperty("timezone", out var timeZone)
                ? timeZone.GetString() ?? "auto" : "auto");
    }

    internal async Task<WeatherSnapshot> GetForecastAsync(WeatherPlace place, bool fahrenheit, bool force = false)
    {
        if (!force && _cached is { } cached &&
            _cachedFahrenheit == fahrenheit &&
            cached.Place.Latitude == place.Latitude &&
            cached.Place.Longitude == place.Longitude &&
            DateTimeOffset.UtcNow - cached.FetchedAt < TimeSpan.FromMinutes(30))
            return cached;

        if (!double.IsFinite(place.Latitude) || !double.IsFinite(place.Longitude) ||
            Math.Abs(place.Latitude) > 90 || Math.Abs(place.Longitude) > 180)
            throw new InvalidOperationException("مختصات شهر معتبر نیست.");

        var unit = fahrenheit ? "fahrenheit" : "celsius";
        var url = "https://api.open-meteo.com/v1/forecast?latitude=" +
            place.Latitude.ToString("R", CultureInfo.InvariantCulture) +
            "&longitude=" + place.Longitude.ToString("R", CultureInfo.InvariantCulture) +
            "&current=temperature_2m,weather_code,is_day&hourly=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
            "&past_days=7&forecast_days=8&temperature_unit=" + unit + "&timezone=auto";

        using var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        var current = root.GetProperty("current");
        var daily = root.GetProperty("daily");
        var dates = daily.GetProperty("time");
        var codes = daily.GetProperty("weather_code");
        var highs = daily.GetProperty("temperature_2m_max");
        var lows = daily.GetProperty("temperature_2m_min");
        var rain = daily.GetProperty("precipitation_probability_max");
        var length = new[] { dates.GetArrayLength(), codes.GetArrayLength(), highs.GetArrayLength(),
            lows.GetArrayLength(), rain.GetArrayLength(), 15 }.Min();
        if (length < 8) throw new InvalidOperationException("اطلاعات روزانه کافی دریافت نشد.");

        var days = new List<WeatherDay>(length);
        for (var i = 0; i < length; i++)
        {
            if (!DateOnly.TryParseExact(dates[i].GetString(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                throw new InvalidOperationException("تاریخ پیش‌بینی نامعتبر است.");
            days.Add(new WeatherDay(day, codes[i].GetInt32(), highs[i].GetDouble(),
                lows[i].GetDouble(), rain[i].ValueKind == JsonValueKind.Number ? rain[i].GetInt32() : 0));
        }
        var hourly = root.GetProperty("hourly");
        var hourlyTimes = hourly.GetProperty("time");
        var hourlyTemperatures = hourly.GetProperty("temperature_2m");
        var hourlyCodes = hourly.GetProperty("weather_code");
        var hourCount = Math.Min(hourlyTimes.GetArrayLength(),
            Math.Min(hourlyTemperatures.GetArrayLength(), hourlyCodes.GetArrayLength()));
        var hours = new List<WeatherHour>();
        for (var index = 0; index < hourCount; index++)
        {
            if (DateTime.TryParseExact(hourlyTimes[index].GetString(), "yyyy-MM-dd'T'HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime))
                hours.Add(new WeatherHour(localTime, hourlyTemperatures[index].GetDouble(),
                    hourlyCodes[index].GetInt32()));
        }
        var snapshot = new WeatherSnapshot(place, DateTimeOffset.UtcNow,
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("weather_code").GetInt32(),
            current.GetProperty("is_day").GetInt32() == 1,
            days.FirstOrDefault(d => d.Date == DateOnly.FromDateTime(DateTime.UtcNow.AddSeconds(root.GetProperty("utc_offset_seconds").GetInt32())))?.PrecipitationChance ?? 0, days, hours,
            DateTime.UtcNow.AddSeconds(root.GetProperty("utc_offset_seconds").GetInt32()));
        _cached = snapshot;
        _cachedFahrenheit = fahrenheit;
        return snapshot;
    }

    internal static (string Icon, string Description) Describe(int code, bool day = true) => code switch
    {
        0 => (day ? "☀️" : "🌙", "آسمان صاف"),
        1 or 2 => (day ? "🌤️" : "☁️", "کمی ابری"),
        3 => ("☁️", "ابری"),
        45 or 48 => ("🌫️", "مه‌آلود"),
        51 or 53 or 55 or 56 or 57 => ("🌦️", "نم‌نم باران"),
        61 or 63 or 65 or 66 or 67 => ("🌧️", "بارانی"),
        71 or 73 or 75 or 77 => ("🌨️", "برفی"),
        80 or 81 or 82 => ("🌦️", "رگبار"),
        85 or 86 => ("❄️", "رگبار برف"),
        95 or 96 or 99 => ("⛈️", "رعدوبرق"),
        _ => ("🌡️", "وضعیت نامشخص")
    };
}
