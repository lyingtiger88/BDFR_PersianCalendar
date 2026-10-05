using BDFR.PersianCalendar.Core;
using Microsoft.Data.Sqlite;

namespace BDFR.PersianCalendar.Infrastructure;

public sealed class SqliteCalendarRepository(string databasePath) : ICalendarRepository
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
            recurrence INTEGER NOT NULL DEFAULT 0
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
            body TEXT NOT NULL
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
        """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
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

        var events = new List<CalendarEvent>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT id,title,start_time,end_time,all_day,description,category,location,recurrence FROM events WHERE persian_date=$d ORDER BY all_day DESC,start_time";
            cmd.Parameters.AddWithValue("$d", key);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                events.Add(new CalendarEvent(
                    reader.GetString(0), reader.GetString(1), date,
                    ParseTime(reader, 2), ParseTime(reader, 3), reader.GetInt32(4) != 0,
                    ReadNullable(reader, 5), ReadNullable(reader, 6), ReadNullable(reader, 7),
                    (RecurrenceKind)reader.GetInt32(8)));
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
            INSERT INTO events(id,title,persian_date,start_time,end_time,all_day,description,category,location,recurrence)
            VALUES($id,$t,$d,$s,$e,$a,$desc,$cat,$loc,$r);
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
            INSERT OR REPLACE INTO reminders(id,item_kind,item_id,fire_at_utc,state,title,body)
            VALUES($id,$k,$item,$fire,$state,$title,$body);
            """;
        cmd.Parameters.AddWithValue("$id", reminder.Id);
        cmd.Parameters.AddWithValue("$k", (int)reminder.ItemKind);
        cmd.Parameters.AddWithValue("$item", reminder.ItemId);
        cmd.Parameters.AddWithValue("$fire", reminder.FireAtUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$state", (int)reminder.State);
        cmd.Parameters.AddWithValue("$title", reminder.Title);
        cmd.Parameters.AddWithValue("$body", reminder.Body);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReminderSchedule>> GetDueRemindersAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT id,item_kind,item_id,fire_at_utc,state,title,body
            FROM reminders WHERE state=$pending AND fire_at_utc <= $now ORDER BY fire_at_utc LIMIT 32;
            """;
        cmd.Parameters.AddWithValue("$pending", (int)ReminderState.Pending);
        cmd.Parameters.AddWithValue("$now", nowUtc.ToUniversalTime().ToString("O"));
        var list = new List<ReminderSchedule>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(new ReminderSchedule(reader.GetString(0), (CalendarItemKind)reader.GetInt32(1), reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)), (ReminderState)reader.GetInt32(4), reader.GetString(5), reader.GetString(6)));
        return list;
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
}