using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace BDFR.PersianCalendar.Desktop;

/// <summary>
/// Write-through breadcrumbs survive sudden native COM/WinUI termination.
/// Never stores the note body, auth tokens, account email or other user content.
/// This diagnostic subsystem cannot become a startup dependency.
/// </summary>
internal static class CrashDiagnostics
{
    private static readonly object Sync = new();
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BDFR", "PersianCalendar", "Diagnostics");
    private static readonly string Journal = Path.Combine(Root, "breadcrumbs.log");
    private static readonly string Marker = Path.Combine(Root, "session-active.txt");
    private static readonly string LastAction = Path.Combine(Root, "last-action.txt");
    private static readonly string CrashContext = Path.Combine(Root, "crash-context.txt");
    private const long MaxBytes = 2 * 1024 * 1024;
    private static int _initialized;
    private static int _closed;
    private static int _lastFirstChanceTick;
    private static long _uiPulseTicks;
    private static string _operation = "not-started";
    private static int _operationSequence;
    private static int _hangReported;
    private static CancellationTokenSource? _watchdogStop;
    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _uiTimer;

    internal static void Start()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
        try
        {
            Directory.CreateDirectory(Root);
            if (File.Exists(Marker))
                Record("PREVIOUS_SESSION_UNCLEAN_EXIT", "Previous process did not complete normal shutdown");
            File.WriteAllText(Marker,
                "pid=" + Environment.ProcessId + " started=" + DateTimeOffset.UtcNow.ToString("O"));
            Record("SESSION_BEGIN", "pid=" + Environment.ProcessId +
                " version=" + typeof(CrashDiagnostics).Assembly.GetName().Version +
                " os=" + Environment.OSVersion.Version);
            Record("REPORT_PATH", Root);
            ConfigureWer();
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            {
                try
                {
                    // First-chance exceptions are frequently expected. Rate-limit
                    // only native/COM/XAML failures; never persist message or payload.
                    var type = e.Exception.GetType().FullName ?? "";
                    if (!type.Contains("COMException", StringComparison.Ordinal) &&
                        !type.Contains("XamlParseException", StringComparison.Ordinal) &&
                        !type.Contains("AccessViolationException", StringComparison.Ordinal)) return;
                    var now = Environment.TickCount;
                    var previous = Volatile.Read(ref _lastFirstChanceTick);
                    if (unchecked(now - previous) < 2000) return;
                    Volatile.Write(ref _lastFirstChanceTick, now);
                    Record("FIRST_CHANCE", "exception=" + type +
                        " hresult=0x" + e.Exception.HResult.ToString("X8"));
                }
                catch { }
            };
        }
        catch { /* Logging must not block the application. */ }
    }

    internal static int BeginAction(string action)
    {
        var sequence = Interlocked.Increment(ref _operationSequence);
        Volatile.Write(ref _operation, action);
        Record("ACTION_BEGIN", "seq=" + sequence + " action=" + action);
        try
        {
            // Replace atomically enough for crash-time diagnosis. Content-free.
            File.WriteAllText(LastAction,
                DateTimeOffset.UtcNow.ToString("O") + " seq=" + sequence +
                " IN_PROGRESS " + action);
        }
        catch { }
        return sequence;
    }

    internal static void Step(int sequence, string step) =>
        Record("ACTION_STEP", "seq=" + sequence + " stage=" + step);

    internal static void EndAction(int sequence, string action)
    {
        Record("ACTION_END", "seq=" + sequence + " action=" + action);
        Volatile.Write(ref _operation, "idle");
        try
        {
            File.WriteAllText(LastAction,
                DateTimeOffset.UtcNow.ToString("O") + " seq=" + sequence +
                " COMPLETE " + action);
        }
        catch { }
    }

    internal static void Failure(int sequence, string operation, Exception ex)
    {
        Record("ACTION_ERROR", "seq=" + sequence + " action=" + operation +
            " exception=" + ex.GetType().FullName + " hresult=0x" +
            ex.HResult.ToString("X8"));
        Fatal("action:" + operation, ex, false);
    }

    internal static void Fatal(string context, Exception ex, bool terminating = true)
    {
        try
        {
            // Exception stack may include local paths. No request bodies or user data.
            var report = new StringBuilder()
                .AppendLine("UTC: " + DateTimeOffset.UtcNow.ToString("O"))
                .AppendLine("PID: " + Environment.ProcessId)
                .AppendLine("TID: " + Environment.CurrentManagedThreadId)
                .AppendLine("Context: " + context)
                .AppendLine("Last action: " + Volatile.Read(ref _operation))
                .AppendLine("Terminating: " + terminating)
                .AppendLine("Exception type: " + ex.GetType().FullName)
                .AppendLine("HResult: 0x" + ex.HResult.ToString("X8"))
                .AppendLine("Stack trace:")
                .AppendLine(ex.StackTrace ?? "<unavailable>")
                .ToString();
            lock (Sync)
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(CrashContext, report);
            }
            Record("MANAGED_EXCEPTION", "context=" + context +
                " type=" + ex.GetType().Name +
                " hresult=0x" + ex.HResult.ToString("X8") +
                " terminating=" + terminating);
        }
        catch { }
    }

    internal static void Record(string category, string detail = "")
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Root);
                RotateIfNecessary();
                var safeCategory = Clean(category, 64);
                var safeDetail = Clean(detail, 400);
                var line = DateTimeOffset.UtcNow.ToString("O") +
                    " | pid=" + Environment.ProcessId +
                    " | tid=" + Environment.CurrentManagedThreadId +
                    " | " + safeCategory + " | " + safeDetail + Environment.NewLine;
                using var file = new FileStream(Journal, FileMode.Append,
                    FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough);
                var bytes = Encoding.UTF8.GetBytes(line);
                file.Write(bytes, 0, bytes.Length);
                file.Flush(flushToDisk: true);
            }
        }
        catch { }
    }

    private static string Clean(string value, int maxLength)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static void RotateIfNecessary()
    {
        if (!File.Exists(Journal) || new FileInfo(Journal).Length < MaxBytes) return;
        for (var i = 3; i >= 1; i--)
        {
            var from = Journal + "." + i;
            var to = Journal + "." + (i + 1);
            if (File.Exists(to)) File.Delete(to);
            if (File.Exists(from)) File.Move(from, to);
        }
        File.Move(Journal, Journal + ".1", overwrite: true);
    }

    private static void ConfigureWer()
    {
        try
        {
            var dumps = Path.Combine(Root, "CrashDumps");
            Directory.CreateDirectory(dumps);
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\BDFR.PersianCalendar.Desktop.exe");
            key?.SetValue("DumpFolder", dumps, RegistryValueKind.ExpandString);
            // Mini dumps are practical to share; full dumps can expose sensitive app data.
            key?.SetValue("DumpType", 1, RegistryValueKind.DWord);
            key?.SetValue("DumpCount", 5, RegistryValueKind.DWord);
            Record("WER_CONFIGURED", "local minidumps enabled");
        }
        catch (Exception ex)
        {
            Record("WER_SETUP_FAILED", ex.GetType().Name);
        }
    }

    internal static void AttachUiWatchdog(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        try
        {
            if (_watchdogStop is not null) return;
            Interlocked.Exchange(ref _uiPulseTicks, DateTime.UtcNow.Ticks);
            _uiTimer = dispatcher.CreateTimer();
            _uiTimer.Interval = TimeSpan.FromSeconds(1);
            _uiTimer.IsRepeating = true;
            _uiTimer.Tick += (_, _) =>
            {
                Interlocked.Exchange(ref _uiPulseTicks, DateTime.UtcNow.Ticks);
                if (Interlocked.Exchange(ref _hangReported, 0) != 0)
                    Record("UI_RECOVERED", "Last action=" + Volatile.Read(ref _operation));
            };
            _uiTimer.Start();
            _watchdogStop = new CancellationTokenSource();
            var token = _watchdogStop.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
                        var age = (DateTime.UtcNow.Ticks - Interlocked.Read(ref _uiPulseTicks))
                            / (double)TimeSpan.TicksPerSecond;
                        if (age >= 8 && Interlocked.CompareExchange(ref _hangReported, 1, 0) == 0)
                            Record("UI_HANG_SUSPECTED", "dispatcher_unresponsive_seconds=" +
                                Math.Round(age) + " last_action=" + Volatile.Read(ref _operation));
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, token);
            Record("UI_WATCHDOG_STARTED", "threshold=8s");
        }
        catch (Exception ex) { Record("UI_WATCHDOG_UNAVAILABLE", ex.GetType().Name); }
    }

    internal static void CloseCleanly()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        try
        {
            Record("SESSION_END", "normal window close");
            _uiTimer?.Stop();
            _watchdogStop?.Cancel();
            if (File.Exists(Marker)) File.Delete(Marker);
        }
        catch { }
    }
}
