using BDFR.PersianCalendar.Core;
using BDFR.PersianCalendar.Infrastructure;

var nowruz = new PersianDate(1405, 1, 1);
if (nowruz.ToDateOnly() != new DateOnly(2026, 3, 21))
    throw new Exception("PersianDate conversion failed.");

var parser = new PersianQuickAddParser();
var parsed = parser.Parse("فردا ساعت 16:30 جلسه تیم", new PersianDate(1405, 7, 14));
if (parsed.Date != new PersianDate(1405, 7, 15) ||
    parsed.Time != new TimeOnly(16, 30) ||
    parsed.Title != "جلسه تیم")
{
    throw new Exception(
        $"QuickAdd parser failed. Date={parsed.Date}, Time={parsed.Time}, Title=[{parsed.Title}]");
}

var recurring = parser.Parse(
    "هفتگی سه‌شنبه ساعت 18:00 تمرین",
    new PersianDate(1405, 7, 14));
if (recurring.Recurrence != RecurrenceKind.Weekly ||
    recurring.Time != new TimeOnly(18, 0) ||
    recurring.Title != "تمرین")
{
    throw new Exception(
        $"QuickAdd recurrence failed. Recurrence={recurring.Recurrence}, Title=[{recurring.Title}]");
}

var grid = MonthGridBuilder.Build(1405, 7, new PersianDate(1405, 7, 14));
if (grid.Count != 42 || grid.Count(x => x.IsToday) != 1)
    throw new Exception("Month grid failed.");

var timeIrFixture = """
<!doctype html>
<html lang="fa">
<body>
  <div id="Month_0">
    <div class="month-title">فروردین ۱۴۰۵</div>
    <div class="event-list">
      <div>
        <div>
          <div class="event holiday"><span>۱ فروردین نوروز</span></div>
          <div class="event"><span>۱۲ فروردین روز جمهوری اسلامی ایران</span></div>
        </div>
      </div>
    </div>
  </div>
</body>
</html>
""";

var occasions = await TimeIrOccasionSource.ParseAnnualHtmlAsync(timeIrFixture, 1405);
var newYear = occasions.SingleOrDefault(x => x.Date == new PersianDate(1405, 1, 1));
if (newYear is null || !newYear.IsHoliday || newYear.Title != "نوروز")
    throw new Exception("time.ir parser fixture failed for holiday event.");

var republicDay = occasions.SingleOrDefault(x => x.Date == new PersianDate(1405, 1, 12));
if (republicDay is null || republicDay.IsHoliday)
    throw new Exception("time.ir parser fixture failed for regular event.");

var dbPath = Path.Combine(Path.GetTempPath(), $"bdfr-calendar-smoke-{Guid.NewGuid():N}.db");
try
{
    var repository = new SqliteCalendarRepository(dbPath);
    await repository.InitializeAsync();

    var task = new CalendarTask(
        "task-smoke",
        "کار آزمایشی",
        new PersianDate(1405, 7, 14),
        new TimeOnly(12, 0));
    await repository.AddTaskAsync(task);

    var reminder = new ReminderSchedule(
        "reminder-smoke",
        CalendarItemKind.Task,
        task.Id,
        DateTimeOffset.UtcNow,
        ReminderState.Fired,
        task.Title,
        "موعد آزمایشی");
    await repository.ScheduleReminderAsync(reminder);

    await new ReminderActionService(repository).HandleAsync(reminder.Id, "done");
    if (!(await repository.GetDayAsync(task.Date)).Tasks.Single().Completed)
        throw new Exception("Reminder done action did not complete the task.");

    await repository.AddActivityAsync(new ActivityLogEntry(
        "activity-smoke",
        DateTimeOffset.UtcNow,
        "smoke",
        null,
        null,
        "activity"));
    if ((await repository.GetRecentActivitiesAsync(10)).Count == 0)
        throw new Exception("Activity center repository query failed.");

    var personalOccasion = new SpecialOccasion(
        "special-smoke",
        "تولد آزمایشی",
        CalendarSystemKind.Persian,
        10,
        5,
        true,
        [7, 1, 0]);
    await repository.AddSpecialOccasionAsync(personalOccasion);

    var personalDay = await repository.GetDayAsync(new PersianDate(1405, 10, 5));
    if (!personalDay.Occasions.Any(x =>
            x.Id == personalOccasion.Id &&
            x.Title == personalOccasion.Title &&
            x.Source == "personal"))
        throw new Exception("Personal Persian occasion did not appear on its configured day.");

    var wrongDay = await repository.GetDayAsync(new PersianDate(1405, 10, 6));
    if (wrongDay.Occasions.Any(x => x.Id == personalOccasion.Id))
        throw new Exception("Personal Persian occasion appeared one day late.");

    var syncAt = DateTimeOffset.UtcNow;
    await repository.SetLastOccasionSyncAsync("time.ir", 1405, syncAt);
    var loadedSyncAt = await repository.GetLastOccasionSyncAsync("time.ir", 1405);
    if (loadedSyncAt is null || Math.Abs((loadedSyncAt.Value - syncAt).TotalSeconds) > 1)
        throw new Exception("Occasion sync state persistence failed.");
}
finally
{
    try { File.Delete(dbPath); } catch { }
}

Console.WriteLine("BDFR Persian Calendar smoke checks passed.");