using System.Text.Json;
using Windows.Storage;

namespace BDFR.PersianCalendar.Desktop;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BDFR",
        "PersianCalendar");

    public string SettingsPath => Path.Combine(DataRoot, "settings.json");
    public string UserPictureRoot => Path.Combine(DataRoot, "picture");
    public string UserBackgroundRoot => Path.Combine(UserPictureRoot, "user background");

    public SettingsService()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(UserPictureRoot);
        Directory.CreateDirectory(UserBackgroundRoot);
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(
                       File.ReadAllText(SettingsPath),
                       JsonOptions)
                   ?? new AppSettings();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Settings load failed: {ex}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            settings.BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0, 0.95);
            Directory.CreateDirectory(DataRoot);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Settings save failed: {ex}");
        }
    }

    public async Task<string?> CopyCustomBackgroundAsync(StorageFile sourceFile)
    {
        try
        {
            Directory.CreateDirectory(UserBackgroundRoot);

            var destinationFolder =
                await StorageFolder.GetFolderFromPathAsync(UserBackgroundRoot);

            var extension = Path.GetExtension(sourceFile.Name);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            var targetName =
                $"custom-background-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension.ToLowerInvariant()}";

            var copied = await sourceFile.CopyAsync(
                destinationFolder,
                targetName,
                NameCollisionOption.ReplaceExisting);

            // Keep only the newly selected background. Unique names also avoid image-cache reuse.
            foreach (var oldPath in Directory.EnumerateFiles(
                         UserBackgroundRoot,
                         "custom-background-*",
                         SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(
                        Path.GetFullPath(oldPath),
                        Path.GetFullPath(copied.Path),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                try { File.Delete(oldPath); }
                catch (Exception ex)
                {
                    StartupDiagnostics.Log($"Old custom background cleanup skipped: {ex.Message}");
                }
            }

            return copied.Path;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Copy custom background failed: {ex}");
            return null;
        }
    }
}
