using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private static readonly (string Id, string Label, double Fajr, double Isha, double Maghrib, int IshaMinutes)[] PrayerMethods =
    [
        ("tehran", "ژئوفیزیک تهران (شیعه)", 17.7, 14.0, 4.5, 0),
        ("jafari", "جعفری / قم (شیعه)", 16.0, 14.0, 4.0, 0),
        ("mwl", "رابطة العالم الإسلامي", 18.0, 17.0, 0.833, 0),
        ("isna", "ISNA", 15.0, 15.0, 0.833, 0),
        ("egypt", "مصر", 19.5, 17.5, 0.833, 0),
        ("karachi", "کراچی", 18.0, 18.0, 0.833, 0),
        ("ummalqura", "ام‌القری", 18.5, 0.0, 0.833, 90)
    ];
    private static readonly (string Id, string Label)[] PrayerSchools =
    [
        ("jafari", "شیعه امامیه (جعفری)"),
        ("hanafi", "سنی حنفی"),
        ("shafii", "سنی شافعی"),
        ("maliki", "سنی مالکی"),
        ("hanbali", "سنی حنبلی")
    ];

    private FrameworkElement BuildDesktopPrayerSettings()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "🕌 اوقات شرعی (محاسبه نجومی)",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        var schoolBox = new ComboBox { Header = "مذهب و فقه", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var school in PrayerSchools) schoolBox.Items.Add(school.Label);
        schoolBox.SelectedIndex = Math.Max(0, Array.FindIndex(PrayerSchools, school => school.Id == _settings.PrayerSchool));
        panel.Children.Add(schoolBox);

        var methodBox = new ComboBox { Header = "روش محاسبه", HorizontalAlignment = HorizontalAlignment.Stretch };
        void UpdateMethods()
        {
            var school = PrayerSchools[Math.Max(0, schoolBox.SelectedIndex)].Id;
            var choices = PrayerMethods.Where(m => (school == "jafari") == (m.Id is "jafari" or "tehran")).ToArray();
            var old = methodBox.SelectedItem?.ToString();
            methodBox.Items.Clear();
            foreach (var method in choices) methodBox.Items.Add(method.Label);
            var current = Array.FindIndex(choices, x => x.Id == _settings.PrayerMethod);
            methodBox.SelectedIndex = current < 0 ? 0 : current;
        }
        schoolBox.SelectionChanged += (_, _) => UpdateMethods();
        UpdateMethods();
        panel.Children.Add(methodBox);

        var city = new TextBox { Header = "شهر", Text = _settings.PrayerCity };
        var latitude = new TextBox { Header = "عرض جغرافیایی", Text = _settings.PrayerLatitude.ToString(CultureInfo.InvariantCulture) };
        var longitude = new TextBox { Header = "طول جغرافیایی", Text = _settings.PrayerLongitude.ToString(CultureInfo.InvariantCulture) };
        var timeZone = new TextBox { Header = "منطقه زمانی (مانند Asia/Tehran)", Text = _settings.PrayerTimeZone };
        panel.Children.Add(city);
        panel.Children.Add(latitude);
        panel.Children.Add(longitude);
        panel.Children.Add(timeZone);
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(result);

        void Display()
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(_settings.PrayerTimeZone);
                var day = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date;
                var prayer = CalculateDesktopPrayerTimes(day, _settings.PrayerLatitude, _settings.PrayerLongitude,
                    zone, _settings.PrayerSchool, _settings.PrayerMethod);
                result.Text = prayer is null
                    ? "در این عرض جغرافیایی امکان محاسبه همه ساعت‌ها وجود ندارد؛ جدول محلی معتبر را بررسی کنید."
                    : _settings.PrayerCity + " · " + day.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) +
                    "\n" + string.Join("\n", prayer.Select(x => x.Name + ": " + x.Time.ToString("HH:mm", CultureInfo.InvariantCulture)));
            }
            catch (Exception ex)
            {
                result.Text = "محاسبه ناموفق: " + ex.Message;
            }
        }

        var save = MakeButton("ذخیره و نمایش اوقات شرعی");
        save.Click += (_, _) =>
        {
            if (!double.TryParse(PersianNumber(latitude.Text), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(PersianNumber(longitude.Text), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                || !double.IsFinite(lat) || !double.IsFinite(lon) || Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
            {
                result.Text = "مختصات نامعتبر هستند.";
                return;
            }
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone.Text.Trim()); }
            catch { result.Text = "شناسه منطقه زمانی نامعتبر است."; return; }
            var school = PrayerSchools[Math.Max(0, schoolBox.SelectedIndex)].Id;
            var choices = PrayerMethods.Where(m => (school == "jafari") == (m.Id is "jafari" or "tehran")).ToArray();
            _settings.PrayerSchool = school;
            _settings.PrayerMethod = choices[Math.Max(0, methodBox.SelectedIndex)].Id;
            _settings.PrayerLatitude = lat;
            _settings.PrayerLongitude = lon;
            _settings.PrayerCity = city.Text.Trim();
            _settings.PrayerTimeZone = timeZone.Text.Trim();
            _settingsService.Save(_settings);
            Display();
        };
        panel.Children.Add(save);
        panel.Children.Add(new TextBlock
        {
            Text = "این ساعت‌ها تخمین نجومی هستند؛ اختلاف با تقویم رسمی، رؤیت هلال، نظر مرجع و شرایط عرض جغرافیایی بالا ممکن است. اعلان صوتی اذان هنوز در دسکتاپ فعال نیست.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7
        });
        Display();
        return panel;
    }

    private static string PersianNumber(string value) =>
        value.Replace('۰','0').Replace('۱','1').Replace('۲','2').Replace('۳','3').Replace('۴','4')
            .Replace('۵','5').Replace('۶','6').Replace('۷','7').Replace('۸','8').Replace('۹','9')
            .Replace('٠','0').Replace('١','1').Replace('٢','2').Replace('٣','3').Replace('٤','4')
            .Replace('٥','5').Replace('٦','6').Replace('٧','7').Replace('٨','8').Replace('٩','9');

    private static IReadOnlyList<(string Name, TimeOnly Time)>? CalculateDesktopPrayerTimes(
        DateTime day, double latitude, double longitude, TimeZoneInfo zone, string school, string methodId)
    {
        var method = PrayerMethods.FirstOrDefault(x => x.Id == methodId);
        if (method.Id is null) method = PrayerMethods[0];
        var g = 2 * Math.PI / 365 * (day.DayOfYear - 1);
        var equation = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g)
            - 0.014615 * Math.Cos(2*g) - 0.040849 * Math.Sin(2*g));
        var declination = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g)
            - 0.006758 * Math.Cos(2*g) + 0.000907 * Math.Sin(2*g)
            - 0.002697 * Math.Cos(3*g) + 0.00148 * Math.Sin(3*g);
        double Rad(double x) => x * Math.PI / 180;
        double Hour(double altitude)
        {
            var c = (Math.Sin(Rad(altitude)) - Math.Sin(Rad(latitude))*Math.Sin(declination))
                / (Math.Cos(Rad(latitude))*Math.Cos(declination));
            return c < -1 || c > 1 ? double.NaN : Math.Acos(c)*12/Math.PI;
        }
        var noon = 12 - longitude/15 - equation/60;
        var rise = Hour(-0.833);
        var fajr = Hour(-method.Fajr);
        var maghrib = Hour(-method.Maghrib);
        var asrAltitude = Math.Atan(1/( (school == "hanafi" ? 2 : 1) + Math.Tan(Math.Abs(Rad(latitude) - declination))))*180/Math.PI;
        var asr = Hour(asrAltitude);
        var isha = method.IshaMinutes > 0 ? double.NaN : Hour(-method.Isha);
        if (new[] { rise, fajr, maghrib, asr }.Any(double.IsNaN) ||
            (method.IshaMinutes == 0 && double.IsNaN(isha))) return null;
        TimeOnly Convert(double utc)
        {
            var timestamp = new DateTimeOffset(DateTime.SpecifyKind(day.Date, DateTimeKind.Utc))
                .AddSeconds(Math.Round(utc * 3600));
            return TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, zone).DateTime);
        }
        var evening = noon + maghrib;
        return
        [
            ("اذان صبح", Convert(noon - fajr)),
            ("طلوع آفتاب", Convert(noon - rise)),
            ("اذان ظهر", Convert(noon)),
            ("عصر", Convert(noon + asr)),
            ("غروب", Convert(noon + rise)),
            ("اذان مغرب", Convert(evening)),
            ("عشا", Convert(method.IshaMinutes > 0 ? evening + method.IshaMinutes/60.0 : noon + isha))
        ];
    }
}
