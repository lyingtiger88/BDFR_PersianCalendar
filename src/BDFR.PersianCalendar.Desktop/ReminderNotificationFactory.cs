using BDFR.PersianCalendar.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace BDFR.PersianCalendar.Desktop;

internal static class ReminderNotificationFactory
{
    public static AppNotification Build(ReminderSchedule reminder)
    {
        var title = reminder.Privacy == PrivacyLevel.Private
            ? "🔒 یادآور خصوصی"
            : reminder.Title;

        var body = reminder.Privacy == PrivacyLevel.Private
            ? "برای مشاهده جزئیات، برنامه زروان را باز کنید."
            : reminder.Body;

        return new AppNotificationBuilder()
            .AddArgument("action", "open")
            .AddArgument("reminderId", reminder.Id)
            .AddText(title)
            .AddText(body)
            .AddButton(new AppNotificationButton("۱. دیدم")
                .AddArgument("action", "done")
                .AddArgument("reminderId", reminder.Id))
            .AddButton(new AppNotificationButton("۲. بعداً یادآوری شود")
                .AddArgument("action", "snoozePrompt")
                .AddArgument("reminderId", reminder.Id))
            .AddButton(new AppNotificationButton("۳. لغو")
                .AddArgument("action", "dismiss")
                .AddArgument("reminderId", reminder.Id))
            .BuildNotification();
    }
}
