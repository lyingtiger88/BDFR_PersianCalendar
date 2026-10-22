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
if (republicDay is null || !republicDay.IsHoliday)
    throw new Exception("12 Farvardin must be classified as an official holiday.");

var timeIrFallbackFixture = """
<!doctype html>
<html lang="fa">
<body>
  <section>
    <h2>مناسبت‌های ماه مهر</h2>
    <div class="event holiday"><span>14 مهر تعطیلی آزمایشی</span></div>
    <div class="event"><span>16 مهر مناسبت آزمایشی</span></div>
  </section>
</body>
</html>
""";

var fallbackOccasions = await TimeIrOccasionSource.ParseAnnualHtmlAsync(timeIrFallbackFixture, 1405);
var fallbackHoliday = fallbackOccasions.SingleOrDefault(x => x.Date == new PersianDate(1405, 7, 14));
if (fallbackHoliday is null || !fallbackHoliday.IsHoliday || fallbackHoliday.Title != "تعطیلی آزمایشی")
    throw new Exception("time.ir fallback parser failed to preserve holiday status.");

var fallbackRegular = fallbackOccasions.SingleOrDefault(x => x.Date == new PersianDate(1405, 7, 16));
if (fallbackRegular is null || fallbackRegular.IsHoliday)
    throw new Exception("time.ir fallback parser failed for regular occasion.");

var timeIrCurrentMarkupFixture = """
<!doctype html>
<html lang="fa">
<body>
  <main>
    <section data-month="11">
      <h2>مناسبت‌های ماه بهمن</h2>
      <custom-event><a href="/calendar">4 بهمن ولادت حضرت قائم عجل الله تعالی فرجه و جشن نیمه شعبان[ ۱۵ شعبان ]</a></custom-event>
      <custom-event><a href="/calendar">6 بهمن بزرگداشت صفی‌الدین اُرمَوی و روز موسیقی ایرانی</a></custom-event>
    </section>
  </main>
</body>
</html>
""";

var currentMarkupOccasions = await TimeIrOccasionSource.ParseAnnualHtmlAsync(
    timeIrCurrentMarkupFixture,
    1405);
var halfShaaban = currentMarkupOccasions.SingleOrDefault(
    x => x.Date == new PersianDate(1405, 11, 4));
if (halfShaaban is null ||
    !halfShaaban.Title.StartsWith("ولادت حضرت قائم", StringComparison.Ordinal))
{
    throw new Exception("time.ir current-markup parser failed for 4 Bahman 1405.");
}

var officialHolidayFallbackFixture = """
<!doctype html>
<html lang="fa">
<body>
  <section>
    <div>22 بهمن پیروزی انقلاب اسلامی ایران</div>
    <div>23 بهمن مناسبت آزمایشی عادی</div>
  </section>
</body>
</html>
""";

var officialFallbackOccasions = await TimeIrOccasionSource.ParseAnnualHtmlAsync(
    officialHolidayFallbackFixture,
    1405);
var revolutionDay = officialFallbackOccasions.SingleOrDefault(
    x => x.Date == new PersianDate(1405, 11, 22));
if (revolutionDay is null || !revolutionDay.IsHoliday)
    throw new Exception("22 Bahman must remain an official holiday even without a holiday CSS class.");

var ordinaryBahman = officialFallbackOccasions.SingleOrDefault(
    x => x.Date == new PersianDate(1405, 11, 23));
if (ordinaryBahman is null || ordinaryBahman.IsHoliday)
    throw new Exception("Ordinary classless occasions must not be promoted to holidays.");

var ramadanHolidayFixture = """
<!doctype html>
<html lang="fa">
<body>
  <section>
    <div>9 اسفند شهادت امام علی (ع) [ ۲۱ رمضان ]</div>
    <div>8 اسفند مناسبت آزمایشی [ ۲۰ رمضان ]</div>
  </section>
</body>
</html>
""";

var ramadanOccasions = await TimeIrOccasionSource.ParseAnnualHtmlAsync(
    ramadanHolidayFixture,
    1405);

var aliMartyrdom = ramadanOccasions.SingleOrDefault(
    x => x.Date == new PersianDate(1405, 12, 9));
if (aliMartyrdom is null || !aliMartyrdom.IsHoliday)
    throw new Exception("21 Ramadan must be classified as an official holiday even without a holiday CSS class.");

var ramadanRegular = ramadanOccasions.SingleOrDefault(
    x => x.Date == new PersianDate(1405, 12, 8));
if (ramadanRegular is null || ramadanRegular.IsHoliday)
    throw new Exception("20 Ramadan regular fixture must remain non-holiday.");

