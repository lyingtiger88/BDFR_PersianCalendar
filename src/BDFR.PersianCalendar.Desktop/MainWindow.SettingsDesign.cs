using System.Reflection;
using System.Security.Cryptography;
using BDFR.PersianCalendar.Core;
using Microsoft.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BDFR.PersianCalendar.Desktop;

/// <summary>
/// Windows Settings-inspired, navigable settings experience.
/// Existing advanced settings are retained and opened on demand, never discarded.
/// Account identity is never invented: local signed license codes contain only
/// edition/device/expiry, not a name, email or avatar.
/// </summary>
public sealed partial class MainWindow
{
    private readonly (string Key, string Icon, string Color, string Description)[] _settingsSections =
    [
        ("عمومی", "\uE80F", "#FF7754E8", "تنظیمات پایه و رفتار کلی برنامه"),
        ("نمایش و ظاهر", "\uE790", "#FFF18727", "قالب، رنگ‌ها، فونت و تصاویر"),
        ("تقویم و زمان", "\uE787", "#FF278EF5", "تقویم، مناسبت‌ها و اوقات"),
        ("برنامه و یادآور", "\uE823", "#FF14AB9C", "برنامه‌ها، هشدارها و رسانهٔ روز"),
        ("زروان دسک", "\uE753", "#FF238AF1", "ورود، Vault و فایل‌های ابری"),
        ("همگام‌سازی و پشتیبان‌گیری", "\uE895", "#FF168EA8", "فضای ابری و پوشهٔ داده‌ها"),
        ("امنیت و حریم خصوصی", "\uE72E", "#FFE4A728", "حریم خصوصی، احراز هویت و دستگاه"),
        ("حساب کاربری", "\uE77B", "#FFE8A51F", "ورود و وضعیت اشتراک سروری"),
        ("زبان و منطقه", "\uE774", "#FF1DB1AD", "زبان برنامه و تنظیمات محلی"),
        ("درباره زروان", "\uE946", "#FF288DF4", "نسخه و مشخصات نرم‌افزار")
    ];

    private const string SettingsTextColor = "#FF282532";
    private const string SettingsSecondaryColor = "#FF797586";
    private const string SettingsCardColor = "#FFFFFDFD";
    private StackPanel? _settingsNavigationList;
    private StackPanel? _settingsContent;
    private TextBox? _settingsSearchBox;
    private string _selectedSettingsSection = "عمومی";
    private Border? _settingsProfileContainer;
    private TextBlock? _settingsServerLicenseStatus;

