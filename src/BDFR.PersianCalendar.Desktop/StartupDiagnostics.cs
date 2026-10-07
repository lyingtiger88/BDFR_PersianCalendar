using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

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
        TryEnableLocalCrashDumps();

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

    public static void MarkPhase(string phase)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(
                Path.Combine(DirectoryPath, "last-startup-phase.txt"),
                $"[{DateTimeOffset.Now:O}] {phase}{Environment.NewLine}");
        }
        catch { }

        Log($"PHASE: {phase}");
    }

    public static void ShowFatal(Exception ex)
    {
        MarkPhase("fatal-managed-exception");
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

    private static void TryEnableLocalCrashDumps()
    {
        try
        {
            var dumpFolder = Path.Combine(DirectoryPath, "CrashDumps");
            Directory.CreateDirectory(dumpFolder);

            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\BDFR.PersianCalendar.Desktop.exe",
                writable: true);

            if (key is null)
                return;

            key.SetValue("DumpFolder", dumpFolder, RegistryValueKind.ExpandString);
            key.SetValue("DumpType", 2, RegistryValueKind.DWord);
            key.SetValue("DumpCount", 10, RegistryValueKind.DWord);
        }
        catch
        {
            // Diagnostics must never become a startup dependency.
        }
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
