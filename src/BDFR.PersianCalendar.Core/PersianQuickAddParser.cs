using System.Text.RegularExpressions;

namespace BDFR.PersianCalendar.Core;

public sealed class PersianQuickAddParser
{
    private static readonly string[] Weekdays =
        ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه"];

    public QuickAddResult Parse(string input, PersianDate? today = null)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("متن فعالیت خالی است.", nameof(input));

        today ??= PersianDate.Today();
        var text = NormalizeDigits(input.Trim());
        var date = ResolveDate(text, today.Value);
        var time = ResolveTime(text);
        var recurrence = ResolveRecurrence(text);
        var title = CleanupTitle(text);

        if (string.IsNullOrWhiteSpace(title)) title = "فعالیت جدید";
        return new QuickAddResult(title, date, time, recurrence);
    }

    private static PersianDate ResolveDate(string text, PersianDate today)
    {
        if (text.Contains("پس فردا", StringComparison.Ordinal)) return today.AddDays(2);
        if (text.Contains("فردا", StringComparison.Ordinal)) return today.AddDays(1);
        if (text.Contains("امروز", StringComparison.Ordinal)) return today;

        var full = Regex.Match(text, @"(1[34]d{2})[/-](d{1,2})[/-](d{1,2})");
        if (full.Success)
            return new PersianDate(int.Parse(full.Groups[1].Value), int.Parse(full.Groups[2].Value), int.Parse(full.Groups[3].Value));

        for (var i = 0; i < PersianDate.MonthNames.Length; i++)
        {
            var m = Regex.Match(text, $@"(d{{1,2}})s+{Regex.Escape(PersianDate.MonthNames[i])}");
            if (m.Success)
            {
                var candidate = new PersianDate(today.Year, i + 1, int.Parse(m.Groups[1].Value));
                if (candidate.CompareTo(today) < 0) candidate = new PersianDate(today.Year + 1, i + 1, int.Parse(m.Groups[1].Value));
                return candidate;
            }
        }

        for (var i = 0; i < Weekdays.Length; i++)
        {
            if (!text.Contains(Weekdays[i], StringComparison.Ordinal)) continue;
            var cursor = today;
            for (var d = 1; d <= 7; d++)
            {
                cursor = today.AddDays(d);
                if (cursor.PersianDayOfWeek == Weekdays[i]) return cursor;
            }
        }

        return today;
    }

    private static TimeOnly? ResolveTime(string text)
    {
        var m = Regex.Match(text, @"(?:ساعتs*)?(d{1,2})(?::(d{1,2}))");
        if (!m.Success) return null;
        var hour = int.Parse(m.Groups[1].Value);
        var minute = int.Parse(m.Groups[2].Value);
        return hour is >= 0 and <= 23 && minute is >= 0 and <= 59 ? new TimeOnly(hour, minute) : null;
    }

    private static RecurrenceKind ResolveRecurrence(string text)
    {
        if (text.Contains("هر روز", StringComparison.Ordinal)) return RecurrenceKind.Daily;
        if (text.Contains("هر هفته", StringComparison.Ordinal) || Weekdays.Any(d => text.Contains($"هر {d}", StringComparison.Ordinal))) return RecurrenceKind.Weekly;
        if (text.Contains("هر ماه", StringComparison.Ordinal)) return RecurrenceKind.Monthly;
        if (text.Contains("هر سال", StringComparison.Ordinal) || text.Contains("سالانه", StringComparison.Ordinal)) return RecurrenceKind.Yearly;
        return RecurrenceKind.None;
    }

    private static string CleanupTitle(string text)
    {
        text = Regex.Replace(text, @"(امروز|فردا|پس فردا)", "");
        text = Regex.Replace(text, @"1[34]d{2}[/-]d{1,2}[/-]d{1,2}", "");
        text = Regex.Replace(text, @"(?:ساعتs*)?d{1,2}:d{1,2}", "");
        text = Regex.Replace(text, @"هرs+(روز|هفته|ماه|سال)", "");
        foreach (var month in PersianDate.MonthNames) text = Regex.Replace(text, $@"d{{1,2}}s+{Regex.Escape(month)}", "");
        return Regex.Replace(text, @"s{2,}", " ").Trim(' ', '-', '،', ',');
    }

    public static string NormalizeDigits(string value)
    {
        const string fa = "۰۱۲۳۴۵۶۷۸۹";
        const string ar = "٠١٢٣٤٥٦٧٨٩";
        for (var i = 0; i < 10; i++)
            value = value.Replace(fa[i], (char)('0' + i)).Replace(ar[i], (char)('0' + i));
        return value;
    }
}