using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private Border BuildNavigationRail()
    {
        var shell = new Grid
        {
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Thickness(8),
            RowSpacing = 8
        };

        shell.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(
            new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
        shell.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });

        var brand = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(4, 4, 4, 6)
        };
        brand.Children.Add(new TextBlock
        {
            Text = "زروان",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });
        brand.Children.Add(new TextBlock
        {
            Text = "Navigation",
            FontSize = 9.5,
            Opacity = 0.58,
            HorizontalAlignment = HorizontalAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });
        Grid.SetRow(brand, 0);
        shell.Children.Add(brand);

        var navItems = new StackPanel
        {
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        navItems.Children.Add(CreateNavigationRailButton(
            "📅 تقویم",
            () =>
            {
                HideSettingsForNavigation();
                QueueBringIntoView(_centerPanelSurface);
                StatusText.Text = "نمای تقویم فعال است.";
            }));

        navItems.Children.Add(CreateNavigationRailButton(
            "🗄 زروان دسک",
            () =>
            {
                OpenSettingsForNavigation(_zurvanDeskSettingsElement);
                StatusText.Text = "بخش Zurvan Desk باز شد.";
            }));

        navItems.Children.Add(CreateNavigationRailButton(
            "🗓 برنامه‌ها",
            () =>
            {
                HideSettingsForNavigation();
                QueueBringIntoView(_eventsSectionAnchor);
                StatusText.Text = "بخش برنامه‌ها و رویدادها باز شد.";
            }));

        navItems.Children.Add(CreateNavigationRailButton(
            "🎉 مناسبت‌ها",
            () =>
            {
                HideSettingsForNavigation();
                QueueBringIntoView(_occasionsSectionAnchor);
                StatusText.Text = "بخش مناسبت‌ها باز شد.";
            }));

        navItems.Children.Add(CreateNavigationRailButton(
            "🔔 یادآورها",
            () =>
            {
                HideSettingsForNavigation();
                QueueBringIntoView(_remindersSectionAnchor);
                StatusText.Text = "بخش یادآورها باز شد.";
            }));

        navItems.Children.Add(CreateNavigationRailButton(
            "🔗 همراه",
            () => _ = ShowCompanionOverviewAsync()));

        navItems.Children.Add(CreateNavigationRailButton(
            "🧭 فعالیت‌ها",
            () =>
            {
                HideSettingsForNavigation();
                QueueBringIntoView(_activitySectionAnchor);
                StatusText.Text = "مرکز فعالیت باز شد.";
            }));

        var scroller = new ScrollViewer
        {
            Content = navItems,
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility =
                ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroller, 1);
        shell.Children.Add(scroller);

        var settingsButton = CreateNavigationRailButton(
            "⚙ تنظیمات",
            () =>
            {
                OpenSettingsForNavigation(_settingsSurface);
                StatusText.Text = "تنظیمات باز شد.";
            });
        settingsButton.FontWeight =
            Microsoft.UI.Text.FontWeights.SemiBold;
        Grid.SetRow(settingsButton, 2);
        shell.Children.Add(settingsButton);

        return new Border
        {
            Background = CreateSideSurfaceBrush(),
            BorderBrush = ThemeService.Brush(_theme.Accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CurrentUiCornerRadius),
            Child = shell,
            FlowDirection = FlowDirection.RightToLeft
        };
    }

    private Button CreateNavigationRailButton(
        string text,
        Action action)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment =
                HorizontalAlignment.Center,
            MinHeight = 42,
            Padding = new Thickness(8, 7, 8, 7),
            Background = BrushWithAlpha(_theme.Accent, 0xB8),
            Foreground = ThemeService.Brush(_theme.PrimaryText),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(CurrentUiCornerRadius)
        };

        button.Click += (_, _) => action();
        return button;
    }

    private bool IsNavigationRailOnLeft()
        => !string.Equals(
            _settings.NavigationRailSide,
            "right",
            StringComparison.OrdinalIgnoreCase);

    private void ApplyWorkspaceLayoutSettingsLive()
    {
        var leftWidth =
            Math.Clamp(_settings.LeftSidebarWidth, 220, 560);
        var rightWidth =
            Math.Clamp(_settings.RightSidebarWidth, 220, 560);
        var railWidth =
            Math.Clamp(_settings.NavigationRailWidth, 108, 210);

        _settings.LeftSidebarWidth = leftWidth;
        _settings.RightSidebarWidth = rightWidth;
        _settings.NavigationRailWidth = railWidth;
        _settings.NavigationRailSide =
            IsNavigationRailOnLeft() ? "left" : "right";

        if (_physicalLeftSidebarColumn is not null)
        {
            _physicalLeftSidebarColumn.Width =
                new GridLength(leftWidth);
        }

        if (_physicalRightSidebarColumn is not null)
        {
            _physicalRightSidebarColumn.Width =
                new GridLength(rightWidth);
        }

        if (_navigationRailLeftColumn is not null)
        {
            _navigationRailLeftColumn.Width =
                new GridLength(
                    IsNavigationRailOnLeft()
                        ? railWidth
                        : 0);
        }

        if (_navigationRailRightColumn is not null)
        {
            _navigationRailRightColumn.Width =
                new GridLength(
                    IsNavigationRailOnLeft()
                        ? 0
                        : railWidth);
        }

        if (_navigationRailSurface is not null)
        {
            Grid.SetColumn(
                _navigationRailSurface,
                IsNavigationRailOnLeft() ? 0 : 4);

            _navigationRailSurface.Width = railWidth;
            _navigationRailSurface.HorizontalAlignment =
                HorizontalAlignment.Stretch;
        }

        StartupDiagnostics.Log(
            $"Workspace layout applied: left={leftWidth:0}; right={rightWidth:0}; rail={railWidth:0}; railSide={_settings.NavigationRailSide}.");
    }

    private void OpenSettingsForNavigation(
        FrameworkElement? target)
    {
        _settingsPanel.Visibility = Visibility.Visible;

        DispatcherQueue.TryEnqueue(() =>
        {
            QueueBringIntoView(target ?? _settingsSurface);
        });
    }

    private void HideSettingsForNavigation()
    {
        // Preserve the panel itself, only collapse its expanded content.
        _settingsPanel.Visibility = Visibility.Collapsed;
    }

    private void QueueBringIntoView(
        FrameworkElement? target)
    {
        if (target is null)
            return;

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                target.StartBringIntoView(
                    new BringIntoViewOptions
                    {
                        AnimationDesired = true,
                        VerticalAlignmentRatio = 0.0,
                        HorizontalAlignmentRatio = 0.5
                    });
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log(
                    $"Navigation bring-into-view skipped: {ex.Message}");
            }
        });
    }

    private async Task ShowCompanionOverviewAsync()
    {
        var content = new StackPanel
        {
            Spacing = 10,
            Width = 420
        };

        content.Children.Add(new TextBlock
        {
            Text = "Zurvan Companion",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        content.Children.Add(new TextBlock
        {
            Text = "جفت‌سازی Windows، Android و iOS با QR یا کد ۶ رقمی طراحی شده است. پروتکل ECDH/AES-GCM و مدل Sync مشترک فعال است؛ Transport زنده LAN و فهرست دستگاه‌های مورد اعتماد در مرحله توسعه بعدی قرار دارد.",
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new TextBlock
        {
            Text = "Zurvan Desk برای همگام‌سازی رمزگذاری‌شده ابری مستقل از Companion محلی باقی می‌ماند.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68
        });

        var dialog = new ContentDialog
        {
            Title = "همراه",
            Content = content,
            CloseButtonText = "بستن"
        };

        if (Content is FrameworkElement root &&
            root.XamlRoot is not null)
        {
            dialog.XamlRoot = root.XamlRoot;
        }

        await dialog.ShowAsync();
    }
}
