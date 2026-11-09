using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private readonly ZurvanDeskDesktopClient _deskClient = new();
    private StackPanel? _deskWorkspace;
    private TextBlock? _deskWorkspaceStatus;
    private readonly List<(string Name, string? MimeType, byte[] Bytes)> _deskPendingUploads = new();

    private FrameworkElement BuildZurvanDeskWorkspace()
    {
        _deskWorkspace = new StackPanel
        {
            Spacing = 14,
            FlowDirection = FlowDirection.RightToLeft
        };
        _deskWorkspaceStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        };
        RenderZurvanDeskWorkspace();
        return _deskWorkspace;
    }

    private void RenderZurvanDeskWorkspace()
    {
        if (_deskWorkspace is null) return;
        _deskWorkspace.Children.Clear();

        _deskWorkspace.Children.Add(BuildDeskDesignHeader());

        if (_deskClient.IsAuthenticated)
        {
            if (!_deskClient.HasLocalVaultKey())
                BuildDeskRecoveryPanel();
            else
                BuildDeskFileManager();
        }
        else
        {
            BuildDeskAuthenticationPanel();
        }

        if (_deskWorkspaceStatus is not null)
            _deskWorkspace.Children.Add(_deskWorkspaceStatus);
    }

    private Border DeskCard(UIElement child)
        => new()
        {
            Background = CreateSideSurfaceBrush(),
            BorderBrush = BrushWithAlpha(_theme.Accent, 0x70),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18),
            Child = child
        };

    private void SetDeskStatus(string message, bool isError = false)
    {
        if (_deskWorkspaceStatus is null) return;
        _deskWorkspaceStatus.Text = message;
        _deskWorkspaceStatus.Foreground = ThemeService.Brush(
            isError ? _theme.HolidayText : _theme.SecondaryText);
    }

    private void BuildDeskAuthenticationPanel()
    {
        var panel = new StackPanel { Spacing = 17, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = "ورود به حساب",
            FontSize = 25,
            Margin = new Thickness(0, 0, 0, 14),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var identifier = new TextBox
        {
            Header = "ایمیل یا شماره حساب",
            PlaceholderText = "ایمیل یا شماره حساب را وارد کنید",
            MinHeight = 46,
            CornerRadius = new CornerRadius(9),
            FlowDirection = FlowDirection.RightToLeft
        };
        var displayName = new TextBox
        {
            Header = "نام نمایشی",
            PlaceholderText = "نامی که در زروان نمایش داده می‌شود",
            MinHeight = 46,
            CornerRadius = new CornerRadius(9)
        };
        var password = new PasswordBox
        {
            Header = "رمز عبور",
            PlaceholderText = "رمز عبور را وارد کنید",
            MinHeight = 46,
            FlowDirection = FlowDirection.RightToLeft,
            CornerRadius = new CornerRadius(9)
        };
        var submit = new Button
        {
            Content = "ورود",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 54,
            CornerRadius = new CornerRadius(13),
            Background = ThemeService.Brush("#FFBC7439"),
            Foreground = ThemeService.Brush("#FFFFFFFF"),
            BorderThickness = new Thickness(0)
        };
        var switchMode = new Button
        {
            Content = "ساخت حساب",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 54,
            CornerRadius = new CornerRadius(13),
            Background = ThemeService.Brush("#FFF3E3D9"),
            Foreground = ThemeService.Brush("#FF26212A"),
            BorderThickness = new Thickness(0),
        };
        var registerMode = false;

        void UpdateMode()
        {
            title.Text = registerMode ? "ساخت حساب جدید" : "ورود به حساب";
            displayName.Visibility = registerMode ? Visibility.Visible : Visibility.Collapsed;
            submit.Content = registerMode ? "ساخت حساب" : "ورود";
            switchMode.Content = registerMode ? "حساب دارم؛ ورود" : "ساخت حساب";
        }

        submit.Click += async (_, _) =>
        {
            var id = identifier.Text.Trim();
            if (id.Length < 3 || password.Password.Length < 8)
            {
                SetDeskStatus("ایمیل یا شماره و رمز عبور معتبر وارد کنید.", true);
                return;
            }

            submit.IsEnabled = switchMode.IsEnabled = false;
            try
            {
                if (registerMode)
                {
                    await _deskClient.RegisterAsync(id, password.Password, displayName.Text.Trim());
                    password.Password = "";
                    SetDeskStatus("حساب ساخته شد؛ در حال ایجاد Vault رمزگذاری‌شده…");
                    var phrase = await _deskClient.CreateVaultAndRecoveryPhraseAsync();
                    await ShowDeskRecoveryPhraseAsync(phrase);
                    SetDeskStatus("حساب و Vault آماده است. عبارت بازیابی را در جای امن و آفلاین نگه دارید.");
                }
                else
                {
                    await _deskClient.LoginAsync(id, password.Password);
                    password.Password = "";
                    SetDeskStatus("ورود موفق بود.");
                }

                RenderZurvanDeskWorkspace();
            }
            catch (Exception ex)
            {
                SetDeskStatus(ex.Message, true);
            }
            finally
            {
                submit.IsEnabled = switchMode.IsEnabled = true;
            }
        };

        switchMode.Click += (_, _) =>
        {
            registerMode = !registerMode;
            UpdateMode();
        };

        panel.Children.Add(title);
        panel.Children.Add(identifier);
        panel.Children.Add(displayName);
        panel.Children.Add(password);
        panel.Children.Add(submit);
        panel.Children.Add(switchMode);
        panel.Children.Add(new TextBlock
        {
            Text = "رمز عبور و نشست حساب در سرویس ابری مدیریت می‌شوند. کلید رمزگذاری فایل‌ها فقط روی دستگاه شما نگه‌داری می‌شود.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });
        UpdateMode();
        _deskWorkspace!.Children.Add(BuildDeskSplitLayout(panel));
    }

    private void BuildDeskRecoveryPanel()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "بازیابی Vault",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "این حساب روی این ویندوز قبلاً باز نشده است. برای رمزگشایی نام و محتوای فایل‌ها، عبارت بازیابی ۲۴ کلمه‌ای را وارد کنید.",
            TextWrapping = TextWrapping.Wrap
        });
        var phrase = new TextBox
        {
            Header = "عبارت بازیابی ۲۴ کلمه‌ای",
            PlaceholderText = "۲۴ کلمه را با فاصله وارد کنید",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
            FlowDirection = FlowDirection.LeftToRight
        };
        var restore = new Button
        {
            Content = "بازیابی و بازکردن فایل‌ها",
            MinHeight = 42
        };
        var logout = new Button { Content = "خروج از حساب" };
        restore.Click += async (_, _) =>
        {
            restore.IsEnabled = false;
            try
            {
                await _deskClient.RestoreVaultAsync(phrase.Text);
                phrase.Text = "";
                SetDeskStatus("Vault با موفقیت بازیابی شد.");
                RenderZurvanDeskWorkspace();
            }
            catch (Exception ex) { SetDeskStatus(ex.Message, true); }
            finally { restore.IsEnabled = true; }
        };
        logout.Click += async (_, _) =>
        {
            await _deskClient.LogoutAsync();
            RenderZurvanDeskWorkspace();
            SetDeskStatus("از حساب خارج شدید.");
        };
        panel.Children.Add(phrase);
        panel.Children.Add(restore);
        panel.Children.Add(logout);
        _deskWorkspace!.Children.Add(DeskCard(panel));
    }

    private void BuildDeskFileManager()
    {
        var panel = new StackPanel { Spacing = 10 };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var account = new StackPanel { Spacing = 2 };
        account.Children.Add(new TextBlock
        {
            Text = _deskClient.Identifier ?? "حساب زروان",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        account.Children.Add(new TextBlock
        {
            Text = "Vault رمزگذاری‌شده فعال است",
            FontSize = 12,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });
        Grid.SetColumn(account, 0);
        header.Children.Add(account);

        var refresh = new Button { Content = "تازه‌سازی" };
        refresh.Click += async (_, _) => await RefreshDeskFilesAsync();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);

        var logout = new Button { Content = "خروج" };
        logout.Click += async (_, _) =>
        {
            await _deskClient.LogoutAsync();
            RenderZurvanDeskWorkspace();
            SetDeskStatus("از حساب خارج شدید.");
        };
        Grid.SetColumn(logout, 2);
        header.Children.Add(logout);

        var pendingList = new StackPanel { Spacing = 6 };
        var list = new StackPanel { Spacing = 8 };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            FlowDirection = FlowDirection.RightToLeft
        };
        var upload = new Button
        {
            Content = "＋ انتخاب فایل‌ها",
            MinHeight = 42
        };
        var sync = new Button
        {
            Content = "🔐 نهایی‌سازی و همگام‌سازی",
            MinHeight = 42,
            IsEnabled = _deskPendingUploads.Count > 0
        };
        upload.Click += async (_, _) => await PickDeskFilesForSyncAsync(upload, sync, pendingList);
        sync.Click += async (_, _) => await FinalizeDeskUploadsAsync(sync, upload, pendingList, list);
        actions.Children.Add(upload);
        actions.Children.Add(sync);

        var pendingTitle = new TextBlock
        {
            Text = "صف ارسال؛ فایل‌ها تا زمان تأیید نهایی روی دستگاه باقی می‌مانند",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0)
        };
        panel.Children.Add(header);
        panel.Children.Add(actions);
        panel.Children.Add(pendingTitle);
        panel.Children.Add(pendingList);
        panel.Children.Add(new TextBlock
        {
            Text = "فایل‌های همگام‌سازی‌شده",
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(list);
        _deskWorkspace!.Children.Add(DeskCard(panel));
        RenderPendingDeskUploads(pendingList, sync);
        _ = RefreshDeskFilesAsync(list, upload);
    }

    private async Task RefreshDeskFilesAsync(
        StackPanel? target = null,
        Button? uploadButton = null)
    {
        target ??= FindDeskFileList();
        if (target is null) return;
        try
        {
            if (uploadButton is not null) uploadButton.IsEnabled = false;
            target.Children.Clear();
            var files = await _deskClient.ListFilesAsync();
            if (files.Count == 0)
            {
                target.Children.Add(new TextBlock
                {
                    Text = "هنوز فایلی در فضای ابری شما وجود ندارد.",
                    Opacity = 0.72,
                    Margin = new Thickness(4, 12, 4, 12)
                });
            }
            foreach (var file in files)
                target.Children.Add(BuildDeskFileRow(file));
            SetDeskStatus($"{files.Count} فایل در فضای Zurvan Desk موجود است.");
        }
        catch (Exception ex) { SetDeskStatus(ex.Message, true); }
        finally { if (uploadButton is not null) uploadButton.IsEnabled = true; }
    }

    private StackPanel? FindDeskFileList()
    {
        if (_deskWorkspace is null) return null;
        // File list follows header, action bar, queue title, pending queue and remote-list title.
        if (_deskWorkspace.Children.Count < 2) return null;
        if (_deskWorkspace.Children[1] is not Border card ||
            card.Child is not StackPanel panel ||
            panel.Children.Count < 6)
            return null;
        return panel.Children[5] as StackPanel;
    }

    private FrameworkElement BuildDeskFileRow(ZurvanDeskFile file)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel { Spacing = 3 };
        info.Children.Add(new TextBlock
        {
            Text = file.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        info.Children.Add(new TextBlock
        {
            Text = $"{FormatDeskBytes(file.Size)}  ·  {file.ModifiedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm}",
            FontSize = 11,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });
        Grid.SetColumn(info, 0);
        row.Children.Add(info);

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        var download = new Button { Content = "دانلود", MinWidth = 62 };
        download.Click += async (_, _) => await DownloadDeskFileAsync(file, download);
        var rename = new Button { Content = "تغییر نام", MinWidth = 72 };
        rename.Click += async (_, _) => await RenameDeskFileAsync(file, rename);
        var delete = new Button { Content = "حذف", MinWidth = 54 };
        delete.Click += async (_, _) => await DeleteDeskFileAsync(file, delete);
        commands.Children.Add(download);
        commands.Children.Add(rename);
        commands.Children.Add(delete);
        Grid.SetColumn(commands, 1);
        row.Children.Add(commands);

        return new Border
        {
            BorderBrush = BrushWithAlpha(_theme.SecondaryText, 0x35),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row
        };
    }

    private async Task PickDeskFilesForSyncAsync(
        Button pickButton,
        Button syncButton,
        StackPanel pendingList)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0) return;

        pickButton.IsEnabled = false;
        try
        {
            foreach (var file in files)
            {
                var properties = await file.GetBasicPropertiesAsync();
                if (properties.Size <= 0 || properties.Size > 37L * 1024 * 1024)
                {
                    SetDeskStatus($"فایل «{file.Name}» از محدوده حجم مجاز فعلی (۳۷ مگابایت) خارج است و به صف اضافه نشد.", true);
                    continue;
                }

                await using var stream = await file.OpenStreamForReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                _deskPendingUploads.Add((file.Name, file.ContentType, memory.ToArray()));
            }

            RenderPendingDeskUploads(pendingList, syncButton);
            SetDeskStatus($"{_deskPendingUploads.Count} فایل در صف آماده‌سازی است. تا تأیید نهایی هیچ فایلی ارسال نمی‌شود.");
        }
        catch (Exception ex)
        {
            SetDeskStatus("افزودن فایل به صف انجام نشد: " + ex.Message, true);
        }
        finally { pickButton.IsEnabled = true; }
    }

    private void RenderPendingDeskUploads(StackPanel target, Button syncButton)
    {
        target.Children.Clear();
        if (_deskPendingUploads.Count == 0)
        {
            target.Children.Add(new TextBlock
            {
                Text = "فایلی برای همگام‌سازی انتخاب نشده است.",
                Opacity = 0.7,
                Margin = new Thickness(4, 4, 4, 8)
            });
        }

        for (var index = 0; index < _deskPendingUploads.Count; index++)
        {
            var item = _deskPendingUploads[index];
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = $"{item.Name} · {FormatDeskBytes(item.Bytes.LongLength)} · آماده ارسال پس از رمزگذاری",
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            var remove = new Button { Content = "حذف از صف", Tag = index };
            remove.Click += (_, _) =>
            {
                var removeIndex = (int)remove.Tag;
                if (removeIndex < 0 || removeIndex >= _deskPendingUploads.Count) return;
                CryptographicOperations.ZeroMemory(_deskPendingUploads[removeIndex].Bytes);
                _deskPendingUploads.RemoveAt(removeIndex);
                RenderPendingDeskUploads(target, syncButton);
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            target.Children.Add(new Border
            {
                Padding = new Thickness(8, 5, 8, 5),
                BorderBrush = BrushWithAlpha(_theme.SecondaryText, 0x35),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = row
            });
        }

        syncButton.IsEnabled = _deskPendingUploads.Count > 0;
    }

    private async Task FinalizeDeskUploadsAsync(
        Button syncButton,
        Button pickButton,
        StackPanel pendingList,
        StackPanel remoteList)
    {
        if (_deskPendingUploads.Count == 0) return;

        var count = _deskPendingUploads.Count;
        var dialog = new ContentDialog
        {
            XamlRoot = _deskWorkspace!.XamlRoot,
            Title = "تأیید همگام‌سازی",
            Content = $"آیا {count} فایل انتخاب‌شده برای رمزگذاری روی این دستگاه و ارسال امن به فضای ابری ارسال شوند؟",
            PrimaryButtonText = "رمزگذاری و ارسال",
            CloseButtonText = "بازگشت",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        syncButton.IsEnabled = pickButton.IsEnabled = false;
        var completed = 0;
        try
        {
            while (_deskPendingUploads.Count > 0)
            {
                var item = _deskPendingUploads[0];
                SetDeskStatus($"در حال رمزگذاری و ارسال فایل {completed + 1} از {count}: {item.Name}");
                await _deskClient.UploadFileAsync(item.Name, item.MimeType, item.Bytes);
                CryptographicOperations.ZeroMemory(item.Bytes);
                _deskPendingUploads.RemoveAt(0);
                completed++;
                RenderPendingDeskUploads(pendingList, syncButton);
            }

            SetDeskStatus($"{completed} فایل با موفقیت روی دستگاه رمزگذاری و به فضای ابری ارسال شد.");
            await RefreshDeskFilesAsync(remoteList);
        }
        catch (Exception ex)
        {
            SetDeskStatus($"همگام‌سازی پس از ارسال {completed} فایل متوقف شد: {ex.Message}. فایل‌های باقی‌مانده در صف حفظ شده‌اند.", true);
            StartupDiagnostics.Log("Zurvan Desk staged sync failed: " + ex);
        }
        finally
        {
            pickButton.IsEnabled = true;
            syncButton.IsEnabled = _deskPendingUploads.Count > 0;
        }
    }

    private async Task DownloadDeskFileAsync(ZurvanDeskFile file, Button button)
    {
        button.IsEnabled = false;
        try
        {
            var (bytes, mimeType) = await _deskClient.DownloadFileAsync(file);
            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedFileName = Path.GetFileName(file.Name)
                };
                var extension = Path.GetExtension(file.Name);
                if (string.IsNullOrWhiteSpace(extension) || extension.Length > 12)
                    extension = ".bin";
                picker.FileTypeChoices.Add("فایل اصلی", new List<string> { extension });
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var destination = await picker.PickSaveFileAsync();
                if (destination is null) return;
                await using var stream = await destination.OpenStreamForWriteAsync();
                stream.SetLength(0);
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                SetDeskStatus($"فایل «{file.Name}» دانلود و رمزگشایی شد.");
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) { SetDeskStatus(ex.Message, true); }
        finally { button.IsEnabled = true; }
    }

    private async Task RenameDeskFileAsync(ZurvanDeskFile file, Button button)
    {
        var input = new TextBox { Text = file.Name, MinWidth = 320 };
        var dialog = new ContentDialog
        {
            XamlRoot = _deskWorkspace!.XamlRoot,
            Title = "تغییر نام فایل",
            Content = input,
            PrimaryButtonText = "ذخیره",
            CloseButtonText = "انصراف",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary ||
            string.IsNullOrWhiteSpace(input.Text)) return;

        button.IsEnabled = false;
        try
        {
            await _deskClient.RenameFileAsync(file, input.Text);
            SetDeskStatus("نام فایل تغییر کرد.");
            await RefreshDeskFilesAsync();
        }
        catch (Exception ex) { SetDeskStatus(ex.Message, true); }
        finally { button.IsEnabled = true; }
    }

    private async Task DeleteDeskFileAsync(ZurvanDeskFile file, Button button)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _deskWorkspace!.XamlRoot,
            Title = "حذف فایل",
            Content = $"آیا از حذف «{file.Name}» مطمئن هستید؟ این عملیات قابل بازگشت نیست.",
            PrimaryButtonText = "حذف",
            CloseButtonText = "انصراف",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        button.IsEnabled = false;
        try
        {
            await _deskClient.DeleteFileAsync(file);
            SetDeskStatus("فایل حذف شد.");
            await RefreshDeskFilesAsync();
        }
        catch (Exception ex) { SetDeskStatus(ex.Message, true); }
        finally { button.IsEnabled = true; }
    }

    private async Task ShowDeskRecoveryPhraseAsync(string phrase)
    {
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "این عبارت فقط یک‌بار در زمان ساخت Vault نمایش داده می‌شود. آن را آفلاین و در مکانی امن نگه دارید. هرکس به آن دسترسی داشته باشد می‌تواند فایل‌های شما را رمزگشایی کند.",
            TextWrapping = TextWrapping.Wrap
        });
        var phraseBox = new TextBox
        {
            Text = string.Join(Environment.NewLine,
                words.Select((word, index) => $"{index + 1:00}. {word}")),
            IsReadOnly = true,
            AcceptsReturn = true,
            MinHeight = 210,
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = FlowDirection.LeftToRight
        };
        content.Children.Add(phraseBox);
        var dialog = new ContentDialog
        {
            XamlRoot = _deskWorkspace!.XamlRoot,
            Title = "عبارت بازیابی ۲۴ کلمه‌ای",
            Content = content,
            PrimaryButtonText = "عبارت را ذخیره کردم",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = true
        };
        await dialog.ShowAsync();
    }

    private static string FormatDeskBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }
}
