using System.Text.Json;

namespace BDFR.PersianCalendar.Desktop;

/// <summary>
/// Reliable, account-scoped and fault-isolated achievement reporting.
/// There is no WinUI object here: network/file errors must never break
/// calendar rendering, note persistence or app startup.
/// </summary>
internal static class ZurvanAchievementOutbox
{
    private sealed record Entry(
        string AccountEmail, string Kind, string? Section, int? Year, int? Month,
        string Key);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string PathOnDisk = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BDFR", "PersianCalendar", "achievements-pending.json");

    internal static void Record(ZurvanAuthClient client, string kind,
        string? section = null, int? year = null, int? month = null)
    {
        try
        {
            var account = client.SignedInEmail?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(account)) return;
            if (kind is not ("app_open" or "content_saved" or "explore" or "calendar_month")) return;
            if (kind == "explore" && section is not (
                "calendar" or "plans" or "occasions" or "reminders" or "desk" or "settings")) return;
            if (kind == "calendar_month" &&
                (year is < 1200 or > 1700 or null || month is < 1 or > 12 or null)) return;
            var eventKey = kind switch {
                "app_open" => "open:" + DateTime.UtcNow.ToString("yyyy-MM-dd"),
                "content_saved" => "first_content",
                "explore" => "explore:" + section,
                _ => "month:" + year + ":" + month
            };
            var entry = new Entry(account, kind, section, year, month, eventKey);
            CrashDiagnostics.Record("ACHIEVEMENT_ENQUEUE", "kind=" + kind);
            // The UI thread never awaits HTTP or disk I/O.
            _ = Task.Run(() => EnqueueAndFlushAsync(client, entry));
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Achievement queue rejected event: " + ex.GetType().Name);
        }
    }

    internal static void Retry(ZurvanAuthClient client)
    {
        try { _ = Task.Run(() => EnqueueAndFlushAsync(client, null)); }
        catch { /* Achievement reporting must not affect app startup. */ }
    }

    private static async Task EnqueueAndFlushAsync(ZurvanAuthClient client, Entry? incoming)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var pending = ReadQueue();
            if (incoming is not null &&
                !pending.Any(e => e.AccountEmail == incoming.AccountEmail && e.Key == incoming.Key))
            {
                pending.Add(incoming);
                if (pending.Count > 512) pending.RemoveRange(0, pending.Count - 512);
                SaveQueue(pending);
                CrashDiagnostics.Record("ACHIEVEMENT_QUEUED", "kind=" + incoming.Kind + " pending=" + pending.Count);
            }
            while (true)
            {
                var currentAccount = client.SignedInEmail;
                if (string.IsNullOrWhiteSpace(currentAccount)) break;
                var index = pending.FindIndex(entry =>
                    string.Equals(entry.AccountEmail, currentAccount,
                        StringComparison.OrdinalIgnoreCase));
                if (index < 0) break;
                var next = pending[index];
                CrashDiagnostics.Record("ACHIEVEMENT_SEND_BEGIN", "kind=" + next.Kind);
                try
                {
                    using var response = await client.ReportAchievementAsync(
                        next.Kind, next.Section, next.Year, next.Month).ConfigureAwait(false);
                    if (!response.RootElement.TryGetProperty("ok", out var ok) ||
                        ok.ValueKind != JsonValueKind.True) break;
                }
                catch (Exception ex)
                {
                    StartupDiagnostics.Log("Achievement delivery deferred: " + ex.GetType().Name);
                    CrashDiagnostics.Record("ACHIEVEMENT_SEND_FAILED", "kind=" + next.Kind + " exception=" + ex.GetType().Name);
                    break;
                }
                pending.RemoveAt(index);
                SaveQueue(pending);
                CrashDiagnostics.Record("ACHIEVEMENT_SEND_COMPLETE", "kind=" + next.Kind);
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Achievement queue unavailable: " + ex.GetType().Name);
            CrashDiagnostics.Record("ACHIEVEMENT_QUEUE_FAILED", "exception=" + ex.GetType().Name);
        }
        finally { Gate.Release(); }
    }

    private static List<Entry> ReadQueue()
    {
        try
        {
            if (!File.Exists(PathOnDisk)) return new List<Entry>();
            return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(PathOnDisk))
                ?.Where(x => !string.IsNullOrWhiteSpace(x.AccountEmail)
                    && !string.IsNullOrWhiteSpace(x.Key)).TakeLast(512).ToList()
                ?? new List<Entry>();
        }
        catch
        {
            return new List<Entry>();
        }
    }

    private static void SaveQueue(List<Entry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
        var tmp = PathOnDisk + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries));
        File.Move(tmp, PathOnDisk, true);
    }
}
