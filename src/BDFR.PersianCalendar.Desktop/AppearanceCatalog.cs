namespace BDFR.PersianCalendar.Desktop;

public sealed record ElenaAccentDefinition(
    string Id,
    string DisplayName,
    string Accent,
    string SelectedDay);

public sealed record ElenaDayColorDefinition(
    string Id,
    string DisplayName,
    string Color);

public sealed record ThemeCatalogItem(
    string Id,
    string DisplayName,
    string Description);

public static class AppearanceCatalog
{
    public static readonly ThemeCatalogItem[] Themes =
    [
        new("zara-pastel", "Zara Pastel", "پاستلی رنگارنگ و روشن"),
        new("windows-light", "Windows Light", "روشن و مینیمال با حس Fluent ویندوز"),
        new("azure-glass", "Azure Glass", "شیشه‌ای با طیف آبی و پنل‌های سرد"),
        new("graphite-night", "Graphite Night", "تیره، گرافیتی و مناسب استفاده شب"),
        new("warm-sand", "Warm Sand", "کرم گرم، شنی و آرام")
    ];

    public static readonly ElenaAccentDefinition[] ElenaAccents =
    [
        new("seasonal", "خودکار فصلی", "#FF4F8EDC", "#FF79B4F4"),
        new("azure", "آبی", "#FF4F8EDC", "#FF79B4F4"),
        new("violet", "بنفش", "#FF8B6FD8", "#FFA98FEA"),
        new("emerald", "سبز", "#FF3E9B78", "#FF67B995"),
        new("coral", "مرجانی", "#FFD66D68", "#FFEC918C"),
        new("amber", "کهربایی", "#FFC49139", "#FFE0B35F"),
        new("graphite", "گرافیتی", "#FF667085", "#FF8B95A7")
    ];

    public static readonly ElenaDayColorDefinition[] ElenaDayColors =
    [
        new("seasonal", "خودکار فصلی", "#FF67B995"),
        new("azure", "آبی روشن", "#FF79B4F4"),
        new("violet", "بنفش روشن", "#FFA98FEA"),
        new("emerald", "سبز روشن", "#FF67B995"),
        new("coral", "مرجانی روشن", "#FFEC918C"),
        new("amber", "کهربایی روشن", "#FFE0B35F"),
        new("graphite", "گرافیتی روشن", "#FF8B95A7")
    ];

    public static ElenaAccentDefinition GetElenaAccent(string? id, int persianMonth)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            string.Equals(id, "seasonal", StringComparison.OrdinalIgnoreCase))
            return GetSeasonalAccent(persianMonth);

        return ElenaAccents.FirstOrDefault(x =>
                   string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
               ?? GetSeasonalAccent(persianMonth);
    }

    public static ElenaDayColorDefinition GetElenaDayColor(string? id, int persianMonth)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            string.Equals(id, "seasonal", StringComparison.OrdinalIgnoreCase))
            return GetSeasonalDayColor(persianMonth);

        return ElenaDayColors.FirstOrDefault(x =>
                   string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
               ?? GetSeasonalDayColor(persianMonth);
    }

    public static ElenaDayColorDefinition GetSeasonalDayColor(int persianMonth)
        => persianMonth switch
        {
            <= 3 => new("seasonal", "بهاری · سبز جوانه", "#FF6FB482"),
            <= 6 => new("seasonal", "تابستانی · طلایی", "#FFF0C35A"),
            <= 9 => new("seasonal", "پاییزی · سبز زیتونی", "#FF6E9278"),
            _ => new("seasonal", "زمستانی · بنفش یخی", "#FFA08AD8")
        };

    public static ElenaAccentDefinition GetSeasonalAccent(int persianMonth)
        => persianMonth switch
        {
            <= 3 => new(
                "seasonal",
                "بهاری · شکوفه‌ای",
                "#FFD46F9E",
                "#FFF0A4C5"),
            <= 6 => new(
                "seasonal",
                "تابستانی · آبی آسمانی",
                "#FF2F8FCE",
                "#FF68BDE8"),
            <= 9 => new(
                "seasonal",
                "پاییزی · کهربایی",
                "#FFC7772F",
                "#FFE3A15C"),
            _ => new(
                "seasonal",
                "زمستانی · آبی یخی",
                "#FF5B75C8",
                "#FF91A7E4")
        };
}
