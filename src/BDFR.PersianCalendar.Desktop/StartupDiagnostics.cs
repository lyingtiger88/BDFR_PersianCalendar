using System.Reflection;
using System.Runtime.InteropServices;

namespace BDFR.PersianCalendar.Desktop;

internal static class StartupDiagnostics
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BDFR",
        "PersianCalendar");

    public static string LogPath => Path.Combine(DirectoryPath, "startup.log");

    public static void BeginSession()
    {
        var assembly = typeof(StartupDiagnostics).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";
        var gitCommit = GetAssemblyMetadata(assembly, "GitCommit");
        var buildNumber = GetAssemblyMetadata(assembly, "BuildNumber");
        var executablePath = Environment.ProcessPath ?? "unknown";

        Log($"========== SESSION START pid={Environment.ProcessId} version={version} informational={informationalVersion} build={buildNumber} commit={gitCommit} ==========");
        Log($"Runtime: {RuntimeInformation.FrameworkDescription}; OS: {RuntimeInformation.OSDescription}; processArch={RuntimeInformation.ProcessArchitecture}; osArch={RuntimeInformation.OSArchitecture}.");
        Log($"Executable: {executablePath}");
        Log($"Base directory: {AppContext.BaseDirectory}");
        Log($"Install kind hint: {DetectInstallKind(executablePath)}");
        Log($"Data directory: {DirectoryPath}");
        Log($"Settings path: {Path.Combine(DirectoryPath, "settings.json")}");
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void ShowFatal(Exception ex)
    {
        Log($"FATAL: {ex}");
        try
        {
            MessageBoxW(
                IntPtr.Zero,
                $"Anahita failed to start.\n\n{ex.Message}\n\nLog:\n{LogPath}",
                "Anahita",
                0x00000010);
        }
        catch { }
    }

    private static string GetAssemblyMetadata(Assembly assembly, string key)
        => assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))?
            .Value ?? "unknown";

    private static string DetectInstallKind(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath == "unknown")
            return "unknown";

        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (!string.IsNullOrWhiteSpace(folder) &&
                executablePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                return "installed-program-files";
            }
        }

        return "portable-or-custom-location";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