    private FrameworkElement BuildWindowsSettingsPage()
    {
        var outer = new Grid
        {
            ColumnSpacing = 22,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var right = new StackPanel { Spacing = 15, Margin = new Thickness(0, 14, 0, 0) };
        _settingsProfileContainer = new Border { Child = BuildSettingsAccountProfile() };
        right.Children.Add(_settingsProfileContainer);

        _settingsSearchBox = new TextBox
        {
            PlaceholderText = "جست‌وجوی تنظیمات...",
            Height = 42,
            CornerRadius = new CornerRadius(13),
            Background = ThemeService.Brush("#FFFFFFFF"),
            BorderBrush = ThemeService.Brush("#FFE0DCE6"),
            BorderThickness = new Thickness(1),
            FlowDirection = FlowDirection.RightToLeft
        };
        _settingsSearchBox.TextChanged += (_, _) => RefreshSettingsCategories();
        right.Children.Add(_settingsSearchBox);

        _settingsNavigationList = new StackPanel { Spacing = 3 };
        right.Children.Add(_settingsNavigationList);
        Grid.SetColumn(right, 0);
        outer.Children.Add(right);

        var leftScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _settingsContent = new StackPanel
        {
            Spacing = 18,
            Padding = new Thickness(6, 12, 5, 25),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        leftScroll.Content = _settingsContent;
        Grid.SetColumn(leftScroll, 1);
        outer.Children.Add(leftScroll);

        RefreshSettingsCategories();
        ShowSettingsSection(_selectedSettingsSection);
        return outer;
    }

    private FrameworkElement BuildSettingsAccountProfile()
    {
        // Display the email from the user's secure Supabase Auth session only.
        // A local activation code has no name/email/photo fields; do not invent them.
        var account = new StackPanel { Spacing = 8 };
        var row = new StackPanel
        {
            Spacing = 10,
            Orientation = Orientation.Horizontal,
            FlowDirection = FlowDirection.RightToLeft
        };
        var avatar = new Border
        {
            Width = 65,
            Height = 65,
            Background = ThemeService.Brush("#FFE1D6FF"),
            CornerRadius = new CornerRadius(33),
            Child = new FontIcon
            {
                Glyph = "\uE77B",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 33,
                Foreground = ThemeService.Brush("#FF7755E8"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = _authClient.SignedInEmail ?? "کاربر زروان",
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(SettingsTextColor)
        });
        text.Children.Add(new TextBlock
        {
            Text = _authClient.SignedInEmail is null
                ? "برای مشاهده حساب وارد شوید"
                : "وضعیت اشتراک سرور: در حال بررسی...",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(SettingsSecondaryColor)
        });
        row.Children.Add(avatar);
        row.Children.Add(text);
        account.Children.Add(row);
        if (_authClient.SignedInEmail is not null)
        {
            var status = (TextBlock)text.Children[1];
            _ = LoadSupabaseLicenseStatusAsync(status);
        }

        if (_deskClient.IsAuthenticated && !string.IsNullOrWhiteSpace(_deskClient.Identifier))
        {
            account.Children.Add(new TextBlock
            {
                Text = "زروان دسک: " + _deskClient.Identifier,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = ThemeService.Brush(SettingsSecondaryColor)
            });
        }
        var profile = new Button
        {
            Content = "مشاهده حساب و وضعیت اشتراک",
            Background = ThemeService.Brush("#00FFFFFF"),
            BorderThickness = new Thickness(0),
            Foreground = ThemeService.Brush("#FF6654A8"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(4, 7, 4, 7)
        };
        profile.Click += (_, _) => ShowSettingsSection("حساب کاربری");
        account.Children.Add(profile);
        return account;
    }

    private void RefreshWindowsSettingsProfile()
    {
        if (_settingsProfileContainer is not null)
            _settingsProfileContainer.Child = BuildSettingsAccountProfile();
    }

    private async Task LoadSupabaseLicenseStatusAsync(TextBlock status)
    {
        try
        {
            var result = await _authClient.ReadLicenseStatusAsync();
            if (result.AccountState != "active")
                status.Text = "حساب زروان: " +
                    (result.AccountState == "registration_required" || result.AccountState == "provisional"
                        ? "ثبت‌نام دستگاه تکمیل نشده است"
                        : "وضعیت حساب: " + result.AccountState);
            else if (result.LicenseAccountState != "active" || result.ActiveLicenses == 0)
                status.Text = "حساب تأییدشده؛ لایسنس فعال پیدا نشد";
            else
                status.Text = $"حساب تأییدشده · {result.ActiveLicenses} مجوز فعال";
        }
        catch (Exception ex)
        {
            status.Text = "استعلام مجوز سرور ناموفق بود";
            StartupDiagnostics.Log("Account license verification unavailable: " + ex.Message);
        }
    }

    private void RefreshSettingsCategories()
    {
        if (_settingsNavigationList is null) return;
        _settingsNavigationList.Children.Clear();
        var term = _settingsSearchBox?.Text?.Trim() ?? "";
        foreach (var (key, icon, color, description) in _settingsSections)
        {
            if (term.Length > 0 &&
                !key.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !description.Contains(term, StringComparison.OrdinalIgnoreCase))
                continue;
            var row = new Grid { FlowDirection = FlowDirection.RightToLeft, ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var symbol = new FontIcon
            {
                Glyph = icon,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 25,
                Foreground = ThemeService.Brush(color),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(symbol, 0);
            var label = new TextBlock
            {
                Text = key,
                FontSize = 15,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeService.Brush(SettingsTextColor)
            };
            Grid.SetColumn(label, 1);
            row.Children.Add(symbol);
            row.Children.Add(label);
            var button = new Button
            {
                Content = row,
                Background = ThemeService.Brush(key == _selectedSettingsSection ? "#FFE8E2EB" : "#00FFFFFF"),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(11),
                MinHeight = 48,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(11, 7, 11, 7)
            };
            var selected = key;
            button.Click += (_, _) => ShowSettingsSection(selected);
            _settingsNavigationList.Children.Add(button);
        }
    }

    private static TextBlock SettingsLabel(string title, int size = 15, bool bold = false)
        => new()
        {
            Text = title,
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(SettingsTextColor),
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
        };

    private static TextBlock SettingsHint(string text)
        => new()
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(SettingsSecondaryColor)
        };

    private static Border SettingsCard(UIElement element, Thickness? padding = null)
        => new()
        {
            Background = ThemeService.Brush(SettingsCardColor),
            BorderBrush = ThemeService.Brush("#FFE8E2E6"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(17),
            Padding = padding ?? new Thickness(18),
            Child = element
        };

    private static FrameworkElement SettingsOptionRow(string title, string hint, UIElement control, string icon = "\uE713")
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            MinHeight = 63,
            FlowDirection = FlowDirection.RightToLeft
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = new FontIcon
        {
            Glyph = icon,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 20,
            Foreground = ThemeService.Brush("#FF665D77"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(glyph, 0);
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(SettingsLabel(title, 15, true));
        text.Children.Add(SettingsHint(hint));
        Grid.SetColumn(text, 1);
        var controlHost = new Border
        {
            Child = control,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 90
        };
        Grid.SetColumn(controlHost, 2);
        grid.Children.Add(glyph);
        grid.Children.Add(text);
        grid.Children.Add(controlHost);
        return grid;
    }

    private static void AddSettingsSeparator(StackPanel panel)
        => panel.Children.Add(new Border
        {
            Height = 1,
            Background = ThemeService.Brush("#FFEEE9EC"),
            Margin = new Thickness(0, 3, 0, 3)
        });

    private StackPanel SettingsGroup(string title, string description, string glyph, string color)
    {
        var panel = new StackPanel { Spacing = 12 };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            FlowDirection = FlowDirection.RightToLeft
        };
        header.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Foreground = ThemeService.Brush(color),
            FontSize = 27
        });
        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(SettingsLabel(title, 19, true));
        texts.Children.Add(SettingsHint(description));
        header.Children.Add(texts);
        panel.Children.Add(header);
        AddSettingsSeparator(panel);
        return panel;
    }

    private Button SettingsActionButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Background = ThemeService.Brush("#FFF6F0F5"),
            Foreground = ThemeService.Brush(SettingsTextColor),
            BorderBrush = ThemeService.Brush("#FFE5DFE6"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 10, 16, 10),
            MinHeight = 43
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void AddSettingsAction(StackPanel parent, string title, string description, string buttonText, Action action, string glyph = "\uE713")
    {
        parent.Children.Add(SettingsOptionRow(
            title, description,
            SettingsActionButton(buttonText, action),
            glyph));
        AddSettingsSeparator(parent);
    }

    private void ShowSettingsSection(string section)
    {
        if (_settingsContent is null) return;
        _selectedSettingsSection = section;
        RefreshSettingsCategories();
        _settingsContent.Children.Clear();
        var meta = _settingsSections.FirstOrDefault(x => x.Key == section);
        var description = !string.IsNullOrWhiteSpace(meta.Description) ? meta.Description : "شخصی‌سازی زروان";
        _settingsContent.Children.Add(SettingsLabel("تنظیمات زروان", 32, true));
        _settingsContent.Children.Add(SettingsHint("سفارشی‌سازی عملکرد، ظاهر و قابلیت‌های برنامه"));
        _settingsContent.Children.Add(SettingsLabel(section, 22, true));

        switch (section)
        {
            case "عمومی": BuildSettingsGeneral(); break;
            case "نمایش و ظاهر": BuildSettingsAppearance(); break;
            case "تقویم و زمان": BuildSettingsCalendar(); break;
            case "برنامه و یادآور": BuildSettingsPlanner(); break;
            case "زروان دسک": BuildSettingsDesk(); break;
            case "همگام‌سازی و پشتیبان‌گیری": BuildSettingsBackup(); break;
            case "امنیت و حریم خصوصی": BuildSettingsPrivacy(); break;
            case "حساب کاربری": BuildSettingsAccount(); break;
            case "زبان و منطقه": BuildSettingsLanguage(); break;
            case "درباره زروان": BuildSettingsAbout(); break;
        }
        _settingsContent.Children.Add(new Border
        {
            Background = ThemeService.Brush("#FFE7EBFF"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Child = SettingsHint("تغییرات ذخیره‌شدنی این صفحه مستقیماً در تنظیمات زروان ثبت می‌شوند. برخی تغییرات ظاهری ممکن است بازسازی رابط را نیاز داشته باشند.")
        });
    }

    private bool IsZurvanAutoStart()
    {
        try
        {
            var value = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")
                ?.GetValue("Zurvan") as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }

    private void SetZurvanAutoStart(bool enabled)
    {
        var executable = Environment.ProcessPath;
        if (enabled && (string.IsNullOrWhiteSpace(executable) ||
            !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("مسیر فایل اجرایی زروان در این نصب در دسترس نیست.");
        using var run = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true);
        if (run is null) throw new InvalidOperationException("دسترسی به تنظیمات شروع ویندوز ممکن نیست.");
        if (enabled) run.SetValue("Zurvan", $"\"{executable}\"", RegistryValueKind.String);
        else run.DeleteValue("Zurvan", throwOnMissingValue: false);
    }

    private ToggleSwitch SettingsToggle(bool enabled, Action<bool> apply)
    {
        var control = new ToggleSwitch
        {
            IsOn = enabled,
            OnContent = "فعال",
            OffContent = "خاموش",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 85
        };
        var changing = false;
        control.Toggled += (_, _) =>
        {
            if (changing) return;
            changing = true;
            try { apply(control.IsOn); enabled = control.IsOn; }
            catch (Exception ex)
            {
                StartupDiagnostics.Log("Settings toggle failed: " + ex);
                StatusText.Text = "تغییر تنظیمات ناموفق بود: " + ex.Message;
                control.IsOn = enabled;
            }
            finally { changing = false; }
        };
        return control;
    }

    private void BuildSettingsGeneral()
    {
        var contents = SettingsGroup("عمومی", "تنظیمات پایه و رفتار زروان", "\uE80F", "#FF7454E8");
        contents.Children.Add(SettingsOptionRow("شروع همراه ویندوز",
            "اجرای زروان هنگام ورود به حساب ویندوز",
            SettingsToggle(IsZurvanAutoStart(), SetZurvanAutoStart), "\uE768"));
        AddSettingsSeparator(contents);
        contents.Children.Add(SettingsOptionRow("نمایش تصاویر مناسبت‌ها",
            "نمایش تصاویر مربوط به روزهای دارای مناسبت",
            SettingsToggle(_settings.ShowOccasionPictures, value =>
            {
                _settings.ShowOccasionPictures = value;
                _settingsService.Save(_settings);
                BuildCalendar();
            }), "\uEB9F"));
        AddSettingsSeparator(contents);
        contents.Children.Add(SettingsOptionRow("رسانه‌های روز",
            "اجازه اتصال تصویر و فایل به روز انتخاب‌شده",
            SettingsToggle(_settings.DayMediaEnabled, SetDayMediaEnabled), "\uE8B7"));

        // As in the approved Windows Settings mockup, use two independent card
        // columns rather than a long stack with large unused horizontal space.
        var columns = new Grid
        {
            ColumnSpacing = 18,
            FlowDirection = FlowDirection.RightToLeft
        };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var right = new StackPanel { Spacing = 17 };
        right.Children.Add(SettingsCard(contents));
        right.Children.Add(new Border
        {
            Background = ThemeService.Brush("#FFE8EDFF"),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(14),
            Child = SettingsHint("تنظیمات این صفحه ذخیره می‌شوند؛ تغییرات فنی که هنوز به رابط قبلی وابسته‌اند، از «تنظیمات کامل» قابل دسترسی‌اند.")
        });
        Grid.SetColumn(right, 0);
        columns.Children.Add(right);

        var left = new StackPanel { Spacing = 17 };
        left.Children.Add(CreateSettingsVersionBanner());
        left.Children.Add(CreateSettingsQuickActionsCard());
        Grid.SetColumn(left, 1);
        columns.Children.Add(left);
        _settingsContent!.Children.Add(columns);
    }

    private FrameworkElement CreateSettingsVersionBanner()
    {
        var container = new Grid { MinHeight = 196 };
        container.Children.Add(new Image
        {
            Source = new SvgImageSource(new Uri("ms-appx:///picture/settings/landscape.svg")),
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        });
        var layout = new StackPanel
        {
            Spacing = 8,
            Padding = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        layout.Children.Add(SettingsLabel("زروان", 26, true));
        layout.Children.Add(SettingsHint("تقویم فارسی، برنامه‌ریزی و مدیریت هوشمند زمان"));
        layout.Children.Add(SettingsHint("نسخه نصب‌شده: " +
            (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "نامشخص")));
        var about = SettingsActionButton("درباره زروان", () => ShowSettingsSection("درباره زروان"));
        about.HorizontalAlignment = HorizontalAlignment.Right;
        layout.Children.Add(about);
        container.Children.Add(layout);
        return new Border
        {
            Background = ThemeService.Brush("#FFEDE4FB"),
            CornerRadius = new CornerRadius(20),
            MinHeight = 196,
            Child = container
        };
    }

    private FrameworkElement CreateSettingsQuickActionsCard()
    {
        var contents = SettingsGroup("شروع سریع", "دسترسی به بخش‌های پرکاربرد", "\uE8A7", "#FF9360E0");
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 2; i++) grid.RowDefinitions.Add(new RowDefinition());
        var actions = new (string Text, string Destination)[]
        {
            ("ایجاد برنامه", "برنامه‌ها"),
            ("زروان دسک", "زروان دسک"),
            ("تنظیم یادآور", "یادآورها"),
            ("تغییر تم", "نمایش و ظاهر"),
            ("حساب کاربری", "حساب کاربری"),
            ("تقویم", "تقویم")
        };
        for (var i = 0; i < actions.Length; i++)
        {
            var item = actions[i];
            var button = SettingsActionButton(item.Text, () =>
            {
                if (_settingsSections.Any(x => x.Key == item.Destination))
                    ShowSettingsSection(item.Destination);
                else ShowDesktopModule(item.Destination);
            });
            var color = item.Destination switch
            {
                "برنامه‌ها" => "#FF1C94F0",
                "زروان دسک" => "#FF278FF3",
                "یادآورها" => "#FFE89C22",
                "نمایش و ظاهر" => "#FF9B70DE",
                "حساب کاربری" => "#FFE4AC20",
                _ => "#FF3CB3A8"
            };
            var glyph = item.Destination switch
            {
                "برنامه‌ها" => "\uE80A",
                "زروان دسک" => "\uE753",
                "یادآورها" => "\uE823",
                "نمایش و ظاهر" => "\uE790",
                "حساب کاربری" => "\uE77B",
                _ => "\uE787"
            };
            var tileContents = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
            tileContents.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 28,
                Foreground = ThemeService.Brush(color),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            tileContents.Children.Add(SettingsLabel(item.Text, 12, true));
            button.Content = tileContents;
            button.Background = ThemeService.Brush("#FFFFFFFF");
            button.MinHeight = 92;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(button, i % 3);
            Grid.SetRow(button, i / 3);
            grid.Children.Add(button);
        }
        contents.Children.Add(grid);
        return SettingsCard(contents);
    }

    private void BuildSettingsAppearance()
    {
        var contents = SettingsGroup("نمایش و ظاهر", "انتخاب تم و تغییرات نمایشی", "\uE790", "#FFF39C42");
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 195
        };
        foreach (var item in AppearanceCatalog.Themes)
            combo.Items.Add(new ComboBoxItem { Content = item.DisplayName, Tag = item.Id });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(x => string.Equals((string?)x.Tag, _settings.ThemeId, StringComparison.OrdinalIgnoreCase));
        combo.SelectionChanged += async (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is string id)
                await SelectThemeAsync(id);
        };
        contents.Children.Add(SettingsOptionRow("تم زروان", "قالب فعلی برنامه", combo, "\uE790"));
        AddSettingsSeparator(contents);

        var zodiacStyles = new (string Id, string Title, string Glyph)[]
        {
            ("cosmic", "کیهانی کلاسیک", "✦"), ("neon", "فانتزی نورانی", "☄"),
            ("minimal", "مینیمال مدرن", "☾"), ("watercolor", "آبرنگی هنری", "✿"),
            ("mythic", "اسطوره‌ای", "♜"), ("cute", "کارتونی کیوت", "☺"),
            ("luxury", "سه‌بعدی لوکس", "✧")
        };
        var zodiacGroup = SettingsGroup("Zodiac Theme", "سبک مستقل نمایش نمادهای دوازده برج فلکی؛ جدا از تم کلی زروان.", "✦", "#FF8B67DD");
        var zodiacChoices = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (var column = 0; column < 3; column++)
            zodiacChoices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 3; row++)
            zodiacChoices.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < zodiacStyles.Length; index++)
        {
            var style = zodiacStyles[index];
            var selected = string.Equals(_settings.ZodiacThemeId, style.Id, StringComparison.OrdinalIgnoreCase);
            var tileContent = new StackPanel { Spacing = 5, Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center };
            var previewArt = new Image
            {
                Width = 82, Height = 82,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _ = ZodiacArtwork.PopulateAsync(previewArt, _selected.Month, style.Id);
            tileContent.Children.Add(previewArt);
            tileContent.Children.Add(new TextBlock { Text = style.Title, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = ThemeService.Brush(_theme.PrimaryText) });
            var tile = new Button
            {
                Content = new Border
                {
                    CornerRadius = new CornerRadius(11),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    BorderBrush = BrushWithAlpha(selected ? _theme.Accent : _theme.SecondaryText, 0x90),
                    Background = BrushWithAlpha(selected ? _theme.Accent : _theme.CardBackground, selected ? (byte)0x35 : (byte)0x45),
                    Child = tileContent
                },
                Padding = new Thickness(0),
                Background = BrushWithAlpha("#00FFFFFF", 0),
                BorderThickness = new Thickness(0)
            };
            tile.Click += (_, _) =>
            {
                _settings.ZodiacThemeId = style.Id;
                _settingsService.Save(_settings);
                StatusText.Text = "سبک نمادهای برج فلکی به «" + style.Title + "» تغییر کرد.";
                ShowSettingsSection("نمایش و ظاهر");
            };
            Grid.SetColumn(tile, index % 3);
            Grid.SetRow(tile, index / 3);
            zodiacChoices.Children.Add(tile);
        }
        zodiacGroup.Children.Add(zodiacChoices);
        contents.Children.Add(SettingsCard(zodiacGroup));
        AddSettingsSeparator(contents);

        contents.Children.Add(SettingsCard(BuildBackgroundManagerCard()));

        var navigationSide = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 195
        };
        navigationSide.Items.Add(new ComboBoxItem { Content = "سمت چپ صفحه", Tag = "left" });
        navigationSide.Items.Add(new ComboBoxItem { Content = "سمت راست صفحه", Tag = "right" });
        navigationSide.SelectedItem = navigationSide.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(x => string.Equals((string?)x.Tag, _settings.NavigationRailSide, StringComparison.OrdinalIgnoreCase))
            ?? navigationSide.Items.OfType<ComboBoxItem>().First();
        navigationSide.SelectionChanged += (_, _) =>
        {
            if (navigationSide.SelectedItem is ComboBoxItem item && item.Tag is string side)
            {
                _settings.NavigationRailSide = side;
                _settingsService.Save(_settings);
                ApplyWorkspaceLayoutSettingsLive();
                StatusText.Text = side == "left"
                    ? "محل سربرگ‌ها به سمت چپ صفحه منتقل شد."
                    : "محل سربرگ‌ها به سمت راست صفحه منتقل شد.";
            }
        };
        var navigationPosition = SettingsGroup("محل قرارگیری سربرگ‌ها",
            "نوار اصلی ناوبری را در سمت دلخواه صفحه قرار دهید.",
            "\\uE700", "#FF4B91C8");
        navigationPosition.Children.Add(SettingsOptionRow(
            "جایگاه سربرگ‌ها",
            "چپ یا راست صفحه",
            navigationSide,
            "\\uE700"));
        contents.Children.Add(SettingsCard(navigationPosition));
        AddSettingsSeparator(contents);

        AddSettingsAction(contents, "تنظیمات ظاهری پیشرفته",
            "رنگ‌ها، فونت‌ها، Glass Mode، پس‌زمینه و تصویرهای فصلی",
            "بازکردن تنظیمات کامل", OpenLegacySettings, "\uE771");
        _settingsContent!.Children.Add(SettingsCard(contents));

        var tabs = SettingsGroup("نمایش و پنهان‌سازی سربرگ‌ها",
            "هر سربرگ را مستقل روشن یا خاموش کنید. تغییرات فوری و ماندگارند.",
            "\uE80F", "#FF8B67DD");

        foreach (var label in OptionalNavigationTabs)
        {
            var name = label;
            var option = SettingsToggle(IsNavigationTabVisible(name),
                enabled => SetNavigationTabVisibility(name, enabled));
            tabs.Children.Add(SettingsOptionRow(name,
                "نمایش این بخش در فهرست کناری برنامه",
                option, NavigationIconGlyph(name)));
            AddSettingsSeparator(tabs);
        }

        tabs.Children.Add(SettingsHint(
            "تقویم و تنظیمات همیشه نمایش داده می‌شوند تا دسترسی به برنامه و بازیابی سربرگ‌های پنهان‌شده ممکن باشد."));
        _settingsContent.Children.Add(SettingsCard(tabs));
    }

    private StackPanel BuildBackgroundManagerCard()
    {
        var group = SettingsGroup("Background Manager", "انتخاب تصویر زمینه و تعیین منطق تعویض خودکار؛ مستقل از تم رنگی زروان.", "\uE91B", "#FF16A6B5");
        var currentMode = new TextBlock
        {
            Text = "روش فعلی: " + ((_settings.WallpaperMode ?? "manual") switch
            {
                "random" => "تصادفی",
                "seasonal" => "فصلی",
                "monthly" => "ماهانه",
                _ => "دستی"
            }),
            Foreground = ThemeService.Brush(_theme.SecondaryText),
            TextWrapping = TextWrapping.Wrap
        };
        group.Children.Add(currentMode);

        var chooseMode = MakeButton("انتخاب روش مدیریت تصویر زمینه");
        chooseMode.Click += async (_, _) =>
        {
            await ShowWallpaperModeDialogAsync();
            currentMode.Text = "روش فعلی: " + ((_settings.WallpaperMode ?? "manual") switch
            {
                "random" => "تصادفی",
                "seasonal" => "فصلی",
                "monthly" => "ماهانه",
                _ => "دستی"
            });
        };
        group.Children.Add(chooseMode);

        var seasonPicker = new ComboBox { Header = "فصل", HorizontalAlignment = HorizontalAlignment.Stretch };
        var seasons = new (string Id, string Label)[]
        {
            ("spring", "بهار"), ("summer", "تابستان"), ("autumn", "پاییز"), ("winter", "زمستان")
        };
        foreach (var season in seasons)
            seasonPicker.Items.Add(new ComboBoxItem { Content = season.Label, Tag = season.Id });
        seasonPicker.SelectedIndex = Array.FindIndex(seasons, x => x.Id == ThemeService.GetSeasonKey(PersianDate.Today().Month));
        if (seasonPicker.SelectedIndex < 0) seasonPicker.SelectedIndex = 0;

        var setSeason = MakeButton("انتخاب تصویر برای فصل");
        setSeason.Click += async (_, _) =>
        {
            var key = (seasonPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "spring";
            await PickWallpaperMappingAsync(true, key);
        };

        var monthPicker = new ComboBox { Header = "ماه", HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var i = 1; i <= 12; i++)
            monthPicker.Items.Add(new ComboBoxItem { Content = PersianDate.MonthNames[i - 1], Tag = i.ToString("00", CultureInfo.InvariantCulture) });
        monthPicker.SelectedIndex = Math.Clamp(PersianDate.Today().Month - 1, 0, 11);
        var setMonth = MakeButton("انتخاب تصویر برای ماه");
        setMonth.Click += async (_, _) =>
        {
            var key = (monthPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? PersianDate.Today().Month.ToString("00", CultureInfo.InvariantCulture);
            await PickWallpaperMappingAsync(false, key);
        };

        var mappingGrid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        mappingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mappingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var seasonStack = new StackPanel { Spacing = 7, Children = { seasonPicker, setSeason } };
        var monthStack = new StackPanel { Spacing = 7, Children = { monthPicker, setMonth } };
        Grid.SetColumn(seasonStack, 0); Grid.SetColumn(monthStack, 1);
        mappingGrid.Children.Add(seasonStack); mappingGrid.Children.Add(monthStack);
        group.Children.Add(mappingGrid);

        var actions = new StackPanel { Spacing = 8 };
        var manual = MakeButton("انتخاب تصویر زمینه دستی");
        manual.Click += async (_, _) => await SelectCustomBackgroundAsync();
        var apply = MakeButton("اعمال تصویر زمینه فعلی");
        apply.Click += async (_, _) => await ApplyBackgroundAsync();
        var gallery = MakeButton("بازکردن Wallpaper Manager و گالری آنلاین");
        gallery.Click += (_, _) => ShowDesktopModule("Wallpaper Manager");
        actions.Children.Add(manual); actions.Children.Add(apply); actions.Children.Add(gallery);
        group.Children.Add(actions);

        group.Children.Add(SettingsHint(
            "تصاویر فصلی و ماهانه در تنظیمات کاربر نگهداری می‌شوند. حالت تصادفی از گالری محلی Wallpaper Manager انتخاب می‌کند. فایل‌های اصلی به پوشه امن داده‌های زروان کپی می‌شوند."));
        return group;
    }

    private void BuildSettingsCalendar()
    {
        var contents = SettingsGroup("تقویم و زمان", "روزها، مناسبت‌ها و ترتیب تقویم‌ها", "\uE787", "#FF288AF0");
        AddSettingsAction(contents, "به‌روزرسانی مناسبت‌ها",
            "همگام‌سازی داده‌های سال انتخاب‌شده با time.ir", "به‌روزرسانی",
            () => _ = SyncOccasionsFromSettingsAsync(), "\uE895");
        AddSettingsAction(contents, "ترتیب تقویم‌ها و اوقات شرعی",
            "تنظیم تقویم شمسی، میلادی و هجری و محاسبه اوقات",
            "تنظیمات کامل", OpenLegacySettings, "\uE787");
        AddSettingsAction(contents, "مناسبت‌ها", "نمایش مناسبت‌های ثبت‌شده و رسمی",
            "بازکردن مناسبت‌ها", () => ShowDesktopModule("مناسبت‌ها"), "\uE787");
        _settingsContent!.Children.Add(SettingsCard(contents));
    }

    private async Task SyncOccasionsFromSettingsAsync()
    {
        try
        {
            await _repository.ReplaceOccasionsForYearAsync(
                _occasionSource.Name, _selected.Year,
                await _occasionSource.GetYearAsync(_selected.Year));
            await LoadSelectedDayAsync();
            await LoadCalendarCellOccasionsAsync(_year, _month);
            StatusText.Text = "مناسبت‌های سال به‌روز شدند.";
        }
        catch (Exception ex) { StatusText.Text = "خطا در دریافت مناسبت‌ها: " + ex.Message; }
    }

    private void BuildSettingsPlanner()
    {
        var contents = SettingsGroup("برنامه و یادآور",
            "تنظیم برنامه‌ها، یادآورها و اطلاعات روزها", "\uE823", "#FF1DAF9F");
        AddSettingsAction(contents, "برنامه‌های من", "نمای هفتگی و مدیریت کارهای ثبت‌شده",
            "بازکردن برنامه‌ها", () => ShowDesktopModule("برنامه‌ها"), "\uE80A");
        AddSettingsAction(contents, "یادآورهای فعال", "مرکز هشدارها و یادآورها",
            "بازکردن یادآورها", () => ShowDesktopModule("یادآورها"), "\uE823");
        contents.Children.Add(SettingsOptionRow("اتصال رسانه به روز",
            "فعال یا غیرفعال‌کردن فایل‌ها و تصاویر روزانه",
            SettingsToggle(_settings.DayMediaEnabled, SetDayMediaEnabled), "\uE8B7"));
        _settingsContent!.Children.Add(SettingsCard(contents));
    }

    private void BuildSettingsDesk()
    {
        var contents = SettingsGroup("زروان دسک",
            "حساب ابری و Vault رمزگذاری‌شدهٔ مستقل از لایسنس", "\uE753", "#FF258BF2");
        contents.Children.Add(SettingsHint(_deskClient.IsAuthenticated
            ? "حساب متصل: " + (_deskClient.Identifier ?? "زروان دسک")
            : "هنوز به زروان دسک وارد نشده‌اید."));
        AddSettingsAction(contents, "حساب و فایل‌منیجر",
            "ورود، ثبت‌نام، بازیابی Vault و مدیریت فایل‌های رمزگذاری‌شده",
            "ورود به زروان دسک", () => ShowDesktopModule("زروان دسک"), "\uE753");
        _settingsContent!.Children.Add(SettingsCard(contents));
    }

    private void BuildSettingsBackup()
    {
        var contents = SettingsGroup("همگام‌سازی و پشتیبان‌گیری",
            "دسترسی به اطلاعات و تنظیمات ذخیره‌شده", "\uE895", "#FF168FA9");
        AddSettingsAction(contents, "فضای ذخیره‌سازی ابری",
            "نمایش و مدیریت فایل‌های ذخیره‌شده روی سرور",
            "بازکردن فایل‌منیجر", () => ShowDesktopModule("زروان دسک"), "\uE753");
        AddSettingsAction(contents, "همگام‌سازی مناسبت‌ها",
            "دریافت دوباره مناسبت‌های سال جاری", "همگام‌سازی",
            () => _ = SyncOccasionsFromSettingsAsync(), "\uE895");
        AddSettingsAction(contents, "تنظیمات فضای ابری پیشرفته",
            "تنظیمات اتصال ابری پیشین زروان بدون حذف اطلاعات ذخیره‌شده",
            "تنظیمات کامل", OpenLegacySettings, "\uE713");
        _settingsContent!.Children.Add(SettingsCard(contents));
    }

    private void BuildSettingsPrivacy()
    {
        var contents = SettingsGroup("امنیت و حریم خصوصی",
            "وضعیت امنیت و حساب‌های مستقل", "\uE72E", "#FFDAAB28");
        contents.Children.Add(SettingsHint(
            "حساب Zurvan Desk مستقل از حساب اصلی است و کلید رمزگذاری فایل‌ها در صندوق امن دستگاه نگه‌داری می‌شود."));
        AddSettingsAction(contents, "حساب و اشتراک", "ورود به حساب اصلی و بررسی اشتراک معتبر در Supabase",
            "مشاهده حساب", () => ShowSettingsSection("حساب کاربری"), "\uE77B");
        AddSettingsAction(contents, "حساب ابری", "مدیریت ورود و بازیابی Vault",
            "بازکردن زروان دسک", () => ShowDesktopModule("زروان دسک"), "\uE753");
        _settingsContent!.Children.Add(SettingsCard(contents));
    }

    private void BuildSettingsAccount()
    {
        var contents = SettingsGroup("حساب کاربری",
            "ورود، ثبت‌نام و وضعیت اشتراک زروان", "\uE77B", "#FFE8AD1D");
        contents.Children.Add(SettingsHint(
            "وضعیت اشتراک صرفاً از سرور Supabase دریافت می‌شود؛ این صفحه مستقل از کدهای فعال‌سازی محلی قدیمی است."));
        contents.Children.Add(BuildAccountAuthPanel());
        _settingsServerLicenseStatus = SettingsHint(
            _authClient.SignedInEmail is null
                ? "برای مشاهده وضعیت اشتراک وارد حساب زروان شوید."
                : "در حال بررسی اشتراک سرور...");
        contents.Children.Add(_settingsServerLicenseStatus);
        if (_authClient.SignedInEmail is not null)
            _ = LoadSupabaseLicenseStatusAsync(_settingsServerLicenseStatus);
        _settingsContent!.Children.Add(SettingsCard(contents));
    }

    private void BuildSettingsLanguage()
    {
        var contents = SettingsGroup("زبان و منطقه",
            "زبان رابط زروان", "\uE774", "#FF16AFA7");
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 200
        };
        var languages = new (string Code, string Label)[]
        {
            ("fa", "فارسی"), ("en", "English"),
            ("ckb", "کوردی (سۆرانی)"), ("bal", "بلوچی")
        };
        foreach (var item in languages)
            combo.Items.Add(new ComboBoxItem { Content = item.Label, Tag = item.Code });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(x => string.Equals((string?)x.Tag, _settings.LanguageCode, StringComparison.OrdinalIgnoreCase))
            ?? combo.Items[0];
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is string code)
            {
                _settings.LanguageCode = code;
                _settingsService.Save(_settings);
                StatusText.Text = "زبان ترجیحی ذخیره شد. ترجمه کامل همه صفحات هنوز در حال تکمیل است.";
            }
        };
        contents.Children.Add(SettingsOptionRow("زبان برنامه",
            "انتخاب زبان ترجیحی؛ ترجمهٔ بعضی صفحات هنوز تکمیل نشده است",
            combo, "\uE774"));
        _settingsContent!.Children.Add(SettingsCard(contents));
    }

    private void BuildSettingsAbout()
    {
        var contents = SettingsGroup("درباره زروان",
            "نسخه و مشخصات نرم‌افزار", "\uE946", "#FF298FF7");
        var asm = Assembly.GetExecutingAssembly();
        contents.Children.Add(SettingsLabel("Zurvan | زروان", 22, true));
        contents.Children.Add(SettingsHint("نسخه: " + (asm.GetName().Version?.ToString() ?? "نامشخص")));
        var revision = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "GitCommit")?.Value;
        if (!string.IsNullOrWhiteSpace(revision))
            contents.Children.Add(SettingsHint("شناسه نسخه: " + revision));
        contents.Children.Add(SettingsHint("تقویم، برنامه‌ریز، مناسبت‌ها، یادآورها و فضای ابری شخصی زروان"));
        AddSettingsAction(contents, "امکانات پیشرفته",
            "نمایش تنظیمات قدیمی برای دسترسی به تمام قابلیت‌های موجود",
            "تنظیمات پیشرفته", OpenLegacySettings, "\uE713");
        _settingsContent!.Children.Add(SettingsCard(contents));
    }

    private void OpenLegacySettings()
    {
        try
        {
            var old = _settingsSurface ?? (Border)BuildSettingsPanelSafe();
            _settingsPanel.Visibility = Visibility.Visible;
            DetachDesktopElement(old);
            ShowSettingsSection("نمایش و ظاهر");
            // Replace the category page content with the full existing editor;
            // do not rebuild the editor or reset any saved values.
            _settingsContent!.Children.Clear();
            _settingsContent.Children.Add(SettingsActionButton("بازگشت به دسته‌بندی‌ها",
                () => ShowSettingsSection("نمایش و ظاهر")));
            _settingsContent.Children.Add(old);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Unable to open legacy settings: " + ex);
            StatusText.Text = "بازکردن تنظیمات پیشرفته ناموفق بود: " + ex.Message;
        }
    }
}
