using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace BDFR.PersianCalendar.Desktop;

/// <summary>
/// Loads locally bundled vector artwork, independently of Windows app packaging.
/// Zodiac symbols are rendered as SVG illustrations, not font glyphs.
/// </summary>
internal static class ZodiacArtwork
{
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

    public static async Task<ImageSource?> LoadAsync(int month, string? theme)
    {
        if (month is < 1 or > 12 || Catalog.Value is null) return null;
        try
        {
            var root = Catalog.Value.RootElement;
            var sign = root.GetProperty("signs")[month - 1];
            var style = root.GetProperty("styles").EnumerateArray()
                .FirstOrDefault(s => s.GetProperty("id").GetString() == theme);
            if (style.ValueKind == JsonValueKind.Undefined)
                style = root.GetProperty("styles")[0];

            var back = style.GetProperty("bg").GetString() ?? "#12102E";
            var accent = style.GetProperty("accent").GetString() ?? "#F5D28D";
            var glow = style.GetProperty("glow").GetString() ?? "#A78BFA";
            var width = style.GetProperty("stroke").GetDouble();
            var path = System.Security.SecurityElement.Escape(sign.GetProperty("path").GetString()) ?? "";

            var svg = $"""
                <svg xmlns="http://www.w3.org/2000/svg" width="240" height="240" viewBox="0 0 100 100">
                  <defs>
                    <radialGradient id="nebula" cx="50%" cy="42%" r="72%">
                      <stop offset="0" stop-color="{glow}" stop-opacity=".47"/>
                      <stop offset=".58" stop-color="{back}" stop-opacity=".9"/>
                      <stop offset="1" stop-color="{back}"/>
                    </radialGradient>
                    <linearGradient id="ink" x1="0" y1="0" x2="1" y2="1">
                      <stop offset="0" stop-color="#ffffff" stop-opacity=".94"/>
                      <stop offset=".46" stop-color="{accent}"/>
                      <stop offset="1" stop-color="{glow}"/>
                    </linearGradient>
                  </defs>
                  <rect x="1" y="1" width="98" height="98" rx="21" fill="url(#nebula)"/>
                  <circle cx="50" cy="50" r="44" fill="none" stroke="{accent}" stroke-opacity=".35" stroke-width=".5"/>
                  <circle cx="50" cy="50" r="39" fill="none" stroke="{glow}" stroke-opacity=".24" stroke-width=".4"/>
                  <g fill="{accent}" opacity=".85"><circle cx="18" cy="18" r="1.1"/><circle cx="82" cy="18" r=".8"/><circle cx="16" cy="74" r=".7"/><circle cx="82" cy="80" r="1.1"/></g>
                  <path d="{path}" fill="none" stroke="{back}" stroke-opacity=".65" stroke-width="{width + 2.4}" stroke-linejoin="round" stroke-linecap="round"/>
                  <path d="{path}" fill="none" stroke="url(#ink)" stroke-width="{width}" stroke-linejoin="round" stroke-linecap="round"/>
                </svg>
                """;

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(Encoding.UTF8.GetBytes(svg));
                await writer.StoreAsync();
                await writer.FlushAsync();
            }
            stream.Seek(0);
            var image = new SvgImageSource();
            var status = await image.SetSourceAsync(stream);
            if (status != SvgImageSourceLoadStatus.Success)
            {
                StartupDiagnostics.Log($"Zodiac SVG load status: {status} for month={month} theme={theme}.");
                return null;
            }
            return image;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Zodiac artwork could not load for month={month}: {ex}");
            return null;
        }
    }
}
