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
    private Border? _zurvanDigitalThemeCard;
    private TextBlock? _zurvanDigitalDate;
    private TextBlock? _zurvanDigitalCaption;
    private TextBlock? _zurvanClockModeLabel;
    private readonly List<TextBlock> _zurvanClockNumerals = new();

    private FrameworkElement BuildZurvanClockPanel()
    {
        const double size = 320;
        var faceHost = new Grid
        {
            Width = size, Height = size,
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };
        var faceArtwork = new Image
        {
            Width = size, Height = size, Stretch = Stretch.Uniform,
            Source = new SvgImageSource { UriSource = new Uri("ms-appx:///picture/clock/zurvan-clock-face.svg") },
            FlowDirection = FlowDirection.LeftToRight
        };
        _zurvanClockArtwork = faceArtwork;
        faceHost.Children.Add(faceArtwork);
        _zurvanClockFace = new Canvas { Width = size, Height = size, FlowDirection = FlowDirection.LeftToRight };
        faceHost.Children.Add(_zurvanClockFace);

        const double center = size / 2;
        AddClockNumerals();
        _zurvanHourHand = new Line { Stroke = ThemeService.Brush(_theme.PrimaryText), StrokeThickness = 7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _zurvanMinuteHand = new Line { Stroke = ThemeService.Brush(_theme.PrimaryText), StrokeThickness = 4.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _zurvanSecondHand = new Line { Stroke = new SolidColorBrush(Color.FromArgb(255, 190, 76, 66)), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _zurvanClockFace.Children.Add(_zurvanHourHand);
        _zurvanClockFace.Children.Add(_zurvanMinuteHand);
        _zurvanClockFace.Children.Add(_zurvanSecondHand);
        var hub = new Ellipse { Width = 14, Height = 14, Fill = ThemeService.Brush(_theme.PrimaryText) };
        Canvas.SetLeft(hub, center - 7);
        Canvas.SetTop(hub, center - 7);
        _zurvanClockFace.Children.Add(hub);

        _zurvanDigitalTime = new TextBlock
        {
            FontSize = 46, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight
        };
        _zurvanDigitalDate = new TextBlock
        {
            FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        _zurvanDigitalCaption = new TextBlock
        {
            FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        var digitalContent = new StackPanel
        {
            Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        digitalContent.Children.Add(_zurvanDigitalCaption);
        digitalContent.Children.Add(_zurvanDigitalTime);
        digitalContent.Children.Add(_zurvanDigitalDate);
        _zurvanDigitalThemeCard = new Border
        {
            Width = 292, Height = 292,
            BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(150),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Child = digitalContent,
            Visibility = _settings.CalendarClockMode == "digital" ? Visibility.Visible : Visibility.Collapsed
        };
        faceHost.Children.Add(_zurvanDigitalThemeCard);
        _zurvanClockFace.Visibility = _settings.CalendarClockMode == "digital" ? Visibility.Collapsed : Visibility.Visible;
        faceArtwork.Visibility = _settings.CalendarClockMode == "digital" ? Visibility.Collapsed : Visibility.Visible;
        ApplyDigitalClockTheme();

        UpdateZurvanClock();
        _zurvanClockTimer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _zurvanClockTimer.Interval = TimeSpan.FromSeconds(1);
        _zurvanClockTimer.Tick -= ZurvanClockTimer_Tick;
        _zurvanClockTimer.Tick += ZurvanClockTimer_Tick;
        _zurvanClockTimer.Start();
        return faceHost;
    }

    private void ZurvanClockTimer_Tick(DispatcherQueueTimer sender, object args) => UpdateZurvanClock();

    private void ApplyClockMode()
    {
        if (_zurvanClockFace is null || _zurvanDigitalTime is null) return;
        var digital = _settings.CalendarClockMode == "digital";
        _zurvanClockFace.Visibility = digital ? Visibility.Collapsed : Visibility.Visible;
        if (_zurvanClockArtwork is not null) _zurvanClockArtwork.Visibility = digital ? Visibility.Collapsed : Visibility.Visible;
        if (_zurvanDigitalThemeCard is not null)
            _zurvanDigitalThemeCard.Visibility = digital ? Visibility.Visible : Visibility.Collapsed;
        ApplyDigitalClockTheme();
        UpdateZurvanClock();
    }

    private void ApplyDigitalClockTheme()
    {
        if (_zurvanDigitalTime is null || _zurvanDigitalThemeCard is null) return;
        var palette = _settings.CalendarDigitalClockTheme switch
        {
            "wooden_amber" => ("#FF442317", "#FFFFB955", "#FFB47F42", "✿  چوبی کهربایی"),
            "minimal_silver" => ("#FF354255", "#FFF2F6FE", "#FFBAC9D8", "—  مینیمال نقره‌ای  —"),
            "art_deco_emerald" => ("#FF073C36", "#FFFFE1A3", "#FFCDA353", "◆  آرت‌دکو زمردی  ◆"),
            "neon_glass" => ("#FF1B2253", "#FF68E9F9", "#FFA276F6", "◌  نئون شیشه‌ای  ◌"),
            "persian_geometric" => ("#FF07284C", "#FFE6F8FF", "#FFD9B365", "❖  هندسی ایرانی  ❖"),
            _ => ("#FF101A3D", "#FFFFD47E", "#FFE2AF5B", "✦  کیهانی طلایی  ✦")
        };
        _zurvanDigitalThemeCard.Background = ThemeService.Brush(palette.Item1);
        _zurvanDigitalThemeCard.BorderBrush = ThemeService.Brush(palette.Item3);
        _zurvanDigitalTime.Foreground = ThemeService.Brush(palette.Item2);
        if (_zurvanDigitalDate is not null)
            _zurvanDigitalDate.Foreground = ThemeService.Brush(palette.Item2);
        if (_zurvanDigitalCaption is not null)
        {
            _zurvanDigitalCaption.Text = palette.Item4;
            _zurvanDigitalCaption.Foreground = ThemeService.Brush(palette.Item3);
        }
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
                Text = values[i], FontSize = _settings.CalendarClockNumeralStyle == "roman" ? 17 : 21,
                Width = 38, Height = 28, TextAlignment = TextAlignment.Center,
                Foreground = ThemeService.Brush(_theme.PrimaryText),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FlowDirection = FlowDirection.LeftToRight
            };
            var angle = (i * 30 - 90) * Math.PI / 180.0;
            Canvas.SetLeft(label, 160 + Math.Cos(angle) * 111 - 19);
            Canvas.SetTop(label, 160 + Math.Sin(angle) * 111 - 14);
            _zurvanClockNumerals.Add(label);
            _zurvanClockFace.Children.Add(label);
        }
    }

    private void UpdateZurvanClock()
    {
        var now = DateTime.Now;
        if (_zurvanDigitalTime is not null)
            _zurvanDigitalTime.Text = now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        if (_zurvanDigitalDate is not null)
        {
            var pc = new System.Globalization.PersianCalendar();
            var names = new[] { "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
                "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };
            _zurvanDigitalDate.Text = $"{pc.GetDayOfMonth(now)} {names[pc.GetMonth(now) - 1]} {pc.GetYear(now)}";
        }
        if (_zurvanClockFace is null || _zurvanHourHand is null || _zurvanMinuteHand is null || _zurvanSecondHand is null) return;
        SetClockHand(_zurvanHourHand, (now.Hour % 12 + now.Minute / 60.0) * 30.0, 66);
        SetClockHand(_zurvanMinuteHand, (now.Minute + now.Second / 60.0) * 6.0, 94);
        SetClockHand(_zurvanSecondHand, now.Second * 6.0, 112);
    }

    private static void SetClockHand(Line hand, double degrees, double length)
    {
        const double center = 160;
        var angle = (degrees - 90) * Math.PI / 180.0;
        hand.X1 = center; hand.Y1 = center;
        hand.X2 = center + Math.Cos(angle) * length;
        hand.Y2 = center + Math.Sin(angle) * length;
    }
}
