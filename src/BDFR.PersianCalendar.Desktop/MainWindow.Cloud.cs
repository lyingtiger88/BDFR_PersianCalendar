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
            Text = "☁ Zurvan Desk",
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
            Content = "فعال‌سازی Zurvan Desk برای این دستگاه",
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

        var vaultIdBox = new TextBox
        {
            Header = "Vault ID",
            Text = _settings.CloudVaultId ?? string.Empty,
            PlaceholderText = "برای Vault جدید خالی بگذارید؛ برای بازیابی Vault قبلی وارد کنید",
            FlowDirection = FlowDirection.LeftToRight
        };
        root.Children.Add(vaultIdBox);

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
            Text = "Access Key، Secret Key و Vault Key داخل فایل Settings ذخیره نمی‌شوند؛ در Secure Storage ویندوز نگه‌داری می‌شوند. شماره تلفن/ایمیل حساب به‌تنهایی نمی‌تواند Vault را رمزگشایی کند.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.66,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        var cloudStatus = new TextBlock
        {
            Text = _settings.CloudEnabled
                ? "وضعیت: پیکربندی Zurvan Desk فعال است."
                : "وضعیت: Zurvan Desk غیرفعال است.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        };
        root.Children.Add(cloudStatus);

        var saveAndTest = MakeButton("🔐 ذخیره امن و تست Upload/Download رمزگذاری‌شده");
        saveAndTest.Click += async (_, _) =>
        {
            saveAndTest.IsEnabled = false;
            cloudStatus.Text = "در حال تست رمزگذاری و اتصال Zurvan Desk به Object Storage...";

            try
            {
                await SaveAndProbeZurvanCloudAsync(
                    enabledCheck.IsChecked == true,
                    endpointBox.Text,
                    regionBox.Text,
                    bucketBox.Text,
                    prefixBox.Text,
                    vaultIdBox.Text,
                    accessKeyBox.Text,
                    secretKeyBox.Password);

                vaultIdBox.Text = _settings.CloudVaultId;

                cloudStatus.Text =
                    "✓ تست موفق: payload روی کلاینت رمز شد، روی Zurvan Desk Upload شد، Download و روی کلاینت Decrypt شد و سپس object آزمایشی حذف شد.";

                accessKeyBox.Text = string.Empty;
                secretKeyBox.Password = string.Empty;
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"Zurvan Desk probe failed: {ex}");
                cloudStatus.Text = $"خطای Zurvan Desk: {ex.Message}";
            }
            finally
            {
                saveAndTest.IsEnabled = true;
            }
        };
        root.Children.Add(saveAndTest);

        root.Children.Add(new TextBlock
        {
            Text = "بازیابی حساب",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });

        root.Children.Add(new TextBlock
        {
            Text = "زروان یک عبارت بازیابی ۲۴ کلمه‌ای با checksum می‌سازد. خود عبارت هرگز روی سرور ذخیره نمی‌شود؛ فقط Vault Key با کلیدی مشتق‌شده از آن wrap و روی Zurvan Desk ذخیره می‌شود.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        var createRecoveryPhrase = MakeButton("🧾 ساخت عبارت بازیابی ۲۴ کلمه‌ای");
        createRecoveryPhrase.Click += async (_, _) =>
        {
            createRecoveryPhrase.IsEnabled = false;

            try
            {
                var phrase = await CreateAndUploadZurvanRecoveryPhraseAsync(
                    endpointBox.Text,
                    regionBox.Text,
                    bucketBox.Text,
                    prefixBox.Text,
                    vaultIdBox.Text,
                    accessKeyBox.Text,
                    secretKeyBox.Password);

                vaultIdBox.Text = _settings.CloudVaultId;
                await ShowZurvanRecoveryPhraseAsync(phrase);

                cloudStatus.Text =
                    "✓ عبارت بازیابی جدید فعال شد. عبارت قبلی دیگر Recovery Envelope فعلی را باز نمی‌کند.";
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"Zurvan Desk recovery phrase creation failed: {ex}");
                cloudStatus.Text = $"ساخت عبارت بازیابی انجام نشد: {ex.Message}";
            }
            finally
            {
                createRecoveryPhrase.IsEnabled = true;
            }
        };
        root.Children.Add(createRecoveryPhrase);

        var restoreFromPhrase = MakeButton("♻ بازیابی Vault با عبارت ۲۴ کلمه‌ای");
        restoreFromPhrase.Click += async (_, _) =>
        {
            restoreFromPhrase.IsEnabled = false;

            try
            {
                var restored = await RestoreZurvanCloudVaultFromPhraseDialogAsync(
                    endpointBox.Text,
                    regionBox.Text,
                    bucketBox.Text,
                    prefixBox.Text,
                    vaultIdBox.Text,
                    accessKeyBox.Text,
                    secretKeyBox.Password);

                if (restored)
                {
                    vaultIdBox.Text = _settings.CloudVaultId;
                    enabledCheck.IsChecked = true;
                    cloudStatus.Text =
                        "✓ Vault Key با عبارت بازیابی روی همین دستگاه بازسازی و در Secure Storage ذخیره شد.";
                }
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"Zurvan Desk recovery restore failed: {ex}");
                cloudStatus.Text = $"بازیابی Vault انجام نشد: {ex.Message}";
            }
            finally
            {
                restoreFromPhrase.IsEnabled = true;
            }
        };
        root.Children.Add(restoreFromPhrase);

        var forgetSecrets = MakeButton("حذف Credentialهای محلی Zurvan Desk");
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
                    "Credentialها و Vault Key از این دستگاه حذف شدند. Ciphertextهای موجود در Zurvan Desk حذف نشده‌اند.";
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"Zurvan Desk local-secret removal failed: {ex}");
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
        string vaultIdInput,
        string accessKeyInput,
        string secretKeyInput)
    {
        var connection = await ResolveZurvanCloudConnectionAsync(
            endpoint,
            region,
            bucket,
            prefix,
            vaultIdInput,
            accessKeyInput,
            secretKeyInput,
            allowCreateVault: true,
            persistSecrets: true);

        _settings.CloudVaultId = connection.Profile.VaultId;
        var vaultKey = await GetOrCreateZurvanCloudVaultKeyAsync();


        try
        {
            var profile = connection.Profile;
            var objectStore = new ArvanS3ObjectStore(
                ZurvanCloudHttpClient,
                profile,
                connection.Credentials);

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
                    "Zurvan Desk encrypted round-trip completed."));
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

    private async Task<(
        ZurvanCloudProfile Profile,
        ZurvanCloudCredentials Credentials)> ResolveZurvanCloudConnectionAsync(
        string endpoint,
        string region,
        string bucket,
        string prefix,
        string vaultIdInput,
        string accessKeyInput,
        string secretKeyInput,
        bool allowCreateVault,
        bool persistSecrets)
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

        var vaultId = (vaultIdInput ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(vaultId))
            vaultId = _settings.CloudVaultId;

        if (string.IsNullOrWhiteSpace(vaultId))
        {
            if (!allowCreateVault)
            {
                throw new InvalidOperationException(
                    "برای بازیابی، Vault ID حساب قبلی لازم است.");
            }

            vaultId = ZurvanCloudProtocol.NewVaultId();
        }

        var accessKey = string.IsNullOrWhiteSpace(accessKeyInput)
            ? await CloudSecretStore.GetAsync(
                ZurvanCloudSecretNames.AccessKeyId)
            : accessKeyInput.Trim();

        var secretKey = string.IsNullOrWhiteSpace(secretKeyInput)
            ? await CloudSecretStore.GetAsync(
                ZurvanCloudSecretNames.SecretAccessKey)
            : secretKeyInput.Trim();

        if (string.IsNullOrWhiteSpace(accessKey))
        {
            throw new InvalidOperationException(
                "Arvan S3 Access Key وارد یا از حساب دریافت نشده است.");
        }

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                "Arvan S3 Secret Key وارد یا از حساب دریافت نشده است.");
        }

        if (persistSecrets)
        {
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
        }

        return (
            new ZurvanCloudProfile(
                ZurvanCloudProtocol.DefaultProvider,
                endpoint,
                region,
                bucket,
                prefix,
                vaultId,
                true),
            new ZurvanCloudCredentials(
                accessKey,
                secretKey));
    }

    private async Task<string> CreateAndUploadZurvanRecoveryPhraseAsync(
        string endpoint,
        string region,
        string bucket,
        string prefix,
        string vaultIdInput,
        string accessKeyInput,
        string secretKeyInput)
    {
        var connection = await ResolveZurvanCloudConnectionAsync(
            endpoint,
            region,
            bucket,
            prefix,
            vaultIdInput,
            accessKeyInput,
            secretKeyInput,
            allowCreateVault: true,
            persistSecrets: true);

        _settings.CloudVaultId = connection.Profile.VaultId;

        var vaultKey = await GetOrCreateZurvanCloudVaultKeyAsync();
        var phrase = ZurvanRecoveryPhrase.Generate(
            ZurvanRecoveryPhrase.DefaultWordCount);

        try
        {
            var envelope = ZurvanRecoveryPhrase.WrapVaultKey(
                phrase,
                connection.Profile.VaultId,
                vaultKey);

            var objectStore = new ArvanS3ObjectStore(
                ZurvanCloudHttpClient,
                connection.Profile,
                connection.Credentials);

            var client = new ZurvanCloudClient(
                objectStore,
                connection.Profile,
                vaultKey);

            await client.UploadRecoveryEnvelopeAsync(envelope);

            _settings.CloudEnabled = true;
            _settings.CloudProvider = connection.Profile.Provider;
            _settings.CloudEndpoint = connection.Profile.Endpoint;
            _settings.CloudRegion = connection.Profile.Region;
            _settings.CloudBucket = connection.Profile.Bucket;
            _settings.CloudPrefix = connection.Profile.Prefix;
            _settings.CloudVaultId = connection.Profile.VaultId;
            _settingsService.Save(_settings);

            await _repository.AddActivityAsync(
                new ActivityLogEntry(
                    $"activity-cloud-recovery-{Guid.NewGuid():N}",
                    DateTimeOffset.UtcNow,
                    "cloud-recovery-created",
                    null,
                    null,
                    "A new 24-word Zurvan Desk recovery phrase was activated."));

            return phrase;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vaultKey);
        }
    }

    private async Task ShowZurvanRecoveryPhraseAsync(
        string phrase)
    {
        var words = phrase.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        var formatted = string.Join(
            Environment.NewLine,
            words.Select(
                (word, index) =>
                    $"{index + 1:00}. {word}"));

        var phraseBox = new TextBox
        {
            Text = formatted,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            MinHeight = 360,
            FlowDirection = FlowDirection.LeftToRight
        };

        var stack = new StackPanel
        {
            Spacing = 10,
            Width = 480
        };
        stack.Children.Add(new TextBlock
        {
            Text = "این ۲۴ کلمه تنها راه بازیابی Vault در صورت حذف برنامه، فراموشی رمز یا تصاحب حساب است. از آن عکس آنلاین نگیرید، در ایمیل/پیام‌رسان ذخیره نکنید و برای هیچ‌کس ارسال نکنید.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        stack.Children.Add(phraseBox);
        stack.Children.Add(new TextBlock
        {
            Text = $"Vault ID: {_settings.CloudVaultId}",
            FontSize = 10.5,
            Opacity = 0.68,
            FlowDirection = FlowDirection.LeftToRight
        });
        stack.Children.Add(new TextBlock
        {
            Text = "ساخت عبارت جدید، عبارت قبلی را برای Recovery Envelope فعلی باطل می‌کند.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68
        });

        var dialog = new ContentDialog
        {
            Title = "عبارت بازیابی Zurvan Desk",
            Content = new ScrollViewer
            {
                Content = stack,
                MaxHeight = 620,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto
            },
            CloseButtonText = "عبارت را ذخیره کردم"
        };

        if (Content is FrameworkElement root &&
            root.XamlRoot is not null)
        {
            dialog.XamlRoot = root.XamlRoot;
        }

        await dialog.ShowAsync();
    }

    private async Task<bool> RestoreZurvanCloudVaultFromPhraseDialogAsync(
        string endpoint,
        string region,
        string bucket,
        string prefix,
        string vaultIdInput,
        string accessKeyInput,
        string secretKeyInput)
    {
        var phraseBox = new TextBox
        {
            Header = "عبارت بازیابی ۲۴ کلمه‌ای",
            PlaceholderText = "کلمات را با فاصله وارد کنید",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 150,
            FlowDirection = FlowDirection.LeftToRight
        };

        var stack = new StackPanel
        {
            Spacing = 10,
            Width = 480
        };
        stack.Children.Add(new TextBlock
        {
            Text = "شماره تلفن یا ایمیل فقط دسترسی حساب را برمی‌گرداند؛ برای بازکردن Vault Key باید عبارت بازیابی صحیح را وارد کنید.",
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(phraseBox);

        var dialog = new ContentDialog
        {
            Title = "بازیابی Zurvan Desk",
            Content = stack,
            PrimaryButtonText = "بازیابی Vault",
            CloseButtonText = "انصراف",
            DefaultButton = ContentDialogButton.Primary
        };

        if (Content is FrameworkElement root &&
            root.XamlRoot is not null)
        {
            dialog.XamlRoot = root.XamlRoot;
        }

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return false;

        var phrase = phraseBox.Text;
        phraseBox.Text = string.Empty;

        if (!ZurvanRecoveryPhrase.TryDecodeEntropy(
                phrase,
                out var phraseEntropy))
        {
            throw new FormatException(
                "عبارت بازیابی یا checksum آن معتبر نیست.");
        }
        CryptographicOperations.ZeroMemory(phraseEntropy);

        var connection = await ResolveZurvanCloudConnectionAsync(
            endpoint,
            region,
            bucket,
            prefix,
            vaultIdInput,
            accessKeyInput,
            secretKeyInput,
            allowCreateVault: false,
            persistSecrets: true);

        // Temporary key only lets the generic client exist; the recovery
        // envelope itself is not encrypted with the vault key.
        var placeholderKey = new byte[32];

        try
        {
            var objectStore = new ArvanS3ObjectStore(
                ZurvanCloudHttpClient,
                connection.Profile,
                connection.Credentials);

            var client = new ZurvanCloudClient(
                objectStore,
                connection.Profile,
                placeholderKey);

            var envelope = await client.DownloadRecoveryEnvelopeAsync()
                ?? throw new InvalidOperationException(
                    "Recovery Envelope برای این Vault روی Cloud پیدا نشد.");

            var recoveredVaultKey =
                ZurvanRecoveryPhrase.UnwrapVaultKey(
                    phrase,
                    envelope);

            try
            {
                await CloudSecretStore.SetAsync(
                    ZurvanCloudSecretNames.VaultKey,
                    ZurvanCloudCrypto.EncodeRecoveryKey(
                        recoveredVaultKey));

                var verifiedClient = new ZurvanCloudClient(
                    objectStore,
                    connection.Profile,
                    recoveredVaultKey);

                await verifiedClient.ProbeRoundTripAsync(
                    GetCloudOriginDeviceId());

                _settings.CloudEnabled = true;
                _settings.CloudProvider =
                    connection.Profile.Provider;
                _settings.CloudEndpoint =
                    connection.Profile.Endpoint;
                _settings.CloudRegion =
                    connection.Profile.Region;
                _settings.CloudBucket =
                    connection.Profile.Bucket;
                _settings.CloudPrefix =
                    connection.Profile.Prefix;
                _settings.CloudVaultId =
                    connection.Profile.VaultId;
                _settingsService.Save(_settings);

                await _repository.AddActivityAsync(
                    new ActivityLogEntry(
                        $"activity-cloud-restore-{Guid.NewGuid():N}",
                        DateTimeOffset.UtcNow,
                        "cloud-recovery-restored",
                        null,
                        null,
                        "Zurvan Desk vault key was restored from the recovery phrase."));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(
                    recoveredVaultKey);
            }

            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                placeholderKey);
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
