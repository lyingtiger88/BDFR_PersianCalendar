using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Zurvan.ThemeStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new Studio());
    }
}

internal sealed class ThemeFile
{
    public int SchemaVersion { get; set; } = 1;
    public string Format { get; set; } = "zurvan-android-theme";
    public string Name { get; set; } = "تم اختصاصی";
    public string ThemeId { get; set; } = "windows-light";
    public Dictionary<string, string> Colors { get; set; } = new();
    public ThemeWallpaper? Wallpaper { get; set; }
}

internal sealed class ThemeWallpaper
{
    public string File { get; set; } = "wallpaper.jpg";
    public string Fit { get; set; } = "cover";
}

internal sealed class Studio : Form
{
    private static readonly (string Key, string Label, string Value)[] Fields =
    [
        ("background", "پس‌زمینه اصلی", "#F5F6F8"),
        ("surface", "نوار بالا و پنل‌ها", "#FFFFFF"),
        ("card", "کارت‌ها", "#FFFFFF"),
        ("ink", "متن و نوشته‌ها", "#202631"),
        ("accent", "دکمه‌ها و رنگ تأکیدی", "#9DC7F5"),
        ("holiday", "روز تعطیل و هشدار", "#D34E5E"),
        ("weekday0", "شنبه", "#F8DDD4"),
        ("weekday1", "یکشنبه", "#FFF0C5"),
        ("weekday2", "دوشنبه", "#DFF0DD"),
        ("weekday3", "سه‌شنبه", "#D5EFED"),
        ("weekday4", "چهارشنبه", "#DCE9FB"),
        ("weekday5", "پنجشنبه", "#E8E0FA"),
        ("weekday6", "جمعه", "#F8DDE7")
    ];
    private readonly Dictionary<string, Color> colors = Fields.ToDictionary(x => x.Key, x => ColorTranslator.FromHtml(x.Value));
    private readonly Dictionary<string, Button> swatches = new();
    private readonly ComboBox baseTheme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox themeName = new() { Dock = DockStyle.Fill, Text = "تم اختصاصی من" };
    private readonly Label selectedLabel = new() { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Label hex = new() { AutoSize = true, Font = new Font("Consolas", 12, FontStyle.Bold) };
    private readonly Panel preview = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private string selected = "accent";
    private string? wallpaperFile;
    private readonly Label wallpaperLabel = new() { AutoSize = true, Text = "بدون تصویر اختصاصی" };
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public Studio()
    {
        Text = "Zurvan Theme Studio | استودیو ساخت تم زروان";
        Width = 1120; Height = 780; MinimumSize = new Size(890, 650);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(247, 246, 250);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        baseTheme.Items.AddRange(["windows-light", "azure-glass", "graphite-night", "warm-sand", "zara-pastel", "season-vibe", "elena-mode"]);
        baseTheme.SelectedIndex = 0;
        baseTheme.SelectedIndexChanged += (_, _) => {
            if (baseTheme.SelectedItem?.ToString() == "zara-pastel") ApplyZaraPastel();
        };

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 380, FixedPanel = FixedPanel.Panel1 };
        Controls.Add(split);
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, AutoScroll = true };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.Panel1.Controls.Add(editor);
        Add(editor, new Label { Text = "استودیو طراحی تم اندروید", Font = new Font("Segoe UI", 17, FontStyle.Bold), Height = 54, TextAlign = ContentAlignment.MiddleRight }, 60);
        Add(editor, new Label { Text = "نام تم", TextAlign = ContentAlignment.MiddleRight }, 32);
        Add(editor, themeName, 42);
        Add(editor, new Label { Text = "تم پایه در برنامه اندروید", TextAlign = ContentAlignment.MiddleRight }, 32);
        Add(editor, baseTheme, 42);
        Add(editor, new Label { Text = "انتخاب رنگ عناصر (برای تغییر کلیک کنید)", Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight }, 44);
        foreach (var field in Fields)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var label = new Button { Text = field.Label, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleRight, BackColor = Color.White };
            var swatch = new Button { Dock = DockStyle.Fill, BackColor = colors[field.Key], FlatStyle = FlatStyle.Flat };
            swatches[field.Key] = swatch;
            label.Click += (_, _) => Pick(field.Key);
            swatch.Click += (_, _) => Pick(field.Key);
            row.Controls.Add(label, 0, 0);
            row.Controls.Add(swatch, 1, 0);
            Add(editor, row, 45);
        }
        Add(editor, new Label { Text = "تصویر اختصاصی تم", Font = new Font("Segoe UI", 11, FontStyle.Bold) }, 30);
        Add(editor, wallpaperLabel, 35);
        Add(editor, MakeButton("انتخاب تصویر", ChooseWallpaper), 45);
        Add(editor, MakeButton("حذف تصویر تم", () => { wallpaperFile = null; wallpaperLabel.Text = "بدون تصویر اختصاصی"; preview.Invalidate(); }), 45);
        Add(editor, selectedLabel, 35);
        Add(editor, hex, 35);
        Add(editor, MakeButton("خروجی هوشمند JSON / ZIP", Export), 45);
        Add(editor, MakeButton("باز کردن تم از JSON یا ZIP", Import), 45);
        Add(editor, MakeButton("بازنشانی رنگ‌ها", Reset), 45);
        Add(editor, new Label { Text = "خروجی با کلیدهای واقعی تم اندروید زروان سازگار است. فایل را برای اعمال در نسخه بعدی ارسال کنید.", ForeColor = Color.DimGray, Dock = DockStyle.Fill, AutoSize = true }, 64);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 3, ColumnCount = 1 };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        right.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "پیش‌نمایش زنده تقویم زروان", Font = new Font("Segoe UI", 16, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
        right.Controls.Add(preview, 0, 1);
        right.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "انتخاب هر عنصر، بخش مرتبط را در نمونه تقویم برجسته می‌کند.", ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleCenter }, 0, 2);
        split.Panel2.Controls.Add(right);
        preview.Paint += PaintPreview;
        Resize += (_, _) => preview.Invalidate();
        Select("accent");
    }

    private void Select(string key)
    {
        selected = key;
        selectedLabel.Text = "بخش در حال تغییر: " + Fields.First(x => x.Key == key).Label;
        hex.Text = ColorTranslator.ToHtml(colors[key]).ToUpperInvariant();
        preview.Invalidate();
    }

    private void Pick(string key)
    {
        Select(key);
        using var dialog = new ColorDialog { FullOpen = true, AnyColor = true, Color = colors[key] };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        colors[key] = dialog.Color;
        swatches[key].BackColor = dialog.Color;
        Select(key);
    }

    private void ApplyZaraPastel()
    {
        var pastels = new[] { "#F8DDD4", "#FFF0C5", "#DFF0DD", "#D5EFED", "#DCE9FB", "#F8DDE7" };
        for (var i = 0; i < 7; i++)
        {
            var key = "weekday" + i;
            colors[key] = ColorTranslator.FromHtml(pastels[i]);
            if (swatches.TryGetValue(key, out var button)) button.BackColor = colors[key];
        }
        Select(selected);
    }

    private void Reset()
    {
        if (MessageBox.Show(this, "همه رنگ‌ها به تنظیمات پیش‌فرض برگردند؟", "بازنشانی", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        foreach (var f in Fields) { colors[f.Key] = ColorTranslator.FromHtml(f.Value); swatches[f.Key].BackColor = colors[f.Key]; }
        Select(selected);
    }

    private ThemeFile Snapshot() => new()
    {
        Name = themeName.Text.Trim(),
        ThemeId = baseTheme.SelectedItem?.ToString() ?? "windows-light",
        Colors = Fields.ToDictionary(x => x.Key, x => $"#{colors[x.Key].R:X2}{colors[x.Key].G:X2}{colors[x.Key].B:X2}")
    };

    private void ChooseWallpaper()
    {
        using var dialog = new OpenFileDialog { Filter = "Images (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (new FileInfo(dialog.FileName).Length > 12_000_000) {
            MessageBox.Show(this, "حداکثر حجم تصویر ۱۲ مگابایت است."); return;
        }
        wallpaperFile = dialog.FileName;
        wallpaperLabel.Text = Path.GetFileName(wallpaperFile);
        preview.Invalidate();
    }

    private void Export()
    {
        var zip = wallpaperFile != null;
        if (string.IsNullOrWhiteSpace(themeName.Text)) { MessageBox.Show(this, "نام تم را وارد کنید."); return; }
        using var dialog = new SaveFileDialog {
            Filter = zip ? "Zurvan theme ZIP (*.zip)|*.zip" : "Zurvan theme JSON (*.json)|*.json",
            FileName = zip ? "zurvan-theme.zip" : "zurvan-theme.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var snapshot = Snapshot();
        var wallpaperName = wallpaperFile == null ? null : "wallpaper" + Path.GetExtension(wallpaperFile).ToLowerInvariant();
        snapshot.Wallpaper = wallpaperName == null ? null : new ThemeWallpaper { File = wallpaperName };
        var data = JsonSerializer.Serialize(snapshot, jsonOptions);
        try {
            if (!zip) File.WriteAllText(dialog.FileName, data);
            else {
                using var stream = File.Create(dialog.FileName);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                var entry = archive.CreateEntry("theme.json");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(data);
                writer.Flush();
                if (wallpaperFile != null && wallpaperName != null)
                    archive.CreateEntryFromFile(wallpaperFile, wallpaperName, CompressionLevel.Optimal);
            }
            MessageBox.Show(this, "فایل تم با موفقیت ذخیره شد.", "زروان");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog { Filter = "Zurvan theme (*.json;*.zip)|*.json;*.zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try {
            string json;
            if (Path.GetExtension(dialog.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase)) {
                using var archive = ZipFile.OpenRead(dialog.FileName);
                var entry = archive.GetEntry("theme.json") ?? throw new InvalidDataException("theme.json در فایل ZIP پیدا نشد.");
                if (entry.Length > 128_000) throw new InvalidDataException("فایل تم بیش از اندازه بزرگ است.");
                using var reader = new StreamReader(entry.Open());
                json = reader.ReadToEnd();
            }
            else {
                if (new FileInfo(dialog.FileName).Length > 128_000) throw new InvalidDataException("فایل تم بیش از اندازه بزرگ است.");
                json = File.ReadAllText(dialog.FileName);
            }
            var data = JsonSerializer.Deserialize<ThemeFile>(json, jsonOptions) ?? throw new InvalidDataException("فایل JSON نامعتبر است.");
            if (data.Format != "zurvan-android-theme" || data.SchemaVersion != 1) throw new InvalidDataException("فرمت تم پشتیبانی نمی‌شود.");
            if (data.Colors == null || Fields.Any(f => !data.Colors.TryGetValue(f.Key, out var value) || !Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$")))
                throw new InvalidDataException("رنگ‌های تم ناقص یا نامعتبر هستند.");
            if (!baseTheme.Items.Contains(data.ThemeId)) throw new InvalidDataException("تم پایه ناشناخته است.");
            foreach (var f in Fields) { colors[f.Key] = ColorTranslator.FromHtml(data.Colors[f.Key]); swatches[f.Key].BackColor = colors[f.Key]; }
            themeName.Text = data.Name;
            baseTheme.SelectedItem = data.ThemeId;
            Select(selected);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "خطا در بارگذاری", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void PaintPreview(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var w = preview.ClientSize.Width;
        var h = preview.ClientSize.Height;
        if (w < 100 || h < 150) return;
        g.Clear(colors["background"]);
        if (wallpaperFile != null && File.Exists(wallpaperFile))
        {
            try {
                using var image = Image.FromFile(wallpaperFile);
                var factor = Math.Max((float)w / image.Width, (float)h / image.Height);
                var width = (int)(image.Width * factor);
                var height = (int)(image.Height * factor);
                g.DrawImage(image, new Rectangle((w - width) / 2, (h - height) / 2, width, height));
                using var veil = new SolidBrush(Color.FromArgb(145, colors["background"]));
                g.FillRectangle(veil, 0, 0, w, h);
            } catch { /* a corrupt preview image never prevents exporting colors */ }
        }
        using var ink = new SolidBrush(colors["ink"]);
        using var heading = new Font("Segoe UI", 15, FontStyle.Bold);
        using var body = new Font("Segoe UI", 10);
        using var tiny = new Font("Segoe UI", 9);
        void Fill(Color c, Rectangle r) { using var b = new SolidBrush(c); g.FillRectangle(b, r); }
        void Label(string text, Font font, Brush brush, Rectangle r) =>
            g.DrawString(text, font, brush, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.DirectionRightToLeft });
        var header = new Rectangle(0, 0, w, 76);
        Fill(colors["surface"], header);
        Label("تقویم زروان", heading, ink, header);
        Label("فروردین ۱۴۰۳", heading, ink, new Rectangle(10, 89, w - 20, 50));
        var col = (w - 36) / 7;
        var names = new[] { "ش", "ی", "د", "س", "چ", "پ", "ج" };
        for (var i = 0; i < 7; i++) {
            var x = 18 + i * col;
            Fill(colors["weekday" + i], new Rectangle(x + 2, 155, col - 4, 28));
            Label(names[i], body, ink, new Rectangle(x, 155, col, 28));
            for (var row = 0; row < 4; row++) {
                var rect = new Rectangle(x + 3, 194 + row * 49, col - 6, 39);
                Fill(colors["weekday" + i], rect);
                if (i == 6 && row == 2) {
                    using var border = new Pen(colors["holiday"], 2);
                    g.DrawRectangle(border, rect);
                }
                Label((row * 7 + i + 1).ToString("00"), body, ink, rect);
            }
        }
        var btn = new Rectangle(w / 2, Math.Min(h - 147, 415), Math.Max(130, w / 2 - 20), 44);
        Fill(colors["accent"], btn);
        using var whiteBrush = new SolidBrush(Color.White);
        Label("+ رویداد جدید", body, whiteBrush, btn);
        var card = new Rectangle(20, Math.Min(h - 93, 477), w - 40, 62);
        Fill(colors["card"], card);
        Label("جلسه تیم طراحی       ۱۰:۰۰ - ۱۱:۳۰", body, ink, card);
        var highlight = selected switch {
            "surface" => header, "accent" => btn, "card" => card,
            "holiday" => new Rectangle(18 + 6 * col + 3, 194 + 2 * 49, col - 6, 39),
            var k when k.StartsWith("weekday") => new Rectangle(18 + int.Parse(k[^1..]) * col + 3, 194, col - 6, 4 * 49),
            _ => new Rectangle(0, 0, w - 1, h - 1)
        };
        using var pen = new Pen(Color.FromArgb(225, 213, 59, 120), 3) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, highlight);
    }

    private static Button MakeButton(string text, Action action) {
        var button = new Button { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
        button.Click += (_, _) => action();
        return button;
    }
    private static void Add(TableLayoutPanel p, Control c, int height)
    {
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        p.Controls.Add(c, 0, p.RowCount++);
    }
}
