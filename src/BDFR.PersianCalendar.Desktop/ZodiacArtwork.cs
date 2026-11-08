using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BDFR.PersianCalendar.Desktop;

/// <summary>
/// Loads locally bundled PNG zodiac artwork. If a developer runs a build
/// without the asset-generation step, a catalog-backed vector fallback keeps
/// the previews and calendar artwork visible rather than rendering blank tiles.
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

    private static readonly ConcurrentDictionary<string, ImageSource> VectorFallbacks = new();

    public static async Task PopulateAsync(Image target, int month, string? theme)
    {
        if (month is < 1 or > 12)
        {
            target.Source = null;
            return;
        }

        target.Source = await LoadAsync(month, theme);
    }

    public static async Task<ImageSource?> LoadAsync(int month, string? theme)
    {
        if (month is < 1 or > 12) return null;

        var selectedTheme = string.IsNullOrWhiteSpace(theme) ? "cosmic" : theme.Trim().ToLowerInvariant();
        var signId = SignIds[month - 1];
        var root = Path.Combine(AppContext.BaseDirectory, "picture", "zodiac", "png");
        var path = Path.Combine(root, $"{selectedTheme}_{signId}.png");
        // Old installations retain the legacy cute files. New style slots are
        // populated by the user’s separate artwork; never manufacture duplicates.
        if (!File.Exists(path) && selectedTheme.StartsWith("cute_", StringComparison.Ordinal))
            path = Path.Combine(root, $"cute_{signId}.png");

        try
        {
            if (File.Exists(path))
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenReadAsync();
                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = 512;
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }

            StartupDiagnostics.Log($"Zodiac PNG missing; using catalog fallback: theme={selectedTheme}, sign={signId}.");
            return await LoadCatalogFallbackAsync(month, selectedTheme);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Zodiac artwork could not load for month={month}, theme={selectedTheme}: {ex}");
            return await LoadCatalogFallbackAsync(month, selectedTheme);
        }
    }

    private static async Task<ImageSource?> LoadCatalogFallbackAsync(int month, string themeId)
    {
        try
        {
            var key = $"{themeId}:{month}";
            if (VectorFallbacks.TryGetValue(key, out var cached))
                return cached;

            var catalog = Catalog.Value;
            if (catalog is null) return null;

            var signs = catalog.RootElement.GetProperty("signs");
            var styles = catalog.RootElement.GetProperty("styles");
            var signId = SignIds[month - 1];
            var sign = signs.EnumerateArray().FirstOrDefault(x =>
                string.Equals(x.GetProperty("id").GetString(), signId, StringComparison.OrdinalIgnoreCase));
            var style = styles.EnumerateArray().FirstOrDefault(x =>
                string.Equals(x.GetProperty("id").GetString(), themeId, StringComparison.OrdinalIgnoreCase));
            if (sign.ValueKind == JsonValueKind.Undefined)
                return null;
            if (style.ValueKind == JsonValueKind.Undefined)
                style = styles.EnumerateArray().First();

            var bg = style.GetProperty("bg").GetString() ?? "#12102E";
            var accent = style.GetProperty("accent").GetString() ?? "#F5D28D";
            var glow = style.GetProperty("glow").GetString() ?? "#A78BFA";
            var stroke = style.TryGetProperty("stroke", out var strokeNode) ? strokeNode.GetDouble() : 3.5;
            var path = sign.GetProperty("path").GetString() ?? "";
            var svg = $"""
                <svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 100 100">
                  <defs><radialGradient id="disc"><stop offset="0" stop-color="{glow}" stop-opacity=".42"/><stop offset="1" stop-color="{bg}" stop-opacity=".10"/></radialGradient></defs>
                  <circle cx="50" cy="50" r="47" fill="url(#disc)"/>
                  <circle cx="50" cy="50" r="45" fill="none" stroke="{glow}" stroke-opacity=".48" stroke-width=".8"/>
                  <circle cx="50" cy="50" r="41.5" fill="none" stroke="{accent}" stroke-opacity=".52" stroke-width=".45" stroke-dasharray="1.2 2.2"/>
                  <path d="{path}" fill="none" stroke="{glow}" stroke-opacity=".38" stroke-width="{stroke + 2.1}" stroke-linecap="round" stroke-linejoin="round" transform="translate(3 3)"/>
                  <path d="{path}" fill="none" stroke="{accent}" stroke-width="{stroke}" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>
                """;

            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteString(svg);
                await writer.StoreAsync();
                await writer.FlushAsync();
            }
            stream.Seek(0);
            var source = new SvgImageSource();
            await source.SetSourceAsync(stream);
            VectorFallbacks.TryAdd(key, source);
            return source;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Zodiac catalog fallback failed: {ex.Message}");
            return null;
        }
    }
}
