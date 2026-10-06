using BDFR.PersianCalendar.Core;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BDFR.PersianCalendar.Desktop;

public sealed class PictureService(ThemeService themeService)
{
    public string EventPicturesRoot =>
        Path.Combine(themeService.PictureRoot, "event picture");

    public string ResolveOccasionPicture(Occasion occasion)
    {
        var title = occasion.Title ?? string.Empty;

        var file = ContainsAny(title, "تولد", "سالگرد")
            ? "birthday.svg"
            : ContainsAny(title, "نوروز", "تحویل سال", "سیزده بدر", "طبیعت")
                    ? "nowruz.svg"
                    : ContainsAny(title, "یلدا", "شب چله")
                        ? "yalda.svg"
                        : ContainsAny(title, "عاشورا", "تاسوعا", "اربعین", "رمضان", "فطر", "قربان", "غدیر", "پیامبر", "امام", "حضرت")
                            ? "religious.svg"
                            : ContainsAny(title, "انقلاب", "جمهوری اسلامی", "ملی شدن", "استقلال", "آزادی")
                                ? "national.svg"
                                : occasion.Source == "personal"
                                    ? "personal.svg"
                                    : "default.svg";

        var path = Path.Combine(EventPicturesRoot, file);
        if (File.Exists(path))
            return path;

        return Path.Combine(EventPicturesRoot, "default.svg");
    }

    public static async Task<ImageSource?> LoadImageAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            if (string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
            {
                var svg = new SvgImageSource();
                await svg.SetSourceAsync(stream);
                return svg;
            }

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Picture load failed ({path}): {ex}");
            return null;
        }
    }

    private static bool ContainsAny(string text, params string[] candidates)
        => candidates.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
}
