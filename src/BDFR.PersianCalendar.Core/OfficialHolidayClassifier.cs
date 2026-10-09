using System.Text.RegularExpressions;

namespace BDFR.PersianCalendar.Core;

public static class OfficialHolidayClassifier
{
    public static bool IsOfficialHoliday(
        PersianDate date,
        string? title,
        bool sourceMarkedHoliday = false)
    {
        if (sourceMarkedHoliday)
            return true;

        if (IsFixedPersianHoliday(date))
            return true;

        var normalized = NormalizeTitle(title);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (normalized.Contains("تعطیل", StringComparison.OrdinalIgnoreCase))
            return true;

        if (TryExtractHijriDayMonth(normalized, out var hijriDay, out var hijriMonth) &&
            IsFixedHijriHoliday(hijriMonth, hijriDay))
        {
            return true;
        }

        string[] officialTitleKeywords =
        [
            "شهادت امام علی",
            "شهادت حضرت علی",
            "شهادت امیرالمؤمنین",
            "شهادت امیر المؤمنین",
            "عید فطر",
            "عید سعید فطر",
            "تاسوعا",
            "عاشورا",
            "اربعین",
            "رحلت پیامبر",
            "رحلت رسول اکرم",
            "شهادت امام حسن مجتبی",
            "شهادت امام رضا",
            "شهادت امام حسن عسکری",
            "ولادت پیامبر",
            "میلاد پیامبر",
            "ولادت امام صادق",
            "میلاد امام صادق",
            "شهادت حضرت فاطمه",
            "ولادت امام علی",
            "میلاد امام علی",
            "مبعث",
            "نیمه شعبان",
            "ولادت حضرت قائم",
            "شهادت امام جعفر صادق",
            "شهادت امام صادق",
            "شهادت امام محمد باقر",
            "عید قربان",
            "عید غدیر",
            "ولادت امام رضا",
            "میلاد امام رضا"
        ];

        return officialTitleKeywords.Any(keyword =>
            normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsFixedPersianHoliday(PersianDate date)
    {
        if (date.Month == 1 && date.Day is >= 1 and <= 4)
            return true;
        if (date.Month == 1 && date.Day is 12 or 13)
            return true;
        if (date.Month == 3 && date.Day is 14 or 15)
            return true;
        if (date.Month == 11 && date.Day == 22)
            return true;
        if (date.Month == 12 && date.Day == 29)
            return true;

        return false;
    }

    public static bool IsFixedHijriHoliday(int month, int day)
        => month switch
        {
            1 => day is 9 or 10,          // تاسوعا، عاشورا
            2 => day is 20 or 28,         // اربعین، رحلت پیامبر/امام حسن
            3 => day is 8 or 17,          // امام حسن عسکری، میلاد پیامبر/امام صادق
            6 => day == 3,                // حضرت فاطمه زهرا
            7 => day is 13 or 27,         // ولادت امام علی، مبعث
            8 => day == 15,               // نیمه شعبان
            9 => day == 21,               // شهادت امام علی
            10 => day is 1 or 2 or 25,    // عید فطر (دو روز)، امام صادق
            11 => day == 11,              // ولادت امام رضا
            12 => day is 7 or 10 or 18,   // امام باقر، قربان، غدیر
            _ => false
        };

    public static bool TryExtractHijriDayMonth(
        string? title,
        out int day,
        out int month)
    {
        day = 0;
        month = 0;

        var normalized = NormalizeTitle(title);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        normalized = PersianQuickAddParser.NormalizeDigits(normalized);

        var match = Regex.Match(
            normalized,
            @"(?<!\d)(?<day>\d{1,2})\s+(?<month>محرم|صفر|ربیع\s*الاول|ربیع\s*اول|ربیع\s*الثانی|ربیع\s*دوم|جمادی\s*الاول|جمادی\s*اول|جمادی\s*الثانی|جمادی\s*دوم|جمادی\s*الاخر|جمادی\s*آخر|رجب|شعبان|رمضان|شوال|ذی\s*القعده|ذیقعده|ذوالقعده|ذی\s*الحجه|ذیحجه|ذوالحجه)(?!\p{L})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success ||
            !int.TryParse(match.Groups["day"].Value, out day))
        {
            return false;
        }

        var monthToken = Regex.Replace(
            match.Groups["month"].Value,
            @"\s+",
            string.Empty);

        month = monthToken switch
        {
            "محرم" => 1,
            "صفر" => 2,
            "ربیعالاول" or "ربیعاول" => 3,
            "ربیعالثانی" or "ربیعدوم" => 4,
            "جمادیالاول" or "جمادیاول" => 5,
            "جمادیالثانی" or "جمادیدوم" or "جمادیالاخر" or "جمادیآخر" => 6,
            "رجب" => 7,
            "شعبان" => 8,
            "رمضان" => 9,
            "شوال" => 10,
            "ذیالقعده" or "ذیقعده" or "ذوالقعده" => 11,
            "ذیالحجه" or "ذیحجه" or "ذوالحجه" => 12,
            _ => 0
        };

        return month is >= 1 and <= 12 && day is >= 1 and <= 30;
    }

    private static string NormalizeTitle(string? title)
        => (title ?? string.Empty)
            .Replace('ي', 'ی')
            .Replace('ك', 'ک')
            .Replace('ۀ', 'ه')
            .Replace('ة', 'ه')
            .Replace('\u200c', ' ')
            .Replace('\u200f', ' ')
            .Replace('\ufeff', ' ')
            .Trim();
}
