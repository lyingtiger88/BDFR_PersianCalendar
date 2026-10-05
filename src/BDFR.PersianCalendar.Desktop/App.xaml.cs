using BDFR.PersianCalendar.Core;
using BDFR.PersianCalendar.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace BDFR.PersianCalendar.Desktop;

public partial class App : Application
{
    private Window? _window;
    private ReminderPollingService? _reminders;
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
            "BDFR", "PersianCalendar");
        Directory.CreateDirectory(dataDir);

        var repository = new SqliteCalendarRepository(Path.Combine(dataDir, "calendar.db"));
        await repository.InitializeAsync();

        try { AppNotificationManager.Default.Register(); } catch { }

        var scheduledNotifications = new WindowsScheduledReminderScheduler();
        var timeZone = TimeZoneInfo.Local;
        var planner = new PlannerService(repository, timeZone, scheduledNotifications);
        var specialOccasions = new SpecialOccasionService(repository, timeZone, scheduledNotifications);

        await specialOccasions.EnsureUpcomingRemindersAsync(new TimeOnly(9, 0));

        var now = DateTimeOffset.UtcNow;
        foreach (var reminder in await repository.GetPendingRemindersAsync(now, now.AddDays(30)))
        {
            if (await scheduledNotifications.TryScheduleAsync(reminder))
                await repository.SetReminderStateAsync(reminder.Id, ReminderState.Scheduled);
        }

        var notificationSink = new WindowsNotificationSink();
        _reminders = new ReminderPollingService(repository, notificationSink);
        _reminders.Start();

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BDFR-PersianCalendar/0.1 (+Windows 11)");
        var source = new TimeIrOccasionSource(_http);

        _window = new MainWindow(repository, planner, source, specialOccasions);
        _window.Closed += async (_, _) =>
        {
            if (_reminders is not null) await _reminders.DisposeAsync();
            _http?.Dispose();
            try { AppNotificationManager.Default.Unregister(); } catch { }
        };
        _window.Activate();
    }
}