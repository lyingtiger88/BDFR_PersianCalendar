using BDFR.PersianCalendar.Core;
using Microsoft.Windows.AppNotifications;

namespace BDFR.PersianCalendar.Desktop;

public sealed class WindowsNotificationSink : INotificationSink
{
    public Task ShowAsync(ReminderSchedule reminder, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppNotificationManager.Default.Show(ReminderNotificationFactory.Build(reminder));
        return Task.CompletedTask;
    }
}
