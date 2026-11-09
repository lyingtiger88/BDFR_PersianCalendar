using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    /// <summary>Live preview and persisted digital/analog clock settings for Windows.</summary>
    private FrameworkElement BuildClockSettingsPanel()
    {
        var group = new StackPanel { Spacing = 12 };
        group.Children.Add(new TextBlock
        {
            Text = "ساعت زروان — آنالوگ یا دیجیتال",
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        });

        var mode = new ComboBox { Header = "نوع نمایش ساعت", MinWidth = 230, HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.Items.Add(new ComboBoxItem { Content = "آنالوگ", Tag = "analog" });
        mode.Items.Add(new ComboBoxItem { Content = "دیجیتال", Tag = "digital" });
        mode.SelectedIndex = _settings.ClockDisplayMode == "digital" ? 1 : 0;

        var style = new ComboBox { Header = "طرح دیجیتال", MinWidth = 230, HorizontalAlignment = HorizontalAlignment.Stretch };
        var styles = new (string Id, string Name)[]
        {
            ("classic", "کلاسیک طلایی"),
            ("modern", "مدرن فیروزه‌ای"),
            ("dots", "نقطه‌ای بنفش"),
            ("flip", "فلیپ")
        };
        foreach (var item in styles)
            style.Items.Add(new ComboBoxItem { Content = item.Name, Tag = item.Id });
        style.SelectedIndex = Math.Max(0, Array.FindIndex(styles, x => x.Id == _settings.DigitalClockStyle));

        var numeral = new ComboBox
        {
            Header = "سبک شمارگان صفحه آنالوگ",
            MinWidth = 230,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var numeralStyles = new (string Id, string Label)[]
        {
            ("zurvan", "زروان (ارقام فارسی)"),
            ("khwarazmi", "خوارزمی (نشانگرهای بازسازی‌شده)"),
            ("pahlavi", "پهلوی (نشانگرهای بازسازی‌شده)"),
            ("cuneiform", "میخی (نشانگرهای بازسازی‌شده)"),
            ("roman", "رومی (I–XII)")
        };
        foreach (var item in numeralStyles)
            numeral.Items.Add(new ComboBoxItem { Content = item.Label, Tag = item.Id });
        numeral.SelectedIndex = Math.Max(0, Array.FindIndex(numeralStyles,
            x => x.Id == _settings.AnalogClockNumeralStyle));
        var analogOptions = new StackPanel { Spacing = 8 };
        analogOptions.Children.Add(numeral);
        analogOptions.Children.Add(SettingsHint(
            "شمارگان خوارزمی، پهلوی و میخی فعلاً نشانگرهای گرافیکی هستند؛ " +
            "تا زمان تأیید منابع تاریخی به‌عنوان اعداد اصیل نمایش داده نمی‌شوند."));

        var use24h = SettingsToggle(_settings.DigitalClockTwentyFourHours, enabled =>
        {
            _settings.DigitalClockTwentyFourHours = enabled;
            _settingsService.Save(_settings);
        });
        var showSeconds = SettingsToggle(_settings.ClockShowSeconds, enabled =>
        {
            _settings.ClockShowSeconds = enabled;
            _settingsService.Save(_settings);
        });
        var digitalOptions = new StackPanel { Spacing = 8 };
        digitalOptions.Children.Add(style);
        digitalOptions.Children.Add(SettingsOptionRow("زمان ۲۴ ساعته", "در حالت خاموش از قالب ۱۲ ساعته استفاده می‌شود", use24h, "\uE823"));
        digitalOptions.Children.Add(SettingsOptionRow("نمایش ثانیه", "ثانیه را در ساعت دیجیتال نشان بده", showSeconds, "\uE823"));

        var preview = new Grid
        {
            Width = 280,
            Height = 280,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var navy = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 28, 51));
        var gold = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 180, 85));
        var black = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 18, 21));
        var red = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 197, 60, 49));
        var white = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 248, 240));

        var face = new Canvas { Width = 280, Height = 280, Background = navy };
        var outer = new Polygon
        {
            Stroke = gold,
            StrokeThickness = 3.0,
            Fill = white
        };
        var outline = new Microsoft.UI.Xaml.Media.PointCollection();
        for (int i = 0; i < 24; i++)
        {
            var angle = (i * 15.0 - 90) * Math.PI / 180.0;
            var radius = i % 2 == 0 ? 126.0 : 97.0;
            outline.Add(new Point(140 + Math.Cos(angle) * radius, 140 + Math.Sin(angle) * radius));
        }
        outer.Points = outline;
        face.Children.Add(outer);

        var dial = new Ellipse
        {
            Width = 160, Height = 160, Stroke = black, StrokeThickness = 2,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(245, 249, 245, 232))
        };
        Canvas.SetLeft(dial, 60);
        Canvas.SetTop(dial, 60);
        face.Children.Add(dial);

        var numeralLabels = new List<TextBlock>();
        var romanNumerals = new[] { "XII", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI" };
        var persianNumerals = new[] { "۱۲", "۱", "۲", "۳", "۴", "۵", "۶", "۷", "۸", "۹", "۱۰", "۱۱" };
        for (int i = 0; i < 12; i++)
        {
            var angle = (i * 30.0 - 90) * Math.PI / 180.0;
            var marker = new TextBlock
            {
                Width = 32, Height = 20,
                TextAlignment = TextAlignment.Center,
                FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = black
            };
            Canvas.SetLeft(marker, 140 + Math.Cos(angle) * 69 - 16);
            Canvas.SetTop(marker, 140 + Math.Sin(angle) * 69 - 10);
            face.Children.Add(marker);
            numeralLabels.Add(marker);
        }
        void RefreshNumerals()
        {
            for (int i = 0; i < numeralLabels.Count; i++)
            {
                numeralLabels[i].Text = _settings.AnalogClockNumeralStyle switch
                {
                    "roman" => romanNumerals[i],
                    "zurvan" => persianNumerals[i],
                    "cuneiform" => "◆",
                    "pahlavi" => "✣",
                    "khwarazmi" => "◇",
                    _ => persianNumerals[i]
                };
            }
        }

        Line CreateHand(double length, double stroke, Brush brush)
        {
            var hand = new Line
            {
                X1 = 140, Y1 = 140, X2 = 140, Y2 = 140 - length,
                Stroke = brush, StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = new RotateTransform { CenterX = 140, CenterY = 140 }
            };
            face.Children.Add(hand);
            return hand;
        }
        var hourHand = CreateHand(45, 7, black);
        var minuteHand = CreateHand(62, 5, gold);
        var secondHand = CreateHand(74, 2, red);
        var hub = new Ellipse { Width = 11, Height = 11, Fill = gold, Stroke = black, StrokeThickness = 2 };
        Canvas.SetLeft(hub, 134.5);
        Canvas.SetTop(hub, 134.5);
        face.Children.Add(hub);

        var digits = new TextBlock
        {
            FontSize = 41,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = gold
        };
        var digitalFace = new Border
        {
            Width = 260,
            Height = 130,
            Background = navy,
            BorderBrush = gold,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = digits,
            Visibility = Visibility.Collapsed
        };
        preview.Children.Add(face);
        preview.Children.Add(digitalFace);

        void RefreshPreview()
        {
            var now = DateTime.Now;
            var digital = _settings.ClockDisplayMode == "digital";
            face.Visibility = digital ? Visibility.Collapsed : Visibility.Visible;
            digitalFace.Visibility = digital ? Visibility.Visible : Visibility.Collapsed;
            digitalOptions.Visibility = digital ? Visibility.Visible : Visibility.Collapsed;
            analogOptions.Visibility = digital ? Visibility.Collapsed : Visibility.Visible;
            RefreshNumerals();
            if (digital)
            {
                var pattern = _settings.DigitalClockTwentyFourHours
                    ? (_settings.ClockShowSeconds ? "HH:mm:ss" : "HH:mm")
                    : (_settings.ClockShowSeconds ? "hh:mm:ss tt" : "hh:mm tt");
                digits.Text = now.ToString(pattern, CultureInfo.InvariantCulture);
                digits.FontSize = _settings.ClockShowSeconds ? 39 : 47;
                digits.Foreground = _settings.DigitalClockStyle switch
                {
                    "modern" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 103, 229, 244)),
                    "dots" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 225, 202, 248)),
                    "flip" => white,
                    _ => gold
                };
            }
            else
            {
                ((RotateTransform)hourHand.RenderTransform).Angle = (now.Hour % 12) * 30 + now.Minute / 2.0;
                ((RotateTransform)minuteHand.RenderTransform).Angle = now.Minute * 6 + now.Second / 10.0;
                ((RotateTransform)secondHand.RenderTransform).Angle = now.Second * 6;
                secondHand.Visibility = _settings.ClockShowSeconds ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        numeral.SelectionChanged += (_, _) =>
        {
            _settings.AnalogClockNumeralStyle =
                (numeral.SelectedItem as ComboBoxItem)?.Tag as string ?? "zurvan";
            _settingsService.Save(_settings);
            RefreshPreview();
        };

        mode.SelectionChanged += (_, _) =>
        {
            _settings.ClockDisplayMode = (mode.SelectedItem as ComboBoxItem)?.Tag as string ?? "analog";
            _settingsService.Save(_settings);
            RefreshPreview();
        };
        style.SelectionChanged += (_, _) =>
        {
            _settings.DigitalClockStyle = (style.SelectedItem as ComboBoxItem)?.Tag as string ?? "classic";
            _settingsService.Save(_settings);
            RefreshPreview();
        };
        use24h.Toggled += (_, _) => RefreshPreview();
        showSeconds.Toggled += (_, _) => RefreshPreview();

        group.Children.Add(mode);
        group.Children.Add(preview);
        group.Children.Add(analogOptions);
        group.Children.Add(digitalOptions);
        group.Children.Add(SettingsHint("پیش‌نمایش زنده است و انتخاب‌ها برای اجرای بعدی نیز ذخیره می‌شوند."));

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => RefreshPreview();
        group.Loaded += (_, _) =>
        {
            RefreshPreview();
            timer.Start();
        };
        group.Unloaded += (_, _) => timer.Stop();
        return group;
    }
}
