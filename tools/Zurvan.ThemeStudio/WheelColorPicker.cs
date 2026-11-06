using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace Zurvan.ThemeStudio;

/// <summary>
/// Interactive HSV wheel with live color harmony preview.
/// Independent of any base-theme preset and safe to embed inside a scrollable editor.
/// </summary>
internal sealed class WheelColorPicker : UserControl
{
    private readonly WheelSurface wheel = new() { Dock = DockStyle.Fill, MinimumSize = new Size(185, 185) };
    private readonly ComboBox harmonies = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TrackBar brightness = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, TickFrequency = 20, Value = 100 };
    private readonly TextBox hexInput = new() { Dock = DockStyle.Fill, TextAlign = HorizontalAlignment.Center, MaxLength = 7 };
    private readonly FlowLayoutPanel harmonySwatches = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false, AutoScroll = true, BackColor = Color.Transparent
    };
    private readonly Panel currentSwatch = new() { Width = 34, Dock = DockStyle.Left };
    private readonly Label targetLabel = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
        Text = "رنگ انتخاب‌شده", Font = new Font("Segoe UI", 10, FontStyle.Bold)
    };

    private readonly (string Title, int[] Offsets)[] schemes =
    [
        ("هم‌جوار (Analogous)", [-30, 0, 30]),
        ("مکمل (Complementary)", [0, 180]),
        ("سه‌گانه (Triadic)", [0, 120, 240]),
        ("مکمل شکسته (Split Complementary)", [0, 150, 210]),
        ("چهارگانه (Tetradic)", [0, 60, 180, 240]),
        ("تک‌رنگ (Monochromatic)", [0, 0, 0])
    ];
    private bool updating;
    private double hue = 45, saturation = .82, value = 1;
    public event Action<Color>? ColorChanged;

    public WheelColorPicker()
    {
        Height = 610;
        MinimumSize = new Size(320, 610);
        BackColor = Color.White;
        Padding = new Padding(10);
        RightToLeft = RightToLeft.Yes;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 11, Dock = DockStyle.Fill,
            BackColor = Color.White, Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 34f, 258f, 23f, 45f, 23f, 38f, 23f, 48f, 22f, 36f, 38f })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        layout.Controls.Add(MakeLabel("چرخه رنگ — انتخاب مستقیم با ماوس", true), 0, 0);
        var wheelHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        wheelHost.Controls.Add(wheel);
        layout.Controls.Add(wheelHost, 0, 1);
        layout.Controls.Add(MakeLabel("روشنایی رنگ", false), 0, 2);
        layout.Controls.Add(brightness, 0, 3);
        layout.Controls.Add(MakeLabel("کد رنگ انتخاب‌شده (HEX)", false), 0, 4);

        var hexRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        hexRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        hexRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hexRow.Controls.Add(currentSwatch, 0, 0);
        hexRow.Controls.Add(hexInput, 1, 0);
        layout.Controls.Add(hexRow, 0, 5);
        layout.Controls.Add(MakeLabel("ترکیب رنگی پیشنهادی", false), 0, 6);
        harmonies.Items.AddRange(schemes.Select(s => (object)s.Title).ToArray());
        harmonies.SelectedIndex = 0;
        layout.Controls.Add(harmonies, 0, 7);
        layout.Controls.Add(targetLabel, 0, 8);
        layout.Controls.Add(harmonySwatches, 0, 9);
        var copyPalette = new Button
        {
            Text = "کپی ترکیب رنگی",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(240, 243, 252),
            ForeColor = Color.FromArgb(33, 57, 94),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        copyPalette.FlatAppearance.BorderColor = Color.FromArgb(218, 225, 240);
        copyPalette.Click += (_, _) =>
        {
            var text = string.Join(Environment.NewLine, HarmonyColors().Select(ToHex));
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "کپی پالت انجام نشد: " + ex.Message, "زروان",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        layout.Controls.Add(copyPalette, 0, 10);
        Controls.Add(layout);

        wheel.ColorPointSelected += (angle, radius) =>
        {
            hue = angle;
            saturation = radius;
            ApplyWheelColor();
        };
        brightness.ValueChanged += (_, _) =>
        {
            if (updating) return;
            value = brightness.Value / 100.0;
            ApplyWheelColor();
        };
        harmonies.SelectedIndexChanged += (_, _) => RefreshHarmonies();
        hexInput.Leave += (_, _) => CommitHex();
        hexInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            CommitHex();
            e.SuppressKeyPress = true;
        };
        SetColor(Color.FromArgb(242, 191, 13), "رنگ تأکیدی");
    }

    private static Label MakeLabel(string text, bool heading) => new()
    {
        Text = text, Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI", heading ? 11 : 9, heading ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = Color.FromArgb(38, 51, 76)
    };

    public void SetColor(Color color, string label)
    {
        updating = true;
        try
        {
            FromRgb(color, out hue, out saturation, out value);
            brightness.Value = Math.Clamp((int)Math.Round(value * 100), 0, 100);
            targetLabel.Text = "رنگ عنصر: " + label;
            UpdateVisuals(color);
        }
        finally { updating = false; }
    }

    private void ApplyWheelColor()
    {
        var color = FromHsv(hue, saturation, value);
        UpdateVisuals(color);
        if (!updating) ColorChanged?.Invoke(color);
    }

    private void UpdateVisuals(Color color)
    {
        currentSwatch.BackColor = color;
        hexInput.Text = ToHex(color);
        wheel.SetMarker(hue, saturation);
        RefreshHarmonies();
    }

    private void CommitHex()
    {
        var text = hexInput.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9a-fA-F]{6}$"))
        {
            hexInput.Text = ToHex(FromHsv(hue, saturation, value));
            return;
        }
        var color = ColorTranslator.FromHtml(text);
        SetColor(color, targetLabel.Text.Replace("رنگ عنصر: ", ""));
        ColorChanged?.Invoke(color);
    }

    private IEnumerable<Color> HarmonyColors()
    {
        var scheme = schemes[Math.Clamp(harmonies.SelectedIndex, 0, schemes.Length - 1)];
        for (int i = 0; i < scheme.Offsets.Length; i++)
        {
            var localSaturation = scheme.Title.StartsWith("تک‌رنگ", StringComparison.Ordinal)
                ? Math.Clamp(saturation * (.5 + .25 * i), 0, 1)
                : saturation;
            var localValue = scheme.Title.StartsWith("تک‌رنگ", StringComparison.Ordinal)
                ? Math.Clamp(value * (.72 + .14 * i), 0, 1)
                : value;
            yield return FromHsv((hue + scheme.Offsets[i] + 360) % 360, localSaturation, localValue);
        }
    }

    private void RefreshHarmonies()
    {
        if (IsDisposed) return;
        harmonySwatches.SuspendLayout();
        try
        {
            while (harmonySwatches.Controls.Count > 0)
            {
                var control = harmonySwatches.Controls[0];
                harmonySwatches.Controls.RemoveAt(0);
                control.Dispose();
            }
            foreach (var color in HarmonyColors())
            {
                var hex = ToHex(color);
                var chip = new Button
                {
                    Width = 85, Height = 30, Text = hex, BackColor = color,
                    ForeColor = color.GetBrightness() < .52 ? Color.White : Color.Black,
                    Font = new Font("Consolas", 8),
                    Margin = new Padding(2), FlatStyle = FlatStyle.Flat
                };
                chip.FlatAppearance.BorderColor = Color.FromArgb(204, 210, 225);
                chip.Click += (_, _) =>
                {
                    SetColor(color, targetLabel.Text.Replace("رنگ عنصر: ", ""));
                    ColorChanged?.Invoke(color);
                };
                harmonySwatches.Controls.Add(chip);
            }
        }
        finally { harmonySwatches.ResumeLayout(); }
    }

    internal static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    internal static Color FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = value - c;
        var sector = (int)(hue / 60);
        (double r, double g, double b) = sector switch
        {
            0 => (c, x, 0), 1 => (x, c, 0), 2 => (0, c, x),
            3 => (0, x, c), 4 => (x, 0, c), _ => (c, 0, x)
        };
        return Color.FromArgb(
            (int)Math.Round((r + m) * 255),
            (int)Math.Round((g + m) * 255),
            (int)Math.Round((b + m) * 255));
    }

    internal static void FromRgb(Color color, out double hue, out double saturation, out double value)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        value = max;
        saturation = max == 0 ? 0 : delta / max;
        hue = delta <= 0 ? 0 :
            max == r ? 60 * (((g - b) / delta) % 6) :
            max == g ? 60 * ((b - r) / delta + 2) :
                       60 * ((r - g) / delta + 4);
        hue = (hue + 360) % 360;
    }

    private sealed class WheelSurface : Control
    {
        private Bitmap? cachedWheel;
        private Size cachedSize;
        private double markerHue, markerSaturation;
        internal event Action<double, double>? ColorPointSelected;

        internal WheelSurface()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Cursor = Cursors.Cross;
            ResizeRedraw = true;
        }

        internal void SetMarker(double hue, double saturation)
        {
            markerHue = hue;
            markerSaturation = saturation;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) ReadPoint(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if ((e.Button & MouseButtons.Left) != 0) ReadPoint(e.Location);
        }

        private void ReadPoint(Point point)
        {
            var cx = ClientSize.Width / 2.0;
            var cy = ClientSize.Height / 2.0;
            var radius = Math.Max(1, Math.Min(ClientSize.Width, ClientSize.Height) / 2.0 - 11);
            var dx = point.X - cx;
            var dy = point.Y - cy;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var angle = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
            ColorPointSelected?.Invoke(angle, Math.Clamp(distance / radius, 0, 1));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var size = ClientSize;
            if (size.Width < 30 || size.Height < 30) return;
            if (cachedWheel is null || cachedSize != size)
            {
                cachedWheel?.Dispose();
                cachedWheel = GenerateWheel(size);
                cachedSize = size;
            }
            g.DrawImageUnscaled(cachedWheel, 0, 0);
            var radius = Math.Min(size.Width, size.Height) / 2f - 11;
            var theta = markerHue * Math.PI / 180;
            var mx = size.Width / 2f + (float)(Math.Cos(theta) * radius * markerSaturation);
            var my = size.Height / 2f - (float)(Math.Sin(theta) * radius * markerSaturation);
            var mark = new RectangleF(mx - 7, my - 7, 14, 14);
            using var pen = new Pen(Color.White, 3);
            using var shadow = new Pen(Color.FromArgb(160, 29, 40, 55), 1);
            g.DrawEllipse(pen, mark);
            g.DrawEllipse(shadow, mx - 8, my - 8, 16, 16);
        }

        private static Bitmap GenerateWheel(Size size)
        {
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            var cx = size.Width / 2.0;
            var cy = size.Height / 2.0;
            var radius = Math.Max(1, Math.Min(size.Width, size.Height) / 2.0 - 11);
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > radius + 6) continue;
                double angle = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
                if (distance <= radius)
                {
                    var saturation = Math.Clamp(distance / radius, 0, 1);
                    bmp.SetPixel(x, y, FromHsv(angle, saturation, 1));
                }
                else
                {
                    // Thin saturated outer rim like the reference image.
                    bmp.SetPixel(x, y, FromHsv(angle, 1, 1));
                }
            }
            return bmp;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) cachedWheel?.Dispose();
            base.Dispose(disposing);
        }
    }
}
