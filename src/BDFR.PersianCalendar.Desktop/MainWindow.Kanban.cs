using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace BDFR.PersianCalendar.Desktop;

/// <summary>Independent personal Kanban workspace; does not reuse PLANKA source.</summary>
public sealed partial class MainWindow
{
    private sealed class DesktopKanbanData
    {
        public List<DesktopKanbanBoard> Boards { get; set; } = new();
        public List<DesktopKanbanLane> Lanes { get; set; } = new();
        public List<DesktopKanbanCard> Cards { get; set; } = new();
    }
    private sealed class DesktopKanbanBoard { public string Id { get; set; } = ""; public string Name { get; set; } = ""; }
    private sealed class DesktopKanbanLane
    {
        public string Id { get; set; } = "";
        public string BoardId { get; set; } = "";
        public string Name { get; set; } = "";
        public int Order { get; set; }
    }
    private sealed class DesktopKanbanCard
    {
        public string Id { get; set; } = "";
        public string LaneId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Labels { get; set; } = "";
        public string DueDate { get; set; } = "";
        public string Priority { get; set; } = "normal";
        public bool Archived { get; set; }
        public int Order { get; set; }
        public List<DesktopKanbanTask> Checklist { get; set; } = new();
        public List<string> Comments { get; set; } = new();
    }
    private sealed class DesktopKanbanTask { public string Name { get; set; } = ""; public bool Done { get; set; } }
    private DesktopKanbanData? _personalKanbanData;
    private string? _activeKanbanBoard;
    private bool _kanbanShowArchive;
    private string _kanbanSearch = "";
    private string _kanbanLoadError = "";

    private string DesktopKanbanPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Zurvan", "personal-kanban.json");

    private DesktopKanbanData LoadDesktopKanban()
    {
        if (_personalKanbanData is not null) return _personalKanbanData;
        var path = DesktopKanbanPath;
        try
        {
            if (!File.Exists(path)) return _personalKanbanData = new DesktopKanbanData();
            return _personalKanbanData = JsonSerializer.Deserialize<DesktopKanbanData>(
                File.ReadAllText(path)) ?? new DesktopKanbanData();
        }
        catch (Exception ex)
        {
            try
            {
                var backup = path + ".bak";
                if (File.Exists(backup))
                {
                    var restored = JsonSerializer.Deserialize<DesktopKanbanData>(File.ReadAllText(backup));
                    if (restored is not null)
                    {
                        File.Copy(backup, path, overwrite: true);
                        StatusText.Text = "داده‌های کانبان از نسخه سالم قبلی بازیابی شدند.";
                        return _personalKanbanData = restored;
                    }
                }
            }
            catch { /* Preserve the damaged file so it can be recovered manually. */ }
            _kanbanLoadError = "فایل کانبان قابل خواندن نیست. اطلاعات موجود حذف نشده‌اند: " + ex.Message;
            return _personalKanbanData = new DesktopKanbanData();
        }
    }

