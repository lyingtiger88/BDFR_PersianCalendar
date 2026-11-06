
using BDFR.PersianCalendar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    // WinUI right-click / long-press menu for each individual Persian day.
    // Date is captured per cell, so commands never accidentally target a previously
    // selected day.
    private void AttachDayCardContextMenu(Button dayButton, PersianDate date)
    {
        var menu = new MenuFlyout();
        MenuFlyoutItem Add(string title, Symbol icon, Action action)
        {
            var item = new MenuFlyoutItem
            {
                Text = title,
                Icon = new SymbolIcon(icon)
            };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        Add("📅 ثبت رویداد و یادآور", Symbol.Calendar, () =>
        {
            SelectDayForContextAction(date);
            AddEvent_Click(dayButton, new RoutedEventArgs());
        });
        Add("✅ ثبت برنامه / کار", Symbol.Edit, () =>
        {
            _ = AddDayTaskFromContextAsync(date);
        });
        Add("🎉 ثبت مناسبت شخصی سالانه", Symbol.Favorite, () =>
        {
            _ = AddDayOccasionFromContextAsync(date);
        });
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("📝 ویرایش یادداشت این روز", Symbol.Edit, () =>
        {
            SelectDayForContextAction(date);
            _ = LoadSelectedDayAsync();
            StatusText.Text = "یادداشت روز انتخاب شد؛ از بخش یادداشت‌ها متن را ویرایش و ذخیره کنید.";
        });
        dayButton.ContextFlyout = menu;
        ToolTipService.SetToolTip(dayButton, "کلیک: مشاهده روز | راست‌کلیک: ثبت برنامه، مناسبت و رویداد");
    }

    private void SelectDayForContextAction(PersianDate date)
    {
        _selected = date;
        if (_year != date.Year || _month != date.Month)
        {
            _year = date.Year;
            _month = date.Month;
        }
        BuildCalendar();
        _ = LoadSelectedDayAsync();
    }

    private async Task AddDayTaskFromContextAsync(PersianDate date)
    {
        try
        {
            var titleBox = new TextBox
            {
                Header = "عنوان برنامه یا کار",
                PlaceholderText = "مثلاً پیگیری پروژه",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var timeBox = new TextBox
            {
                Header = "ساعت (اختیاری)",
                PlaceholderText = "09:30",
                FlowDirection = FlowDirection.LeftToRight
            };
            var form = new StackPanel { Spacing = 12, Width = 350 };
            form.Children.Add(new TextBlock
            {
                Text = $"ثبت برنامه برای {ToPersianDigits(date.Year.ToString())}/{ToPersianDigits(date.Month.ToString("00"))}/{ToPersianDigits(date.Day.ToString("00"))}",
                TextWrapping = TextWrapping.Wrap
            });
            form.Children.Add(titleBox);
            form.Children.Add(timeBox);
            var dialog = new ContentDialog
            {
                Title = "✅ برنامه جدید",
                Content = form,
                PrimaryButtonText = "ذخیره برنامه",
                CloseButtonText = "انصراف",
                DefaultButton = ContentDialogButton.Primary
            };
            if (Content is not FrameworkElement root || root.XamlRoot is null) return;
            dialog.XamlRoot = root.XamlRoot;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(titleBox.Text))
            {
                StatusText.Text = "عنوان برنامه نمی‌تواند خالی باشد.";
                return;
            }
            TimeOnly? dueTime = null;
            if (!string.IsNullOrWhiteSpace(timeBox.Text))
            {
                var normalized = PersianQuickAddParser.NormalizeDigits(timeBox.Text);
                if (!TimeOnly.TryParse(normalized, out var parsed))
                {
                    StatusText.Text = "ساعت واردشده معتبر نیست؛ از قالب 09:30 استفاده کنید.";
                    return;
                }
                dueTime = parsed;
            }
            await _planner.AddTaskAsync(titleBox.Text.Trim(), date, dueTime);
            SelectDayForContextAction(date);
            await LoadSelectedDayAsync();
            await LoadActivitiesAsync();
            StatusText.Text = "برنامه جدید برای روز انتخاب‌شده ثبت شد.";
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Day card task context failed: {ex}");
            StatusText.Text = "ثبت برنامه انجام نشد: " + ex.Message;
        }
    }

    private async Task AddDayOccasionFromContextAsync(PersianDate date)
    {
        try
        {
            var titleBox = new TextBox
            {
                Header = "عنوان مناسبت",
                PlaceholderText = "مثلاً سالگرد آشنایی",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var note = new TextBlock
            {
                Text = $"مناسبت هر سال در {ToPersianDigits(date.Day.ToString())} {PersianDate.MonthNames[date.Month - 1]} تکرار می‌شود.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            };
            var form = new StackPanel { Spacing = 12, Width = 350 };
            form.Children.Add(note);
            form.Children.Add(titleBox);
            var dialog = new ContentDialog
            {
                Title = "🎉 مناسبت شخصی جدید",
                Content = form,
                PrimaryButtonText = "ثبت مناسبت",
                CloseButtonText = "انصراف",
                DefaultButton = ContentDialogButton.Primary
            };
            if (Content is not FrameworkElement root || root.XamlRoot is null) return;
            dialog.XamlRoot = root.XamlRoot;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(titleBox.Text))
            {
                StatusText.Text = "عنوان مناسبت نمی‌تواند خالی باشد.";
                return;
            }

            await _specialOccasions.AddAnnualAsync(
                titleBox.Text.Trim(),
                CalendarSystemKind.Persian,
                date.Month,
                date.Day,
                [7, 1, 0],
                new TimeOnly(9, 0));
            SelectDayForContextAction(date);
            await LoadSelectedDayAsync();
            await LoadCalendarCellOccasionsAsync(_year, _month);
            await LoadActivitiesAsync();
            StatusText.Text = "مناسبت سالانه با یادآورهای ۷، ۱ و صفر روز قبل ثبت شد.";
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Day card occasion context failed: {ex}");
            StatusText.Text = "ثبت مناسبت انجام نشد: " + ex.Message;
        }
    }

    private FrameworkElement BuildMobileInspiredSettingsCard()
    {
        var content = new StackPanel { Spacing = 11 };
        content.Children.Add(new TextBlock
        {
            Text = "✨ شخصی‌سازی سریع زروان",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });
        content.Children.Add(new TextBlock
        {
            Text = "تم‌های رنگی مشابه موبایل؛ با انتخاب هر گزینه، پیش‌نمایش تقویم و عناصر برنامه فوراً تغییر می‌کنند.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(_theme.SecondaryText),
            FontSize = 12
        });

        var themePicker = new ComboBox
        {
            Header = "پالت و قالب ظاهری",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "انتخاب تم"
        };
        var items = new (string Code, string Label)[]
        {
            ("zara-pastel", "🌸 زارا پاستلی"),
            ("azure-glass", "💠 شیشه‌ای آبی"),
            ("windows-light", "☀️ روشن مدرن"),
            ("warm-sand", "🏜️ شنی گرم"),
            ("graphite-night", "🌙 گرافیتی تیره")
        };
        foreach (var item in items)
            themePicker.Items.Add(new ComboBoxItem { Content = item.Label, Tag = item.Code });
        if (!string.Equals(_settings.AppearanceMode, "elena", StringComparison.OrdinalIgnoreCase))
        {
            themePicker.SelectedItem = themePicker.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(x => string.Equals(x.Tag as string, _settings.ThemeId, StringComparison.OrdinalIgnoreCase));
        }
        themePicker.SelectionChanged += async (_, _) =>
        {
            if (themePicker.SelectedItem is ComboBoxItem item && item.Tag is string code &&
                (!string.Equals(_settings.AppearanceMode, "theme", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(code, _settings.ThemeId, StringComparison.OrdinalIgnoreCase)))
                await SelectThemeAsync(code);
        };
        content.Children.Add(themePicker);

        var seasonal = new Button
        {
            Content = "🍃 فعال‌سازی Elena Mode و پس‌زمینه فصلی",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = BrushWithAlpha(_theme.Accent, 0x35),
            Foreground = ThemeService.Brush(_theme.PrimaryText),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12)
        };
        seasonal.Click += async (_, _) => await SelectElenaModeAsync();
        content.Children.Add(seasonal);

        var tips = new TextBlock
        {
            Text = "💡 برای ثبت سریع مناسبت، رویداد یا برنامه، روی کارت هر روز در تقویم راست‌کلیک کنید.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        };
        content.Children.Add(tips);

        return new Border
        {
            Background = BrushWithAlpha(_theme.CardBackground, 0xE8),
            BorderBrush = BrushWithAlpha(_theme.Accent, 0x90),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(17),
            Margin = new Thickness(1, 4, 1, 14),
            Child = content
        };
    }
}
