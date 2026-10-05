namespace BDFR.PersianCalendar.Core;

public sealed record MonthCell(PersianDate Date, bool IsCurrentMonth, bool IsToday);

public static class MonthGridBuilder
{
    public static IReadOnlyList<MonthCell> Build(int year, int month, PersianDate? today = null)
    {
        today ??= PersianDate.Today();
        var first = new PersianDate(year, month, 1);
        var saturdayIndex = ((int)first.DayOfWeek + 1) % 7;
        var start = first.AddDays(-saturdayIndex);

        return Enumerable.Range(0, 42)
            .Select(i =>
            {
                var d = start.AddDays(i);
                return new MonthCell(d, d.Year == year && d.Month == month, d == today.Value);
            })
            .ToArray();
    }
}