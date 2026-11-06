using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using Microsoft.Win32;
using BDFR.PersianCalendar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private FrameworkElement? _licensePanelElement;
    private TextBox? _licenseActivationBox;
    private TextBlock? _licenseStatusText;

    private string GetWindowsDeviceCode()
    {
        // Prefer the Windows installation MachineGuid, matching Android's use of
        // a platform installation identifier. Keep a per-install fallback for
        // restricted registry environments.
        var machineId = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
            "MachineGuid",
            null) as string;
        var installationId = !string.IsNullOrWhiteSpace(machineId)
            ? machineId.Trim()
            : _settings.LicenseInstallationId;
        if (string.IsNullOrWhiteSpace(installationId))
        {
            installationId = Guid.NewGuid().ToString("N");
            _settings.LicenseInstallationId = installationId;
            _settingsService.Save(_settings);
        }
        return ZurvanLicense.DeviceCode("windows", installationId);
    }

    private bool IsWindowsLicensed()
    {
        var code = _settings.LicenseActivationCode;
        if (string.IsNullOrWhiteSpace(code)) return false;
        var pem = ReadLicensePublicKey();
        if (string.IsNullOrWhiteSpace(pem)) return false;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return ZurvanLicense.TryVerify(code, GetWindowsDeviceCode(), rsa, out _);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Windows license verification failed: {ex}");
            return false;
        }
    }

    private static string ReadLicensePublicKey()
    {
        try
        {
            var value = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(x => x.Key == "LicensePublicKeyB64")?.Value ?? "";
            if (string.IsNullOrWhiteSpace(value)) return "";
            return Encoding.UTF8.GetString(Convert.FromBase64String(value.Trim()));
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Windows license public key is unavailable: {ex.Message}");
            return "";
        }
    }

    private FrameworkElement BuildLicensePanel()
    {
        var body = new StackPanel { Spacing = 12, Padding = new Thickness(16) };
        body.Children.Add(new TextBlock
        {
            Text = "🔐 لایسنس و فعال‌سازی زروان",
            FontSize = 19,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });
        body.Children.Add(new TextBlock
        {
            Text = "لایسنس زروان به این نصب ویندوز متصل است. کد دستگاه را برای صادرکننده ارسال کنید و کد امضاشده دریافتی را در کادر زیر وارد کنید.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        var deviceCode = GetWindowsDeviceCode();
        body.Children.Add(new TextBox
        {
            Header = "شناسه دستگاه",
            Text = deviceCode,
            IsReadOnly = true,
            FlowDirection = FlowDirection.LeftToRight
        });
        var copy = MakeButton("کپی شناسه دستگاه");
        copy.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(deviceCode);
            Clipboard.SetContent(package);
            if (_licenseStatusText is not null) _licenseStatusText.Text = "شناسه دستگاه کپی شد.";
        };
        body.Children.Add(copy);

        _licenseActivationBox = new TextBox
        {
            Header = "کد فعال‌سازی",
            PlaceholderText = "ZRV1.…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            MaxLength = 8192,
            FlowDirection = FlowDirection.LeftToRight,
            Text = _settings.LicenseActivationCode
        };
        body.Children.Add(_licenseActivationBox);

        var activate = MakeButton("فعال‌سازی زروان");
        activate.Click += (_, _) =>
        {
            var entered = _licenseActivationBox.Text?.Trim() ?? "";
            var publicKey = ReadLicensePublicKey();
            if (string.IsNullOrWhiteSpace(publicKey))
            {
                _licenseStatusText.Text = "کلید عمومی صادرکننده در این بیلد پیکربندی نشده است؛ فعال‌سازی امن ممکن نیست.";
                return;
            }
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKey);
                if (!ZurvanLicense.TryVerify(entered, deviceCode, rsa, out var payload) || payload is null)
                {
                    _licenseStatusText.Text = "کد معتبر نیست، برای دستگاه دیگری صادر شده یا اعتبار آن پایان یافته است.";
                    return;
                }
                _settings.LicenseActivationCode = entered;
                _settingsService.Save(_settings);
                _licenseStatusText.Text = $"فعال‌سازی موفق · ویرایش {payload.Edition}" +
                    (string.IsNullOrWhiteSpace(payload.ExpiresUtc) ? " · بدون تاریخ انقضا" : $" · انقضا: {payload.ExpiresUtc}");
                StatusText.Text = "لایسنس زروان با موفقیت فعال شد.";
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"Windows license activation failed: {ex}");
                _licenseStatusText.Text = "اعتبارسنجی لایسنس انجام نشد: " + ex.Message;
            }
        };
        body.Children.Add(activate);

        _licenseStatusText = new TextBlock
        {
            Text = IsWindowsLicensed()
                ? "✓ این نصب زروان فعال است."
                : "این نصب هنوز فعال نشده است. تقویم و صفحه فعال‌سازی در دسترس هستند.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        };
        body.Children.Add(_licenseStatusText);
        body.Children.Add(new TextBlock
        {
            Text = "امنیت: برنامه فقط کلید عمومی را نگه می‌دارد؛ کلید خصوصی صادرکننده هرگز در برنامه یا مخزن قرار نمی‌گیرد. اعتبارسنجی با RSA-PSS و SHA-256 و تطبیق شناسه دستگاه انجام می‌شود.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });
        return new Border
        {
            Background = BrushWithAlpha(_theme.CardBackground, 0xE8),
            BorderBrush = BrushWithAlpha(_theme.Accent, 0x90),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(1, 8, 1, 12),
            Child = body
        };
    }
}

