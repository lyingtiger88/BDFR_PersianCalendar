using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private readonly ZurvanAuthClient _authClient = new();

    private readonly System.Collections.Generic.HashSet<string> _monthsReportedThisSession = new();
    private readonly System.Collections.Generic.HashSet<string> _sectionsReportedThisSession = new();

    private async Task ReportAchievementSafelyAsync(string kind, string? section = null,
        int? year = null, int? month = null)
    {
        if (_authClient.SignedInEmail is null) return;
        try
        {
            using var _ = await _authClient.ReportAchievementAsync(kind, section, year, month);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Achievement event deferred: " + ex.GetType().Name);
            if (kind == "calendar_month" && year.HasValue && month.HasValue)
                _monthsReportedThisSession.Remove(year + ":" + month);
            if (kind == "explore" && section is not null)
                _sectionsReportedThisSession.Remove(section);
        }
    }

    private void RefreshAchievementSession()
    {
        _monthsReportedThisSession.Clear();
        _sectionsReportedThisSession.Clear();
        ReportMonthAchievement();
        ReportExploreAchievement(_activeDesktopModule);
    }

    private void ReportMonthAchievement()
    {
        if (_authClient.SignedInEmail is null || _month is < 1 or > 12) return;
        var key = _year + ":" + _month;
        if (_monthsReportedThisSession.Add(key))
            _ = ReportAchievementSafelyAsync("calendar_month", year: _year, month: _month);
    }

    private void ReportExploreAchievement(string module)
    {
        if (_authClient.SignedInEmail is null) return;
        var section = module switch
        {
            "تقویم" => "calendar",
            "برنامه‌ها" => "plans",
            "مناسبت‌ها" => "occasions",
            "یادآورها" => "reminders",
            "زروان دسک" => "desk",
            "تنظیمات" => "settings",
            _ => null
        };
        if (section is not null && _sectionsReportedThisSession.Add(section))
            _ = ReportAchievementSafelyAsync("explore", section: section);
    }

    private DateTimeOffset _lastForegroundAchievementCheck = DateTimeOffset.MinValue;

    private void CountForegroundVisit()
    {
        if (_authClient.SignedInEmail is null) return;
        var now = DateTimeOffset.UtcNow;
        if (now - _lastForegroundAchievementCheck < TimeSpan.FromMinutes(5)) return;
        _lastForegroundAchievementCheck = now;
        _ = ClaimAppOpenXpSafelyAsync();
    }

    private async Task ClaimAppOpenXpSafelyAsync()
    {
        if (_authClient.SignedInEmail is null) return;
        try
        {
            using var _ = await _authClient.ClaimAppOpenXpAsync();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("XP open claim deferred: " + ex.GetType().Name);
        }
    }

    private FrameworkElement BuildAccountAuthPanel()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "حساب زروان", FontSize = 19 });
        panel.Children.Add(new TextBlock
        {
            Text = "ورود به حساب از فعال‌سازی لایسنس جداست.",
            TextWrapping = TextWrapping.Wrap
        });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var email = new TextBox { PlaceholderText = "ایمیل" };
        var password = new PasswordBox { PlaceholderText = "رمز عبور" };
        var signIn = new Button { Content = "ورود" };
        var signUp = new Button { Content = "ثبت‌نام" };
        var signOut = new Button { Content = "خروج از حساب" };
        var recovery = new Button { Content = "بازیابی رمز عبور" };
        void Update()
        {
            var user = _authClient.SignedInEmail;
            var enabled = _authClient.Configured;
            status.Text = !enabled ? "پیکربندی Supabase در این نسخه انجام نشده است."
                : user is null ? "هنوز وارد حساب نشده‌اید." : $"حساب فعال: {user}";
            email.Visibility = password.Visibility = signIn.Visibility = signUp.Visibility = recovery.Visibility =
                enabled && user is null ? Visibility.Visible : Visibility.Collapsed;
            signOut.Visibility = enabled && user is not null ? Visibility.Visible : Visibility.Collapsed;
        }
        async Task Run(Func<Task> action)
        {
            signIn.IsEnabled = signUp.IsEnabled = signOut.IsEnabled = recovery.IsEnabled = false;
            const string pending = "در حال برقراری ارتباط امن...";
            status.Text = pending;
            try {
                await action();
                var feedback = status.Text;
                password.Password = "";
                Update();
                RefreshWindowsSettingsProfile();
                RefreshAchievementSession();
                _ = RefreshLicensePolicyAsync();
                _ = ClaimAppOpenXpSafelyAsync();
                if (feedback != pending) status.Text = feedback;
            }
            catch (Exception error) { status.Text = error.Message; }
            finally { signIn.IsEnabled = signUp.IsEnabled = signOut.IsEnabled = recovery.IsEnabled = true; }
        }
        signIn.Click += async (_, _) => await Run(() => _authClient.SignInAsync(email.Text, password.Password));
        signUp.Click += async (_, _) => await Run(async () =>
        {
            var signedIn = await _authClient.SignUpAsync(email.Text, password.Password);
            if (!signedIn)
                status.Text = "لطفاً ایمیل خود را تأیید کرده و سپس وارد شوید.";
        });
        recovery.Click += async (_, _) => await Run(async () => {
            await _authClient.SendPasswordRecoveryAsync(email.Text);
            status.Text = "اگر حسابی با این ایمیل وجود داشته باشد، پیام بازیابی ارسال می‌شود.";
        });
        signOut.Click += async (_, _) => await Run(() => _authClient.SignOutAsync());
        panel.Children.Add(BuildAccountBadgeDashboard());
        panel.Children.Add(status);
        panel.Children.Add(email);
        panel.Children.Add(password);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(signIn);
        actions.Children.Add(signUp);
        actions.Children.Add(recovery);
        actions.Children.Add(signOut);
        panel.Children.Add(actions);
        Update();
        return panel;
    }

    private FrameworkElement BuildAccountBadgeDashboard()
    {
        var navy = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 23, 46));
        var cardBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 37, 68));
        var gold = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 199, 92));
        var muted = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 183, 184, 210));
        var white = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

        var root = new StackPanel { Spacing = 12 };
        var heading = new TextBlock {
            Text = "♛  " + (_authClient.SignedInEmail ?? "کاربر زروان"),
            FontSize = 21, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = gold, TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(heading);
        var level = new TextBlock { Text = "سطح: در حال دریافت...", Foreground = muted };
        root.Children.Add(level);
        var stats = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < 3; i++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var metrics = new[] { "★ XP", "◈ Silver", "♧ دعوت موفق" };
        var metricValues = new TextBlock[3];
        for (int i = 0; i < metrics.Length; i++)
        {
            var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = metrics[i], Foreground = muted });
            metricValues[i] = new TextBlock {
                Text = "…", Foreground = white, FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(metricValues[i]);
            var tile = new Border {
                Background = cardBrush, CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8), Child = stack
            };
            Grid.SetColumn(tile, i);
            stats.Children.Add(tile);
        }
        root.Children.Add(stats);
        root.Children.Add(new TextBlock {
            Text = "🏅 نشان‌ها (Badge)", Foreground = white,
            FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        var badges = new StackPanel { Spacing = 6 };
        badges.Children.Add(new TextBlock {
            Text = "در حال دریافت وضعیت نشان‌ها...", Foreground = muted,
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new Border {
            Background = cardBrush, CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12), Child = badges
        });

        var referral = new StackPanel { Spacing = 7 };
        referral.Children.Add(new TextBlock {
            Text = "دعوت از دوستان", Foreground = white, FontSize = 18
        });
        var codeText = new TextBlock {
            Text = "کد معرف: در حال دریافت...", Foreground = gold, TextWrapping = TextWrapping.Wrap
        };
        referral.Children.Add(codeText);
        var copyCode = new Button {
            Content = "کپی کد دعوت", Visibility = Visibility.Collapsed
        };
        referral.Children.Add(copyCode);
        referral.Children.Add(new TextBlock {
            Text = "۱۰۰ XP برای هر دعوت موفق · Premium Silver برای هر ۱۵ دعوت موفق",
            Foreground = muted, TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new Border {
            Background = cardBrush, CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12), Child = referral
        });

        var feedback = new TextBlock {
            Text = "", Foreground = muted, TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(feedback);
        var refresh = new Button { Content = "↻ به‌روزرسانی امتیازات" };
        root.Children.Add(refresh);

        async Task LoadProgressAsync()
        {
            if (_authClient.SignedInEmail is null)
            {
                feedback.Text = "برای نمایش موجودی و نشان‌ها ابتدا وارد حساب تأییدشده شوید.";
                level.Text = "سطح: نیاز به ورود";
                for (int i = 0; i < metricValues.Length; i++) metricValues[i].Text = "—";
                badges.Children.Clear();
                copyCode.Visibility = Visibility.Collapsed;
                codeText.Text = "کد معرف: —";
                return;
            }

            refresh.IsEnabled = false;
            feedback.Text = "در حال دریافت اطلاعات از سرور زروان...";
            try
            {
                using var document = await _authClient.ReadXpProgressAsync();
                var response = document.RootElement;
                var profile = response.GetProperty("profile");
                var refData = response.GetProperty("referral");
                var xp = profile.GetProperty("total_xp").GetInt64();

                heading.Text = "♛  " + (_authClient.SignedInEmail ?? "کاربر زروان");
                level.Text = "سطح: " + profile.GetProperty("level_title").GetString();
                metricValues[0].Text = xp.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
                metricValues[1].Text = refData.TryGetProperty("current_silver", out var silver)
                    && silver.ValueKind == System.Text.Json.JsonValueKind.Object ? "فعال" : "غیرفعال";
                metricValues[2].Text = refData.GetProperty("successful").GetInt32().ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

                badges.Children.Clear();
                if (response.TryGetProperty("special_rank", out var rank) &&
                    rank.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    rank.TryGetProperty("display_name_fa", out var title))
                {
                    badges.Children.Add(new TextBlock {
                        Text = "♛ " + title.GetString(), Foreground = gold,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold
                    });
                }
                if (response.TryGetProperty("badges", out var receivedBadges) &&
                    receivedBadges.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var badge in receivedBadges.EnumerateArray())
                    {
                        var earned = badge.TryGetProperty("earned", out var earnedValue) &&
                            earnedValue.ValueKind == System.Text.Json.JsonValueKind.True;
                        var name = badge.TryGetProperty("name_fa", out var nameValue)
                            ? nameValue.GetString() : "نشان";
                        badges.Children.Add(new TextBlock {
                            Text = (earned ? "🏅 " : "🔒 ") + name,
                            Foreground = earned ? gold : muted,
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                }
                if (badges.Children.Count == 0)
                    badges.Children.Add(new TextBlock { Text = "هنوز نشانی دریافت نشده است.", Foreground = muted });

                var code = refData.TryGetProperty("identifier", out var identifier)
                    && identifier.ValueKind == System.Text.Json.JsonValueKind.String
                    ? identifier.GetString() : null;
                codeText.Text = "کد معرف: " + (string.IsNullOrWhiteSpace(code) ? "هنوز تخصیص داده نشده" : code);
                copyCode.Visibility = string.IsNullOrWhiteSpace(code)
                    ? Visibility.Collapsed : Visibility.Visible;
                copyCode.Click += (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(code)) return;
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(code);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                    feedback.Text = "کد معرف کپی شد.";
                };
                feedback.Text = "مقادیر از حساب زروان دریافت و به‌روزرسانی شدند.";
            }
            catch (Exception ex)
            {
                feedback.Text = "دریافت امتیازات ناموفق بود: " + ex.Message;
                StartupDiagnostics.Log("XP account view failed: " + ex.GetType().Name);
            }
            finally { refresh.IsEnabled = true; }
        }
        refresh.Click += async (_, _) => await LoadProgressAsync();
        _ = LoadProgressAsync();
        return new Border {
            Background = navy, CornerRadius = new CornerRadius(22),
            Padding = new Thickness(16), Child = root
        };
    }

}
