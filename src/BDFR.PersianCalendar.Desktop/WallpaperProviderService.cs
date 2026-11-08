using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BDFR.PersianCalendar.Desktop;

public sealed record WallpaperItem(
    string Id,
    string Title,
    string Category,
    int Width,
    int Height,
    long FileSize,
    string ImageUrl,
    string PageUrl,
    string ThumbnailUrl,
    IReadOnlyList<string> Tags);

public sealed record WallpaperSearchResult(IReadOnlyList<WallpaperItem> Items, int CurrentPage, int TotalPages);

public sealed class WallpaperProviderService
{
    private const string ApiRoot = "https://wallhaven.cc/api/v1/";
    private const long MaxDownloadBytes = 40L * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    public string GalleryRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Zurvan", "WallpaperGallery");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZurvanWallpaperManager/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<WallpaperSearchResult> SearchPageAsync(
        string query,
        string category,
        int minimumWidth,
        int minimumHeight,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        page = Math.Clamp(page, 1, 100);
        minimumWidth = Math.Clamp(minimumWidth, 800, 16384);
        minimumHeight = Math.Clamp(minimumHeight, 600, 16384);
        // Wallhaven's "atleast" filter expects one of its supported resolution
        // presets. Results are additionally filtered against the actual monitor.
        var apiMinimum = minimumWidth >= 3840 && minimumHeight >= 2160
            ? (Width: 3840, Height: 2160)
            : minimumWidth >= 2560 && minimumHeight >= 1440
                ? (Width: 2560, Height: 1440)
                : minimumWidth >= 1920 && minimumHeight >= 1200
                    ? (Width: 1920, Height: 1200)
                    : (Width: 1920, Height: 1080);
        var categoryFlags = category switch
        {
            "anime" => "010",
            "people" => "001",
            "all" => "111",
            _ => "100"
        };
        var terms = new List<string>
        {
            "categories=" + categoryFlags,
            "purity=100",
            "sorting=relevance",
            "order=desc",
            $"atleast={apiMinimum.Width}x{apiMinimum.Height}",
            $"page={page}"
        };
        if (!string.IsNullOrWhiteSpace(query))
            terms.Add("q=" + Uri.EscapeDataString(query.Trim()));

        var url = ApiRoot + "search?" + string.Join("&", terms);
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return new WallpaperSearchResult([], page, page);

        var items = new List<WallpaperItem>();
        foreach (var entry in data.EnumerateArray())
        {
            var id = ReadString(entry, "id");
            var path = ReadString(entry, "path");
            if (string.IsNullOrWhiteSpace(id) || !Uri.TryCreate(path, UriKind.Absolute, out var imageUri) ||
                imageUri.Scheme != Uri.UriSchemeHttps)
                continue;

            var width = ReadInt(entry, "dimension_x");
            var height = ReadInt(entry, "dimension_y");
            var size = ReadLong(entry, "file_size");
            if (width < minimumWidth || height < minimumHeight || size <= 0 || size > MaxDownloadBytes)
                continue;

            var categoryName = ReadString(entry, "category");
            var pageUrl = ReadString(entry, "url");
            var thumbnail = "";
            if (entry.TryGetProperty("thumbs", out var thumbs))
                thumbnail = ReadString(thumbs, "large");
            var tags = new List<string>();
            if (entry.TryGetProperty("tags", out var tagArray) && tagArray.ValueKind == JsonValueKind.Array)
                foreach (var tag in tagArray.EnumerateArray())
                    if (!string.IsNullOrWhiteSpace(ReadString(tag, "name")))
                        tags.Add(ReadString(tag, "name"));

            items.Add(new WallpaperItem(
                id, tags.FirstOrDefault() ?? "Wallpaper",
                string.IsNullOrWhiteSpace(categoryName) ? "general" : categoryName,
                width, height, size, imageUri.ToString(),
                Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri) ? pageUri.ToString() : $"https://wallhaven.cc/w/{id}",
                thumbnail, tags));
        }
        return items;
    }

    public async Task<string> DownloadAsync(
        WallpaperItem item,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(item.ImageUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.EndsWith("wallhaven.cc", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("نشانی تصویر از منبع معتبر نیست.");

        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            throw new InvalidOperationException("قالب تصویر پشتیبانی نمی‌شود.");

        var category = SanitizeCategory(item.Tags.FirstOrDefault() ?? item.Category);
        var folder = Path.Combine(GalleryRoot, category);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, item.Id + extension);
        if (File.Exists(target)) return target;

        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > MaxDownloadBytes))
            throw new InvalidOperationException("حجم تصویر خارج از محدوده مجاز است.");

        var temporary = target + ".download";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes)
                        throw new InvalidOperationException("حجم تصویر از سقف ۴۰ مگابایت بیشتر است.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            var info = new FileInfo(temporary);
            if (!info.Exists || info.Length < 1024)
                throw new InvalidDataException("فایل دریافت‌شده معتبر نیست.");

            File.Move(temporary, target, true);
            var metadata = new
            {
                item.Id, item.Title, item.Category, GalleryCategory = category, item.Width, item.Height,
                item.PageUrl, item.ImageUrl, item.Tags,
                Provider = "Wallhaven", DownloadedAtUtc = DateTimeOffset.UtcNow
            };
            await File.WriteAllTextAsync(Path.ChangeExtension(target, ".json"),
                JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            return target;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public IReadOnlyList<string> GetGalleryCategories()
    {
        if (!Directory.Exists(GalleryRoot)) return [];
        return Directory.EnumerateDirectories(GalleryRoot)
            .Select(path => Path.GetFileName(path))
            .OfType<string>()
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> GetGalleryFiles(string? category = null)
    {
        if (!Directory.Exists(GalleryRoot)) return [];
        var root = string.IsNullOrWhiteSpace(category)
            ? GalleryRoot
            : Path.Combine(GalleryRoot, SanitizeCategory(category));
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(x => Path.GetExtension(x).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
    }

    private static string SanitizeCategory(string? value)
    {
        var safe = new string((value ?? "general")
            .Trim().ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            .Take(48)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "general" : safe;
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static int ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static long ReadLong(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;
}
