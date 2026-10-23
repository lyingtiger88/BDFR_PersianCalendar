using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BDFR.PersianCalendar.Core;
using Microsoft.Data.Sqlite;

namespace BDFR.PersianCalendar.Infrastructure;

public sealed class SqliteCalendarRepository(string databasePath) : ICalendarRepository, ICompanionSnapshotStore
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
        PRAGMA journal_mode=WAL;
        PRAGMA foreign_keys=ON;

        CREATE TABLE IF NOT EXISTS events(
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            persian_date TEXT NOT NULL,
            start_time TEXT NULL,
            end_time TEXT NULL,
            all_day INTEGER NOT NULL DEFAULT 0,
            description TEXT NULL,
            category TEXT NULL,
            location TEXT NULL,
            recurrence INTEGER NOT NULL DEFAULT 0,
            privacy INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_events_date ON events(persian_date);

        CREATE TABLE IF NOT EXISTS tasks(
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            persian_date TEXT NOT NULL,
            due_time TEXT NULL,
            priority INTEGER NOT NULL DEFAULT 0,
            completed INTEGER NOT NULL DEFAULT 0,
            category TEXT NULL,
            description TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_tasks_date ON tasks(persian_date);

        CREATE TABLE IF NOT EXISTS notes(
            id TEXT PRIMARY KEY,
            persian_date TEXT NOT NULL UNIQUE,
            text TEXT NOT NULL,
            pinned INTEGER NOT NULL DEFAULT 0,
            modified_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS occasions(
            id TEXT PRIMARY KEY,
            persian_date TEXT NOT NULL,
            title TEXT NOT NULL,
            is_holiday INTEGER NOT NULL DEFAULT 0,
            source TEXT NOT NULL,
            source_hash TEXT NULL,
            UNIQUE(persian_date, title, source)
        );
        CREATE INDEX IF NOT EXISTS ix_occasions_date ON occasions(persian_date);

        CREATE TABLE IF NOT EXISTS special_occasions(
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            calendar_system INTEGER NOT NULL,
            month INTEGER NOT NULL,
            day INTEGER NOT NULL,
            repeat_yearly INTEGER NOT NULL DEFAULT 1,
            reminder_days TEXT NOT NULL,
            category TEXT NULL,
            note TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS reminders(
            id TEXT PRIMARY KEY,
            item_kind INTEGER NOT NULL,
            item_id TEXT NOT NULL,
            fire_at_utc TEXT NOT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            title TEXT NOT NULL,
            body TEXT NOT NULL,
            privacy INTEGER NOT NULL DEFAULT 0,
            repeat_count INTEGER NOT NULL DEFAULT 1,
            repeat_interval_minutes INTEGER NOT NULL DEFAULT 5,
            attempt INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_reminders_due ON reminders(state, fire_at_utc);

        CREATE TABLE IF NOT EXISTS activity_log(
            id TEXT PRIMARY KEY,
            created_at TEXT NOT NULL,
            action TEXT NOT NULL,
            item_kind INTEGER NULL,
            item_id TEXT NULL,
            message TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_activity_log_created ON activity_log(created_at DESC);

        CREATE TABLE IF NOT EXISTS source_sync(
            source TEXT NOT NULL,
            persian_year INTEGER NOT NULL,
            synced_at TEXT NOT NULL,
            PRIMARY KEY(source, persian_year)
        );
        """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await EnsureColumnAsync(db, "events", "privacy", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "reminders", "privacy", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "reminders", "repeat_count", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(db, "reminders", "repeat_interval_minutes", "INTEGER NOT NULL DEFAULT 5", cancellationToken);
        await EnsureColumnAsync(db, "reminders", "attempt", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
    }

    public async Task<DaySnapshot> GetDayAsync(PersianDate date, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var key = date.ToString();

        var occasions = new List<Occasion>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT id,title,is_holiday,source,source_hash FROM occasions WHERE persian_date=$d ORDER BY is_holiday DESC,title";
            cmd.Parameters.AddWithValue("$d", key);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                occasions.Add(new Occasion(reader.GetString(0), date, reader.GetString(1), reader.GetInt32(2) != 0,
                    reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id,title,calendar_system,month,day
                FROM special_occasions
                ORDER BY month,day,title;
                """;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var calendarSystem = (CalendarSystemKind)reader.GetInt32(2);
                var month = reader.GetInt32(3);
                var day = reader.GetInt32(4);

                if (!MatchesSpecialOccasion(date, calendarSystem, month, day))
                    continue;

                occasions.Add(new Occasion(
                    reader.GetString(0),
                    date,
                    reader.GetString(1),
                    false,
                    "personal"));
            }
        }

        var events = new List<CalendarEvent>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id,title,persian_date,start_time,end_time,all_day,description,category,location,recurrence,privacy
                FROM events
                WHERE persian_date=$d OR (recurrence<>0 AND persian_date <= $d)
                ORDER BY all_day DESC,start_time;
                """;
            cmd.Parameters.AddWithValue("$d", key);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var startDate = ParsePersianDate(reader.GetString(2));
                var recurrence = (RecurrenceKind)reader.GetInt32(9);
                if (!RecurrenceEngine.OccursOn(startDate, recurrence, date)) continue;

                events.Add(new CalendarEvent(
                    reader.GetString(0), reader.GetString(1), date,
                    ParseTime(reader, 3), ParseTime(reader, 4), reader.GetInt32(5) != 0,
                    ReadNullable(reader, 6), ReadNullable(reader, 7), ReadNullable(reader, 8),
                    recurrence,
                    (PrivacyLevel)reader.GetInt32(10)));
            }
        }

        var tasks = new List<CalendarTask>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT id,title,due_time,priority,completed,category,description FROM tasks WHERE persian_date=$d ORDER BY completed,priority DESC,due_time";
            cmd.Parameters.AddWithValue("$d", key);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                tasks.Add(new CalendarTask(reader.GetString(0), reader.GetString(1), date, ParseTime(reader, 2),
                    reader.GetInt32(3), reader.GetInt32(4) != 0, ReadNullable(reader, 5), ReadNullable(reader, 6)));
        }

        DayNote? note = null;
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT id,text,pinned,modified_at FROM notes WHERE persian_date=$d LIMIT 1";
            cmd.Parameters.AddWithValue("$d", key);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                note = new DayNote(reader.GetString(0), date, reader.GetString(1), reader.GetInt32(2) != 0,
                    DateTimeOffset.Parse(reader.GetString(3)));
        }

        return new DaySnapshot(date, occasions, events, tasks, note);
    }

    public async Task UpsertNoteAsync(PersianDate date, string text, bool pinned = false, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO notes(id,persian_date,text,pinned,modified_at)
            VALUES($id,$d,$t,$p,$m)
            ON CONFLICT(persian_date) DO UPDATE SET text=excluded.text,pinned=excluded.pinned,modified_at=excluded.modified_at;
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$d", date.ToString());
        cmd.Parameters.AddWithValue("$t", text);
        cmd.Parameters.AddWithValue("$p", pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$m", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddTaskAsync(CalendarTask task, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO tasks(id,title,persian_date,due_time,priority,completed,category,description)
            VALUES($id,$t,$d,$due,$p,$c,$cat,$desc);
            """;
        cmd.Parameters.AddWithValue("$id", task.Id);
        cmd.Parameters.AddWithValue("$t", task.Title);
        cmd.Parameters.AddWithValue("$d", task.Date.ToString());
        cmd.Parameters.AddWithValue("$due", (object?)task.DueTime?.ToString("HH:mm") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$p", task.Priority);
        cmd.Parameters.AddWithValue("$c", task.Completed ? 1 : 0);
        cmd.Parameters.AddWithValue("$cat", (object?)task.Category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$desc", (object?)task.Description ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetTaskCompletedAsync(string taskId, bool completed, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE tasks SET completed=$c WHERE id=$id";
        cmd.Parameters.AddWithValue("$c", completed ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", taskId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO events(id,title,persian_date,start_time,end_time,all_day,description,category,location,recurrence,privacy)
            VALUES($id,$t,$d,$s,$e,$a,$desc,$cat,$loc,$r,$privacy);
            """;
        cmd.Parameters.AddWithValue("$id", calendarEvent.Id);
        cmd.Parameters.AddWithValue("$t", calendarEvent.Title);
        cmd.Parameters.AddWithValue("$d", calendarEvent.Date.ToString());
        cmd.Parameters.AddWithValue("$s", (object?)calendarEvent.StartTime?.ToString("HH:mm") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$e", (object?)calendarEvent.EndTime?.ToString("HH:mm") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$a", calendarEvent.AllDay ? 1 : 0);
        cmd.Parameters.AddWithValue("$desc", (object?)calendarEvent.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cat", (object?)calendarEvent.Category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$loc", (object?)calendarEvent.Location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$r", (int)calendarEvent.Recurrence);
        cmd.Parameters.AddWithValue("$privacy", (int)calendarEvent.Privacy);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertOccasionsAsync(IEnumerable<Occasion> occasions, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        foreach (var item in occasions)
        {
            var cmd = db.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                INSERT INTO occasions(id,persian_date,title,is_holiday,source,source_hash)
                VALUES($id,$d,$t,$h,$s,$hash)
                ON CONFLICT(persian_date,title,source) DO UPDATE SET is_holiday=excluded.is_holiday,source_hash=excluded.source_hash;
                """;
            cmd.Parameters.AddWithValue("$id", item.Id);
            cmd.Parameters.AddWithValue("$d", item.Date.ToString());
            cmd.Parameters.AddWithValue("$t", item.Title);
            cmd.Parameters.AddWithValue("$h", item.IsHoliday ? 1 : 0);
            cmd.Parameters.AddWithValue("$s", item.Source);
            cmd.Parameters.AddWithValue("$hash", (object?)item.SourceHash ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
    }

    public async Task ReplaceOccasionsForYearAsync(
        string source,
        int persianYear,
        IEnumerable<Occasion> occasions,
        CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);

        var delete = db.CreateCommand();
        delete.Transaction = (SqliteTransaction)tx;
        delete.CommandText = """
            DELETE FROM occasions
            WHERE source=$source
              AND persian_date >= $from
              AND persian_date <= $to;
            """;
        delete.Parameters.AddWithValue("$source", source);
        delete.Parameters.AddWithValue("$from", $"{persianYear:0000}/01/01");
        delete.Parameters.AddWithValue("$to", $"{persianYear:0000}/12/30");
        await delete.ExecuteNonQueryAsync(cancellationToken);

        foreach (var item in occasions)
        {
            var cmd = db.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = """
                INSERT INTO occasions(id,persian_date,title,is_holiday,source,source_hash)
                VALUES($id,$d,$t,$h,$s,$hash)
                ON CONFLICT(persian_date,title,source)
                DO UPDATE SET
                    is_holiday=excluded.is_holiday,
                    source_hash=excluded.source_hash,
                    id=excluded.id;
                """;
            cmd.Parameters.AddWithValue("$id", item.Id);
            cmd.Parameters.AddWithValue("$d", item.Date.ToString());
            cmd.Parameters.AddWithValue("$t", item.Title);
            cmd.Parameters.AddWithValue("$h", item.IsHoliday ? 1 : 0);
            cmd.Parameters.AddWithValue("$s", item.Source);
            cmd.Parameters.AddWithValue("$hash", (object?)item.SourceHash ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task AddSpecialOccasionAsync(SpecialOccasion occasion, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO special_occasions(id,title,calendar_system,month,day,repeat_yearly,reminder_days,category,note)
            VALUES($id,$t,$cs,$m,$d,$r,$days,$cat,$note);
            """;
        cmd.Parameters.AddWithValue("$id", occasion.Id);
        cmd.Parameters.AddWithValue("$t", occasion.Title);
        cmd.Parameters.AddWithValue("$cs", (int)occasion.CalendarSystem);
        cmd.Parameters.AddWithValue("$m", occasion.Month);
        cmd.Parameters.AddWithValue("$d", occasion.Day);
        cmd.Parameters.AddWithValue("$r", occasion.RepeatYearly ? 1 : 0);
        cmd.Parameters.AddWithValue("$days", string.Join(",", occasion.ReminderDaysBefore));
        cmd.Parameters.AddWithValue("$cat", (object?)occasion.Category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$note", (object?)occasion.Note ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SpecialOccasion>> GetSpecialOccasionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,title,calendar_system,month,day,repeat_yearly,reminder_days,category,note FROM special_occasions ORDER BY month,day,title";
        var list = new List<SpecialOccasion>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var reminders = reader.GetString(6)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .Distinct()
                .OrderByDescending(x => x)
                .ToArray();
            list.Add(new SpecialOccasion(
                reader.GetString(0), reader.GetString(1), (CalendarSystemKind)reader.GetInt32(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5) != 0, reminders,
                ReadNullable(reader, 7), ReadNullable(reader, 8)));
        }
        return list;
    }

    public async Task ScheduleReminderAsync(ReminderSchedule reminder, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO reminders(
                id,item_kind,item_id,fire_at_utc,state,title,body,privacy,
                repeat_count,repeat_interval_minutes,attempt)
            VALUES($id,$k,$item,$fire,$state,$title,$body,$privacy,$repeat,$interval,$attempt);
            """;
        cmd.Parameters.AddWithValue("$id", reminder.Id);
        cmd.Parameters.AddWithValue("$k", (int)reminder.ItemKind);
        cmd.Parameters.AddWithValue("$item", reminder.ItemId);
        cmd.Parameters.AddWithValue("$fire", reminder.FireAtUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$state", (int)reminder.State);
        cmd.Parameters.AddWithValue("$title", reminder.Title);
        cmd.Parameters.AddWithValue("$body", reminder.Body);
        cmd.Parameters.AddWithValue("$privacy", (int)reminder.Privacy);
        cmd.Parameters.AddWithValue("$repeat", Math.Clamp(reminder.RepeatCount, 1, 20));
        cmd.Parameters.AddWithValue("$interval", Math.Clamp(reminder.RepeatIntervalMinutes, 1, 1440));
        cmd.Parameters.AddWithValue("$attempt", Math.Max(0, reminder.Attempt));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReminderSchedule>> GetDueRemindersAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT id,item_kind,item_id,fire_at_utc,state,title,body,privacy,repeat_count,repeat_interval_minutes,attempt
            FROM reminders WHERE state=$pending AND fire_at_utc <= $now ORDER BY fire_at_utc LIMIT 32;
            """;
        cmd.Parameters.AddWithValue("$pending", (int)ReminderState.Pending);
        cmd.Parameters.AddWithValue("$now", nowUtc.ToUniversalTime().ToString("O"));
        var list = new List<ReminderSchedule>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(ReadReminder(reader));
        return list;
    }

    public async Task<IReadOnlyList<ReminderSchedule>> GetAwaitingAcknowledgementRemindersAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT id,item_kind,item_id,fire_at_utc,state,title,body,privacy,repeat_count,repeat_interval_minutes,attempt
            FROM reminders
            WHERE state=$fired AND attempt > 0
            ORDER BY fire_at_utc DESC
            LIMIT 16;
            """;
        cmd.Parameters.AddWithValue("$fired", (int)ReminderState.Fired);
        var list = new List<ReminderSchedule>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(ReadReminder(reader));
        return list;
    }

    public async Task<IReadOnlyList<ReminderSchedule>> GetPendingRemindersAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT id,item_kind,item_id,fire_at_utc,state,title,body,privacy,repeat_count,repeat_interval_minutes,attempt
            FROM reminders
            WHERE state=$pending AND fire_at_utc > $from AND fire_at_utc <= $to
            ORDER BY fire_at_utc LIMIT 512;
            """;
        cmd.Parameters.AddWithValue("$pending", (int)ReminderState.Pending);
        cmd.Parameters.AddWithValue("$from", fromUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$to", toUtc.ToUniversalTime().ToString("O"));

        var list = new List<ReminderSchedule>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(ReadReminder(reader));
        return list;
    }

    public async Task UpdateReminderDeliveryAsync(
        string reminderId,
        ReminderState state,
        int attempt,
        DateTimeOffset? newFireAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = newFireAtUtc is null
            ? "UPDATE reminders SET state=$s, attempt=$attempt WHERE id=$id"
            : "UPDATE reminders SET state=$s, attempt=$attempt, fire_at_utc=$fire WHERE id=$id";
        cmd.Parameters.AddWithValue("$s", (int)state);
        cmd.Parameters.AddWithValue("$attempt", Math.Max(0, attempt));
        cmd.Parameters.AddWithValue("$id", reminderId);
        if (newFireAtUtc is not null)
            cmd.Parameters.AddWithValue("$fire", newFireAtUtc.Value.ToUniversalTime().ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetReminderStateAsync(string reminderId, ReminderState state, DateTimeOffset? newFireAtUtc = null, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = newFireAtUtc is null
            ? "UPDATE reminders SET state=$s WHERE id=$id"
            : "UPDATE reminders SET state=$s, fire_at_utc=$fire WHERE id=$id";
        cmd.Parameters.AddWithValue("$s", (int)state);
        cmd.Parameters.AddWithValue("$id", reminderId);
        if (newFireAtUtc is not null) cmd.Parameters.AddWithValue("$fire", newFireAtUtc.Value.ToUniversalTime().ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddActivityAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO activity_log(id,created_at,action,item_kind,item_id,message) VALUES($id,$at,$a,$k,$item,$m)";
        cmd.Parameters.AddWithValue("$id", entry.Id);
        cmd.Parameters.AddWithValue("$at", entry.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$a", entry.Action);
        cmd.Parameters.AddWithValue("$k", entry.ItemKind is null ? DBNull.Value : (object)(int)entry.ItemKind.Value);
        cmd.Parameters.AddWithValue("$item", (object?)entry.ItemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$m", entry.Message);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearActivitiesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM activity_log";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ReminderSchedule?> GetReminderAsync(string reminderId, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,item_kind,item_id,fire_at_utc,state,title,body,privacy,repeat_count,repeat_interval_minutes,attempt FROM reminders WHERE id=$id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", reminderId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ReadReminder(reader);
    }

    public async Task<IReadOnlyList<ActivityLogEntry>> GetRecentActivitiesAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,created_at,action,item_kind,item_id,message FROM activity_log ORDER BY created_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        var list = new List<ActivityLogEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(new ActivityLogEntry(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2),
                reader.IsDBNull(3) ? null : (CalendarItemKind)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)));
        return list;
    }

    public async Task<DateTimeOffset?> GetLastOccasionSyncAsync(string source, int persianYear, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT synced_at FROM source_sync WHERE source=$s AND persian_year=$y LIMIT 1";
        cmd.Parameters.AddWithValue("$s", source);
        cmd.Parameters.AddWithValue("$y", persianYear);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is string text ? DateTimeOffset.Parse(text) : null;
    }

    public async Task SetLastOccasionSyncAsync(string source, int persianYear, DateTimeOffset syncedAt, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO source_sync(source,persian_year,synced_at) VALUES($s,$y,$at) ON CONFLICT(source,persian_year) DO UPDATE SET synced_at=excluded.synced_at";
        cmd.Parameters.AddWithValue("$s", source);
        cmd.Parameters.AddWithValue("$y", persianYear);
        cmd.Parameters.AddWithValue("$at", syncedAt.ToUniversalTime().ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SyncBatch> CreateSnapshotAsync(
        string sourceDeviceId,
        string targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        var items = new List<SyncEnvelope>();
        var snapshotAt = DateTimeOffset.UtcNow;

        await using var db = await OpenAsync(cancellationToken);

        async Task AddRowsAsync<T>(
            string sql,
            SyncEntityKind kind,
            Func<SqliteDataReader, T> projector,
            Func<T, string> idSelector,
            Func<T, DateTimeOffset>? modifiedSelector = null)
        {
            var cmd = db.CreateCommand();
            cmd.CommandText = sql;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var payload = projector(reader);
                var payloadJson = JsonSerializer.Serialize(payload);
                var modified = modifiedSelector?.Invoke(payload) ?? snapshotAt;
                var id = idSelector(payload);

                items.Add(new SyncEnvelope(
                    $"env_{Guid.NewGuid():N}",
                    kind,
                    id,
                    SyncMutationKind.Upsert,
                    1,
                    modified,
                    sourceDeviceId,
                    payloadJson,
                    ComputeContentHash(payloadJson)));
            }
        }

        await AddRowsAsync(
            """
            SELECT id,title,persian_date,start_time,end_time,all_day,
                   description,category,location,recurrence,privacy
            FROM events
            ORDER BY persian_date,id
            """,
            SyncEntityKind.Event,
            r => new CompanionEventPayload(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                ReadNullable(r, 3),
                ReadNullable(r, 4),
                r.GetInt32(5) != 0,
                ReadNullable(r, 6),
                ReadNullable(r, 7),
                ReadNullable(r, 8),
                r.GetInt32(9),
                r.GetInt32(10)),
            x => x.Id);

        await AddRowsAsync(
            """
            SELECT id,title,persian_date,due_time,priority,completed,
                   category,description
            FROM tasks
            ORDER BY persian_date,id
            """,
            SyncEntityKind.Task,
            r => new CompanionTaskPayload(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                ReadNullable(r, 3),
                r.GetInt32(4),
                r.GetInt32(5) != 0,
                ReadNullable(r, 6),
                ReadNullable(r, 7)),
            x => x.Id);

        await AddRowsAsync(
            """
            SELECT id,persian_date,text,pinned,modified_at
            FROM notes
            ORDER BY persian_date
            """,
            SyncEntityKind.Note,
            r => new CompanionNotePayload(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                r.GetInt32(3) != 0,
                r.GetString(4)),
            x => x.Id,
            x => DateTimeOffset.TryParse(x.ModifiedAtUtc, out var at)
                ? at
                : snapshotAt);

        await AddRowsAsync(
            """
            SELECT id,title,calendar_system,month,day,repeat_yearly,
                   reminder_days,category,note
            FROM special_occasions
            ORDER BY month,day,id
            """,
            SyncEntityKind.SpecialOccasion,
            r => new CompanionSpecialOccasionPayload(
                r.GetString(0),
                r.GetString(1),
                r.GetInt32(2),
                r.GetInt32(3),
                r.GetInt32(4),
                r.GetInt32(5) != 0,
                r.GetString(6)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(int.Parse)
                    .ToArray(),
                ReadNullable(r, 7),
                ReadNullable(r, 8)),
            x => x.Id);

        await AddRowsAsync(
            """
            SELECT id,item_kind,item_id,fire_at_utc,state,title,body,
                   privacy,repeat_count,repeat_interval_minutes,attempt
            FROM reminders
            ORDER BY fire_at_utc,id
            """,
            SyncEntityKind.Reminder,
            r => new CompanionReminderPayload(
                r.GetString(0),
                r.GetInt32(1),
                r.GetString(2),
                r.GetString(3),
                r.GetInt32(4),
                r.GetString(5),
                r.GetString(6),
                r.GetInt32(7),
                r.GetInt32(8),
                r.GetInt32(9),
                r.GetInt32(10)),
            x => x.Id);

        return new SyncBatch(
            CompanionProtocol.CurrentVersion,
            CompanionProtocol.NewBatchId(),
            sourceDeviceId,
            targetDeviceId,
            null,
            items,
            snapshotAt);
    }

    public async Task<SyncAck> ApplySnapshotAsync(
        SyncBatch batch,
        bool preferIncoming,
        CancellationToken cancellationToken = default)
    {
        if (batch.ProtocolVersion != CompanionProtocol.CurrentVersion)
            throw new NotSupportedException(
                $"Unsupported companion protocol version: {batch.ProtocolVersion}");

        var applied = new List<string>();
        var conflicts = new List<SyncConflict>();

        await using var db = await OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);

        foreach (var envelope in batch.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (envelope.Mutation == SyncMutationKind.Delete)
            {
                if (!preferIncoming)
                    continue;

                var delete = db.CreateCommand();
                delete.Transaction = (SqliteTransaction)tx;
                delete.Parameters.AddWithValue("$id", envelope.EntityId);
                delete.CommandText = envelope.EntityKind switch
                {
                    SyncEntityKind.Event => "DELETE FROM events WHERE id=$id",
                    SyncEntityKind.Task => "DELETE FROM tasks WHERE id=$id",
                    SyncEntityKind.Note => "DELETE FROM notes WHERE id=$id",
                    SyncEntityKind.SpecialOccasion => "DELETE FROM special_occasions WHERE id=$id",
                    SyncEntityKind.Reminder => "DELETE FROM reminders WHERE id=$id",
                    _ => string.Empty
                };

                if (!string.IsNullOrEmpty(delete.CommandText))
                {
                    await delete.ExecuteNonQueryAsync(cancellationToken);
                    applied.Add(envelope.EnvelopeId);
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(envelope.PayloadJson))
                continue;

            var insertMode = preferIncoming
                ? "INSERT OR REPLACE"
                : "INSERT OR IGNORE";

            var cmd = db.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;

            switch (envelope.EntityKind)
            {
                case SyncEntityKind.Event:
                {
                    var payload = JsonSerializer.Deserialize<CompanionEventPayload>(
                        envelope.PayloadJson)
                        ?? throw new InvalidOperationException("Invalid companion event payload.");

                    cmd.CommandText = $"""
                        {insertMode} INTO events(
                            id,title,persian_date,start_time,end_time,all_day,
                            description,category,location,recurrence,privacy)
                        VALUES($id,$title,$date,$start,$end,$all,$description,
                               $category,$location,$recurrence,$privacy)
                        """;
                    cmd.Parameters.AddWithValue("$id", payload.Id);
                    cmd.Parameters.AddWithValue("$title", payload.Title);
                    cmd.Parameters.AddWithValue("$date", payload.PersianDate);
                    cmd.Parameters.AddWithValue("$start", (object?)payload.StartTime ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$end", (object?)payload.EndTime ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$all", payload.AllDay ? 1 : 0);
                    cmd.Parameters.AddWithValue("$description", (object?)payload.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$category", (object?)payload.Category ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$location", (object?)payload.Location ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$recurrence", payload.Recurrence);
                    cmd.Parameters.AddWithValue("$privacy", payload.Privacy);
                    break;
                }

                case SyncEntityKind.Task:
                {
                    var payload = JsonSerializer.Deserialize<CompanionTaskPayload>(
                        envelope.PayloadJson)
                        ?? throw new InvalidOperationException("Invalid companion task payload.");

                    cmd.CommandText = $"""
                        {insertMode} INTO tasks(
                            id,title,persian_date,due_time,priority,completed,
                            category,description)
                        VALUES($id,$title,$date,$due,$priority,$completed,
                               $category,$description)
                        """;
                    cmd.Parameters.AddWithValue("$id", payload.Id);
                    cmd.Parameters.AddWithValue("$title", payload.Title);
                    cmd.Parameters.AddWithValue("$date", payload.PersianDate);
                    cmd.Parameters.AddWithValue("$due", (object?)payload.DueTime ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$priority", payload.Priority);
                    cmd.Parameters.AddWithValue("$completed", payload.Completed ? 1 : 0);
                    cmd.Parameters.AddWithValue("$category", (object?)payload.Category ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$description", (object?)payload.Description ?? DBNull.Value);
                    break;
                }

                case SyncEntityKind.Note:
                {
                    var payload = JsonSerializer.Deserialize<CompanionNotePayload>(
                        envelope.PayloadJson)
                        ?? throw new InvalidOperationException("Invalid companion note payload.");

                    if (!preferIncoming)
                    {
                        var exists = db.CreateCommand();
                        exists.Transaction = (SqliteTransaction)tx;
                        exists.CommandText = "SELECT 1 FROM notes WHERE persian_date=$date LIMIT 1";
                        exists.Parameters.AddWithValue("$date", payload.PersianDate);
                        if (await exists.ExecuteScalarAsync(cancellationToken) is not null)
                            continue;
                    }

                    cmd.CommandText = """
                        INSERT INTO notes(id,persian_date,text,pinned,modified_at)
                        VALUES($id,$date,$text,$pinned,$modified)
                        ON CONFLICT(persian_date) DO UPDATE SET
                            id=excluded.id,
                            text=excluded.text,
                            pinned=excluded.pinned,
                            modified_at=excluded.modified_at
                        """;
                    cmd.Parameters.AddWithValue("$id", payload.Id);
                    cmd.Parameters.AddWithValue("$date", payload.PersianDate);
                    cmd.Parameters.AddWithValue("$text", payload.Text);
                    cmd.Parameters.AddWithValue("$pinned", payload.Pinned ? 1 : 0);
                    cmd.Parameters.AddWithValue("$modified", payload.ModifiedAtUtc);
                    break;
                }

                case SyncEntityKind.SpecialOccasion:
                {
                    var payload = JsonSerializer.Deserialize<CompanionSpecialOccasionPayload>(
                        envelope.PayloadJson)
                        ?? throw new InvalidOperationException("Invalid companion occasion payload.");

                    cmd.CommandText = $"""
                        {insertMode} INTO special_occasions(
                            id,title,calendar_system,month,day,repeat_yearly,
                            reminder_days,category,note)
                        VALUES($id,$title,$calendar,$month,$day,$repeat,
                               $reminders,$category,$note)
                        """;
                    cmd.Parameters.AddWithValue("$id", payload.Id);
                    cmd.Parameters.AddWithValue("$title", payload.Title);
                    cmd.Parameters.AddWithValue("$calendar", payload.CalendarSystem);
                    cmd.Parameters.AddWithValue("$month", payload.Month);
                    cmd.Parameters.AddWithValue("$day", payload.Day);
                    cmd.Parameters.AddWithValue("$repeat", payload.RepeatYearly ? 1 : 0);
                    cmd.Parameters.AddWithValue("$reminders", string.Join(",", payload.ReminderDaysBefore));
                    cmd.Parameters.AddWithValue("$category", (object?)payload.Category ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$note", (object?)payload.Note ?? DBNull.Value);
                    break;
                }

                case SyncEntityKind.Reminder:
                {
                    var payload = JsonSerializer.Deserialize<CompanionReminderPayload>(
                        envelope.PayloadJson)
                        ?? throw new InvalidOperationException("Invalid companion reminder payload.");

                    cmd.CommandText = $"""
                        {insertMode} INTO reminders(
                            id,item_kind,item_id,fire_at_utc,state,title,body,
                            privacy,repeat_count,repeat_interval_minutes,attempt)
                        VALUES($id,$kind,$item,$fire,$state,$title,$body,
                               $privacy,$repeat,$interval,$attempt)
                        """;
                    cmd.Parameters.AddWithValue("$id", payload.Id);
                    cmd.Parameters.AddWithValue("$kind", payload.ItemKind);
                    cmd.Parameters.AddWithValue("$item", payload.ItemId);
                    cmd.Parameters.AddWithValue("$fire", payload.FireAtUtc);
                    cmd.Parameters.AddWithValue("$state", payload.State);
                    cmd.Parameters.AddWithValue("$title", payload.Title);
                    cmd.Parameters.AddWithValue("$body", payload.Body);
                    cmd.Parameters.AddWithValue("$privacy", payload.Privacy);
                    cmd.Parameters.AddWithValue("$repeat", payload.RepeatCount);
                    cmd.Parameters.AddWithValue("$interval", payload.RepeatIntervalMinutes);
                    cmd.Parameters.AddWithValue("$attempt", payload.Attempt);
                    break;
                }

                default:
                    continue;
            }

            var changed = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (changed > 0)
                applied.Add(envelope.EnvelopeId);
        }

        await tx.CommitAsync(cancellationToken);

        return new SyncAck(
            CompanionProtocol.CurrentVersion,
            batch.BatchId,
            batch.TargetDeviceId,
            DateTimeOffset.UtcNow.ToString("O"),
            applied,
            conflicts,
            DateTimeOffset.UtcNow);
    }

    private static string ComputeContentHash(string payloadJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ReminderSchedule ReadReminder(SqliteDataReader reader)
        => new(
            reader.GetString(0),
            (CalendarItemKind)reader.GetInt32(1),
            reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3)),
            (ReminderState)reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            (PrivacyLevel)reader.GetInt32(7),
            Math.Max(1, reader.GetInt32(8)),
            Math.Max(1, reader.GetInt32(9)),
            Math.Max(0, reader.GetInt32(10)));

    private static async Task EnsureColumnAsync(
        SqliteConnection db,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var pragma = db.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({table})";

        var exists = false;
        await using (var reader = await pragma.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
            return;

        var alter = db.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);
        return db;
    }

    private static TimeOnly? ParseTime(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : TimeOnly.Parse(reader.GetString(ordinal));

    private static string? ReadNullable(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static PersianDate ParsePersianDate(string value)
    {
        var parts = value.Split('/');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) ||
            !int.TryParse(parts[2], out var day))
            throw new FormatException($"Invalid Persian date stored in calendar database: {value}");

        return new PersianDate(year, month, day);
    }
    private static bool MatchesSpecialOccasion(
        PersianDate selectedDate,
        CalendarSystemKind calendarSystem,
        int month,
        int day)
    {
        if (calendarSystem == CalendarSystemKind.Persian)
            return selectedDate.Month == month && selectedDate.Day == day;

        var gregorian = selectedDate.ToDateOnly();

        if (calendarSystem == CalendarSystemKind.Gregorian)
            return gregorian.Month == month && gregorian.Day == day;

        var hijri = new HijriCalendar();
        var dt = gregorian.ToDateTime(TimeOnly.MinValue);
        return hijri.GetMonth(dt) == month && hijri.GetDayOfMonth(dt) == day;
    }

}