namespace BDFR.PersianCalendar.Desktop;

public sealed class AppSettings
{
    // AppearanceMode: "elena" or "theme".
    public string AppearanceMode { get; set; } = "";

    // Used only when AppearanceMode == "theme".
    public string ThemeId { get; set; } = "zara-pastel";

    // Used only when AppearanceMode == "elena".
    public string ElenaAccentId { get; set; } = "azure";

    // Theme-mode wallpaper: "custom" or "none".
    // "elena" is accepted only as a legacy value and is migrated to AppearanceMode.
    public string? BackgroundMode { get; set; }

    // Legacy compatibility; Elena now owns seasonal background selection.
    public bool UseSeasonalBackground { get; set; } = true;

    public string? CustomBackgroundPath { get; set; }
    public double BackgroundOpacity { get; set; } = 0.22;
    public bool ShowOccasionPictures { get; set; } = true;
}
