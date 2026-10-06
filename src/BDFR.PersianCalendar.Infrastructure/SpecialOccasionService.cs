using System.Globalization;
using BDFR.PersianCalendar.Core;

namespace BDFR.PersianCalendar.Infrastructure;

public sealed class SpecialOccasionService(ICalendarRepository repository, TimeZoneInfo timeZone, IReminderScheduler? platformScheduler = null)
{
    public Task<SpecialOccasion> AddPersianAnnualAsync(string title, int month, int day, IReadOnlyList<int> reminderDaysBefore, TimeOnly notificationTime, CancellationToken cancellationToken = default)
        => AddAnnualAsync(title, CalendarSystemKind.Persian, month, day, reminderDaysBefore, notificationTime, cancellationToken);

    public async Task<SpecialOccasion> AddAnnualAsync(string title, CalendarSystemKind calendarSystem, int month, int day, IReadOnlyList<int> reminderDaysBefore, TimeOnly notificationTime, CancellationToken cancellationToken = default)
    {
        ValidateMonthDay(calendarSystem, month, day);
        var occasion = new SpecialOccasion(Guid.NewGuid().ToString("N"), title.Trim(), calendarSystem, month, day, true,
            reminderDaysBefore.Where(x => x >= 0).Distinct().OrderByDescending(x => x).ToArray());

        await repository.AddSpecialOccasionAsync(occasion, cancellationToken);
        await ScheduleNextAsync(occasion, notificationTime, cancellationToken);
        await repository.AddActivityAsync(new ActivityLogEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            "special-occasion-created", CalendarItemKind.SpecialOccasion, occasion.Id, occasion.Title), cancellationToken);
        return occasion;
    }

    public async Task EnsureUpcomingRemindersAsync(TimeOnly notificationTime, CancellationToken cancellationToken = default)
    {
        foreach (var occasion in await repository.GetSpecialOccasionsAsync(cancellationToken))
            await ScheduleNextAsync(occasion, notificationTime, cancellationToken);
    }

    private async Task ScheduleNextAsync(SpecialOccasion occasion, TimeOnly notificationTime, CancellationToken cancellationToken)
    {
        var target = ResolveNextDate(occasion);
        foreach (var days in occasion.ReminderDaysBefore.Where(x => x >= 0).Distinct())
        {
            var local = target.AddDays(-days).ToDateTime(notificationTime, DateTimeKind.Unspecified);
            var fireAt = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
            if (fireAt <= DateTimeOffset.UtcNow) continue;

            var id = $"{occasion.Id}-{target:yyyyMMdd}-{days}";
            var reminder = new ReminderSchedule(
                id,
                CalendarItemKind.SpecialOccasion,
                occasion.Id,
                fireAt,
                ReminderState.Pending,
                occasion.Title,
                days == 0 ? $"امروز: {occasion.Title}" : $"{days} روز تا {occasion.Title}");

            await repository.ScheduleReminderAsync(reminder, cancellationToken);
            if (platformScheduler is not null && await platformScheduler.TryScheduleAsync(reminder, cancellationToken))
                await repository.SetReminderStateAsync(reminder.Id, ReminderState.Scheduled, cancellationToken: cancellationToken);
        }
    }

    private static DateOnly ResolveNextDate(SpecialOccasion occasion)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (occasion.CalendarSystem == CalendarSystemKind.Persian)
        {
            var pToday = PersianDate.Today();
            var candidate = SafePersian(pToday.Year, occasion.Month, occasion.Day).ToDateOnly();
            return candidate < today ? SafePersian(pToday.Year + 1, occasion.Month, occasion.Day).ToDateOnly() : candidate;
        }

        if (occasion.CalendarSystem == CalendarSystemKind.Gregorian)
        {
            var candidate = new DateOnly(today.Year, occasion.Month, Math.Min(occasion.Day, DateTime.DaysInMonth(today.Year, occasion.Month)));
            if (candidate >= today) return candidate;
            var y = today.Year + 1;
            return new DateOnly(y, occasion.Month, Math.Min(occasion.Day, DateTime.DaysInMonth(y, occasion.Month)));
        }

        var hijri = new HijriCalendar();
        var hy = hijri.GetYear(today.ToDateTime(TimeOnly.MinValue));

        DateOnly Make(int year)
        {
            var d = Math.Min(occasion.Day, hijri.GetDaysInMonth(year, occasion.Month));
            return DateOnly.FromDateTime(hijri.ToDateTime(year, occasion.Month, d, 0, 0, 0, 0));
        }

        var candidateHijri = Make(hy);
        return candidateHijri < today ? Make(hy + 1) : candidateHijri;
    }

    private static PersianDate SafePersian(int year, int month, int day)
        => new(year, month, Math.Min(day, PersianDate.DaysInMonth(year, month)));

    private static void ValidateMonthDay(CalendarSystemKind system, int month, int day)
    {
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));

        var max = system switch
        {
            CalendarSystemKind.Persian => month <= 6 ? 31 : 30,
            CalendarSystemKind.Gregorian => DateTime.DaysInMonth(2024, month),
            CalendarSystemKind.Hijri => 30,
            _ => throw new ArgumentOutOfRangeException(nameof(system))
        };

        if (day is < 1 || day > max) throw new ArgumentOutOfRangeException(nameof(day));
    }
}
