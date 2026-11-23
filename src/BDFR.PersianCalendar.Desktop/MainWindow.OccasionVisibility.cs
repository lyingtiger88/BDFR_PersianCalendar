using BDFR.PersianCalendar.Core;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    // The current time.ir import stores all occasions with one "time.ir"
    // source value, without an explicit calendar system. This classifier
    // therefore uses reliable title/source cues and otherwise treats the
    // event as Persian. No record is modified or deleted.
    private bool IsOccasionVisible(Occasion item)
    {
        if (string.Equals(item.Source, "personal", StringComparison.OrdinalIgnoreCase))
            return true; // Personal events have their own independent visibility.

        var source = item.Source?.ToLowerInvariant() ?? "";
        var title = item.Title?.Trim() ?? "";
        if (source.Contains("hijri") || source.Contains("lunar") ||
            source.Contains("islamic") || IsHijriOccasionTitle(title))
            return _settings.ShowHijriOccasions;

        if (source.Contains("gregorian") || source.Contains("international") ||
            source.Contains("global") || IsGregorianOccasionTitle(title))
            return _settings.ShowGregorianOccasions;

        return _settings.ShowPersianOccasions;
    }

    private static bool IsHijriOccasionTitle(string title)
    {
        string[] phrases =
        [
            "رمضان", "عید فطر", "عید قربان", "غدیر", "مبعث", "شعبان", "محرم",
            "صفر", "اربعین", "عاشورا", "تاسوعا", "میلاد پیامبر", "ولادت پیامبر",
            "رحلت پیامبر", "وفات پیامبر", "ولادت امام", "شهادت امام",
            "ولادت حضرت", "شهادت حضرت", "میلاد حضرت", "وفات حضرت",
            "نیمه شعبان", "عرفه", "شب قدر", "لیلة القدر", "رجب",
            "ذی الحجه", "ذیحجه", "ذی‌الحجه", "جمادی", "ربیع الاول", "ربیع‌الاول"
        ];
        return phrases.Any(x=> title.Contains(x,StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGregorianOccasionTitle(string title)
    {
        string[] phrases =
        [
            "روز جهانی", "روز بین‌المللی", "روز بين المللي", "روز بین المللی",
            "هفته جهانی", "روز زمین", "کریسمس", "میلاد مسیح", "سال نو میلادی",
            "هالووین", "ولنتاین", "روز ولنتاین"
        ];
        return phrases.Any(x=>title.Contains(x,StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyOccasionVisibility()
    {
        _ = LoadCalendarCellOccasionsAsync(_year, _month);
        _ = LoadSelectedDayAsync();
        if (_activeDesktopModule == "مناسبت‌ها")
            ShowDesktopModule("مناسبت‌ها");
    }
}
