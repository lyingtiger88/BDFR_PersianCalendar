using BDFR.PersianCalendar.Core;
using BDFR.PersianCalendar.Infrastructure;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace BDFR.PersianCalendar.Desktop;

public sealed class MainWindow : Window
{
    private readonly ICalendarRepository _repository;
    private readonly PlannerService _planner;
    private readonly IOccasionSource _occasionSource;
    private readonly SpecialOccasionService _specialOccasions;
    private readonly PersianQuickAddParser _quickAdd = new();

    private readonly TextBlock MonthTitle = new();
    private readonly Grid CalendarGrid = new();
    private readonly TextBlock SelectedDateTitle = new();
    private readonly TextBlock GregorianDateText = new();
    private readonly StackPanel OccasionsPanel = new();
    private readonly StackPanel EventsPanel = new();
    private readonly StackPanel TasksPanel = new();
    private readonly TextBox NoteBox = new();
    private readonly TextBox EventTitleBox = new();
    private readonly TextBox EventTimeBox = new();
    private readonly NumberBox ReminderMinutesBox = new();
    private readonly TextBox TaskTitleBox = new();
    private readonly TextBox TaskTimeBox = new();
    private readonly TextBox SpecialTitleBox = new();
    private readonly ComboBox SpecialCalendarBox = new();
    private readonly NumberBox SpecialMonthBox = new();
    private readonly NumberBox SpecialDayBox = new();
    private readonly TextBox SpecialReminderDaysBox = new();
    private readonly TextBox QuickAddBox = new();
    private readonly TextBlock StatusText = new();
    private readonly ProgressRing SyncProgress = new();
    private readonly StackPanel ActivityPanel = new();

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

        _year = _selected.Year;
        _month = _selected.Month;

        Title = "BDFR Persian Calendar";
        Content = BuildRoot();

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

