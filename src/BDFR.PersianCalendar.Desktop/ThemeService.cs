using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

public sealed class ThemeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string PictureRoot => Path.Combine(AppContext.BaseDirectory, "picture");
    public string ThemesRoot => Path.Combine(PictureRoot, "themes");

    public ThemeDefinition Load(string themeId)
    {
        var normalized = string.IsNullOrWhiteSpace(themeId)
            ? "zara-pastel"
            : themeId.Trim().ToLowerInvariant();

        var path = Path.Combine(ThemesRoot, normalized, "theme.json");
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<ThemeDefinition>(
                           File.ReadAllText(path),
                           JsonOptions)
                       ?? new ThemeDefinition();
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Theme load failed ({normalized}): {ex}");
        }

        return new ThemeDefinition();
    }

    public string ThemeRoot(ThemeDefinition theme)
        => Path.Combine(ThemesRoot, theme.Id);

    public string? GetSeasonalBackgroundPath(ThemeDefinition theme, int persianMonth)
    {
        var season = persianMonth switch
        {
            <= 3 => "spring",
            <= 6 => "summer",
            <= 9 => "autumn",
            _ => "winter"
        };

        if (!theme.SeasonalBackgrounds.TryGetValue(season, out var relative) ||
            string.IsNullOrWhiteSpace(relative))
            return null;

        return Path.Combine(ThemeRoot(theme), relative.Replace('/', Path.DirectorySeparatorChar));
    }

    public static SolidColorBrush Brush(string value, string fallback = "#FFFFFFFF")
        => new(ParseColor(value, fallback));

    public static Windows.UI.Color ParseColor(string value, string fallback = "#FFFFFFFF")
    {
        try
        {
            var hex = (value ?? fallback).Trim().TrimStart('#');
            if (hex.Length == 6) hex = "FF" + hex;
            if (hex.Length != 8) hex = fallback.TrimStart('#');

            return Windows.UI.Color.FromArgb(
                byte.Parse(hex[..2], NumberStyles.HexNumber),
                byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber));
        }
        catch
        {
            return Windows.UI.Color.FromArgb(255, 255, 255, 255);
        }
    }
}