if (!CalendarDateConverter.TryConvertInput(
        "۱۴۰۵/۰۷/۱۷",
        CalendarSystemKind.Persian,
        out var conversion,
        out var conversionError))
{
    throw new Exception($"Persian date conversion failed: {conversionError}");
}

if (conversion.Persian != new PersianDate(1405, 7, 17))
    throw new Exception("Persian converter returned the wrong Persian date.");

var gregorianRoundTrip = CalendarDateConverter.Convert(
    CalendarSystemKind.Gregorian,
    conversion.Gregorian.Year,
    conversion.Gregorian.Month,
    conversion.Gregorian.Day);
if (gregorianRoundTrip.Persian != conversion.Persian)
    throw new Exception("Gregorian converter round-trip failed.");

var hijriRoundTrip = CalendarDateConverter.Convert(
    CalendarSystemKind.Hijri,
    conversion.Hijri.Year,
    conversion.Hijri.Month,
    conversion.Hijri.Day);
if (hijriRoundTrip.Gregorian != conversion.Gregorian)
    throw new Exception("Hijri converter round-trip failed.");

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

    await repository.ClearActivitiesAsync();
    if ((await repository.GetRecentActivitiesAsync(10)).Count != 0)
        throw new Exception("Activity center clear did not remove activity_log rows.");

    var privateEvent = new CalendarEvent(
        "event-private-smoke",
        "رویداد خصوصی",
        new PersianDate(1405, 7, 14),
        new TimeOnly(15, 30),
        new TimeOnly(16, 30),
        false,
        Privacy: PrivacyLevel.Private);
    await repository.AddEventAsync(privateEvent);

    var privateEventLoaded = (await repository.GetDayAsync(privateEvent.Date))
        .Events.Single(x => x.Id == privateEvent.Id);
    if (privateEventLoaded.Privacy != PrivacyLevel.Private)
        throw new Exception("Event privacy persistence failed.");

    var repeatingReminder = new ReminderSchedule(
        "reminder-retry-smoke",
        CalendarItemKind.Event,
        privateEvent.Id,
        DateTimeOffset.UtcNow.AddMinutes(-1),
        ReminderState.Pending,
        privateEvent.Title,
        "تست یادآور",
        PrivacyLevel.Private,
        3,
        7,
        0);
    await repository.ScheduleReminderAsync(repeatingReminder);

    var dueRepeat = (await repository.GetDueRemindersAsync(DateTimeOffset.UtcNow))
        .Single(x => x.Id == repeatingReminder.Id);
    if (dueRepeat.Privacy != PrivacyLevel.Private ||
        dueRepeat.RepeatCount != 3 ||
        dueRepeat.RepeatIntervalMinutes != 7)
        throw new Exception("Reminder privacy/repeat persistence failed.");

    await repository.UpdateReminderDeliveryAsync(
        repeatingReminder.Id,
        ReminderState.Fired,
        3);

    var awaitingAck = await repository.GetAwaitingAcknowledgementRemindersAsync();
    if (!awaitingAck.Any(x => x.Id == repeatingReminder.Id && x.Attempt == 3))
        throw new Exception("Awaiting-acknowledgement reminder query failed.");

    await repository.UpdateReminderDeliveryAsync(
        repeatingReminder.Id,
        ReminderState.Acknowledged,
        3);

    if ((await repository.GetAwaitingAcknowledgementRemindersAsync())
        .Any(x => x.Id == repeatingReminder.Id))
        throw new Exception("Reminder acknowledgement state failed.");

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

    await repository.UpsertOccasionsAsync([
        new Occasion(
            "old-timeir",
            new PersianDate(1405, 7, 1),
            "رکورد قدیمی",
            false,
            "time.ir")
    ]);

    await repository.ReplaceOccasionsForYearAsync(
        "time.ir",
        1405,
        [
            new Occasion(
                "fresh-timeir",
                new PersianDate(1405, 7, 2),
                "رکورد تازه",
                true,
                "time.ir")
        ]);

    if ((await repository.GetDayAsync(new PersianDate(1405, 7, 1))).Occasions.Any(x => x.Id == "old-timeir"))
        throw new Exception("Yearly occasion refresh did not remove stale time.ir rows.");

    var refreshedDay = await repository.GetDayAsync(new PersianDate(1405, 7, 2));
    if (!refreshedDay.Occasions.Any(x => x.Id == "fresh-timeir" && x.IsHoliday))
        throw new Exception("Yearly occasion refresh did not store fresh holiday rows.");

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

