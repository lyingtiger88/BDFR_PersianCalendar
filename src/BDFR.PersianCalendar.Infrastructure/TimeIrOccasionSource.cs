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

    public async Task<IReadOnlyList<Occasion>> GetYearAsync(int persianYear, CancellationToken cancellationToken = default)
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

        throw new InvalidOperationException($"داده سال {persianYear} از time.ir دریافت نشد. آخرین cache محلی باید حفظ شود.");
    }

    public static async Task<IReadOnlyList<Occasion>> ParseAnnualHtmlAsync(string html, int persianYear, CancellationToken cancellationToken = default)
    {
        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, cancellationToken);
        var result = new Dictionary<string, Occasion>(StringComparer.Ordinal);

        for (var monthIndex = 0; monthIndex < 12; monthIndex++)
        {
            var month = monthIndex + 1;
            var monthName = PersianDate.MonthNames[monthIndex];
            var root = document.QuerySelector($"#Month_{monthIndex}");
            if (root is null) continue;

            // Guard against time.ir returning a different year after a year request.
            if (!ContainsYear(root, persianYear)) continue;

            var eventList = root.Children.Where(x => x.LocalName.Equals("div", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
            var nodes = eventList?.QuerySelectorAll("div > div > div") ?? root.QuerySelectorAll("div");

            foreach (var node in nodes)
            {
                var text = Collapse(node.TextContent);
                var match = Regex.Match(text,
                    $@"^(?<day>[0-9۰-۹٠-٩]{{1,2}})s+{Regex.Escape(monthName)}s+(?<title>.+)$",
                    RegexOptions.CultureInvariant);
                if (!match.Success) continue;

                var dayText = PersianQuickAddParser.NormalizeDigits(match.Groups["day"].Value);
                if (!int.TryParse(dayText, out var day) || day < 1 || day > PersianDate.DaysInMonth(persianYear, month)) continue;

                var title = Collapse(match.Groups["title"].Value);
                if (string.IsNullOrWhiteSpace(title)) continue;

                var isHoliday = HasHolidayClass(node);
                var date = new PersianDate(persianYear, month, day);
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{date}|{title}|{isHoliday}")));
                var id = $"timeir-{persianYear:0000}{month:00}{day:00}-{hash[..12].ToLowerInvariant()}";
                result[$"{date}|{title}"] = new Occasion(id, date, title, isHoliday, "time.ir", hash);
            }
        }

        return result.Values.OrderBy(x => x.Date).ThenBy(x => x.Title).ToArray();
    }

    private static bool ContainsYear(IElement root, int year)
    {
        var normalized = PersianQuickAddParser.NormalizeDigits(root.TextContent);
        return Regex.IsMatch(normalized, $@"(?<!d){year}(?!d)");
    }

    private static bool HasHolidayClass(IElement node)
        => node.ClassList.Any(c => c.Contains("holiday", StringComparison.OrdinalIgnoreCase))
           || node.QuerySelector("[class*='holiday' i]") is not null;

    private static string Collapse(string value)
        => Regex.Replace(value.Replace('\u200c', ' '), @"s+", " ").Trim();
}