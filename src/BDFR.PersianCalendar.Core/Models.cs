namespace BDFR.PersianCalendar.Core;

public enum CalendarItemKind { Event, Task, Note, SpecialOccasion }
public enum ReminderState { Pending = 0, Scheduled = 1, Fired = 2, Snoozed = 3, Dismissed = 4, Completed = 5 }
public enum CalendarSystemKind { Persian, Gregorian, Hijri }
public enum RecurrenceKind { None, Daily, Weekly, Monthly, Yearly }

public sealed record CalendarEvent(
    string Id,
    string Title,
    PersianDate Date,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    bool AllDay,
    string? Description = null,
    string? Category = null,
    string? Location = null,
    RecurrenceKind Recurrence = RecurrenceKind.None);

public sealed record CalendarTask(
    string Id,
    string Title,
    PersianDate Date,
    TimeOnly? DueTime,
    int Priority = 0,
    bool Completed = false,
    string? Category = null,
    string? Description = null);

public sealed record DayNote(
    string Id,
    PersianDate Date,
    string Text,
    bool Pinned,
    DateTimeOffset ModifiedAt);

public sealed record Occasion(
    string Id,
    PersianDate Date,
    string Title,
    bool IsHoliday,
    string Source,
    string? SourceHash = null);

public sealed record SpecialOccasion(
    string Id,
    string Title,
    CalendarSystemKind CalendarSystem,
    int Month,
    int Day,
    bool RepeatYearly,
    IReadOnlyList<int> ReminderDaysBefore,
    string? Category = null,
    string? Note = null);

public sealed record ReminderSchedule(
    string Id,
    CalendarItemKind ItemKind,
    string ItemId,
    DateTimeOffset FireAtUtc,
    ReminderState State,
    string Title,
    string Body);

public sealed record ActivityLogEntry(
    string Id,
    DateTimeOffset CreatedAt,
    string Action,
    CalendarItemKind? ItemKind,
    string? ItemId,
    string Message);

public sealed record DaySnapshot(
    PersianDate Date,
    IReadOnlyList<Occasion> Occasions,
    IReadOnlyList<CalendarEvent> Events,
    IReadOnlyList<CalendarTask> Tasks,
    DayNote? Note)
{
    public bool IsHoliday => Date.DayOfWeek == DayOfWeek.Friday || Occasions.Any(x => x.IsHoliday);
    public int OpenTaskCount => Tasks.Count(x => !x.Completed);
}

public sealed record QuickAddResult(
    string Title,
    PersianDate Date,
    TimeOnly? Time,
    RecurrenceKind Recurrence,
    int ReminderMinutesBefore = 10);
