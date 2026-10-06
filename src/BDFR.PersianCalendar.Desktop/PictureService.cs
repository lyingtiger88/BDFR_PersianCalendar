using BDFR.PersianCalendar.Core;
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

        var file = occasion.Source == "personal"
            ? "personal.png"
            : ContainsAny(title, "تولد", "سالگرد")
                ? "birthday.png"
                : ContainsAny(title, "نوروز", "تحویل سال", "سیزده بدر", "طبیعت")
                    ? "nowruz.png"
                    : ContainsAny(title, "یلدا", "شب چله")
                        ? "yalda.png"
                        : ContainsAny(title, "عاشورا", "تاسوعا", "اربعین", "رمضان", "فطر", "قربان", "غدیر", "پیامبر", "امام", "حضرت")
                            ? "religious.png"
                            : ContainsAny(title, "انقلاب", "جمهوری اسلامی", "ملی شدن", "استقلال", "آزادی")
                                ? "national.png"
                                : "default.png";

        var path = Path.Combine(EventPicturesRoot, file);
        if (File.Exists(path))
            return path;

        return Path.Combine(EventPicturesRoot, "default.png");
    }

    public static async Task<BitmapImage?> LoadBitmapAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return image;
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
