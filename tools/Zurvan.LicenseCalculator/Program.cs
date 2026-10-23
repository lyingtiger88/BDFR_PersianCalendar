using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BDFR.PersianCalendar.Core;

namespace Zurvan.LicenseCalculator;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new IssuerForm());
    }
}

internal sealed class IssuerForm : Form
{
    private readonly TextBox keyFile = new() { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = "Select issuer private key (*.pem)" };
    private readonly TextBox device = new() { Dock = DockStyle.Fill, CharacterCasing = CharacterCasing.Upper, PlaceholderText = "ZRV-0123456789ABCDEF01234567" };
    private readonly ComboBox edition = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox expires = new() { Text = "Expiry date", AutoSize = true };
    private readonly DateTimePicker expiry = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm", Enabled = false, Dock = DockStyle.Fill };
    private readonly TextBox activation = new() { ReadOnly = true, Dock = DockStyle.Fill, Multiline = true, Height = 120, ScrollBars = ScrollBars.Vertical, WordWrap = false };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.SteelBlue, Text = "Ready. Private keys remain on this computer." };

    public IssuerForm()
    {
        Text = "Zurvan | ماشین‌حساب فعال‌سازی";
        Width = 760; Height = 660;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(660, 580);
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(244, 248, 252);
        edition.Items.AddRange(new object[] { "standard", "pro", "enterprise" });
        edition.SelectedIndex = 0;
        expires.CheckedChanged += (_, _) => expiry.Enabled = expires.Checked;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20), ColumnCount = 1, RowCount = 0 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        AddTitle(layout, "ZURVAN   •   License Calculator");
        AddText(layout, "Offline activation code issuer. The signing key is never transmitted.");
        AddText(layout, "کلید خصوصی صادرکننده");
        var keyRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, Height = 43 };
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        keyRow.Controls.Add(keyFile, 0, 0);
        var browse = Button("انتخاب کلید");
        browse.Click += (_, _) => {
            using var dialog = new OpenFileDialog { Filter = "PEM private key (*.pem)|*.pem", Title = "Select private signing key" };
            if (dialog.ShowDialog(this) == DialogResult.OK) { keyFile.Text = dialog.FileName; status.Text = "Issuer key selected."; }
        };
        keyRow.Controls.Add(browse, 1, 0);
        Add(layout, keyRow, 48);
        var generateKeys = Button("ساخت جفت‌کلید جدید (فقط مدیر)");
        generateKeys.Click += (_, _) => GenerateKeys();
        Add(layout, generateKeys, 42);

        AddText(layout, "کد سخت‌افزاری دستگاه");
        Add(layout, device, 42);
        AddText(layout, "نوع لایسنس");
        Add(layout, edition, 42);
        var dateRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill };
        dateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        dateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dateRow.Controls.Add(expires, 0, 0); dateRow.Controls.Add(expiry, 1, 0);
        Add(layout, dateRow, 40);

        var issue = Button("تولید کد فعال‌سازی");
        issue.BackColor = Color.FromArgb(17, 95, 142); issue.ForeColor = Color.White;
        issue.Click += (_, _) => Issue();
        Add(layout, issue, 48);
        AddText(layout, "کد فعال‌سازی تولیدشده");
        Add(layout, activation, 122);
        var copy = Button("کپی کد");
        copy.Click += (_, _) => { if (activation.TextLength > 0) Clipboard.SetText(activation.Text); };
        Add(layout, copy, 40);
        Add(layout, status, 50);
    }

    private void GenerateKeys()
    {
        try
        {
            using var dialog = new FolderBrowserDialog { Description = "Select a secure local folder for the issuer keys" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var privatePath = Path.Combine(dialog.SelectedPath, "zurvan-issuer-private.pem");
            var publicPath = Path.Combine(dialog.SelectedPath, "zurvan-issuer-public.pem");
            if (File.Exists(privatePath) || File.Exists(publicPath))
                throw new InvalidOperationException("Key files already exist. Select a different folder; existing keys will not be overwritten.");
            using var rsa = RSA.Create(3072);
            File.WriteAllText(privatePath, rsa.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(publicPath, rsa.ExportSubjectPublicKeyInfoPem());
            keyFile.Text = privatePath;
            status.Text = "جفت‌کلید ایجاد شد. کلید عمومی را برای نسخه‌های مشتری تنظیم کنید.";
            MessageBox.Show(this, "Private key: " + privatePath + Environment.NewLine +
                "Public key: " + publicPath + Environment.NewLine +
                "Keep the private key offline. Never publish it.", "Keys generated",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Key generation error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Issue()
    {
        try
        {
            activation.Clear();
            if (!File.Exists(keyFile.Text)) throw new InvalidOperationException("ابتدا کلید خصوصی را انتخاب کنید.");
            var hardware = device.Text.Trim();
            if (!Regex.IsMatch(hardware, "^ZRV-[0-9A-F]{24}$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("فرمت کد دستگاه نامعتبر است.");
            string? date = expires.Checked ? new DateTimeOffset(expiry.Value, TimeZoneInfo.Local.GetUtcOffset(expiry.Value)).ToUniversalTime().ToString("O") : null;
            using var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(keyFile.Text));
            if (rsa.KeySize < 3072) throw new InvalidOperationException("کلید خصوصی باید حداقل ۳۰۷۲ بیت باشد.");
            var payload = new ZurvanLicense.Payload(ZurvanLicense.ProductId, hardware, edition.SelectedItem?.ToString() ?? "standard", date);
            activation.Text = ZurvanLicense.Issue(payload, rsa);
            status.Text = "کد فعال‌سازی امضاشده با موفقیت تولید شد.";
        }
        catch (Exception ex)
        {
            status.Text = "خطا: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Activation error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Button Button(string text) => new() { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(3) };
    private static void Add(TableLayoutPanel p, Control c, int height) {
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        p.Controls.Add(c, 0, p.RowCount++);
    }
    private static void AddText(TableLayoutPanel p, string text) => Add(p, new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 30);
    private static void AddTitle(TableLayoutPanel p, string text) => Add(p, new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Color.FromArgb(12, 66, 102) }, 51);
}
