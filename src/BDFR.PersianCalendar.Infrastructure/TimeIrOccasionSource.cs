using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using BDFR.PersianCalendar.Core;

namespace BDFR.PersianCalendar.Infrastructure;

public sealed class TimeIrOccasionSource(HttpClient httpClient) : IOccasionSource
{
    public const string AnnualCalendarUrl = "https://www.time.ir/event-year";
    private const string HomeUrl = "https://www.time.ir/";

    public string Name => "time.ir";

    public async Task<IReadOnlyList<Occasion>> GetYearAsync(
        int persianYear,
        CancellationToken cancellationToken = default)
    {
        var candidates = new[]
        {
            $"{AnnualCalendarUrl}?year={persianYear}",
            AnnualCalendarUrl
        };

        var failures = new List<string>();

        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
                await WarmUpAsync(cancellationToken);

            foreach (var url in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var request = CreateRequest(url);
                    using var response = await httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseContentRead,
                        cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        failures.Add($"{(int)response.StatusCode} {response.ReasonPhrase}");
                        continue;
                    }

                    var html = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!LooksLikeRequestedYear(html, persianYear))
                    {
                        failures.Add($"پاسخ دریافت شد اما تقویم سال {persianYear} در صفحه تشخیص داده نشد");
                        continue;
                    }

                    var parsed = await ParseAnnualHtmlAsync(html, persianYear, cancellationToken);
                    if (parsed.Count > 0)
                        return parsed;

