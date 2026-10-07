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
    private AppNotificationManager? _notificationManager;

    public App()
    {
        StartupDiagnostics.BeginSession();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            StartupDiagnostics.Log($"AppDomain unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            StartupDiagnostics.Log("ProcessExit raised.");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            StartupDiagnostics.Log($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };

        UnhandledException += OnUnhandledException;

        try
        {
            StartupDiagnostics.Log("App constructor: InitializeComponent starting.");
            InitializeComponent();
            StartupDiagnostics.Log("App constructor: InitializeComponent completed.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ShowFatal(ex);
            throw;
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            StartupDiagnostics.Log("OnLaunched entered.");

            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BDFR",
                "PersianCalendar");
            Directory.CreateDirectory(dataDir);

            var repository = new SqliteCalendarRepository(Path.Combine(dataDir, "calendar.db"));
            StartupDiagnostics.Log("Initializing SQLite.");
            await repository.InitializeAsync();
            StartupDiagnostics.Log("SQLite initialized.");

            var timeZone = TimeZoneInfo.Local;

            // Keep core planner services independent from Windows notification runtime.
            var planner = new PlannerService(repository, timeZone, platformScheduler: null);
            var specialOccasions = new SpecialOccasionService(repository, timeZone, platformScheduler: null);
            _reminderActions = new ReminderActionService(repository, platformScheduler: null);

            // time.ir can occasionally respond slowly. Request headers are applied
            // per request by TimeIrOccasionSource so retries can mimic a normal browser.
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
            var source = new TimeIrOccasionSource(_http);

            StartupDiagnostics.Log("Creating MainWindow.");
            MainWindow mainWindow;
            try
            {
                mainWindow = new MainWindow(repository, planner, source, specialOccasions);
                _window = mainWindow;
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"MainWindow creation failed; entering safe mode: {ex}");
                var fallback = new StartupFallbackWindow(ex);
                _window = fallback;
                fallback.Activate();
                StartupDiagnostics.Log("Safe-mode fallback window activated.");
                return;
            }

            _window.Closed += async (_, _) =>
            {
                StartupDiagnostics.Log("MainWindow closed.");

                if (_reminders is not null)
                    await _reminders.DisposeAsync();

                _http?.Dispose();

                if (_notificationManager is not null)
                {
                    try
                    {
                        _notificationManager.NotificationInvoked -= OnNotificationInvoked;
                        _notificationManager.Unregister();
                    }
                    catch (Exception ex)
                    {
                        StartupDiagnostics.Log($"Notification cleanup failed: {ex}");
                    }
                }
            };

            StartupDiagnostics.Log("Activating MainWindow.");
            _window.Activate();
            StartupDiagnostics.Log("MainWindow activated.");

            _ = InitializeBackgroundServicesAsync(
                repository,
                planner,
                source,
                specialOccasions,
                mainWindow);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ShowFatal(ex);
        }
    }

    private async Task InitializeBackgroundServicesAsync(
        ICalendarRepository repository,
        PlannerService planner,
        IOccasionSource source,
        SpecialOccasionService specialOccasions,
        MainWindow mainWindow)
    {
        try
        {
            // Allow the first frame to render before any optional background work.
            await Task.Delay(750);
            StartupDiagnostics.Log("Background initialization starting.");

            var notificationsAvailable = false;
            WindowsScheduledReminderScheduler? scheduler = null;

            try
            {
                _notificationManager = AppNotificationManager.Default;
                _notificationManager.NotificationInvoked += OnNotificationInvoked;
                _notificationManager.Register();
                notificationsAvailable = true;
                scheduler = new WindowsScheduledReminderScheduler();
                _reminderActions = new ReminderActionService(repository, scheduler);
                StartupDiagnostics.Log("App notifications registered.");
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"Notifications disabled for this session: {ex}");
                _notificationManager = null;

                mainWindow.DispatcherQueue.TryEnqueue(() =>
                    mainWindow.NotifyExternalChange(
                        "تقویم فعال است؛ اعلان‌های ویندوز در این اجرا غیرفعال شدند چون Windows App Runtime موردنیاز اعلان‌ها در دسترس نبود."));
            }

            // Always persist personal-occasion reminders locally.
            try
            {
                await specialOccasions.EnsureUpcomingRemindersAsync(new TimeOnly(9, 0));
                StartupDiagnostics.Log("Local reminder records initialized.");
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"Local reminder initialization failed: {ex}");
            }

            // Touch Windows notification APIs only after registration succeeded.
            if (notificationsAvailable && scheduler is not null)
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    foreach (var reminder in await repository.GetPendingRemindersAsync(now, now.AddDays(30)))
                    {
                        if (await scheduler.TryScheduleAsync(reminder))
                        {
                            await repository.SetReminderStateAsync(
                                reminder.Id,
                                ReminderState.Scheduled);
                        }
                    }

                    _reminders = new ReminderPollingService(
                        repository,
                        new WindowsNotificationSink());
                    _reminders.Start();

                    StartupDiagnostics.Log("Windows reminder scheduling and polling started.");
                }
                catch (Exception ex)
                {
                    StartupDiagnostics.Log($"Windows reminder subsystem disabled: {ex}");
                }
            }
            else
            {
                StartupDiagnostics.Log("Windows reminder scheduling/polling skipped because notifications are unavailable.");
            }

            await AutoSyncOccasionsAsync(repository, planner, source, mainWindow);
            StartupDiagnostics.Log("Background initialization completed.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Background initialization failed: {ex}");
            mainWindow.DispatcherQueue.TryEnqueue(() =>
                mainWindow.NotifyExternalChange(
                    "برنامه باز است، اما یکی از سرویس‌های پس‌زمینه با خطا مواجه شد. startup.log را بررسی کنید."));
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.Log($"WinUI unhandled exception (handled to keep app alive): {e.Exception}");

        try
        {
            if (_window is MainWindow main)
            {
                main.NotifyExternalChange(
                    "یک خطای رابط کاربری مهار شد؛ برنامه باز مانده است. جزئیات در startup.log ثبت شد.");
            }
        }
        catch (Exception notifyEx)
        {
            StartupDiagnostics.Log($"Unable to surface handled UI exception: {notifyEx}");
        }

        // WinUI UI-thread exceptions should not tear down the entire calendar.
        // Fatal CLR/AppDomain exceptions are still recorded by the AppDomain handler.
        e.Handled = true;
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
            if (action == "open")
                _window.Activate();

            if (_window is MainWindow main)
                main.NotifyExternalChange(message);
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

        if (last is not null &&
            DateTimeOffset.UtcNow - last.Value < TimeSpan.FromDays(7))
        {
            StartupDiagnostics.Log("time.ir auto-sync skipped; cache is fresh.");
            return;
        }

        try
        {
            var count = await planner.SyncOccasionsAsync(source, year);
            StartupDiagnostics.Log($"time.ir auto-sync completed: {count} items.");

            window.DispatcherQueue.TryEnqueue(() =>
                window.NotifyExternalChange(
                    $"همگام‌سازی خودکار: {count} مناسبت سال {year} به‌روز شد."));
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"time.ir auto-sync failed: {ex}");

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
