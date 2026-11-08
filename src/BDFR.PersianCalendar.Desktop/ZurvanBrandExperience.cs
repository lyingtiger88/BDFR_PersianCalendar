using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private int _zurvanLogoTapCount;
    private long _zurvanLastTapTicks;

    private UIElement BuildZurvanBrandMark()
    {
        var mark = new Grid { Width = 62, Height = 62 };
        mark.Children.Add(new Ellipse
        {
            Fill = ThemeService.Brush("#FF172C42"),
            Stroke = ThemeService.Brush("#FFD7B36A"),
            StrokeThickness = 2
        });
        mark.Children.Add(new Ellipse
        {
            Margin = new Thickness(6),
            Stroke = ThemeService.Brush("#FF8DA9C4"),
            StrokeThickness = 1.2
        });
        mark.Children.Add(new Ellipse
        {
            Margin = new Thickness(12),
            Stroke = ThemeService.Brush("#FFD7B36A"),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 1, 2 }
        });
        mark.Children.Add(new TextBlock
        {
            Text = "∞",
            FontSize = 35,
            FontWeight = Microsoft.UI.Text.FontWeights.Light,
            Foreground = ThemeService.Brush("#FFF4D99B"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -3, 0, 0)
        });
        mark.Children.Add(new Ellipse
        {
            Width = 6, Height = 6,
            Fill = ThemeService.Brush("#FFFFE6A8"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        return mark;
    }

    private void HandleZurvanLogoTap(FrameworkElement source)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = _zurvanLastTapTicks == 0
            ? TimeSpan.MaxValue
            : Stopwatch.GetElapsedTime(_zurvanLastTapTicks, now);
        _zurvanLogoTapCount = elapsed > TimeSpan.FromSeconds(8)
            ? 1 : _zurvanLogoTapCount + 1;
        _zurvanLastTapTicks = now;
        if (_zurvanLogoTapCount < 7) return;

        _zurvanLogoTapCount = 0;
        _ = ShowZurvanPhilosophyAsync(source);
    }

    private async Task ShowZurvanPhilosophyAsync(FrameworkElement source)
    {
        var illustration = new Grid
        {
            Width = 220,
            Height = 180,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        illustration.Children.Add(new Ellipse
        {
            Fill = ThemeService.Brush("#FF172C42"),
            Stroke = ThemeService.Brush("#FFD7B36A"),
            StrokeThickness = 3
        });
        illustration.Children.Add(new Ellipse
        {
            Margin = new Thickness(14),
            Stroke = ThemeService.Brush("#FF8DA9C4"),
            StrokeThickness = 2
        });
        illustration.Children.Add(new Ellipse
        {
            Margin = new Thickness(30),
            Stroke = ThemeService.Brush("#FFD7B36A"),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 1, 2 }
        });
        illustration.Children.Add(new Line
        {
            X1 = 110, Y1 = 90, X2 = 145, Y2 = 53,
            Stroke = ThemeService.Brush("#FFF4D99B"),
            StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        illustration.Children.Add(new Line
        {
            X1 = 110, Y1 = 90, X2 = 84, Y2 = 48,
            Stroke = ThemeService.Brush("#FF8DA9C4"),
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        illustration.Children.Add(new Ellipse
        {
            Width = 13, Height = 13,
            Fill = ThemeService.Brush("#FFFFE6A8"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        var body = new StackPanel
        {
            Spacing = 13,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        body.Children.Add(illustration);
        body.Children.Add(new TextBlock
        {
            Text = "زُروان، نماد زمان در اندیشه ایران باستان",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush("#FF282532")
        });
        body.Children.Add(new TextBlock
        {
            Text = "زروان در سنت‌های کهن ایرانی با مفهوم زمان پیوند دارد؛ در روایت‌های زروانی، «زروان اکرانه» به معنای زمان بی‌کران است. نام تقویم زروان یادآور گذر روزها، گردش فصل‌ها و پیوند گذشته، اکنون و آینده است.",
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush("#FF625E6B")
        });
        body.Children.Add(new TextBlock
        {
            Text = "زمان، حافظه زندگی است.",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Foreground = ThemeService.Brush("#FF9A7132")
        });

        var dialog = new ContentDialog
        {
            Title = "فلسفه نام زروان",
            Content = body,
            CloseButtonText = "بستن",
            XamlRoot = source.XamlRoot,
            FlowDirection = FlowDirection.RightToLeft,
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }
}
