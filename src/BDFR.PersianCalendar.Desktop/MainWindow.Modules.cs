using System.Text.Json;
using BDFR.PersianCalendar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
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
        if (_desktopCalendarPage is null || _desktopModulePage is null || _desktopSelectedDayPanel is null)
        {
            StartupDiagnostics.Log($"Module navigation unavailable: '{module}', workspace not initialized.");
            return;
        }

        try
        {
            var isCalendar = module == "تقویم";
            // Build before changing the visible page: an optional module
            // failure must not leave the central workspace blank.
            var page = isCalendar ? null : BuildDesktopModulePage(module);
            _activeDesktopModule = module;
            _desktopModulePage.Child = page;
            _desktopModulePage.FlowDirection = FlowDirection.RightToLeft;
            _desktopCalendarPage.Visibility = isCalendar ? Visibility.Visible : Visibility.Collapsed;
            _desktopSelectedDayPanel.Visibility = isCalendar ? Visibility.Visible : Visibility.Collapsed;
            _desktopModulePage.Visibility = isCalendar ? Visibility.Collapsed : Visibility.Visible;

            // The selected-date sidebar belongs to Calendar only. Non-calendar
            // modules should occupy the full workspace, including the area
            // formerly reserved for the selected day.
            if (_physicalLeftSidebarColumn is not null)
                _physicalLeftSidebarColumn.Width = new GridLength(
                    isCalendar ? Math.Clamp(_settings.LeftSidebarWidth, 220, 560) : 0);
            StartupDiagnostics.Log($"Desktop module opened: '{module}' (calendar={isCalendar}).");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Desktop module '{module}' failed: {ex}");
            StatusText.Text = $"خطا در باز کردن بخش {module}: {ex.Message}";
            // Ensure the failed page does not make subsequent navigation inert.
            _desktopModulePage.Visibility = Visibility.Visible;
            _desktopCalendarPage.Visibility = Visibility.Collapsed;
            _desktopSelectedDayPanel.Visibility = Visibility.Collapsed;
            if (_physicalLeftSidebarColumn is not null)
                _physicalLeftSidebarColumn.Width = new GridLength(0);
            _desktopModulePage.Child = new StackPanel
            {
                Padding = new Thickness(22),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"باز کردن بخش «{module}» با خطا مواجه شد.",
                        FontSize = 20,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap }
                }
            };
        }
    }

    private FrameworkElement BuildDesktopModulePage(string module)
    {
        // Desk already owns the complete page header and two-column workspace.
        // Avoid wrapping it in the legacy generic heading/card layout.
        if (module == "زروان دسک")
        {
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FlowDirection = FlowDirection.RightToLeft,
                Content = BuildDeskPage()
            };
        }
        if (module == "تنظیمات")
        {
            return BuildWindowsSettingsPage();
        }
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
                var officialMonths = new StackPanel { Spacing = 12 };
                var personal = new StackPanel { Spacing = 6 };
                var refresh = MakeButton("به‌روزرسانی مناسبت‌های سال");
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

                async Task RenderOfficialMonths()
                {
                    officialMonths.Children.Clear();
                    var entries = await _occasionSource.GetYearAsync(_selected.Year);
                    var months = PersianDate.MonthNames;
                    for (var month = 1; month <= 12; month++)
                    {
                        var monthEntries = entries
                            .Where(x => x.Date.Month == month)
                            .OrderBy(x => x.Date.Day)
                            .ThenBy(x => x.Title)
                            .ToList();
                        var monthBody = new StackPanel { Spacing = 7, Padding = new Thickness(12) };
                        if (monthEntries.Count == 0)
                        {
                            monthBody.Children.Add(new TextBlock
                            {
                                Text = "برای این ماه مناسبتی ثبت نشده است.",
                                Opacity = 0.55,
                                Foreground = ThemeService.Brush(_theme.SecondaryText)
                            });
                        }
                        foreach (var item in monthEntries)
                        {
                            var holiday = item.IsHoliday;
                            var row = new Grid { ColumnSpacing = 10, FlowDirection = FlowDirection.RightToLeft };
                            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            var day = new Border
                            {
                                MinWidth = 42,
                                Padding = new Thickness(8, 5, 8, 5),
                                CornerRadius = new CornerRadius(8),
                                Background = holiday ? BrushWithAlpha("#FFE53935", 0x20) : BrushWithAlpha(_theme.Accent, 0x18),
                                Child = new TextBlock
                                {
                                    Text = ToPersianDigits(item.Date.Day.ToString()),
                                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                    Foreground = holiday ? ThemeService.Brush("#FFD32F2F") : ThemeService.Brush(_theme.PrimaryText),
                                    HorizontalAlignment = HorizontalAlignment.Center
                                }
                            };
                            Grid.SetColumn(day, 0);
                            row.Children.Add(day);
                            var title = new TextBlock
                            {
                                Text = item.Title,
                                TextWrapping = TextWrapping.Wrap,
                                VerticalAlignment = VerticalAlignment.Center,
                                Foreground = holiday ? ThemeService.Brush("#FFD32F2F") : ThemeService.Brush(_theme.PrimaryText),
                                FontWeight = holiday ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
                            };
                            Grid.SetColumn(title, 1);
                            row.Children.Add(title);
                            if (holiday)
                            {
                                var badge = new Border
                                {
                                    Padding = new Thickness(7, 4, 7, 4),
                                    CornerRadius = new CornerRadius(8),
                                    Background = BrushWithAlpha("#FFE53935", 0x20),
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Child = new TextBlock
                                    {
                                        Text = "تعطیل رسمی",
                                        FontSize = 11,
                                        Foreground = ThemeService.Brush("#FFD32F2F")
                                    }
                                };
                                Grid.SetColumn(badge, 2);
                                row.Children.Add(badge);
                            }
                            monthBody.Children.Add(row);
                        }

                        var monthCard = new Border
                        {
                            Background = CreateSideSurfaceBrush(),
                            BorderBrush = BrushWithAlpha(_theme.Accent, 0x60),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(13),
                            Child = monthBody
                        };
                        var monthSection = new StackPanel { Spacing = 6 };
                        monthSection.Children.Add(new TextBlock
                        {
                            Text = months[month - 1],
                            FontSize = 17,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = ThemeService.Brush(_theme.PrimaryText)
                        });
                        monthSection.Children.Add(monthCard);
                        officialMonths.Children.Add(monthSection);
                    }
                }

                refresh.Click += async (_, _) =>
                {
                    refresh.IsEnabled = false;
                    try
                    {
                        var entries = await _occasionSource.GetYearAsync(_selected.Year);
                        await _repository.ReplaceOccasionsForYearAsync(_occasionSource.Name, _selected.Year, entries);
                        await LoadSelectedDayAsync();
                        await LoadCalendarCellOccasionsAsync(_year, _month);
                        await RenderOfficialMonths();
                        StatusText.Text = $"مناسبت‌های سال {ToPersianDigits(_selected.Year.ToString())} به‌روزرسانی شد.";
                    }
                    catch (Exception ex) { StatusText.Text = "به‌روزرسانی مناسبت‌ها انجام نشد: " + ex.Message; }
                    finally { refresh.IsEnabled = true; }
                };

                var selectedDayOccasions = OccasionsPanel;
                DetachDesktopElement(selectedDayOccasions);
                page.Children.Add(Card("مناسبت‌های رسمی سال " + ToPersianDigits(_selected.Year.ToString()),
                    "مناسبت‌ها به تفکیک ماه مرتب شده‌اند. تعطیلات رسمی با رنگ قرمز مشخص می‌شوند.", officialMonths));
                page.Children.Add(refresh);
                page.Children.Add(Card("مناسبت‌های روز انتخاب‌شده", "مناسبت‌های مربوط به روز انتخاب‌شده در تقویم.", selectedDayOccasions));
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
                    try { await RenderOfficialMonths(); }
                    catch
                    {
                        officialMonths.Children.Clear();
                        officialMonths.Children.Add(new TextBlock { Text = "برای دریافت تقویم رسمی، دکمه به‌روزرسانی را بزنید.", Opacity = 0.65 });
                    }
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
                var actions = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal };
                var refresh = MakeButton("تازه‌سازی فعالیت‌ها");
                var clear = MakeButton("پاکسازی تاریخچه فعالیت");
                actions.Children.Add(refresh);
                actions.Children.Add(clear);

                var categoryGrid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
                categoryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                categoryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (var i = 0; i < 3; i++)
                    categoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var history = new StackPanel { Spacing = 8 };
                var summary = new TextBlock
                {
                    Text = "یک دسته را انتخاب کنید تا سوابق آن نمایش داده شود.",
                    Foreground = ThemeService.Brush(_theme.SecondaryText),
                    TextWrapping = TextWrapping.Wrap
                };
                var allItems = new List<ActivityLogEntry>();
                var categories = new (string Key, string Title, string Glyph, Func<ActivityLogEntry, bool> Match)[]
                {
                    ("all", "همه فعالیت‌ها", "▦", _ => true),
                    ("planning", "برنامه و کارها", "✓", x => x.Action is "event-created" or "task-created" or "task-completed" or "task-reopened"),
                    ("notes", "یادداشت‌ها", "▤", x => x.Action == "note-saved"),
                    ("reminders", "یادآورها و اعلان‌ها", "◷", x => x.Action.StartsWith("reminder", StringComparison.Ordinal) || x.Action == "notification-fired"),
                    ("occasions", "مناسبت‌ها", "✦", x => x.Action == "special-occasion-created"),
                    ("sync", "همگام‌سازی", "⟳", x => x.Action.StartsWith("occasion-sync", StringComparison.Ordinal))
                };

                void ShowCategory(string key)
                {
                    var selected = categories.First(x => x.Key == key);
                    var filtered = allItems.Where(selected.Match).ToList();
                    summary.Text = $"{selected.Title} · {ToPersianDigits(filtered.Count.ToString())} مورد";
                    history.Children.Clear();
                    if (filtered.Count == 0)
                    {
                        history.Children.Add(new TextBlock
                        {
                            Text = "برای این دسته فعالیتی ثبت نشده است.",
                            Opacity = 0.6,
                            Padding = new Thickness(8)
                        });
                        return;
                    }
                    foreach (var item in filtered)
                    {
                        var local = item.CreatedAt.ToLocalTime();
                        var date = PersianDate.FromDateOnly(DateOnly.FromDateTime(local.DateTime));
                        var title = item.Action switch
                        {
                            "event-created" => "رویداد جدید",
                            "task-created" => "کار جدید",
                            "task-completed" => "کار انجام شد",
                            "task-reopened" => "کار دوباره باز شد",
                            "note-saved" => "یادداشت ذخیره شد",
                            "special-occasion-created" => "مناسبت شخصی",
                            "notification-fired" => "اعلان نمایش داده شد",
                            "reminder-acknowledged" => "یادآور تأیید شد",
                            "reminder-completed" => "یادآور انجام شد",
                            "reminder-snoozed" => "یادآور به تعویق افتاد",
                            "reminder-dismissed" => "یادآور رد شد",
                            "occasion-sync" => "همگام‌سازی مناسبت‌ها",
                            "occasion-sync-failed" => "خطای همگام‌سازی",
                            _ => item.Action
                        };
                        var detail = new StackPanel { Spacing = 4, Padding = new Thickness(12) };
                        detail.Children.Add(new TextBlock
                        {
                            Text = title,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = ThemeService.Brush(_theme.PrimaryText)
                        });
                        detail.Children.Add(new TextBlock
                        {
                            Text = $"{FormatPersianActivityTimestamp(date, local.TimeOfDay)} · {item.Message}",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = ThemeService.Brush(_theme.SecondaryText)
                        });
                        history.Children.Add(new Border
                        {
                            Background = CreateSideSurfaceBrush(),
                            BorderBrush = BrushWithAlpha(_theme.Accent, 0x50),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(12),
                            Child = detail
                        });
                    }
                }

                async Task LoadActivityTiles()
                {
                    allItems = (await _repository.GetRecentActivitiesAsync(500))
                        .OrderByDescending(x => x.CreatedAt)
                        .ToList();
                    categoryGrid.Children.Clear();
                    for (var i = 0; i < categories.Length; i++)
                    {
                        var category = categories[i];
                        var count = allItems.Count(category.Match);
                        var tileContent = new StackPanel
                        {
                            Spacing = 6,
                            Padding = new Thickness(12),
                            HorizontalAlignment = HorizontalAlignment.Center
                        };
                        tileContent.Children.Add(new TextBlock
                        {
                            Text = category.Glyph,
                            FontSize = 23,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Foreground = ThemeService.Brush(_theme.Accent)
                        });
                        tileContent.Children.Add(new TextBlock
                        {
                            Text = category.Title,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Foreground = ThemeService.Brush(_theme.PrimaryText)
                        });
                        tileContent.Children.Add(new TextBlock
                        {
                            Text = ToPersianDigits(count.ToString()) + " مورد",
                            FontSize = 11,
                            Foreground = ThemeService.Brush(_theme.SecondaryText),
                            HorizontalAlignment = HorizontalAlignment.Center
                        });
                        var tile = new Button
                        {
                            Content = new Border
                            {
                                Background = CreateSideSurfaceBrush(),
                                BorderBrush = BrushWithAlpha(_theme.Accent, 0x65),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(13),
                                Child = tileContent
                            },
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            Padding = new Thickness(0),
                            Background = BrushWithAlpha("#00FFFFFF", 0),
                            BorderThickness = new Thickness(0)
                        };
                        tile.Click += (_, _) => ShowCategory(category.Key);
                        Grid.SetColumn(tile, i % 3);
                        Grid.SetRow(tile, i / 3);
                        categoryGrid.Children.Add(tile);
                    }
                    ShowCategory("all");
                }

                refresh.Click += async (_, _) => await LoadActivityTiles();
                clear.Click += async (_, _) =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "پاکسازی مرکز فعالیت",
                        Content = "فقط تاریخچه فعالیت‌ها پاک می‌شود. یادداشت‌ها، رویدادها، کارها، مناسبت‌ها و یادآورها حذف نمی‌شوند.",
                        PrimaryButtonText = "پاکسازی",
                        CloseButtonText = "انصراف",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = (Content as FrameworkElement)?.XamlRoot
                    };
                    if (dialog.XamlRoot is null) return;
                    var result = await dialog.ShowAsync();
                    if (result != ContentDialogResult.Primary) return;
                    await _repository.ClearActivitiesAsync();
                    await LoadActivityTiles();
                    StatusText.Text = "تاریخچه مرکز فعالیت پاک شد.";
                };
                page.Children.Add(Card("دسته‌بندی فعالیت‌ها", "برای مشاهده تاریخچه، یکی از کاشی‌های زیر را انتخاب کنید.", categoryGrid));
                page.Children.Add(Card("سوابق فعالیت", "", new StackPanel { Spacing = 8, Children = { summary, history } }));
                page.Children.Add(actions);
                _ = LoadActivityTiles();
                break;
            }
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
