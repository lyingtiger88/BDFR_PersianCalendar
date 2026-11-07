using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private readonly List<(Button Button, string Icon, string Label)> _navigationEntries = new();
    private bool _navigationExpanded = true;
    private const double ZurvanDesignNavigationWidth = 290;

    private Border BuildNavigationRail()
    {
        var shell = new Grid
        {
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Thickness(16, 20, 16, 22),
            RowSpacing = 18
        };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var brand = new StackPanel { Spacing = 3, Margin = new Thickness(9, 8, 9, 24), HorizontalAlignment = HorizontalAlignment.Right };
        brand.Children.Add(new TextBlock
        {
            Text = "◈",
            FontSize = 39,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = ThemeService.Brush(_theme.Accent)
        });
        brand.Children.Add(new TextBlock
        {
            Text = "زروان",
            FontSize = 21,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });
        Grid.SetRow(brand, 0);
        shell.Children.Add(brand);

        var navItems = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
        AddNavigationEntry(navItems, "⌂", "تقویم", () =>
        {
            ShowDesktopModule("تقویم");
            StatusText.Text = "نمای تقویم فعال است.";
        });
        AddNavigationEntry(navItems, "▣", "زروان دسک", () =>
        {
            ShowDesktopModule("زروان دسک");
            StatusText.Text = "بخش زروان دسک باز شد.";
        });
        AddNavigationEntry(navItems, "▦", "برنامه‌ها", () =>
        {
            ShowDesktopModule("برنامه‌ها");
            StatusText.Text = "بخش برنامه‌ها باز شد.";
        });
        AddNavigationEntry(navItems, "▦", "مناسبت‌ها", () =>
        {
            ShowDesktopModule("مناسبت‌ها");
            StatusText.Text = "بخش مناسبت‌ها باز شد.";
        });
        AddNavigationEntry(navItems, "♟", "یادآورها", () =>
        {
            ShowDesktopModule("یادآورها");
            StatusText.Text = "مرکز یادآورها باز شد.";
        });
        AddNavigationEntry(navItems, "▯", "همراه", () => ShowDesktopModule("همراه"));
        AddNavigationEntry(navItems, "◷", "فعالیت‌ها", () =>
        {
            ShowDesktopModule("فعالیت‌ها");
            StatusText.Text = "مرکز فعالیت باز شد.";
        });
        AddNavigationEntry(navItems, "⚿", "لایسنس و فعال‌سازی", () =>
        {
            ShowDesktopModule("لایسنس و فعال‌سازی");
            StatusText.Text = "مدیریت لایسنس زروان باز شد.";
        });

        var scroller = new ScrollViewer
        {
            Content = navItems,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroller, 1);
        shell.Children.Add(scroller);

        var settingsHost = new StackPanel { Spacing = 0 };
        AddNavigationEntry(settingsHost, "⚙", "تنظیمات", () =>
        {
            ShowDesktopModule("تنظیمات");
            StatusText.Text = "تنظیمات باز شد.";
        });
        Grid.SetRow(settingsHost, 2);
        shell.Children.Add(settingsHost);

        var border = new Border
        {
            Width = ZurvanDesignNavigationWidth,
            Background = BrushWithAlpha("#FFF7F0EB", 0xD8),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Child = shell,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        UpdateNavigationEntryLabels(true);
        return border;
    }

    private void AddNavigationEntry(Panel host, string icon, string label, Action action)
    {
        // Navigation is always available, including Zurvan Desk.
        // Desk credentials and the license service are independent. Never
        // redirect navigation to an obsolete license panel: a missing local
        // activation must not make tabs appear inert.
        var button = CreateNavigationRailButton(icon, label, action);
        _navigationEntries.Add((button, icon, label));
        host.Children.Add(button);
    }

    private string NavigationIconColor(string label) => label switch
    {
        "تقویم" => "#FF9E56EB",
        "زروان دسک" => "#FF2389F8",
        "برنامه‌ها" => "#FF248AEE",
        "مناسبت‌ها" => "#FFF06388",
        "یادآورها" => "#FFF4AC20",
        "همراه" => "#FF1BAFB7",
        "فعالیت‌ها" => "#FFA069E4",
        "لایسنس و فعال‌سازی" => "#FF309CF5",
        "تنظیمات" => "#FFB17BEB",
        _ => "#FF7A7484"
    };

    private void SelectNavigationEntry(string module)
    {
        foreach (var (button, _, label) in _navigationEntries)
        {
            var selected = label == module;
            button.Background = selected
                ? BrushWithAlpha("#FFECD7CC", 0xA8)
                : BrushWithAlpha("#00FFFFFF", 0);
            button.FontWeight = selected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private Button CreateNavigationRailButton(string icon, string label, Action action)
    {
        var button = new Button
        {
            Tag = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 54,
            Padding = new Thickness(16, 10, 16, 10),
            Background = BrushWithAlpha("#00FFFFFF", 0),
            Foreground = ThemeService.Brush("#FF211C26"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(15)
        };
        ToolTipService.SetToolTip(button, label);
        button.Click += (_, _) => { action(); SelectNavigationEntry(label); };
        return button;
    }

    private void SetNavigationExpanded(bool expanded)
    {
        // The Windows-settings-like sidebar is intentionally always readable.
        _navigationExpanded = true;
        UpdateNavigationEntryLabels(true);
        if (_navigationRailSurface is not null)
            _navigationRailSurface.Width = ZurvanDesignNavigationWidth;
        if (_navigationRailLeftColumn is not null)
            _navigationRailLeftColumn.Width = new GridLength(
                IsNavigationRailOnLeft() ? ZurvanDesignNavigationWidth : 0);
        if (_navigationRailRightColumn is not null)
            _navigationRailRightColumn.Width = new GridLength(
                IsNavigationRailOnLeft() ? 0 : ZurvanDesignNavigationWidth);
    }

    private void UpdateNavigationEntryLabels(bool expanded)
    {
        foreach (var entry in _navigationEntries)
        {
            var content = new Grid { ColumnSpacing = 16, FlowDirection = FlowDirection.RightToLeft };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var symbol = new TextBlock
            {
                Text = entry.Icon,
                FontSize = 29,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeService.Brush(NavigationIconColor(entry.Label))
            };
            Grid.SetColumn(symbol, 0);
            var label = new TextBlock
            {
                Text = entry.Label,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ThemeService.Brush("#FF25202B")
            };
            Grid.SetColumn(label, 1);
            content.Children.Add(symbol);
            content.Children.Add(label);
            entry.Button.Content = content;
            entry.Button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }
        SelectNavigationEntry(_activeDesktopModule);
    }

    // The approved Windows Settings-style mockup keeps the navigation on the left.
    private bool IsNavigationRailOnLeft() => true;

    private void ApplyWorkspaceLayoutSettingsLive()
    {
        var leftWidth = Math.Clamp(_settings.LeftSidebarWidth, 220, 560);
        var rightWidth = Math.Clamp(_settings.RightSidebarWidth, 220, 560);
        const double collapsedRailWidth = ZurvanDesignNavigationWidth;
        _settings.NavigationRailWidth = Math.Clamp(_settings.NavigationRailWidth, 150, 240);
        _settings.NavigationRailSide = IsNavigationRailOnLeft() ? "left" : "right";

        if (_physicalLeftSidebarColumn is not null)
            _physicalLeftSidebarColumn.Width = new GridLength(_activeDesktopModule == "تقویم" ? leftWidth : 0);
        if (_physicalRightSidebarColumn is not null) _physicalRightSidebarColumn.Width = new GridLength(0);
        if (_navigationRailLeftColumn is not null)
            _navigationRailLeftColumn.Width = new GridLength(IsNavigationRailOnLeft() ? collapsedRailWidth : 0);
        if (_navigationRailRightColumn is not null)
            _navigationRailRightColumn.Width = new GridLength(IsNavigationRailOnLeft() ? 0 : collapsedRailWidth);
        if (_navigationRailSurface is not null)
        {
            Grid.SetColumn(_navigationRailSurface, IsNavigationRailOnLeft() ? 0 : 4);
            _navigationRailSurface.Width = collapsedRailWidth;
            _navigationRailSurface.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        StartupDiagnostics.Log($"Workspace layout applied: left={leftWidth:0}; right={rightWidth:0}; rail={collapsedRailWidth:0}; railSide={_settings.NavigationRailSide}.");
    }

    private void OpenSettingsForNavigation(FrameworkElement? target)
    {
        _settingsPanel.Visibility = Visibility.Visible;
        DispatcherQueue.TryEnqueue(() => QueueBringIntoView(target ?? _settingsSurface));
    }

    private void HideSettingsForNavigation()
        => _settingsPanel.Visibility = Visibility.Collapsed;

    private void QueueBringIntoView(FrameworkElement? target)
    {
        if (target is null) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                target.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalAlignmentRatio = 0.0,
                    HorizontalAlignmentRatio = 0.5
                });
            }
            catch (Exception ex) { StartupDiagnostics.Log($"Navigation bring-into-view skipped: {ex.Message}"); }
        });
    }

    private async Task ShowCompanionOverviewAsync()
        => await ShowCompanionPairingAsync();
}
