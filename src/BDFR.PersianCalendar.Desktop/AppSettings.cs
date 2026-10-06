namespace BDFR.PersianCalendar.Desktop;

public sealed class AppSettings
{
    public string ThemeId { get; set; } = "zara-pastel";
    public bool UseSeasonalBackground { get; set; } = true;
    public string? CustomBackgroundPath { get; set; }
    public double BackgroundOpacity { get; set; } = 0.16;
    public bool ShowOccasionPictures { get; set; } = true;
}
