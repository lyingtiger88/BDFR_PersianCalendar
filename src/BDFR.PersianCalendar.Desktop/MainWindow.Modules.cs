using System.Text.Json;
using BDFR.PersianCalendar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

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

    private FrameworkElement BuildWallpaperManagerPage()
    {
        var root = new StackPanel { Spacing = 14, Padding = new Thickness(18), FlowDirection = FlowDirection.RightToLeft };
        var intro = new StackPanel { Spacing = 5 };
        intro.Children.Add(new TextBlock { Text = "Wallpaper Manager", FontSize = 25, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = ThemeService.Brush(_theme.PrimaryText) });
        intro.Children.Add(new TextBlock
        {
            Text = "جست‌وجو، دریافت و دسته‌بندی کاغذدیواری‌های باکیفیت متناسب با وضوح نمایشگر",
            TextWrapping = TextWrapping.Wrap, Foreground = ThemeService.Brush(_theme.SecondaryText)
        });
        root.Children.Add(Card("مدیریت کاغذدیواری", "", intro));

        var search = new StackPanel { Spacing = 8 };
        var query = new TextBox { Header = "عبارت جست‌وجو", Text = "nature landscape", PlaceholderText = "مثلاً forest, space, architecture" };
        var category = new ComboBox { Header = "دسته‌بندی منبع", HorizontalAlignment = HorizontalAlignment.Stretch };
        category.Items.Add(new ComboBoxItem { Content = "عمومی", Tag = "general" });
        category.Items.Add(new ComboBoxItem { Content = "انیمه", Tag = "anime" });
        category.Items.Add(new ComboBoxItem { Content = "افراد", Tag = "people" });
        category.Items.Add(new ComboBoxItem { Content = "همه دسته‌ها", Tag = "all" });
        category.SelectedIndex = 0;
        var resolution = new TextBlock { Text = "وضوح موردنیاز: در حال تشخیص نمایشگر...", Foreground = ThemeService.Brush(_theme.SecondaryText) };
        var searchButton = MakeButton("جست‌وجوی کاغذدیواری");
        var resultSummary = new TextBlock { Text = "نتایج جست‌وجو اینجا نمایش داده می‌شوند.", Foreground = ThemeService.Brush(_theme.SecondaryText) };
        var results = new StackPanel { Spacing = 10 };
        var gallery = new StackPanel { Spacing = 8 };
        var gallerySummary = new TextBlock { Foreground = ThemeService.Brush(_theme.SecondaryText) };

        var (screenWidth, screenHeight) = GetPrimaryDisplayResolution();
        resolution.Text = $"حداقل وضوح جست‌وجو: {screenWidth} × {screenHeight} پیکسل";
        searchButton.Click += async (_, _) =>
        {
            searchButton.IsEnabled = false;
            resultSummary.Text = "در حال دریافت نتایج از Wallhaven (فقط محتوای SFW)...";
            results.Children.Clear();
            try
            {
                var categoryId = (category.SelectedItem as ComboBoxItem)?.Tag as string ?? "general";
                var items = await _wallpaperService.SearchAsync(query.Text, categoryId, screenWidth, screenHeight);
                resultSummary.Text = items.Count == 0
                    ? "نتیجه‌ای با وضوح کافی پیدا نشد. عبارت یا دسته‌بندی را تغییر دهید."
                    : $"{items.Count} تصویر سازگار با وضوح نمایشگر پیدا شد.";
                foreach (var item in items)
                {
                    var cardBody = new StackPanel { Spacing = 6 };
                    var preview = new Image { Height = 190, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Stretch };
                    if (Uri.TryCreate(item.ThumbnailUrl, UriKind.Absolute, out var thumbUri))
                    {
                        var bitmap = new BitmapImage { UriSource = thumbUri, DecodePixelWidth = 720 };
                        preview.Source = bitmap;
                    }
                    cardBody.Children.Add(preview);
                    cardBody.Children.Add(new TextBlock
                    {
                        Text = $"{item.Title} · {item.Width}×{item.Height} · {item.Category}",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = ThemeService.Brush(_theme.PrimaryText)
                    });
                    cardBody.Children.Add(new TextBlock
                    {
                        Text = string.Join("، ", item.Tags.Take(6)),
                        FontSize = 11, TextWrapping = TextWrapping.Wrap,
                        Foreground = ThemeService.Brush(_theme.SecondaryText)
                    });
                    var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    var download = MakeButton("دانلود و اعمال");
                    download.Click += async (_, _) =>
                    {
                        download.IsEnabled = false;
                        try
                        {
                            var path = await _wallpaperService.DownloadAsync(item);
                            _settings.CustomBackgroundPath = path;
                            _settings.BackgroundMode = "custom";
                            _settings.WallpaperMode = "manual";
                            _settingsService.Save(_settings);
                            await ApplyBackgroundAsync();
                            StatusText.Text = $"کاغذدیواری دریافت و اعمال شد · {item.Width}×{item.Height}";
                            await LoadGalleryAsync();
                        }
                        catch (Exception ex)
                        {
                            StatusText.Text = "دریافت تصویر انجام نشد: " + ex.Message;
                            StartupDiagnostics.Log("Wallpaper download failed: " + ex);
                        }
                        finally { download.IsEnabled = true; }
                    };
                    var openSource = MakeButton("صفحه منبع");
                    openSource.Click += (_, _) =>
                    {
                        try { Process.Start(new ProcessStartInfo(item.PageUrl) { UseShellExecute = true }); }
                        catch (Exception ex) { StartupDiagnostics.Log("Wallpaper source open failed: " + ex.Message); }
                    };
                    actions.Children.Add(download); actions.Children.Add(openSource);
                    cardBody.Children.Add(actions);
                    results.Children.Add(Card(item.Id, "منبع: Wallhaven · فقط نسخه عمومی و مجاز برای جست‌وجوی مهمان", cardBody));
                }
            }
            catch (Exception ex)
            {
                resultSummary.Text = "ارتباط با منبع تصاویر برقرار نشد: " + ex.Message;
                StartupDiagnostics.Log("Wallpaper search failed: " + ex);
            }
            finally { searchButton.IsEnabled = true; }
        };
        search.Children.Add(query);
        search.Children.Add(category);
        search.Children.Add(resolution);
        search.Children.Add(searchButton);
        search.Children.Add(resultSummary);
        search.Children.Add(results);
        root.Children.Add(Card("جست‌وجوی آنلاین", "Wallhaven API · فیلتر محتوای SFW · حداقل وضوح برابر با وضوح نمایشگر", search));

        async Task LoadGalleryAsync()
        {
            gallery.Children.Clear();
            var files = _wallpaperService.GetGalleryFiles();
            gallerySummary.Text = $"{files.Count} تصویر در گالری محلی · مسیر: {_wallpaperService.GalleryRoot}";
            if (files.Count == 0)
            {
                gallery.Children.Add(new TextBlock { Text = "هنوز تصویری دانلود نشده است.", Opacity = 0.65 });
                return;
            }
            foreach (var path in files.Take(30))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Padding = new Thickness(8) };
                var image = new Image { Width = 110, Height = 70, Stretch = Stretch.UniformToFill };
                image.Source = await PictureService.LoadImageAsync(path, 220);
                row.Children.Add(image);
                var details = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                details.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                details.Children.Add(new TextBlock { Text = new DirectoryInfo(Path.GetDirectoryName(path)!).Name, FontSize = 11, Opacity = 0.65 });
                var apply = MakeButton("اعمال");
                apply.Click += async (_, _) =>
                {
                    _settings.CustomBackgroundPath = path;
                    _settings.BackgroundMode = "custom";
                    _settings.WallpaperMode = "manual";
                    _settingsService.Save(_settings);
                    await ApplyBackgroundAsync();
                    StatusText.Text = "تصویر انتخاب‌شده از گالری اعمال شد.";
                };
                details.Children.Add(apply);
                row.Children.Add(details);
                gallery.Children.Add(new Border
                {
                    BorderBrush = BrushWithAlpha(_theme.Accent, 0x70),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Child = row
                });
            }
        }

        var galleryRefresh = MakeButton("تازه‌سازی گالری");
        galleryRefresh.Click += async (_, _) => await LoadGalleryAsync();
        gallery.Children.Add(galleryRefresh);
        root.Children.Add(Card("گالری محلی", "تصاویر دانلودشده در پوشه‌های دسته‌بندی‌شده نگهداری می‌شوند؛ برای هر تصویر اطلاعات منبع نیز ذخیره می‌شود.", new StackPanel
        {
            Spacing = 8,
            Children = { gallerySummary, gallery }
        }));
        _ = LoadGalleryAsync();

        var openGallery = MakeButton("بازکردن پوشه گالری در ویندوز");
        openGallery.Click += (_, _) => OpenFolderInExplorer(_wallpaperService.GalleryRoot);
        root.Children.Add(openGallery);

        var scroll = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FlowDirection = FlowDirection.RightToLeft
        };
        return scroll;
    }

    private (int Width, int Height) GetPrimaryDisplayResolution()
    {
        try
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (display is not null)
                return (Math.Max(800, display.WorkArea.Width), Math.Max(600, display.WorkArea.Height));
        }
        catch (Exception ex) { StartupDiagnostics.Log("Display resolution lookup failed: " + ex.Message); }
        return (Math.Max(1280, AppWindow.Size.Width), Math.Max(720, AppWindow.Size.Height));
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
        if (module == "Wallpaper Manager")
            return BuildWallpaperManagerPage();
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
                        {
                            var row = new StackPanel { Spacing = 6 };
                            row.Children.Add(new TextBlock { Text = $"◷ {ev.Title} · {(ev.StartTime?.ToString("HH:mm") ?? "تمام‌روز")}", TextWrapping = TextWrapping.Wrap });
                            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                            var edit = MakeButton("ویرایش");
                            edit.Click += async (_, _) => { await EditEventAsync(ev); await LoadWeek(); };
                            var delete = MakeButton("حذف");
                            delete.Click += async (_, _) => { await DeleteEventAsync(ev); await LoadWeek(); };
                            actions.Children.Add(edit); actions.Children.Add(delete); row.Children.Add(actions);
                            day.Children.Add(row);
                        }
                        foreach (var task in snapshot.Tasks)
                        {
                            var row = new StackPanel { Spacing = 5 };
                            var check = new CheckBox { Content = task.Title, IsChecked = task.Completed };
                            check.Checked += async (_, _) => { await _repository.SetTaskCompletedAsync(task.Id, true); await LoadWeek(); };
                            check.Unchecked += async (_, _) => { await _repository.SetTaskCompletedAsync(task.Id, false); await LoadWeek(); };
                            row.Children.Add(check);
                            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                            var edit = MakeButton("ویرایش");
                            edit.Click += async (_, _) => { await EditTaskAsync(task); await LoadWeek(); };
                            var delete = MakeButton("حذف");
                            delete.Click += async (_, _) => { await DeleteTaskAsync(task); await LoadWeek(); };
                            actions.Children.Add(edit); actions.Children.Add(delete); row.Children.Add(actions);
                            day.Children.Add(row);
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
                    var personalEntries = await _repository.GetSpecialOccasionsAsync();
                    var yearStart = new PersianDate(_selected.Year, 1, 1).ToDateOnly();
                    var gregorianYear = yearStart.Year;
                    var hijriYear = new System.Globalization.HijriCalendar().GetYear(yearStart.ToDateTime(TimeOnly.MinValue));
                    var personalDates = personalEntries.Select(item =>
                    {
                        try
                        {
                            var date = item.CalendarSystem switch
                            {
                                CalendarSystemKind.Persian => new PersianDate(_selected.Year, item.Month,
                                    Math.Min(item.Day, PersianDate.DaysInMonth(_selected.Year, item.Month))).ToDateOnly(),
                                CalendarSystemKind.Gregorian => new DateOnly(gregorianYear, item.Month,
                                    Math.Min(item.Day, DateTime.DaysInMonth(gregorianYear, item.Month))),
                                CalendarSystemKind.Hijri => DateOnly.FromDateTime(new System.Globalization.HijriCalendar().ToDateTime(
                                    hijriYear, item.Month, Math.Min(item.Day, new System.Globalization.HijriCalendar().GetDaysInMonth(hijriYear, item.Month)),
                                    0, 0, 0, 0)),
                                _ => yearStart
                            };
                            var converted = CalendarDateConverter.Convert(date).Persian;
                            return (Item: item, Date: converted);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            return (Item: item, Date: default(PersianDate));
                        }
                    }).Where(x => x.Date.Year == _selected.Year).ToList();
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

                        // Personal annual occasions are merged into the same month
                        // timeline, while retaining a distinct gold identity.
                        foreach (var personalItem in personalDates
                            .Where(x => x.Date.Month == month)
                            .OrderBy(x => x.Date.Day))
                        {
                            var personalRow = new Grid { ColumnSpacing = 10, FlowDirection = FlowDirection.RightToLeft };
                            personalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            personalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            personalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            var dayBadge = new Border
                            {
                                MinWidth = 42, Padding = new Thickness(8, 5, 8, 5),
                                CornerRadius = new CornerRadius(8),
                                Background = BrushWithAlpha("#FFFFC857", 0x30),
                                Child = new TextBlock
                                {
                                    Text = ToPersianDigits(personalItem.Date.Day.ToString()),
                                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                                    Foreground = ThemeService.Brush("#FF9A6700"),
                                    HorizontalAlignment = HorizontalAlignment.Center
                                }
                            };
                            Grid.SetColumn(dayBadge, 0);
                            personalRow.Children.Add(dayBadge);
                            var personalTitle = new TextBlock
                            {
                                Text = personalItem.Item.Title,
                                TextWrapping = TextWrapping.Wrap,
                                VerticalAlignment = VerticalAlignment.Center,
                                Foreground = ThemeService.Brush(_theme.PrimaryText),
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                            };
                            Grid.SetColumn(personalTitle, 1);
                            personalRow.Children.Add(personalTitle);
                            var personalMarker = new Border
                            {
                                Padding = new Thickness(8, 4, 8, 4),
                                CornerRadius = new CornerRadius(10),
                                Background = BrushWithAlpha("#FFFFC857", 0x35),
                                VerticalAlignment = VerticalAlignment.Center,
                                Child = new TextBlock
                                {
                                    Text = "✦ مناسبت شخصی",
                                    FontSize = 11,
                                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                    Foreground = ThemeService.Brush("#FF9A6700")
                                }
                            };
                            Grid.SetColumn(personalMarker, 2);
                            personalRow.Children.Add(personalMarker);
                            var personalCard = new Border
                            {
                                BorderThickness = new Thickness(1.5),
                                BorderBrush = ThemeService.Brush("#FFD6A63B"),
                                Background = BrushWithAlpha("#FFFFD76A", 0x0C),
                                CornerRadius = new CornerRadius(10),
                                Padding = new Thickness(5),
                                Child = personalRow
                            };
                            monthBody.Children.Add(personalCard);
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
                        var monthHeader = new StackPanel { Spacing = 7 };
                        monthHeader.Children.Add(new TextBlock
                        {
                            Text = months[month - 1],
                            FontSize = 17,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = ThemeService.Brush(_theme.PrimaryText)
                        });
                        // Artwork sits in its own row between month title and event list.
                        // It never overlays or obscures occasion descriptions.
                        var monthArtworkSource = await ZodiacArtwork.LoadAsync(month, _settings.ZodiacThemeId);
                        if (monthArtworkSource is not null)
                        {
                            monthHeader.Children.Add(new Image
                            {
                                Source = monthArtworkSource,
                                Width = 104,
                                Height = 104,
                                Stretch = Stretch.Uniform,
                                HorizontalAlignment = HorizontalAlignment.Right
                            });
                        }
                        monthSection.Children.Add(monthHeader);
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
                        await LoadPersonal();
                        StatusText.Text = $"مناسبت‌های سال {ToPersianDigits(_selected.Year.ToString())} به‌روزرسانی شد.";
                    }
                    catch (Exception ex) { StatusText.Text = "به‌روزرسانی مناسبت‌ها انجام نشد: " + ex.Message; }
                    finally { refresh.IsEnabled = true; }
                };

                var zodiacOptions = new (string Id, string Title, string Glyph)[]
                {
                    ("cosmic", "کیهانی کلاسیک", "✦"), ("neon", "فانتزی نورانی", "☄"),
                    ("minimal", "مینیمال مدرن", "☾"), ("watercolor", "آبرنگی هنری", "✿"),
                    ("mythic", "اسطوره‌ای", "♜"), ("cute", "کارتونی کیوت", "☺"),
                    ("luxury", "سه‌بعدی لوکس", "✧")
                };
                var selectedZodiac = zodiacOptions.FirstOrDefault(x => x.Id == _settings.ZodiacThemeId);
                if (string.IsNullOrWhiteSpace(selectedZodiac.Id)) selectedZodiac = zodiacOptions[0];

                var zodiacBannerBody = new Grid { ColumnSpacing = 12, Padding = new Thickness(18), FlowDirection = FlowDirection.RightToLeft };
                zodiacBannerBody.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                zodiacBannerBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var zodiacHeroImage = new Image
                {
                    Width = 104, Height = 104,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _ = ZodiacArtwork.PopulateAsync(zodiacHeroImage, _selected.Month, _settings.ZodiacThemeId);
                Grid.SetColumn(zodiacHeroImage, 0);
                zodiacBannerBody.Children.Add(zodiacHeroImage);
                var zodiacText = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
                zodiacText.Children.Add(new TextBlock
                {
                    Text = "مناسبت‌ها",
                    FontSize = 23,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = ThemeService.Brush("#FFFFFFFF")
                });
                zodiacText.Children.Add(new TextBlock
                {
                    Text = "تقویم رسمی و رویدادهای سال",
                    Foreground = ThemeService.Brush("#FFF0E9FF")
                });
                zodiacText.Children.Add(new TextBlock
                {
                    Text = "نمادهای فلکی و حال‌وهوای کیهانی زروان",
                    FontSize = 12,
                    Foreground = ThemeService.Brush("#FFD8CCF5")
                });
                Grid.SetColumn(zodiacText, 1);
                zodiacBannerBody.Children.Add(zodiacText);
                var zodiacPalette = selectedZodiac.Id switch
                {
                    "neon" => ("#FF190B42", "#FF6525A8"),
                    "minimal" => ("#FF171A24", "#FF414655"),
                    "watercolor" => ("#FF60436E", "#FFB879A0"),
                    "mythic" => ("#FF38200D", "#FF93602C"),
                    "cute" => ("#FF6D3157", "#FFC879A8"),
                    "luxury" => ("#FF102536", "#FF53647E"),
                    _ => ("#FF100D35", "#FF39236F")
                };
                var zodiacBanner = new Border
                {
                    CornerRadius = new CornerRadius(16),
                    BorderThickness = new Thickness(1),
                    BorderBrush = BrushWithAlpha(_theme.Accent, 0xB0),
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new Windows.Foundation.Point(0, 0),
                        EndPoint = new Windows.Foundation.Point(1, 1),
                        GradientStops =
                        {
                            new GradientStop { Color = Windows.UI.Color.FromArgb(255,
                                Convert.ToByte(zodiacPalette.Item1.Substring(3, 2), 16),
                                Convert.ToByte(zodiacPalette.Item1.Substring(5, 2), 16),
                                Convert.ToByte(zodiacPalette.Item1.Substring(7, 2), 16)), Offset = 0 },
                            new GradientStop { Color = Windows.UI.Color.FromArgb(255,
                                Convert.ToByte(zodiacPalette.Item2.Substring(3, 2), 16),
                                Convert.ToByte(zodiacPalette.Item2.Substring(5, 2), 16),
                                Convert.ToByte(zodiacPalette.Item2.Substring(7, 2), 16)), Offset = 1 }
                        }
                    },
                    Child = zodiacBannerBody
                };

                var selectedDayOccasions = OccasionsPanel;
                DetachDesktopElement(selectedDayOccasions);
                page.Children.Add(zodiacBanner);
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
                    {
                        var line = new StackPanel { Spacing = 4 };
                        line.Children.Add(new TextBlock
                        {
                            Text = "✦  " + item.Title,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = ThemeService.Brush("#FF9A6700")
                        });
                        line.Children.Add(new TextBlock
                        {
                            Text = $"{item.CalendarSystem} · {item.Month}/{item.Day} · یادآوری: {string.Join("، ", item.ReminderDaysBefore)} روز قبل",
                            Foreground = ThemeService.Brush(_theme.SecondaryText),
                            FontSize = 12
                        });
                        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
                        var edit = MakeButton("ویرایش");
                        edit.Click += async (_, _) => await EditSpecialOccasionAsync(item);
                        var delete = MakeButton("حذف");
                        delete.Click += async (_, _) => await DeleteSpecialOccasionAsync(item);
                        actions.Children.Add(edit); actions.Children.Add(delete);
                        line.Children.Add(actions);
                        personal.Children.Add(new Border
                        {
                            BorderThickness = new Thickness(1.5),
                            BorderBrush = ThemeService.Brush("#FFD6A63B"),
                            Background = BrushWithAlpha("#FFFFD76A", 0x0C),
                            CornerRadius = new CornerRadius(10),
                            Padding = new Thickness(10),
                            Child = line
                        });
                    }
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
                    ("planning", "برنامه و کارها", "✓", x => x.Action.StartsWith("event-", StringComparison.Ordinal) || x.Action.StartsWith("task-", StringComparison.Ordinal)),
                    ("notes", "یادداشت‌ها", "▤", x => x.Action.StartsWith("note-", StringComparison.Ordinal)),
                    ("reminders", "یادآورها و اعلان‌ها", "◷", x => x.Action.StartsWith("reminder", StringComparison.Ordinal) || x.Action == "notification-fired"),
                    ("occasions", "مناسبت‌ها", "✦", x => x.Action.StartsWith("special-occasion-", StringComparison.Ordinal)),
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
                            "event-updated" => "رویداد ویرایش شد",
                            "event-deleted" => "رویداد حذف شد",
                            "task-created" => "کار جدید",
                            "task-updated" => "کار ویرایش شد",
                            "task-deleted" => "کار حذف شد",
                            "task-completed" => "کار انجام شد",
                            "task-reopened" => "کار دوباره باز شد",
                            "note-saved" => "یادداشت ذخیره شد",
                            "note-deleted" => "یادداشت حذف شد",
                            "special-occasion-created" => "مناسبت شخصی",
                            "special-occasion-recovered" => "بازیابی سابقه مناسبت شخصی",
                            "special-occasion-updated" => "مناسبت شخصی ویرایش شد",
                            "special-occasion-deleted" => "مناسبت شخصی حذف شد",
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
                    allItems = (await _repository.GetRecentActivitiesAsync(5000))
                        .OrderByDescending(x => x.CreatedAt)
                        .ToList();

                    // Backfill legacy personal occasions created before activity
                    // logging was active. Use a stable item id check to avoid duplicates.
                    var personalItems = await _repository.GetSpecialOccasionsAsync();
                    var loggedIds = allItems
                        .Where(x => x.Action is "special-occasion-created" or "special-occasion-recovered")
                        .Select(x => x.ItemId)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var occasion in personalItems.Where(x => !loggedIds.Contains(x.Id)))
                    {
                        await _repository.AddActivityAsync(new ActivityLogEntry(
                            "legacy-special-" + occasion.Id,
                            DateTimeOffset.UtcNow,
                            "special-occasion-recovered",
                            CalendarItemKind.SpecialOccasion,
                            occasion.Id,
                            $"{occasion.Title} — سابقه مناسبت شخصی بازیابی شد"));
                    }
                    if (personalItems.Count > 0)
                    {
                        allItems = (await _repository.GetRecentActivitiesAsync(5000))
                            .OrderByDescending(x => x.CreatedAt)
                            .ToList();
                    }
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
