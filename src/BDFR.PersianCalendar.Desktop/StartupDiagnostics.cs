using System.Runtime.InteropServices;

namespace BDFR.PersianCalendar.Desktop;

internal static class StartupDiagnostics
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BDFR",
        "PersianCalendar");

    public static string LogPath => Path.Combine(DirectoryPath, "startup.log");

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
                $"BDFR Persian Calendar failed to start.\n\n{ex.Message}\n\nLog:\n{LogPath}",
                "BDFR Persian Calendar",
                0x00000010);
        }
        catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
