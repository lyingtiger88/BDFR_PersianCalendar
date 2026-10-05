using BDFR.PersianCalendar.Core;

namespace BDFR.PersianCalendar.Infrastructure;

public sealed class PlannerService(ICalendarRepository repository, TimeZoneInfo timeZone)
{
    public async Task<CalendarEvent> AddEventAsync(
        string title,
        PersianDate date,
        TimeOnly start,
        int durationMinutes = 60,
        int[]? reminderMinutes = null,
        CancellationToken cancellationToken = default)
    {
        var calendarEvent = new CalendarEvent(
            Guid.NewGuid().ToString("N"), title.Trim(), date, start, start.AddMinutes(durationMinutes), false);

        await repository.AddEventAsync(calendarEvent, cancellationToken);
        foreach (var reminder in ReminderEngine.ForEvent(calendarEvent, timeZone, reminderMinutes ?? [10]))
            await repository.ScheduleReminderAsync(reminder, cancellationToken);

        await repository.AddActivityAsync(new ActivityLogEntry(
            Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "event-created",
            CalendarItemKind.Event, calendarEvent.Id, calendarEvent.Title), cancellationToken);

        return calendarEvent;
    }

    public async Task<CalendarTask> AddTaskAsync(
        string title,
        PersianDate date,
        TimeOnly? dueTime = null,
        int reminderMinutes = 10,
        CancellationToken cancellationToken = default)
    {
        var task = new CalendarTask(Guid.NewGuid().ToString("N"), title.Trim(), date, dueTime);
        await repository.AddTaskAsync(task, cancellationToken);

        if (dueTime is not null)
            foreach (var reminder in ReminderEngine.ForTask(task, timeZone, reminderMinutes))
                await repository.ScheduleReminderAsync(reminder, cancellationToken);

        await repository.AddActivityAsync(new ActivityLogEntry(
            Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "task-created",
            CalendarItemKind.Task, task.Id, task.Title), cancellationToken);

        return task;
    }

    public async Task<int> SyncOccasionsAsync(IOccasionSource source, int year, CancellationToken cancellationToken = default)
    {
        var items = await source.GetYearAsync(year, cancellationToken);
        await repository.UpsertOccasionsAsync(items, cancellationToken);
        await repository.AddActivityAsync(new ActivityLogEntry(
            Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "occasion-sync",
            null, null, $"{source.Name}: {items.Count} occasions for {year}"), cancellationToken);
        return items.Count;
    }
}