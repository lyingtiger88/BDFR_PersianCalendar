using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using BDFR.PersianCalendar.Core;

namespace BDFR.PersianCalendar.Desktop;

public sealed class LogonUiBridgePublisher
{
    private const string PipeName = "BDFR.LogonUI.Broker.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public async Task<bool> PublishAsync(
        ICalendarRepository repository,
        CancellationToken cancellationToken = default)
    {
        var today = PersianDate.Today();

        var day = await repository.GetDayAsync(today, cancellationToken);
        var pendingReminders = await repository.GetPendingRemindersAsync(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1),
            cancellationToken);

        var agenda = new List<BridgeAgendaItem>();

        foreach (var occasion in day.Occasions.Take(24))
        {
            agenda.Add(new BridgeAgendaItem(
                occasion.Id,
                "occasion",
                occasion.Title,
                null,
                null,
                true,
                string.Equals(occasion.Source, "personal", StringComparison.OrdinalIgnoreCase)
                    ? BridgePrivacy.Private
                    : BridgePrivacy.Public));
        }

        foreach (var item in day.Events.Take(24))
        {
            agenda.Add(new BridgeAgendaItem(
                item.Id,
                "event",
                item.Title,
                item.AllDay || item.StartTime is null
                    ? null
                    : ToDateTimeOffset(item.Date, item.StartTime.Value),
                item.AllDay || item.EndTime is null
                    ? null
                    : ToDateTimeOffset(item.Date, item.EndTime.Value),
                item.AllDay,
                BridgePrivacy.Private));
        }

        foreach (var task in day.Tasks.Where(x => !x.Completed).Take(16))
        {
            agenda.Add(new BridgeAgendaItem(
                task.Id,
                "task",
                task.Title,
                task.DueTime is null
                    ? null
                    : ToDateTimeOffset(task.Date, task.DueTime.Value),
                null,
                task.DueTime is null,
                BridgePrivacy.Private));
        }

        var reminders = pendingReminders
            .Where(x => x.State is ReminderState.Pending or ReminderState.Scheduled or ReminderState.Snoozed)
            .OrderBy(x => x.FireAtUtc)
            .Take(32)
            .Select(x => new BridgeReminderItem(
                x.Id,
                x.Title,
                x.FireAtUtc,
                BridgePrivacy.Private))
            .ToArray();

        var snapshot = new BridgeCalendarSnapshot(
            "bdfr.anahita",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(10),
            today.ToLongPersianString(),
            today.ToDateOnly().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FormatHijri(today.ToDateOnly()),
            agenda,
            reminders,
            1);

        var request = new BridgeRequest(
            "publish-calendar",
            "bdfr.anahita",
            snapshot,
            true,
            1);

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(300, cancellationToken);

            using var reader = new StreamReader(
                pipe,
                new UTF8Encoding(false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(false),
                bufferSize: 4096,
                leaveOpen: true)
            {
                AutoFlush = true
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
            var response = await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(response))
                return false;

            using var document = JsonDocument.Parse(response);
            return document.RootElement.TryGetProperty("Success", out var success) &&
                   success.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (
            ex is IOException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    private static DateTimeOffset ToDateTimeOffset(PersianDate date, TimeOnly time)
    {
        var local = date.ToDateOnly().ToDateTime(time, DateTimeKind.Unspecified);
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static string FormatHijri(DateOnly date)
    {
        var calendar = new HijriCalendar();
        var value = date.ToDateTime(TimeOnly.MinValue);

        return $"{ToArabicIndicDigits(calendar.GetYear(value).ToString(CultureInfo.InvariantCulture))}/" +
               $"{ToArabicIndicDigits(calendar.GetMonth(value).ToString("00", CultureInfo.InvariantCulture))}/" +
               $"{ToArabicIndicDigits(calendar.GetDayOfMonth(value).ToString("00", CultureInfo.InvariantCulture))}";
    }

    private static string ToArabicIndicDigits(string value)
    {
        const string latin = "0123456789";
        const string arabic = "٠١٢٣٤٥٦٧٨٩";
        var chars = value.ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            var index = latin.IndexOf(chars[i]);
            if (index >= 0)
                chars[i] = arabic[index];
        }

        return new string(chars);
    }

    private enum BridgePrivacy
    {
        Public = 0,
        Private = 1,
        Secret = 2
    }

    private sealed record BridgeAgendaItem(
        string Id,
        string Kind,
        string Title,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        bool AllDay,
        BridgePrivacy Privacy);

    private sealed record BridgeReminderItem(
        string Id,
        string Title,
        DateTimeOffset FireAtUtc,
        BridgePrivacy Privacy);

    private sealed record BridgeCalendarSnapshot(
        string ProviderId,
        DateTimeOffset GeneratedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        string PersianDate,
        string GregorianDate,
        string HijriDate,
        IReadOnlyList<BridgeAgendaItem> Agenda,
        IReadOnlyList<BridgeReminderItem> Reminders,
        int SchemaVersion);

    private sealed record BridgeRequest(
        string Operation,
        string? ProviderId,
        BridgeCalendarSnapshot? Calendar,
        bool IsLocked,
        int ProtocolVersion);
}
