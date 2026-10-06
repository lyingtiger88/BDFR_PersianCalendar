using BDFR.PersianCalendar.Core;

namespace BDFR.PersianCalendar.Infrastructure;

public sealed class ReminderActionService(ICalendarRepository repository, IReminderScheduler? platformScheduler = null)
{
    public async Task<string> HandleAsync(string reminderId, string action, CancellationToken cancellationToken = default)
    {
        var reminder = await repository.GetReminderAsync(reminderId, cancellationToken);
        if (reminder is null) return "یادآور پیدا نشد.";
        action = action.Trim().ToLowerInvariant();

        if (action == "done")
        {
            if (reminder.ItemKind == CalendarItemKind.Task)
                await repository.SetTaskCompletedAsync(reminder.ItemId, true, cancellationToken);
            await repository.SetReminderStateAsync(reminder.Id, ReminderState.Completed, cancellationToken: cancellationToken);
            await LogAsync(reminder, "reminder-completed", "انجام شد", cancellationToken);
            return $"«{reminder.Title}» انجام‌شده ثبت شد.";
        }

        if (action == "dismiss")
        {
            await repository.SetReminderStateAsync(reminder.Id, ReminderState.Dismissed, cancellationToken: cancellationToken);
            await LogAsync(reminder, "reminder-dismissed", "اعلان رد شد", cancellationToken);
            return $"یادآور «{reminder.Title}» رد شد.";
        }

        if (action.StartsWith("snooze", StringComparison.Ordinal))
        {
            var minutes = int.TryParse(action["snooze".Length..], out var m) ? Math.Clamp(m, 1, 1440) : 10;
            var next = DateTimeOffset.UtcNow.AddMinutes(minutes);
            await repository.SetReminderStateAsync(reminder.Id, ReminderState.Pending, next, cancellationToken);

            if (platformScheduler is not null)
            {
                var updated = reminder with { FireAtUtc = next, State = ReminderState.Pending, Body = $"{minutes} دقیقه به تعویق افتاد" };
                if (await platformScheduler.TryScheduleAsync(updated, cancellationToken))
                    await repository.SetReminderStateAsync(reminder.Id, ReminderState.Scheduled, next, cancellationToken);
            }

            await LogAsync(reminder, "reminder-snoozed", $"{minutes} دقیقه به تعویق افتاد", cancellationToken);
            return $"یادآور «{reminder.Title}» {minutes} دقیقه عقب افتاد.";
        }

        await LogAsync(reminder, "reminder-opened", "اعلان باز شد", cancellationToken);
        return reminder.Title;
    }

    private Task LogAsync(ReminderSchedule reminder, string action, string message, CancellationToken cancellationToken)
        => repository.AddActivityAsync(new ActivityLogEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            action, reminder.ItemKind, reminder.ItemId, $"{reminder.Title} — {message}"), cancellationToken);
}
