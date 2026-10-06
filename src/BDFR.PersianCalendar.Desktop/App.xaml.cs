using BDFR.PersianCalendar.Core;
using BDFR.PersianCalendar.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace BDFR.PersianCalendar.Desktop;

public partial class App : Application
{
    private Window? _window;
    private ReminderPollingService? _reminders;
    private ReminderActionService? _reminderActions;
    private HttpClient? _http;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => e.Handled = true;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BDFR",
            "PersianCalendar");
        Directory.CreateDirectory(dataDir);

        var repository = new SqliteCalendarRepository(Path.Combine(dataDir, "calendar.db"));
        await repository.InitializeAsync();

        var scheduler = new WindowsScheduledReminderScheduler();
        var timeZone = TimeZoneInfo.Local;
        var planner = new PlannerService(repository, timeZone, scheduler);
        var specialOccasions = new SpecialOccasionService(repository, timeZone, scheduler);
        _reminderActions = new ReminderActionService(repository, scheduler);

        var manager = AppNotificationManager.Default;
        manager.NotificationInvoked += OnNotificationInvoked;
        try { manager.Register(); } catch { }

        await specialOccasions.EnsureUpcomingRemindersAsync(new TimeOnly(9, 0));

        var now = DateTimeOffset.UtcNow;
        foreach (var reminder in await repository.GetPendingRemindersAsync(now, now.AddDays(30)))
        {
            if (await scheduler.TryScheduleAsync(reminder))
                await repository.SetReminderStateAsync(reminder.Id, ReminderState.Scheduled);
        }

        _reminders = new ReminderPollingService(repository, new WindowsNotificationSink());
        _reminders.Start();

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BDFR-PersianCalendar/1.0-test (+Windows 11)");
        var source = new TimeIrOccasionSource(_http);

        var mainWindow = new MainWindow(repository, planner, source, specialOccasions);
        _window = mainWindow;
        _window.Closed += async (_, _) =>
        {
            if (_reminders is not null) await _reminders.DisposeAsync();
            _http?.Dispose();
            try
            {
                manager.NotificationInvoked -= OnNotificationInvoked;
                manager.Unregister();
            }
            catch { }
        };

        _window.Activate();
        _ = AutoSyncOccasionsAsync(repository, planner, source, mainWindow);
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
        => _ = HandleNotificationInvocationAsync(args);

    private async Task HandleNotificationInvocationAsync(AppNotificationActivatedEventArgs args)
    {
        if (_reminderActions is null) return;

        var action = args.Arguments.ContainsKey("action")
            ? args.Arguments["action"]
            : "open";
        var reminderId = args.Arguments.ContainsKey("reminderId")
            ? args.Arguments["reminderId"]
            : string.Empty;

        if (string.IsNullOrWhiteSpace(reminderId))
        {
            _window?.DispatcherQueue.TryEnqueue(() => _window?.Activate());
            return;
        }

        var message = await _reminderActions.HandleAsync(reminderId, action);

        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (action == "open") _window.Activate();
            if (_window is MainWindow main) main.NotifyExternalChange(message);
        });
    }

    private static async Task AutoSyncOccasionsAsync(
        ICalendarRepository repository,
        PlannerService planner,
        IOccasionSource source,
        MainWindow window)
    {
        var year = PersianDate.Today().Year;
        var last = await repository.GetLastOccasionSyncAsync(source.Name, year);
        if (last is not null && DateTimeOffset.UtcNow - last.Value < TimeSpan.FromDays(7))
            return;

        try
        {
            var count = await planner.SyncOccasionsAsync(source, year);
            window.DispatcherQueue.TryEnqueue(() =>
                window.NotifyExternalChange($"همگام‌سازی خودکار: {count} مناسبت سال {year} به‌روز شد."));
        }
        catch (Exception ex)
        {
            await repository.AddActivityAsync(new ActivityLogEntry(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                "occasion-sync-failed",
                null,
                null,
                ex.Message));
        }
    }
}
