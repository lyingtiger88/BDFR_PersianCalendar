using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using BDFR.PersianCalendar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using Windows.Storage.Streams;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private sealed record WindowsPairingResultWire(
        bool Accepted,
        string DeviceId,
        string? TrustedDeviceId,
        DateTimeOffset CompletedAtUtc,
        string Sas,
        string? Error = null);

    private sealed record WindowsCompanionSyncResult(
        string PeerDeviceId,
        string PeerDisplayName,
        CompanionPlatform PeerPlatform,
        string Sas,
        int ReceivedItemCount,
        int SentItemCount);

    private sealed class WindowsCompanionMasterSession :
        IAsyncDisposable
    {
        private static readonly JsonSerializerOptions WireJson =
            CreateWireJsonOptions();

        private readonly TcpListener _listener;
        private readonly CompanionEphemeralKey _ephemeral;

        public WindowsCompanionMasterSession(
            string deviceId,
            string displayName)
        {
            var address = FindLanIpv4()
                ?? throw new InvalidOperationException(
                    "آدرس IPv4 شبکه محلی پیدا نشد. رایانه و گوشی باید روی یک شبکه LAN/Wi‑Fi باشند.");

            _listener = new TcpListener(
                IPAddress.Any,
                0);
            _listener.Start();

            _ephemeral =
                new CompanionEphemeralKey();

            var port =
                ((IPEndPoint)_listener.LocalEndpoint)
                .Port;

            var expiresAt =
                DateTimeOffset.UtcNow.AddMinutes(5);

            Offer = new CompanionPairingOffer(
                CompanionProtocol.CurrentVersion,
                CompanionProtocol.NewSessionId(),
                deviceId,
                displayName,
                CompanionPlatform.Windows,
                $"tcp://${address}:{port}",
                CompanionProtocol.CreateSixDigitCode(),
                expiresAt,
                _ephemeral.PublicKeyBase64);

            QrPayload =
                CompanionProtocol
                    .BuildPairingQrPayload(
                        Offer);
        }

        public CompanionPairingOffer Offer { get; }

        public string QrPayload { get; }

        public async Task<WindowsCompanionSyncResult>
            AcceptAndSyncAsync(
                ICompanionSnapshotStore snapshotStore,
                IProgress<string>? progress,
                CancellationToken cancellationToken)
        {
            progress?.Report(
                "در انتظار اسکن QR توسط دستگاه Secondary…");

            using var client =
                await _listener
                    .AcceptTcpClientAsync(
                        cancellationToken);

            _listener.Stop();

            client.NoDelay = true;

            await using var stream =
                client.GetStream();

            using var reader =
                new StreamReader(
                    stream,
                    System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 8192,
                    leaveOpen: true);

            using var writer =
                new StreamWriter(
                    stream,
                    new System.Text.UTF8Encoding(false),
                    bufferSize: 8192,
                    leaveOpen: true)
                {
                    AutoFlush = true
                };

            var joinLine =
                await ReadLineAsync(
                    reader,
                    cancellationToken);

            var join =
                JsonSerializer
                    .Deserialize<
                        CompanionPairingJoin>(
                        joinLine,
                        WireJson)
                ?? throw new InvalidOperationException(
                    "درخواست Pairing خالی است.");

            if (
                join.ProtocolVersion !=
                    CompanionProtocol.CurrentVersion ||
                !string.Equals(
                    join.SessionId,
                    Offer.SessionId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    join.PairingCode,
                    Offer.PairingCode,
                    StringComparison.Ordinal) ||
                Offer.ExpiresAtUtc <=
                    DateTimeOffset.UtcNow ||
                string.IsNullOrWhiteSpace(
                    join.DevicePublicKey)
            )
            {
                throw new SecurityException(
                    "درخواست Pairing نامعتبر یا منقضی است.");
            }

            progress?.Report(
                $"Secondary پیدا شد: {join.DeviceDisplayName} · در حال ساخت کلید جلسه…");

            var sessionKey =
                _ephemeral
                    .DeriveSessionKey(
                        join.DevicePublicKey,
                        Offer.SessionId);

            try
            {
                var sas =
                    CompanionEphemeralKey
                        .ComputeSixDigitSas(
                            sessionKey);

                var result =
                    new WindowsPairingResultWire(
                        true,
                        Offer.HostDeviceId,
                        join.DeviceId,
                        DateTimeOffset.UtcNow,
                        sas);

                await WriteEncryptedAsync(
                    writer,
                    result,
                    sessionKey,
                    cancellationToken);

                progress?.Report(
                    "Handshake امن تأیید شد؛ در حال ارسال داده‌های Master…");

                var masterBatch =
                    await snapshotStore
                        .CreateSnapshotAsync(
                            Offer.HostDeviceId,
                            join.DeviceId,
                            cancellationToken);

                await WriteEncryptedAsync(
                    writer,
                    masterBatch,
                    sessionKey,
                    cancellationToken);

                var secondaryBatch =
                    await ReadEncryptedAsync<
                        SyncBatch>(
                        reader,
                        sessionKey,
                        cancellationToken);

                progress?.Report(
                    "داده‌های Secondary دریافت شد؛ در حال merge بدون overwrite داده‌های Master…");

                var ack =
                    await snapshotStore
                        .ApplySnapshotAsync(
                            secondaryBatch,
                            preferIncoming: false,
                            cancellationToken);

                await WriteEncryptedAsync(
                    writer,
                    ack,
                    sessionKey,
                    cancellationToken);

                return new WindowsCompanionSyncResult(
                    join.DeviceId,
                    join.DeviceDisplayName,
                    join.Platform,
                    sas,
                    ack.AppliedEnvelopeIds.Count,
                    masterBatch.Items.Count);
            }
            finally
            {
                System.Security.Cryptography
                    .CryptographicOperations
                    .ZeroMemory(sessionKey);
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
            }

            _ephemeral.Dispose();
            return ValueTask.CompletedTask;
        }

        private static async Task
            WriteEncryptedAsync<T>(
                StreamWriter writer,
                T payload,
                byte[] sessionKey,
                CancellationToken cancellationToken)
        {
            var frame =
                CompanionEphemeralKey
                    .EncryptJson(
                        payload,
                        sessionKey);

            var line =
                JsonSerializer.Serialize(
                    frame,
                    WireJson);

            if (line.Length >
                4 * 1024 * 1024)
            {
                throw new InvalidOperationException(
                    "Companion frame بیش از حد بزرگ است.");
            }

            await writer
                .WriteLineAsync(
                    line.AsMemory(),
                    cancellationToken);
        }

        private static async Task<T>
            ReadEncryptedAsync<T>(
                StreamReader reader,
                byte[] sessionKey,
                CancellationToken cancellationToken)
        {
            var line =
                await ReadLineAsync(
                    reader,
                    cancellationToken);

            var frame =
                JsonSerializer.Deserialize<
                    EncryptedCompanionFrame>(
                        line,
                        WireJson)
                ?? throw new InvalidOperationException(
                    "Companion encrypted frame خالی است.");

            return CompanionEphemeralKey
                .DecryptJson<T>(
                    frame,
                    sessionKey);
        }

        private static async Task<string>
            ReadLineAsync(
                StreamReader reader,
                CancellationToken cancellationToken)
        {
            var line =
                await reader
                    .ReadLineAsync(
                        cancellationToken)
                ?? throw new IOException(
                    "اتصال Companion بسته شد.");

            if (line.Length >
                4 * 1024 * 1024)
            {
                throw new InvalidOperationException(
                    "Companion frame بیش از حد بزرگ است.");
            }

            return line;
        }

        private static IPAddress?
            FindLanIpv4()
        {
            var candidates =
                NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(
                        network =>
                            network.OperationalStatus ==
                                OperationalStatus.Up &&
                            network.NetworkInterfaceType !=
                                NetworkInterfaceType.Loopback &&
                            network.NetworkInterfaceType !=
                                NetworkInterfaceType.Tunnel)
                    .SelectMany(
                        network =>
                            network
                                .GetIPProperties()
                                .UnicastAddresses)
                    .Select(
                        address =>
                            address.Address)
                    .Where(
                        address =>
                            address.AddressFamily ==
                                AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(
                                address))
                    .ToArray();

            return candidates
                .FirstOrDefault(
                    address =>
                    {
                        var bytes =
                            address.GetAddressBytes();

                        return !(
                            bytes[0] == 169 &&
                            bytes[1] == 254);
                    })
                ?? candidates
                    .FirstOrDefault();
        }

        private static JsonSerializerOptions
            CreateWireJsonOptions()
        {
            var options =
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive =
                        true
                };

            options.Converters.Add(
                new JsonStringEnumConverter());

            return options;
        }
    }

    private async Task
        ShowCompanionPairingAsync()
    {
        if (
            _repository is not
                ICompanionSnapshotStore
                snapshotStore)
        {
            StatusText.Text =
                "Repository فعلی قابلیت Companion Sync ندارد.";
            return;
        }

        if (
            string.IsNullOrWhiteSpace(
                _settings.CompanionDeviceId)
        )
        {
            _settings.CompanionDeviceId =
                CompanionProtocol.NewDeviceId();
            _settingsService.Save(
                _settings);
        }

        await using var session =
            new WindowsCompanionMasterSession(
                _settings
                    .CompanionDeviceId,
                $"Zurvan Windows · {Environment.MachineName}");

        var qrSource =
            await BuildCompanionQrSourceAsync(
                session.QrPayload);

        var status =
            new TextBlock
            {
                Text =
                    "در انتظار اسکن QR…",
                TextWrapping =
                    TextWrapping.Wrap,
                FontWeight =
                    Microsoft.UI.Text
                        .FontWeights
                        .SemiBold
            };

        var code =
            new TextBlock
            {
                Text =
                    $"کد جلسه: {session.Offer.PairingCode}",
                FontSize = 18,
                FontWeight =
                    Microsoft.UI.Text
                        .FontWeights
                        .Bold,
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                FlowDirection =
                    FlowDirection.LeftToRight
            };

        var image =
            new Image
            {
                Source = qrSource,
                Width = 300,
                Height = 300,
                Stretch =
                    Microsoft.UI.Xaml.Media
                        .Stretch.Uniform,
                HorizontalAlignment =
                    HorizontalAlignment.Center
            };

        var security =
            new TextBlock
            {
                Text =
                    "این QR فقط ۵ دقیقه و برای یک Pair معتبر است. بعد از Scan، کد امنیتی مشترک از ECDH ساخته می‌شود و تمام Sync Frameها با AES-GCM رمز می‌شوند.",
                TextWrapping =
                    TextWrapping.Wrap,
                Opacity = 0.72
            };

        var content =
            new StackPanel
            {
                Spacing = 10,
                Width = 440
            };

        content.Children.Add(
            new TextBlock
            {
                Text =
                    "در گوشی زروان: همراه → Secondary → اسکن QR",
                TextWrapping =
                    TextWrapping.Wrap
            });
        content.Children.Add(image);
        content.Children.Add(code);
        content.Children.Add(status);
        content.Children.Add(security);

        var dialog =
            new ContentDialog
            {
                Title =
                    "Zurvan Companion · Master",
                Content =
                    new ScrollViewer
                    {
                        Content = content,
                        MaxHeight = 650,
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto
                    },
                CloseButtonText =
                    "بستن / لغو"
            };

        if (
            Content is FrameworkElement root &&
            root.XamlRoot is not null)
        {
            dialog.XamlRoot =
                root.XamlRoot;
        }

        using var cts =
            new CancellationTokenSource();

        var progress =
            new Progress<string>(
                message =>
                {
                    status.Text = message;
                });

        var pairingTask =
            session.AcceptAndSyncAsync(
                snapshotStore,
                progress,
                cts.Token);

        var observerTask =
            ObserveWindowsCompanionPairingAsync(
                pairingTask,
                status);

        await dialog.ShowAsync();

        cts.Cancel();

        try
        {
            await pairingTask;
        }
        catch (
            OperationCanceledException)
        {
        }
        catch
        {
            // Observer already surfaces the error.
        }

        await observerTask;
    }

    private async Task
        ObserveWindowsCompanionPairingAsync(
            Task<WindowsCompanionSyncResult>
                pairingTask,
            TextBlock status)
    {
        try
        {
            var result =
                await pairingTask;

            status.Text =
                $"✓ {result.PeerDisplayName} جفت شد · کد امنیتی {result.Sas} · " +
                $"ارسال {result.SentItemCount} آیتم · دریافت {result.ReceivedItemCount} آیتم";

            StatusText.Text =
                "همگام‌سازی Companion با موفقیت انجام شد.";

            await _repository
                .AddActivityAsync(
                    new ActivityLogEntry(
                        $"activity-companion-{Guid.NewGuid():N}",
                        DateTimeOffset.UtcNow,
                        "companion-sync",
                        null,
                        null,
                        $"Paired with {result.PeerDisplayName}; SAS={result.Sas}; sent={result.SentItemCount}; received={result.ReceivedItemCount}."));

            NotifyExternalChange(
                $"Companion: {result.PeerDisplayName} همگام شد.");
        }
        catch (
            OperationCanceledException)
        {
            status.Text =
                "جلسه Pairing لغو شد.";
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log(
                $"Windows Companion pairing failed: {ex}");

            status.Text =
                $"Pairing انجام نشد: {ex.Message}";

            StatusText.Text =
                "جفت‌سازی Companion انجام نشد.";
        }
    }

    private static async Task<BitmapImage>
        BuildCompanionQrSourceAsync(
            string payload)
    {
        using var generator =
            new QRCodeGenerator();

        using var data =
            generator.CreateQrCode(
                payload,
                QRCodeGenerator
                    .ECCLevel.M);

        var qr =
            new PngByteQRCode(data);

        var bytes =
            qr.GetGraphic(8);

        var stream =
            new InMemoryRandomAccessStream();

        using (
            var writer =
                new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
        }

        stream.Seek(0);

        var bitmap =
            new BitmapImage();

        await bitmap
            .SetSourceAsync(stream);

        return bitmap;
    }
}
