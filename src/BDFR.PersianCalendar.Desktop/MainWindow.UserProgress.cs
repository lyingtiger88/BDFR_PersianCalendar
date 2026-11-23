using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    // General XP never grants the Emperor/Empress/Special Advisor ranks.
    // All XP writes are verified on Supabase, not on the Windows machine.
    private async Task LoadUserProgressAsync(
        StackPanel host, string action = "status", bool? visible = null)
    {
        host.Children.Clear();
        host.Children.Add(SettingsHint("در حال دریافت امتیازها و نشان‌های حساب از سرور..."));
        if (_authClient.SignedInEmail is null)
        {
            host.Children.Clear();
            host.Children.Add(SettingsHint("برای مشاهده امتیازها و نشان‌ها وارد حساب زروان شوید."));
            return;
        }

        try
        {
            using var response = await _authClient.ReadUserProgressAsync(action, visible);
            var root = response.RootElement;
            var profile = root.GetProperty("profile");
            int ReadInt(JsonElement value, string key) =>
                value.TryGetProperty(key, out var item) && item.TryGetInt32(out var n) ? n : 0;
            string ReadText(JsonElement value, string key) =>
                value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String
                    ? item.GetString() ?? "" : "";
            bool ReadFlag(JsonElement value, string key) =>
                value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.True;

            host.Children.Clear();
            host.Children.Add(SettingsLabel("امتیاز و سطح کاربر", 18, true));
            var xp = ReadInt(profile, "total_xp");
            var level = ReadInt(profile, "level");
            host.Children.Add(SettingsLabel(
                $"سطح {ToPersianDigits(level.ToString())} · {ReadText(profile, "level_title")} · " +
                $"{ToPersianDigits(xp.ToString())} XP", 15, true));
            var next = profile.TryGetProperty("next_level_xp", out var nextValue) &&
                       nextValue.ValueKind == JsonValueKind.Number
                ? nextValue.GetInt32() : 0;
            host.Children.Add(new ProgressBar
            {
                Minimum = 0, Maximum = 100,
                Value = ReadInt(profile, "progress_percent"),
                Height = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
            host.Children.Add(SettingsHint(next == 0
                ? "به بالاترین سطح فعلی رسیده‌اید."
                : $"{ToPersianDigits(ReadInt(profile, "xp_to_next").ToString())} امتیاز تا سطح بعدی"));
            host.Children.Add(SettingsHint(
                $"حضور متوالی: {ToPersianDigits(ReadInt(profile, "daily_streak").ToString())} روز · " +
                $"بهترین رکورد: {ToPersianDigits(ReadInt(profile, "best_streak").ToString())} روز"));

            var claim = SettingsActionButton("ثبت حضور و دریافت امتیاز امروز", () =>
                _ = LoadUserProgressAsync(host, "claim_daily"));
            claim.IsEnabled = !ReadFlag(profile, "checked_in_today");
            host.Children.Add(claim);
            if (action == "claim_daily" && root.TryGetProperty("claim", out var claimResult) &&
                claimResult.ValueKind == JsonValueKind.Object)
            {
                host.Children.Add(SettingsHint(
                    ReadFlag(claimResult, "claimed")
                        ? $"امروز {ToPersianDigits(ReadInt(claimResult, "awarded_xp").ToString())} امتیاز دریافت کردی."
                        : "امتیاز امروز قبلاً ثبت شده است."));
            }

            AddSettingsSeparator(host);
            host.Children.Add(SettingsLabel("نشان‌های کسب‌کردنی", 17, true));
            if (root.TryGetProperty("badges", out var badges) &&
                badges.ValueKind == JsonValueKind.Array)
            {
                foreach (var badge in badges.EnumerateArray())
                {
                    var isEarned = ReadFlag(badge, "earned");
                    host.Children.Add(SettingsOptionRow(
                        (isEarned ? "✓ " : "🔒 ") + ReadText(badge, "icon") + " " + ReadText(badge, "name_fa"),
                        ReadText(badge, "description_fa") +
                        (isEarned ? " · دریافت‌شده" : " · هنوز کسب نشده"),
                        SettingsHint(isEarned ? "کسب شد" : "در انتظار"),
                        "\uE734"));
                    AddSettingsSeparator(host);
                }
            }

            host.Children.Add(SettingsLabel("تاریخچه امتیازها", 17, true));
            if (root.TryGetProperty("history", out var history) &&
                history.ValueKind == JsonValueKind.Array)
            {
                var count = 0;
                foreach (var item in history.EnumerateArray())
                {
                    if (count++ >= 8) break;
                    host.Children.Add(SettingsHint(
                        $"{ReadText(item, "title")} · +{ReadInt(item, "points")} XP · " +
                        ReadText(item, "created_at")));
                }
                if (count == 0)
                    host.Children.Add(SettingsHint("هنوز امتیازی ثبت نشده است."));
            }

            AddSettingsSeparator(host);
            host.Children.Add(SettingsLabel("جدول رتبه‌بندی", 17, true));
            host.Children.Add(SettingsHint(
                "نمایش امتیاز و نام کاربری در جدول عمومی اختیاری است؛ ایمیل نمایش داده نمی‌شود."));
            var isPublic = ReadFlag(profile, "leaderboard_opt_in");
            host.Children.Add(SettingsOptionRow("حضور در رتبه‌بندی عمومی",
                isPublic ? "امتیازت برای دیگر کاربران قابل مشاهده است." :
                           "امتیازت فقط برای خودت قابل مشاهده است.",
                SettingsToggle(isPublic, value =>
                    _ = LoadUserProgressAsync(host, "set_visibility", value)), "\uE734"));
            if (root.TryGetProperty("leaderboard", out var board) &&
                board.ValueKind == JsonValueKind.Array)
            {
                var count = 0;
                foreach (var item in board.EnumerateArray())
                {
                    if (count++ >= 10) break;
                    host.Children.Add(SettingsHint(
                        $"{ToPersianDigits(ReadInt(item, "position").ToString())}. " +
                        ReadText(item, "display_name") + " · " +
                        ToPersianDigits(ReadInt(item, "total_xp").ToString()) + " XP" +
                        (ReadFlag(item, "is_self") ? " (شما)" : "")));
                }
                if (count == 0)
                    host.Children.Add(SettingsHint("هنوز کاربری حضور در جدول رتبه‌بندی را فعال نکرده است."));
            }
            host.Children.Add(SettingsActionButton("به‌روزرسانی امتیاز و نشان‌ها",
                () => _ = LoadUserProgressAsync(host)));
        }
        catch (Exception e)
        {
            host.Children.Clear();
            host.Children.Add(SettingsHint("دریافت امتیاز از سرور ممکن نیست: " + e.Message));
            host.Children.Add(SettingsActionButton("تلاش دوباره",
                () => _ = LoadUserProgressAsync(host)));
        }
    }
}
