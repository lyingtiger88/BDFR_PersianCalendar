using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private static readonly string DeskInk = "#FF24212B";
    private static readonly string DeskMuted = "#FF767182";
    private static readonly string DeskOrange = "#FFBE753A";

    private FrameworkElement BuildDeskDesignHeader()
    {
        var layout = new Grid
        {
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 16, 0, 26),
            ColumnSpacing = 18
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel
        {
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        heading.Children.Add(new TextBlock
        {
            Text = "زروان دسک",
            FontSize = 36,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ThemeService.Brush(DeskInk),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Zurvan Desk",
            FontSize = 25,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(DeskMuted),
            HorizontalAlignment = HorizontalAlignment.Right,
            FlowDirection = FlowDirection.LeftToRight
        });
        heading.Children.Add(new TextBlock
        {
            Text = "فضای ابری شخصی زروان؛ حساب مستقل از لایسنس و فعال‌سازی نرم‌افزار",
            FontSize = 15,
            Foreground = ThemeService.Brush(DeskMuted),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        Grid.SetColumn(heading, 0);

        var cloudBrand = new Border
        {
            Width = 91, Height = 91,
            Background = BrushWithAlpha("#FFF7DFD2", 0xFF),
            CornerRadius = new CornerRadius(25),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock
            {
                Text = "☁",
                FontSize = 67,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Symbol"),
                Foreground = ThemeService.Brush("#FFF49B4B"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var headingLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            FlowDirection = FlowDirection.RightToLeft,
            Spacing = 20,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        headingLine.Children.Add(cloudBrand);
        headingLine.Children.Add(heading);
        Grid.SetColumn(headingLine, 0);
        layout.Children.Add(headingLine);

        var search = new TextBox
        {
            PlaceholderText = "جستجوی تنظیمات، امکانات و راهنما...",
            Width = 440,
            Height = 43,
            CornerRadius = new CornerRadius(24),
            Background = ThemeService.Brush("#FFFFFFFF"),
            BorderBrush = ThemeService.Brush("#FFE9E1E6"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var q = search.Text.Trim();
            if (q.Contains("برنامه", StringComparison.OrdinalIgnoreCase))
                ShowDesktopModule("برنامه‌ها");
            else if (q.Contains("مناسبت", StringComparison.OrdinalIgnoreCase))
                ShowDesktopModule("مناسبت‌ها");
            else if (q.Contains("یادآور", StringComparison.OrdinalIgnoreCase))
                ShowDesktopModule("یادآورها");
            else if (q.Contains("تنظیم", StringComparison.OrdinalIgnoreCase))
                ShowDesktopModule("تنظیمات");
            else if (q.Contains("تقویم", StringComparison.OrdinalIgnoreCase))
                ShowDesktopModule("تقویم");
            else if (q.Contains("فعالیت", StringComparison.OrdinalIgnoreCase))
                ShowDesktopModule("فعالیت‌ها");
            else if (q.Contains("لایسنس", StringComparison.OrdinalIgnoreCase))
                ShowDesktopModule("لایسنس و فعال‌سازی");
        };
        Grid.SetColumn(search, 1);
        layout.Children.Add(search);
        return layout;
    }

    private FrameworkElement BuildDeskSplitLayout(UIElement form)
    {
        var grid = new Grid
        {
            FlowDirection = FlowDirection.RightToLeft,
            ColumnSpacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 505,
            Margin = new Thickness(0, 0, 0, 16)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var card = new Border
        {
            Background = ThemeService.Brush("#FFFDFCFC"),
            BorderBrush = BrushWithAlpha("#FFDDCFCC", 0x9A),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(34, 30, 34, 27),
            MinHeight = 505,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = form
        };
        Grid.SetColumn(card, 0);

        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Source = new SvgImageSource(new Uri("ms-appx:///picture/desk/welcome.svg"))
        };
        var hero = new Border
        {
            Background = ThemeService.Brush("#FFF3DDDA"),
            CornerRadius = new CornerRadius(24),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = 505,
            Child = image
        };
        Grid.SetColumn(hero, 1);
        grid.Children.Add(hero);
        grid.Children.Add(card);
        return grid;
    }
}
