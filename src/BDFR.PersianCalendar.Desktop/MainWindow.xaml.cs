using BDFR.PersianCalendar.Core;
using BDFR.PersianCalendar.Infrastructure;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.CompilerServices;

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
    private ThemeDefinition _theme;

    private readonly Image _backgroundImage = new();
    private readonly Border _backgroundWash = new();
    private readonly StackPanel _settingsPanel = new();
    private readonly CheckBox _seasonalBackgroundCheck = new();
    private readonly CheckBox _occasionPicturesCheck = new();
    private readonly CheckBox _glassModeCheck = new();
    private readonly Slider _backgroundOpacitySlider = new();
    private readonly TextBlock _backgroundOpacityValue = new();
    private readonly ComboBox _fontFamilyCombo = new();
    private readonly NumberBox _fontSizeBox = new();
    private readonly ComboBox _fontWeightCombo = new();
    private readonly ComboBox _fontStyleCombo = new();
    private readonly TextBlock _fontPreview = new();
    private readonly ConditionalWeakTable<DependencyObject, TypographyBaseline> _typographyBaselines = new();
    private readonly TextBlock _backgroundPathText = new();
    private readonly StackPanel _pictureLibraryPanel = new();
    private readonly TextBlock _pictureLibrarySummary = new();
    private readonly StackPanel _accentSubmenuPanel = new();
    private readonly StackPanel _aboutUsPanel = new();

    private Grid? _rootSurface;
    private Border? _leftPanelSurface;
    private Border? _centerPanelSurface;
    private Border? _rightPanelSurface;
    private Border? _settingsSurface;
    private Border? _accentSubmenuSurface;
    private Border? _pictureLibrarySurface;
    private Border? _aboutUsSurface;

    private readonly TextBlock MonthTitle = new();
    private readonly TextBlock MonthLeftDecoration = new();
    private readonly TextBlock MonthRightDecoration = new();
    private readonly Grid CalendarGrid = new();
    private readonly List<Border> _weekdayHeaderBorders = new();
    private readonly List<TextBlock> _weekdayHeaderLabels = new();
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
    private readonly Dictionary<PersianDate, Border> _calendarMourningRibbons = new();
    private readonly HashSet<int> _yearSyncInFlight = new();
    private readonly HashSet<int> _yearSyncedThisSession = new();

    private PersianDate _selected = PersianDate.Today();
    private int _year;
    private int _month;
    private int _paletteEditorMonth;

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
        _settingsService.Save(_settings);

        _year = _selected.Year;
        _month = _selected.Month;

        _theme = _themeService.ResolveAppearance(_settings, _month);
        _pictureService = new PictureService(_themeService);

        Title = "Anahita";
        Content = BuildRoot();
        ApplyUserFont(Content);

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
            Background = ThemeService.Brush(_theme.WindowBackground),
            RequestedTheme = string.Equals(_theme.Id, "graphite-night", StringComparison.OrdinalIgnoreCase)
                ? ElementTheme.Dark
                : ElementTheme.Light
        };

        _rootSurface = outer;

        _backgroundImage.Stretch = Stretch.UniformToFill;
        _backgroundImage.Opacity = Math.Clamp(_settings.BackgroundOpacity, 0, 0.95);
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
            Background = CreateCenterSurfaceBrush(),
            CornerRadius = new CornerRadius(IsLiquidGlassActive() ? 24 : 18),
            BorderBrush = BrushWithAlpha("#FFFFFFFF", IsLiquidGlassActive() ? (byte)0xD0 : (byte)0xA8),
            BorderThickness = new Thickness(1),
            Child = BuildCalendarPanel()
        };
        _centerPanelSurface = center;
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
            Text = "Anahita",
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
            Background = BrushWithAlpha(_theme.Accent, 0x24),
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

        _leftPanelSurface = new Border
        {
            Background = CreateSideSurfaceBrush(),
            CornerRadius = new CornerRadius(IsLiquidGlassActive() ? 24 : 18),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeService.Brush(_theme.Accent),
            Child = new ScrollViewer { Content = stack }
        };
        return _leftPanelSurface;
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

        _weekdayHeaderBorders.Clear();
        _weekdayHeaderLabels.Clear();

        var names = new[] { "شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه" };
        for (var i = 0; i < names.Length; i++)
        {
            var text = new TextBlock
            {
                Text = names[i],
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = i == 6
                    ? ThemeService.Brush(_theme.HolidayText)
                    : ThemeService.Brush(_theme.PrimaryText),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };

            var label = new Border
            {
                Margin = new Thickness(4),
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(12),
                Background = CreateWeekdayHeaderBrush(GetWeekdayColor(i)),
                Child = text
            };

            _weekdayHeaderBorders.Add(label);
            _weekdayHeaderLabels.Add(text);

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

        _rightPanelSurface = new Border
        {
            Background = CreateSideSurfaceBrush(),
            CornerRadius = new CornerRadius(IsLiquidGlassActive() ? 24 : 18),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeService.Brush(_theme.Accent),
            Child = new ScrollViewer { Content = stack }
        };
        return _rightPanelSurface;
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

    private bool IsZaraPastelActive()
        => string.Equals(
               _settings.AppearanceMode,
               "theme",
               StringComparison.OrdinalIgnoreCase) &&
           string.Equals(
               _settings.ThemeId,
               "zara-pastel",
               StringComparison.OrdinalIgnoreCase);

    private bool IsLiquidGlassActive()
        => IsZaraPastelActive() && _settings.GlassMode;

    private Brush CreateLiquidGlassBrush(
        string tintHex,
        double tintOpacity = 0.20,
        byte fallbackAlpha = 0xA8)
    {
        var tint = ThemeService.ParseColor(tintHex);

        try
        {
            return new AcrylicBrush
            {
                TintColor = tint,
                TintOpacity = tintOpacity,
                FallbackColor = Windows.UI.Color.FromArgb(
                    fallbackAlpha,
                    tint.R,
                    tint.G,
                    tint.B)
            };
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Acrylic brush fallback used: {ex.Message}");
            return BrushWithAlpha(tintHex, fallbackAlpha);
        }
    }

    private Brush CreateCenterSurfaceBrush()
    {
        if (IsLiquidGlassActive())
            return CreateLiquidGlassBrush(_theme.CardBackground, 0.14, 0x9A);

        if (IsZaraPastelActive())
            return BrushWithAlpha(_theme.CardBackground, 0x44);

        return BrushWithAlpha(_theme.CardBackground, 0x58);
    }

    private Brush CreateSideSurfaceBrush()
    {
        if (IsLiquidGlassActive())
            return CreateLiquidGlassBrush(_theme.PanelBackground, 0.22, 0xB0);

        return ThemeService.Brush(_theme.PanelBackground);
    }

    private Brush CreateCalendarCellBrush(string color, bool selected)
    {
        if (IsLiquidGlassActive())
            return CreateLiquidGlassBrush(
                color,
                selected ? 0.30 : 0.16,
                selected ? (byte)0xB8 : (byte)0x78);

        if (IsZaraPastelActive())
            return BrushWithAlpha(
                color,
                selected ? (byte)0xA4 : (byte)0x58);

        return BrushWithAlpha(
            color,
            selected ? (byte)0xC8 : (byte)0x76);
    }

    private Brush CreateWeekdayHeaderBrush(string color)
    {
        if (IsLiquidGlassActive())
            return CreateLiquidGlassBrush(color, 0.18, 0x86);

        if (IsZaraPastelActive())
            return BrushWithAlpha(color, 0x78);

        return BrushWithAlpha(color, 0xB0);
    }

    private string GetWeekdayColor(int column)
    {
        if (_theme.WeekdayColors is { Length: > 0 })
            return _theme.WeekdayColors[Math.Clamp(column, 0, _theme.WeekdayColors.Length - 1)];

        return _theme.CardBackground;
    }


    private FrameworkElement BuildSettingsPanel()
    {
        _settingsPanel.Children.Clear();
        _settingsPanel.Spacing = 9;
        _settingsPanel.Padding = new Thickness(10);
        _settingsPanel.Visibility = Visibility.Collapsed;

        var isElena = string.Equals(
            _settings.AppearanceMode,
            "elena",
            StringComparison.OrdinalIgnoreCase);

        _settingsPanel.Children.Add(new TextBlock
        {
            Text = "حالت ظاهری",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        var elenaButton = MakeButton(
            isElena
                ? "✓ Elena Mode — پس‌زمینه فصلی خودکار"
                : "فعال‌سازی Elena Mode — پس‌زمینه فصلی خودکار");
        elenaButton.Click += async (_, _) => await SelectElenaModeAsync();
        _settingsPanel.Children.Add(elenaButton);

        var seasonName = GetCurrentSeasonName();
        var desktopAspect = ThemeService.FormatAspectRatioLabel(GetDesktopAspectRatio());
        var seasonalFile = _themeService.GetSeasonalBackgroundPath(
            _month,
            GetDesktopAspectRatio());

        _settingsPanel.Children.Add(new Border
        {
            Background = BrushWithAlpha(
                isElena ? _theme.Accent : _theme.CardBackground,
                isElena ? (byte)0x38 : (byte)0x60),
            CornerRadius = new CornerRadius(12),
            BorderBrush = BrushWithAlpha(_theme.Accent, 0x78),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = isElena
                            ? $"Elena Mode فعال است · فصل: {seasonName}"
                            : "Elena Mode غیرفعال است",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = ThemeService.Brush(_theme.PrimaryText)
                    },
                    new TextBlock
                    {
                        Text = $"نسبت نمایشگر: {desktopAspect}",
                        FontSize = 10.5,
                        Foreground = ThemeService.Brush(_theme.SecondaryText)
                    },
                    new TextBlock
                    {
                        Text = seasonalFile is null
                            ? "فایل فصل مناسب پیدا نشد."
                            : $"فایل فصل: {Path.GetFileName(seasonalFile)}",
                        FontSize = 10.5,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = ThemeService.Brush(_theme.SecondaryText)
                    }
                }
            }
        });

        _settingsPanel.Children.Add(new TextBlock
        {
            Text = "Elena Mode تم نیست. فقط تصویر فصل را از picture\\theme\\season backgrounds انتخاب و روی کل برنامه اعمال می‌کند. رنگ Accent هم جداگانه قابل انتخاب است.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        var openSeasonFolder = MakeButton("📁 باز کردن پوشه Season Backgrounds");
        openSeasonFolder.Click += (_, _) =>
            OpenFolderInExplorer(_themeService.SeasonalBackgroundsRoot);
        _settingsPanel.Children.Add(openSeasonFolder);

        if (isElena)
        {
            var accent = AppearanceCatalog.GetElenaAccent(_settings.ElenaAccentId, _month);
            var dayAccent = AppearanceCatalog.GetElenaDayColor(_settings.ElenaDayAccentId, _month);
            var accentToggle = MakeButton(
                $"Accent ▾  ·  کلی: {accent.DisplayName}  ·  روز: {dayAccent.DisplayName}");
            accentToggle.Click += (_, _) =>
                _accentSubmenuPanel.Visibility =
                    _accentSubmenuPanel.Visibility == Visibility.Visible
                        ? Visibility.Collapsed
                        : Visibility.Visible;
            _settingsPanel.Children.Add(accentToggle);
            _settingsPanel.Children.Add(BuildElenaAccentSubmenu());
        }

        _settingsPanel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 4),
            Background = BrushWithAlpha(_theme.SecondaryText, 0x30)
        });

        _settingsPanel.Children.Add(new TextBlock
        {
            Text = "Themes",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        foreach (var item in AppearanceCatalog.Themes)
        {
            var active = !isElena &&
                         string.Equals(
                             _settings.ThemeId,
                             item.Id,
                             StringComparison.OrdinalIgnoreCase);

            var button = MakeButton($"{(active ? "✓ " : "")}{item.DisplayName}");
            var capturedId = item.Id;
            button.Click += async (_, _) => await SelectThemeAsync(capturedId);
            _settingsPanel.Children.Add(button);

            _settingsPanel.Children.Add(new TextBlock
            {
                Text = item.Description,
                FontSize = 10.5,
                Margin = new Thickness(6, -4, 6, 2),
                Opacity = 0.60,
                Foreground = ThemeService.Brush(_theme.SecondaryText)
            });
        }

        _settingsPanel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 6, 0, 4),
            Background = BrushWithAlpha(_theme.SecondaryText, 0x30)
        });

        _settingsPanel.Children.Add(BuildUniversalColorPalettePanel());

        var isZaraPastel = !isElena &&
                          string.Equals(
                              _settings.ThemeId,
                              "zara-pastel",
                              StringComparison.OrdinalIgnoreCase);

        if (isZaraPastel)
        {
            _settingsPanel.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 4, 0, 4),
                Background = BrushWithAlpha(_theme.SecondaryText, 0x30)
            });

            _settingsPanel.Children.Add(new TextBlock
            {
                Text = "Zara Pastel",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ThemeService.Brush(_theme.PrimaryText)
            });

            _glassModeCheck.Content = "Glass Mode · Liquid Glass";
            _glassModeCheck.IsChecked = _settings.GlassMode;
            _glassModeCheck.Click -= GlassModeCheck_Click;
            _glassModeCheck.Click += GlassModeCheck_Click;
            _settingsPanel.Children.Add(_glassModeCheck);

            _settingsPanel.Children.Add(new TextBlock
            {
                Text = "شفافیت بیشتر جدول‌ها و پنل‌های Acrylic شیشه‌ای؛ برای دیده‌شدن بهتر تصویر زمینه.",
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.62,
                Foreground = ThemeService.Brush(_theme.SecondaryText)
            });
        }

        if (!isElena)
        {
            _settingsPanel.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 4, 0, 4),
                Background = BrushWithAlpha(_theme.SecondaryText, 0x30)
            });

            _settingsPanel.Children.Add(new TextBlock
            {
                Text = "پس‌زمینه در Theme Mode",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ThemeService.Brush(_theme.PrimaryText)
            });

            var chooseBackground = MakeButton("انتخاب عکس زمینه دلخواه");
            chooseBackground.Click += SelectCustomBackground_Click;
            _settingsPanel.Children.Add(chooseBackground);

            var clearBackground = MakeButton("حذف عکس زمینه دلخواه");
            clearBackground.Click += async (_, _) =>
            {
                _settings.CustomBackgroundPath = null;
                _settings.BackgroundMode = "none";
                _settings.UseSeasonalBackground = false;
                _settingsService.Save(_settings);
                await ApplyBackgroundAsync();
            };
            _settingsPanel.Children.Add(clearBackground);
        }

        _backgroundPathText.TextWrapping = TextWrapping.Wrap;
        _backgroundPathText.FontSize = 11;
        _backgroundPathText.Opacity = 0.65;
        _settingsPanel.Children.Add(_backgroundPathText);

        _settingsPanel.Children.Add(new TextBlock
        {
            Text = isElena ? "شدت تصویر فصلی" : "شدت عکس زمینه",
            FontSize = 12,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        var opacityRow = new Grid { ColumnSpacing = 8 };
        opacityRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        opacityRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });
        opacityRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        _backgroundOpacitySlider.Minimum = 0;
        _backgroundOpacitySlider.Maximum = 95;
        _backgroundOpacitySlider.StepFrequency = 1;
        _backgroundOpacitySlider.Value = Math.Round(
            Math.Clamp(_settings.BackgroundOpacity, 0, 0.95) * 100);
        _backgroundOpacitySlider.HorizontalAlignment = HorizontalAlignment.Stretch;
        _backgroundOpacitySlider.ValueChanged -= BackgroundOpacitySlider_ValueChanged;
        _backgroundOpacitySlider.ValueChanged += BackgroundOpacitySlider_ValueChanged;
        Grid.SetColumn(_backgroundOpacitySlider, 0);
        opacityRow.Children.Add(_backgroundOpacitySlider);

        _backgroundOpacityValue.Text =
            $"{Math.Round(_backgroundOpacitySlider.Value):0}%";
        _backgroundOpacityValue.MinWidth = 42;
        _backgroundOpacityValue.VerticalAlignment = VerticalAlignment.Center;
        _backgroundOpacityValue.HorizontalAlignment = HorizontalAlignment.Center;
        _backgroundOpacityValue.Foreground = ThemeService.Brush(_theme.PrimaryText);
        Grid.SetColumn(_backgroundOpacityValue, 1);
        opacityRow.Children.Add(_backgroundOpacityValue);

        var applyBackgroundOpacity = MakeButton("اعمال");
        applyBackgroundOpacity.Click += ApplyBackgroundOpacity_Click;
        Grid.SetColumn(applyBackgroundOpacity, 2);
        opacityRow.Children.Add(applyBackgroundOpacity);

        _settingsPanel.Children.Add(opacityRow);

        _settingsPanel.Children.Add(new TextBlock
        {
            Text = "مقدار را با اسلایدر تنظیم کنید و «اعمال» را بزنید؛ بدون تعویض تم، تصویر همان لحظه به‌روزرسانی می‌شود.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.62,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        _occasionPicturesCheck.Content = "نمایش عکس برای مناسبت‌ها";
        _occasionPicturesCheck.IsChecked = _settings.ShowOccasionPictures;
        _occasionPicturesCheck.Click -= OccasionPicturesCheck_Click;
        _occasionPicturesCheck.Click += OccasionPicturesCheck_Click;
        _settingsPanel.Children.Add(_occasionPicturesCheck);

        _settingsPanel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 6, 0, 4),
            Background = BrushWithAlpha(_theme.SecondaryText, 0x30)
        });

        _settingsPanel.Children.Add(new TextBlock
        {
            Text = "فونت و تایپوگرافی",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        _settingsPanel.Children.Add(new TextBlock
        {
            Text = "فونت‌های نصب‌شده ویندوز از کتابخانه فونت سیستم خوانده می‌شوند. اندازه، وزن و حالت نوشته نیز مستقل قابل تنظیم‌اند.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        PopulateFontLibrary();
        _fontFamilyCombo.Header = "Font Family";
        _fontFamilyCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
        _fontFamilyCombo.SelectionChanged -= FontTypographyPreview_Changed;
        _fontFamilyCombo.SelectionChanged += FontTypographyPreview_Changed;
        _settingsPanel.Children.Add(_fontFamilyCombo);

        var fontControls = new Grid { ColumnSpacing = 8 };
        fontControls.ColumnDefinitions.Add(new ColumnDefinition());
        fontControls.ColumnDefinitions.Add(new ColumnDefinition());

        _fontSizeBox.Header = "اندازه پایه";
        _fontSizeBox.Minimum = 10;
        _fontSizeBox.Maximum = 24;
        _fontSizeBox.SmallChange = 0.5;
        _fontSizeBox.Value = _settings.FontSize;
        _fontSizeBox.SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline;
        _fontSizeBox.ValueChanged -= FontSizeBox_ValueChanged;
        _fontSizeBox.ValueChanged += FontSizeBox_ValueChanged;
        Grid.SetColumn(_fontSizeBox, 0);
        fontControls.Children.Add(_fontSizeBox);

        _fontWeightCombo.Header = "وزن";
        _fontWeightCombo.ItemsSource = new[]
        {
            "Light",
            "Normal",
            "SemiBold",
            "Bold"
        };
        _fontWeightCombo.SelectedItem = FontWeightModeDisplayName(_settings.FontWeightMode);
        _fontWeightCombo.SelectionChanged -= FontTypographyPreview_Changed;
        _fontWeightCombo.SelectionChanged += FontTypographyPreview_Changed;
        Grid.SetColumn(_fontWeightCombo, 1);
        fontControls.Children.Add(_fontWeightCombo);

        _settingsPanel.Children.Add(fontControls);

        _fontStyleCombo.Header = "حالت نوشته";
        _fontStyleCombo.ItemsSource = new[] { "Normal", "Italic" };
        _fontStyleCombo.SelectedItem = string.Equals(
            _settings.FontStyleMode,
            "italic",
            StringComparison.OrdinalIgnoreCase)
            ? "Italic"
            : "Normal";
        _fontStyleCombo.SelectionChanged -= FontTypographyPreview_Changed;
        _fontStyleCombo.SelectionChanged += FontTypographyPreview_Changed;
        _settingsPanel.Children.Add(_fontStyleCombo);

        _fontPreview.Text =
            "آناهیتا · امروز یک روز تازه است — Anahita Calendar 1405";
        _fontPreview.TextWrapping = TextWrapping.Wrap;
        _fontPreview.Margin = new Thickness(2, 6, 2, 6);
        _fontPreview.Padding = new Thickness(10);
        _fontPreview.Foreground = ThemeService.Brush(_theme.PrimaryText);
        UpdateFontPreview();
        _settingsPanel.Children.Add(_fontPreview);

        var applyFont = MakeButton("اعمال تنظیمات فونت");
        applyFont.Click += ApplyFont_Click;
        _settingsPanel.Children.Add(applyFont);

        var refreshFonts = MakeButton("↻ بازخوانی کتابخانه فونت ویندوز");
        refreshFonts.Click += (_, _) =>
        {
            PopulateFontLibrary();
            UpdateFontPreview();
            StatusText.Text = "کتابخانه فونت‌های ویندوز دوباره خوانده شد.";
        };
        _settingsPanel.Children.Add(refreshFonts);

        var resetFont = MakeButton("بازگشت تایپوگرافی به حالت پیش‌فرض");
        resetFont.Click += ResetFont_Click;
        _settingsPanel.Children.Add(resetFont);

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

        var aboutToggle = MakeButton("ℹ About Us");
        aboutToggle.Click += (_, _) =>
            _aboutUsPanel.Visibility =
                _aboutUsPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        _settingsPanel.Children.Add(aboutToggle);
        _settingsPanel.Children.Add(BuildAboutUsPanel());

        _settingsSurface ??= new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1)
        };
        _settingsSurface.Background = IsLiquidGlassActive()
            ? CreateLiquidGlassBrush(_theme.CardBackground, 0.18, 0xA8)
            : BrushWithAlpha(_theme.CardBackground, 0xE0);
        _settingsSurface.BorderBrush = ThemeService.Brush(_theme.Accent);
        if (_settingsSurface.Child is null)
            _settingsSurface.Child = _settingsPanel;
        return _settingsSurface;
    }

    private FrameworkElement BuildUniversalColorPalettePanel()
    {
        var root = new StackPanel
        {
            Spacing = 8,
            Padding = new Thickness(8)
        };

        root.Children.Add(new TextBlock
        {
            Text = "🎨 پالت رنگ و Accent",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        root.Children.Add(new TextBlock
        {
            Text = "رنگ‌های این بخش روی ظاهر انتخاب‌شده override می‌شوند. هر Theme پالت مستقل خودش را دارد و Elena برای هر فصل یک پالت جدا نگه می‌دارد.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.64,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        var editorHost = new StackPanel { Spacing = 7 };

        if (string.Equals(
                _settings.AppearanceMode,
                "elena",
                StringComparison.OrdinalIgnoreCase))
        {
            _paletteEditorMonth = _paletteEditorMonth switch
            {
                >= 1 and <= 3 => 1,
                >= 4 and <= 6 => 4,
                >= 7 and <= 9 => 7,
                >= 10 and <= 12 => 10,
                _ => _month switch
                {
                    <= 3 => 1,
                    <= 6 => 4,
                    <= 9 => 7,
                    _ => 10
                }
            };

            var seasonPicker = new ComboBox
            {
                Header = "پالت فصل",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new[] { "بهار", "تابستان", "پاییز", "زمستان" },
                SelectedIndex = _paletteEditorMonth switch
                {
                    1 => 0,
                    4 => 1,
                    7 => 2,
                    _ => 3
                }
            };

            seasonPicker.SelectionChanged += (_, _) =>
            {
                _paletteEditorMonth = seasonPicker.SelectedIndex switch
                {
                    0 => 1,
                    1 => 4,
                    2 => 7,
                    _ => 10
                };

                BuildColorPaletteEditor(editorHost, _paletteEditorMonth);
            };

            root.Children.Add(seasonPicker);
        }
        else
        {
            _paletteEditorMonth = _month;
        }

        BuildColorPaletteEditor(editorHost, _paletteEditorMonth);
        root.Children.Add(editorHost);

        return new Border
        {
            Background = BrushWithAlpha(_theme.CardBackground, 0x84),
            BorderBrush = BrushWithAlpha(_theme.Accent, 0x74),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = root
        };
    }

    private void BuildColorPaletteEditor(
        StackPanel host,
        int paletteMonth)
    {
        host.Children.Clear();

        var paletteKey = ThemeService.GetColorPaletteKey(
            _settings,
            paletteMonth);

        var defaults = _themeService.ResolveBaseAppearance(
            _settings,
            paletteMonth);

        var effective = _themeService.ResolveAppearance(
            _settings,
            paletteMonth);

        var title = paletteKey switch
        {
            "elena-spring" => "Elena · بهار",
            "elena-summer" => "Elena · تابستان",
            "elena-autumn" => "Elena · پاییز",
            "elena-winter" => "Elena · زمستان",
            _ => defaults.DisplayName
        };

        host.Children.Add(new TextBlock
        {
            Text = $"پالت فعال: {title}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        host.Children.Add(CreateColorRoleEditor(
            "Accent کلی رابط",
            "accent",
            effective.Accent,
            defaults.Accent,
            paletteKey,
            paletteMonth));

        host.Children.Add(CreateColorRoleEditor(
            "روز انتخاب‌شده",
            "selected-day",
            effective.SelectedDay,
            defaults.SelectedDay,
            paletteKey,
            paletteMonth));

        host.Children.Add(CreateColorRoleEditor(
            "تعطیلات رسمی",
            "holiday",
            effective.HolidayText,
            defaults.HolidayText,
            paletteKey,
            paletteMonth));

        host.Children.Add(CreateColorRoleEditor(
            "پنل‌ها",
            "panel",
            effective.PanelBackground,
            defaults.PanelBackground,
            paletteKey,
            paletteMonth));

        host.Children.Add(CreateColorRoleEditor(
            "کارت‌ها",
            "card",
            effective.CardBackground,
            defaults.CardBackground,
            paletteKey,
            paletteMonth));

        host.Children.Add(CreateColorRoleEditor(
            "جدول و هدر روزهای تقویم",
            "calendar",
            effective.WeekdayColors.FirstOrDefault() ?? effective.CardBackground,
            defaults.WeekdayColors.FirstOrDefault() ?? defaults.CardBackground,
            paletteKey,
            paletteMonth));

        var resetAll = MakeButton("بازنشانی کل پالت این حالت");
        resetAll.Click += async (_, _) =>
        {
            _settings.ColorOverrides.Remove(paletteKey);
            _settingsService.Save(_settings);

            if (string.Equals(
                    ThemeService.GetColorPaletteKey(_settings, _month),
                    paletteKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                await ApplyAppearanceAsync();
            }
            else
            {
                BuildColorPaletteEditor(host, paletteMonth);
                StatusText.Text = $"پالت {title} به رنگ‌های پیش‌فرض بازگشت.";
            }
        };
        host.Children.Add(resetAll);
    }

    private FrameworkElement CreateColorRoleEditor(
        string label,
        string role,
        string effectiveColor,
        string defaultColor,
        string paletteKey,
        int paletteMonth)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        };
        Grid.SetColumn(labelBlock, 0);
        row.Children.Add(labelBlock);

        var colorButton = new Button
        {
            Content = ThemeService.NormalizeHexColor(effectiveColor),
            MinWidth = 118,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = ThemeService.Brush(effectiveColor),
            Foreground = ContrastBrush(effectiveColor),
            BorderBrush = BrushWithAlpha(_theme.PrimaryText, 0x48),
            BorderThickness = new Thickness(1)
        };
        Grid.SetColumn(colorButton, 1);
        row.Children.Add(colorButton);

        colorButton.Click += (_, _) =>
        {
            var picker = new ColorPicker
            {
                Color = ThemeService.ParseColor(effectiveColor),
                IsAlphaEnabled = true,
                IsColorSpectrumVisible = true,
                IsColorSliderVisible = true,
                IsColorPreviewVisible = true,
                MinWidth = 320
            };

            var flyoutStack = new StackPanel
            {
                Spacing = 8,
                Padding = new Thickness(8)
            };

            flyoutStack.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            flyoutStack.Children.Add(picker);

            var actions = new Grid { ColumnSpacing = 8 };
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition());

            var apply = MakeButton("اعمال");
            var reset = MakeButton("پیش‌فرض");

            Grid.SetColumn(apply, 0);
            Grid.SetColumn(reset, 1);
            actions.Children.Add(apply);
            actions.Children.Add(reset);
            flyoutStack.Children.Add(actions);

            var flyout = new Flyout { Content = flyoutStack };

            apply.Click += async (_, _) =>
            {
                SetColorOverride(
                    paletteKey,
                    role,
                    ThemeService.ColorToHex(picker.Color));

                _settingsService.Save(_settings);
                flyout.Hide();

                if (string.Equals(
                        ThemeService.GetColorPaletteKey(_settings, _month),
                        paletteKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await ApplyAppearanceAsync();
                }
                else
                {
                    StatusText.Text =
                        $"رنگ «{label}» برای {paletteKey} ذخیره شد.";
                }
            };

            reset.Click += async (_, _) =>
            {
                ClearColorOverride(paletteKey, role);
                _settingsService.Save(_settings);
                flyout.Hide();

                if (string.Equals(
                        ThemeService.GetColorPaletteKey(_settings, _month),
                        paletteKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await ApplyAppearanceAsync();
                }
                else
                {
                    StatusText.Text =
                        $"رنگ «{label}» برای {paletteKey} به پیش‌فرض برگشت.";
                }
            };

            flyout.ShowAt(colorButton);
        };

        ToolTipService.SetToolTip(
            colorButton,
            $"پیش‌فرض: {ThemeService.NormalizeHexColor(defaultColor)}");

        return row;
    }

    private void SetColorOverride(
        string paletteKey,
        string role,
        string color)
    {
        _settings.ColorOverrides ??=
            new Dictionary<string, AppearanceColorOverrides>();

        if (!_settings.ColorOverrides.TryGetValue(
                paletteKey,
                out var custom) ||
            custom is null)
        {
            custom = new AppearanceColorOverrides();
            _settings.ColorOverrides[paletteKey] = custom;
        }

        var normalized = ThemeService.NormalizeHexColor(color);

        switch (role)
        {
            case "accent":
                custom.Accent = normalized;
                break;
            case "selected-day":
                custom.SelectedDay = normalized;
                break;
            case "holiday":
                custom.Holiday = normalized;
                break;
            case "panel":
                custom.Panel = normalized;
                break;
            case "card":
                custom.Card = normalized;
                break;
            case "calendar":
                custom.Calendar = normalized;
                break;
        }
    }

    private void ClearColorOverride(
        string paletteKey,
        string role)
    {
        if (_settings.ColorOverrides is null ||
            !_settings.ColorOverrides.TryGetValue(
                paletteKey,
                out var custom) ||
            custom is null)
            return;

        switch (role)
        {
            case "accent":
                custom.Accent = null;
                break;
            case "selected-day":
                custom.SelectedDay = null;
                break;
            case "holiday":
                custom.Holiday = null;
                break;
            case "panel":
                custom.Panel = null;
                break;
            case "card":
                custom.Card = null;
                break;
            case "calendar":
                custom.Calendar = null;
                break;
        }

        if (custom.IsEmpty())
            _settings.ColorOverrides.Remove(paletteKey);
    }

    private static SolidColorBrush ContrastBrush(string color)
    {
        var parsed = ThemeService.ParseColor(color);
        var luminance =
            (0.299 * parsed.R + 0.587 * parsed.G + 0.114 * parsed.B) / 255.0;

        return new SolidColorBrush(
            luminance > 0.58
                ? Colors.Black
                : Colors.White);
    }

    private FrameworkElement BuildElenaAccentSubmenu()
    {
        _accentSubmenuPanel.Children.Clear();
        _accentSubmenuPanel.Spacing = 6;
        _accentSubmenuPanel.Padding = new Thickness(8);
        _accentSubmenuPanel.Visibility = Visibility.Collapsed;

        _accentSubmenuPanel.Children.Add(new TextBlock
        {
            Text = "Accent کلی تم",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        foreach (var accent in AppearanceCatalog.ElenaAccents)
        {
            var active = string.Equals(
                accent.Id,
                _settings.ElenaAccentId,
                StringComparison.OrdinalIgnoreCase);

            var previewColor = string.Equals(
                    accent.Id,
                    "seasonal",
                    StringComparison.OrdinalIgnoreCase)
                ? AppearanceCatalog.GetSeasonalAccent(_month).Accent
                : accent.Accent;

            var button = new Button
            {
                Content = $"{(active ? "✓ " : "")}{accent.DisplayName}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = ThemeService.Brush(previewColor),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };

            var capturedId = accent.Id;
            button.Click += async (_, _) => await SelectElenaAccentAsync(capturedId);
            _accentSubmenuPanel.Children.Add(button);
        }

        _accentSubmenuPanel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 5, 0, 5),
            Background = BrushWithAlpha(_theme.SecondaryText, 0x38)
        });

        _accentSubmenuPanel.Children.Add(new TextBlock
        {
            Text = "رنگ روز انتخاب‌شده",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        _accentSubmenuPanel.Children.Add(new TextBlock
        {
            Text = "این رنگ مستقل از Accent کلی است و فقط خانه روز انتخاب‌شده را مشخص می‌کند.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.62,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        foreach (var dayColor in AppearanceCatalog.ElenaDayColors)
        {
            var active = string.Equals(
                dayColor.Id,
                _settings.ElenaDayAccentId,
                StringComparison.OrdinalIgnoreCase);

            var previewColor = string.Equals(
                    dayColor.Id,
                    "seasonal",
                    StringComparison.OrdinalIgnoreCase)
                ? AppearanceCatalog.GetSeasonalDayColor(_month).Color
                : dayColor.Color;

            var button = new Button
            {
                Content = $"{(active ? "✓ " : "")}{dayColor.DisplayName}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = ThemeService.Brush(previewColor),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };

            var capturedId = dayColor.Id;
            button.Click += async (_, _) =>
                await SelectElenaDayAccentAsync(capturedId);
            _accentSubmenuPanel.Children.Add(button);
        }

        _accentSubmenuSurface ??= new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };
        _accentSubmenuSurface.Background = BrushWithAlpha("#FFFFFFFF", 0x62);
        _accentSubmenuSurface.BorderBrush = BrushWithAlpha(_theme.Accent, 0x80);
        if (_accentSubmenuSurface.Child is null)
            _accentSubmenuSurface.Child = _accentSubmenuPanel;
        return _accentSubmenuSurface;
    }

    private FrameworkElement BuildAboutUsPanel()
    {
        _aboutUsPanel.Children.Clear();
        _aboutUsPanel.Spacing = 10;
        _aboutUsPanel.Padding = new Thickness(12);
        _aboutUsPanel.Visibility = Visibility.Collapsed;

        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString()
                      ?? "development";

        _aboutUsPanel.Children.Add(new TextBlock
        {
            Text = "About",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        _aboutUsPanel.Children.Add(new TextBlock
        {
            Text = "Created by Behdad Badfar",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        _aboutUsPanel.Children.Add(new TextBlock
        {
            Text = "Made with love in Iran 🇮🇷",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        _aboutUsPanel.Children.Add(new TextBlock
        {
            Text = $"Anahita · Version {version}",
            FontSize = 11,
            Opacity = 0.62,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        AddAboutParagraph(
            "Every project begins with an idea, but bringing an idea to life takes time, patience, countless attempts, and someone who stays beside you through all of it.");

        AddAboutParagraph("This project is a reflection of that journey.");

        _aboutUsPanel.Children.Add(new TextBlock
        {
            Text = "Special Thanks",
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        _aboutUsPanel.Children.Add(new TextBlock
        {
            Text = "To my partner,sweet lovely elaa",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.Accent)
        });

        AddAboutParagraph(
            "Thank you for standing beside me through every idea, every challenge, every late night, and every moment when things didn't go as planned.");

        AddAboutParagraph(
            "Your patience, encouragement, and support have been an important part of this journey. Even when you weren't directly involved in the work, your presence made it easier to keep going.");

        AddAboutParagraph(
            "This project may carry my name as its creator, but a part of the journey behind it belongs to you too.");

        _aboutUsPanel.Children.Add(new TextBlock
        {
            Text = "Thank you for believing in me, supporting me, and being part of this journey. ❤️",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        _aboutUsPanel.Children.Add(new TextBlock
        {
            Text = "Built with passion.\nMade with love.\nFrom Iran, with ❤️",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        _aboutUsSurface ??= new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };
        _aboutUsSurface.Background = IsLiquidGlassActive()
            ? CreateLiquidGlassBrush(_theme.PanelBackground, 0.16, 0x98)
            : BrushWithAlpha(_theme.PanelBackground, 0xA0);
        _aboutUsSurface.BorderBrush = BrushWithAlpha(_theme.Accent, 0x88);
        if (_aboutUsSurface.Child is null)
            _aboutUsSurface.Child = _aboutUsPanel;
        return _aboutUsSurface;
    }

    private void AddAboutParagraph(string text)
    {
        _aboutUsPanel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 19,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });
    }

    private async Task SelectElenaModeAsync()
    {
        var snapshot = CaptureAppearanceSettings();

        try
        {
            StartupDiagnostics.Log("Appearance switch requested: Elena Mode.");

            _settings.AppearanceMode = "elena";
            _settings.BackgroundMode = "none";
            _settings.UseSeasonalBackground = true;

            if (_settings.BackgroundOpacity < 0.45)
                _settings.BackgroundOpacity = 0.62;

            _settingsService.Save(_settings);
            await ApplyAppearanceAsync();

            StartupDiagnostics.Log("Appearance switch completed: Elena Mode.");
        }
        catch (Exception ex)
        {
            await RecoverAppearanceAsync(snapshot, ex, "Elena Mode");
        }
    }

    private async Task SelectElenaAccentAsync(string accentId)
    {
        var snapshot = CaptureAppearanceSettings();

        try
        {
            StartupDiagnostics.Log($"Elena accent switch requested: {accentId}.");

            _settings.AppearanceMode = "elena";
            _settings.ElenaAccentId = accentId;
            _settings.UseSeasonalBackground = true;
            _settingsService.Save(_settings);

            await ApplyAppearanceAsync();

            StartupDiagnostics.Log($"Elena accent switch completed: {accentId}.");
        }
        catch (Exception ex)
        {
            await RecoverAppearanceAsync(snapshot, ex, $"Accent {accentId}");
        }
    }

    private async Task SelectElenaDayAccentAsync(string dayAccentId)
    {
        var snapshot = CaptureAppearanceSettings();

        try
        {
            StartupDiagnostics.Log(
                $"Elena selected-day color switch requested: {dayAccentId}.");

            _settings.AppearanceMode = "elena";
            _settings.ElenaDayAccentId = dayAccentId;
            _settings.UseSeasonalBackground = true;
            _settingsService.Save(_settings);

            await ApplyAppearanceAsync();

            StartupDiagnostics.Log(
                $"Elena selected-day color switch completed: {dayAccentId}.");
        }
        catch (Exception ex)
        {
            await RecoverAppearanceAsync(
                snapshot,
                ex,
                $"Selected day color {dayAccentId}");
        }
    }

    private async Task SelectThemeAsync(string themeId)
    {
        var snapshot = CaptureAppearanceSettings();

        try
        {
            StartupDiagnostics.Log($"Theme switch requested: {themeId}.");

            _settings.AppearanceMode = "theme";
            _settings.ThemeId = themeId;
            _settings.UseSeasonalBackground = false;

            if (string.Equals(_settings.BackgroundMode, "elena", StringComparison.OrdinalIgnoreCase))
                _settings.BackgroundMode = "none";

            _settingsService.Save(_settings);
            await ApplyAppearanceAsync();

            StartupDiagnostics.Log($"Theme switch completed: {themeId}.");
        }
        catch (Exception ex)
        {
            await RecoverAppearanceAsync(snapshot, ex, $"Theme {themeId}");
        }
    }

    private AppearanceSettingsSnapshot CaptureAppearanceSettings()
        => new(
            _settings.AppearanceMode,
            _settings.ThemeId,
            _settings.ElenaAccentId,
            _settings.ElenaDayAccentId,
            _settings.BackgroundMode,
            _settings.UseSeasonalBackground,
            _settings.BackgroundOpacity);

    private async Task RecoverAppearanceAsync(
        AppearanceSettingsSnapshot snapshot,
        Exception ex,
        string requestedAppearance)
    {
        StartupDiagnostics.Log(
            $"Appearance switch failed ({requestedAppearance}); rolling back: {ex}");

        _settings.AppearanceMode = snapshot.AppearanceMode;
        _settings.ThemeId = snapshot.ThemeId;
        _settings.ElenaAccentId = snapshot.ElenaAccentId;
        _settings.ElenaDayAccentId = snapshot.ElenaDayAccentId;
        _settings.BackgroundMode = snapshot.BackgroundMode;
        _settings.UseSeasonalBackground = snapshot.UseSeasonalBackground;
        _settings.BackgroundOpacity = snapshot.BackgroundOpacity;
        _settingsService.Save(_settings);

        try
        {
            _theme = _themeService.ResolveAppearance(_settings, _month);
            ApplyAppearanceBrushesOnly();
            BuildCalendar();
            await LoadSelectedDayAsync();
            await ApplyBackgroundAsync();

            StatusText.Text =
                $"اعمال {requestedAppearance} انجام نشد؛ ظاهر قبلی بازیابی شد.";
        }
        catch (Exception recoveryEx)
        {
            StartupDiagnostics.Log($"Appearance rollback UI refresh failed: {recoveryEx}");
            StatusText.Text =
                $"تعویض ظاهر انجام نشد. تنظیم قبلی حفظ شد. جزئیات در startup.log ثبت شد.";
        }
    }

    private sealed record AppearanceSettingsSnapshot(
        string AppearanceMode,
        string ThemeId,
        string ElenaAccentId,
        string ElenaDayAccentId,
        string? BackgroundMode,
        bool UseSeasonalBackground,
        double BackgroundOpacity);

    private async Task ApplyAppearanceAsync()
    {
        _theme = _themeService.ResolveAppearance(_settings, _month);
        ApplyAppearanceBrushesOnly();

        // Dynamic controls are rebuilt only after their containers are detached/cleared.
        // BuildPictureLibraryPanel now clears its own children before reusing persistent
        // UIElement instances.
        BuildSettingsPanel();
        BuildCalendar();

        await LoadSelectedDayAsync();
        await LoadActivitiesAsync();
        await ApplyBackgroundAsync();

        StatusText.Text = string.Equals(
            _settings.AppearanceMode,
            "elena",
            StringComparison.OrdinalIgnoreCase)
            ? $"Elena Mode فعال شد · Accent کلی: {AppearanceCatalog.GetElenaAccent(_settings.ElenaAccentId, _month).DisplayName} · رنگ روز: {AppearanceCatalog.GetElenaDayColor(_settings.ElenaDayAccentId, _month).DisplayName}"
            : $"Theme فعال شد: {_theme.DisplayName}";
    }

    private void ApplyAppearanceBrushesOnly()
    {
        if (_rootSurface is not null)
        {
            _rootSurface.Background = ThemeService.Brush(_theme.WindowBackground);
            _rootSurface.RequestedTheme =
                string.Equals(_theme.Id, "graphite-night", StringComparison.OrdinalIgnoreCase)
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
        }

        _backgroundWash.Background = ThemeService.Brush(_theme.WindowBackground);

        if (_leftPanelSurface is not null)
        {
            _leftPanelSurface.Background = CreateSideSurfaceBrush();
            _leftPanelSurface.CornerRadius = new CornerRadius(IsLiquidGlassActive() ? 24 : 18);
            _leftPanelSurface.BorderBrush = IsLiquidGlassActive()
                ? BrushWithAlpha("#FFFFFFFF", 0xC8)
                : ThemeService.Brush(_theme.Accent);
        }

        if (_centerPanelSurface is not null)
        {
            _centerPanelSurface.Background = CreateCenterSurfaceBrush();
            _centerPanelSurface.CornerRadius = new CornerRadius(IsLiquidGlassActive() ? 24 : 18);
            _centerPanelSurface.BorderBrush = BrushWithAlpha(
                "#FFFFFFFF",
                IsLiquidGlassActive() ? (byte)0xD0 : (byte)0xA8);
        }

        if (_rightPanelSurface is not null)
        {
            _rightPanelSurface.Background = CreateSideSurfaceBrush();
            _rightPanelSurface.CornerRadius = new CornerRadius(IsLiquidGlassActive() ? 24 : 18);
            _rightPanelSurface.BorderBrush = IsLiquidGlassActive()
                ? BrushWithAlpha("#FFFFFFFF", 0xC8)
                : ThemeService.Brush(_theme.Accent);
        }

        if (_settingsSurface is not null)
        {
            _settingsSurface.Background = IsLiquidGlassActive()
                ? CreateLiquidGlassBrush(_theme.CardBackground, 0.18, 0xA8)
                : BrushWithAlpha(_theme.CardBackground, 0xE0);
            _settingsSurface.BorderBrush = ThemeService.Brush(_theme.Accent);
        }

        if (_pictureLibrarySurface is not null)
        {
            _pictureLibrarySurface.Background = IsLiquidGlassActive()
                ? CreateLiquidGlassBrush(_theme.CardBackground, 0.14, 0x90)
                : BrushWithAlpha(_theme.CardBackground, 0x72);
            _pictureLibrarySurface.BorderBrush = BrushWithAlpha("#FFFFFFFF", 0xA0);
        }

        if (_aboutUsSurface is not null)
        {
            _aboutUsSurface.Background = IsLiquidGlassActive()
                ? CreateLiquidGlassBrush(_theme.PanelBackground, 0.16, 0x98)
                : BrushWithAlpha(_theme.PanelBackground, 0xA0);
            _aboutUsSurface.BorderBrush = BrushWithAlpha(_theme.Accent, 0x88);
        }

        if (_accentSubmenuSurface is not null)
            _accentSubmenuSurface.BorderBrush = BrushWithAlpha(_theme.Accent, 0x80);

        MonthTitle.Foreground = ThemeService.Brush(_theme.PrimaryText);
        SelectedDateTitle.Foreground = ThemeService.Brush(_theme.PrimaryText);

        ApplyGenericTheme(Content);
        UpdateWeekdayHeaderAppearance();
    }

    private void UpdateWeekdayHeaderAppearance()
    {
        for (var i = 0; i < _weekdayHeaderBorders.Count; i++)
        {
            _weekdayHeaderBorders[i].Background =
                CreateWeekdayHeaderBrush(GetWeekdayColor(i));

            _weekdayHeaderLabels[i].Foreground = i == 6
                ? ThemeService.Brush(_theme.HolidayText)
                : ThemeService.Brush(_theme.PrimaryText);
        }
    }

    private void ApplyGenericTheme(DependencyObject? root)
    {
        if (root is null)
            return;

        try
        {
            var userFont = ResolveUserFont();

            if (root is Control fontControl)
                fontControl.FontFamily = userFont;
            if (root is TextBlock fontText)
                fontText.FontFamily = userFont;

            if (root is Button button)
            {
                button.Background = ThemeService.Brush(_theme.Accent);
                button.Foreground = ThemeService.Brush(_theme.PrimaryText);
            }
            else if (root is TextBlock text)
            {
                text.Foreground = ThemeService.Brush(_theme.PrimaryText);
            }
            else if (root is TextBox textBox)
            {
                textBox.Background = BrushWithAlpha(_theme.CardBackground, 0xE8);
                textBox.Foreground = ThemeService.Brush(_theme.PrimaryText);
                textBox.BorderBrush = BrushWithAlpha(_theme.Accent, 0xB8);
            }
            else if (root is CheckBox checkBox)
            {
                checkBox.Foreground = ThemeService.Brush(_theme.PrimaryText);
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
                ApplyGenericTheme(VisualTreeHelper.GetChild(root, i));
        }
        catch (Exception ex)
        {
            // Theme changes must never terminate the application because a transient
            // WinUI template element disappeared while walking the visual tree.
            StartupDiagnostics.Log(
                $"Theme traversal skipped transient element {root.GetType().Name}: {ex.Message}");
        }
    }

    private sealed record TypographyBaseline(
        double FontSize,
        Windows.UI.Text.FontWeight FontWeight,
        Windows.UI.Text.FontStyle FontStyle);

    private static string FontWeightModeDisplayName(string? value)
        => (value ?? "normal").Trim().ToLowerInvariant() switch
        {
            "light" => "Light",
            "semibold" => "SemiBold",
            "bold" => "Bold",
            _ => "Normal"
        };

    private static string NormalizeFontWeightMode(string? value)
        => (value ?? "normal").Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "semibold" => "semibold",
            "bold" => "bold",
            _ => "normal"
        };

    private static string NormalizeFontStyleMode(string? value)
        => string.Equals(
                value?.Trim(),
                "italic",
                StringComparison.OrdinalIgnoreCase)
            ? "italic"
            : "normal";

    private static string NormalizeRegistryFontName(string valueName)
    {
        var name = valueName.Trim();

        var paren = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (paren > 0)
            name = name[..paren].Trim();

        string[] suffixes =
        [
            " Bold Italic",
            " Bold Oblique",
            " SemiBold Italic",
            " Semibold Italic",
            " SemiBold",
            " Semibold",
            " ExtraBold",
            " ExtraLight",
            " UltraLight",
            " Medium",
            " Regular",
            " Light",
            " Bold",
            " Italic",
            " Oblique",
            " Black",
            " Thin"
        ];

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var suffix in suffixes)
            {
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                name = name[..^suffix.Length].Trim();
                changed = true;
                break;
            }
        }

        return name;
    }

    private static IReadOnlyList<string> GetInstalledFontFamilies()
    {
        var fonts = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase)
        {
            "Segoe UI Variable",
            "Segoe UI",
            "Tahoma",
            "Arial"
        };

        static void ReadRegistryFonts(
            SortedSet<string> target,
            RegistryKey root,
            string path)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key is null)
                    return;

                foreach (var valueName in key.GetValueNames())
                {
                    var family = NormalizeRegistryFontName(valueName);
                    if (!string.IsNullOrWhiteSpace(family))
                        target.Add(family);
                }
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log(
                    $"Windows font library registry read skipped ({path}): {ex.Message}");
            }
        }

        const string machineFonts =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
        const string userFonts =
            @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

        ReadRegistryFonts(fonts, Registry.LocalMachine, machineFonts);
        ReadRegistryFonts(fonts, Registry.CurrentUser, userFonts);

        return fonts.ToArray();
    }

    private void PopulateFontLibrary()
    {
        var current = string.IsNullOrWhiteSpace(_settings.FontFamilyName)
            ? "Segoe UI Variable"
            : _settings.FontFamilyName.Trim();

        var fonts = GetInstalledFontFamilies().ToList();
        if (!fonts.Contains(current, StringComparer.CurrentCultureIgnoreCase))
        {
            fonts.Add(current);
            fonts = fonts
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        _fontFamilyCombo.ItemsSource = fonts;
        _fontFamilyCombo.SelectedItem =
            fonts.FirstOrDefault(x =>
                string.Equals(
                    x,
                    current,
                    StringComparison.CurrentCultureIgnoreCase))
            ?? fonts.FirstOrDefault();
    }

    private FontFamily ResolveUserFont(string? familyName = null)
    {
        var requested = string.IsNullOrWhiteSpace(familyName)
            ? string.IsNullOrWhiteSpace(_settings.FontFamilyName)
                ? "Segoe UI Variable"
                : _settings.FontFamilyName.Trim()
            : familyName.Trim();

        try
        {
            return new FontFamily(requested);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log(
                $"Font family '{requested}' could not be created; default used: {ex.Message}");
            return new FontFamily("Segoe UI Variable");
        }
    }

    private static Windows.UI.Text.FontWeight ResolveUserFontWeight(
        string? mode,
        Windows.UI.Text.FontWeight baseline)
        => NormalizeFontWeightMode(mode) switch
        {
            "light" => Microsoft.UI.Text.FontWeights.Light,
            "semibold" => Microsoft.UI.Text.FontWeights.SemiBold,
            "bold" => Microsoft.UI.Text.FontWeights.Bold,
            _ => baseline
        };

    private static Windows.UI.Text.FontStyle ResolveUserFontStyle(
        string? mode,
        Windows.UI.Text.FontStyle baseline)
        => NormalizeFontStyleMode(mode) == "italic"
            ? Windows.UI.Text.FontStyle.Italic
            : baseline;

    private TypographyBaseline GetTypographyBaseline(
        DependencyObject element,
        double fontSize,
        Windows.UI.Text.FontWeight fontWeight,
        Windows.UI.Text.FontStyle fontStyle)
    {
        if (_typographyBaselines.TryGetValue(element, out var baseline))
            return baseline;

        baseline = new TypographyBaseline(
            fontSize > 0 ? fontSize : 14.0,
            fontWeight,
            fontStyle);

        _typographyBaselines.Add(element, baseline);
        return baseline;
    }

    private void ApplyUserFont(DependencyObject? root)
    {
        if (root is null)
            return;

        try
        {
            var font = ResolveUserFont();
            var sizeScale = Math.Clamp(_settings.FontSize, 10.0, 24.0) / 14.0;

            if (root is Control control)
            {
                var baseline = GetTypographyBaseline(
                    control,
                    control.FontSize,
                    control.FontWeight,
                    control.FontStyle);

                control.FontFamily = font;
                control.FontSize = Math.Clamp(
                    baseline.FontSize * sizeScale,
                    8.0,
                    52.0);
                control.FontWeight = ResolveUserFontWeight(
                    _settings.FontWeightMode,
                    baseline.FontWeight);
                control.FontStyle = ResolveUserFontStyle(
                    _settings.FontStyleMode,
                    baseline.FontStyle);
            }

            if (root is TextBlock textBlock)
            {
                var baseline = GetTypographyBaseline(
                    textBlock,
                    textBlock.FontSize,
                    textBlock.FontWeight,
                    textBlock.FontStyle);

                textBlock.FontFamily = font;
                textBlock.FontSize = Math.Clamp(
                    baseline.FontSize * sizeScale,
                    8.0,
                    52.0);
                textBlock.FontWeight = ResolveUserFontWeight(
                    _settings.FontWeightMode,
                    baseline.FontWeight);
                textBlock.FontStyle = ResolveUserFontStyle(
                    _settings.FontStyleMode,
                    baseline.FontStyle);
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
                ApplyUserFont(VisualTreeHelper.GetChild(root, i));
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log(
                $"Font traversal skipped {root.GetType().Name}: {ex.Message}");
        }
    }

    private void UpdateFontPreview()
    {
        var family = _fontFamilyCombo.SelectedItem as string
                     ?? _settings.FontFamilyName;

        var requestedSize = double.IsNaN(_fontSizeBox.Value)
            ? _settings.FontSize
            : _fontSizeBox.Value;

        var weightMode = NormalizeFontWeightMode(
            _fontWeightCombo.SelectedItem as string
            ?? _settings.FontWeightMode);

        var styleMode = NormalizeFontStyleMode(
            _fontStyleCombo.SelectedItem as string
            ?? _settings.FontStyleMode);

        _fontPreview.FontFamily = ResolveUserFont(family);
        _fontPreview.FontSize = Math.Clamp(requestedSize, 10.0, 24.0);
        _fontPreview.FontWeight = ResolveUserFontWeight(
            weightMode,
            Microsoft.UI.Text.FontWeights.Normal);
        _fontPreview.FontStyle = ResolveUserFontStyle(
            styleMode,
            Windows.UI.Text.FontStyle.Normal);
    }

    private void FontTypographyPreview_Changed(
        object sender,
        SelectionChangedEventArgs e)
        => UpdateFontPreview();

    private void FontSizeBox_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
        => UpdateFontPreview();

    private void ApplyFont_Click(object sender, RoutedEventArgs e)
    {
        var family = _fontFamilyCombo.SelectedItem as string;
        _settings.FontFamilyName = string.IsNullOrWhiteSpace(family)
            ? "Segoe UI Variable"
            : family.Trim();

        _settings.FontSize = double.IsNaN(_fontSizeBox.Value)
            ? 14.0
            : Math.Clamp(_fontSizeBox.Value, 10.0, 24.0);

        _settings.FontWeightMode = NormalizeFontWeightMode(
            _fontWeightCombo.SelectedItem as string);

        _settings.FontStyleMode = NormalizeFontStyleMode(
            _fontStyleCombo.SelectedItem as string);

        _settingsService.Save(_settings);
        ApplyUserFont(Content);
        UpdateFontPreview();

        StatusText.Text =
            $"فونت «{_settings.FontFamilyName}» · اندازه {_settings.FontSize:0.#} · {FontWeightModeDisplayName(_settings.FontWeightMode)} · {_settings.FontStyleMode}";
    }

    private void ResetFont_Click(object sender, RoutedEventArgs e)
    {
        _settings.FontFamilyName = "Segoe UI Variable";
        _settings.FontSize = 14.0;
        _settings.FontWeightMode = "normal";
        _settings.FontStyleMode = "normal";
        _settingsService.Save(_settings);

        PopulateFontLibrary();
        _fontSizeBox.Value = _settings.FontSize;
        _fontWeightCombo.SelectedItem = "Normal";
        _fontStyleCombo.SelectedItem = "Normal";

        ApplyUserFont(Content);
        UpdateFontPreview();
        StatusText.Text = "فونت و تایپوگرافی به حالت پیش‌فرض برگشت.";
    }

    private static bool IsMourningHolidayTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var normalized = title
            .Replace('ي', 'ی')
            .Replace('ك', 'ک')
            .Replace("\u200c", " ")
            .Trim();

        string[] keywords =
        [
            "شهادت",
            "رحلت",
            "وفات",
            "عاشورا",
            "تاسوعا",
            "اربعین",
            "عزاداری",
            "سوگواری"
        ];

        return keywords.Any(keyword =>
            normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private async void GlassModeCheck_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.GlassMode = _glassModeCheck.IsChecked == true;
            _settingsService.Save(_settings);

            ApplyAppearanceBrushesOnly();
            BuildCalendar();
            await LoadSelectedDayAsync();
            await ApplyBackgroundAsync();

            StatusText.Text = _settings.GlassMode
                ? "Glass Mode برای Zara Pastel فعال شد."
                : "Glass Mode غیرفعال شد؛ Zara Pastel شفاف استاندارد فعال است.";
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Glass Mode switch failed: {ex}");
            _settings.GlassMode = false;
            _glassModeCheck.IsChecked = false;
            _settingsService.Save(_settings);
            StatusText.Text = "Glass Mode اعمال نشد؛ حالت استاندارد Zara Pastel حفظ شد.";
        }
    }

    private void BackgroundOpacitySlider_ValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs e)
    {
        _backgroundOpacityValue.Text = $"{Math.Round(e.NewValue):0}%";
    }

    private async void ApplyBackgroundOpacity_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _settings.BackgroundOpacity = Math.Clamp(
                _backgroundOpacitySlider.Value / 100.0,
                0,
                0.95);

            _settingsService.Save(_settings);
            await ApplyBackgroundAsync();

            var modeLabel = string.Equals(
                    _settings.AppearanceMode,
                    "elena",
                    StringComparison.OrdinalIgnoreCase)
                ? "تصویر فصلی"
                : "عکس زمینه";

            StatusText.Text =
                $"شدت {modeLabel} روی {Math.Round(_settings.BackgroundOpacity * 100):0}% اعمال شد.";
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Background opacity apply failed: {ex}");
            _backgroundOpacitySlider.Value =
                Math.Round(Math.Clamp(_settings.BackgroundOpacity, 0, 0.95) * 100);
            _backgroundOpacityValue.Text =
                $"{Math.Round(_backgroundOpacitySlider.Value):0}%";
            StatusText.Text = "اعمال شدت تصویر انجام نشد؛ مقدار قبلی حفظ شد.";
        }
    }

    private async void OccasionPicturesCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowOccasionPictures = _occasionPicturesCheck.IsChecked == true;
        _settingsService.Save(_settings);
        await LoadSelectedDayAsync();
    }

    private FrameworkElement BuildPictureLibraryPanel()
    {
        // This panel contains persistent UIElement instances such as
        // _pictureLibrarySummary. It MUST be cleared before a settings rebuild,
        // otherwise WinUI attempts to parent the same element twice and throws
        // COMException 0x800F1000.
        _pictureLibraryPanel.Children.Clear();
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

        _pictureLibrarySurface ??= new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1)
        };
        _pictureLibrarySurface.Background = IsLiquidGlassActive()
            ? CreateLiquidGlassBrush(_theme.CardBackground, 0.14, 0x90)
            : BrushWithAlpha(_theme.CardBackground, 0x72);
        _pictureLibrarySurface.BorderBrush = BrushWithAlpha("#FFFFFFFF", 0xA0);
        if (_pictureLibrarySurface.Child is null)
            _pictureLibrarySurface.Child = _pictureLibraryPanel;
        return _pictureLibrarySurface;
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

            var wasElena = string.Equals(
                _settings.AppearanceMode,
                "elena",
                StringComparison.OrdinalIgnoreCase);

            _settings.AppearanceMode = "theme";
            _settings.CustomBackgroundPath = copied;
            _settings.BackgroundMode = "custom";
            _settings.UseSeasonalBackground = false;

            if (_settings.BackgroundOpacity < 0.45)
                _settings.BackgroundOpacity = 0.72;

            _backgroundOpacityBox.Text = _settings.BackgroundOpacity.ToString(
                "0.00",
                System.Globalization.CultureInfo.InvariantCulture);

            _settingsService.Save(_settings);

            if (wasElena)
                await ApplyAppearanceAsync();
            else
                await ApplyBackgroundAsync();

            if (_pictureLibraryPanel.Visibility == Visibility.Visible)
                await RefreshPictureLibraryAsync();

            StatusText.Text = "عکس زمینه دلخواه در Theme Mode اعمال شد.";
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Custom background picker failed: {ex}");
            StatusText.Text = $"انتخاب عکس زمینه انجام نشد: {ex.Message}";
        }
    }

    private string GetCurrentSeasonName()
        => _month switch
        {
            <= 3 => "بهار",
            <= 6 => "تابستان",
            <= 9 => "پاییز",
            _ => "زمستان"
        };

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
            var isElena = string.Equals(
                _settings.AppearanceMode,
                "elena",
                StringComparison.OrdinalIgnoreCase);
            var isCustom = false;

            if (isElena)
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
            else if (string.Equals(
                         _settings.BackgroundMode,
                         "custom",
                         StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(_settings.CustomBackgroundPath) &&
                     File.Exists(_settings.CustomBackgroundPath))
            {
                path = _settings.CustomBackgroundPath;
                isCustom = true;
                label = $"تصویر شخصی · {Path.GetFileName(path)}";
            }

            var source = await PictureService.LoadImageAsync(path);

            // Theme Mode must never silently jump into Elena Mode.
            if (!isElena &&
                string.Equals(_settings.BackgroundMode, "custom", StringComparison.OrdinalIgnoreCase) &&
                source is null)
            {
                _settings.BackgroundMode = "none";
                _settings.CustomBackgroundPath = null;
                _settingsService.Save(_settings);
                label = "تصویر شخصی پیدا نشد؛ پس‌زمینه Theme استفاده می‌شود.";
            }

            _backgroundImage.Source = source;
            _backgroundImage.Opacity = Math.Clamp(_settings.BackgroundOpacity, 0, 0.95);

            _backgroundWash.Opacity = source is null
                ? 0
                : IsLiquidGlassActive()
                    ? 0.08
                    : IsZaraPastelActive()
                        ? 0.12
                        : isCustom
                            ? 0.16
                            : 0.24;

            _backgroundPathText.Text = label;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"ApplyBackgroundAsync failed: {ex}");
            _backgroundImage.Source = null;
            _backgroundWash.Opacity = 0;
            _backgroundPathText.Text = "بارگذاری تصویر زمینه انجام نشد.";
        }
    }

    private void UpdateMonthDecorations()
    {
        var isZaraPastel = string.Equals(
            _theme.Id,
            "zara-pastel",
            StringComparison.OrdinalIgnoreCase);

        var isElena = string.Equals(
            _settings.AppearanceMode,
            "elena",
            StringComparison.OrdinalIgnoreCase);

        if (!isZaraPastel && !isElena)
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

        // Zara keeps its softer secondary color; Elena follows its active
        // seasonal/manual accent so month decorations match the current mode.
        var decorationColor = isElena
            ? _theme.Accent
            : _theme.SecondaryText;

        MonthLeftDecoration.Foreground = ThemeService.Brush(decorationColor);
        MonthRightDecoration.Foreground = ThemeService.Brush(decorationColor);
    }

    private void RefreshElenaSeasonalPalette()
    {
        var isElena = string.Equals(
            _settings.AppearanceMode,
            "elena",
            StringComparison.OrdinalIgnoreCase);

        var generalIsSeasonal = string.Equals(
            _settings.ElenaAccentId,
            "seasonal",
            StringComparison.OrdinalIgnoreCase);

        var dayIsSeasonal = string.Equals(
            _settings.ElenaDayAccentId,
            "seasonal",
            StringComparison.OrdinalIgnoreCase);

        if (!isElena || (!generalIsSeasonal && !dayIsSeasonal))
            return;

        var updatedTheme = _themeService.ResolveAppearance(_settings, _month);
        if (string.Equals(updatedTheme.Accent, _theme.Accent, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(updatedTheme.SelectedDay, _theme.SelectedDay, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(updatedTheme.WindowBackground, _theme.WindowBackground, StringComparison.OrdinalIgnoreCase))
            return;

        _theme = updatedTheme;
        ApplyAppearanceBrushesOnly();

        StartupDiagnostics.Log(
            $"Elena seasonal palette changed for Persian month {_month}: accent={_theme.Accent}, selectedDay={_theme.SelectedDay}");
    }

    private void BuildCalendar()
    {
        RefreshElenaSeasonalPalette();
        MonthTitle.Text = $"{PersianDate.MonthNames[_month - 1]} {_year}";
        UpdateMonthDecorations();
        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();
        _calendarOccasionPanels.Clear();
        _calendarDayNumberLabels.Clear();
        _calendarMourningRibbons.Clear();

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

            // Diagonal black mourning ribbon. It stays hidden for normal holidays
            // and becomes visible only for official mourning holidays.
            var mourningRibbon = new Border
            {
                Width = 34,
                Height = 8,
                Background = new SolidColorBrush(Colors.Black),
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-8, 7, 0, 0),
                FlowDirection = FlowDirection.LeftToRight,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new RotateTransform { Angle = -45 }
            };
            ToolTipService.SetToolTip(
                mourningRibbon,
                "تعطیل رسمی سوگواری");

            var cellLayer = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            cellLayer.Children.Add(content);
            cellLayer.Children.Add(mourningRibbon);

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
                Background = CreateCalendarCellBrush(
                    cell.Date == _selected
                        ? _theme.SelectedDay
                        : GetWeekdayColor(i % 7),
                    cell.Date == _selected),
                BorderBrush = cell.Date == _selected
                    ? BrushWithAlpha(
                        _theme.PrimaryText,
                        IsLiquidGlassActive() ? (byte)0xB8 : (byte)0x9A)
                    : BrushWithAlpha(
                        "#FFFFFFFF",
                        IsLiquidGlassActive() ? (byte)0xD8 : (byte)0xB8),
                BorderThickness = cell.Date == _selected
                    ? new Thickness(IsLiquidGlassActive() ? 2.0 : 1.6)
                    : new Thickness(1),
                CornerRadius = new CornerRadius(IsLiquidGlassActive() ? 20 : 14),
                Content = cellLayer
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
            _calendarMourningRibbons[captured] = mourningRibbon;

            Grid.SetRow(button, i / 7);
            Grid.SetColumn(button, i % 7);
            CalendarGrid.Children.Add(button);
        }

        ApplyUserFont(CalendarGrid);
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

                var hasOfficialHoliday = snapshot.Occasions.Any(x => x.IsHoliday);

                if ((date.DayOfWeek == DayOfWeek.Friday || hasOfficialHoliday) &&
                    _calendarDayNumberLabels.TryGetValue(date, out var dayLabel))
                {
                    dayLabel.Foreground = ThemeService.Brush(_theme.HolidayText);
                    dayLabel.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                }

                var hasMourningHoliday = snapshot.Occasions.Any(
                    x => x.IsHoliday && IsMourningHolidayTitle(x.Title));

                if (_calendarMourningRibbons.TryGetValue(date, out var ribbon))
                {
                    ribbon.Visibility = hasMourningHoliday
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                ApplyUserFont(panel);
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

            ApplyUserFont(OccasionsPanel);
            ApplyUserFont(EventsPanel);
            ApplyUserFont(TasksPanel);
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

            ApplyUserFont(ActivityPanel);
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
