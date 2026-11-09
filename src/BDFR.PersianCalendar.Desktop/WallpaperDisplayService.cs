using System.Runtime.InteropServices;

namespace BDFR.PersianCalendar.Desktop;

internal sealed record WallpaperDisplayInfo(int Index, string DevicePath, string Label);

internal static class WallpaperDisplayService
{
    public static IReadOnlyList<WallpaperDisplayInfo> GetDisplays()
    {
        IDesktopWallpaper? desktop = null;
        try
        {
            desktop = (IDesktopWallpaper)new DesktopWallpaper();
            desktop.GetMonitorDevicePathCount(out var count);
            var displays = new List<WallpaperDisplayInfo>();
            for (uint i = 0; i < count; i++)
            {
                desktop.GetMonitorDevicePathAt(i, out var path);
                desktop.GetMonitorRECT(path, out var rect);
                displays.Add(new WallpaperDisplayInfo(
                    checked((int)i + 1),
                    path,
                    $"نمایشگر {i + 1} · {rect.Right - rect.Left}×{rect.Bottom - rect.Top}"));
            }
            return displays;
        }
        finally
        {
            if (desktop is not null && Marshal.IsComObject(desktop))
                Marshal.FinalReleaseComObject(desktop);
        }
    }

    public static void SetWallpaper(string monitorDevicePath, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(monitorDevicePath))
            throw new ArgumentException("شناسه نمایشگر معتبر نیست.", nameof(monitorDevicePath));
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("فایل تصویر زمینه پیدا نشد.", imagePath);

        IDesktopWallpaper? desktop = null;
        try
        {
            desktop = (IDesktopWallpaper)new DesktopWallpaper();
            desktop.SetPosition(DesktopWallpaperPosition.Fill);
            desktop.SetWallpaper(monitorDevicePath, imagePath);
        }
        finally
        {
            if (desktop is not null && Marshal.IsComObject(desktop))
                Marshal.FinalReleaseComObject(desktop);
        }
    }

    [ComImport]
    [Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaper { }

    private enum DesktopWallpaperPosition
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5
    }

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID,
            [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID,
            [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        void GetMonitorDevicePathAt(uint monitorIndex,
            [MarshalAs(UnmanagedType.LPWStr)] out string monitorID);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out NativeRect displayRect);
        void SetBackgroundColor(uint color);
        void GetBackgroundColor(out uint color);
        void SetPosition(DesktopWallpaperPosition position);
        void GetPosition(out DesktopWallpaperPosition position);
        void SetSlideshow(IntPtr items);
        void GetSlideshow(out IntPtr items);
        void SetSlideshowOptions(uint options, uint slideshowTick);
        void GetSlideshowOptions(out uint options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, uint direction);
        void GetStatus(out uint state);
        void Enable(bool enable);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
