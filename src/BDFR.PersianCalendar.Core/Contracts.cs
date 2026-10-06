namespace BDFR.PersianCalendar.Core;

public interface ICalendarRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<DaySnapshot> GetDayAsync(PersianDate date, CancellationToken cancellationToken = default);
    Task UpsertNoteAsync(PersianDate date, string text, bool pinned = false, CancellationToken cancellationToken = default);
    Task AddTaskAsync(CalendarTask task, CancellationToken cancellationToken = default);
    Task SetTaskCompletedAsync(string taskId, bool completed, CancellationToken cancellationToken = default);
    Task AddEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default);
    Task UpsertOccasionsAsync(IEnumerable<Occasion> occasions, CancellationToken cancellationToken = default);
    Task AddSpecialOccasionAsync(SpecialOccasion occasion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SpecialOccasion>> GetSpecialOccasionsAsync(CancellationToken cancellationToken = default);
    Task ScheduleReminderAsync(ReminderSchedule reminder, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReminderSchedule>> GetDueRemindersAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReminderSchedule>> GetPendingRemindersAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
    Task SetReminderStateAsync(string reminderId, ReminderState state, DateTimeOffset? newFireAtUtc = null, CancellationToken cancellationToken = default);
    Task<ReminderSchedule?> GetReminderAsync(string reminderId, CancellationToken cancellationToken = default);
    Task AddActivityAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityLogEntry>> GetRecentActivitiesAsync(int limit = 20, CancellationToken cancellationToken = default);
    Task<DateTimeOffset?> GetLastOccasionSyncAsync(string source, int persianYear, CancellationToken cancellationToken = default);
    Task SetLastOccasionSyncAsync(string source, int persianYear, DateTimeOffset syncedAt, CancellationToken cancellationToken = default);
}

public interface IOccasionSource
{
    string Name { get; }
    Task<IReadOnlyList<Occasion>> GetYearAsync(int persianYear, CancellationToken cancellationToken = default);
}

public interface INotificationSink
{
    Task ShowAsync(ReminderSchedule reminder, CancellationToken cancellationToken = default);
}

public interface IReminderScheduler
{
    Task<bool> TryScheduleAsync(ReminderSchedule reminder, CancellationToken cancellationToken = default);
}
