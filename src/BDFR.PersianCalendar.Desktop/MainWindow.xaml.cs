using BDFR.PersianCalendar.Core;
using BDFR.PersianCalendar.Infrastructure;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly ICalendarRepository _repository;
    private readonly PlannerService _planner;
    private readonly IOccasionSource _occasionSource;
    private readonly SpecialOccasionService _specialOccasions;
    private readonly PersianQuickAddParser _quickAdd = new();

    private PersianDate _selected = PersianDate.Today();
    private int _year;
    private int _month;

    public MainWindow(ICalendarRepository repository, PlannerService planner, IOccasionSource occasionSource, SpecialOccasionService specialOccasions)
    {
        InitializeComponent();
        _repository = repository;
        _planner = planner;
        _occasionSource = occasionSource;
        _specialOccasions = specialOccasions;

        _year = _selected.Year;
        _month = _selected.Month;

        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop();

        BuildCalendar();
        _ = LoadSelectedDayAsync();
    }

    private void BuildCalendar()
    {
        MonthTitle.Text = $"{PersianDate.MonthNames[_month - 1]} {_year}";
        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();

        for (var i = 0; i < 7; i++) CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 6; i++) CalendarGrid.RowDefinitions.Add(new RowDefinition());

        var cells = MonthGridBuilder.Build(_year, _month);
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(12),
                MinHeight = 72,
                Opacity = cell.IsCurrentMonth ? 1 : 0.38,
                Content = new TextBlock
                {
                    Text = cell.Date.Day.ToString(),
                    FontSize = 18,
                    FontWeight = cell.IsToday ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
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
        if (snapshot.Occasions.Count == 0) OccasionsPanel.Children.Add(new TextBlock { Text = "مناسبتی ثبت نشده", Opacity = 0.55 });

        EventsPanel.Children.Clear();
        foreach (var item in snapshot.Events)
            EventsPanel.Children.Add(new TextBlock
            {
                Text = $"{(item.StartTime is null ? "تمام‌روز" : item.StartTime.Value.ToString("HH:mm"))}  {item.Title}",
                TextWrapping = TextWrapping.Wrap
            });
        if (snapshot.Events.Count == 0) EventsPanel.Children.Add(new TextBlock { Text = "رویدادی ندارید", Opacity = 0.55 });

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
                    await _repository.SetTaskCompletedAsync(id, cb.IsChecked == true);
                    await LoadSelectedDayAsync();
                }
            };
            TasksPanel.Children.Add(checkbox);
        }
        if (snapshot.Tasks.Count == 0) TasksPanel.Children.Add(new TextBlock { Text = "کاری ندارید", Opacity = 0.55 });
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
        StatusText.Text = "یادداشت ذخیره شد.";
        await LoadSelectedDayAsync();
    }

    private async void AddEvent_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EventTitleBox.Text) || !TimeOnly.TryParse(EventTimeBox.Text, out var time))
        {
            StatusText.Text = "برای رویداد عنوان و زمان معتبر وارد کنید.";
            return;
        }

        var reminder = double.IsNaN(ReminderMinutesBox.Value) ? 10 : (int)ReminderMinutesBox.Value;
        await _planner.AddEventAsync(EventTitleBox.Text, _selected, time, reminderMinutes: [reminder]);
        EventTitleBox.Text = "";
        EventTimeBox.Text = "";
        StatusText.Text = "رویداد و یادآور ثبت شد.";
        await LoadSelectedDayAsync();
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
    }

    private async void QuickAdd_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var parsed = _quickAdd.Parse(QuickAddBox.Text);
            if (parsed.Time is not null)
                await _planner.AddEventAsync(parsed.Title, parsed.Date, parsed.Time.Value, reminderMinutes: [parsed.ReminderMinutesBefore]);
            else
                await _planner.AddTaskAsync(parsed.Title, parsed.Date);

            _selected = parsed.Date;
            _year = _selected.Year;
            _month = _selected.Month;
            QuickAddBox.Text = "";
            BuildCalendar();
            await LoadSelectedDayAsync();
            StatusText.Text = "فعالیت با افزودن سریع ثبت شد.";
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
            await _specialOccasions.AddPersianAnnualAsync(
                SpecialTitleBox.Text.Trim(), month, day,
                reminderDays.Length == 0 ? [7, 1, 0] : reminderDays,
                new TimeOnly(9, 0));
            SpecialTitleBox.Text = "";
            StatusText.Text = "مناسبت شخصی و یادآورهای سالانه ثبت شد.";
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
}