                    failures.Add("پاسخ دریافت شد اما هیچ مناسبت قابل‌خواندنی پیدا نشد");
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    failures.Add("مهلت اتصال به time.ir تمام شد");
                }
                catch (HttpRequestException ex)
                {
                    failures.Add($"خطای شبکه: {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    failures.Add(ex.Message);
                }
            }
        }

        var detail = failures.Count == 0
            ? "پاسخ معتبری دریافت نشد"
            : string.Join(" | ", failures.Distinct().Take(4));

        throw new InvalidOperationException(
            $"داده سال {persianYear} از time.ir دریافت نشد. {detail}. آخرین cache محلی حفظ می‌شود.");
    }

    public static async Task<IReadOnlyList<Occasion>> ParseAnnualHtmlAsync(
        string html,
        int persianYear,
        CancellationToken cancellationToken = default)
    {
        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, cancellationToken);
        var result = new Dictionary<string, Occasion>(StringComparer.Ordinal);

        // Pass 1: older and structured time.ir layouts.
        for (var monthIndex = 0; monthIndex < 12; monthIndex++)
        {
            var root = document.QuerySelector($"#Month_{monthIndex}");
            if (root is null) continue;

            ParseCandidateElements(
                root.QuerySelectorAll("*"),
                persianYear,
                monthIndex + 1,
                PersianDate.MonthNames[monthIndex],
                result);
        }

        // Pass 2: current/future layouts. Do not depend on specific wrapper tags/classes.
        foreach (var element in document.QuerySelectorAll("body *"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element.LocalName is "script" or "style" or "noscript" or "svg" or "path")
                continue;

            var text = Collapse(element.TextContent);
            if (text.Length is 0 or > 500)
                continue;

            for (var monthIndex = 0; monthIndex < PersianDate.MonthNames.Length; monthIndex++)
            {
                var monthName = PersianDate.MonthNames[monthIndex];
                var match = MatchOccasion(text, monthName);
                if (!match.Success) continue;

                AddOccasion(
                    element,
                    persianYear,
                    monthIndex + 1,
                    match,
                    result);

                break;
            }
        }

        // Pass 3: plain-text fallback. This survives wrapper/class changes on time.ir.
        foreach (var text in ExtractLooseTextLines(html))
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var monthIndex = 0; monthIndex < PersianDate.MonthNames.Length; monthIndex++)
            {
                var monthName = PersianDate.MonthNames[monthIndex];
                var match = MatchOccasion(text, monthName);
                if (!match.Success) continue;

                AddOccasion(
                    node: null,
                    persianYear,
                    monthIndex + 1,
                    match,
                    result);

                break;
            }
        }

        var items = result.Values
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Title)
            .ToArray();

        if (items.Length == 0)
            throw new InvalidOperationException(
                $"ساختار مناسبت‌های time.ir برای سال {persianYear} شناخته نشد.");

        return items;
    }

    private async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest(HomeUrl);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.Content is not null)
                _ = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Retry loop will report the final timeout.
        }
        catch (HttpRequestException)
        {
            // Retry loop will report the final network error.
        }
    }

    private static HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36 Edg/153.0.0.0");
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation(
            "Accept-Language",
            "fa-IR,fa;q=0.9,en-US;q=0.6,en;q=0.5");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        request.Headers.Referrer = new Uri(HomeUrl);

        return request;
    }

    private static bool LooksLikeRequestedYear(string html, int year)
    {
        var normalized = PersianQuickAddParser.NormalizeDigits(
            WebUtility.HtmlDecode(html));

        var count = Regex.Matches(
            normalized,
            $@"(?<!\d){year}(?!\d)",
            RegexOptions.CultureInvariant).Count;

        // The annual page repeats the active Persian year in multiple month sections.
        // Requiring more than one occurrence also prevents accidentally importing a
        // historical year that only appears inside an event title.
        return count >= 2;
    }

    private static void ParseCandidateElements(
        IEnumerable<IElement> elements,
        int persianYear,
        int month,
        string monthName,
        IDictionary<string, Occasion> result)
    {
        foreach (var node in elements)
        {
            var text = Collapse(node.TextContent);
            if (text.Length is 0 or > 500)
                continue;

            var match = MatchOccasion(text, monthName);
            if (!match.Success) continue;

            AddOccasion(node, persianYear, month, match, result);
        }
    }

    private static Match MatchOccasion(string text, string monthName)
        => Regex.Match(
            text,
            $@"^(?<day>[0-9۰-۹٠-٩]{{1,2}})\s+{Regex.Escape(monthName)}\s+(?<title>.+)$",
            RegexOptions.CultureInvariant);

    private static void AddOccasion(
        IElement? node,
        int persianYear,
        int month,
        Match match,
        IDictionary<string, Occasion> result)
    {
        if (node is not null)
        {
            // Skip wrapper elements that only duplicate a more specific nested event row.
            var monthName = PersianDate.MonthNames[month - 1];
            var containsNestedEventRow = node.Children.Any(child =>
                MatchOccasion(Collapse(child.TextContent), monthName).Success);
            if (containsNestedEventRow) return;
        }

        var dayText = PersianQuickAddParser.NormalizeDigits(match.Groups["day"].Value);
        if (!int.TryParse(dayText, out var day) ||
            day < 1 ||
            day > PersianDate.DaysInMonth(persianYear, month))
            return;

        var title = Collapse(match.Groups["title"].Value);
        if (string.IsNullOrWhiteSpace(title))
            return;

        var date = new PersianDate(persianYear, month, day);
        var isHoliday =
            (node is not null && HasHolidayClass(node)) ||
            LooksLikeOfficialHoliday(date, title);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{date}|{title}|{isHoliday}")));

        var id = $"timeir-{persianYear:0000}{month:00}{day:00}-{hash[..12].ToLowerInvariant()}";
        var key = $"{date}|{title}";

        // If a later pass knows this row is a holiday, keep the richer version.
        if (result.TryGetValue(key, out var existing) && existing.IsHoliday && !isHoliday)
            return;

        result[key] = new Occasion(id, date, title, isHoliday, "time.ir", hash);
    }

    private static IEnumerable<string> ExtractLooseTextLines(string html)
    {
        var value = Regex.Replace(
            html,
            @"<script\b[^>]*>[\s\S]*?</script>|<style\b[^>]*>[\s\S]*?</style>|<noscript\b[^>]*>[\s\S]*?</noscript>",
            "\n",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        value = Regex.Replace(
            value,
            @"<(br\s*/?|/div|/li|/p|/tr|/td|/section|/article|/h[1-6])\b[^>]*>",
            "\n",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        value = Regex.Replace(
            value,
            @"<[^>]+>",
            " ",
            RegexOptions.CultureInvariant);

        value = WebUtility.HtmlDecode(value)
            .Replace("\r", "\n", StringComparison.Ordinal);

        foreach (var rawLine in value.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = Collapse(rawLine);
            if (line.Length is > 0 and <= 500)
                yield return line;
        }
    }

    private static bool HasHolidayClass(IElement node)
    {
        IElement? current = node;
        while (current is not null)
        {
            if (current.ClassList.Any(c =>
                    c.Contains("holiday", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (current.Id.StartsWith("Month_", StringComparison.OrdinalIgnoreCase))
                break;

            current = current.ParentElement;
        }

        return node.QuerySelector("[class*='holiday' i]") is not null;
    }

    private static bool LooksLikeOfficialHoliday(
        PersianDate date,
        string title)
        => OfficialHolidayClassifier.IsOfficialHoliday(
            date,
            title,
            sourceMarkedHoliday: false);

    private static string Collapse(string value)
        => Regex.Replace(
                value
                    .Replace('\u200c', ' ')
                    .Replace('\u200f', ' ')
                    .Replace('\ufeff', ' '),
                @"\s+",
                " ")
            .Trim();
}
