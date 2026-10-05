using BDFR.PersianCalendar.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace BDFR.PersianCalendar.Desktop;

public sealed class WindowsNotificationSink : INotificationSink
{
    public Task ShowAsync(ReminderSchedule reminder, CancellationToken cancellationToken = default)
    {
        var notification = new AppNotificationBuilder()
            .AddArgument("reminderId", reminder.Id)
            .AddText(reminder.Title)
            .AddText(reminder.Body)
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);
        return Task.CompletedTask;
    }
}