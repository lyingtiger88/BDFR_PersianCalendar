using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private readonly ZurvanAuthClient _authClient = new();

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
        root.Children.Add(new TextBlock {
            Text = "♛  " + (_authClient.SignedInEmail ?? "کاربر زروان"),
            FontSize = 21, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = gold, TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock { Text = "سطح: در انتظار دریافت", Foreground = muted });
        var stats = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var metrics = new[] { "★ XP", "◈ Silver", "♧ دعوت موفق" };
        for (int i = 0; i < metrics.Length; i++) {
            var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = metrics[i], Foreground = muted });
            stack.Children.Add(new TextBlock { Text = "—", Foreground = white, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center });
            var panel = new Border { Background = cardBrush, CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Child = stack };
            Grid.SetColumn(panel, i);
            stats.Children.Add(panel);
        }
        root.Children.Add(stats);
        root.Children.Add(new TextBlock { Text = "🏅 نشان‌ها (Badge)", Foreground = white, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        var badges = new Grid { ColumnSpacing = 6 };
        for (int i = 0; i < 4; i++) badges.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var names = new[] { "📅 کاربر فعال", "✦ کاوشگر", "♧ دعوت‌کننده", "♛ عضو ویژه" };
        for (int i = 0; i < names.Length; i++) {
            var item = new Border {
                Background = cardBrush, CornerRadius = new CornerRadius(12), Padding = new Thickness(8),
                Child = new TextBlock { Text = names[i], Foreground = gold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }
            };
            Grid.SetColumn(item, i);
            badges.Children.Add(item);
        }
        root.Children.Add(badges);
        root.Children.Add(new TextBlock { Text = "وضعیت دریافت نشان‌ها هنوز به حساب متصل نشده است.", Foreground = muted, TextWrapping = TextWrapping.Wrap });
        var referral = new StackPanel { Spacing = 7 };
        referral.Children.Add(new TextBlock { Text = "دعوت از دوستان", Foreground = white, FontSize = 18 });
        referral.Children.Add(new TextBlock { Text = "کد معرف: در انتظار دریافت از سرور", Foreground = gold });
        referral.Children.Add(new TextBlock { Text = "۱۰۰ XP برای هر دعوت موفق · Premium Silver برای هر ۱۵ دعوت موفق", Foreground = muted, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new Border { Background = cardBrush, CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Child = referral });
        return new Border { Background = navy, CornerRadius = new CornerRadius(22), Padding = new Thickness(16), Child = root };
    }

}
