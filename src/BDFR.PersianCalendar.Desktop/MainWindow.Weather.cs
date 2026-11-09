using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.Foundation;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private readonly ZurvanWeatherService _weatherService = new();
    private FrameworkElement? _weatherCalendarHost;
    private TextBlock? _weatherLocationText;
    private TextBlock? _weatherTemperatureText;
    private TextBlock? _weatherConditionText;
    private TextBlock? _weatherRainText;
    private TextBlock? _weatherSymbolText;
    private TextBlock? _weatherStatusText;
    private Grid? _weatherForecastGrid;
    private StackPanel? _weatherHourlyStrip;
    private readonly Dictionary<DateOnly, TextBlock> _weatherDayBadges = new();
    private Button? _weatherFullForecastButton;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _weatherRefreshTimer;
    private WeatherSnapshot? _weatherSnapshot;
    private bool _weatherLoading;
    private bool _weatherRefreshPending;
    private long _weatherRequestVersion;

    private static SolidColorBrush WeatherBrush(byte r, byte g, byte b, byte alpha = 255)
        => new(Windows.UI.Color.FromArgb(alpha, r, g, b));

    private WeatherPlace SelectedWeatherPlace() => new(
        _settings.WeatherCity,
        _settings.WeatherCountryCode,
        _settings.WeatherLatitude,
        _settings.WeatherLongitude,
        "auto");

    private FrameworkElement BuildWeatherCard()
    {
        var background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0.9, 1),
            GradientStops =
            {
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 9, 30, 63), Offset = 0 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 16, 42, 75), Offset = 0.55 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 12, 25, 48), Offset = 1 }
            }
        };

        var panel = new StackPanel
        {
            Spacing = 10,
            Padding = new Thickness(11, 10, 11, 10),
            FlowDirection = FlowDirection.LeftToRight
        };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(new TextBlock
        {
            Text = "🌤️",
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        _weatherLocationText = new TextBlock
        {
            Text = SelectedWeatherPlace().DisplayName,
            Foreground = WeatherBrush(235, 244, 255),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        Grid.SetColumn(_weatherLocationText, 1);
        header.Children.Add(_weatherLocationText);

        var options = new Button
        {
            Content = "⋯",
            FontSize = 19,
            Padding = new Thickness(6, 0, 6, 3),
            MinWidth = 28,
            MinHeight = 27,
            Background = WeatherBrush(255, 255, 255, 15),
            BorderThickness = new Thickness(0),
            Foreground = WeatherBrush(245, 248, 255)
        };
        ToolTipService.SetToolTip(options, "تغییر شهر و واحد دما");
        options.Click += async (_, _) => await ShowWeatherOptionsAsync();
        Grid.SetColumn(options, 2);
        header.Children.Add(options);
        panel.Children.Add(header);

        var currentRow = new Grid { MinHeight = 76 };
        currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        currentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var mainTemp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        _weatherSymbolText = new TextBlock
        {
            Text = "☾", FontSize = 39, Foreground = WeatherBrush(255, 221, 79),
            VerticalAlignment = VerticalAlignment.Center
        };
        _weatherTemperatureText = new TextBlock
        {
            Text = "—°", FontSize = 43, FontWeight = FontWeights.Light,
            Foreground = WeatherBrush(251, 252, 255),
            VerticalAlignment = VerticalAlignment.Center
        };
        mainTemp.Children.Add(_weatherSymbolText);
        mainTemp.Children.Add(_weatherTemperatureText);
        currentRow.Children.Add(mainTemp);

        var details = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = FlowDirection.RightToLeft
        };
        _weatherConditionText = new TextBlock
        {
            Text = "در حال دریافت...",
            FontSize = 11,
            Foreground = WeatherBrush(225, 238, 253),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right
        };
        _weatherRainText = new TextBlock
        {
            Text = "💧 —%",
            FontSize = 11,
            Foreground = WeatherBrush(199, 225, 253),
            TextAlignment = TextAlignment.Right
        };
        details.Children.Add(_weatherConditionText);
        details.Children.Add(_weatherRainText);
        Grid.SetColumn(details, 1);
        currentRow.Children.Add(details);
        panel.Children.Add(currentRow);

        _weatherForecastGrid = new Grid { ColumnSpacing = 4, FlowDirection = FlowDirection.LeftToRight };
        for (var i = 0; i < 5; i++)
            _weatherForecastGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _weatherHourlyStrip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = "روند دمای ۲۴ ساعت آینده",
            TextAlignment = TextAlignment.Right,
            FontSize = 11,
            Foreground = WeatherBrush(222, 233, 252)
        });
        panel.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = _weatherHourlyStrip,
            MaxHeight = 98
        });

        _weatherStatusText = new TextBlock
        {
            Text = "در حال دریافت اطلاعات آب‌وهوا...",
            FontSize = 10,
            Foreground = WeatherBrush(194, 218, 245),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(_weatherStatusText);

        _weatherFullForecastButton = new Button
        {
            Content = "نمایش پیش‌بینی کامل",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = WeatherBrush(255, 255, 255, 12),
            Foreground = WeatherBrush(111, 209, 244),
            FontSize = 11,
            Padding = new Thickness(6, 5, 6, 5),
            BorderThickness = new Thickness(0),
            IsEnabled = false
        };
        _weatherFullForecastButton.Click += async (_, _) => await ShowCompleteWeatherForecastAsync();
        // The seven-day outlook is also attached to each matching calendar day card.
        panel.Children.Add(_weatherFullForecastButton);
        panel.Children.Add(new HyperlinkButton
        {
            Content = "Weather data by Open-Meteo.com",
            NavigateUri = new Uri("https://open-meteo.com/"),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 9,
            Foreground = WeatherBrush(162, 189, 218)
        });

        // Subtle star-field behind the data, matching the supplied nighttime preview.
        var layers = new Grid();
        layers.Children.Add(new TextBlock
        {
            Text = "·    ✦       ·       ·      ✧     ·       ✦      ·      ·",
            FontSize = 10,
            Opacity = 0.25,
            Foreground = WeatherBrush(191, 219, 255),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Margin = new Thickness(10, 35, 0, 0)
        });
        layers.Children.Add(panel);
        var card = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(12),
            BorderBrush = WeatherBrush(85, 125, 179, 95),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = layers
        };
        card.Loaded += (_, _) =>
        {
            if (_weatherRefreshTimer is null)
            {
                _weatherRefreshTimer = DispatcherQueue.CreateTimer();
                _weatherRefreshTimer.Interval = TimeSpan.FromMinutes(30);
                _weatherRefreshTimer.Tick += async (_, _) => await RefreshWeatherAsync(force: true);
                _weatherRefreshTimer.Start();
            }
            _ = RefreshWeatherAsync();
        };
        return card;
    }

    private async Task RefreshWeatherAsync(bool force = false)
    {
        if (!_settings.WeatherEnabled || _weatherLocationText is null) return;
        if (_weatherLoading)
        {
            if (force)
            {
                _weatherRefreshPending = true;
                _weatherRequestVersion++;
            }
            return;
        }

        var version = ++_weatherRequestVersion;
        _weatherLoading = true;
        if (_weatherStatusText is not null) _weatherStatusText.Text = "در حال به‌روزرسانی...";
        try
        {
            var place = SelectedWeatherPlace();
            var snapshot = await _weatherService.GetForecastAsync(place, _settings.WeatherFahrenheit, force);
            if (version != _weatherRequestVersion) return;
            _weatherSnapshot = snapshot;
            var (symbol, description) = ZurvanWeatherService.Describe(snapshot.WeatherCode, snapshot.IsDay);
            _weatherLocationText.Text = snapshot.Place.DisplayName;
            if (_weatherSymbolText is not null) _weatherSymbolText.Text = symbol;
            if (_weatherTemperatureText is not null)
                _weatherTemperatureText.Text = Math.Round(snapshot.CurrentTemperature).ToString("0") + "°";
            if (_weatherConditionText is not null) _weatherConditionText.Text = description;
            if (_weatherRainText is not null)
                _weatherRainText.Text = $"💧 {snapshot.PrecipitationChance}%";
            RenderWeatherForecast(snapshot);
            RenderWeatherDayBadges();
            if (_weatherFullForecastButton is not null) _weatherFullForecastButton.IsEnabled = true;
            if (_weatherStatusText is not null)
                _weatherStatusText.Text = $"به‌روزرسانی: {DateTime.Now:HH:mm} · {(_settings.WeatherFahrenheit ? "°F" : "°C")}";
        }
        catch (Exception ex)
        {
            if (_weatherStatusText is not null)
                _weatherStatusText.Text = _weatherSnapshot is null
                    ? "دریافت آب‌وهوا ممکن نشد؛ از منوی ⋯ دوباره تلاش کنید."
                    : "اتصال قطع است؛ آخرین پیش‌بینی نمایش داده می‌شود.";
            StartupDiagnostics.Log("Weather forecast fetch failed: " + ex.Message);
        }
        finally
        {
            _weatherLoading = false;
            if (_weatherRefreshPending)
            {
                _weatherRefreshPending = false;
                _ = RefreshWeatherAsync(force: true);
            }
        }
    }

    private void RenderWeatherForecast(WeatherSnapshot snapshot)
    {
        if (_weatherHourlyStrip is null) return;
        _weatherHourlyStrip.Children.Clear();
        var now = snapshot.ForecastLocalTime;
        // The weather service returns wall-clock times for the selected forecast city.
        // Display its next 24 forecast observations, without claiming local GPS data.
        var hours = snapshot.Hours.Where(h => h.LocalTime >= now.AddHours(-1))
            .Take(24).ToList();
        if (hours.Count == 0) hours = snapshot.Hours.Take(24).ToList();
        foreach (var hour in hours)
        {
            var temperature = Math.Round(hour.Temperature).ToString("0") + "°";
            var tile = new StackPanel
            {
                Width = 46,
                Spacing = 3,
                Padding = new Thickness(3, 5, 3, 5),
                Background = WeatherBrush(255, 255, 255, 15)
            };
            tile.Children.Add(new TextBlock
            {
                Text = hour.LocalTime.ToString("HH:mm"),
                FontSize = 9, Foreground = WeatherBrush(196, 215, 238),
                TextAlignment = TextAlignment.Center
            });
            tile.Children.Add(new TextBlock
            {
                Text = ZurvanWeatherService.Describe(hour.Code).Icon,
                FontSize = 17, TextAlignment = TextAlignment.Center
            });
            tile.Children.Add(new TextBlock
            {
                Text = temperature, FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Foreground = WeatherBrush(255, 255, 255)
            });
            _weatherHourlyStrip.Children.Add(tile);
        }
    }

    private void RenderWeatherDayBadges()
    {
        foreach (var badge in _weatherDayBadges.Values)
            badge.Visibility = Visibility.Collapsed;
        if (!_settings.WeatherEnabled || !_settings.WeatherOnCalendarCards || _weatherSnapshot is null)
            return;
        var lookup = _weatherSnapshot.Days.ToDictionary(day => day.Date);
        foreach (var (date, badge) in _weatherDayBadges)
        {
            if (!lookup.TryGetValue(date, out var day)) continue;
            badge.Text = ZurvanWeatherService.Describe(day.Code).Icon + " " +
                Math.Round(day.High).ToString("0") + "°/" + Math.Round(day.Low).ToString("0") + "°";
            badge.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(badge,
                ZurvanWeatherService.Describe(day.Code).Description +
                " · احتمال بارش " + day.PrecipitationChance + "%");
        }
    }

    private async Task ShowDesktopMonthPickerAsync()
    {
        if (_desktopCalendarPage?.XamlRoot is null) return;
        var monthBox = new ComboBox
        {
            Header = "ماه شمسی",
            MinWidth = 235,
            SelectedIndex = _month - 1
        };
        foreach (var monthName in BDFR.PersianCalendar.Core.PersianDate.MonthNames)
            monthBox.Items.Add(monthName);
        var yearBox = new NumberBox
        {
            Header = "سال شمسی",
            Value = _year,
            Minimum = 1,
            Maximum = 9377,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            MinWidth = 235
        };
        var form = new StackPanel { Spacing = 12 };
        form.Children.Add(monthBox);
        form.Children.Add(yearBox);
        var dialog = new ContentDialog
        {
            XamlRoot = _desktopCalendarPage.XamlRoot,
            Title = "انتخاب ماه و سال",
            Content = form,
            PrimaryButtonText = "نمایش ماه",
            CloseButtonText = "انصراف",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (monthBox.SelectedIndex is < 0 or > 11 || !double.IsFinite(yearBox.Value)) return;
        var year = (int)yearBox.Value;
        if (year < 1 || year > 9377) return;
        _year = year;
        _month = monthBox.SelectedIndex + 1;
        BuildCalendar();
        QueueSeasonCarouselRefreshForDisplayedMonth();
    }

    private async Task ShowWeatherOptionsAsync()
    {
        if (_desktopSelectedDayPanel?.XamlRoot is null) return;
        var cityBox = new TextBox
        {
            Header = "شهر",
            Text = _settings.WeatherCity,
            PlaceholderText = "مثلاً Tehran یا Seattle",
            FlowDirection = FlowDirection.LeftToRight
        };
        var unitBox = new ComboBox
        {
            Header = "واحد دما",
            ItemsSource = new[] { "سانتی‌گراد (°C)", "فارنهایت (°F)" },
            SelectedIndex = _settings.WeatherFahrenheit ? 1 : 0,
            MinWidth = 230
        };
        var editor = new StackPanel { Spacing = 12, Children = { cityBox, unitBox } };
        var dialog = new ContentDialog
        {
            XamlRoot = _desktopSelectedDayPanel.XamlRoot,
            Title = "تنظیمات آب‌وهوا",
            Content = editor,
            PrimaryButtonText = "ذخیره و به‌روزرسانی",
            SecondaryButtonText = "به‌روزرسانی اکنون",
            CloseButtonText = "انصراف",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                var changedCity = !string.Equals(_settings.WeatherCity, cityBox.Text.Trim(), StringComparison.OrdinalIgnoreCase);
                WeatherPlace? place = null;
                if (changedCity) place = await _weatherService.FindCityAsync(cityBox.Text.Trim());
                if (place is not null)
                {
                    _settings.WeatherCity = place.Name;
                    _settings.WeatherCountryCode = place.Country;
                    _settings.WeatherLatitude = place.Latitude;
                    _settings.WeatherLongitude = place.Longitude;
                }
                _settings.WeatherFahrenheit = unitBox.SelectedIndex == 1;
                _settingsService.Save(_settings);
                await RefreshWeatherAsync(force: true);
            }
            catch (Exception ex)
            {
                if (_weatherStatusText is not null) _weatherStatusText.Text = ex.Message;
            }
        }
        else
        {
            await RefreshWeatherAsync(force: true);
        }
    }

    private async Task ShowCompleteWeatherForecastAsync()
    {
        if (_desktopSelectedDayPanel?.XamlRoot is null || _weatherSnapshot is null) return;
        var forecast = _weatherSnapshot;
        var rows = new StackPanel { Spacing = 9, Padding = new Thickness(12) };
        foreach (var day in forecast.Days)
        {
            var details = ZurvanWeatherService.Describe(day.Code);
            rows.Children.Add(new TextBlock
            {
                Text = $"{day.Date:yyyy/MM/dd}   {details.Icon} {details.Description}   " +
                    $"{Math.Round(day.High)}° / {Math.Round(day.Low)}°   💧 {day.PrecipitationChance}%",
                TextWrapping = TextWrapping.Wrap
            });
        }
        rows.Children.Add(new TextBlock { Text = "منبع: Open-Meteo · داده‌ها پیش‌بینی هستند، نه مشاهدات قطعی.", Opacity = 0.65, FontSize = 11 });
        var dialog = new ContentDialog
        {
            XamlRoot = _desktopSelectedDayPanel.XamlRoot,
            Title = "پیش‌بینی هفت‌روزه · " + forecast.Place.DisplayName,
            Content = new ScrollViewer { Content = rows, MaxHeight = 440 },
            CloseButtonText = "بستن"
        };
        await dialog.ShowAsync();
    }
}
