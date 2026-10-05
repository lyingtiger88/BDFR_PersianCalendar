using System.Globalization;
using BDFR.PersianCalendar.Core;

namespace BDFR.PersianCalendar.Infrastructure;

public sealed class SpecialOccasionService(ICalendarRepository repository, TimeZoneInfo timeZone)
{
    public async Task<SpecialOccasion> AddPersianAnnualAsync(
        string title,
        int month,
        int day,
        IReadOnlyList<int> reminderDaysBefore,
        TimeOnly notificationTime,
        CancellationToken cancellationToken = default)
    {
        ValidatePersianMonthDay(month, day);
        var occasion = new SpecialOccasion(
            Guid.NewGuid().ToString("N"),
            title,
            CalendarSystemKind.Persian,
            month,
            day,
            true,
            reminderDaysBefore.Where(x => x >= 0).Distinct().OrderByDescending(x => x).ToArray());

        await repository.AddSpecialOccasionAsync(occasion, cancellationToken);
        await ScheduleNextAsync(occasion, notificationTime, cancellationToken);
        await repository.AddActivityAsync(new ActivityLogEntry(
            Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "special-occasion-created",
            CalendarItemKind.SpecialOccasion, occasion.Id, occasion.Title), cancellationToken);
        return occasion;
    }

    public async Task EnsureUpcomingRemindersAsync(
        TimeOnly notificationTime,
        CancellationToken cancellationToken = default)
    {
        foreach (var occasion in await repository.GetSpecialOccasionsAsync(cancellationToken))
            await ScheduleNextAsync(occasion, notificationTime, cancellationToken);
    }

    private async Task ScheduleNextAsync(
        SpecialOccasion occasion,
        TimeOnly notificationTime,
        CancellationToken cancellationToken)
    {
        var target = ResolveNextDate(occasion);
        foreach (var days in occasion.ReminderDaysBefore.Where(x => x >= 0).Distinct())
        {
            var localDate = target.AddDays(-days);
            var local = localDate.ToDateTime(notificationTime, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
            var fireAt = new DateTimeOffset(utc, TimeSpan.Zero);
            if (fireAt <= DateTimeOffset.UtcNow) continue;

            var id = $"{occasion.Id}-{target:yyyyMMdd}-{days}";
            var body = days == 0 ? $"امروز: {occasion.Title}" : $"{days} روز تا {occasion.Title}";
            await repository.ScheduleReminderAsync(new ReminderSchedule(
                id, CalendarItemKind.SpecialOccasion, occasion.Id, fireAt,
                ReminderState.Pending, occasion.Title, body), cancellationToken);
        }
    }

    private static DateOnly ResolveNextDate(SpecialOccasion occasion)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (occasion.CalendarSystem == CalendarSystemKind.Persian)
        {
            var pToday = PersianDate.Today();
            var candidate = SafePersian(pToday.Year, occasion.Month, occasion.Day).ToDateOnly();
            if (candidate < today)
                candidate = SafePersian(pToday.Year + 1, occasion.Month, occasion.Day).ToDateOnly();
            return candidate;
        }

        if (occasion.CalendarSystem == CalendarSystemKind.Gregorian)
        {
            var day = Math.Min(occasion.Day, DateTime.DaysInMonth(today.Year, occasion.Month));
            var candidate = new DateOnly(today.Year, occasion.Month, day);
            if (candidate < today)
            {
                var year = today.Year + 1;
                candidate = new DateOnly(year, occasion.Month, Math.Min(occasion.Day, DateTime.DaysInMonth(year, occasion.Month)));
            }
            return candidate;
        }

        var hijri = new HijriCalendar();
        var now = today.ToDateTime(TimeOnly.MinValue);
        var hijriYear = hijri.GetYear(now);
        var hDay = Math.Min(occasion.Day, hijri.GetDaysInMonth(hijriYear, occasion.Month));
        var candidateDate = DateOnly.FromDateTime(hijri.ToDateTime(hijriYear, occasion.Month, hDay, 0, 0, 0, 0));
        if (candidateDate < today)
        {
            hijriYear++;
            hDay = Math.Min(occasion.Day, hijri.GetDaysInMonth(hijriYear, occasion.Month));
            candidateDate = DateOnly.FromDateTime(hijri.ToDateTime(hijriYear, occasion.Month, hDay, 0, 0, 0, 0));
        }
        return candidateDate;
    }

    private static PersianDate SafePersian(int year, int month, int day)
        => new(year, month, Math.Min(day, PersianDate.DaysInMonth(year, month)));

    private static void ValidatePersianMonthDay(int month, int day)
    {
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        var max = month <= 6 ? 31 : month <= 11 ? 30 : 30;
        if (day is < 1 || day > max) throw new ArgumentOutOfRangeException(nameof(day));
    }
}