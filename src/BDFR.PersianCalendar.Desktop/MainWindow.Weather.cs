using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private static readonly HttpClient WeatherHttp = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly TextBlock _weatherNow = new();
    private readonly TextBlock _weatherDescription = new();
    private readonly TextBlock _weatherLocation = new();
    private readonly StackPanel _weatherPastDays = new();
    private readonly StackPanel _weatherFutureDays = new();
    private readonly TextBox _weatherCityInput = new();
    private DateTime _weatherLastRequestUtc = DateTime.MinValue;
    private string _weatherLastCity = "";
    private bool _weatherBusy;

    private FrameworkElement BuildWeatherCard()
    {
        var layout = new StackPanel { Spacing = 9, Padding = new Thickness(12) };
        var top = new Grid { ColumnSpacing = 7 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var refresh = new Button { Content = "↻", Padding = new Thickness(10, 4, 10, 4), MinWidth = 34 };
        ToolTipService.SetToolTip(refresh, "به‌روزرسانی آب‌وهوا");
        refresh.Click += (_, _) => _ = LoadWeatherAsync(true);
        Grid.SetColumn(refresh, 1);
        top.Children.Add(refresh);
        layout.Children.Add(top);

        _weatherLocation.FontSize = 11;
        _weatherLocation.Opacity = 0.8;
        _weatherLocation.Text = "آب‌وهوای شهر انتخابی";
        layout.Children.Add(_weatherLocation);
        _weatherNow.FontSize = 32;
        _weatherNow.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _weatherNow.Text = "در حال دریافت…";
        layout.Children.Add(_weatherNow);
        _weatherDescription.FontSize = 11;
        _weatherDescription.TextWrapping = TextWrapping.Wrap;
        layout.Children.Add(_weatherDescription);

        layout.Children.Add(new TextBlock
        {
            Text = "۷ روز گذشته",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush("#FFFFFFFF")
        });
        var pastScroll = CreateWeatherDayScroller(_weatherPastDays);
        layout.Children.Add(pastScroll);
        layout.Children.Add(new TextBlock
        {
            Text = "۷ روز آینده",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush("#FFFFFFFF"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        var futureScroll = CreateWeatherDayScroller(_weatherFutureDays);
        layout.Children.Add(futureScroll);
        layout.Children.Add(new TextBlock
        {
            Text = "داده‌ها: Open-Meteo • دما بر حسب °C",
            FontSize = 10,
            Opacity = 0.6
        });

        var card = new Border
        {
            Background = BrushWithAlpha("#FF173352", 0xF5),
            CornerRadius = new CornerRadius(12),
            Child = layout
        };
        // Keep the weather module independently legible on the pale sidebar.
        _weatherLocation.Foreground = ThemeService.Brush("#FFFFFFFF");
        _weatherNow.Foreground = ThemeService.Brush("#FFFFFFFF");
        _weatherDescription.Foreground = ThemeService.Brush("#FFFFFFFF");
        _ = LoadWeatherAsync(false);
        return card;
    }

    private async Task RefreshWeatherForSettingsAsync()
    {
        _weatherLastCity = "";
        await LoadWeatherAsync(true);
    }

    private async Task LoadWeatherAsync(bool force)
    {
        var city = _settings.WeatherCity?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(city))
        {
            _weatherNow.Text = "شهر را وارد کنید";
            _weatherDescription.Text = "برای نمایش پیش‌بینی، نام شهر را بنویسید.";
            return;
        }
        if (_weatherBusy) return;
        if (!force && city == _weatherLastCity &&
            DateTime.UtcNow - _weatherLastRequestUtc < TimeSpan.FromMinutes(25)) return;
        _weatherBusy = true;
        _weatherNow.Text = "در حال دریافت…";
        _weatherDescription.Text = "در حال اتصال به سرویس هواشناسی";
        _weatherPastDays.Children.Clear();
        _weatherFutureDays.Children.Clear();
        try
        {
            string lat, lon, place;
            // The prayer city already has exact coordinates in the user's settings;
            // use those for the first weather load instead of relying on Persian-name
            // transliteration by a third-party geocoder.
            if (string.IsNullOrWhiteSpace(_settings.WeatherCity) &&
                string.Equals(city, _settings.PrayerCity, StringComparison.OrdinalIgnoreCase))
            {
                lat = _settings.PrayerLatitude.ToString(CultureInfo.InvariantCulture);
                lon = _settings.PrayerLongitude.ToString(CultureInfo.InvariantCulture);
                place = city;
            }
            else
            {
                using var geoResponse = await WeatherHttp.GetAsync(
                    "https://geocoding-api.open-meteo.com/v1/search?name=" +
                    Uri.EscapeDataString(city) + "&count=10&language=en&countryCode=" + Uri.EscapeDataString(_settings.WeatherCountryCode) + "&format=json");
                geoResponse.EnsureSuccessStatusCode();
                using var geo = JsonDocument.Parse(await geoResponse.Content.ReadAsStringAsync());
                if (!geo.RootElement.TryGetProperty("results", out var matches) ||
                    matches.GetArrayLength() == 0)
                {
                    _weatherNow.Text = "شهر پیدا نشد";
                    _weatherDescription.Text = "نام شهر را به فارسی یا انگلیسی تغییر دهید.";
                    return;
                }
                var chosen = matches[0];
                lat = chosen.GetProperty("latitude").GetDouble().ToString(CultureInfo.InvariantCulture);
                lon = chosen.GetProperty("longitude").GetDouble().ToString(CultureInfo.InvariantCulture);
                place = chosen.GetProperty("name").GetString() ?? city;
            }
            using var forecastResponse = await WeatherHttp.GetAsync(
                "https://api.open-meteo.com/v1/forecast?latitude=" + lat +
                "&longitude=" + lon +
                "&current=temperature_2m,relative_humidity_2m,weather_code,is_day,wind_speed_10m" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                "&timezone=auto&past_days=7&forecast_days=8");
            forecastResponse.EnsureSuccessStatusCode();
            using var forecast = JsonDocument.Parse(await forecastResponse.Content.ReadAsStringAsync());
            var root = forecast.RootElement;
            var current = root.GetProperty("current");
            var temp = current.GetProperty("temperature_2m").GetDouble();
            var code = current.GetProperty("weather_code").GetInt32();
            var daytime = current.GetProperty("is_day").GetInt32() == 1;
            var humidity = current.GetProperty("relative_humidity_2m").GetInt32();
            var wind = current.GetProperty("wind_speed_10m").GetDouble();
            _weatherLocation.Text = _settings.WeatherCountryName + " • " + city + " • " + place;
            _weatherNow.Text = WeatherSymbol(code, daytime) + "  " + temp.ToString("0", CultureInfo.InvariantCulture) + "°";
            _weatherDescription.Text = WeatherCondition(code) +
                "  •  رطوبت " + humidity.ToString(CultureInfo.InvariantCulture) +
                "٪  •  باد " + wind.ToString("0", CultureInfo.InvariantCulture) + " km/h";
            var daily = root.GetProperty("daily");
            var dates = daily.GetProperty("time");
            var highs = daily.GetProperty("temperature_2m_max");
            var lows = daily.GetProperty("temperature_2m_min");
            var codes = daily.GetProperty("weather_code");
            var today = DateTime.Today;
            var culture = new CultureInfo("fa-IR");
            for (var i = 0; i < dates.GetArrayLength(); i++)
            {
                var date = DateTime.Parse(dates[i].GetString()!, CultureInfo.InvariantCulture).Date;
                var high = highs[i].GetDouble();
                var low = lows[i].GetDouble();
                var codeDay = codes[i].GetInt32();
                var label = date == today ? "امروز" :
                    date == today.AddDays(-1) ? "دیروز" :
                    date == today.AddDays(1) ? "فردا" :
                    date.ToString("ddd", culture);
                var tile = new StackPanel { Spacing = 4, Width = 48, HorizontalAlignment = HorizontalAlignment.Center };
                tile.Children.Add(new TextBlock {
                    Text = label, FontSize = 10,
                    Foreground = ThemeService.Brush("#FFFFFFFF"), TextAlignment = TextAlignment.Center });
                tile.Children.Add(new TextBlock {
                    Text = date.ToString("MM/dd", CultureInfo.InvariantCulture),
                    FontSize = 9, Opacity = 0.7,
                    Foreground = ThemeService.Brush("#FFFFFFFF"), TextAlignment = TextAlignment.Center });
                tile.Children.Add(new TextBlock {
                    Text = WeatherSymbol(codeDay, true), FontSize = 22,
                    Foreground = ThemeService.Brush("#FFFFFFFF"), TextAlignment = TextAlignment.Center });
                tile.Children.Add(new TextBlock {
                    Text = high.ToString("0", CultureInfo.InvariantCulture) + "°",
                    FontSize = 11, Foreground = ThemeService.Brush("#FFFFFFFF"), TextAlignment = TextAlignment.Center });
                tile.Children.Add(new TextBlock {
                    Text = low.ToString("0", CultureInfo.InvariantCulture) + "°",
                    FontSize = 10, Opacity = 0.75,
                    Foreground = ThemeService.Brush("#FFFFFFFF"), TextAlignment = TextAlignment.Center });
                var target = date < today ? _weatherPastDays : _weatherFutureDays;
                target.Children.Add(new Border {
                    CornerRadius = new CornerRadius(7), Padding = new Thickness(3, 6, 3, 6),
                    Background = BrushWithAlpha("#FFFFFFFF", 0x20), Child = tile });
            }
            _weatherLastCity = city;
            _weatherLastRequestUtc = DateTime.UtcNow;

        }
        catch (Exception ex)
        {
            _weatherNow.Text = "آب‌وهوا در دسترس نیست";
            _weatherDescription.Text = "اتصال اینترنت یا نام شهر را بررسی و دوباره تلاش کنید.";
            StartupDiagnostics.Log("Weather fetch failed: " + ex.Message);
        }
        finally { _weatherBusy = false; }
    }

    private static ScrollViewer CreateWeatherDayScroller(StackPanel days)
    {
        days.Orientation = Orientation.Horizontal;
        days.Spacing = 5;
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = days
        };
    }

    private static string WeatherCondition(int code) => code switch
    {
        0 => "صاف", 1 or 2 => "نیمه‌ابری", 3 => "ابری",
        45 or 48 => "مه‌آلود", 51 or 53 or 55 or 56 or 57 => "نم‌نم باران",
        61 or 63 or 65 or 66 or 67 => "بارانی",
        71 or 73 or 75 or 77 or 85 or 86 => "برفی",
        80 or 81 or 82 => "رگبار", 95 or 96 or 99 => "رعدوبرق",
        _ => "وضعیت متغیر"
    };
    private static string WeatherSymbol(int code, bool day) => code switch
    {
        0 => day ? "☀️" : "🌙",
        1 or 2 => day ? "🌤️" : "☁️", 3 => "☁️",
        45 or 48 => "🌫️", 51 or 53 or 55 or 56 or 57 => "🌦️",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "🌧️",
        71 or 73 or 75 or 77 or 85 or 86 => "🌨️",
        95 or 96 or 99 => "⛈️", _ => "🌥️"
    };
}
