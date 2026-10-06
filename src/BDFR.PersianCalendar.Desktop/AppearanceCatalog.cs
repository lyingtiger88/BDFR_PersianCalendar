namespace BDFR.PersianCalendar.Desktop;

public sealed record ElenaAccentDefinition(
    string Id,
    string DisplayName,
    string Accent,
    string SelectedDay);

public sealed record ThemeCatalogItem(
    string Id,
    string DisplayName,
    string Description);

public static class AppearanceCatalog
{
    public static readonly ThemeCatalogItem[] Themes =
    [
        new("zara-pastel", "Zara Pastel", "پاستلی رنگارنگ و روشن"),
        new("windows-light", "Windows Light", "روشن، ساده و نزدیک به ظاهر ویندوز"),
        new("azure-glass", "Azure Glass", "شیشه‌ای با طیف آبی"),
        new("graphite-night", "Graphite Night", "تیره و خنثی برای استفاده شب"),
        new("warm-sand", "Warm Sand", "کرم گرم و آرام")
    ];

    public static readonly ElenaAccentDefinition[] ElenaAccents =
    [
        new("azure", "آبی", "#FF4F8EDC", "#FF79B4F4"),
        new("violet", "بنفش", "#FF8B6FD8", "#FFA98FEA"),
        new("emerald", "سبز", "#FF3E9B78", "#FF67B995"),
        new("coral", "مرجانی", "#FFD66D68", "#FFEC918C"),
        new("amber", "کهربایی", "#FFC49139", "#FFE0B35F"),
        new("graphite", "گرافیتی", "#FF667085", "#FF8B95A7")
    ];

    public static ElenaAccentDefinition GetElenaAccent(string? id)
        => ElenaAccents.FirstOrDefault(x =>
               string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
           ?? ElenaAccents[0];
}
