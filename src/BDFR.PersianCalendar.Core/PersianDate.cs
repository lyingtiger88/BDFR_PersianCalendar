using System.Globalization;

namespace BDFR.PersianCalendar.Core;

public readonly record struct PersianDate : IComparable<PersianDate>
{
    private static readonly PersianCalendar Calendar = new();

    public static readonly string[] MonthNames =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "اَمرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    ];

    public int Year { get; }
    public int Month { get; }
    public int Day { get; }

    public PersianDate(int year, int month, int day)
    {
        if (year is < 1 or > 9377) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        var max = Calendar.GetDaysInMonth(year, month);
        if (day is < 1 || day > max) throw new ArgumentOutOfRangeException(nameof(day));

        Year = year;
        Month = month;
        Day = day;
    }

    public string MonthName => MonthNames[Month - 1];

    public DateOnly ToDateOnly()
        => DateOnly.FromDateTime(Calendar.ToDateTime(Year, Month, Day, 0, 0, 0, 0));

    public DateTime ToDateTime()
        => Calendar.ToDateTime(Year, Month, Day, 0, 0, 0, 0, DateTimeKind.Unspecified);

    public PersianDate AddDays(int days) => FromDateOnly(ToDateOnly().AddDays(days));

    public static PersianDate FromDateOnly(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return new PersianDate(Calendar.GetYear(dt), Calendar.GetMonth(dt), Calendar.GetDayOfMonth(dt));
    }

    public static PersianDate Today() => FromDateOnly(DateOnly.FromDateTime(DateTime.Today));

    public static int DaysInMonth(int year, int month) => Calendar.GetDaysInMonth(year, month);

    public DayOfWeek DayOfWeek => ToDateOnly().DayOfWeek;

    public string PersianDayOfWeek => DayOfWeek switch
    {
        DayOfWeek.Saturday => "شنبه",
        DayOfWeek.Sunday => "یکشنبه",
        DayOfWeek.Monday => "دوشنبه",
        DayOfWeek.Tuesday => "سه‌شنبه",
        DayOfWeek.Wednesday => "چهارشنبه",
        DayOfWeek.Thursday => "پنجشنبه",
        DayOfWeek.Friday => "جمعه",
        _ => ""
    };

    public int CompareTo(PersianDate other) => ToDateOnly().CompareTo(other.ToDateOnly());

    public override string ToString() => $"{Year:0000}/{Month:00}/{Day:00}";

    public string ToLongPersianString() => $"{PersianDayOfWeek} {Day} {MonthName} {Year}";
}