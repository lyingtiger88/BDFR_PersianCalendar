namespace BDFR.PersianCalendar.Desktop;

public sealed class AppearanceColorOverrides
{
    public string? Accent { get; set; }
    public string? SelectedDay { get; set; }
    public string? Holiday { get; set; }
    public string? Panel { get; set; }
    public string? Card { get; set; }
    public string? Calendar { get; set; }

    public bool IsEmpty()
        => string.IsNullOrWhiteSpace(Accent) &&
           string.IsNullOrWhiteSpace(SelectedDay) &&
           string.IsNullOrWhiteSpace(Holiday) &&
           string.IsNullOrWhiteSpace(Panel) &&
           string.IsNullOrWhiteSpace(Card) &&
           string.IsNullOrWhiteSpace(Calendar);
}

public sealed class AppSettings
{
    // AppearanceMode: "elena" or "theme".
    public string AppearanceMode { get; set; } = "";

    // Used only when AppearanceMode == "theme".
    public string ThemeId { get; set; } = "zara-pastel";

    // Used only when AppearanceMode == "elena".
    // "seasonal" follows the currently displayed Persian season automatically.
    public string ElenaAccentId { get; set; } = "seasonal";

    // Selected-day color is independent from the general Elena accent.
    public string ElenaDayAccentId { get; set; } = "seasonal";

    // Internal migration marker for appearance settings.
    public int AppearanceSettingsVersion { get; set; }

    // Theme-mode wallpaper: "custom" or "none".
    // "elena" is accepted only as a legacy value and is migrated to AppearanceMode.
    public string? BackgroundMode { get; set; }

    // Legacy compatibility; Elena now owns seasonal background selection.
    public bool UseSeasonalBackground { get; set; } = true;

    public string? CustomBackgroundPath { get; set; }
    public double BackgroundOpacity { get; set; } = 0.22;
    public bool ShowOccasionPictures { get; set; } = true;

    // Zara Pastel optional Liquid Glass presentation.
    public bool GlassMode { get; set; }

    // Global corner radius for cards and interactive UI elements.
    // 0 = square corners, 32 = strongly rounded.
    public double UiCornerRadius { get; set; } = 14.0;

    // Global UI typography.
    public string FontFamilyName { get; set; } = "Segoe UI Variable";

    // Base UI size. Existing visual hierarchy is scaled relative to 14px.
    public double FontSize { get; set; } = 14.0;

    // light / normal / semibold / bold.
    // "normal" preserves the designed weight hierarchy.
    public string FontWeightMode { get; set; } = "normal";

    // normal / italic.
    public string FontStyleMode { get; set; } = "normal";

    // Per-appearance color overrides. Theme modes get their own palette and
    // Elena gets one palette per season (spring/summer/autumn/winter).
    public Dictionary<string, AppearanceColorOverrides> ColorOverrides { get; set; } = new();
}
