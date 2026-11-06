using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private readonly List<(Button Button, string Icon, string Label)> _navigationEntries = new();
    private bool _navigationExpanded;

    private Border BuildNavigationRail()
    {
        var shell = new Grid
        {
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Thickness(6),
            RowSpacing = 8
        };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var brand = new StackPanel { Spacing = 2, Margin = new Thickness(2, 4, 2, 8) };
        brand.Children.Add(new TextBlock
        {
            Text = "◈",
            FontSize = 25,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = ThemeService.Brush(_theme.Accent)
        });
        brand.Children.Add(new TextBlock
        {
            Text = "زروان",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });
        Grid.SetRow(brand, 0);
        shell.Children.Add(brand);

        var navItems = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        AddNavigationEntry(navItems, "▦", "تقویم", () =>
        {
            HideSettingsForNavigation();
            QueueBringIntoView(_centerPanelSurface);
            StatusText.Text = "نمای تقویم فعال است.";
        });
        AddNavigationEntry(navItems, "▣", "زروان دسک", () =>
        {
            OpenSettingsForNavigation(_zurvanDeskSettingsElement);
            StatusText.Text = "بخش زروان دسک باز شد.";
        });
        AddNavigationEntry(navItems, "✓", "برنامه‌ها", () =>
        {
            HideSettingsForNavigation();
            QueueBringIntoView(_eventsSectionAnchor);
            StatusText.Text = "بخش برنامه‌ها باز شد.";
        });
        AddNavigationEntry(navItems, "✿", "مناسبت‌ها", () =>
        {
            HideSettingsForNavigation();
            QueueBringIntoView(_occasionsSectionAnchor);
            StatusText.Text = "بخش مناسبت‌ها باز شد.";
        });
        AddNavigationEntry(navItems, "♧", "یادآورها", () =>
        {
            HideSettingsForNavigation();
            QueueBringIntoView(_remindersSectionAnchor);
            StatusText.Text = "مرکز یادآورها باز شد.";
        });
        AddNavigationEntry(navItems, "▤", "همراه", () => _ = ShowCompanionOverviewAsync());
        AddNavigationEntry(navItems, "◷", "فعالیت‌ها", () =>
        {
            HideSettingsForNavigation();
            QueueBringIntoView(_activitySectionAnchor);
            StatusText.Text = "مرکز فعالیت باز شد.";
        });
        AddNavigationEntry(navItems, "♙", "لایسنس و فعال‌سازی", () =>
        {
            OpenSettingsForNavigation(_licensePanelElement);
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
            OpenSettingsForNavigation(_settingsSurface);
            StatusText.Text = "تنظیمات باز شد.";
        });
        Grid.SetRow(settingsHost, 2);
        shell.Children.Add(settingsHost);

        var border = new Border
        {
            Width = 64,
            Background = CreateSideSurfaceBrush(),
            BorderBrush = ThemeService.Brush(_theme.Accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CurrentUiCornerRadius),
            Child = shell,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        border.PointerEntered += (_, _) => SetNavigationExpanded(true);
        border.PointerExited += (_, _) => SetNavigationExpanded(false);
        UpdateNavigationEntryLabels(false);
        return border;
    }

    private void AddNavigationEntry(Panel host, string icon, string label, Action action)
    {
        var button = CreateNavigationRailButton(icon, label, () =>
        {
            if (label != "تقویم" && label != "لایسنس و فعال‌سازی" && !IsWindowsLicensed())
            {
                OpenSettingsForNavigation(_licensePanelElement);
                StatusText.Text = "برای استفاده از این ماژول ابتدا زروان را فعال کنید.";
                return;
            }
            action();
        });
        _navigationEntries.Add((button, icon, label));
        host.Children.Add(button);
    }

    private Button CreateNavigationRailButton(string icon, string label, Action action)
    {
        var button = new Button
        {
            Content = icon,
            Tag = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 0,
            MinHeight = 46,
            Padding = new Thickness(5, 8, 5, 8),
            Background = BrushWithAlpha(_theme.Accent, 0x35),
            Foreground = ThemeService.Brush(_theme.PrimaryText),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12)
        };
        ToolTipService.SetToolTip(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private void SetNavigationExpanded(bool expanded)
    {
        if (_navigationExpanded == expanded) return;
        _navigationExpanded = expanded;
        UpdateNavigationEntryLabels(expanded);
        var width = expanded ? Math.Clamp(_settings.NavigationRailWidth, 150, 240) : 64.0;
        if (_navigationRailSurface is not null)
        {
            _navigationRailSurface.Width = width;
            _navigationRailSurface.HorizontalAlignment = IsNavigationRailOnLeft() ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }
        var column = IsNavigationRailOnLeft()
            ? _navigationRailLeftColumn
            : _navigationRailRightColumn;
        if (column is not null) column.Width = new GridLength(width);
    }

    private void UpdateNavigationEntryLabels(bool expanded)
    {
        foreach (var entry in _navigationEntries)
        {
            entry.Button.Content = expanded ? $"{entry.Icon}   {entry.Label}" : entry.Icon;
            entry.Button.HorizontalContentAlignment = expanded
                ? HorizontalAlignment.Stretch
                : HorizontalAlignment.Center;
            entry.Button.Padding = expanded
                ? new Thickness(14, 8, 10, 8)
                : new Thickness(5, 8, 5, 8);
        }
    }

    private bool IsNavigationRailOnLeft()
        => !string.Equals(_settings.NavigationRailSide, "right", StringComparison.OrdinalIgnoreCase);

    private void ApplyWorkspaceLayoutSettingsLive()
    {
        var leftWidth = Math.Clamp(_settings.LeftSidebarWidth, 220, 560);
        var rightWidth = Math.Clamp(_settings.RightSidebarWidth, 220, 560);
        const double collapsedRailWidth = 64;
        _settings.NavigationRailWidth = Math.Clamp(_settings.NavigationRailWidth, 150, 240);
        _settings.NavigationRailSide = IsNavigationRailOnLeft() ? "left" : "right";

        if (_physicalLeftSidebarColumn is not null) _physicalLeftSidebarColumn.Width = new GridLength(leftWidth);
        if (_physicalRightSidebarColumn is not null) _physicalRightSidebarColumn.Width = new GridLength(rightWidth);
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
