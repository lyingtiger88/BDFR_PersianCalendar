namespace BDFR.PersianCalendar.Core;

public static class ReminderEngine
{
    public static IReadOnlyList<ReminderSchedule> ForEvent(
        CalendarEvent calendarEvent,
        TimeZoneInfo timeZone,
        params int[] minutesBefore)
        => ForEvent(
            calendarEvent,
            timeZone,
            repeatCount: 1,
            repeatIntervalMinutes: 5,
            minutesBefore);

    public static IReadOnlyList<ReminderSchedule> ForEvent(
        CalendarEvent calendarEvent,
        TimeZoneInfo timeZone,
        int repeatCount,
        int repeatIntervalMinutes,
        params int[] minutesBefore)
    {
        if (calendarEvent.AllDay || calendarEvent.StartTime is null) return [];

        var local = calendarEvent.Date.ToDateOnly().ToDateTime(calendarEvent.StartTime.Value, DateTimeKind.Unspecified);
        var eventUtc = TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
        repeatCount = Math.Clamp(repeatCount, 1, 20);
        repeatIntervalMinutes = Math.Clamp(repeatIntervalMinutes, 1, 1440);

        return minutesBefore
            .Distinct()
            .Where(x => x >= 0)
            .OrderByDescending(x => x)
            .Select(offset => new ReminderSchedule(
                Guid.NewGuid().ToString("N"),
                CalendarItemKind.Event,
                calendarEvent.Id,
                new DateTimeOffset(eventUtc, TimeSpan.Zero).AddMinutes(-offset),
                ReminderState.Pending,
                calendarEvent.Title,
                offset == 0 ? "زمان شروع رویداد فرا رسیده است." : $"{offset} دقیقه تا شروع رویداد",
                calendarEvent.Privacy,
                repeatCount,
                repeatIntervalMinutes,
                0))
            .ToArray();
    }

    public static IReadOnlyList<ReminderSchedule> ForTask(
        CalendarTask task,
        TimeZoneInfo timeZone,
        params int[] minutesBefore)
    {
        if (task.DueTime is null) return [];

        var local = task.Date.ToDateOnly().ToDateTime(task.DueTime.Value, DateTimeKind.Unspecified);
        var dueUtc = TimeZoneInfo.ConvertTimeToUtc(local, timeZone);

        return minutesBefore
            .Distinct()
            .Where(x => x >= 0)
            .Select(offset => new ReminderSchedule(
                Guid.NewGuid().ToString("N"),
                CalendarItemKind.Task,
                task.Id,
                new DateTimeOffset(dueUtc, TimeSpan.Zero).AddMinutes(-offset),
                ReminderState.Pending,
                task.Title,
                offset == 0 ? "موعد انجام این فعالیت رسیده است." : $"{offset} دقیقه تا موعد فعالیت"))
            .ToArray();
    }
}