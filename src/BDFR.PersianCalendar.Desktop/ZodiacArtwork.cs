using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace BDFR.PersianCalendar.Desktop;

/// <summary>
/// Loads the approved, locally bundled PNG zodiac artwork.
/// No runtime SVG generation or font glyph substitution is used.
/// </summary>
internal static class ZodiacArtwork
{
    private static readonly string[] SignIds =
    [
        "aries", "taurus", "gemini", "cancer", "leo", "virgo",
        "libra", "scorpio", "sagittarius", "capricorn", "aquarius", "pisces"
    ];

    private static readonly Lazy<JsonDocument?> Catalog = new(() =>
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "picture", "zodiac", "catalog.json");
            return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception e)
        {
            StartupDiagnostics.Log($"Zodiac catalog unavailable: {e.Message}");
            return null;
        }
    });

    public static async Task PopulateAsync(Image target, int month, string? theme)
    {
        target.Source = await LoadAsync(month, theme);
    }

    public static async Task<ImageSource?> LoadAsync(int month, string? theme)
    {
        if (month is < 1 or > 12) return null;

        try
        {
            var selectedTheme = string.IsNullOrWhiteSpace(theme) ? "cosmic" : theme.Trim().ToLowerInvariant();
            var root = Path.Combine(AppContext.BaseDirectory, "picture", "zodiac", "png");
            var path = Path.Combine(root, $"{selectedTheme}_{SignIds[month - 1]}.png");

            // Fall back to the cosmic set only when the selected style asset is absent.
            if (!File.Exists(path))
                path = Path.Combine(root, $"cosmic_{SignIds[month - 1]}.png");

            if (!File.Exists(path))
            {
                StartupDiagnostics.Log($"Zodiac PNG missing: theme={selectedTheme}, month={month}, path={path}");
                return null;
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Zodiac PNG could not load for month={month}, theme={theme}: {ex}");
            return null;
        }
    }
}
