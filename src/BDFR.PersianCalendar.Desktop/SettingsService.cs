using System.Text.Json;

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
            settings.BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0, 0.85);
            Directory.CreateDirectory(DataRoot);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Settings save failed: {ex}");
        }
    }

    public async Task<string?> CopyCustomBackgroundAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return null;

        try
        {
            Directory.CreateDirectory(UserBackgroundRoot);
            var extension = Path.GetExtension(sourcePath);
            var target = Path.Combine(UserBackgroundRoot, $"custom-background{extension}");
            await using var source = File.OpenRead(sourcePath);
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination);
            return target;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Copy custom background failed: {ex}");
            return null;
        }
    }
}
