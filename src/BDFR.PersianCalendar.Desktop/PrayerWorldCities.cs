using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace BDFR.PersianCalendar.Desktop;

/// <summary>
/// GeoNames cities500: populated places >=500 inhabitants, not every settlement.
/// Source: https://download.geonames.org/export/dump/ ; CC BY 4.0.
/// Cached per user; no location permissions required.
/// </summary>
internal static class PrayerWorldCities
{
    internal sealed record City(long Id, string Name, string Country, double Lat, double Lon, string Zone);
    private static IReadOnlyList<City>? _cities;
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zurvan", "GeoNames");
    private static readonly string FilePath = Path.Combine(Folder, "cities500.txt");
    internal static bool HasCache => File.Exists(FilePath);

    internal static IReadOnlyList<(string Code, string Name)> Countries =>
        CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(c => new RegionInfo(c.Name))
            .GroupBy(c => c.TwoLetterISORegionName, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Code:g.Key, Name:g.First().DisplayName))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    internal static async Task<IReadOnlyList<City>> LoadAsync()
    {
        if (_cities is not null) return _cities;
        if (!HasCache) return [];
        var cities = new List<City>(200000);
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 131072, useAsync: true);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync() is { } line)
        {
            var f = line.Split('\t');
            if (f.Length < 18 || f[6] != "P" ||
                !long.TryParse(f[0], out var id) ||
                !double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
                string.IsNullOrEmpty(f[17])) continue;
            if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180) continue;
            cities.Add(new City(id, f[1], f[8], lat, lon, f[17]));
        }
        return _cities = cities;
    }

    internal static async Task<IReadOnlyList<City>> UpdateAsync()
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };
            using var response = await client.GetAsync(
                "https://download.geonames.org/export/dump/cities500.zip",
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var remote = await response.Content.ReadAsStreamAsync();
            using var archive = new ZipArchive(remote, ZipArchiveMode.Read, leaveOpen:false);
            var entry = archive.GetEntry("cities500.txt")
                ?? throw new InvalidDataException("GeoNames cities500.txt not found");
            await using (var input = entry.Open())
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write,
                             FileShare.None, 131072, useAsync:true))
                await input.CopyToAsync(output);
            if (new FileInfo(temp).Length == 0)
                throw new InvalidDataException("Empty GeoNames file");
            File.Move(temp, FilePath, overwrite:true);
            _cities = null;
            return await LoadAsync();
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
