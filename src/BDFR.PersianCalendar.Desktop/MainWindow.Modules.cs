using System.Text.Json;
using System.Diagnostics;
using BDFR.PersianCalendar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private TextBlock? _deskStatus;
    private StackPanel? _deskFiles;
    private bool _deskRegisterMode;
    private string _collaborationGroupId = "";
    private long _collaborationCursor;
    private System.Threading.Timer? _collaborationTimer;
    private TextBlock? _collaborationStatus;
    private TextBlock? _collaborationDataStatus;
    private int _collaborationPollBusy;
    private readonly CollaborationOfflineQueue _collaborationOfflineQueue = new();
    private List<string> _schoolSubjectIds = new();
    private List<string> _schoolHomeworkIds = new();


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

    private void StartCollaborationPolling()
    {
        _collaborationTimer?.Dispose();
        if (!_deskClient.IsAuthenticated || string.IsNullOrWhiteSpace(_collaborationGroupId)) return;
        _collaborationTimer = new System.Threading.Timer(async _ =>
        {
            if (System.Threading.Interlocked.Exchange(ref _collaborationPollBusy, 1) != 0) return;
            try
            {
                foreach (var queued in _collaborationOfflineQueue.ReadAll().Where(x => x.GroupId == _collaborationGroupId && x.BlockedReason is null).Take(20))
                {
                    try
                    {
                        using var replay = await _deskClient.ReplayCollaborationCommandAsync(
                            queued.GroupId, queued.CommandId, queued.Kind, queued.Payload);
                        _collaborationOfflineQueue.Remove(queued.CommandId);
                    }
                    catch (Exception replayError)
                    {
                        if (replayError.Message.Contains("version_conflict", StringComparison.OrdinalIgnoreCase) ||
                            replayError.Message.Contains("command_denied", StringComparison.OrdinalIgnoreCase) ||
                            replayError.Message.Contains("command_rejected", StringComparison.OrdinalIgnoreCase))
                        {
                            _collaborationOfflineQueue.Block(queued.CommandId, replayError.Message);
                            _desktopModulePage?.DispatcherQueue.TryEnqueue(() =>
                            {
                                if (_collaborationStatus is not null)
                                    _collaborationStatus.Text = "فرمان آفلاین به دلیل تعارض نسخه یا مجوز متوقف شد و نیازمند بازبینی است.";
                            });
                        }
                        break;
                    }
                }
                using var result = await _deskClient.ReadCollaborationChangesAsync(
                    _collaborationGroupId, _collaborationCursor);
                var root = result.RootElement;
                var next = root.TryGetProperty("nextCursor", out var cursor) ? cursor.GetInt64() : _collaborationCursor;
                var count = root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array
                    ? events.GetArrayLength() : 0;
                if (next > _collaborationCursor) _collaborationCursor = next;
                using var items = await _deskClient.ReadCollaborationItemsAsync(_collaborationGroupId);
                using var messages = await _deskClient.ReadCollaborationMessagesAsync(_collaborationGroupId);
                var itemCount = items.RootElement.ValueKind == JsonValueKind.Array ? items.RootElement.GetArrayLength() : 0;
                var messageCount = messages.RootElement.ValueKind == JsonValueKind.Array ? messages.RootElement.GetArrayLength() : 0;
                _desktopModulePage?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_collaborationStatus is not null)
                        _collaborationStatus.Text = count > 0
                            ? $"همگام‌سازی آنلاین فعال • {count} تغییر جدید دریافت شد."
                            : "همگام‌سازی آنلاین فعال • آخرین وضعیت به‌روز است.";
                    if (_collaborationDataStatus is not null)
                        _collaborationDataStatus.Text = $"داده مشترک: {itemCount} کار/برنامه • {messageCount} پیام";
                });
            }
            catch
            {
                _desktopModulePage?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_collaborationStatus is not null)
                        _collaborationStatus.Text = "اتصال گروه موقتاً قطع است؛ تلاش مجدد انجام می‌شود.";
                });
            }
            finally { System.Threading.Interlocked.Exchange(ref _collaborationPollBusy, 0); }
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private void StopCollaborationPolling()
    {
        _collaborationTimer?.Dispose();
        _collaborationTimer = null;
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
        Border Card(string title, string description, FrameworkElement content)
        {
            var body = new StackPanel { Spacing = 10, Padding = new Thickness(16) };
            if (!string.IsNullOrWhiteSpace(title))
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
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        var root = new StackPanel { Spacing = 12, Padding = new Thickness(14, 18, 14, 22), FlowDirection = FlowDirection.RightToLeft };
        var (screenWidth, screenHeight) = GetPrimaryDisplayResolution();

        // Genre selector is intentionally placed at the top of the page, before
        // any results are requested. Search only downloads thumbnail previews.
        var genre = new ComboBox { Header = "ژانر تصاویر", HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 145 };
        var genres = new (string Label, string Query)[]
        {
            ("همه ژانرها", ""),
            ("طبیعت", "nature"),
            ("منظره", "landscape"),
            ("فضا و کیهان", "space OR galaxy OR nebula"),
            ("معماری", "architecture"),
            ("مینیمال", "minimalism"),
            ("هنر انتزاعی", "abstract"),
            ("خودرو", "cars OR automotive"),
            ("انیمه", "anime"),
            ("حیوانات", "animals OR wildlife"),
            ("افراد", "people OR portrait"),
            ("فانتزی", "fantasy"),
            ("بازی", "video games"),
            ("دریا و اقیانوس", "ocean OR sea"),
            ("شهر و شب", "cityscape OR night")
        };
        foreach (var item in genres) genre.Items.Add(new ComboBoxItem { Content = item.Label, Tag = item.Query });
        genre.SelectedIndex = 0;

        var contentCategory = new ComboBox { Header = "نوع محتوای منبع", HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 145 };
        contentCategory.Items.Add(new ComboBoxItem { Content = "عمومی", Tag = "general" });
        contentCategory.Items.Add(new ComboBoxItem { Content = "انیمه", Tag = "anime" });
        contentCategory.Items.Add(new ComboBoxItem { Content = "افراد", Tag = "people" });
        contentCategory.Items.Add(new ComboBoxItem { Content = "همه دسته‌ها", Tag = "all" });
        contentCategory.SelectedIndex = 0;

        var query = new TextBox { Header = "عبارت تکمیلی جست‌وجو (اختیاری)", PlaceholderText = "مثلاً forest, purple, cinematic", HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 180 };
        var resolution = new TextBlock
        {
            Text = $"وضوح نمایشگر: {screenWidth} × {screenHeight} پیکسل · دریافت اولیه فقط تصویر بندانگشتی",
            Foreground = ThemeService.Brush(_theme.SecondaryText),
            TextWrapping = TextWrapping.Wrap
        };
        var searchButton = MakeButton("جست‌وجوی کاغذدیواری");
        searchButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        searchButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        searchButton.Background = ThemeService.Brush("#FFC9C2FF");
        searchButton.Foreground = ThemeService.Brush("#FF302B4B");
        searchButton.BorderThickness = new Thickness(0);
        searchButton.MinHeight = 38;
        var resultSummary = new TextBlock { Text = "ابتدا ژانر تصویر را انتخاب و جست‌وجو کنید.", Foreground = ThemeService.Brush(_theme.SecondaryText) };
        var resultGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12, Padding = new Thickness(1, 2, 1, 10) };
        for (var i = 0; i < 4; i++)
            resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var resultScroll = new ScrollViewer
        {
            Content = resultGrid,
            Height = 590,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var currentPage = 0;
        var totalPages = 1;
        var loadingPage = false;
        var searchGeneration = 0;
        var gallery = new StackPanel { Spacing = 8 };
        var gallerySummary = new TextBlock { Foreground = ThemeService.Brush(_theme.SecondaryText) };
        var galleryCategoryPicker = new ComboBox { Header = "دسته‌بندی گالری", HorizontalAlignment = HorizontalAlignment.Stretch };
        galleryCategoryPicker.Items.Add(new ComboBoxItem { Content = "همه دسته‌ها", Tag = "" });
        foreach (var name in _wallpaperService.GetGalleryCategories())
            galleryCategoryPicker.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        galleryCategoryPicker.SelectedIndex = 0;
        var rebuildingGalleryCategories = false;
        var galleryRefresh = MakeButton("تازه‌سازی گالری");
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentQuery = "";
        var currentCategory = "general";

        async Task DownloadItemAsync(WallpaperItem item, bool apply, Button button)
        {
            button.IsEnabled = false;
            try
            {
                // The original-resolution file is fetched only after an explicit
                // Download or Download & Apply click.
                var path = await _wallpaperService.DownloadAsync(item);
                if (apply)
                {
                    _settings.CustomBackgroundPath = path;
                    _settings.BackgroundMode = "custom";
                    _settings.WallpaperMode = "manual";
                    _settings.AppearanceMode = "theme";
                    _settings.UseSeasonalBackground = false;
                    _settingsService.Save(_settings);
                    await ApplyBackgroundAsync();
                    StatusText.Text = $"کاغذدیواری دانلود و اعمال شد · {item.Width}×{item.Height}";
                }
                else
                {
                    StatusText.Text = $"تصویر در گالری ذخیره شد · {item.Width}×{item.Height}";
                }
                await LoadGalleryAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = "دریافت تصویر انجام نشد: " + ex.Message;
                StartupDiagnostics.Log("Wallpaper download failed: " + ex);
            }
            finally { button.IsEnabled = true; }
        }

        void AddWallpaperCard(WallpaperItem item, int index)
        {
            // Each result card owns its preview height. The image must not be
            // squeezed by the grid's auto-sized rows when the page is scrolled.
            var cardBody = new StackPanel { Spacing = 7, Padding = new Thickness(7, 7, 7, 8) };
            var preview = new Image
            {
                Height = 164,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (Uri.TryCreate(item.ThumbnailUrl, UriKind.Absolute, out var thumbUri))
                preview.Source = new BitmapImage { UriSource = thumbUri, DecodePixelWidth = 420 };

            cardBody.Children.Add(new Border
            {
                Height = 164,
                CornerRadius = new CornerRadius(8),
                Background = ThemeService.Brush("#FFF2EFF8"),
                Child = preview
            });
            cardBody.Children.Add(new TextBlock
            {
                Text = $"{item.Width}×{item.Height} · {item.Category}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ThemeService.Brush("#FF37435C")
            });
            var download = MakeButton("دانلود");
            var apply = MakeButton("دانلود و اعمال");
            foreach (var action in new[] { download, apply })
            {
                action.HorizontalAlignment = HorizontalAlignment.Stretch;
                action.HorizontalContentAlignment = HorizontalAlignment.Center;
                action.Background = ThemeService.Brush("#FFC9C2FF");
                action.Foreground = ThemeService.Brush("#FF302B4B");
                action.BorderThickness = new Thickness(0);
                action.MinHeight = 30;
                action.Padding = new Thickness(6, 4, 6, 4);
            }
            download.Click += async (_, _) => await DownloadItemAsync(item, false, download);
            apply.Click += async (_, _) => await DownloadItemAsync(item, true, apply);
            cardBody.Children.Add(download);
            cardBody.Children.Add(apply);
            var card = new Border
            {
                BorderBrush = ThemeService.Brush("#FFE2DDFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = cardBody,
                Background = ThemeService.Brush("#FFFFFFFF"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var row = index / 4;
            var column = index % 4;
            while (resultGrid.RowDefinitions.Count <= row)
                resultGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(card, row);
            Grid.SetColumn(card, column);
            resultGrid.Children.Add(card);
        }

        async Task LoadNextPageAsync(bool reset)
        {
            if (loadingPage) return;
            if (!reset && currentPage >= totalPages) return;
            loadingPage = true;
            var generation = searchGeneration;
            searchButton.IsEnabled = false;
            try
            {
                var nextPage = reset ? 1 : currentPage + 1;
                resultSummary.Text = reset
                    ? "در حال دریافت پیش‌نمایش‌های بندانگشتی..."
                    : $"در حال بارگذاری صفحه {nextPage}...";
                var result = await _wallpaperService.SearchPageAsync(
                    currentQuery, currentCategory, screenWidth, screenHeight, nextPage);
                if (generation != searchGeneration) return;
                if (reset)
                {
                    resultGrid.Children.Clear();
                    resultGrid.RowDefinitions.Clear();
                    seenIds.Clear();
                }
                foreach (var item in result.Items)
                {
                    if (!seenIds.Add(item.Id)) continue;
                    AddWallpaperCard(item, seenIds.Count - 1);
                }
                currentPage = result.CurrentPage;
                totalPages = Math.Max(result.TotalPages, currentPage);
                resultSummary.Text = seenIds.Count == 0
                    ? "نتیجه‌ای با وضوح کافی پیدا نشد. ژانر دیگری را انتخاب کنید."
                    : $"{seenIds.Count} پیش‌نمایش بارگذاری شده · صفحه {currentPage} از {totalPages} · برای دیدن موارد بیشتر به پایین بروید.";
                if (seenIds.Count == 0)
                    resultSummary.Text += " می‌توانید حداقل وضوح را در تنظیمات نمایشگر تغییر دهید.";
            }
            catch (Exception ex)
            {
                resultSummary.Text = "ارتباط با منبع تصاویر برقرار نشد: " + ex.Message;
                StartupDiagnostics.Log("Wallpaper search failed: " + ex);
            }
            finally
            {
                loadingPage = false;
                searchButton.IsEnabled = true;
            }
        }

        searchButton.Click += async (_, _) =>
        {
            searchGeneration++;
            currentPage = 0;
            totalPages = 1;
            var genreQuery = (genre.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            var extraQuery = query.Text?.Trim() ?? "";
            currentQuery = string.Join(" ", new[] { genreQuery, extraQuery }.Where(x => !string.IsNullOrWhiteSpace(x)));
            currentCategory = (contentCategory.SelectedItem as ComboBoxItem)?.Tag as string ?? "general";
            await LoadNextPageAsync(true);
        };

        // Infinite scrolling within the results branch. Each page appends to the
        // same 4-column grid; no giant originals are fetched during browsing.
        resultScroll.ViewChanged += async (_, _) =>
        {
            if (!loadingPage && currentPage > 0 && currentPage < totalPages &&
                resultScroll.VerticalOffset >= resultScroll.ScrollableHeight - 180)
                await LoadNextPageAsync(false);
        };

        var searchPanel = new StackPanel { Spacing = 10 };
        // Match the desktop reference: all three filters on one line, in RTL
        // order: genre (right), source category (middle), extra search (left).
        var filters = new Grid { ColumnSpacing = 12, FlowDirection = FlowDirection.RightToLeft };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        Grid.SetColumn(genre, 0);
        Grid.SetColumn(contentCategory, 1);
        Grid.SetColumn(query, 2);
        filters.Children.Add(genre);
        filters.Children.Add(contentCategory);
        filters.Children.Add(query);
        searchPanel.Children.Add(filters);
        resolution.TextAlignment = TextAlignment.Right;
        searchPanel.Children.Add(resolution);
        searchPanel.Children.Add(searchButton);
        resultSummary.TextAlignment = TextAlignment.Right;
        searchPanel.Children.Add(resultSummary);
        searchPanel.Children.Add(resultScroll);
        root.Children.Add(Card("دانلود کاغذدیواری", "ابتدا ژانر را از فهرست بالا انتخاب کنید. در مرحله مرور فقط تصاویر بندانگشتی دریافت می‌شوند؛ فایل اصلی تنها پس از انتخاب «دانلود» یا «دانلود و اعمال» دریافت خواهد شد.", searchPanel));

        galleryRefresh.Click += async (_, _) => await LoadGalleryAsync();
        galleryCategoryPicker.SelectionChanged += async (_, _) =>
        {
            if (!rebuildingGalleryCategories) await LoadGalleryAsync();
        };

        async Task LoadGalleryAsync()
        {
            gallery.Children.Clear();
            gallery.Children.Add(galleryRefresh);
            var selectedCategory = (galleryCategoryPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            var currentCategories = _wallpaperService.GetGalleryCategories();
            var previousCategory = selectedCategory;
            rebuildingGalleryCategories = true;
            galleryCategoryPicker.Items.Clear();
            galleryCategoryPicker.Items.Add(new ComboBoxItem { Content = "همه دسته‌ها", Tag = "" });
            foreach (var name in currentCategories)
                galleryCategoryPicker.Items.Add(new ComboBoxItem { Content = name, Tag = name });
            galleryCategoryPicker.SelectedItem = galleryCategoryPicker.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(x => string.Equals((string?)x.Tag, previousCategory, StringComparison.OrdinalIgnoreCase))
                ?? galleryCategoryPicker.Items.OfType<ComboBoxItem>().First();
            rebuildingGalleryCategories = false;
            selectedCategory = (galleryCategoryPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            var files = _wallpaperService.GetGalleryFiles(selectedCategory);
            gallerySummary.Text = $"{files.Count} تصویر در این دسته · مسیر: {_wallpaperService.GalleryRoot}";
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
                    _settings.AppearanceMode = "theme";
                    _settings.UseSeasonalBackground = false;
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

        root.Children.Add(Card("گالری محلی", "تصاویر دانلودشده در پوشه‌های دسته‌بندی‌شده نگهداری می‌شوند؛ برای هر تصویر اطلاعات منبع نیز ذخیره می‌شود.", new StackPanel
        {
            Spacing = 8,
            Children = { galleryCategoryPicker, gallerySummary, gallery }
        }));
        _ = LoadGalleryAsync();

        var openGallery = MakeButton("بازکردن پوشه گالری در ویندوز");
        openGallery.Click += (_, _) => OpenFolderInExplorer(_wallpaperService.GalleryRoot);
        root.Children.Add(openGallery);

        return new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FlowDirection = FlowDirection.RightToLeft
        };
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
                var collaboration = new StackPanel { Spacing = 8 };
                var groupNameBox = new TextBox { Header = "نام گروه آنلاین", PlaceholderText = "نام گروه" };
                var groupIdBox = new TextBox { Header = "شناسه گروه", PlaceholderText = "پس از ساخت گروه خودکار تکمیل می‌شود" };
                var inviteIdBox = new TextBox { Header = "شناسه زروان عضو", PlaceholderText = "شناسه حساب عضو" };
                var agendaMode = new CheckBox { Content = "Agenda Mode", IsChecked = true };
                var kanbanMode = new CheckBox { Content = "Kanban Mode", IsChecked = true };
                var schoolMode = new CheckBox { Content = "School Mode", IsChecked = false };
                var assistantMode = new CheckBox { Content = "دعوت به‌عنوان دستیار مدیر" };
                _collaborationStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
                _collaborationDataStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
                var createGroup = MakeButton("ساخت گروه آنلاین");
                createGroup.Click += async (_, _) =>
                {
                    if (!_deskClient.IsAuthenticated) { _collaborationStatus.Text = "ابتدا در Zurvan Desk وارد شوید."; return; }
                    if (string.IsNullOrWhiteSpace(groupNameBox.Text) ||
                        !(agendaMode.IsChecked == true || kanbanMode.IsChecked == true || schoolMode.IsChecked == true))
                    { _collaborationStatus.Text = "نام گروه و حداقل یک حالت کاری لازم است."; return; }
                    createGroup.IsEnabled = false;
                    try
                    {
                        using var result = await _deskClient.CreateCollaborationGroupAsync(
                            groupNameBox.Text.Trim(), agendaMode.IsChecked == true,
                            kanbanMode.IsChecked == true, schoolMode.IsChecked == true);
                        _collaborationGroupId = result.RootElement.GetProperty("id").GetString() ?? "";
                        groupIdBox.Text = _collaborationGroupId;
                        _collaborationCursor = 0;
                        _collaborationStatus.Text = "گروه آنلاین ساخته شد؛ همگام‌سازی هر ۳ ثانیه انجام می‌شود.";
                        StartCollaborationPolling();
                    }
                    catch (Exception ex) { _collaborationStatus.Text = ex.Message; }
                    finally { createGroup.IsEnabled = true; }
                };
                var inviteMember = MakeButton("ارسال دعوت");
                inviteMember.Click += async (_, _) =>
                {
                    if (!_deskClient.IsAuthenticated || string.IsNullOrWhiteSpace(_collaborationGroupId) ||
                        string.IsNullOrWhiteSpace(inviteIdBox.Text))
                    { _collaborationStatus.Text = "ابتدا وارد حساب شوید، گروه را انتخاب کنید و شناسه عضو را بنویسید."; return; }
                    inviteMember.IsEnabled = false;
                    try
                    {
                        await _deskClient.InviteCollaborationMemberAsync(
                            _collaborationGroupId, inviteIdBox.Text.Trim(), assistantMode.IsChecked == true);
                        _collaborationStatus.Text = "دعوت برای حساب زروان ارسال شد؛ عضو باید آن را بپذیرد.";
                        inviteIdBox.Text = "";
                    }
                    catch (Exception ex) { _collaborationStatus.Text = ex.Message; }
                    finally { inviteMember.IsEnabled = true; }
                };
                var loadMyGroups = MakeButton("نمایش گروه‌های من");
                loadMyGroups.Click += async (_, _) =>
                {
                    try
                    {
                        using var groups = await _deskClient.ListCollaborationGroupsAsync();
                        var rows = groups.RootElement.EnumerateArray().ToList();
                        if (rows.Count == 0) { _collaborationStatus!.Text = "هنوز عضو هیچ گروهی نیستید."; return; }
                        var choices = rows.Select(x => $"{x.GetProperty("name").GetString()} · {x.GetProperty("id").GetString()}").ToArray();
                        var picker = new ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 320 };
                        var dialog = new ContentDialog
                        {
                            XamlRoot = _desktopModulePage!.XamlRoot,
                            Title = "انتخاب گروه",
                            Content = picker,
                            PrimaryButtonText = "اتصال",
                            CloseButtonText = "انصراف"
                        };
                        if (await dialog.ShowAsync() == ContentDialogResult.Primary && picker.SelectedIndex >= 0)
                        {
                            _collaborationGroupId = rows[picker.SelectedIndex].GetProperty("id").GetString() ?? "";
                            groupIdBox.Text = _collaborationGroupId;
                            _collaborationCursor = 0;
                            StartCollaborationPolling();
                            _collaborationStatus!.Text = "گروه انتخاب شد؛ همگام‌سازی آنلاین فعال است.";
                        }
                    }
                    catch (Exception ex) { _collaborationStatus!.Text = ex.Message; }
                };
                collaboration.Children.Add(loadMyGroups);
                var connectGroup = MakeButton("اتصال به گروه و شروع همگام‌سازی");
                connectGroup.Click += async (_, _) =>
                {
                    _collaborationGroupId = groupIdBox.Text.Trim();
                    if (!_deskClient.IsAuthenticated || string.IsNullOrWhiteSpace(_collaborationGroupId))
                    { _collaborationStatus.Text = "ابتدا وارد حساب شوید و شناسه گروه را وارد کنید."; return; }
                    try
                    {
                        using var group = await _deskClient.ReadCollaborationGroupAsync(_collaborationGroupId);
                        _collaborationStatus.Text = "اتصال گروه تأیید شد؛ همگام‌سازی آنلاین فعال است.";
                        StartCollaborationPolling();
                    }
                    catch (Exception ex) { _collaborationStatus.Text = ex.Message; }
                };
                var memberWorkspace = new StackPanel { Spacing = 6 };
                var memberPicker = new ComboBox { Header = "عضو گروه", MinWidth = 260 };
                var memberIds = new List<string>();
                var memberPermissions = new List<(bool Chat, bool Report, bool Edit)>();
                var memberChat = new CheckBox { Content = "اجازه گفت‌وگو", IsChecked = true };
                var memberReport = new CheckBox { Content = "اجازه ثبت گزارش", IsChecked = true };
                var memberEdit = new CheckBox { Content = "اجازه ویرایش", IsChecked = false };
                var memberStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
                memberPicker.SelectionChanged += (_, _) =>
                {
                    var index = memberPicker.SelectedIndex;
                    if (index < 0 || index >= memberPermissions.Count) return;
                    memberChat.IsChecked = memberPermissions[index].Chat;
                    memberReport.IsChecked = memberPermissions[index].Report;
                    memberEdit.IsChecked = memberPermissions[index].Edit;
                };
                async Task RefreshMembers()
                {
                    using var detail = await _deskClient.ReadCollaborationGroupAsync(_collaborationGroupId);
                    var rows = detail.RootElement.GetProperty("members").EnumerateArray().ToList();
                    memberIds = rows.Select(x => x.GetProperty("account_id").GetString() ?? "").ToList();
                    memberPermissions = rows.Select(x => (x.GetProperty("can_chat").GetBoolean(), x.GetProperty("can_report").GetBoolean(), x.GetProperty("can_edit").GetBoolean())).ToList();
                    memberPicker.ItemsSource = rows.Select(x =>
                    {
                        var account = x.TryGetProperty("account", out var a) ? a : default;
                        var identifier = account.ValueKind == JsonValueKind.Object && account.TryGetProperty("identifier", out var ident)
                            ? ident.GetString() : x.GetProperty("account_id").GetString();
                        return $"{identifier} · {x.GetProperty("role").GetString()}";
                    }).ToArray();
                    memberPicker.SelectedIndex = rows.Count > 0 ? 0 : -1;
                    memberStatus.Text = $"{rows.Count} عضو در گروه";
                }
                var refreshMembers = MakeButton("بارگذاری اعضا و مجوزها");
                refreshMembers.Click += async (_, _) => { try { await RefreshMembers(); } catch (Exception ex) { memberStatus.Text = ex.Message; } };
                var saveMemberPermissions = MakeButton("ذخیره مجوز عضو انتخابی");
                saveMemberPermissions.Click += async (_, _) =>
                {
                    try
                    {
                        var i = memberPicker.SelectedIndex;
                        if (i < 0 || i >= memberIds.Count) return;
                        await _deskClient.UpdateCollaborationPermissionsAsync(_collaborationGroupId, memberIds[i],
                            memberChat.IsChecked == true, memberReport.IsChecked == true, memberEdit.IsChecked == true);
                        memberStatus.Text = "مجوزهای عضو ذخیره شد.";
                    }
                    catch (Exception ex) { memberStatus.Text = ex.Message; }
                };
                var removeMember = MakeButton("حذف عضو انتخابی");
                removeMember.Click += async (_, _) =>
                {
                    try
                    {
                        var i = memberPicker.SelectedIndex;
                        if (i < 0 || i >= memberIds.Count) return;
                        await _deskClient.RemoveCollaborationMemberAsync(_collaborationGroupId, memberIds[i]);
                        await RefreshMembers();
                        memberStatus.Text = "عضو از گروه حذف شد.";
                    }
                    catch (Exception ex) { memberStatus.Text = ex.Message; }
                };
                memberWorkspace.Children.Add(refreshMembers);
                memberWorkspace.Children.Add(memberPicker);
                memberWorkspace.Children.Add(memberChat);
                memberWorkspace.Children.Add(memberReport);
                memberWorkspace.Children.Add(memberEdit);
                memberWorkspace.Children.Add(saveMemberPermissions);
                memberWorkspace.Children.Add(removeMember);
                memberWorkspace.Children.Add(memberStatus);
                var sharedTaskBox = new TextBox { Header = "عنوان کار مشترک", PlaceholderText = "کار جدید برای همه اعضا" };
                var sharedTaskMode = new ComboBox { Header = "نوع برنامه", Items = { "Agenda", "Kanban", "School" }, SelectedIndex = 0 };
                var sharedTaskRecurrence = new ComboBox { Header = "تکرار برنامه (Agenda)", Items = { "بدون تکرار", "روزانه", "روزهای کاری", "هفتگی", "ماهانه" }, SelectedIndex = 0 };

                var schoolWorkspace = new StackPanel { Spacing = 8 };
                var subjectName = new TextBox { Header = "نام درس", PlaceholderText = "مثلاً ریاضی" };
                var subjectPicker = new ComboBox { Header = "درس", MinWidth = 260 };
                var timetableStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var homeworkTitle = new TextBox { Header = "عنوان تکلیف" };
                var homeworkInstructions = new TextBox { Header = "شرح تکلیف", AcceptsReturn = true, Height = 76 };
                var homeworkPicker = new ComboBox { Header = "تکلیف", MinWidth = 260 };
                var submissionBody = new TextBox { Header = "پاسخ دانش‌آموز", AcceptsReturn = true, Height = 90 };
                var gradeBox = new NumberBox { Header = "نمره (۰ تا ۱۰۰)", Minimum = 0, Maximum = 100, Value = 0 };
                var feedbackBox = new TextBox { Header = "بازخورد معلم" };
                async Task RefreshSchoolData()
                {
                    try
                    {
                        using var subjects = await _deskClient.ReadCollaborationSubjectsAsync(_collaborationGroupId);
                        var subjectRows = subjects.RootElement.EnumerateArray().ToList();
                        subjectPicker.ItemsSource = subjectRows.Select(x => x.GetProperty("name").GetString() ?? "").ToArray();
                        subjectPicker.SelectedIndex = subjectRows.Count > 0 ? 0 : -1;
                        using var timetable = await _deskClient.ReadCollaborationTimetableAsync(_collaborationGroupId);
                        using var homework = await _deskClient.ReadCollaborationHomeworkAsync(_collaborationGroupId);
                        var homeworkRows = homework.RootElement.EnumerateArray().ToList();
                        homeworkPicker.ItemsSource = homeworkRows.Select(x => x.GetProperty("title").GetString() ?? "").ToArray();
                        homeworkPicker.SelectedIndex = homeworkRows.Count > 0 ? 0 : -1;
                        timetableStatus.Text = $"درس: {subjectRows.Count} • جلسه هفتگی: {timetable.RootElement.GetArrayLength()} • تکلیف: {homeworkRows.Count}";
                        _schoolSubjectIds = subjectRows.Select(x => x.GetProperty("id").GetString() ?? "").ToList();
                        _schoolHomeworkIds = homeworkRows.Select(x => x.GetProperty("id").GetString() ?? "").ToList();
                    }
                    catch (Exception ex) { timetableStatus.Text = ex.Message; }
                }
                var schoolRefresh = MakeButton("بارگذاری درس‌ها، برنامه هفتگی و تکالیف");
                schoolRefresh.Click += async (_, _) => await RefreshSchoolData();
                var addSubject = MakeButton("افزودن درس");
                addSubject.Click += async (_, _) =>
                {
                    try { using var _ = await _deskClient.CreateCollaborationSubjectAsync(_collaborationGroupId, subjectName.Text.Trim()); subjectName.Text = ""; await RefreshSchoolData(); }
                    catch (Exception ex) { timetableStatus.Text = ex.Message; }
                };
                var addTimetable = MakeButton("ثبت جلسه نمونه برای روز انتخابی (۸ تا ۹)");
                addTimetable.Click += async (_, _) =>
                {
                    try
                    {
                        if (subjectPicker.SelectedIndex < 0) throw new InvalidOperationException("ابتدا درس را بارگذاری و انتخاب کنید.");
                        using var _ = await _deskClient.CreateCollaborationTimetableSlotAsync(
                            _collaborationGroupId, _schoolSubjectIds[subjectPicker.SelectedIndex], 0, "08:00", "09:00");
                        await RefreshSchoolData();
                    }
                    catch (Exception ex) { timetableStatus.Text = ex.Message; }
                };
                var addHomework = MakeButton("ثبت تکلیف برای درس انتخابی");
                addHomework.Click += async (_, _) =>
                {
                    try
                    {
                        if (subjectPicker.SelectedIndex < 0) throw new InvalidOperationException("ابتدا درس را انتخاب کنید.");
                        using var _ = await _deskClient.CreateCollaborationHomeworkAsync(
                            _collaborationGroupId, _schoolSubjectIds[subjectPicker.SelectedIndex],
                            homeworkTitle.Text.Trim(), homeworkInstructions.Text);
                        homeworkTitle.Text = ""; homeworkInstructions.Text = "";
                        await RefreshSchoolData();
                    }
                    catch (Exception ex) { timetableStatus.Text = ex.Message; }
                };
                var submitHomework = MakeButton("ارسال پاسخ تکلیف انتخابی");
                submitHomework.Click += async (_, _) =>
                {
                    try
                    {
                        if (homeworkPicker.SelectedIndex < 0) throw new InvalidOperationException("ابتدا تکلیف را انتخاب کنید.");
                        using var _ = await _deskClient.SubmitHomeworkAsync(
                            _schoolHomeworkIds[homeworkPicker.SelectedIndex], submissionBody.Text);
                        timetableStatus.Text = "پاسخ تکلیف ثبت شد.";
                    }
                    catch (Exception ex) { timetableStatus.Text = ex.Message; }
                };
                var schoolGrade = MakeButton("ثبت نمره و بازخورد پاسخ انتخابی");
                schoolGrade.Click += async (_, _) =>
                {
                    try
                    {
                        if (homeworkPicker.SelectedIndex < 0) throw new InvalidOperationException("ابتدا تکلیف را انتخاب کنید.");
                        using var submissions = await _deskClient.ReadHomeworkSubmissionsAsync(_schoolHomeworkIds[homeworkPicker.SelectedIndex]);
                        if (submissions.RootElement.GetArrayLength() == 0) throw new InvalidOperationException("هنوز پاسخی ثبت نشده است.");
                        var first = submissions.RootElement[0];
                        using var _ = await _deskClient.GradeHomeworkSubmissionAsync(
                            _schoolHomeworkIds[homeworkPicker.SelectedIndex],
                            first.GetProperty("id").GetString()!, (decimal)gradeBox.Value, feedbackBox.Text);
                        timetableStatus.Text = "نمره و بازخورد ثبت شد.";
                    }
                    catch (Exception ex) { timetableStatus.Text = ex.Message; }
                };
                schoolWorkspace.Children.Add(schoolRefresh);
                schoolWorkspace.Children.Add(subjectName);
                schoolWorkspace.Children.Add(addSubject);
                schoolWorkspace.Children.Add(subjectPicker);
                schoolWorkspace.Children.Add(addTimetable);
                schoolWorkspace.Children.Add(homeworkTitle);
                schoolWorkspace.Children.Add(homeworkInstructions);
                schoolWorkspace.Children.Add(addHomework);
                schoolWorkspace.Children.Add(homeworkPicker);
                schoolWorkspace.Children.Add(submissionBody);
                schoolWorkspace.Children.Add(submitHomework);
                schoolWorkspace.Children.Add(gradeBox);
                schoolWorkspace.Children.Add(feedbackBox);
                schoolWorkspace.Children.Add(schoolGrade);
                schoolWorkspace.Children.Add(timetableStatus);
                var kanbanWorkspace = new StackPanel { Spacing = 6 };
                var kanbanColumnName = new TextBox { Header = "ستون جدید Kanban", PlaceholderText = "مثلاً در انتظار" };
                var kanbanColumnPicker = new ComboBox { Header = "ستون‌ها", MinWidth = 260 };
                var kanbanColumnIds = new List<string>();
                var kanbanCardIds = new List<string>();
                var kanbanCardTitles = new List<string>();
                var kanbanCardColumns = new List<string>();
                var kanbanCardVersions = new List<int>();
                var kanbanCardPicker = new ComboBox { Header = "کارت برای جابه‌جایی", MinWidth = 260 };
                var kanbanStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
                async Task LoadKanbanData()
                {
                    try
                    {
                        using var columns = await _deskClient.ReadKanbanColumnsAsync(_collaborationGroupId);
                        var rows = columns.RootElement.EnumerateArray().ToList();
                        kanbanColumnIds = rows.Select(x => x.GetProperty("id").GetString() ?? "").ToList();
                        kanbanColumnPicker.ItemsSource = rows.Select(x => x.GetProperty("name").GetString() ?? "").ToArray();
                        kanbanColumnPicker.SelectedIndex = rows.Count > 0 ? 0 : -1;
                        using var items = await _deskClient.ReadCollaborationItemsAsync(_collaborationGroupId);
                        var cards = items.RootElement.EnumerateArray().Where(x => x.TryGetProperty("mode", out var mode) && mode.GetString() == "kanban").ToList();
                        kanbanCardIds = cards.Select(x => x.GetProperty("id").GetString() ?? "").ToList();
                        kanbanCardTitles = cards.Select(x => x.GetProperty("title").GetString() ?? "").ToList();
                        kanbanCardColumns = cards.Select(x => x.TryGetProperty("column_id", out var col) && col.ValueKind == JsonValueKind.String ? col.GetString()! : "").ToList();
                        kanbanCardVersions = cards.Select(x => x.TryGetProperty("version", out var ver) ? ver.GetInt32() : 1).ToList();
                        kanbanCardPicker.ItemsSource = kanbanCardTitles.ToArray();
                        kanbanCardPicker.SelectedIndex = cards.Count > 0 ? 0 : -1;
                        kanbanStatus.Text = $"{rows.Count} ستون • {cards.Count} کارت بارگذاری شد.";
                    }
                    catch (Exception ex) { kanbanStatus.Text = ex.Message; }
                }
                var loadKanban = MakeButton("بارگذاری ستون‌های Kanban");
                loadKanban.Click += async (_, _) => await LoadKanbanData();
                var addKanbanColumn = MakeButton("افزودن ستون");
                addKanbanColumn.Click += async (_, _) =>
                {
                    try { using var _ = await _deskClient.CreateKanbanColumnAsync(_collaborationGroupId, kanbanColumnName.Text.Trim()); kanbanColumnName.Text = ""; await LoadKanbanData(); }
                    catch (Exception ex) { kanbanStatus.Text = ex.Message; }
                };
                var moveKanbanCard = MakeButton("انتقال کارت به ستون بعدی");
                moveKanbanCard.Click += async (_, _) =>
                {
                    try
                    {
                        var index = kanbanCardPicker.SelectedIndex;
                        if (index < 0 || index >= kanbanCardIds.Count) return;
                        var current = kanbanColumnIds.IndexOf(kanbanCardColumns[index]);
                        if (current < 0 || current >= kanbanColumnIds.Count - 1) return;
                        var commandId = Guid.NewGuid();
                        var payload = new { itemId = kanbanCardIds[index], expectedVersion = kanbanCardVersions[index],
                            title = kanbanCardTitles[index], details = "", state = "todo",
                            columnId = kanbanColumnIds[current + 1],
                            timezone = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var zone)
                                ? zone : "Asia/Tehran" };
                        try
                        {
                            using var _ = await _deskClient.ReplayCollaborationCommandAsync(
                                _collaborationGroupId, commandId, "update_item", payload);
                            await LoadKanbanData();
                        }
                        catch (Exception ex) when (!ex.Message.Contains("version_conflict", StringComparison.OrdinalIgnoreCase) &&
                            !ex.Message.Contains("command_denied", StringComparison.OrdinalIgnoreCase) &&
                            !ex.Message.Contains("command_rejected", StringComparison.OrdinalIgnoreCase))
                        {
                            _collaborationOfflineQueue.Enqueue(_collaborationGroupId, "update_item", payload, commandId);
                            kanbanStatus.Text = "انتقال کارت در صف آفلاین ذخیره شد.";
                        }
                        catch (Exception ex) { kanbanStatus.Text = "تعارض یا رد فرمان: " + ex.Message; }
                    }
                    catch (Exception ex) { kanbanStatus.Text = ex.Message; }
                };
                var moveKanbanColumn = MakeButton("انتقال ستون انتخابی به انتهای برد");
                moveKanbanColumn.Click += async (_, _) =>
                {
                    try
                    {
                        if (kanbanColumnPicker.SelectedIndex < 0) return;
                        var index = kanbanColumnPicker.SelectedIndex;
                        var ids = kanbanColumnIds.ToList();
                        var selected = ids[index]; ids.RemoveAt(index); ids.Add(selected);
                        using var _ = await _deskClient.ReorderKanbanColumnsAsync(_collaborationGroupId, ids);
                        await LoadKanbanData();
                    }
                    catch (Exception ex) { kanbanStatus.Text = ex.Message; }
                };
                kanbanWorkspace.Children.Add(loadKanban);
                kanbanWorkspace.Children.Add(kanbanColumnName);
                kanbanWorkspace.Children.Add(addKanbanColumn);
                kanbanWorkspace.Children.Add(kanbanColumnPicker);
                kanbanWorkspace.Children.Add(moveKanbanColumn);
                kanbanWorkspace.Children.Add(kanbanCardPicker);
                kanbanWorkspace.Children.Add(moveKanbanCard);
                kanbanWorkspace.Children.Add(kanbanStatus);
                var addSharedTask = MakeButton("ثبت کار مشترک");
                addSharedTask.Click += async (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(_collaborationGroupId) || !_deskClient.IsAuthenticated)
                    { _collaborationStatus!.Text = "ابتدا وارد حساب شوید و به گروه متصل شوید."; return; }
                    if (string.IsNullOrWhiteSpace(sharedTaskBox.Text)) { _collaborationStatus!.Text = "عنوان کار را وارد کنید."; return; }
                    try
                    {
                        var mode = sharedTaskMode.SelectedIndex switch { 1 => "kanban", 2 => "school", _ => "agenda" };
                        var payload = new { title = sharedTaskBox.Text.Trim(), mode, state = "todo",
                            recurrenceRule = mode == "agenda" && sharedTaskRecurrence.SelectedIndex > 0
                                ? new[] { "", "daily", "weekdays", "weekly", "monthly" }[sharedTaskRecurrence.SelectedIndex] : null,
                            timezone = (TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var localZone) ? localZone : "Asia/Tehran") };
                        var commandId = Guid.NewGuid();
                        try
                        {
                            using var item = await _deskClient.ReplayCollaborationCommandAsync(
                                _collaborationGroupId, commandId, "create_item", payload);
                            _collaborationStatus!.Text = "کار مشترک در سرور ثبت شد.";
                        }
                        catch (Exception ex) when (!ex.Message.Contains("command_denied", StringComparison.OrdinalIgnoreCase) &&
                            !ex.Message.Contains("command_rejected", StringComparison.OrdinalIgnoreCase) &&
                            !ex.Message.Contains("version_conflict", StringComparison.OrdinalIgnoreCase))
                        {
                            _collaborationOfflineQueue.Enqueue(_collaborationGroupId, "create_item", payload, commandId);
                            _collaborationStatus!.Text = "ارتباط قطع است؛ کار در صف آفلاین ذخیره و با همین شناسه پس از اتصال ارسال می‌شود.";
                        }
                        catch (Exception ex) { _collaborationStatus!.Text = "فرمان توسط سرور رد شد: " + ex.Message; }
                        sharedTaskBox.Text = "";
                    }
                    catch (Exception ex) { _collaborationStatus!.Text = ex.Message; }
                };
                var sharedMessageBox = new TextBox { Header = "پیام گروه", PlaceholderText = "پیام برای اعضای گروه" };
                var sendSharedMessage = MakeButton("ارسال پیام");
                sendSharedMessage.Click += async (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(_collaborationGroupId) || !_deskClient.IsAuthenticated ||
                        string.IsNullOrWhiteSpace(sharedMessageBox.Text))
                    { _collaborationStatus!.Text = "به گروه متصل شوید و متن پیام را وارد کنید."; return; }
                    try
                    {
                        var payload = new { body = sharedMessageBox.Text.Trim() };
                        var commandId = Guid.NewGuid();
                        try
                        {
                            using var message = await _deskClient.ReplayCollaborationCommandAsync(
                                _collaborationGroupId, commandId, "send_message", payload);
                            _collaborationStatus!.Text = "پیام برای گروه ارسال شد.";
                        }
                        catch (Exception ex) when (!ex.Message.Contains("command_denied", StringComparison.OrdinalIgnoreCase) &&
                            !ex.Message.Contains("command_rejected", StringComparison.OrdinalIgnoreCase) &&
                            !ex.Message.Contains("version_conflict", StringComparison.OrdinalIgnoreCase))
                        {
                            _collaborationOfflineQueue.Enqueue(_collaborationGroupId, "send_message", payload, commandId);
                            _collaborationStatus!.Text = "پیام در صف آفلاین ذخیره شد و پس از اتصال ارسال می‌شود.";
                        }
                        catch (Exception ex) { _collaborationStatus!.Text = "پیام توسط سرور رد شد: " + ex.Message; }
                        sharedMessageBox.Text = "";
                    }
                    catch (Exception ex) { _collaborationStatus!.Text = ex.Message; }
                };
                var checkInvitations = MakeButton("بررسی دعوت‌های دریافتی");
                checkInvitations.Click += async (_, _) =>
                {
                    try
                    {
                        using var invitations = await _deskClient.ReadCollaborationInvitationsAsync();
                        var rows = invitations.RootElement.EnumerateArray().ToList();
                        if (rows.Count == 0) { _collaborationStatus!.Text = "دعوت جدیدی ندارید."; return; }
                        var first = rows[0];
                        var inviteId = first.GetProperty("id").GetString() ?? "";
                        var groupTitle = first.TryGetProperty("collab_groups", out var group)
                            ? group.GetProperty("name").GetString() : "گروه";
                        var dialog = new ContentDialog
                        {
                            XamlRoot = _desktopModulePage!.XamlRoot,
                            Title = "دعوت عضویت",
                            Content = $"برای عضویت در گروه «{groupTitle}» دعوت شده‌اید. دعوت را می‌پذیرید؟",
                            PrimaryButtonText = "پذیرش دعوت",
                            CloseButtonText = "بعداً"
                        };
                        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                        {
                            await _deskClient.AcceptCollaborationInvitationAsync(inviteId);
                            _collaborationStatus!.Text = "عضویت در گروه پذیرفته شد.";
                        }
                    }
                    catch (Exception ex) { _collaborationStatus!.Text = ex.Message; }
                };
                collaboration.Children.Add(sharedTaskBox);
                collaboration.Children.Add(sharedTaskMode);
                collaboration.Children.Add(sharedTaskRecurrence);
                if (kanbanMode.IsChecked == true) collaboration.Children.Add(kanbanWorkspace);
                if (schoolMode.IsChecked == true) collaboration.Children.Add(schoolWorkspace);
                collaboration.Children.Add(addSharedTask);
                collaboration.Children.Add(sharedMessageBox);
                collaboration.Children.Add(sendSharedMessage);
                collaboration.Children.Add(checkInvitations);
                collaboration.Children.Add(groupNameBox);
                collaboration.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
                    Children = { agendaMode, kanbanMode, schoolMode } });
                collaboration.Children.Add(createGroup);
                collaboration.Children.Add(groupIdBox);
                collaboration.Children.Add(connectGroup);
                collaboration.Children.Add(memberWorkspace);
                collaboration.Children.Add(inviteIdBox);
                collaboration.Children.Add(assistantMode);
                collaboration.Children.Add(inviteMember);
                collaboration.Children.Add(_collaborationStatus);
                collaboration.Children.Add(_collaborationDataStatus);
                page.Children.Add(Card("همکاری آنلاین و گروه‌ها",
                    "ساخت گروه، دعوت با شناسه واقعی زروان و دریافت تغییرات مشترک. ورود به حساب Zurvan Desk الزامی است.",
                    collaboration));
                StartCollaborationPolling();

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

                        // Render the zodiac art at 200% of its former size.
                        // It is a translucent watermark behind the occasion text,
                        // not a separate row, and Uniform preserves the full artwork.
                        var monthContent = new Grid();
                        var monthArtworkSource = await ZodiacArtwork.LoadAsync(month, _settings.ZodiacThemeId);
                        if (monthArtworkSource is not null)
                        {
                            monthContent.Children.Add(new Image
                            {
                                Source = monthArtworkSource,
                                Width = 208,
                                Height = 208,
                                Opacity = 0.5,
                                Stretch = Stretch.Uniform,
                                HorizontalAlignment = HorizontalAlignment.Right,
                                VerticalAlignment = VerticalAlignment.Center,
                                IsHitTestVisible = false
                            });
                        }
                        monthContent.Children.Add(monthBody);
                        var monthCard = new Border
                        {
                            Background = CreateSideSurfaceBrush(),
                            BorderBrush = BrushWithAlpha(_theme.Accent, 0x60),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(13),
                            Child = monthContent
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
                    ("mythic", "اسطوره‌ای", "♜"), ("cute_1", "کیوت ۱", "☺"),
                    ("cute_2", "کیوت ۲", "♡"), ("cute_3", "کیوت ۳", "✿"),
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
                    "cute_1" or "cute_2" or "cute_3" or "cute" => ("#FF6D3157", "#FFC879A8"),
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
