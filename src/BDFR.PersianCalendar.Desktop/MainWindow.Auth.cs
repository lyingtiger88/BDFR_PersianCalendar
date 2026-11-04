using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private readonly ZurvanAuthClient _authClient = new();

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
        void Update()
        {
            var user = _authClient.SignedInEmail;
            var enabled = _authClient.Configured;
            status.Text = !enabled ? "پیکربندی Supabase در این نسخه انجام نشده است."
                : user is null ? "هنوز وارد حساب نشده‌اید." : $"حساب فعال: {user}";
            email.Visibility = password.Visibility = signIn.Visibility = signUp.Visibility =
                enabled && user is null ? Visibility.Visible : Visibility.Collapsed;
            signOut.Visibility = enabled && user is not null ? Visibility.Visible : Visibility.Collapsed;
        }
        async Task Run(Func<Task> action)
        {
            signIn.IsEnabled = signUp.IsEnabled = signOut.IsEnabled = false;
            status.Text = "در حال برقراری ارتباط امن...";
            try { await action(); password.Password = ""; Update(); }
            catch (Exception error) { status.Text = error.Message; }
            finally { signIn.IsEnabled = signUp.IsEnabled = signOut.IsEnabled = true; }
        }
        signIn.Click += async (_, _) => await Run(() => _authClient.SignInAsync(email.Text, password.Password));
        signUp.Click += async (_, _) => await Run(async () =>
        {
            var signedIn = await _authClient.SignUpAsync(email.Text, password.Password);
            if (!signedIn)
                status.Text = "لطفاً ایمیل خود را تأیید کرده و سپس وارد شوید.";
        });
        signOut.Click += async (_, _) => await Run(() => _authClient.SignOutAsync());
        panel.Children.Add(status);
        panel.Children.Add(email);
        panel.Children.Add(password);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(signIn);
        actions.Children.Add(signUp);
        actions.Children.Add(signOut);
        panel.Children.Add(actions);
        Update();
        return panel;
    }
}
