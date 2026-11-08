using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Zurvan.ThemeStudio;

internal sealed partial class Studio
{
    private WheelColorPicker? wheelPicker;
    private readonly List<string> enhancedWallpapers = new();
    private readonly FlowLayoutPanel wallpaperThumbnails = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.White };
    private readonly ComboBox rotationMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox layoutTemplate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox targetPlatform = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Panel liveCanvas = new() { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(8) };
    private readonly Label exportHint = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label imageCount = new() { AutoSize = true, Text = "۰ تصویر انتخاب شده" };
    private readonly Label appliedHint = new() { Dock = DockStyle.Top, Height = 36, Text = "✓ تم انتخاب‌شده اعمال شد", TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(220, 245, 233), ForeColor = Color.FromArgb(34, 117, 74) };
    private string activePlatform = "pc";
    private int selectedWallpaper = -1;
    private bool initialized;
    private readonly Dictionary<string, Color[]> presets = new Dictionary<string, string[]>()
    {
        ["windows-light"] = ["#F5F6F8", "#FFFFFF", "#FFFFFF", "#202631", "#3685E6", "#D34E5E"],
        ["azure-glass"] = ["#E8F4FF", "#DDEEFF", "#FFFFFF", "#1D365A", "#338EDC", "#D34E5E"],
        ["graphite-night"] = ["#181E2A", "#293144", "#343E51", "#F2F4FA", "#628FE6", "#EF7285"],
        ["warm-sand"] = ["#FFF4E7", "#F9E7D1", "#FFF9EF", "#533E36", "#C38B5C", "#D66F5E"],
        ["zara-pastel"] = ["#F7F6FC", "#E8E0FA", "#FFFFFF", "#293049", "#789DE8", "#D56B8E"],
        ["season-vibe"] = ["#F0F9EE", "#D9F2DA", "#FFFFFF", "#234132", "#51A88A", "#D34E5E"],
        ["elena-mode"] = ["#F2F5FC", "#E7EEFB", "#FFFFFF", "#263047", "#758DCE", "#CC637B"]
    }.ToDictionary(x => x.Key, x => x.Value.Select(value => ColorTranslator.FromHtml(value)).ToArray());
    private static readonly (string Id, string Title)[] RotationOptions =
    [
        ("sequential", "ترتیبی"), ("daily", "روزانه"), ("random", "تصادفی روزانه"),
        ("monthly", "ماهانه (شمسی)"), ("seasonal", "فصلی (شمسی)")
    ];
    private static readonly (string Id, string Title)[] LayoutOptions =
    [
        ("soft-wave", "موج ملایم"), ("minimal", "مینیمال"), ("glass", "شیشه‌ای"), ("compact", "فشرده")
    ];

    private void EnhanceStudio()
    {
        SuspendLayout();
        Controls.Clear();
        Width = 1460;
        Height = 900;
        MinimumSize = new Size(1040, 720);
        BackColor = Color.FromArgb(244, 246, 251);
        Text = "Zurvan Theme Studio | استودیو ساخت تم زروان";
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1, SplitterWidth = 8,
            BackColor = Color.FromArgb(230, 233, 244)
        };
        Controls.Add(split);

        var editor = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(20, 14, 15, 16),
            BackColor = Color.FromArgb(246, 248, 253)
        };
        split.Panel1.Controls.Add(editor);
        void FitEditor(object? sender = null, EventArgs? args = null)
        {
            var width = Math.Max(380, editor.ClientSize.Width - 40);
            foreach (Control control in editor.Controls) control.Width = width;
        }
        editor.Resize += FitEditor;

        editor.Controls.Add(Header("⚙ تنظیمات تم", "رنگ‌ها، تصاویر زمینه، رفتار و خروجی تم زروان"));
        editor.Controls.Add(Heading("نام و مقصد تم"));
        editor.Controls.Add(themeName);
        themeName.Height = 38;
        editor.Controls.Add(Heading("پلتفرم طراحی"));
        targetPlatform.Items.AddRange(["ویندوز (PC)", "موبایل (Android)", "هر دو نسخه"]);
        targetPlatform.SelectedIndex = 0;
        editor.Controls.Add(targetPlatform);
        targetPlatform.Height = 38;
        editor.Controls.Add(Heading("تم پایه"));
        editor.Controls.Add(baseTheme);
        baseTheme.Height = 40;
        editor.Controls.Add(Heading("الگو (Template)"));
        layoutTemplate.Items.AddRange(LayoutOptions.Select(x => x.Title).ToArray());
        layoutTemplate.SelectedIndex = 0;
        layoutTemplate.Height = 40;
        editor.Controls.Add(Information("با انتخاب تم پایه یا الگو، تغییرات بلافاصله در پیش‌نمایش اعمال می‌شود."));

        editor.Controls.Add(Heading("🎨 پالت رنگی عناصر تم"));
        editor.Controls.Add(Information("برای تغییر رنگ هر عنصر، روی نمونه رنگی آن کلیک کنید."));
        var palette = new TableLayoutPanel
        {
            Height = 7 * 54 + 10, Dock = DockStyle.None, ColumnCount = 2, RowCount = 7,
            Padding = new Padding(0, 4, 0, 4),
            BackColor = Color.Transparent
        };
        palette.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        palette.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 7; i++) palette.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        for (int i = 0; i < Fields.Length; i++)
        {
            var field = Fields[i];
            var cell = new TableLayoutPanel
            {
                ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill,
                BackColor = Color.White, Margin = new Padding(3), Padding = new Padding(4)
            };
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            var label = new Button
            {
                Text = field.Label, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleRight, BackColor = Color.White,
                Font = new Font("Segoe UI", 9.2f)
            };
            label.FlatAppearance.BorderSize = 0;
            var swatch = new Button
            {
                BackColor = colors[field.Key], Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat, Margin = new Padding(3, 5, 3, 5)
            };
            swatch.FlatAppearance.BorderColor = Color.FromArgb(214, 219, 234);
            swatches[field.Key] = swatch;
            label.Click += (_, _) => Pick(field.Key);
            swatch.Click += (_, _) => Pick(field.Key);
            cell.Controls.Add(label, 0, 0);
            cell.Controls.Add(swatch, 1, 0);
            palette.Controls.Add(cell, i % 2, i / 2);
        }
        editor.Controls.Add(palette);

        editor.Controls.Add(Heading("🌈 چرخه رنگ و هارمونی"));
        editor.Controls.Add(Information("عنصر را از پالت بالا انتخاب کن، سپس رنگ را از چرخه بردار. ترکیب‌های مکمل، مشابه و سه‌گانه به‌صورت زنده پیشنهاد می‌شوند."));
        wheelPicker = new WheelColorPicker
        {
            Height = 610,
            Margin = new Padding(2, 4, 2, 12)
        };
        wheelPicker.ColorChanged += color =>
        {
            if (!colors.ContainsKey(selected)) return;
            colors[selected] = color;
            if (swatches.TryGetValue(selected, out var target))
                target.BackColor = color;
            hex.Text = WheelColorPicker.ToHex(color);
            liveCanvas.Invalidate();
            preview.Invalidate();
        };
        wheelPicker.SetColor(colors[selected], Fields.First(field => field.Key == selected).Label);
        editor.Controls.Add(wheelPicker);

        editor.Controls.Add(Heading("🖼 تصاویر زمینه"));
        editor.Controls.Add(Information("برای هر دو نسخه می‌توانید تصویر مناسب انتخاب کنید. تصاویر هنگام خروجی گرفتن در بسته تم قرار می‌گیرند."));
        wallpaperThumbnails.Height = 114;
        editor.Controls.Add(wallpaperThumbnails);
        var imageActions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Height = 49,
            BackColor = Color.Transparent
        };
        imageActions.Controls.Add(ActionButton("＋ افزودن تصویر", AddWallpapers, true, 150));
        imageActions.Controls.Add(ActionButton("حذف", RemoveWallpaper, false, 76));
        imageActions.Controls.Add(ActionButton("پیش‌نمایش", () => liveCanvas.Invalidate(), false, 122));
        editor.Controls.Add(imageActions);
        editor.Controls.Add(imageCount);
        editor.Controls.Add(Heading("رفتار نمایش چند تصویر"));
        editor.Controls.Add(Information("تعویض بر اساس تاریخ شمسی؛ حالت تصادفی در هر روز پایدار می‌ماند."));
        rotationMode.Items.AddRange(RotationOptions.Select(x => x.Title).ToArray());
        rotationMode.SelectedIndex = 0;
        editor.Controls.Add(rotationMode);

        editor.Controls.Add(Heading("ذخیره و خروجی تم"));
        exportHint.Text = "بدون تصویر: JSON";
        exportHint.Height = 30;
        editor.Controls.Add(exportHint);
        editor.Controls.Add(ActionButton("💾 ذخیره و خروجی گرفتن", ExportEnhanced, true, 315));
        editor.Controls.Add(ActionButton("📂 باز کردن تم JSON یا ZIP", ImportEnhanced, false, 315));
        editor.Controls.Add(ActionButton("↺ بازنشانی رنگ‌ها", ResetEnhanced, false, 315));

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3,
            BackColor = Color.FromArgb(246, 248, 253)
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        right.Controls.Add(Header("پیش‌نمایش زنده تقویم زروان", "نمونه متناسب با نسخه انتخاب‌شده"), 0, 0);
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18) };
        frame.Controls.Add(liveCanvas);
        liveCanvas.Controls.Add(appliedHint);
        liveCanvas.Paint += PaintEnhanced;
        right.Controls.Add(frame, 0, 1);
        right.Controls.Add(new Label
        {
            Text = "انتخاب هر عنصر، بخش مرتبط را در نمونه تقویم برجسته می‌کند.",
            TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, ForeColor = Color.Gray
        }, 0, 2);
        split.Panel2.Controls.Add(right);
        // The SplitContainer has not been laid out yet. Setting its panel minimums
        // and distance before the first layout can throw during Form construction.
        Shown += (_, _) =>
        {
            var available = split.ClientSize.Width - split.SplitterWidth;
            if (available < 700) return;
            var leftWidth = Math.Clamp((int)(available * 0.43), 350, available - 350);
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            split.SplitterDistance = leftWidth;
            split.Panel1MinSize = Math.Min(400, leftWidth);
            split.Panel2MinSize = Math.Min(400, available - leftWidth);
        };

        targetPlatform.SelectedIndexChanged += (_, _) =>
        {
            activePlatform = targetPlatform.SelectedIndex == 1 ? "mobile" : "pc";
            liveCanvas.Invalidate();
        };
        baseTheme.SelectedIndexChanged += (_, _) => { if (initialized) ApplyBasePreset(); };
        layoutTemplate.SelectedIndexChanged += (_, _) => { liveCanvas.Invalidate(); appliedHint.Visible = true; };
        rotationMode.SelectedIndexChanged += (_, _) => liveCanvas.Invalidate();
        initialized = true;
        ApplyBasePreset();
        RefreshThumbnails();
        FitEditor();
        ResumeLayout(true);
    }

    private static Control Header(string title, string subtitle)
    {
        var panel = new Panel { Height = 76, BackColor = Color.Transparent };
        panel.Controls.Add(new Label
        {
            Text = subtitle, Dock = DockStyle.Bottom, Height = 29,
            TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9)
        });
        panel.Controls.Add(new Label
        {
            Text = title, Dock = DockStyle.Top, Height = 38,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 49, 81)
        });
        return panel;
    }
    private static Control Heading(string text) => new Label
    {
        Text = text, Height = 32, TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI", 11, FontStyle.Bold),
        ForeColor = Color.FromArgb(39, 54, 88),
        Margin = new Padding(0, 14, 0, 2)
    };
    private static Control Information(string text) => new Label
    {
        Text = text, Height = 52, TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(92, 102, 122),
        BackColor = Color.FromArgb(237, 241, 254),
        Padding = new Padding(8), Margin = new Padding(0, 3, 0, 8)
    };
    private static Button ActionButton(string label, Action action, bool primary, int width)
    {
        var button = new Button
        {
            Text = label, Width = width, Height = 41,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 4, 2, 4),
            BackColor = primary ? Color.FromArgb(53, 110, 224) : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(45, 58, 87),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(216, 224, 240);
        button.Click += (_, _) => action();
        return button;
    }
    private void ApplyBasePreset()
    {
        var id = baseTheme.SelectedItem?.ToString() ?? "windows-light";
        if (presets.TryGetValue(id, out var values))
        {
            var keys = new[] { "background", "surface", "card", "ink", "accent", "holiday" };
            for (var i = 0; i < Math.Min(keys.Length, values.Length); i++) colors[keys[i]] = values[i];
            // Weekday shades belong to the active base template. Apply all seven
            // without indexing beyond the palette, including Friday.
            string[] zaraDays =
            [
                "#F8DDD4", "#FFF0C5", "#DFF0DD", "#D5EFED",
                "#DCE9FB", "#E8E0FA", "#F8DDE7"
            ];
            for (var i = 0; i < 7; i++)
            {
                var weekday = "weekday" + i;
                var value = id == "zara-pastel"
                    ? zaraDays[i]
                    : Fields.First(field => field.Key == weekday).Value;
                colors[weekday] = ColorTranslator.FromHtml(value);
            }
            for (var i = 0; i < Fields.Length; i++)
            {
                var k = Fields[i].Key;
                if (swatches.TryGetValue(k, out var button)) button.BackColor = colors[k];
            }
        }
        Select(selected);
        liveCanvas.Invalidate();
    }
    internal void RunPresetSmokeTest()
    {
        // Regression: selecting the sixth base theme previously accessed a
        // nonexistent seventh color and raised IndexOutOfRangeException.
        foreach (var themeId in new[] { "zara-pastel", "windows-light", "zara-pastel" })
        {
            baseTheme.SelectedItem = themeId;
            if (!string.Equals(baseTheme.SelectedItem?.ToString(), themeId, StringComparison.Ordinal))
                throw new InvalidOperationException("Theme preset could not be selected: " + themeId);
            for (var day = 0; day < 7; day++)
            {
                var key = "weekday" + day;
                if (!colors.ContainsKey(key) || !swatches.ContainsKey(key) ||
                    swatches[key].BackColor != colors[key])
                    throw new InvalidOperationException("Invalid weekday palette: " + key);
            }
        }
    }

    private void ResetEnhanced()
    {
        foreach (var field in Fields)
        {
            colors[field.Key] = ColorTranslator.FromHtml(field.Value);
            if (swatches.TryGetValue(field.Key, out var button)) button.BackColor = colors[field.Key];
        }
        Select(selected);
        liveCanvas.Invalidate();
    }
    private void AddWallpapers()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Images (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var file in dialog.FileNames)
        {
            if (new FileInfo(file).Length > 12_000_000)
            {
                MessageBox.Show(this, $"حجم تصویر بیش از ۱۲ مگابایت است: {Path.GetFileName(file)}");
                continue;
            }
            if (!enhancedWallpapers.Contains(file, StringComparer.OrdinalIgnoreCase))
                enhancedWallpapers.Add(file);
        }
        selectedWallpaper = enhancedWallpapers.Count > 0 ? enhancedWallpapers.Count - 1 : -1;
        RefreshThumbnails();
    }
    private void RemoveWallpaper()
    {
        if (selectedWallpaper < 0 || selectedWallpaper >= enhancedWallpapers.Count) return;
        enhancedWallpapers.RemoveAt(selectedWallpaper);
        selectedWallpaper = enhancedWallpapers.Count == 0 ? -1 : Math.Min(selectedWallpaper, enhancedWallpapers.Count - 1);
        RefreshThumbnails();
    }
    private void RefreshThumbnails()
    {
        foreach (Control control in wallpaperThumbnails.Controls)
        {
            if (control is PictureBox picture) { var image = picture.Image; picture.Image = null; image?.Dispose(); }
            control.Dispose();
        }
        wallpaperThumbnails.Controls.Clear();
        for (var i = 0; i < enhancedWallpapers.Count; i++)
        {
            var index = i;
            var box = new PictureBox
            {
                Width = 116, Height = 88, SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = i == selectedWallpaper ? Color.FromArgb(213, 227, 255) : Color.FromArgb(240, 242, 248),
                Padding = new Padding(4), Margin = new Padding(4), BorderStyle = BorderStyle.FixedSingle
            };
            try
            {
                using var source = Image.FromFile(enhancedWallpapers[i]);
                box.Image = new Bitmap(source);
            }
            catch { /* Preview unavailable; keep the image path for error handling during export. */ }
            box.Click += (_, _) => { selectedWallpaper = index; RefreshThumbnails(); };
            wallpaperThumbnails.Controls.Add(box);
        }
        imageCount.Text = $"{enhancedWallpapers.Count} تصویر انتخاب شده";
        exportHint.Text = enhancedWallpapers.Count == 0 ? "خروجی بدون تصویر: JSON" : "خروجی همراه تصاویر: ZIP";
        liveCanvas.Invalidate();
    }

    private static DateTime PersianDate() => DateTime.Now;
    private int ChooseWallpaperIndex()
    {
        if (enhancedWallpapers.Count == 0) return -1;
        var now = PersianDate();
        var calendar = new PersianCalendar();
        var year = calendar.GetYear(now);
        var month = calendar.GetMonth(now);
        var day = calendar.GetDayOfMonth(now);
        var count = enhancedWallpapers.Count;
        return rotationMode.SelectedIndex switch
        {
            1 => (day - 1) % count,
            2 => (int)((uint)HashCode.Combine(year, month, day, themeName.Text) % (uint)count),
            3 => (month - 1) % count,
            4 => ((month - 1) / 3) % count,
            _ => selectedWallpaper >= 0 ? selectedWallpaper : 0
        };
    }

    private static string NormalizeExtension(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            throw new InvalidDataException("پسوند تصویر پشتیبانی نمی‌شود.");
        return ext;
    }
    private ThemeFile SnapshotEnhanced(List<string> filenames)
    {
        var data = Snapshot();
        data.Platform = targetPlatform.SelectedIndex == 2 ? "both" : activePlatform;
        data.Template = LayoutOptions[Math.Max(layoutTemplate.SelectedIndex, 0)].Id;
        data.Rotation = RotationOptions[Math.Max(rotationMode.SelectedIndex, 0)].Id;
        data.Wallpapers = filenames.Select(file => new ThemeWallpaper { File = file, Fit = "cover" }).ToList();
        data.Wallpaper = data.Wallpapers.FirstOrDefault();
        return data;
    }
    private void ExportEnhanced()
    {
        if (string.IsNullOrWhiteSpace(themeName.Text))
        {
            MessageBox.Show(this, "نام تم را وارد کنید.");
            return;
        }
        var zip = enhancedWallpapers.Count != 0;
        using var dialog = new SaveFileDialog
        {
            Filter = zip ? "Zurvan theme ZIP (*.zip)|*.zip" : "Zurvan theme JSON (*.json)|*.json",
            FileName = zip ? "zurvan-theme.zip" : "zurvan-theme.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var filenames = enhancedWallpapers.Select((file, i) => $"wallpapers/background-{i + 1:000}{NormalizeExtension(file)}").ToList();
            var data = SnapshotEnhanced(filenames);
            var json = JsonSerializer.Serialize(data, jsonOptions);
            if (!zip) File.WriteAllText(dialog.FileName, json);
            else
            {
                using var stream = File.Create(dialog.FileName);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                var entry = archive.CreateEntry("theme.json");
                using (var writer = new StreamWriter(entry.Open())) writer.Write(json);
                for (var i = 0; i < filenames.Count; i++)
                {
                    if (!File.Exists(enhancedWallpapers[i]) || new FileInfo(enhancedWallpapers[i]).Length > 12_000_000)
                        throw new InvalidDataException("یکی از تصاویر یافت نشد یا بزرگ‌تر از حد مجاز است.");
                    archive.CreateEntryFromFile(enhancedWallpapers[i], filenames[i], CompressionLevel.Optimal);
                }
            }
            MessageBox.Show(this, "تم با موفقیت ذخیره شد.", "زروان");
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "خطای ذخیره", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private void ImportEnhanced()
    {
        using var dialog = new OpenFileDialog { Filter = "Zurvan themes (*.json;*.zip)|*.json;*.zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var zip = dialog.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            string json;
            var extracted = new List<string>();
            using (var archive = zip ? ZipFile.OpenRead(dialog.FileName) : null)
            {
                if (zip)
                {
                    var entry = archive!.GetEntry("theme.json") ?? throw new InvalidDataException("theme.json یافت نشد.");
                    if (entry.Length > 128_000) throw new InvalidDataException("اطلاعات تم بیش از اندازه بزرگ است.");
                    using var reader = new StreamReader(entry.Open());
                    json = reader.ReadToEnd();
                }
                else
                {
                    if (new FileInfo(dialog.FileName).Length > 128_000) throw new InvalidDataException("فایل JSON بزرگ است.");
                    json = File.ReadAllText(dialog.FileName);
                }
                var data = JsonSerializer.Deserialize<ThemeFile>(json, jsonOptions) ?? throw new InvalidDataException("ساختار تم نامعتبر است.");
                if (data.SchemaVersion != 1 || data.Format != "zurvan-android-theme") throw new InvalidDataException("فرمت تم پشتیبانی نمی‌شود.");
                if (data.Colors == null || Fields.Any(f => !data.Colors.TryGetValue(f.Key, out var value) || !Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$")))
                    throw new InvalidDataException("اطلاعات رنگ‌ها ناقص است.");
                if (!baseTheme.Items.Contains(data.ThemeId)) throw new InvalidDataException("تم پایه نامعتبر است.");
                var assets = (data.Wallpapers?.Count ?? 0) != 0 ? data.Wallpapers! : data.Wallpaper is null ? new List<ThemeWallpaper>() : [data.Wallpaper];
                if (assets.Count > 100) throw new InvalidDataException("حداکثر ۱۰۰ تصویر مجاز است.");
                if (assets.Count > 0 && !zip) throw new InvalidDataException("تم تصویردار باید به صورت ZIP وارد شود.");
                foreach (var asset in assets)
                {
                    if (!Regex.IsMatch(asset.File, @"^(wallpapers/background-\d{3}|wallpaper)\.(jpg|jpeg|png|webp)$", RegexOptions.IgnoreCase))
                        throw new InvalidDataException("مسیر تصویر نامعتبر است.");
                    var entry = archive!.GetEntry(asset.File) ?? throw new InvalidDataException("تصویر موجود نیست: " + asset.File);
                    if (entry.Length > 12_000_000) throw new InvalidDataException("حجم تصویر زیاد است.");
                    var local = Path.Combine(Path.GetTempPath(), "zurvan-studio-" + Guid.NewGuid().ToString("N") + Path.GetExtension(asset.File));
                    entry.ExtractToFile(local);
                    extracted.Add(local);
                }
                baseTheme.SelectedItem = data.ThemeId;
                themeName.Text = data.Name;
                foreach (var field in Fields)
                {
                    colors[field.Key] = ColorTranslator.FromHtml(data.Colors[field.Key]);
                    swatches[field.Key].BackColor = colors[field.Key];
                }
                targetPlatform.SelectedIndex = data.Platform switch { "mobile" => 1, "both" => 2, _ => 0 };
                var templateIndex = Array.FindIndex(LayoutOptions, x => x.Id == data.Template);
                layoutTemplate.SelectedIndex = templateIndex < 0 ? 0 : templateIndex;
                var rotationIndex = Array.FindIndex(RotationOptions, x => x.Id == data.Rotation);
                rotationMode.SelectedIndex = rotationIndex < 0 ? 0 : rotationIndex;
                enhancedWallpapers.Clear();
                enhancedWallpapers.AddRange(extracted);
                selectedWallpaper = extracted.Count == 0 ? -1 : 0;
                RefreshThumbnails();
            }
            Select(selected);
            liveCanvas.Invalidate();
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "خطای بارگذاری", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void PaintEnhanced(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = liveCanvas.ClientRectangle;
        if (bounds.Width < 160 || bounds.Height < 200) return;
        g.Clear(Color.FromArgb(238, 243, 255));

        if (targetPlatform.SelectedIndex == 2)
        {
            // Side-by-side desktop and mobile previews share the exact same
            // colors, wallpaper, template and live editing state.
            const int gap = 18;
            var paneWidth = (bounds.Width - gap - 32) / 2;
            if (paneWidth < 170) return;
            var left = new Rectangle(bounds.X + 8, bounds.Y + 26, paneWidth, bounds.Height - 34);
            var right = new Rectangle(left.Right + gap, bounds.Y + 26, paneWidth, bounds.Height - 34);
            DrawPlatformPreview(g, left, false);
            DrawPlatformPreview(g, right, true);
            using var labelFont = new Font("Segoe UI", 9, FontStyle.Bold);
            using var labelBrush = new SolidBrush(Color.FromArgb(54, 70, 105));
            g.DrawString("پیش‌نمایش دسکتاپ", labelFont, labelBrush, left.X + 8, left.Y + 2);
            g.DrawString("پیش‌نمایش موبایل", labelFont, labelBrush, right.X + 8, right.Y + 2);
            return;
        }

        DrawPlatformPreview(g, bounds, targetPlatform.SelectedIndex == 1);
    }

    private void DrawPlatformPreview(Graphics g, Rectangle bounds, bool mobile)
    {
        if (bounds.Width < 160 || bounds.Height < 200) return;
var w = mobile ? Math.Min(360, bounds.Width - 38) : Math.Min(bounds.Width - 28, 950);
        var h = mobile ? Math.Min(bounds.Height - 34, 690) : Math.Min(bounds.Height - 32, 660);
        var rect = new Rectangle(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
        using (var shadow = new SolidBrush(Color.FromArgb(30, Color.Black)))
            g.FillRounded(shadow, new Rectangle(rect.X + 5, rect.Y + 7, rect.Width, rect.Height), 20);
        using (var fill = new SolidBrush(colors["background"])) g.FillRounded(fill, rect, mobile ? 25 : 18);
        var background = ChooseWallpaperIndex();
        if (background >= 0)
        {
            try
            {
                using var bitmap = Image.FromFile(enhancedWallpapers[background]);
                var factor = Math.Max((float)rect.Width / bitmap.Width, (float)rect.Height / bitmap.Height);
                var rw = (int)(bitmap.Width * factor);
                var rh = (int)(bitmap.Height * factor);
                var clipState = g.Save();
                using var path = RoundedGraphicsExtensions.Rounded(rect, mobile ? 25 : 18);
                g.SetClip(path);
                g.DrawImage(bitmap, new Rectangle(rect.X + (rect.Width - rw) / 2, rect.Y + (rect.Height - rh) / 2, rw, rh));
                using var wash = new SolidBrush(Color.FromArgb(185, colors["background"]));
                g.FillRectangle(wash, rect);
                g.Restore(clipState);
            }
            catch { /* Ignore a broken thumbnail without crashing the editor. */ }
        }
        var inset = mobile ? 14 : 26;
        var cx = rect.X + inset;
        var cy = rect.Y + inset;
        var cw = rect.Width - 2 * inset;
        var hh = mobile ? 84 : 116;
        using (var headerBrush = new SolidBrush(colors["surface"]))
            g.FillRounded(headerBrush, new Rectangle(cx, cy, cw, hh), 17);
        using var inkBrush = new SolidBrush(colors["ink"]);
        using var headingFont = new Font("Segoe UI", mobile ? 14 : 22, FontStyle.Bold);
        using var normalFont = new Font("Segoe UI", mobile ? 9 : 11, FontStyle.Regular);
        using var smallFont = new Font("Segoe UI", mobile ? 8 : 9);
        void Center(string text, Font font, Brush brush, Rectangle r)
        {
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.DirectionRightToLeft };
            g.DrawString(text, font, brush, r, fmt);
        }
        Center("تقویم زروان", headingFont, inkBrush, new Rectangle(cx, cy + 9, cw, hh / 2));
        Center("فروردین ۱۴۰۵", normalFont, inkBrush, new Rectangle(cx, cy + hh / 2, cw, 34));
        var gridTop = cy + hh + (mobile ? 14 : 24);
        var col = cw / 7f;
        var cellH = mobile ? Math.Min(45f, (h - hh - 190) / 5.3f) : Math.Min(72f, (h - hh - 230) / 5.0f);
        if (cellH < 16) cellH = 16;
        string[] days = ["ش", "ی", "د", "س", "چ", "پ", "ج"];
        for (int i = 0; i < 7; i++)
        {
            var x = cx + (int)((6 - i) * col);
            using var dayFill = new SolidBrush(colors["weekday" + i]);
            var dayHeader = new Rectangle(x + 2, gridTop, (int)col - 4, mobile ? 27 : 37);
            g.FillRounded(dayFill, dayHeader, 9);
            Center(days[i], normalFont, inkBrush, dayHeader);
            for (int row = 0; row < 4; row++)
            {
                var y = gridTop + (mobile ? 31 : 43) + (int)(row * (cellH + 7));
                var box = new Rectangle(x + 2, y, (int)col - 4, (int)cellH);
                g.FillRounded(dayFill, box, 9);
                if (row == 2 && i == 6)
                {
                    using var outline = new Pen(colors["holiday"], 2);
                    g.DrawRounded(outline, box, 9);
                }
                Center((row * 7 + i + 1).ToString("00"), smallFont, inkBrush, box);
            }
        }
        var bottom = gridTop + (mobile ? 31 : 43) + (int)(4 * (cellH + 7)) + (mobile ? 12 : 18);
        var buttonRect = new Rectangle(cx + (mobile ? 0 : cw / 2), bottom, mobile ? cw : cw / 2, mobile ? 39 : 48);
        using (var accentBrush = new SolidBrush(colors["accent"])) g.FillRounded(accentBrush, buttonRect, 12);
        using var white = new SolidBrush(Color.White);
        Center("＋ رویداد جدید", normalFont, white, buttonRect);
        var eventRect = new Rectangle(cx, bottom + (mobile ? 53 : 67), cw, 57);
        if (eventRect.Bottom < rect.Bottom - (mobile ? 50 : 20))
        {
            using var cardBrush = new SolidBrush(colors["card"]);
            g.FillRounded(cardBrush, eventRect, 12);
            Center("جلسه تیم طراحی     ۱۰:۰۰ - ۱۱:۳۰", normalFont, inkBrush, eventRect);
        }
        if (mobile)
        {
            var nav = new Rectangle(cx, rect.Bottom - 54, cw, 43);
            using var navBrush = new SolidBrush(colors["surface"]);
            g.FillRounded(navBrush, nav, 13);
            Center("تقویم       برنامه‌ها       یادآورها       تنظیمات", smallFont, inkBrush, nav);
        }
        if (layoutTemplate.SelectedIndex == 2)
        {
            using var outline = new Pen(Color.FromArgb(110, colors["accent"]), 2);
            g.DrawRounded(outline, new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4), 20);
        }
    
    }
}

internal static class RoundedGraphicsExtensions
{
    public static GraphicsPath Rounded(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(2, radius * 2);
        path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90);
        path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90);
        path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
    public static void FillRounded(this Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = Rounded(rect, radius);
        g.FillPath(brush, path);
    }
    public static void DrawRounded(this Graphics g, Pen pen, Rectangle rect, int radius)
    {
        using var path = Rounded(rect, radius);
        g.DrawPath(pen, path);
    }
}