        StartupDiagnostics.Log("MainWindow: programmatic UI ready.");
    }

    private FrameworkElement BuildRoot()
    {
        var root = new Grid
        {
            FlowDirection = FlowDirection.RightToLeft
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });

        var left = BuildLeftPanel();
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        var center = BuildCalendarPanel();
        Grid.SetColumn(center, 1);
        root.Children.Add(center);

        var right = BuildRightPanel();
        Grid.SetColumn(right, 2);
        root.Children.Add(right);

        return root;
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
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        stack.Children.Add(new TextBlock
        {
            Text = "تقویم، برنامه‌ریز و یادآور فارسی ویندوز",
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap
        });

        var today = MakeButton("امروز");
        today.Click += Today_Click;
        stack.Children.Add(today);

        var sync = MakeButton("همگام‌سازی مناسبت‌های time.ir");
        sync.Click += Sync_Click;
        stack.Children.Add(sync);

        SyncProgress.IsActive = false;
        SyncProgress.Width = 24;
        SyncProgress.Height = 24;
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

        return new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(32, 128, 128, 128)),
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
        MonthTitle.HorizontalAlignment = HorizontalAlignment.Center;
        MonthTitle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(MonthTitle, 1);
        header.Children.Add(MonthTitle);

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
            var label = new TextBlock
            {
                Text = names[i],
                HorizontalAlignment = HorizontalAlignment.Center
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

        ReminderMinutesBox.Header = "یادآوری چند دقیقه قبل؟";
        ReminderMinutesBox.Minimum = 0;
        ReminderMinutesBox.Maximum = 10080;
        ReminderMinutesBox.Value = 10;
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

        SpecialCalendarBox.Header = "نوع تقویم";
        SpecialCalendarBox.Items.Add("شمسی");
        SpecialCalendarBox.Items.Add("میلادی");
        SpecialCalendarBox.Items.Add("قمری");
        SpecialCalendarBox.SelectedIndex = 0;
        stack.Children.Add(SpecialCalendarBox);

        var md = new Grid { ColumnSpacing = 8 };
        md.ColumnDefinitions.Add(new ColumnDefinition());
        md.ColumnDefinitions.Add(new ColumnDefinition());

        SpecialMonthBox.Header = "ماه";
        SpecialMonthBox.Minimum = 1;
        SpecialMonthBox.Maximum = 12;
        SpecialMonthBox.Value = 1;
        Grid.SetColumn(SpecialMonthBox, 0);
        md.Children.Add(SpecialMonthBox);

        SpecialDayBox.Header = "روز";
        SpecialDayBox.Minimum = 1;
        SpecialDayBox.Maximum = 31;
        SpecialDayBox.Value = 1;
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
            BorderThickness = new Thickness(1, 0, 0, 0),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(32, 128, 128, 128)),
            Child = new ScrollViewer { Content = stack }
        };
    }

    private static TextBlock SectionTitle(string text)
        => new()
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 0)
        };

    private static Button MakeButton(string text)
        => new()
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

    private void BuildCalendar()
    {
        MonthTitle.Text = $"{PersianDate.MonthNames[_month - 1]} {_year}";
        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();

        for (var i = 0; i < 7; i++)
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 6; i++)
            CalendarGrid.RowDefinitions.Add(new RowDefinition());

        var cells = MonthGridBuilder.Build(_year, _month);
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(4),
                MinHeight = 72,
                Opacity = cell.IsCurrentMonth ? 1 : 0.38,
                Content = new TextBlock
                {
                    Text = cell.Date.Day.ToString(),
                    FontSize = 18,
                    FontWeight = cell.IsToday
                        ? Microsoft.UI.Text.FontWeights.Bold
                        : Microsoft.UI.Text.FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };

            if (cell.Date.DayOfWeek == DayOfWeek.Friday)
                button.Foreground = new SolidColorBrush(Colors.IndianRed);

            var captured = cell.Date;
            button.Click += async (_, _) =>
            {
                _selected = captured;
                await LoadSelectedDayAsync();
            };

            Grid.SetRow(button, i / 7);
            Grid.SetColumn(button, i % 7);
            CalendarGrid.Children.Add(button);
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
                OccasionsPanel.Children.Add(new TextBlock
                {
                    Text = $"{(item.IsHoliday ? "● " : "• ")}{item.Title}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = item.IsHoliday ? new SolidColorBrush(Colors.IndianRed) : null
                });
            }
            if (snapshot.Occasions.Count == 0)
                OccasionsPanel.Children.Add(new TextBlock { Text = "مناسبتی ثبت نشده", Opacity = 0.55 });

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

        var reminder = double.IsNaN(ReminderMinutesBox.Value)
            ? 10
            : (int)ReminderMinutesBox.Value;

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

        var month = (int)SpecialMonthBox.Value;
        var day = (int)SpecialDayBox.Value;

        var reminderDays = (SpecialReminderDaysBox.Text ?? "7,1,0")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(PersianQuickAddParser.NormalizeDigits(x), out var n) ? n : -1)
            .Where(x => x >= 0)
            .Distinct()
            .ToArray();

        try
        {
            var calendarSystem = (CalendarSystemKind)Math.Clamp(SpecialCalendarBox.SelectedIndex, 0, 2);

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
        SyncProgress.IsActive = true;
        StatusText.Text = "در حال دریافت مناسبت‌ها...";

        try
        {
            var count = await _planner.SyncOccasionsAsync(_occasionSource, _year);
            StatusText.Text = $"{count} مناسبت برای سال {_year} همگام شد.";

            await LoadSelectedDayAsync();
            await LoadActivitiesAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"همگام‌سازی ناموفق بود؛ داده محلی حذف نشد. {ex.Message}";
        }
        finally
        {
            SyncProgress.IsActive = false;
        }
    }

    private async void RefreshActivities_Click(object sender, RoutedEventArgs e)
        => await LoadActivitiesAsync();

    private async Task LoadActivitiesAsync()
    {
        try
        {
            var items = await _repository.GetRecentActivitiesAsync(8);
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

                ActivityPanel.Children.Add(new TextBlock
                {
                    Text = $"{action} · {item.CreatedAt.ToLocalTime():MM/dd HH:mm}\n{item.Message}",
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
}
