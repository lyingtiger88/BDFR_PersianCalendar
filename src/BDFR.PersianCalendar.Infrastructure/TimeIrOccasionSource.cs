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

        foreach (var url in candidates.Distinct())
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) continue;

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = await ParseAnnualHtmlAsync(html, persianYear, cancellationToken);
            if (parsed.Count > 0) return parsed;
        }

        throw new InvalidOperationException(
            $"داده سال {persianYear} از time.ir دریافت نشد. آخرین cache محلی حفظ می‌شود.");
    }

    public static async Task<IReadOnlyList<Occasion>> ParseAnnualHtmlAsync(
        string html,
        int persianYear,
        CancellationToken cancellationToken = default)
    {
        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, cancellationToken);
        var result = new Dictionary<string, Occasion>(StringComparer.Ordinal);

        // Pass 1: preferred structured month containers used by time.ir.
        for (var monthIndex = 0; monthIndex < 12; monthIndex++)
        {
            var root = document.QuerySelector($"#Month_{monthIndex}");
            if (root is null) continue;

            ParseCandidateElements(
                root.QuerySelectorAll("div,li,p,span"),
                persianYear,
                monthIndex + 1,
                PersianDate.MonthNames[monthIndex],
                result);
        }

        // Pass 2: resilient fallback for layout changes. The current site still exposes
        // human-readable rows like "14 خرداد رحلت حضرت امام خمینی"; parse those
        // regardless of wrapper ids/classes.
        foreach (var element in document.QuerySelectorAll("div,li,p,span"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = Collapse(element.TextContent);

            for (var monthIndex = 0; monthIndex < PersianDate.MonthNames.Length; monthIndex++)
            {
                var monthName = PersianDate.MonthNames[monthIndex];
                var match = Regex.Match(
                    text,
                    $@"^(?<day>[0-9۰-۹٠-٩]{{1,2}})\s+{Regex.Escape(monthName)}\s+(?<title>.+)$",
                    RegexOptions.CultureInvariant);

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

        var items = result.Values
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Title)
            .ToArray();

        if (items.Length == 0)
            throw new InvalidOperationException(
                $"ساختار مناسبت‌های time.ir برای سال {persianYear} شناخته نشد.");

        return items;
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
            var match = Regex.Match(
                text,
                $@"^(?<day>[0-9۰-۹٠-٩]{{1,2}})\s+{Regex.Escape(monthName)}\s+(?<title>.+)$",
                RegexOptions.CultureInvariant);

            if (!match.Success) continue;
            AddOccasion(node, persianYear, month, match, result);
        }
    }

    private static void AddOccasion(
        IElement node,
        int persianYear,
        int month,
        Match match,
        IDictionary<string, Occasion> result)
    {
        // Skip wrapper elements that only duplicate a more specific nested event row.
        var monthName = PersianDate.MonthNames[month - 1];
        var containsNestedEventRow = node.Children.Any(child =>
            Regex.IsMatch(
                Collapse(child.TextContent),
                $@"^[0-9۰-۹٠-٩]{{1,2}}\s+{Regex.Escape(monthName)}\s+.+$",
                RegexOptions.CultureInvariant));
        if (containsNestedEventRow) return;

        var dayText = PersianQuickAddParser.NormalizeDigits(match.Groups["day"].Value);
        if (!int.TryParse(dayText, out var day) ||
            day < 1 ||
            day > PersianDate.DaysInMonth(persianYear, month))
            return;

        var title = Collapse(match.Groups["title"].Value);
        if (string.IsNullOrWhiteSpace(title)) return;

        var isHoliday = HasHolidayClass(node);
        var date = new PersianDate(persianYear, month, day);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{date}|{title}|{isHoliday}")));

        var id = $"timeir-{persianYear:0000}{month:00}{day:00}-{hash[..12].ToLowerInvariant()}";
        result[$"{date}|{title}"] =
            new Occasion(id, date, title, isHoliday, "time.ir", hash);
    }

    private static bool ContainsYear(IElement root, int year)
    {
        var normalized = PersianQuickAddParser.NormalizeDigits(root.TextContent);
        return Regex.IsMatch(normalized, $@"(?<!\d){year}(?!\d)");
    }

    private static bool HasHolidayClass(IElement node)
        => node.ClassList.Any(c => c.Contains("holiday", StringComparison.OrdinalIgnoreCase))
           || node.QuerySelector("[class*='holiday' i]") is not null;

    private static string Collapse(string value)
        => Regex.Replace(value.Replace('\u200c', ' '), @"\s+", " ").Trim();
}