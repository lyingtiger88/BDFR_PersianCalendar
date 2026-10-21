using BDFR.PersianCalendar.Core;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BDFR.PersianCalendar.Desktop;

public sealed class PictureService(ThemeService themeService)
{
    private sealed record OccasionPictureMapEntry(
        string Pattern,
        string NormalizedPattern,
        string PicturePath);

    private readonly object _occasionMapLock = new();
    private IReadOnlyList<OccasionPictureMapEntry>? _occasionMapCache;
    private DateTime _occasionMapCacheStampUtc;

    public string EventPicturesRoot =>
        Path.Combine(themeService.PictureRoot, "event picture");

    public string OccasionPicturesRoot =>
        Path.Combine(themeService.PictureRoot, "occasion");

    public string PersianOccasionPicturesRoot =>
        Path.Combine(OccasionPicturesRoot, "persian");

    public string HijriOccasionPicturesRoot =>
        Path.Combine(OccasionPicturesRoot, "hijri");

    public string GregorianOccasionPicturesRoot =>
        Path.Combine(OccasionPicturesRoot, "gregorian");

    public string ResolveOccasionPicture(Occasion occasion)
    {
        var title = occasion.Title ?? string.Empty;
        var normalizedTitle = NormalizeOccasionText(title);

        foreach (var entry in GetOccasionPictureMap())
        {
            if (normalizedTitle.Contains(
                    entry.NormalizedPattern,
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(entry.PicturePath))
            {
                return entry.PicturePath;
            }
        }

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

    public void ReloadOccasionPictureMappings()
    {
        lock (_occasionMapLock)
        {
            _occasionMapCache = null;
            _occasionMapCacheStampUtc = DateTime.MinValue;
        }
    }

    public static async Task<ImageSource?> LoadImageAsync(
        string? path,
        int decodePixelWidth = 0)
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
            if (decodePixelWidth > 0)
                bitmap.DecodePixelWidth = decodePixelWidth;
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Picture load failed ({path}): {ex}");
            return null;
        }
    }

    private IReadOnlyList<OccasionPictureMapEntry> GetOccasionPictureMap()
    {
        lock (_occasionMapLock)
        {
            var mappingFiles = new[]
            {
                Path.Combine(PersianOccasionPicturesRoot, "occasions.txt"),
                Path.Combine(HijriOccasionPicturesRoot, "occasions.txt"),
                Path.Combine(GregorianOccasionPicturesRoot, "occasions.txt")
            };

            var latestStamp = mappingFiles
                .Where(File.Exists)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            if (_occasionMapCache is not null &&
                latestStamp <= _occasionMapCacheStampUtc)
            {
                return _occasionMapCache;
            }

            var entries = new List<OccasionPictureMapEntry>();

            foreach (var mappingFile in mappingFiles)
            {
                if (!File.Exists(mappingFile))
                    continue;

                var folder = Path.GetDirectoryName(mappingFile)!;

                foreach (var rawLine in File.ReadLines(mappingFile))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 ||
                        line.StartsWith("#", StringComparison.Ordinal) ||
                        line.StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('|');
                    if (separatorIndex <= 0 || separatorIndex >= line.Length - 1)
                        continue;

                    var pattern = line[..separatorIndex].Trim();
                    var imageName = line[(separatorIndex + 1)..].Trim();

                    if (pattern.Length == 0 || imageName.Length == 0)
                        continue;

                    var normalizedPattern = NormalizeOccasionText(pattern);
                    if (normalizedPattern.Length == 0)
                        continue;

                    var picturePath = Path.GetFullPath(
                        Path.Combine(folder, imageName));

                    entries.Add(new OccasionPictureMapEntry(
                        pattern,
                        normalizedPattern,
                        picturePath));
                }
            }

            // More specific/longer patterns win over broad words such as "امام".
            _occasionMapCache = entries
                .OrderByDescending(x => x.NormalizedPattern.Length)
                .ThenBy(x => x.Pattern, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _occasionMapCacheStampUtc = latestStamp;

            StartupDiagnostics.Log(
                $"Occasion picture mappings loaded: {_occasionMapCache.Count} entries.");

            return _occasionMapCache;
        }
    }

    private static string NormalizeOccasionText(string? text)
        => PersianQuickAddParser.NormalizeDigits(text ?? string.Empty)
            .Replace('ي', 'ی')
            .Replace('ك', 'ک')
            .Replace('ۀ', 'ه')
            .Replace('ة', 'ه')
            .Replace('\u200c', ' ')
            .Replace('\u200f', ' ')
            .Replace('\ufeff', ' ')
            .Trim()
            .ToLowerInvariant();

    private static bool ContainsAny(string text, params string[] candidates)
        => candidates.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
}
