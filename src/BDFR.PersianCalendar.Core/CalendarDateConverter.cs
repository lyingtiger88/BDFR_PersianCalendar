using System.Globalization;

namespace BDFR.PersianCalendar.Core;

public readonly record struct HijriDateValue(int Year, int Month, int Day)
{
    public override string ToString() => $"{Year:0000}/{Month:00}/{Day:00}";
}

public readonly record struct CalendarDateConversionResult(
    PersianDate Persian,
    DateOnly Gregorian,
    HijriDateValue Hijri);

public static class CalendarDateConverter
{
    public static CalendarDateConversionResult Convert(DateOnly gregorian)
    {
        var dateTime = gregorian.ToDateTime(TimeOnly.MinValue);
        var hijri = new HijriCalendar();

        return new CalendarDateConversionResult(
            PersianDate.FromDateOnly(gregorian),
            gregorian,
            new HijriDateValue(
                hijri.GetYear(dateTime),
                hijri.GetMonth(dateTime),
                hijri.GetDayOfMonth(dateTime)));
    }

    public static CalendarDateConversionResult Convert(
        CalendarSystemKind calendar,
        int year,
        int month,
        int day)
    {
        var gregorian = calendar switch
        {
            CalendarSystemKind.Persian =>
                new PersianDate(year, month, day).ToDateOnly(),

            CalendarSystemKind.Gregorian =>
                new DateOnly(year, month, day),

            CalendarSystemKind.Hijri =>
                DateOnly.FromDateTime(
                    new HijriCalendar().ToDateTime(
                        year,
                        month,
                        day,
                        0,
                        0,
                        0,
                        0)),

            _ => throw new ArgumentOutOfRangeException(
                nameof(calendar),
                calendar,
                "نوع تقویم پشتیبانی نمی‌شود.")
        };

        return Convert(gregorian);
    }

    public static bool TryConvertInput(
        string? input,
        CalendarSystemKind calendar,
        out CalendarDateConversionResult result,
        out string error)
    {
        result = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "تاریخ را وارد کنید.";
            return false;
        }

        var normalized = PersianQuickAddParser.NormalizeDigits(input.Trim())
            .Replace('\\', '/')
            .Replace('-', '/')
            .Replace('.', '/');

        var parts = normalized
            .Split(
                ['/', ' '],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            error = "فرمت تاریخ باید به شکل سال/ماه/روز باشد؛ مثل 1405/07/17.";
            return false;
        }

        try
        {
            result = Convert(calendar, year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            error = "این تاریخ در تقویم انتخاب‌شده معتبر نیست.";
            return false;
        }
        catch (Exception ex)
        {
            error = $"تبدیل تاریخ انجام نشد: {ex.Message}";
            return false;
        }
    }
}
