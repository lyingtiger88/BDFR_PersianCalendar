using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

internal sealed class StartupFallbackWindow : Window
{
    public StartupFallbackWindow(Exception exception)
    {
        Title = "Anahita - Safe Mode";

        var openLogButton = new Button
        {
            Content = "باز کردن پوشه لاگ",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 16, 0, 0)
        };
        openLogButton.Click += (_, _) =>
        {
            try
            {
                var folder = Path.GetDirectoryName(StartupDiagnostics.LogPath);
                if (!string.IsNullOrWhiteSpace(folder))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", folder)
                    {
                        UseShellExecute = true
                    });
            }
            catch { }
        };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Padding = new Thickness(28),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Anahita",
                        FontSize = 28,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "حالت بازیابی رابط کاربری",
                        FontSize = 20
                    },
                    new TextBlock
                    {
                        Text = "رابط اصلی XAML روی این سیستم بارگذاری نشد. برنامه زنده است و خطا ثبت شده تا نسخه بعدی بدون حدس اصلاح شود.",
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 760
                    },
                    new TextBlock
                    {
                        Text = exception.ToString(),
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = new FontFamily("Consolas"),
                        Opacity = 0.8,
                        MaxWidth = 1000
                    },
                    new TextBlock
                    {
                        Text = $"Log: {StartupDiagnostics.LogPath}",
                        TextWrapping = TextWrapping.Wrap
                    },
                    openLogButton
                }
            }
        };

        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 700));
        }
        catch { }
    }
}
