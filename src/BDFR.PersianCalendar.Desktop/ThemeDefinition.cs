namespace BDFR.PersianCalendar.Desktop;

public sealed class ThemeDefinition
{
    public string Id { get; set; } = "zara-pastel";
    public string DisplayName { get; set; } = "Zara Pastel";
    public string WindowBackground { get; set; } = "#FFFDF9F6";
    public string PanelBackground { get; set; } = "#EFFFFFFF";
    public string CardBackground { get; set; } = "#F7FFFFFF";
    public string PrimaryText { get; set; } = "#FF1E2A44";
    public string SecondaryText { get; set; } = "#FF6B7280";
    public string Accent { get; set; } = "#FFC9C3FF";
    public string SelectedDay { get; set; } = "#FF7CC9F5";
    public string HolidayText { get; set; } = "#FFD65A6F";
    public string[] WeekdayColors { get; set; } =
    [
        "#FFF9DDD3",
        "#FFF9EDC4",
        "#FFDDF0D9",
        "#FFD5EFEF",
        "#FFDCE9FA",
        "#FFE7E0FA",
        "#FFF8DCE5"
    ];

    public Dictionary<string, string> SeasonalBackgrounds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["spring"] = "backgrounds/spring.jpg",
            ["summer"] = "backgrounds/summer.jpg",
            ["autumn"] = "backgrounds/autumn.jpg",
            ["winter"] = "backgrounds/winter.jpg"
        };
}