    private void SaveDesktopKanban()
    {
        if (!string.IsNullOrEmpty(_kanbanLoadError))
        {
            StatusText.Text = _kanbanLoadError;
            return;
        }
        try
        {
            var path = DesktopKanbanPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            var data = JsonSerializer.Serialize(LoadDesktopKanban(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, data);
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            StatusText.Text = "ذخیره کانبان ناموفق بود؛ تغییرات فعلی هنوز روی دیسک ثبت نشده‌اند: " + ex.Message;
        }
    }

    private async Task<string?> AskKanbanTextAsync(string title, string label, string initial = "", bool multiline = false)
    {
        var input = new TextBox
        {
            Header = label, Text = initial, MinWidth = 310,
            AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 100 : 36
        };
        var dialog = new ContentDialog
        {
            Title = title, Content = input,
            PrimaryButtonText = "ذخیره", CloseButtonText = "انصراف",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = (Content as FrameworkElement)?.XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text)
            ? input.Text.Trim() : null;
    }

    private FrameworkElement BuildPersonalKanbanPage()
    {
        var data = LoadDesktopKanban();
        var root = new StackPanel { Spacing = 14, FlowDirection = FlowDirection.RightToLeft };
        void Refresh() => Render();

        void Render()
        {
            root.Children.Clear();
            if (!string.IsNullOrEmpty(_kanbanLoadError))
            {
                root.Children.Add(new TextBlock
                {
                    Text = _kanbanLoadError, TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemeService.Brush("#FFD32F2F")
                });
                return;
            }
            var board = data.Boards.FirstOrDefault(x => x.Id == _activeKanbanBoard);
            if (board is null)
            {
                void AddArea(string header, string description, bool team)
                {
                    var section = new StackPanel { Spacing = 9, Padding = new Thickness(18) };
                    section.Children.Add(new TextBlock { Text = header,
                        FontSize = 21, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                    section.Children.Add(new TextBlock { Text = description, Opacity = 0.75 });
                    var add = MakeButton("＋ افزودن");
                    add.Click += async (_, _) =>
                    {
                        var choice = new ComboBox
                        {
                            MinWidth = 280, Header = "مدل برنامه",
                            Items = { "۱. کانبان (Kanban)", "۲. مدل دوم (بعداً)", "۳. مدل سوم (بعداً)" },
                            SelectedIndex = 0
                        };
                        var select = new ContentDialog
                        {
                            Title = "انتخاب مدل برنامه", Content = choice,
                            PrimaryButtonText = "ادامه", CloseButtonText = "انصراف",
                            XamlRoot = (Content as FrameworkElement)?.XamlRoot
                        };
                        if (await select.ShowAsync() != ContentDialogResult.Primary) return;
                        if (team)
                        {
                            StatusText.Text = "مدل مشارکتی در مرحله مستقل بعدی طراحی می‌شود.";
                            return;
                        }
                        if (choice.SelectedIndex != 0)
                        {
                            StatusText.Text = "این مدل هنوز تعریف نشده است.";
                            return;
                        }
                        var title = await AskKanbanTextAsync("پروژه کانبان", "نام پروژه");
                        if (title is null) return;
                        var created = new DesktopKanbanBoard { Id = Guid.NewGuid().ToString("N"), Name = title };
                        data.Boards.Add(created);
                        SaveDesktopKanban();
                        _activeKanbanBoard = created.Id;
                        Refresh();
                    };
                    section.Children.Add(add);
                    if (!team)
                    {
                        foreach (var item in data.Boards)
                        {
                            var open = MakeButton("▦  " + item.Name);
                            open.HorizontalAlignment = HorizontalAlignment.Stretch;
                            open.Click += (_, _) => { _activeKanbanBoard = item.Id; Refresh(); };
                            section.Children.Add(open);
                        }
                    }
                    root.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(14), Padding = new Thickness(2),
                        Background = CreateSideSurfaceBrush(),
                        BorderBrush = BrushWithAlpha(_theme.Accent, 0x65),
                        BorderThickness = new Thickness(1), Child = section
                    });
                }
                AddArea("برنامه انفرادی و شخصی", "خصوصی؛ فقط پس از افزودن، پروژه نمایش داده می‌شود.", false);
                AddArea("برنامه مشارکتی و گروهی", "بخش مستقل برای تیم‌ها و اعضای مشترک.", true);
                return;
            }

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
            var back = MakeButton("بازگشت");
            back.Click += (_, _) => { _activeKanbanBoard = null; Refresh(); };
            header.Children.Add(back);
            header.Children.Add(new TextBlock
            {
                Text = board.Name, FontSize = 23,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var rename = MakeButton("تغییر نام");
            rename.Click += async (_, _) =>
            {
                var title = await AskKanbanTextAsync("ویرایش پروژه", "عنوان", board.Name);
                if (title is null) return;
                board.Name = title; SaveDesktopKanban(); Refresh();
            };
            header.Children.Add(rename);
            var delete = MakeButton("حذف پروژه");
            delete.Click += async (_, _) =>
            {
                if (!await ConfirmKanbanAsync("پروژه و تمام ستون‌ها و کارت‌های آن حذف شوند؟")) return;
                var laneIds = data.Lanes.Where(x => x.BoardId == board.Id).Select(x => x.Id).ToHashSet();
                data.Cards.RemoveAll(x => laneIds.Contains(x.LaneId));
                data.Lanes.RemoveAll(x => x.BoardId == board.Id);
                data.Boards.Remove(board);
                SaveDesktopKanban(); _activeKanbanBoard = null; Refresh();
            };
            header.Children.Add(delete);
            root.Children.Add(header);

            var filter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var search = new TextBox { Header = "جستجوی کارت", PlaceholderText = "عنوان، برچسب، توضیحات",
                Width = 270, Text = _kanbanSearch };
            filter.Children.Add(search);
            var applySearch = MakeButton("جستجو");
            applySearch.Click += (_, _) => { _kanbanSearch = search.Text.Trim(); Refresh(); };
            filter.Children.Add(applySearch);
            var resetSearch = MakeButton("پاک‌کردن");
            resetSearch.Click += (_, _) => { _kanbanSearch = ""; Refresh(); };
            filter.Children.Add(resetSearch);
            var archived = new CheckBox { Content = "کارت‌های بایگانی‌شده", IsChecked = _kanbanShowArchive,
                VerticalAlignment = VerticalAlignment.Bottom };
            archived.Checked += (_, _) => { _kanbanShowArchive = true; Refresh(); };
            archived.Unchecked += (_, _) => { _kanbanShowArchive = false; Refresh(); };
            filter.Children.Add(archived);
            root.Children.Add(filter);

            var boardColumns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 13,
                FlowDirection = FlowDirection.RightToLeft };
            var lanes = data.Lanes.Where(x => x.BoardId == board.Id).OrderBy(x => x.Order).ToList();
            foreach (var lane in lanes)
            {
                var column = new StackPanel { Width = 277, Spacing = 8, Padding = new Thickness(10) };
                var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                controls.Children.Add(new TextBlock { Text = lane.Name, FontSize = 17, Width = 136,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                var options = MakeButton("⋯");
                options.Click += async (_, _) =>
                {
                    var chosen = new ComboBox
                    {
                        Items = { "تغییر نام", "انتقال به راست", "انتقال به چپ", "حذف ستون" },
                        SelectedIndex = 0
                    };
                    var dialog = new ContentDialog
                    {
                        Title = "تنظیمات ستون", Content = chosen,
                        PrimaryButtonText = "اجرا", CloseButtonText = "انصراف",
                        XamlRoot = (Content as FrameworkElement)?.XamlRoot
                    };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                    if (chosen.SelectedIndex == 0)
                    {
                        var text = await AskKanbanTextAsync("ویرایش ستون", "نام ستون", lane.Name);
                        if (text is not null) lane.Name = text;
                    }
                    else if (chosen.SelectedIndex is 1 or 2)
                    {
                        var position = lanes.IndexOf(lane);
                        var destination = position + (chosen.SelectedIndex == 1 ? -1 : 1);
                        if (destination >= 0 && destination < lanes.Count)
                        {
                            lanes.RemoveAt(position); lanes.Insert(destination, lane);
                            for (var i = 0; i < lanes.Count; i++) lanes[i].Order = i;
                        }
                    }
                    else if (await ConfirmKanbanAsync("ستون و کارت‌های آن حذف شوند؟"))
                    {
                        data.Cards.RemoveAll(x => x.LaneId == lane.Id);
                        data.Lanes.Remove(lane);
                    }
                    SaveDesktopKanban(); Refresh();
                };
                controls.Children.Add(options);
                column.Children.Add(controls);
                var visible = data.Cards
                    .Where(x => x.LaneId == lane.Id && x.Archived == _kanbanShowArchive)
                    .Where(x => _kanbanSearch.Length == 0 ||
                        (x.Title + " " + x.Labels + " " + x.Description)
                        .Contains(_kanbanSearch, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Order).ToList();
                foreach (var card in visible)
                {
                    var box = new StackPanel { Spacing = 5, Padding = new Thickness(12) };
                    if (!string.IsNullOrWhiteSpace(card.Labels))
                        box.Children.Add(new TextBlock { Text = card.Labels, FontSize = 11, Opacity = .85 });
                    box.Children.Add(new TextBlock { Text = card.Title,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                    if (!string.IsNullOrEmpty(card.DueDate))
                        box.Children.Add(new TextBlock { Text = "◷ " + card.DueDate, FontSize = 12 });
                    if (card.Checklist.Count > 0)
                        box.Children.Add(new TextBlock { Text = $"☑ {card.Checklist.Count(x => x.Done)}/{card.Checklist.Count}", FontSize = 12 });
                    var edit = MakeButton("بازکردن کارت");
                    edit.Click += async (_, _) => { await EditDesktopKanbanCardAsync(data, card, lanes); Refresh(); };
                    box.Children.Add(edit);
                    var draggable = new Border
                    {
                        Background = CreateSideSurfaceBrush(),
                        CornerRadius = new CornerRadius(9),
                        BorderBrush = BrushWithAlpha(_theme.Accent, 0x50),
                        BorderThickness = new Thickness(1), Child = box,
                        CanDrag = !_kanbanShowArchive
                    };
                    draggable.DragStarting += (_, args) =>
                    {
                        args.Data.SetText(card.Id);
                        args.Data.RequestedOperation = DataPackageOperation.Move;
                    };
                    column.Children.Add(draggable);
                }
                if (!_kanbanShowArchive)
                {
                    var addCard = MakeButton("＋ افزودن کارت");
                    addCard.Click += async (_, _) =>
                    {
                        var title = await AskKanbanTextAsync("کارت جدید", "عنوان کارت");
                        if (title is null) return;
                        data.Cards.Add(new DesktopKanbanCard
                        {
                            Id = Guid.NewGuid().ToString("N"), LaneId = lane.Id, Title = title,
                            Order = data.Cards.Count(x => x.LaneId == lane.Id)
                        });
                        SaveDesktopKanban(); Refresh();
                    };
                    column.Children.Add(addCard);
                }
                var dropColumn = new Border
                {
                    Width = 290, Padding = new Thickness(4),
                    Background = BrushWithAlpha(_theme.Accent, 0x15),
                    CornerRadius = new CornerRadius(12), Child = column,
                    AllowDrop = true
                };
                dropColumn.DragOver += (_, args) =>
                {
                    if (args.DataView.Contains(StandardDataFormats.Text))
                        args.AcceptedOperation = DataPackageOperation.Move;
                };
                dropColumn.Drop += async (_, args) =>
                {
                    if (!args.DataView.Contains(StandardDataFormats.Text)) return;
                    var id = await args.DataView.GetTextAsync();
                    var dragged = data.Cards.FirstOrDefault(x => x.Id == id);
                    if (dragged is null || dragged.Archived || dragged.LaneId == lane.Id) return;
                    dragged.LaneId = lane.Id;
                    dragged.Order = data.Cards.Count(x => x.LaneId == lane.Id);
                    SaveDesktopKanban();
                    Refresh();
                };
                boardColumns.Children.Add(dropColumn);
            }
            var addLane = MakeButton("＋ افزودن ستون");
            addLane.Click += async (_, _) =>
            {
                var text = await AskKanbanTextAsync("ستون جدید", "نام ستون");
                if (text is null) return;
                data.Lanes.Add(new DesktopKanbanLane
                {
                    Id = Guid.NewGuid().ToString("N"), BoardId = board.Id,
                    Name = text, Order = data.Lanes.Count(x => x.BoardId == board.Id)
                });
                SaveDesktopKanban(); Refresh();
            };
            boardColumns.Children.Add(addLane);
            root.Children.Add(new ScrollViewer
            {
                HorizontalScrollMode = ScrollMode.Enabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Content = boardColumns,
                FlowDirection = FlowDirection.RightToLeft
            });
        }
        Render();
        return root;
    }

    private async Task<bool> ConfirmKanbanAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "تأیید عملیات", Content = message,
            PrimaryButtonText = "تأیید", CloseButtonText = "انصراف",
            XamlRoot = (Content as FrameworkElement)?.XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task EditDesktopKanbanCardAsync(DesktopKanbanData data,
        DesktopKanbanCard card, List<DesktopKanbanLane> lanes)
    {
        var form = new StackPanel { Spacing = 9, Width = 410 };
        var title = new TextBox { Header = "عنوان", Text = card.Title };
        var description = new TextBox { Header = "توضیحات", Text = card.Description,
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90 };
        var labels = new TextBox { Header = "برچسب‌ها (ویرگول)", Text = card.Labels };
        var due = new TextBox { Header = "سررسید YYYY-MM-DD", Text = card.DueDate };
        var priority = new ComboBox { Header = "اولویت", MinWidth = 190,
            Items = { "کم", "عادی", "زیاد", "فوری" },
            SelectedIndex = card.Priority switch { "low" => 0, "high" => 2, "urgent" => 3, _ => 1 } };
        var lane = new ComboBox { Header = "انتقال به ستون", MinWidth = 200 };
        foreach (var column in lanes) lane.Items.Add(column.Name);
        lane.SelectedIndex = Math.Max(0, lanes.FindIndex(x => x.Id == card.LaneId));
        var checklist = new TextBox { Header = "چک‌لیست (هر خط یک مورد؛ [x] یعنی انجام‌شده)",
            Text = string.Join(Environment.NewLine, card.Checklist.Select(x => (x.Done ? "[x] " : "[ ] ") + x.Name)),
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        var comments = new TextBox { Header = "نظرات (هر خط یک نظر)",
            Text = string.Join(Environment.NewLine, card.Comments),
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
        foreach (var control in new Control[] { title, description, labels, due, priority, lane, checklist, comments })
            form.Children.Add(control);
        var archive = new CheckBox { Content = "بایگانی کارت", IsChecked = card.Archived };
        form.Children.Add(archive);
        var dialog = new ContentDialog
        {
            Title = "جزئیات کارت", Content = new ScrollViewer
            {
                MaxHeight = 530, Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            PrimaryButtonText = "ذخیره", SecondaryButtonText = "حذف کارت",
            CloseButtonText = "انصراف",
            XamlRoot = (Content as FrameworkElement)?.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            if (!await ConfirmKanbanAsync("این کارت حذف شود؟")) return;
            data.Cards.Remove(card); SaveDesktopKanban(); return;
        }
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(title.Text)) return;
        if (!string.IsNullOrWhiteSpace(due.Text) &&
            !DateOnly.TryParseExact(due.Text.Trim(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
        {
            StatusText.Text = "سررسید باید با فرمت YYYY-MM-DD وارد شود.";
            return;
        }
        card.Title = title.Text.Trim();
        card.Description = description.Text;
        card.Labels = string.Join(", ", labels.Text.Split(',', '،')
            .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().Take(12));
        card.DueDate = due.Text.Trim();
        card.Priority = priority.SelectedIndex switch { 0 => "low", 2 => "high", 3 => "urgent", _ => "normal" };
        card.Archived = archive.IsChecked == true;
        if (lane.SelectedIndex >= 0 && lane.SelectedIndex < lanes.Count)
        {
            var newLane = lanes[lane.SelectedIndex].Id;
            if (card.LaneId != newLane)
            {
                card.LaneId = newLane;
                card.Order = data.Cards.Count(x => x.LaneId == newLane);
            }
        }
        card.Checklist = checklist.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0)
            .Select(x => new DesktopKanbanTask
            {
                Done = x.StartsWith("[x]", StringComparison.OrdinalIgnoreCase),
                Name = x.StartsWith("[x]", StringComparison.OrdinalIgnoreCase) || x.StartsWith("[ ]")
                    ? x.Substring(3).Trim() : x
            }).ToList();
        card.Comments = comments.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        SaveDesktopKanban();
    }
}
