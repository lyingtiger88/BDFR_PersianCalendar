using BDFR.PersianCalendar.Core;
using BDFR.PersianCalendar.Infrastructure;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using System.Diagnostics;

namespace BDFR.PersianCalendar.Desktop;

public sealed class MainWindow : Window
{
    private readonly ICalendarRepository _repository;
    private readonly PlannerService _planner;
    private readonly IOccasionSource _occasionSource;
    private readonly SpecialOccasionService _specialOccasions;
    private readonly PersianQuickAddParser _quickAdd = new();
    private readonly SettingsService _settingsService;
    private readonly ThemeService _themeService;
    private readonly PictureService _pictureService;
    private readonly AppSettings _settings;
    private readonly ThemeDefinition _theme;

    private readonly Image _backgroundImage = new();
    private readonly Border _backgroundWash = new();
    private readonly StackPanel _settingsPanel = new();
    private readonly CheckBox _seasonalBackgroundCheck = new();
    private readonly CheckBox _occasionPicturesCheck = new();
    private readonly TextBox _backgroundOpacityBox = new();
    private readonly TextBlock _backgroundPathText = new();
    private readonly StackPanel _pictureLibraryPanel = new();
    private readonly TextBlock _pictureLibrarySummary = new();

    private readonly TextBlock MonthTitle = new();
    private readonly TextBlock MonthLeftDecoration = new();
    private readonly TextBlock MonthRightDecoration = new();
    private readonly Grid CalendarGrid = new();
    private readonly TextBlock SelectedDateTitle = new();
    private readonly TextBlock GregorianDateText = new();
    private readonly StackPanel OccasionsPanel = new();
    private readonly StackPanel EventsPanel = new();
    private readonly StackPanel TasksPanel = new();
    private readonly TextBox NoteBox = new();
    private readonly TextBox EventTitleBox = new();
    private readonly TextBox EventTimeBox = new();
    private readonly TextBox ReminderMinutesBox = new();
    private readonly TextBox TaskTitleBox = new();
    private readonly TextBox TaskTimeBox = new();
    private readonly TextBox SpecialTitleBox = new();
    private readonly TextBox SpecialCalendarBox = new();
    private readonly TextBox SpecialMonthBox = new();
    private readonly TextBox SpecialDayBox = new();
    private readonly TextBox SpecialReminderDaysBox = new();
    private readonly TextBox QuickAddBox = new();
    private readonly TextBlock StatusText = new();
    private readonly TextBlock SyncProgress = new();
    private readonly StackPanel ActivityPanel = new();
    private readonly Dictionary<PersianDate, StackPanel> _calendarOccasionPanels = new();
    private readonly Dictionary<PersianDate, TextBlock> _calendarDayNumberLabels = new();
    private readonly HashSet<int> _yearSyncInFlight = new();
    private readonly HashSet<int> _yearSyncedThisSession = new();

    private PersianDate _selected = PersianDate.Today();
    private int _year;
    private int _month;

