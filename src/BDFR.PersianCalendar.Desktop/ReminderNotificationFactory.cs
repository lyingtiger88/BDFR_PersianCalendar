using BDFR.PersianCalendar.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace BDFR.PersianCalendar.Desktop;

internal static class ReminderNotificationFactory
{
    public static AppNotification Build(ReminderSchedule reminder)
        => new AppNotificationBuilder()
            .AddArgument("action", "open")
            .AddArgument("reminderId", reminder.Id)
            .AddText(reminder.Title)
            .AddText(reminder.Body)
            .AddButton(new AppNotificationButton("انجام شد")
                .AddArgument("action", "done")
                .AddArgument("reminderId", reminder.Id))
            .AddButton(new AppNotificationButton("۱۰ دقیقه بعد")
                .AddArgument("action", "snooze10")
                .AddArgument("reminderId", reminder.Id))
            .AddButton(new AppNotificationButton("رد کردن")
                .AddArgument("action", "dismiss")
                .AddArgument("reminderId", reminder.Id))
            .BuildNotification();
}
