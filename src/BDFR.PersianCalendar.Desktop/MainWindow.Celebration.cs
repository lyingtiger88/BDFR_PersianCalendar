using BDFR.PersianCalendar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private readonly ZurvanCelebrationClient _celebrationClient = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _celebrationTimer;
    private string? _temporaryCelebrationTheme;
    private bool _celebrationChecking;

    private void StartCelebrationMonitor()
    {
        _celebrationTimer = DispatcherQueue.CreateTimer();
        _celebrationTimer.Interval = TimeSpan.FromMinutes(1);
        _celebrationTimer.Tick += async (_, _) => await CheckCelebrationThemeAsync();
        _celebrationTimer.Start();
        _ = CheckCelebrationThemeAsync();
    }

    private async Task CheckCelebrationThemeAsync()
    {
        if (_celebrationChecking) return;
        _celebrationChecking = true;
        try
        {
            // Revalidate on startup and every minute. Never persist this temporary
            // overlay into the user's chosen permanent appearance.
            var directory = await _celebrationClient.LoadAsync();
            var next = ZurvanCelebrationClient.Today(directory)?.ThemeId;
            if (next == _temporaryCelebrationTheme) return;
            _temporaryCelebrationTheme = next;
            _theme = next is null
                ? _themeService.ResolveAppearance(_settings, _month)
                : _themeService.Load(next);
            ApplyAppearanceBrushesOnly();
            BuildCalendar();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Celebration theme refresh failed: " + ex.Message);
            // Never retain a dated overlay when it cannot be revalidated.
            if (_temporaryCelebrationTheme is not null)
            {
                _temporaryCelebrationTheme = null;
                _theme = _themeService.ResolveAppearance(_settings, _month);
                ApplyAppearanceBrushesOnly();
                BuildCalendar();
            }
        }
        finally { _celebrationChecking = false; }
    }

    private async Task ShowCreatorCelebrationAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "🎉 تنظیمات خصوصی Celebration",
            CloseButtonText = "بستن",
            XamlRoot = (Content as FrameworkElement)?.XamlRoot
        };
        var panel = new StackPanel { Spacing = 9, Padding = new Thickness(10) };
        var info = new TextBlock
        {
            Text = "دسترسی فقط برای Creator تاییدشده توسط Supabase است.",
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(info);

        var email = new TextBox { Header = "ایمیل حساب Supabase" };
        var password = new PasswordBox { Header = "رمز عبور" };
        panel.Children.Add(email);
        panel.Children.Add(password);

        var accounts = new ComboBox { Header = "حساب دریافت‌کننده", HorizontalAlignment = HorizontalAlignment.Stretch };
        var occasions = new ComboBox { Header = "نوع مناسبت", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (id, name) in new[]
        {
            ("birthday", "Birthday Celebration · تولد"),
            ("meeting_anniversary", "First Meeting Anniversary · سالگرد آشنایی"),
            ("wedding_anniversary", "Wedding Anniversary · سالگرد ازدواج")
        })
            occasions.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        occasions.SelectedIndex = 0;

        var months = new NumberBox { Header = "ماه شمسی", Minimum = 1, Maximum = 12, Value = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var days = new NumberBox { Header = "روز شمسی", Minimum = 1, Maximum = 31, Value = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var themes = new ComboBox { Header = "تم اختصاصی", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var theme in AppearanceCatalog.Themes)
            themes.Items.Add(new ComboBoxItem { Content = theme.DisplayName, Tag = theme.Id });
        themes.SelectedIndex = 0;
        var title = new TextBox { Header = "عنوان دلخواه", MaxLength = 80 };
        var enabled = new CheckBox { Content = "فعال", IsChecked = true };
        var records = new StackPanel { Spacing = 4 };
        var editor = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        foreach (var control in new FrameworkElement[] { accounts, occasions, months, days, themes, title, enabled, records })
            editor.Children.Add(control);

        async Task RefreshAsync()
        {
            try
            {
                var data = await _celebrationClient.LoadAsync();
                if (!data.IsCreator)
                {
                    info.Text = "این حساب مجوز مدیریت مناسبت‌ها را ندارد.";
                    editor.Visibility = Visibility.Collapsed;
                    return;
                }
                info.Text = "Creator تایید شد. هر تغییر در Supabase ذخیره می‌شود.";
                editor.Visibility = Visibility.Visible;
                accounts.Items.Clear();
                foreach (var item in data.Accounts ?? [])
                    accounts.Items.Add(new ComboBoxItem { Content = item.Username, Tag = item.Id });
                if (accounts.Items.Count > 0) accounts.SelectedIndex = 0;
                records.Children.Clear();
                foreach (var row in data.Schedules ?? [])
                {
                    var button = new Button
                    {
                        Content = $"{row.OccasionType} · {row.Month}/{row.Day} · {row.ThemeId} · {row.AccountId[..Math.Min(row.AccountId.Length, 8)]}",
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    button.Click += (_, _) =>
                    {
                        for (var i = 0; i < accounts.Items.Count; i++)
                            if ((accounts.Items[i] as ComboBoxItem)?.Tag as string == row.AccountId)
                                accounts.SelectedIndex = i;
                        for (var i = 0; i < occasions.Items.Count; i++)
                            if ((occasions.Items[i] as ComboBoxItem)?.Tag as string == row.OccasionType)
                                occasions.SelectedIndex = i;
                        for (var i = 0; i < themes.Items.Count; i++)
                            if ((themes.Items[i] as ComboBoxItem)?.Tag as string == row.ThemeId)
                                themes.SelectedIndex = i;
                        months.Value = row.Month;
                        days.Value = row.Day;
                        title.Text = row.Title ?? "";
                        enabled.IsChecked = row.Enabled;
                    };
                    records.Children.Add(button);
                    var deleteButton = new Button { Content = "حذف این مناسبت" };
                    deleteButton.Click += async (_, _) =>
                    {
                        try
                        {
                            await _celebrationClient.DeleteAsync(row.Id);
                            await RefreshAsync();
                            await CheckCelebrationThemeAsync();
                        }
                        catch (Exception ex) { info.Text = ex.Message; }
                    };
                    records.Children.Add(deleteButton);
                }
            }
            catch (Exception ex) { info.Text = ex.Message; }
        }

        var signIn = new Button { Content = "ورود / بررسی دسترسی Creator" };
        signIn.Click += async (_, _) =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(email.Text))
                    await new ZurvanAuthClient().SignInAsync(email.Text, password.Password);
                password.Password = "";
                await RefreshAsync();
            }
            catch (Exception ex) { info.Text = ex.Message; }
        };
        panel.Children.Add(signIn);

        var save = new Button { Content = "ذخیره مناسبت" };
        save.Click += async (_, _) =>
        {
            try
            {
                var accountId = (accounts.SelectedItem as ComboBoxItem)?.Tag as string
                    ?? throw new InvalidOperationException("حساب مقصد انتخاب نشده است.");
                var occasion = (occasions.SelectedItem as ComboBoxItem)?.Tag as string ?? "birthday";
                var theme = (themes.SelectedItem as ComboBoxItem)?.Tag as string ?? "zara-pastel";
                var month = checked((int)months.Value);
                var day = checked((int)days.Value);
                if (month is < 1 or > 12 || day < 1 ||
                    day > (month <= 6 ? 31 : 30))
                    throw new InvalidOperationException("تاریخ شمسی معتبر نیست.");
                await _celebrationClient.SaveAsync(accountId, occasion, month, day, theme,
                    title.Text, enabled.IsChecked == true);
                await RefreshAsync();
                await CheckCelebrationThemeAsync();
            }
            catch (Exception ex) { info.Text = ex.Message; }
        };
        editor.Children.Add(save);
        panel.Children.Add(editor);
        dialog.Content = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 650,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        await dialog.ShowAsync();
    }
}
