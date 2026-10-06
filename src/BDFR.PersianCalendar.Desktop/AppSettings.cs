namespace BDFR.PersianCalendar.Desktop;

public sealed class AppSettings
{
    public string ThemeId { get; set; } = "zara-pastel";

    // null keeps backward compatibility with settings written before background modes existed.
    // Supported values: elena, custom, none.
    public string? BackgroundMode { get; set; }

    public bool UseSeasonalBackground { get; set; } = true;
    public string? CustomBackgroundPath { get; set; }
    public double BackgroundOpacity { get; set; } = 0.22;
    public bool ShowOccasionPictures { get; set; } = true;
}
