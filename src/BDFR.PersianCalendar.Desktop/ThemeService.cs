using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public string SeasonalBackgroundsRoot => Path.Combine(PictureRoot, "theme", "season backgrounds");

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

    public ThemeDefinition CreateElenaTheme(string? accentId)
    {
        var accent = AppearanceCatalog.GetElenaAccent(accentId);

        return new ThemeDefinition
        {
            Id = "elena-neutral",
            DisplayName = "Elena Mode",
            WindowBackground = "#FFF2F4F7",
            PanelBackground = "#D8FFFFFF",
            CardBackground = "#C8FFFFFF",
            PrimaryText = "#FF202631",
            SecondaryText = "#FF697386",
            Accent = accent.Accent,
            SelectedDay = accent.SelectedDay,
            HolidayText = "#FFD34E5E",
            WeekdayColors =
            [
                "#FFE8ECF1",
                "#FFE6EAF0",
                "#FFE9EDF2",
                "#FFE7EBF0",
                "#FFE8ECF1",
                "#FFE6EAF0",
                "#FFE9EDF2"
            ]
        };
    }

    public ThemeDefinition ResolveAppearance(AppSettings settings)
        => string.Equals(settings.AppearanceMode, "elena", StringComparison.OrdinalIgnoreCase)
            ? CreateElenaTheme(settings.ElenaAccentId)
            : Load(settings.ThemeId);


    public string? GetSeasonalBackgroundPath(int persianMonth, double targetAspectRatio)
    {
        var season = persianMonth switch
        {
            <= 3 => "spring",
            <= 6 => "summer",
            <= 9 => "autumn",
            _ => "winter"
        };

        if (!Directory.Exists(SeasonalBackgroundsRoot))
            return null;

        var allowedExtensions = new HashSet<string>(
            [".svg", ".png", ".jpg", ".jpeg", ".webp"],
            StringComparer.OrdinalIgnoreCase);

        var files = Directory
            .EnumerateFiles(SeasonalBackgroundsRoot, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => allowedExtensions.Contains(Path.GetExtension(path)))
            .ToArray();

        var candidates = new List<(string Path, double Ratio, bool HasRatio)>();

        foreach (var path in files)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(name, season, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add((path, 0, false));
                continue;
            }

            var match = Regex.Match(
                name,
                $@"^{Regex.Escape(season)}[_\-\s]?(?<w>\d{{1,3}})x(?<h>\d{{1,3}})$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!match.Success ||
                !double.TryParse(match.Groups["w"].Value, out var width) ||
                !double.TryParse(match.Groups["h"].Value, out var height) ||
                width <= 0 ||
                height <= 0)
                continue;

            candidates.Add((path, width / height, true));
        }

        if (candidates.Count == 0)
            return null;

        targetAspectRatio = targetAspectRatio > 0
            ? targetAspectRatio
            : 16d / 9d;

        var ratioCandidates = candidates
            .Where(x => x.HasRatio)
            .OrderBy(x => Math.Abs(x.Ratio - targetAspectRatio))
            .ThenBy(x => Path.GetExtension(x.Path).Equals(".svg", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToArray();

        if (ratioCandidates.Length > 0)
            return ratioCandidates[0].Path;

        return candidates.FirstOrDefault(x => !x.HasRatio).Path;
    }

    public static string FormatAspectRatioLabel(double aspectRatio)
    {
        var known = new (int W, int H)[]
        {
            (32, 9),
            (21, 9),
            (16, 9),
            (16, 10),
            (3, 2),
            (4, 3),
            (5, 4)
        };

        var best = known
            .OrderBy(x => Math.Abs((double)x.W / x.H - aspectRatio))
            .First();

        return $"{best.W}x{best.H}";
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
