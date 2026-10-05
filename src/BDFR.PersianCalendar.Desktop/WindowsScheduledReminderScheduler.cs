using System.Security.Cryptography;
using System.Text;
using BDFR.PersianCalendar.Core;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace BDFR.PersianCalendar.Desktop;

public sealed class WindowsScheduledReminderScheduler : IReminderScheduler
{
    private const string GroupName = "bdfr-calendar";

    public Task<bool> TryScheduleAsync(
        ReminderSchedule reminder,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (reminder.FireAtUtc <= DateTimeOffset.UtcNow.AddSeconds(5))
            return Task.FromResult(false);

        try
        {
            var appNotification = new AppNotificationBuilder()
                .AddArgument("reminderId", reminder.Id)
                .AddText(reminder.Title)
                .AddText(reminder.Body)
                .BuildNotification();

            var xml = new XmlDocument();
            xml.LoadXml(appNotification.Payload);

            var tag = CreateTag(reminder.Id);
            var notifier = ToastNotificationManager.CreateToastNotifier();

            foreach (var existing in notifier.GetScheduledToastNotifications()
                         .Where(x => x.Tag == tag && x.Group == GroupName)
                         .ToArray())
            {
                notifier.RemoveFromSchedule(existing);
            }

            var toast = new ScheduledToastNotification(xml, reminder.FireAtUtc)
            {
                Tag = tag,
                Group = GroupName
            };

            notifier.AddToSchedule(toast);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static string CreateTag(string id)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return Convert.ToHexString(hash)[..16];
    }
}