// Companion protocol: both peers must derive the same session key.
using (var hostKey = new CompanionEphemeralKey())
using (var mobileKey = new CompanionEphemeralKey())
{
    var sessionId = CompanionProtocol.NewSessionId();
    var hostSessionKey = hostKey.DeriveSessionKey(
        mobileKey.PublicKeyBase64,
        sessionId);
    var mobileSessionKey = mobileKey.DeriveSessionKey(
        hostKey.PublicKeyBase64,
        sessionId);

    if (!hostSessionKey.SequenceEqual(mobileSessionKey))
        throw new Exception("Companion ECDH session keys do not match.");

    var hostSas = CompanionEphemeralKey.ComputeSixDigitSas(hostSessionKey);
    var mobileSas = CompanionEphemeralKey.ComputeSixDigitSas(mobileSessionKey);
    if (hostSas != mobileSas || hostSas.Length != 6)
        throw new Exception("Companion six-digit SAS mismatch.");

    var syncEnvelope = new SyncEnvelope(
        "env-smoke",
        SyncEntityKind.Note,
        "note-smoke",
        SyncMutationKind.Upsert,
        2,
        DateTimeOffset.UtcNow,
        "device-host",
        "{\"text\":\"hello\"}",
        "hash-smoke");

    var encrypted = CompanionEphemeralKey.EncryptJson(
        syncEnvelope,
        hostSessionKey);
    var decrypted = CompanionEphemeralKey.DecryptJson<SyncEnvelope>(
        encrypted,
        mobileSessionKey);

    if (decrypted.EntityId != syncEnvelope.EntityId ||
        decrypted.Revision != syncEnvelope.Revision ||
        decrypted.PayloadJson != syncEnvelope.PayloadJson)
    {
        throw new Exception("Companion encrypted frame round-trip failed.");
    }

    CryptographicOperations.ZeroMemory(hostSessionKey);
    CryptographicOperations.ZeroMemory(mobileSessionKey);
}

var localEnvelope = new SyncEnvelope(
    "env-local",
    SyncEntityKind.Event,
    "event-shared",
    SyncMutationKind.Upsert,
    5,
    DateTimeOffset.UtcNow.AddMinutes(-2),
    "device-a",
    "{}",
    "A");

var incomingEnvelope = localEnvelope with
{
    EnvelopeId = "env-incoming",
    Revision = 6,
    ModifiedAtUtc = DateTimeOffset.UtcNow,
    OriginDeviceId = "device-b",
    ContentHash = "B"
};

if (SyncMergePolicy.Decide(localEnvelope, incomingEnvelope) !=
    SyncConflictResolution.ApplyIncoming)
{
    throw new Exception("Companion merge policy failed for a newer revision.");
}

// Feature parity contract: every feature must explicitly track all three clients.
string? repoRoot = null;
foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        var candidate = Path.Combine(
            directory.FullName,
            "platform",
            "feature-parity.json");

        if (File.Exists(candidate))
        {
            repoRoot = directory.FullName;
            break;
        }

        directory = directory.Parent;
    }

    if (repoRoot is not null)
        break;
}

if (repoRoot is null)
    throw new Exception("Could not locate platform/feature-parity.json.");

var parityPath = Path.Combine(repoRoot, "platform", "feature-parity.json");
using (var parityDocument = System.Text.Json.JsonDocument.Parse(
           await File.ReadAllTextAsync(parityPath)))
{
    var features = parityDocument.RootElement.GetProperty("features");
    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var feature in features.EnumerateArray())
    {
        var id = feature.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            throw new Exception("Feature parity manifest contains a missing or duplicate feature id.");

        foreach (var platform in new[] { "windows", "android", "ios" })
        {
            if (!feature.TryGetProperty(platform, out var status) ||
                string.IsNullOrWhiteSpace(status.GetString()))
            {
                throw new Exception(
                    $"Feature parity entry [{id}] is missing platform [{platform}].");
            }
        }
    }
}

foreach (var requiredMobileFile in new[]
{
    "mobile/android/app/src/main/java/com/bdfr/zurvan/MainActivity.kt",
    "mobile/android/app/src/main/java/com/bdfr/zurvan/CompanionModels.kt",
    "mobile/ios/ZurvanIOS/ContentView.swift",
    "mobile/ios/ZurvanIOS/CompanionModels.swift",
    "docs/COMPANION_SYNC.md"
})
{
    if (!File.Exists(Path.Combine(repoRoot, requiredMobileFile)))
        throw new Exception(
            $"Cross-platform scaffold is missing required file: {requiredMobileFile}");
}

Console.WriteLine("BDFR Persian Calendar smoke checks passed.");