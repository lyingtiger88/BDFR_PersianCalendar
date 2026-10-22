using System.Security.Cryptography;
using BDFR.PersianCalendar.Core;
using BDFR.PersianCalendar.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private static readonly HttpClient ZurvanCloudHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private WindowsCloudSecretStore? _cloudSecretStore;

    private WindowsCloudSecretStore CloudSecretStore
        => _cloudSecretStore ??= new WindowsCloudSecretStore();

    private FrameworkElement BuildZurvanCloudSettingsPanel()
    {
        var root = new StackPanel
        {
            Spacing = 8,
            Padding = new Thickness(10)
        };

        root.Children.Add(new TextBlock
        {
            Text = "☁ Zurvan Cloud",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        root.Children.Add(new TextBlock
        {
            Text = "ذخیره‌سازی End-to-End Encrypted روی Object Storage ابر آروان. داده قبل از Upload روی همین دستگاه رمز می‌شود و فقط روی دستگاه‌های مورد اعتماد زروان رمزگشایی می‌شود.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        var enabledCheck = new CheckBox
        {
            Content = "فعال‌سازی Zurvan Cloud برای این دستگاه",
            IsChecked = _settings.CloudEnabled
        };
        root.Children.Add(enabledCheck);

        var endpointBox = new TextBox
        {
            Header = "S3 Endpoint",
            Text = string.IsNullOrWhiteSpace(_settings.CloudEndpoint)
                ? ZurvanCloudProtocol.DefaultEndpoint
                : _settings.CloudEndpoint,
            PlaceholderText = ZurvanCloudProtocol.DefaultEndpoint,
            FlowDirection = FlowDirection.LeftToRight
        };
        root.Children.Add(endpointBox);

        var regionBox = new TextBox
        {
            Header = "Region",
            Text = string.IsNullOrWhiteSpace(_settings.CloudRegion)
                ? ZurvanCloudProtocol.DefaultRegion
                : _settings.CloudRegion,
            PlaceholderText = ZurvanCloudProtocol.DefaultRegion,
            FlowDirection = FlowDirection.LeftToRight
        };
        root.Children.Add(regionBox);

        var bucketBox = new TextBox
        {
            Header = "Bucket",
            Text = _settings.CloudBucket ?? string.Empty,
            PlaceholderText = "zurvan-user-data",
            FlowDirection = FlowDirection.LeftToRight
        };
        root.Children.Add(bucketBox);

        var prefixBox = new TextBox
        {
            Header = "Object Prefix",
            Text = ZurvanCloudProtocol.NormalizePrefix(_settings.CloudPrefix),
            PlaceholderText = "zurvan",
            FlowDirection = FlowDirection.LeftToRight
        };
        root.Children.Add(prefixBox);

        var accessKeyBox = new TextBox
        {
            Header = "Arvan S3 Access Key",
            PlaceholderText = "اگر خالی بماند مقدار ذخیره‌شده استفاده می‌شود",
            FlowDirection = FlowDirection.LeftToRight
        };
        root.Children.Add(accessKeyBox);

        var secretKeyBox = new PasswordBox
        {
            Header = "Arvan S3 Secret Key",
            PlaceholderText = "اگر خالی بماند مقدار ذخیره‌شده استفاده می‌شود",
            FlowDirection = FlowDirection.LeftToRight
        };
        root.Children.Add(secretKeyBox);

        root.Children.Add(new TextBlock
        {
            Text = "Access Key، Secret Key و Vault Key داخل فایل Settings ذخیره نمی‌شوند؛ در Secure Storage ویندوز نگه‌داری می‌شوند.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.66,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        var cloudStatus = new TextBlock
        {
            Text = _settings.CloudEnabled
                ? "وضعیت: پیکربندی Cloud فعال است."
                : "وضعیت: Cloud غیرفعال است.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        };
        root.Children.Add(cloudStatus);

        var saveAndTest = MakeButton("🔐 ذخیره امن و تست Upload/Download رمزگذاری‌شده");
        saveAndTest.Click += async (_, _) =>
        {
            saveAndTest.IsEnabled = false;
            cloudStatus.Text = "در حال تست رمزگذاری و اتصال به Object Storage...";

            try
            {
                await SaveAndProbeZurvanCloudAsync(
                    enabledCheck.IsChecked == true,
                    endpointBox.Text,
                    regionBox.Text,
                    bucketBox.Text,
                    prefixBox.Text,
                    accessKeyBox.Text,
                    secretKeyBox.Password);

                cloudStatus.Text =
                    "✓ تست موفق: payload روی کلاینت رمز شد، روی Cloud Upload شد، Download و روی کلاینت Decrypt شد و سپس object آزمایشی حذف شد.";

                accessKeyBox.Text = string.Empty;
                secretKeyBox.Password = string.Empty;
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"Zurvan Cloud probe failed: {ex}");
                cloudStatus.Text = $"خطای Zurvan Cloud: {ex.Message}";
            }
            finally
            {
                saveAndTest.IsEnabled = true;
            }
        };
        root.Children.Add(saveAndTest);

        var recoveryKey = MakeButton("🗝 نمایش / ساخت Recovery Key این Vault");
        recoveryKey.Click += async (_, _) =>
        {
            try
            {
                await ShowZurvanCloudRecoveryKeyAsync();
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"Zurvan Cloud recovery-key view failed: {ex}");
                cloudStatus.Text = $"نمایش Recovery Key انجام نشد: {ex.Message}";
            }
        };
        root.Children.Add(recoveryKey);

        var forgetSecrets = MakeButton("حذف Credentialهای محلی Zurvan Cloud");
        forgetSecrets.Click += async (_, _) =>
        {
            try
            {
                await CloudSecretStore.RemoveAsync(
                    ZurvanCloudSecretNames.AccessKeyId);
                await CloudSecretStore.RemoveAsync(
                    ZurvanCloudSecretNames.SecretAccessKey);
                await CloudSecretStore.RemoveAsync(
                    ZurvanCloudSecretNames.VaultKey);

                _settings.CloudEnabled = false;
                _settingsService.Save(_settings);
                enabledCheck.IsChecked = false;
                cloudStatus.Text =
                    "Credentialها و Vault Key از این دستگاه حذف شدند. Ciphertextهای موجود در Cloud حذف نشده‌اند.";
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"Zurvan Cloud local-secret removal failed: {ex}");
                cloudStatus.Text = $"حذف Credentialها انجام نشد: {ex.Message}";
            }
        };
        root.Children.Add(forgetSecrets);

        return new Border
        {
            Margin = new Thickness(0, 8, 0, 4),
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(CurrentUiCornerRadius),
            BorderBrush = BrushWithAlpha(_theme.Accent, 0x88),
            BorderThickness = new Thickness(1),
            Background = BrushWithAlpha(_theme.CardBackground, 0x92),
            Child = root
        };
    }

    private async Task SaveAndProbeZurvanCloudAsync(
        bool enabled,
        string endpoint,
        string region,
        string bucket,
        string prefix,
        string accessKeyInput,
        string secretKeyInput)
    {
        endpoint = (endpoint ?? string.Empty).Trim();
        region = (region ?? string.Empty).Trim();
        bucket = (bucket ?? string.Empty).Trim();
        prefix = ZurvanCloudProtocol.NormalizePrefix(prefix);

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "Endpoint باید یک آدرس HTTPS معتبر باشد.");
        }

        if (string.IsNullOrWhiteSpace(region))
            throw new ArgumentException("Region را وارد کنید.");

        if (string.IsNullOrWhiteSpace(bucket))
            throw new ArgumentException("Bucket را وارد کنید.");

        var accessKey = string.IsNullOrWhiteSpace(accessKeyInput)
            ? await CloudSecretStore.GetAsync(
                ZurvanCloudSecretNames.AccessKeyId)
            : accessKeyInput.Trim();

        var secretKey = string.IsNullOrWhiteSpace(secretKeyInput)
            ? await CloudSecretStore.GetAsync(
                ZurvanCloudSecretNames.SecretAccessKey)
            : secretKeyInput.Trim();

        if (string.IsNullOrWhiteSpace(accessKey))
            throw new InvalidOperationException(
                "Arvan S3 Access Key وارد یا ذخیره نشده است.");

        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException(
                "Arvan S3 Secret Key وارد یا ذخیره نشده است.");

        if (!string.IsNullOrWhiteSpace(accessKeyInput))
        {
            await CloudSecretStore.SetAsync(
                ZurvanCloudSecretNames.AccessKeyId,
                accessKey);
        }

        if (!string.IsNullOrWhiteSpace(secretKeyInput))
        {
            await CloudSecretStore.SetAsync(
                ZurvanCloudSecretNames.SecretAccessKey,
                secretKey);
        }

        if (string.IsNullOrWhiteSpace(_settings.CloudVaultId))
            _settings.CloudVaultId = ZurvanCloudProtocol.NewVaultId();

        var vaultKey = await GetOrCreateZurvanCloudVaultKeyAsync();

        try
        {
            var profile = new ZurvanCloudProfile(
                ZurvanCloudProtocol.DefaultProvider,
                endpoint,
                region,
                bucket,
                prefix,
                _settings.CloudVaultId,
                enabled);

            var objectStore = new ArvanS3ObjectStore(
                ZurvanCloudHttpClient,
                profile,
                new ZurvanCloudCredentials(accessKey, secretKey));

            var client = new ZurvanCloudClient(
                objectStore,
                profile,
                vaultKey);

            await client.ProbeRoundTripAsync(
                GetCloudOriginDeviceId());

            _settings.CloudEnabled = enabled;
            _settings.CloudProvider = profile.Provider;
            _settings.CloudEndpoint = profile.Endpoint;
            _settings.CloudRegion = profile.Region;
            _settings.CloudBucket = profile.Bucket;
            _settings.CloudPrefix = profile.Prefix;
            _settings.CloudVaultId = profile.VaultId;
            _settingsService.Save(_settings);

            await _repository.AddActivityAsync(
                new ActivityLogEntry(
                    $"activity-cloud-{Guid.NewGuid():N}",
                    DateTimeOffset.UtcNow,
                    "cloud-probe",
                    null,
                    null,
                    "Zurvan Cloud encrypted round-trip completed."));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vaultKey);
        }
    }

    private async Task<byte[]> GetOrCreateZurvanCloudVaultKeyAsync()
    {
        var encoded = await CloudSecretStore.GetAsync(
            ZurvanCloudSecretNames.VaultKey);

        if (!string.IsNullOrWhiteSpace(encoded))
            return ZurvanCloudCrypto.DecodeRecoveryKey(encoded);

        var key = ZurvanCloudCrypto.CreateVaultKey();
        var recovery = ZurvanCloudCrypto.EncodeRecoveryKey(key);

        await CloudSecretStore.SetAsync(
            ZurvanCloudSecretNames.VaultKey,
            recovery);

        if (string.IsNullOrWhiteSpace(_settings.CloudVaultId))
        {
            _settings.CloudVaultId = ZurvanCloudProtocol.NewVaultId();
            _settingsService.Save(_settings);
        }

        return key;
    }

    private async Task ShowZurvanCloudRecoveryKeyAsync()
    {
        var key = await GetOrCreateZurvanCloudVaultKeyAsync();

        try
        {
            var recoveryKey = ZurvanCloudCrypto.EncodeRecoveryKey(key);

            var recoveryBox = new TextBox
            {
                Text = recoveryKey,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FlowDirection = FlowDirection.LeftToRight
            };

            var stack = new StackPanel
            {
                Spacing = 10,
                Width = 460
            };
            stack.Children.Add(new TextBlock
            {
                Text = "این کلید می‌تواند تمام داده‌های Zurvan Cloud این Vault را رمزگشایی کند. آن را فقط در محل امن نگه‌دار و برای هیچ‌کس ارسال نکن.",
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(recoveryBox);
            stack.Children.Add(new TextBlock
            {
                Text = $"Vault ID: {_settings.CloudVaultId}",
                FontSize = 10.5,
                Opacity = 0.68,
                FlowDirection = FlowDirection.LeftToRight
            });

            var dialog = new ContentDialog
            {
                Title = "Zurvan Cloud Recovery Key",
                Content = stack,
                CloseButtonText = "بستن"
            };

            if (Content is FrameworkElement root &&
                root.XamlRoot is not null)
            {
                dialog.XamlRoot = root.XamlRoot;
            }

            await dialog.ShowAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string GetCloudOriginDeviceId()
    {
        var source = $"{Environment.MachineName}|{Environment.UserName}|windows";
        var hash = SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(source));

        return $"win_{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
    }
}
