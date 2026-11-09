using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _zurvanClockTimer;
    private Canvas? _zurvanClockFace;
    private Image? _zurvanClockArtwork;
    private Line? _zurvanHourHand;
    private Line? _zurvanMinuteHand;
    private Line? _zurvanSecondHand;
    private TextBlock? _zurvanDigitalTime;
    private TextBlock? _zurvanClockModeLabel;
    private readonly List<TextBlock> _zurvanClockNumerals = new();

    private FrameworkElement BuildZurvanClockPanel()
    {
        var host = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(8, 2, 8, 2)
        };

        var faceHost = new Grid { Width = 172, Height = 172 };
        var faceArtwork = new Image
        {
            Width = 172, Height = 172, Stretch = Stretch.Uniform,
            Source = new SvgImageSource { UriSource = new Uri("ms-appx:///picture/clock/zurvan-clock-face.svg") }
        };
        _zurvanClockArtwork = faceArtwork;
        faceHost.Children.Add(faceArtwork);
        _zurvanClockFace = new Canvas { Width = 172, Height = 172 };
        faceHost.Children.Add(_zurvanClockFace);

        var center = 86.0;

        AddClockNumerals();
        _zurvanHourHand = new Line { Stroke = ThemeService.Brush(_theme.PrimaryText), StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _zurvanMinuteHand = new Line { Stroke = ThemeService.Brush(_theme.PrimaryText), StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _zurvanSecondHand = new Line { Stroke = new SolidColorBrush(Color.FromArgb(255, 190, 76, 66)), StrokeThickness = 1.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _zurvanClockFace.Children.Add(_zurvanHourHand);
        _zurvanClockFace.Children.Add(_zurvanMinuteHand);
        _zurvanClockFace.Children.Add(_zurvanSecondHand);
        var hub = new Ellipse { Width = 8, Height = 8, Fill = ThemeService.Brush(_theme.PrimaryText) };
        Canvas.SetLeft(hub, center - 4); Canvas.SetTop(hub, center - 4);
        _zurvanClockFace.Children.Add(hub);

        _zurvanDigitalTime = new TextBlock
        {
            FontSize = 25, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight,
            Foreground = ThemeService.Brush(_theme.PrimaryText),
            Visibility = _settings.CalendarClockMode == "digital" ? Visibility.Visible : Visibility.Collapsed
        };
        faceHost.Children.Add(_zurvanDigitalTime);
        _zurvanClockFace.Visibility = _settings.CalendarClockMode == "digital" ? Visibility.Collapsed : Visibility.Visible;
        host.Children.Add(faceHost);

        var controls = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Width = 142 };
        _zurvanClockModeLabel = new TextBlock { Text = "ساعت زروان", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ThemeService.Brush(_theme.PrimaryText) };
        controls.Children.Add(_zurvanClockModeLabel);
        var mode = MakeButton(_settings.CalendarClockMode == "digital" ? "نمایش آنالوگ" : "نمایش دیجیتال");
        mode.MinWidth = 130;
        mode.Click += (_, _) =>
        {
            _settings.CalendarClockMode = _settings.CalendarClockMode == "digital" ? "analog" : "digital";
            _settingsService.Save(_settings);
            ApplyClockMode();
            mode.Content = _settings.CalendarClockMode == "digital" ? "نمایش آنالوگ" : "نمایش دیجیتال";
        };
        controls.Children.Add(mode);

        var numeralLabel = new TextBlock { Text = "سبک شمارگان", FontSize = 11, Opacity = .75, Foreground = ThemeService.Brush(_theme.PrimaryText) };
        controls.Children.Add(numeralLabel);
        var numeralPicker = new ComboBox { MinWidth = 132, HorizontalAlignment = HorizontalAlignment.Stretch };
        numeralPicker.Items.Add(new ComboBoxItem { Content = "خوارزمی", Tag = "khwarazmi" });
        numeralPicker.Items.Add(new ComboBoxItem { Content = "پهلوی", Tag = "pahlavi" });
        numeralPicker.Items.Add(new ComboBoxItem { Content = "میخی", Tag = "cuneiform" });
        numeralPicker.Items.Add(new ComboBoxItem { Content = "رومی", Tag = "roman" });
        numeralPicker.SelectedIndex = Math.Max(0, new[] { "khwarazmi", "pahlavi", "cuneiform", "roman" }.ToList().IndexOf(_settings.CalendarClockNumeralStyle));
        numeralPicker.SelectionChanged += (_, _) =>
        {
            if (numeralPicker.SelectedItem is ComboBoxItem selected && selected.Tag is string style)
            {
                _settings.CalendarClockNumeralStyle = style;
                _settingsService.Save(_settings);
                AddClockNumerals();
                UpdateZurvanClock();
            }
        };
        controls.Children.Add(numeralPicker);
        host.Children.Add(controls);

        UpdateZurvanClock();
        _zurvanClockTimer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _zurvanClockTimer.Interval = TimeSpan.FromSeconds(1);
        _zurvanClockTimer.Tick -= ZurvanClockTimer_Tick;
        _zurvanClockTimer.Tick += ZurvanClockTimer_Tick;
        _zurvanClockTimer.Start();
        return host;
    }

    private void ZurvanClockTimer_Tick(DispatcherQueueTimer sender, object args) => UpdateZurvanClock();

    private void ApplyClockMode()
    {
        if (_zurvanClockFace is null || _zurvanDigitalTime is null) return;
        var digital = _settings.CalendarClockMode == "digital";
        _zurvanClockFace.Visibility = digital ? Visibility.Collapsed : Visibility.Visible;
        if (_zurvanClockArtwork is not null) _zurvanClockArtwork.Visibility = digital ? Visibility.Collapsed : Visibility.Visible;
        _zurvanDigitalTime.Visibility = digital ? Visibility.Visible : Visibility.Collapsed;
        UpdateZurvanClock();
    }

    private void AddClockNumerals()
    {
        if (_zurvanClockFace is null) return;
        foreach (var item in _zurvanClockNumerals) _zurvanClockFace.Children.Remove(item);
        _zurvanClockNumerals.Clear();
        var values = _settings.CalendarClockNumeralStyle switch
        {
            "roman" => new[] { "XII", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI" },
            "pahlavi" => new[] { "𐭠", "𐭡", "𐭢", "𐭣", "𐭤", "𐭥", "𐭦", "𐭧", "𐭨", "𐭩", "𐭪", "𐭫" },
            "cuneiform" => new[] { "𒐕", "𒐖", "𒐗", "𒐘", "𒐙", "𒐚", "𒐛", "𒐜", "𒐝", "𒐞", "𒐟", "𒐠" },
            _ => new[] { "۱۲", "۱", "۲", "۳", "۴", "۵", "۶", "۷", "۸", "۹", "۱۰", "۱۱" }
        };
        for (var i = 0; i < 12; i++)
        {
            var label = new TextBlock
            {
                Text = values[i], FontSize = _settings.CalendarClockNumeralStyle == "roman" ? 9 : 11,
                Width = 24, Height = 18, TextAlignment = TextAlignment.Center,
                Foreground = ThemeService.Brush(_theme.PrimaryText),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FlowDirection = FlowDirection.LeftToRight
            };
            var angle = (i * 30 - 90) * Math.PI / 180.0;
            Canvas.SetLeft(label, 86 + Math.Cos(angle) * 59 - 12);
            Canvas.SetTop(label, 86 + Math.Sin(angle) * 59 - 9);
            _zurvanClockNumerals.Add(label);
            _zurvanClockFace.Children.Add(label);
        }
    }

    private void UpdateZurvanClock()
    {
        var now = DateTime.Now;
        if (_zurvanDigitalTime is not null)
            _zurvanDigitalTime.Text = now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        if (_zurvanClockFace is null || _zurvanHourHand is null || _zurvanMinuteHand is null || _zurvanSecondHand is null) return;
        SetClockHand(_zurvanHourHand, (now.Hour % 12 + now.Minute / 60.0) * 30.0, 34);
        SetClockHand(_zurvanMinuteHand, (now.Minute + now.Second / 60.0) * 6.0, 49);
        SetClockHand(_zurvanSecondHand, now.Second * 6.0, 57);
    }

    private static void SetClockHand(Line hand, double degrees, double length)
    {
        const double center = 86;
        var angle = (degrees - 90) * Math.PI / 180.0;
        hand.X1 = center; hand.Y1 = center;
        hand.X2 = center + Math.Cos(angle) * length;
        hand.Y2 = center + Math.Sin(angle) * length;
    }
}
