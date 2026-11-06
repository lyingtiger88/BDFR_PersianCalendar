using System.Text.Json;
using BDFR.PersianCalendar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private readonly ZurvanDeskDesktopClient _deskClient = new();
    private TextBlock? _deskStatus;
    private StackPanel? _deskFiles;
    private bool _deskRegisterMode;

    private static void DetachDesktopElement(FrameworkElement element)
    {
        var parent = VisualTreeHelper.GetParent(element);
        if (parent is Panel panel) panel.Children.Remove(element);
        else if (parent is Border border) border.Child = null;
        else if (parent is ContentControl content) content.Content = null;
    }

    private void AddModuleControl(Panel host, FrameworkElement element)
    {
        DetachDesktopElement(element);
        host.Children.Add(element);
    }

    private void ShowDesktopModule(string module)
    {
        _activeDesktopModule = module;
        if (_desktopCalendarPage is null || _desktopModulePage is null || _desktopSelectedDayPanel is null)
            return;

        var isCalendar = module == "تقویم";
        _desktopCalendarPage.Visibility = isCalendar ? Visibility.Visible : Visibility.Collapsed;
        _desktopSelectedDayPanel.Visibility = isCalendar ? Visibility.Visible : Visibility.Collapsed;
        _desktopModulePage.Visibility = isCalendar ? Visibility.Collapsed : Visibility.Visible;
        _desktopModulePage.Child = BuildDesktopModulePage(module);
        _desktopModulePage.FlowDirection = FlowDirection.RightToLeft;
    }

    private FrameworkElement BuildDesktopModulePage(string module)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FlowDirection = FlowDirection.RightToLeft
        };
        var page = new StackPanel
        {
            Spacing = 14,
            Padding = new Thickness(12, 8, 12, 24),
            MaxWidth = 1200,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FlowDirection = FlowDirection.RightToLeft
        };
        page.Children.Add(new TextBlock
        {
            Text = module,
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        Border Card(string title, string description, FrameworkElement content)
        {
            var body = new StackPanel { Spacing = 10, Padding = new Thickness(18) };
            body.Children.Add(new TextBlock
            {
                Text = title, FontSize = 17,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ThemeService.Brush(_theme.PrimaryText)
            });
            if (!string.IsNullOrWhiteSpace(description))
                body.Children.Add(new TextBlock
                {
                    Text = description, TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemeService.Brush(_theme.SecondaryText)
                });
            body.Children.Add(content);
            return new Border
            {
                Background = CreateSideSurfaceBrush(),
                BorderBrush = BrushWithAlpha(_theme.Accent, 0x80),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = body,
                MaxWidth = 1000,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        switch (module)
        {
            case "برنامه‌ها":
            {
                var tools = new StackPanel { Spacing = 8 };
                var week = new StackPanel { Spacing = 7 };
                var nav = new Grid { ColumnSpacing = 8 };
                nav.ColumnDefinitions.Add(new ColumnDefinition());
                nav.ColumnDefinitions.Add(new ColumnDefinition());
                nav.ColumnDefinitions.Add(new ColumnDefinition());
                var previous = MakeButton("هفته قبل");
                var today = MakeButton("هفته جاری");
                var next = MakeButton("هفته بعد");
                var offset = 0;
                async Task LoadWeek()
                {
                    week.Children.Clear();
                    var start = _selected.AddDays(-(((int)_selected.DayOfWeek + 1) % 7) + offset * 7);
                    for (var i = 0; i < 7; i++)
                    {
                        var date = start.AddDays(i);
                        var snapshot = await _repository.GetDayAsync(date);
                        var day = new StackPanel { Spacing = 4, Padding = new Thickness(10) };
                        day.Children.Add(new TextBlock
                        {
                            Text = date.ToLongPersianString(),
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = ThemeService.Brush(_theme.PrimaryText)
                        });
                        foreach (var ev in snapshot.Events)
                            day.Children.Add(new TextBlock { Text = $"◷ {ev.Title} · {(ev.StartTime?.ToString("HH:mm") ?? "تمام‌روز")}", TextWrapping = TextWrapping.Wrap });
                        foreach (var task in snapshot.Tasks)
                        {
                            var check = new CheckBox { Content = task.Title, IsChecked = task.Completed };
                            check.Checked += async (_, _) => { await _repository.SetTaskCompletedAsync(task.Id, true); await LoadWeek(); };
                            check.Unchecked += async (_, _) => { await _repository.SetTaskCompletedAsync(task.Id, false); await LoadWeek(); };
                            day.Children.Add(check);
                        }
                        if (snapshot.Events.Count == 0 && snapshot.Tasks.Count == 0)
                            day.Children.Add(new TextBlock { Text = "برنامه‌ای ثبت نشده است.", Opacity = 0.6 });
                        week.Children.Add(Card("", "", day));
                    }
                }
                previous.Click += async (_, _) => { offset--; await LoadWeek(); };
                today.Click += async (_, _) => { offset = 0; await LoadWeek(); };
                next.Click += async (_, _) => { offset++; await LoadWeek(); };
                Grid.SetColumn(previous, 0); Grid.SetColumn(today, 1); Grid.SetColumn(next, 2);
                nav.Children.Add(previous); nav.Children.Add(today); nav.Children.Add(next);
                tools.Children.Add(nav);
                var addTask = MakeButton("＋ ثبت کار برای روز انتخاب‌شده");
                addTask.Click += AddTask_Click;
                AddModuleControl(tools, TaskTitleBox);
                AddModuleControl(tools, TaskTimeBox);
                tools.Children.Add(addTask);
                var addEvent = MakeButton("＋ ثبت رویداد و زمان‌بندی یادآور");
                addEvent.Click += AddEvent_Click;
                tools.Children.Add(addEvent);
                page.Children.Add(Card("برنامه‌ریز هفتگی", "برنامه‌ها بر اساس روز نمایش داده می‌شوند؛ انجام کارها از همین صفحه قابل تغییر است.", week));
                page.Children.Add(Card("افزودن برنامه", "ابتدا روز را در تقویم انتخاب کنید؛ سپس کار یا رویداد را ثبت کنید.", tools));
                _ = LoadWeek();
                break;
            }
            case "مناسبت‌ها":
            {
                var official = new StackPanel { Spacing = 6 };
                var personal = new StackPanel { Spacing = 6 };
                var refresh = MakeButton("به‌روزرسانی مناسبت‌های سال");
                refresh.Click += async (_, _) =>
                {
                    refresh.IsEnabled = false;
                    try
                    {
                        var entries = await _occasionSource.GetYearAsync(_selected.Year);
                        await _repository.ReplaceOccasionsForYearAsync(_occasionSource.Name, _selected.Year, entries);
                        await LoadSelectedDayAsync();
                        await LoadCalendarCellOccasionsAsync(_year, _month);
                        official.Children.Clear();
                        foreach (var item in entries.OrderBy(x => x.Date))
                            official.Children.Add(new TextBlock { Text = $"{item.Date.MonthName} {item.Date.Day} · {item.Title}{(item.IsHoliday ? " · تعطیل رسمی" : "")}", TextWrapping = TextWrapping.Wrap });
                        StatusText.Text = $"{entries.Count} مناسبت برای سال {_selected.Year} به‌روزرسانی شد.";
                    }
                    catch (Exception ex) { StatusText.Text = "به‌روزرسانی مناسبت‌ها انجام نشد: " + ex.Message; }
                    finally { refresh.IsEnabled = true; }
                };
                var add = new StackPanel { Spacing = 8 };
                AddModuleControl(add, SpecialTitleBox);
                AddModuleControl(add, SpecialCalendarBox);
                AddModuleControl(add, SpecialMonthBox);
                AddModuleControl(add, SpecialDayBox);
                AddModuleControl(add, SpecialHourBox);
                AddModuleControl(add, SpecialMinuteBox);
                AddModuleControl(add, SpecialReminderDaysBox);
                var save = MakeButton("ثبت مناسبت شخصی سالانه");
                save.Click += AddSpecialOccasion_Click;
                add.Children.Add(save);
                page.Children.Add(Card("مناسبت‌های رسمی", $"تقویم رسمی سال {_selected.Year}", official));
                page.Children.Add(refresh);
                var selectedDayOccasions = OccasionsPanel;
                DetachDesktopElement(selectedDayOccasions);
                page.Children.Add(Card("مناسبت‌های روز انتخاب‌شده", "مناسبت‌هایی که در روز جاری انتخاب شده‌اند.", selectedDayOccasions));
                page.Children.Add(Card("ثبت مناسبت شخصی", "تولد، سالگرد و مناسبت‌های تکرارشونده با زمان و یادآورهای مستقل.", add));
                page.Children.Add(Card("مناسبت‌های شخصی ثبت‌شده", "فهرست مناسبت‌های سالانه کاربر.", personal));
                async Task LoadPersonal()
                {
                    personal.Children.Clear();
                    var items = await _repository.GetSpecialOccasionsAsync();
                    if (items.Count == 0) personal.Children.Add(new TextBlock { Text = "هنوز مناسبتی ثبت نشده است.", Opacity = 0.65 });
                    foreach (var item in items)
                        personal.Children.Add(new TextBlock { Text = $"{item.Title} · {item.CalendarSystem} · {item.Month}/{item.Day} · یادآوری: {string.Join("، ", item.ReminderDaysBefore)} روز قبل", TextWrapping = TextWrapping.Wrap });
                }
                async Task LoadOfficial()
                {
                    try
                    {
                        var entries = await _occasionSource.GetYearAsync(_selected.Year);
                        official.Children.Clear();
                        foreach (var item in entries.OrderBy(x => x.Date))
                            official.Children.Add(new TextBlock { Text = $"{item.Date.MonthName} {item.Date.Day} · {item.Title}{(item.IsHoliday ? " · تعطیل رسمی" : "")}", TextWrapping = TextWrapping.Wrap });
                    }
                    catch { official.Children.Add(new TextBlock { Text = "برای دریافت تقویم رسمی، دکمه به‌روزرسانی را بزنید.", Opacity = 0.65 }); }
                }
                _ = LoadOfficial();
                _ = LoadPersonal();
                break;
            }
            case "یادآورها":
            {
                var reminders = new StackPanel { Spacing = 8 };
                var refresh = MakeButton("تازه‌سازی یادآورها");
                async Task LoadReminders()
                {
                    reminders.Children.Clear();
                    var items = await _repository.GetPendingRemindersAsync(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(2));
                    var awaiting = await _repository.GetAwaitingAcknowledgementRemindersAsync();
                    var all = awaiting.Concat(items).GroupBy(x => x.Id).Select(x => x.First()).OrderBy(x => x.FireAtUtc).ToList();
                    if (all.Count == 0) reminders.Children.Add(new TextBlock { Text = "یادآور فعالی وجود ندارد.", Opacity = 0.65 });
                    foreach (var item in all)
                    {
                        var row = new StackPanel { Spacing = 5, Padding = new Thickness(10) };
                        row.Children.Add(new TextBlock { Text = item.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                        row.Children.Add(new TextBlock { Text = $"{item.FireAtUtc.ToLocalTime():yyyy/MM/dd HH:mm} · {item.State} · تلاش {item.Attempt}/{item.RepeatCount}", Opacity = 0.7 });
                        row.Children.Add(new TextBlock { Text = item.Body, TextWrapping = TextWrapping.Wrap });
                        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        var done = MakeButton("متوجه شدم");
                        done.Click += async (_, _) => { await _repository.SetReminderStateAsync(item.Id, ReminderState.Acknowledged); await LoadReminders(); };
                        var dismiss = MakeButton("رد کردن");
                        dismiss.Click += async (_, _) => { await _repository.SetReminderStateAsync(item.Id, ReminderState.Dismissed); await LoadReminders(); };
                        actions.Children.Add(done); actions.Children.Add(dismiss); row.Children.Add(actions);
                        reminders.Children.Add(Card("", "", row));
                    }
                }
                refresh.Click += async (_, _) => await LoadReminders();
                var add = MakeButton("＋ ثبت رویداد و یادآور جدید");
                add.Click += AddEvent_Click;
                page.Children.Add(Card("مرکز یادآورها", "زمان‌بندی، تکرار هشدار و تأیید یادآورها؛ همان داده‌های مورد استفاده تقویم.", reminders));
                page.Children.Add(refresh);
                page.Children.Add(add);
                _ = LoadReminders();
                break;
            }
            case "زروان دسک":
                page.Children.Add(BuildDeskPage());
                break;
            case "همراه":
                page.Children.Add(BuildCompanionPage());
                break;
            case "فعالیت‌ها":
            {
                var actions = new StackPanel { Spacing = 8 };
                var refresh = MakeButton("تازه‌سازی فعالیت‌ها");
                refresh.Click += RefreshActivities_Click;
                var clear = MakeButton("پاکسازی تاریخچه فعالیت");
                clear.Click += ClearActivities_Click;
                actions.Children.Add(refresh); actions.Children.Add(clear);
                DetachDesktopElement(ActivityPanel);
                page.Children.Add(Card("مرکز فعالیت", "گزارش رویدادهای ثبت‌شده در زروان.", ActivityPanel));
                page.Children.Add(actions);
                break;
            }
            case "لایسنس و فعال‌سازی":
                page.Children.Add(BuildLicensePanel());
                break;
            case "تنظیمات":
            {
                var settings = _settingsSurface ?? (Border)BuildSettingsPanelSafe();
                DetachDesktopElement(settings);
                page.Children.Add(settings);
                break;
            }
            default:
                page.Children.Add(Card("تقویم زروان", "برای مشاهده روزها و جزئیات روزانه به صفحه تقویم بازگردید.", MakeButton("بازگشت به تقویم")));
                break;
        }

        scroll.Content = page;
        return scroll;
    }

    private FrameworkElement BuildDeskPage() => BuildZurvanDeskWorkspace();

    private FrameworkElement BuildCompanionPage()
    {
        var host = new StackPanel { Spacing = 10 };
        var open = MakeButton("باز کردن مدیریت همراه و اتصال دستگاه‌ها");
        open.Click += async (_, _) => await ShowCompanionOverviewAsync();
        host.Children.Add(open);
        host.Children.Add(new TextBlock
        {
            Text = "اتصال امن Master/Secondary، QR یک‌بارمصرف و همگام‌سازی رمزگذاری‌شده دستگاه‌ها در این ماژول مدیریت می‌شود.",
            TextWrapping = TextWrapping.Wrap
        });
        return host;
    }
}

// [pc-android-only] Android-parity module navigation implementation.

// [pc-android-only] Build scope: Windows desktop and Android only.
