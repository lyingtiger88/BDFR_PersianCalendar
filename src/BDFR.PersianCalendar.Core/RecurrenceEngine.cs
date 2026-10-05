namespace BDFR.PersianCalendar.Core;

public static class RecurrenceEngine
{
    public static bool OccursOn(PersianDate start, RecurrenceKind recurrence, PersianDate target)
    {
        if (target.CompareTo(start) < 0) return false;

        return recurrence switch
        {
            RecurrenceKind.None => target == start,
            RecurrenceKind.Daily => true,
            RecurrenceKind.Weekly =>
                (target.ToDateOnly().DayNumber - start.ToDateOnly().DayNumber) % 7 == 0,
            RecurrenceKind.Monthly =>
                target.Day == Math.Min(start.Day, PersianDate.DaysInMonth(target.Year, target.Month)),
            RecurrenceKind.Yearly =>
                target.Month == start.Month &&
                target.Day == Math.Min(start.Day, PersianDate.DaysInMonth(target.Year, start.Month)),
            _ => false
        };
    }
}
