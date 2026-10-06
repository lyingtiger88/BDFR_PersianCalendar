using BDFR.PersianCalendar.Core;
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

        var fallback = CreateBuiltInTheme(normalized);
        var path = Path.Combine(ThemesRoot, normalized, "theme.json");

        try
        {
            if (File.Exists(path))
            {
                var parsed = JsonSerializer.Deserialize<ThemeDefinition>(
                    File.ReadAllText(path),
                    JsonOptions);

                if (parsed is not null &&
                    string.Equals(parsed.Id, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    StartupDiagnostics.Log(
                        $"Theme loaded from packaged JSON: {parsed.Id} ({path})");
                    return parsed;
                }

                StartupDiagnostics.Log(
                    $"Theme JSON was invalid or had a mismatched id ({normalized}); built-in palette used.");
            }
            else
            {
                StartupDiagnostics.Log(
                    $"Theme JSON not found ({path}); built-in palette used for {normalized}.");
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log(
                $"Theme load failed ({normalized}); built-in palette used: {ex}");
        }

        return fallback;
    }

    public string ThemeRoot(ThemeDefinition theme)
        => Path.Combine(ThemesRoot, theme.Id);

    public ThemeDefinition CreateElenaTheme(
        string? accentId,
        string? dayAccentId,
        int persianMonth)
    {
        var accent = AppearanceCatalog.GetElenaAccent(accentId, persianMonth);
        var dayAccent = AppearanceCatalog.GetElenaDayColor(dayAccentId, persianMonth);

        var seasonalSurface = persianMonth switch
        {
            <= 3 => new[]
            {
                "#FFF8F2F5", "#D8FFFFFF", "#C8FFFFFF",
                "#FFF6EAF0", "#FFF3EDF2", "#FFF7EEF3"
            },
            <= 6 => new[]
            {
                "#FFF0F7FB", "#D8FFFFFF", "#C8FFFFFF",
                "#FFE9F4FA", "#FFE6F2F9", "#FFECF6FB"
            },
            <= 9 => new[]
            {
                "#FFFAF4EC", "#D8FFFFFF", "#C8FFFFFF",
                "#FFF7EEE4", "#FFF5EBE0", "#FFF8F0E7"
            },
            _ => new[]
            {
                "#FFF1F4FA", "#D8FFFFFF", "#C8FFFFFF",
                "#FFE9EEF8", "#FFE7ECF6", "#FFEDF1FA"
            }
        };

        return new ThemeDefinition
        {
            Id = "elena-neutral",
            DisplayName = "Elena Mode",
            WindowBackground = seasonalSurface[0],
            PanelBackground = seasonalSurface[1],
            CardBackground = seasonalSurface[2],
            PrimaryText = "#FF202631",
            SecondaryText = "#FF697386",
            Accent = accent.Accent,
            SelectedDay = dayAccent.Color,
            HolidayText = "#FFD34E5E",
            WeekdayColors =
            [
                seasonalSurface[3],
                seasonalSurface[4],
                seasonalSurface[5],
                seasonalSurface[3],
                seasonalSurface[4],
                seasonalSurface[5],
                "#FFF6E7EA"
            ]
        };
    }

    public ThemeDefinition ResolveAppearance(AppSettings settings, int persianMonth)
        => string.Equals(settings.AppearanceMode, "elena", StringComparison.OrdinalIgnoreCase)
            ? CreateElenaTheme(
                settings.ElenaAccentId,
                settings.ElenaDayAccentId,
                persianMonth)
            : Load(settings.ThemeId);

    public ThemeDefinition ResolveAppearance(AppSettings settings)
        => ResolveAppearance(settings, PersianDate.Today().Month);

    private static ThemeDefinition CreateBuiltInTheme(string id)
        => id switch
        {
            "windows-light" => new ThemeDefinition
            {
                Id = "windows-light",
                DisplayName = "Windows Light",
                WindowBackground = "#FFF3F3F3",
                PanelBackground = "#FFFFFFFF",
                CardBackground = "#FFF9F9F9",
                PrimaryText = "#FF1F1F1F",
                SecondaryText = "#FF616161",
                Accent = "#FF0F6CBD",
                SelectedDay = "#FFD7E9F8",
                HolidayText = "#FFC42B1C",
                WeekdayColors =
                [
                    "#FFF5F5F5", "#FFF2F5F8", "#FFF5F5F5",
                    "#FFF2F5F8", "#FFF5F5F5", "#FFF2F5F8", "#FFFBE9EA"
                ]
            },
            "azure-glass" => new ThemeDefinition
            {
                Id = "azure-glass",
                DisplayName = "Azure Glass",
                WindowBackground = "#FFE4F3FF",
                PanelBackground = "#CDEFF8FF",
                CardBackground = "#B8D9EEFF",
                PrimaryText = "#FF103753",
                SecondaryText = "#FF587489",
                Accent = "#FF2389C9",
                SelectedDay = "#FF71C5F2",
                HolidayText = "#FFD14F61",
                WeekdayColors =
                [
                    "#FFCFEAFF", "#FFD9F0FF", "#FFCFEAFF",
                    "#FFD9F0FF", "#FFCFEAFF", "#FFD9F0FF", "#FFF1D8E0"
                ]
            },
            "graphite-night" => new ThemeDefinition
            {
                Id = "graphite-night",
                DisplayName = "Graphite Night",
                WindowBackground = "#FF15181D",
                PanelBackground = "#F022272E",
                CardBackground = "#E72C323B",
                PrimaryText = "#FFF4F7FA",
                SecondaryText = "#FFABB5C3",
                Accent = "#FF71839B",
                SelectedDay = "#FF435B76",
                HolidayText = "#FFFF7A86",
                WeekdayColors =
                [
                    "#FF29313A", "#FF303843", "#FF29313A",
                    "#FF303843", "#FF29313A", "#FF303843", "#FF422F35"
                ]
            },
            "warm-sand" => new ThemeDefinition
            {
                Id = "warm-sand",
                DisplayName = "Warm Sand",
                WindowBackground = "#FFF4E8D8",
                PanelBackground = "#FFF8EEDF",
                CardBackground = "#FFFFF8ED",
                PrimaryText = "#FF49392B",
                SecondaryText = "#FF7B6958",
                Accent = "#FFC9915A",
                SelectedDay = "#FFE4BB89",
                HolidayText = "#FFC65B55",
                WeekdayColors =
                [
                    "#FFF1DEC8", "#FFF5E5D1", "#FFF1DEC8",
                    "#FFF5E5D1", "#FFF1DEC8", "#FFF5E5D1", "#FFF2D8D4"
                ]
            },
            _ => new ThemeDefinition
            {
                Id = "zara-pastel",
                DisplayName = "Zara Pastel",
                WindowBackground = "#FFFDF9F6",
                PanelBackground = "#E6FFFFFF",
                CardBackground = "#EEFFFFFF",
                PrimaryText = "#FF1E2A44",
                SecondaryText = "#FF6B7280",
                Accent = "#FFC9C3FF",
                SelectedDay = "#FF7CC9F5",
                HolidayText = "#FFD65A6F",
                WeekdayColors =
                [
                    "#FFF9DDD3", "#FFF9EDC4", "#FFDDF0D9",
                    "#FFD5EFEF", "#FFDCE9FA", "#FFE7E0FA", "#FFF8DCE5"
                ]
            }
        };

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