    public MainWindow(
        ICalendarRepository repository,
        PlannerService planner,
        IOccasionSource occasionSource,
        SpecialOccasionService specialOccasions)
    {
        StartupDiagnostics.Log("MainWindow: building programmatic UI.");

        _repository = repository;
        _planner = planner;
        _occasionSource = occasionSource;
        _specialOccasions = specialOccasions;

        _settingsService = new SettingsService();
        _themeService = new ThemeService();
        _settings = _settingsService.Load();
        _theme = _themeService.Load(_settings.ThemeId);
        _pictureService = new PictureService(_themeService);

        _year = _selected.Year;
        _month = _selected.Month;

        Title = "BDFR Persian Calendar";
        Content = BuildRoot();

        Activated += (_, _) => StartupDiagnostics.Log("MainWindow Activated event fired.");
        Closed += (_, _) => StartupDiagnostics.Log("MainWindow Closed event fired.");
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidVisibilityChange)
                StartupDiagnostics.Log($"MainWindow visibility changed: {AppWindow.IsVisible}");
        };

        try
        {
            AppWindow.Resize(new SizeInt32(1280, 820));
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Window resize skipped: {ex.Message}");
        }

        BuildCalendar();
        _ = LoadSelectedDayAsync();
        _ = LoadActivitiesAsync();
        _ = ApplyBackgroundAsync();

        StartupDiagnostics.Log("MainWindow: programmatic UI ready.");
    }

    private FrameworkElement BuildRoot()
    {
        var outer = new Grid
        {
            Background = ThemeService.Brush(_theme.WindowBackground)
        };

        _backgroundImage.Stretch = Stretch.UniformToFill;
        _backgroundImage.Opacity = Math.Clamp(_settings.BackgroundOpacity, 0, 0.85);
        outer.Children.Add(_backgroundImage);

        _backgroundWash.Background = ThemeService.Brush(_theme.WindowBackground);
        _backgroundWash.Opacity = 0.62;
        outer.Children.Add(_backgroundWash);

        var root = new Grid
        {
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(12),
            ColumnSpacing = 12
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });

        var left = BuildLeftPanel();
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        var center = new Border
        {
            Background = BrushWithAlpha(_theme.CardBackground, 0x58),
            CornerRadius = new CornerRadius(18),
            BorderBrush = BrushWithAlpha("#FFFFFFFF", 0xA8),
            BorderThickness = new Thickness(1),
            Child = BuildCalendarPanel()
        };
        Grid.SetColumn(center, 1);
        root.Children.Add(center);

        var right = BuildRightPanel();
        Grid.SetColumn(right, 2);
        root.Children.Add(right);

        outer.Children.Add(root);
        return outer;
    }

    private FrameworkElement BuildLeftPanel()
    {
        var stack = new StackPanel
        {
            Spacing = 12,
            Padding = new Thickness(20)
        };

        stack.Children.Add(new TextBlock
        {
            Text = "BDFR Persian Calendar",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "تقویم، برنامه‌ریز و یادآور فارسی ویندوز",
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        var today = MakeButton("امروز");
        today.Click += Today_Click;
        stack.Children.Add(today);

        var sync = MakeButton("همگام‌سازی مناسبت‌های time.ir");
        sync.Click += Sync_Click;
        stack.Children.Add(sync);

        SyncProgress.Text = "";
        SyncProgress.Opacity = 0.65;
        stack.Children.Add(SyncProgress);

        stack.Children.Add(SectionTitle("افزودن سریع"));

        QuickAddBox.PlaceholderText = "مثلاً: فردا ساعت 16:30 جلسه تیم";
        stack.Children.Add(QuickAddBox);

        var quick = MakeButton("افزودن");
        quick.Click += QuickAdd_Click;
        stack.Children.Add(quick);

        stack.Children.Add(new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 45, 125, 255)),
            Child = new TextBlock
            {
                Text = "اطلاعات و مناسبت‌ها در SQLite به‌صورت محلی نگهداری می‌شوند و برنامه بدون اینترنت هم قابل استفاده است.",
                TextWrapping = TextWrapping.Wrap
            }
        });

        StatusText.TextWrapping = TextWrapping.Wrap;
        StatusText.Opacity = 0.78;
        stack.Children.Add(StatusText);

        stack.Children.Add(SectionTitle("مرکز فعالیت"));

        var refresh = MakeButton("تازه‌سازی فعالیت‌ها");
        refresh.Click += RefreshActivities_Click;
        stack.Children.Add(refresh);

        ActivityPanel.Spacing = 6;
        stack.Children.Add(ActivityPanel);

        var settingsButton = MakeButton("⚙ تنظیمات");
        settingsButton.Click += (_, _) =>
            _settingsPanel.Visibility = _settingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        stack.Children.Add(settingsButton);
        stack.Children.Add(BuildSettingsPanel());

        return new Border
        {
            Background = ThemeService.Brush(_theme.PanelBackground),
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeService.Brush(_theme.Accent),
            Child = new ScrollViewer { Content = stack }
        };
    }

    private FrameworkElement BuildCalendarPanel()
    {
        var panel = new Grid { Padding = new Thickness(24) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var next = MakeButton("‹");
        next.FontSize = 22;
        next.Click += NextMonth_Click;
        Grid.SetColumn(next, 0);
        header.Children.Add(next);

        MonthTitle.FontSize = 28;
        MonthTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        MonthTitle.Foreground = ThemeService.Brush(_theme.PrimaryText);
        MonthTitle.HorizontalAlignment = HorizontalAlignment.Center;
        MonthTitle.VerticalAlignment = VerticalAlignment.Center;
        MonthTitle.FlowDirection = FlowDirection.RightToLeft;

        MonthLeftDecoration.FontSize = 22;
        MonthLeftDecoration.Opacity = 0.82;
        MonthLeftDecoration.VerticalAlignment = VerticalAlignment.Center;

        MonthRightDecoration.FontSize = 22;
        MonthRightDecoration.Opacity = 0.82;
        MonthRightDecoration.VerticalAlignment = VerticalAlignment.Center;

        var decoratedMonthTitle = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight
        };
        decoratedMonthTitle.Children.Add(MonthLeftDecoration);
        decoratedMonthTitle.Children.Add(MonthTitle);
        decoratedMonthTitle.Children.Add(MonthRightDecoration);

        Grid.SetColumn(decoratedMonthTitle, 1);
        header.Children.Add(decoratedMonthTitle);

        var prev = MakeButton("›");
        prev.FontSize = 22;
        prev.Click += PreviousMonth_Click;
        Grid.SetColumn(prev, 2);
        header.Children.Add(prev);

        Grid.SetRow(header, 0);
        panel.Children.Add(header);

        var weekdays = new Grid { Margin = new Thickness(0, 20, 0, 10) };
        for (var i = 0; i < 7; i++)
            weekdays.ColumnDefinitions.Add(new ColumnDefinition());

        var names = new[] { "شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه" };
        for (var i = 0; i < names.Length; i++)
        {
            var label = new Border
            {
                Margin = new Thickness(4),
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(12),
                Background = BrushWithAlpha(GetWeekdayColor(i), 0xB0),
                Child = new TextBlock
                {
                    Text = names[i],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = i == 6
                        ? ThemeService.Brush(_theme.HolidayText)
                        : ThemeService.Brush(_theme.PrimaryText),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
            Grid.SetColumn(label, i);
            weekdays.Children.Add(label);
        }

        Grid.SetRow(weekdays, 1);
        panel.Children.Add(weekdays);

        Grid.SetRow(CalendarGrid, 2);
        panel.Children.Add(CalendarGrid);

        return panel;
    }

    private FrameworkElement BuildRightPanel()
    {
        var stack = new StackPanel
        {
            Spacing = 10,
            Padding = new Thickness(20)
        };

        SelectedDateTitle.FontSize = 24;
        SelectedDateTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        SelectedDateTitle.Foreground = ThemeService.Brush(_theme.PrimaryText);
        stack.Children.Add(SelectedDateTitle);

        GregorianDateText.Opacity = 0.65;
        stack.Children.Add(GregorianDateText);

        stack.Children.Add(SectionTitle("مناسبت‌ها"));
        OccasionsPanel.Spacing = 6;
        stack.Children.Add(OccasionsPanel);

        stack.Children.Add(SectionTitle("رویدادها"));
        EventsPanel.Spacing = 6;
        stack.Children.Add(EventsPanel);

        stack.Children.Add(SectionTitle("کارها"));
        TasksPanel.Spacing = 6;
        stack.Children.Add(TasksPanel);

        stack.Children.Add(SectionTitle("یادداشت روز"));
        NoteBox.AcceptsReturn = true;
        NoteBox.TextWrapping = TextWrapping.Wrap;
        NoteBox.MinHeight = 100;
        stack.Children.Add(NoteBox);

        var saveNote = MakeButton("ذخیره یادداشت");
        saveNote.Click += SaveNote_Click;
        stack.Children.Add(saveNote);

        stack.Children.Add(SectionTitle("رویداد جدید"));

        EventTitleBox.PlaceholderText = "عنوان رویداد";
        stack.Children.Add(EventTitleBox);

        EventTimeBox.PlaceholderText = "زمان، مثل 14:30";
        stack.Children.Add(EventTimeBox);

        stack.Children.Add(new TextBlock { Text = "یادآوری چند دقیقه قبل؟", Opacity = 0.7 });
        ReminderMinutesBox.Text = "10";
        ReminderMinutesBox.PlaceholderText = "مثلاً 10";
        stack.Children.Add(ReminderMinutesBox);

        var addEvent = MakeButton("ثبت رویداد و یادآور");
        addEvent.Click += AddEvent_Click;
        stack.Children.Add(addEvent);

        stack.Children.Add(SectionTitle("کار جدید"));

        TaskTitleBox.PlaceholderText = "عنوان کار";
        stack.Children.Add(TaskTitleBox);

        TaskTimeBox.PlaceholderText = "موعد اختیاری، مثل 18:00";
        stack.Children.Add(TaskTimeBox);

        var addTask = MakeButton("ثبت کار");
        addTask.Click += AddTask_Click;
        stack.Children.Add(addTask);

        stack.Children.Add(SectionTitle("مناسبت شخصی سالانه"));

        SpecialTitleBox.PlaceholderText = "مثلاً تولد علی";
        stack.Children.Add(SpecialTitleBox);

        stack.Children.Add(new TextBlock { Text = "نوع تقویم", Opacity = 0.7 });
        SpecialCalendarBox.Text = "شمسی";
        SpecialCalendarBox.PlaceholderText = "شمسی / میلادی / قمری";
        stack.Children.Add(SpecialCalendarBox);

        var md = new Grid { ColumnSpacing = 8 };
        md.ColumnDefinitions.Add(new ColumnDefinition());
        md.ColumnDefinitions.Add(new ColumnDefinition());

        SpecialMonthBox.Text = "1";
        SpecialMonthBox.PlaceholderText = "ماه 1 تا 12";
        Grid.SetColumn(SpecialMonthBox, 0);
        md.Children.Add(SpecialMonthBox);

        SpecialDayBox.Text = "1";
        SpecialDayBox.PlaceholderText = "روز 1 تا 31";
        Grid.SetColumn(SpecialDayBox, 1);
        md.Children.Add(SpecialDayBox);

        stack.Children.Add(md);

        SpecialReminderDaysBox.Header = "روزهای یادآوری قبل از مناسبت";
        SpecialReminderDaysBox.Text = "7,1,0";
        SpecialReminderDaysBox.PlaceholderText = "مثلاً 30,7,1,0";
        stack.Children.Add(SpecialReminderDaysBox);

        var addSpecial = MakeButton("ثبت مناسبت و یادآورها");
        addSpecial.Click += AddSpecialOccasion_Click;
        stack.Children.Add(addSpecial);

        return new Border
        {
            Background = ThemeService.Brush(_theme.PanelBackground),
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeService.Brush(_theme.Accent),
            Child = new ScrollViewer { Content = stack }
        };
    }

    private TextBlock SectionTitle(string text)
        => new()
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText),
            Margin = new Thickness(0, 10, 0, 0)
        };

    private Button MakeButton(string text)
        => new()
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = ThemeService.Brush(_theme.Accent),
            Foreground = ThemeService.Brush(_theme.PrimaryText),
            BorderThickness = new Thickness(0)
        };

    private static SolidColorBrush BrushWithAlpha(string color, byte alpha)
    {
        var parsed = ThemeService.ParseColor(color);
        return new SolidColorBrush(
            Windows.UI.Color.FromArgb(alpha, parsed.R, parsed.G, parsed.B));
    }

    private string GetWeekdayColor(int column)
    {
        if (_theme.WeekdayColors is { Length: > 0 })
            return _theme.WeekdayColors[Math.Clamp(column, 0, _theme.WeekdayColors.Length - 1)];

        return _theme.CardBackground;
    }


    private FrameworkElement BuildSettingsPanel()
    {
        _settingsPanel.Spacing = 8;
        _settingsPanel.Padding = new Thickness(10);
        _settingsPanel.Visibility = Visibility.Collapsed;

        _settingsPanel.Children.Add(new Border
        {
            Background = ThemeService.Brush(_theme.WeekdayColors.ElementAtOrDefault(4) ?? _theme.Accent),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Child = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Zara Pastel",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = ThemeService.Brush(_theme.PrimaryText)
                    },
                    new TextBlock
                    {
                        Text = "تم اختصاصی پاستلی رنگارنگ",
                        FontSize = 12,
                        Opacity = 0.7,
                        Foreground = ThemeService.Brush(_theme.SecondaryText)
                    }
                }
            }
        });

        _seasonalBackgroundCheck.Content = "Elena Mode";
        _seasonalBackgroundCheck.IsChecked =
            string.Equals(ResolveBackgroundMode(), "elena", StringComparison.OrdinalIgnoreCase);
        _seasonalBackgroundCheck.Click += async (_, _) =>
        {
            var enabled = _seasonalBackgroundCheck.IsChecked == true;
            _settings.UseSeasonalBackground = enabled;
            _settings.BackgroundMode = enabled ? "elena" : "none";
            _settingsService.Save(_settings);
            await ApplyBackgroundAsync();
        };
        _settingsPanel.Children.Add(_seasonalBackgroundCheck);
        _settingsPanel.Children.Add(new TextBlock
        {
            Text = "پس‌زمینه فصلی خودکار؛ متناسب با ماه شمسی",
            FontSize = 11,
            Opacity = 0.65,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        var chooseBackground = MakeButton("انتخاب عکس زمینه دلخواه");
        chooseBackground.Click += SelectCustomBackground_Click;
        _settingsPanel.Children.Add(chooseBackground);

        var clearBackground = MakeButton("حذف عکس زمینه دلخواه");
        clearBackground.Click += async (_, _) =>
        {
            _settings.CustomBackgroundPath = null;
            _settings.BackgroundMode = "elena";
            _settings.UseSeasonalBackground = true;
            _seasonalBackgroundCheck.IsChecked = true;
            _settingsService.Save(_settings);
            await ApplyBackgroundAsync();
        };
        _settingsPanel.Children.Add(clearBackground);

        _backgroundPathText.TextWrapping = TextWrapping.Wrap;
        _backgroundPathText.FontSize = 11;
        _backgroundPathText.Opacity = 0.65;
        _settingsPanel.Children.Add(_backgroundPathText);

        _settingsPanel.Children.Add(new TextBlock
        {
            Text = "شدت عکس زمینه (۰ تا ۰٫۹۵)",
            FontSize = 12,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        _backgroundOpacityBox.Text = _settings.BackgroundOpacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        _backgroundOpacityBox.PlaceholderText = "مثلاً 0.18";
        _backgroundOpacityBox.LostFocus += async (_, _) =>
        {
            if (double.TryParse(
                    PersianQuickAddParser.NormalizeDigits(_backgroundOpacityBox.Text ?? ""),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var opacity))
            {
                _settings.BackgroundOpacity = Math.Clamp(opacity, 0, 0.95);
                _settingsService.Save(_settings);
                await ApplyBackgroundAsync();
            }
            else
            {
                _backgroundOpacityBox.Text = _settings.BackgroundOpacity.ToString(
                    "0.00",
                    System.Globalization.CultureInfo.InvariantCulture);
            }
        };
        _settingsPanel.Children.Add(_backgroundOpacityBox);

        _occasionPicturesCheck.Content = "نمایش عکس برای مناسبت‌ها";
        _occasionPicturesCheck.IsChecked = _settings.ShowOccasionPictures;
        _occasionPicturesCheck.Click += async (_, _) =>
        {
            _settings.ShowOccasionPictures = _occasionPicturesCheck.IsChecked == true;
            _settingsService.Save(_settings);
            await LoadSelectedDayAsync();
        };
        _settingsPanel.Children.Add(_occasionPicturesCheck);

        var pictureLibraryToggle = MakeButton("🖼 کتابخانه تصاویر");
        pictureLibraryToggle.Click += async (_, _) =>
        {
            _pictureLibraryPanel.Visibility =
                _pictureLibraryPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            if (_pictureLibraryPanel.Visibility == Visibility.Visible)
                await RefreshPictureLibraryAsync();
        };
        _settingsPanel.Children.Add(pictureLibraryToggle);
        _settingsPanel.Children.Add(BuildPictureLibraryPanel());

        var openPictures = MakeButton("📁 باز کردن پوشه picture در Explorer");
        openPictures.Click += (_, _) => OpenFolderInExplorer(_themeService.PictureRoot);
        _settingsPanel.Children.Add(openPictures);

        return new Border
        {
            Background = ThemeService.Brush(_theme.CardBackground),
            CornerRadius = new CornerRadius(14),
            BorderBrush = ThemeService.Brush(_theme.Accent),
            BorderThickness = new Thickness(1),
            Child = _settingsPanel
        };
    }

    private FrameworkElement BuildPictureLibraryPanel()
    {
        _pictureLibraryPanel.Spacing = 10;
        _pictureLibraryPanel.Padding = new Thickness(10);
        _pictureLibraryPanel.Visibility = Visibility.Collapsed;

        _pictureLibrarySummary.Text = "برای مشاهده تصاویر، کتابخانه را باز کنید.";
        _pictureLibrarySummary.TextWrapping = TextWrapping.Wrap;
        _pictureLibrarySummary.FontSize = 11;
        _pictureLibrarySummary.Foreground = ThemeService.Brush(_theme.SecondaryText);
        _pictureLibraryPanel.Children.Add(_pictureLibrarySummary);

        var refresh = MakeButton("↻ بازخوانی کتابخانه");
        refresh.Click += async (_, _) => await RefreshPictureLibraryAsync();
        _pictureLibraryPanel.Children.Add(refresh);

        return new Border
        {
            Background = BrushWithAlpha(_theme.CardBackground, 0x72),
            CornerRadius = new CornerRadius(14),
            BorderBrush = BrushWithAlpha("#FFFFFFFF", 0xA0),
            BorderThickness = new Thickness(1),
            Child = _pictureLibraryPanel
        };
    }

    private async Task RefreshPictureLibraryAsync()
    {
        try
        {
            _pictureLibraryPanel.Children.Clear();

            _pictureLibraryPanel.Children.Add(new TextBlock
            {
                Text = "📁 picture",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 15,
                Foreground = ThemeService.Brush(_theme.PrimaryText)
            });

            var refresh = MakeButton("↻ بازخوانی کتابخانه");
            refresh.Click += async (_, _) => await RefreshPictureLibraryAsync();
            _pictureLibraryPanel.Children.Add(refresh);

            var themeCount = await AddPictureFolderSectionAsync(
                "🎨 Themes",
                _themeService.ThemesRoot,
                SearchOption.AllDirectories);

            var seasonalCount = await AddPictureFolderSectionAsync(
                "🌦 Season Backgrounds",
                _themeService.SeasonalBackgroundsRoot,
                SearchOption.TopDirectoryOnly);

            var eventCount = await AddPictureFolderSectionAsync(
                "🖼 Event Pictures",
                _pictureService.EventPicturesRoot,
                SearchOption.TopDirectoryOnly);

            var userCount = await AddPictureFolderSectionAsync(
                "🌄 User Backgrounds",
                _settingsService.UserBackgroundRoot,
                SearchOption.TopDirectoryOnly);

            _pictureLibraryPanel.Children.Insert(1, new TextBlock
            {
                Text = $"مجموع تصاویر: {ToPersianDigits((themeCount + seasonalCount + eventCount + userCount).ToString())}",
                FontSize = 11,
                Opacity = 0.68,
                Foreground = ThemeService.Brush(_theme.SecondaryText)
            });
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Picture library refresh failed: {ex}");
            _pictureLibraryPanel.Children.Clear();
            _pictureLibraryPanel.Children.Add(new TextBlock
            {
                Text = $"خواندن کتابخانه تصاویر انجام نشد: {ex.Message}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeService.Brush(_theme.HolidayText)
            });
        }
    }

    private async Task<int> AddPictureFolderSectionAsync(
        string title,
        string folderPath,
        SearchOption searchOption)
    {
        Directory.CreateDirectory(folderPath);

        var extensions = new HashSet<string>(
            [".png", ".jpg", ".jpeg", ".webp", ".svg"],
            StringComparer.OrdinalIgnoreCase);

        var files = Directory
            .EnumerateFiles(folderPath, "*.*", searchOption)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = $"{title} · {ToPersianDigits(files.Length.ToString())}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        header.Children.Add(label);

        var open = new Button
        {
            Content = "باز کردن",
            Padding = new Thickness(9, 4, 9, 4),
            Background = BrushWithAlpha(_theme.Accent, 0xB8),
            Foreground = ThemeService.Brush(_theme.PrimaryText),
            BorderThickness = new Thickness(0)
        };
        open.Click += (_, _) => OpenFolderInExplorer(folderPath);
        Grid.SetColumn(open, 1);
        header.Children.Add(open);

        var section = new StackPanel { Spacing = 7 };
        section.Children.Add(header);
        section.Children.Add(new TextBlock
        {
            Text = folderPath,
            FontSize = 9.5,
            Opacity = 0.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        if (files.Length == 0)
        {
            section.Children.Add(new TextBlock
            {
                Text = "تصویری در این پوشه نیست.",
                FontSize = 11,
                Opacity = 0.55
            });
        }
        else
        {
            var previews = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7
            };

            foreach (var file in files.Take(12))
            {
                var source = await PictureService.LoadImageAsync(file);
                var image = new Image
                {
                    Source = source,
                    Width = 54,
                    Height = 54,
                    Stretch = Stretch.UniformToFill
                };

                var tile = new StackPanel
                {
                    Width = 66,
                    Spacing = 3
                };
                tile.Children.Add(new Border
                {
                    Width = 58,
                    Height = 58,
                    CornerRadius = new CornerRadius(10),
                    Background = BrushWithAlpha("#FFFFFFFF", 0x78),
                    BorderBrush = BrushWithAlpha("#FFFFFFFF", 0xC0),
                    BorderThickness = new Thickness(1),
                    Child = image
                });
                tile.Children.Add(new TextBlock
                {
                    Text = Path.GetFileNameWithoutExtension(file),
                    FontSize = 8.5,
                    MaxLines = 2,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Foreground = ThemeService.Brush(_theme.SecondaryText)
                });
                previews.Children.Add(tile);
            }

            section.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = previews
            });

            if (files.Length > 12)
            {
                section.Children.Add(new TextBlock
                {
                    Text = $"+ {ToPersianDigits((files.Length - 12).ToString())} تصویر دیگر",
                    FontSize = 10,
                    Opacity = 0.55
                });
            }
        }

        _pictureLibraryPanel.Children.Add(new Border
        {
            Background = BrushWithAlpha(_theme.PanelBackground, 0x78),
            CornerRadius = new CornerRadius(12),
            BorderBrush = BrushWithAlpha("#FFFFFFFF", 0x98),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9),
            Child = section
        });

        return files.Length;
    }

    private static void OpenFolderInExplorer(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Open picture folder failed ({path}): {ex}");
        }
    }

    private async void SelectCustomBackground_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".webp");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            var copied = await _settingsService.CopyCustomBackgroundAsync(file);
            if (copied is null)
            {
                StatusText.Text = "کپی تصویر زمینه انجام نشد.";
                return;
            }

            _settings.CustomBackgroundPath = copied;
            _settings.BackgroundMode = "custom";
            _settings.UseSeasonalBackground = false;
            _seasonalBackgroundCheck.IsChecked = false;

            // A custom photograph must be visibly applied on first selection.
            if (_settings.BackgroundOpacity < 0.45)
                _settings.BackgroundOpacity = 0.72;

            _backgroundOpacityBox.Text = _settings.BackgroundOpacity.ToString(
                "0.00",
                System.Globalization.CultureInfo.InvariantCulture);

            _settingsService.Save(_settings);
            await ApplyBackgroundAsync();

            if (_pictureLibraryPanel.Visibility == Visibility.Visible)
                await RefreshPictureLibraryAsync();

            StatusText.Text = "عکس زمینه دلخواه اعمال شد.";
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Custom background picker failed: {ex}");
            StatusText.Text = $"انتخاب عکس زمینه انجام نشد: {ex.Message}";
        }
    }

    private string ResolveBackgroundMode()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BackgroundMode))
            return _settings.BackgroundMode.Trim().ToLowerInvariant();

        // Migration for settings created before Elena/custom/none modes existed.
        if (!string.IsNullOrWhiteSpace(_settings.CustomBackgroundPath) &&
            File.Exists(_settings.CustomBackgroundPath))
        {
            _settings.BackgroundMode = "custom";
            if (_settings.BackgroundOpacity < 0.30)
                _settings.BackgroundOpacity = 0.72;
            _settingsService.Save(_settings);
            return "custom";
        }

        _settings.BackgroundMode = _settings.UseSeasonalBackground ? "elena" : "none";
        _settingsService.Save(_settings);
        return _settings.BackgroundMode;
    }

    private double GetDesktopAspectRatio()
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(
                AppWindow.Id,
                DisplayAreaFallback.Primary);

            var workArea = displayArea.WorkArea;
            if (workArea.Width > 0 && workArea.Height > 0)
            {
                var ratio = (double)workArea.Width / workArea.Height;
                StartupDiagnostics.Log(
                    $"Desktop work area: {workArea.Width}x{workArea.Height}, ratio={ratio:0.000}");
                return ratio;
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Desktop aspect ratio detection failed: {ex}");
        }

        return 16d / 9d;
    }

    private async Task ApplyBackgroundAsync()
    {
        try
        {
            string? path = null;
            var label = "بدون تصویر زمینه";
            var mode = ResolveBackgroundMode();
            var isCustom = false;

            if (mode == "custom" &&
                !string.IsNullOrWhiteSpace(_settings.CustomBackgroundPath) &&
                File.Exists(_settings.CustomBackgroundPath))
            {
                path = _settings.CustomBackgroundPath;
                isCustom = true;
                label = $"تصویر شخصی · {Path.GetFileName(path)}";
            }
            else if (mode == "elena")
            {
                var desktopAspectRatio = GetDesktopAspectRatio();
                path = _themeService.GetSeasonalBackgroundPath(_month, desktopAspectRatio);
                var season = _month switch
                {
                    <= 3 => "بهار",
                    <= 6 => "تابستان",
                    <= 9 => "پاییز",
                    _ => "زمستان"
                };
                var ratioLabel = ThemeService.FormatAspectRatioLabel(desktopAspectRatio);
                label = path is null
                    ? $"Elena Mode · {season} · {ratioLabel} · تصویری پیدا نشد"
                    : $"Elena Mode · {season} · {ratioLabel} · {Path.GetFileName(path)}";
            }

            var source = await PictureService.LoadImageAsync(path);

            // If a saved custom file disappears, gracefully fall back to Elena Mode.
            if (mode == "custom" && source is null)
            {
                _settings.BackgroundMode = "elena";
                _settings.UseSeasonalBackground = true;
                _seasonalBackgroundCheck.IsChecked = true;
                _settingsService.Save(_settings);

                var desktopAspectRatio = GetDesktopAspectRatio();
                path = _themeService.GetSeasonalBackgroundPath(_month, desktopAspectRatio);
                source = await PictureService.LoadImageAsync(path);
                var ratioLabel = ThemeService.FormatAspectRatioLabel(desktopAspectRatio);
                label = path is null
                    ? $"Elena Mode · بازگشت خودکار · {ratioLabel}"
                    : $"Elena Mode · بازگشت خودکار · {ratioLabel} · {Path.GetFileName(path)}";
                isCustom = false;
            }

            _backgroundImage.Source = source;
            _backgroundImage.Opacity = Math.Clamp(_settings.BackgroundOpacity, 0, 0.95);

            // Zara Pastel keeps Elena subtle, while a user photo is intentionally visible.
            _backgroundWash.Opacity = isCustom ? 0.16 : 0.58;
            _backgroundPathText.Text = label;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"ApplyBackgroundAsync failed: {ex}");
            _backgroundImage.Source = null;
            _backgroundPathText.Text = "بارگذاری تصویر زمینه انجام نشد.";
        }
    }

    private void UpdateMonthDecorations()
    {
        if (!string.Equals(_theme.Id, "zara-pastel", StringComparison.OrdinalIgnoreCase))
        {
            MonthLeftDecoration.Text = "";
            MonthRightDecoration.Text = "";
            return;
        }

        var pair = _month switch
        {
            1 => ("🌱", "🌸"),
            2 => ("🌷", "🌿"),
            3 => ("🌼", "🪻"),
            4 => ("☀️", "🌿"),
            5 => ("🌻", "☀️"),
            6 => ("🍉", "🌾"),
            7 => ("🍁", "🍂"),
            8 => ("🍂", "🌰"),
            9 => ("🌾", "🍁"),
            10 => ("❄️", "✦"),
            11 => ("☁️", "❄️"),
            12 => ("🌨️", "🌱"),
            _ => ("✦", "✦")
        };

        MonthLeftDecoration.Text = pair.Item1;
        MonthRightDecoration.Text = pair.Item2;
        MonthLeftDecoration.Foreground = ThemeService.Brush(_theme.SecondaryText);
        MonthRightDecoration.Foreground = ThemeService.Brush(_theme.SecondaryText);
    }

    private void BuildCalendar()
    {
        MonthTitle.Text = $"{PersianDate.MonthNames[_month - 1]} {_year}";
        UpdateMonthDecorations();
        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();
        _calendarOccasionPanels.Clear();
        _calendarDayNumberLabels.Clear();

        for (var i = 0; i < 7; i++)
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 6; i++)
            CalendarGrid.RowDefinitions.Add(new RowDefinition());

        var cells = MonthGridBuilder.Build(_year, _month);
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var dayNumber = new TextBlock
            {
                Text = ToPersianDigits(cell.Date.Day.ToString()),
                FontSize = 17,
                FontWeight = cell.IsToday
                    ? Microsoft.UI.Text.FontWeights.Bold
                    : Microsoft.UI.Text.FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = cell.Date.DayOfWeek == DayOfWeek.Friday
                    ? ThemeService.Brush(_theme.HolidayText)
                    : ThemeService.Brush(_theme.PrimaryText)
            };

            var occasionPanel = new StackPanel
            {
                Spacing = 2,
                Margin = new Thickness(2, 4, 2, 0)
            };

            var content = new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            content.Children.Add(dayNumber);
            content.Children.Add(occasionPanel);

            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4),
                MinHeight = 88,
                Padding = new Thickness(6),
                Opacity = cell.IsCurrentMonth ? 1 : 0.48,
                Background = cell.Date == _selected
                    ? BrushWithAlpha(_theme.SelectedDay, 0xC8)
                    : BrushWithAlpha(GetWeekdayColor(i % 7), 0x76),
                BorderBrush = cell.Date == _selected
                    ? BrushWithAlpha(_theme.PrimaryText, 0x9A)
                    : BrushWithAlpha("#FFFFFFFF", 0xB8),
                BorderThickness = cell.Date == _selected
                    ? new Thickness(1.6)
                    : new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Content = content
            };

            var captured = cell.Date;
            button.Click += async (_, _) =>
            {
                _selected = captured;
                BuildCalendar();
                await LoadSelectedDayAsync();
            };

            _calendarOccasionPanels[captured] = occasionPanel;
            _calendarDayNumberLabels[captured] = dayNumber;

            Grid.SetRow(button, i / 7);
            Grid.SetColumn(button, i % 7);
            CalendarGrid.Children.Add(button);
        }

        _ = LoadCalendarCellOccasionsAsync(_year, _month);
        _ = EnsureYearOccasionsAsync(_year);
        _ = ApplyBackgroundAsync();
    }

    private async Task LoadCalendarCellOccasionsAsync(int year, int month)
    {
        try
        {
            var days = PersianDate.DaysInMonth(year, month);

            for (var day = 1; day <= days; day++)
            {
                var date = new PersianDate(year, month, day);
                var snapshot = await _repository.GetDayAsync(date);

                if (year != _year || month != _month ||
                    !_calendarOccasionPanels.TryGetValue(date, out var panel))
                    return;

                panel.Children.Clear();

                var visible = snapshot.Occasions
                    .OrderByDescending(x => x.IsHoliday)
                    .ThenBy(x => x.Source == "personal" ? 0 : 1)
                    .ThenBy(x => x.Title)
                    .Take(3)
                    .ToArray();

                foreach (var occasion in visible)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = occasion.Title,
                        FontSize = 10.5,
                        TextWrapping = TextWrapping.Wrap,
                        MaxLines = 2,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Foreground = occasion.IsHoliday
                            ? ThemeService.Brush(_theme.HolidayText)
                            : null
                    });
                }

                if (snapshot.Occasions.Count > visible.Length)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"+{ToPersianDigits((snapshot.Occasions.Count - visible.Length).ToString())}",
                        FontSize = 10,
                        Opacity = 0.55,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                }

                if (snapshot.IsHoliday &&
                    _calendarDayNumberLabels.TryGetValue(date, out var dayLabel))
                {
                    dayLabel.Foreground = ThemeService.Brush(_theme.HolidayText);
                }
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"LoadCalendarCellOccasionsAsync failed: {ex}");
        }
    }

    private async Task EnsureYearOccasionsAsync(int year)
    {
        if (_yearSyncedThisSession.Contains(year) || !_yearSyncInFlight.Add(year))
            return;

        try
        {
            var last = await _repository.GetLastOccasionSyncAsync(_occasionSource.Name, year);
            var needsRefresh = last is null ||
                               DateTimeOffset.UtcNow - last.Value >= TimeSpan.FromDays(7);

            if (needsRefresh)
            {
                DispatcherQueue.TryEnqueue(() =>
                    SyncProgress.Text = $"در حال به‌روزرسانی مناسبت‌های {ToPersianDigits(year.ToString())} از time.ir...");

                var count = await _planner.SyncOccasionsAsync(_occasionSource, year);

                DispatcherQueue.TryEnqueue(() =>
                {
                    StatusText.Text = $"{ToPersianDigits(count.ToString())} مناسبت سال {ToPersianDigits(year.ToString())} از time.ir به‌روزرسانی شد.";
                    SyncProgress.Text = "";
                });
            }

            _yearSyncedThisSession.Add(year);

            if (year == _year)
                await LoadCalendarCellOccasionsAsync(_year, _month);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Automatic time.ir sync for {year} failed: {ex}");

            DispatcherQueue.TryEnqueue(() =>
            {
                SyncProgress.Text = "";
                StatusText.Text =
                    $"به‌روزرسانی time.ir انجام نشد؛ آخرین اطلاعات محلی حفظ شد. {ex.Message}";
            });
        }
        finally
        {
            _yearSyncInFlight.Remove(year);
        }
    }

    private async Task LoadSelectedDayAsync()
    {
        try
        {
            var snapshot = await _repository.GetDayAsync(_selected);
            SelectedDateTitle.Text = _selected.ToLongPersianString();
            GregorianDateText.Text = _selected.ToDateOnly().ToString("yyyy-MM-dd");
            NoteBox.Text = snapshot.Note?.Text ?? string.Empty;

            OccasionsPanel.Children.Clear();
            foreach (var item in snapshot.Occasions)
            {
                var text = new TextBlock
                {
                    Text = $"{(item.IsHoliday ? "● " : "• ")}{item.Title}",
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = item.IsHoliday
                        ? ThemeService.Brush(_theme.HolidayText)
                        : ThemeService.Brush(_theme.PrimaryText)
                };

                if (!_settings.ShowOccasionPictures)
                {
                    OccasionsPanel.Children.Add(text);
                    continue;
                }

                var row = new Grid
                {
                    ColumnSpacing = 8,
                    Padding = new Thickness(6)
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var image = new Image
                {
                    Width = 44,
                    Height = 44,
                    Stretch = Stretch.UniformToFill
                };

                var picturePath = _pictureService.ResolveOccasionPicture(item);
                image.Source = await PictureService.LoadImageAsync(picturePath);

                Grid.SetColumn(image, 0);
                row.Children.Add(image);
                Grid.SetColumn(text, 1);
                row.Children.Add(text);

                OccasionsPanel.Children.Add(new Border
                {
                    Background = ThemeService.Brush(_theme.CardBackground),
                    CornerRadius = new CornerRadius(10),
                    BorderBrush = item.IsHoliday
                        ? ThemeService.Brush(_theme.HolidayText)
                        : ThemeService.Brush(_theme.Accent),
                    BorderThickness = new Thickness(1),
                    Child = row
                });
            }
            if (snapshot.Occasions.Count == 0)
                OccasionsPanel.Children.Add(new TextBlock
                {
                    Text = "مناسبتی ثبت نشده",
                    Opacity = 0.55,
                    Foreground = ThemeService.Brush(_theme.SecondaryText)
                });

            EventsPanel.Children.Clear();
            foreach (var item in snapshot.Events)
            {
                EventsPanel.Children.Add(new TextBlock
                {
                    Text = $"{(item.StartTime is null ? "تمام‌روز" : item.StartTime.Value.ToString("HH:mm"))}  {item.Title}",
                    TextWrapping = TextWrapping.Wrap
                });
            }
            if (snapshot.Events.Count == 0)
                EventsPanel.Children.Add(new TextBlock { Text = "رویدادی ندارید", Opacity = 0.55 });

            TasksPanel.Children.Clear();
            foreach (var item in snapshot.Tasks)
            {
                var checkbox = new CheckBox
                {
                    Content = $"{(item.DueTime is null ? "" : item.DueTime.Value.ToString("HH:mm") + "  ")}{item.Title}",
                    IsChecked = item.Completed,
                    Tag = item.Id
                };

                checkbox.Click += async (sender, _) =>
                {
                    if (sender is CheckBox cb && cb.Tag is string id)
                    {
                        var completed = cb.IsChecked == true;
                        await _repository.SetTaskCompletedAsync(id, completed);
                        await _repository.AddActivityAsync(new ActivityLogEntry(
                            Guid.NewGuid().ToString("N"),
                            DateTimeOffset.UtcNow,
                            completed ? "task-completed" : "task-reopened",
                            CalendarItemKind.Task,
                            id,
                            item.Title));
                        await LoadSelectedDayAsync();
                        await LoadActivitiesAsync();
                    }
                };

                TasksPanel.Children.Add(checkbox);
            }
            if (snapshot.Tasks.Count == 0)
                TasksPanel.Children.Add(new TextBlock { Text = "کاری ندارید", Opacity = 0.55 });
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"LoadSelectedDayAsync failed: {ex}");
            StatusText.Text = ex.Message;
        }
    }

    private async void Today_Click(object sender, RoutedEventArgs e)
    {
        _selected = PersianDate.Today();
        _year = _selected.Year;
        _month = _selected.Month;
        BuildCalendar();
        await LoadSelectedDayAsync();
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        _month--;
        if (_month == 0) { _month = 12; _year--; }
        BuildCalendar();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _month++;
        if (_month == 13) { _month = 1; _year++; }
        BuildCalendar();
    }

    private async void SaveNote_Click(object sender, RoutedEventArgs e)
    {
        await _repository.UpsertNoteAsync(_selected, NoteBox.Text ?? string.Empty);
        await _repository.AddActivityAsync(new ActivityLogEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            "note-saved",
            CalendarItemKind.Note,
            null,
            $"یادداشت {_selected} ذخیره شد"));

        StatusText.Text = "یادداشت ذخیره شد.";
        await LoadSelectedDayAsync();
        await LoadActivitiesAsync();
    }

    private async void AddEvent_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EventTitleBox.Text) ||
            !TimeOnly.TryParse(EventTimeBox.Text, out var time))
        {
            StatusText.Text = "برای رویداد عنوان و زمان معتبر وارد کنید.";
            return;
        }

        var reminder = int.TryParse(
            PersianQuickAddParser.NormalizeDigits(ReminderMinutesBox.Text ?? "10"),
            out var parsedReminder)
            ? Math.Clamp(parsedReminder, 0, 10080)
            : 10;

        await _planner.AddEventAsync(
            EventTitleBox.Text,
            _selected,
            time,
            reminderMinutes: [reminder]);

        EventTitleBox.Text = "";
        EventTimeBox.Text = "";
        StatusText.Text = "رویداد و یادآور ثبت شد.";

        await LoadSelectedDayAsync();
        await LoadActivitiesAsync();
    }

    private async void AddTask_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TaskTitleBox.Text))
        {
            StatusText.Text = "عنوان کار را وارد کنید.";
            return;
        }

        TimeOnly? due = null;
        if (!string.IsNullOrWhiteSpace(TaskTimeBox.Text))
        {
            if (!TimeOnly.TryParse(TaskTimeBox.Text, out var parsed))
            {
                StatusText.Text = "فرمت زمان کار معتبر نیست.";
                return;
            }
            due = parsed;
        }

        await _planner.AddTaskAsync(TaskTitleBox.Text, _selected, due);
        TaskTitleBox.Text = "";
        TaskTimeBox.Text = "";
        StatusText.Text = "کار ثبت شد.";

        await LoadSelectedDayAsync();
        await LoadActivitiesAsync();
    }

    private async void QuickAdd_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var parsed = _quickAdd.Parse(QuickAddBox.Text);

            if (parsed.Time is not null)
            {
                await _planner.AddEventAsync(
                    parsed.Title,
                    parsed.Date,
                    parsed.Time.Value,
                    reminderMinutes: [parsed.ReminderMinutesBefore],
                    recurrence: parsed.Recurrence);
            }
            else
            {
                await _planner.AddTaskAsync(parsed.Title, parsed.Date);
            }

            _selected = parsed.Date;
            _year = _selected.Year;
            _month = _selected.Month;
            QuickAddBox.Text = "";

            BuildCalendar();
            await LoadSelectedDayAsync();
            StatusText.Text = "فعالیت با افزودن سریع ثبت شد.";
            await LoadActivitiesAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void AddSpecialOccasion_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SpecialTitleBox.Text))
        {
            StatusText.Text = "عنوان مناسبت را وارد کنید.";
            return;
        }

        if (!int.TryParse(PersianQuickAddParser.NormalizeDigits(SpecialMonthBox.Text ?? ""), out var month) ||
            month is < 1 or > 12 ||
            !int.TryParse(PersianQuickAddParser.NormalizeDigits(SpecialDayBox.Text ?? ""), out var day) ||
            day is < 1 or > 31)
        {
            StatusText.Text = "ماه یا روز مناسبت معتبر نیست.";
            return;
        }

        var reminderDays = (SpecialReminderDaysBox.Text ?? "7,1,0")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(PersianQuickAddParser.NormalizeDigits(x), out var n) ? n : -1)
            .Where(x => x >= 0)
            .Distinct()
            .ToArray();

        try
        {
            var calendarSystem = (SpecialCalendarBox.Text ?? "شمسی").Trim() switch
            {
                "میلادی" => CalendarSystemKind.Gregorian,
                "قمری" => CalendarSystemKind.Hijri,
                _ => CalendarSystemKind.Persian
            };

            await _specialOccasions.AddAnnualAsync(
                SpecialTitleBox.Text.Trim(),
                calendarSystem,
                month,
                day,
                reminderDays.Length == 0 ? [7, 1, 0] : reminderDays,
                new TimeOnly(9, 0));

            SpecialTitleBox.Text = "";
            StatusText.Text = "مناسبت شخصی و یادآورهای سالانه ثبت شد.";
            await LoadActivitiesAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        SyncProgress.Text = "در حال دریافت مناسبت‌ها...";
        StatusText.Text = "در حال دریافت مناسبت‌ها...";

        try
        {
            var count = await _planner.SyncOccasionsAsync(_occasionSource, _year);
            _yearSyncedThisSession.Add(_year);
            StatusText.Text = $"{ToPersianDigits(count.ToString())} مناسبت برای سال {ToPersianDigits(_year.ToString())} همگام شد.";

            await LoadSelectedDayAsync();
            await LoadCalendarCellOccasionsAsync(_year, _month);
            await LoadActivitiesAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"همگام‌سازی ناموفق بود؛ داده محلی حذف نشد. {ex.Message}";
        }
        finally
        {
            SyncProgress.Text = "";
        }
    }

    private async void RefreshActivities_Click(object sender, RoutedEventArgs e)
        => await LoadActivitiesAsync();

    private async Task LoadActivitiesAsync()
    {
        try
        {
            var items = await _repository.GetRecentActivitiesAsync(8);
            var specialOccasions = (await _repository.GetSpecialOccasionsAsync())
                .ToDictionary(x => x.Id, StringComparer.Ordinal);
            ActivityPanel.Children.Clear();

            if (items.Count == 0)
            {
                ActivityPanel.Children.Add(new TextBlock
                {
                    Text = "هنوز فعالیتی ثبت نشده است.",
                    Opacity = 0.55,
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var item in items)
            {
                var action = item.Action switch
                {
                    "event-created" => "رویداد",
                    "task-created" => "کار جدید",
                    "task-completed" => "کار انجام شد",
                    "task-reopened" => "کار باز شد",
                    "note-saved" => "یادداشت",
                    "occasion-sync" => "همگام‌سازی مناسبت‌ها",
                    "special-occasion-created" => "مناسبت شخصی",
                    "notification-fired" => "اعلان",
                    "reminder-completed" => "یادآور انجام شد",
                    "reminder-snoozed" => "یادآور به تعویق افتاد",
                    "reminder-dismissed" => "یادآور رد شد",
                    "occasion-sync-failed" => "خطای همگام‌سازی",
                    _ => item.Action
                };

                var localCreatedAt = item.CreatedAt.ToLocalTime();
                var activityPersianDate = PersianDate.FromDateOnly(
                    DateOnly.FromDateTime(localCreatedAt.DateTime));

                var message = item.Message;
                if (item.Action == "special-occasion-created" &&
                    item.ItemId is not null &&
                    specialOccasions.TryGetValue(item.ItemId, out var special))
                {
                    message = $"{special.Title} — {FormatSpecialOccasionDate(special)}";
                }

                ActivityPanel.Children.Add(new TextBlock
                {
                    Text = $"{action} · {FormatPersianActivityTimestamp(activityPersianDate, localCreatedAt.TimeOfDay)}\n{message}",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.82
                });
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"LoadActivitiesAsync failed: {ex}");
        }
    }

    public void NotifyExternalChange(string message)
    {
        StatusText.Text = message;
        _ = LoadSelectedDayAsync();
        _ = LoadActivitiesAsync();
    }
    private static string FormatSpecialOccasionDate(SpecialOccasion occasion)
        => occasion.CalendarSystem switch
        {
            CalendarSystemKind.Persian =>
                $"{ToPersianDigits(occasion.Day.ToString())} {PersianDate.MonthNames[occasion.Month - 1]} (سالانه)",
            CalendarSystemKind.Gregorian =>
                $"روز {ToPersianDigits(occasion.Day.ToString())} ماه {ToPersianDigits(occasion.Month.ToString())} میلادی (سالانه)",
            CalendarSystemKind.Hijri =>
                $"روز {ToPersianDigits(occasion.Day.ToString())} ماه {ToPersianDigits(occasion.Month.ToString())} قمری (سالانه)",
            _ => $"{occasion.Month}/{occasion.Day}"
        };

    private static string FormatPersianActivityTimestamp(PersianDate date, TimeSpan time)
        => $"{date.PersianDayOfWeek} {ToPersianDigits(date.Day.ToString())} {date.MonthName} {ToPersianDigits(date.Year.ToString())} · " +
           $"{ToPersianDigits(((int)time.TotalHours).ToString("00"))}:{ToPersianDigits(time.Minutes.ToString("00"))}";

    private static string ToPersianDigits(string value)
        => value
            .Replace('0', '۰')
            .Replace('1', '۱')
            .Replace('2', '۲')
            .Replace('3', '۳')
            .Replace('4', '۴')
            .Replace('5', '۵')
            .Replace('6', '۶')
            .Replace('7', '۷')
            .Replace('8', '۸')
            .Replace('9', '۹');

}
