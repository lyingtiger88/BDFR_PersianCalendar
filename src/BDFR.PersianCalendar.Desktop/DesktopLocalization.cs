using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

internal static class DesktopLocalization
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Text =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new Dictionary<string, string>
            {
                ["زبان ترجیحی ذخیره شد."] = "Language preference saved.", ["تقویم"] = "Calendar", ["برنامه‌ها"] = "Events", ["مناسبت‌ها"] = "Occasions",
                ["پادآورها"] = "Reminders", ["تنظیمات"] = "Settings", ["مدیریت تصویر زمینه"] = "Wallpaper Manager",
                ["ذخیره یادداشت"] = "Save note", ["حذف یادداشت روز"] = "Delete day's note",
                ["زبان و منطقه"] = "Language & region", ["زبان برنامه"] = "Application language",
                ["نمایش و ظاهر"] = "Display & appearance", ["محل قرارگیری سربرگ‌ها"] = "Navigation placement",
                ["جایگاه سربرگ‌ها"] = "Navigation position", ["چپ"] = "Left", ["راست"] = "Right",
                ["امکانات پیشرفته"] = "Advanced features", ["تنظیمات ظاهری پیشرفته"] = "Advanced appearance settings",
                ["گالری محلی"] = "Local gallery", ["اعمال تصویر انتخاب‌شده"] = "Apply selected image",
                ["اعمال روی زمینه تقویم زروان"] = "Apply to Zurvan calendar background",
                ["اعمال روی همه نمایشگرها"] = "Apply to all displays", ["نمایشگر اصلی"] = "Primary display",
                ["ساعت دیجیتال"] = "Digital clock", ["ساعت آنالوگ"] = "Analog clock",
                ["خوارزمی"] = "Khwarazmi", ["پهلوی"] = "Pahlavi", ["میخی"] = "Cuneiform", ["رومی"] = "Roman",
                ["ذخیره"] = "Save", ["حذف"] = "Delete", ["ویرایش"] = "Edit", ["لغو"] = "Cancel",
                ["تأیید"] = "Confirm", ["بستن"] = "Close", ["جستجو"] = "Search", ["افزودن"] = "Add",
                ["نام"] = "Name", ["تاریخ"] = "Date", ["توضیحات"] = "Description"
            },
            ["ckb"] = new Dictionary<string, string>
            {
                ["زبان ترجیحی ذخیره شد."] = "زمانی هەڵبژێردراو پاشەکەوت کرا.", ["تقویم"] = "ڕۆژژمێر", ["برنامه‌ها"] = "بەرنامەکان", ["مناسبت‌ها"] = "بۆنەکان",
                ["پادآورها"] = "بیرخەرەوەکان", ["تنظیمات"] = "ڕێکخستنەکان", ["مدیریت تصویر زمینه"] = "بەڕێوەبردنی وێنەی پاشبنەما",
                ["ذخیره یادداشت"] = "پاشەکەوتکردنی تێبینی", ["حذف یادداشت روز"] = "سڕینەوەی تێبینیی ئەمڕۆ",
                ["زبان و منطقه"] = "زمان و ناوچە", ["زبان برنامه"] = "زمانی بەرنامە",
                ["نمایش و ظاهر"] = "پیشاندان و ڕووکار", ["محل قرارگیری سربرگ‌ها"] = "شوێنی دانانی سەرپەڕەکان",
                ["جایگاه سربرگ‌ها"] = "شوێنی سەرپەڕەکان", ["چپ"] = "چەپ", ["راست"] = "ڕاست",
                ["گالری محلی"] = "گەلەریی ناوخۆیی", ["ذخیره"] = "پاشەکەوتکردن", ["حذف"] = "سڕینەوە",
                ["ویرایش"] = "دەستکاریکردن", ["لغو"] = "پاشگەزبوونەوە", ["تأیید"] = "پەسەندکردن",
                ["بستن"] = "داخستن", ["جستجو"] = "گەڕان", ["افزودن"] = "زیادکردن",
                ["نام"] = "ناو", ["تاریخ"] = "بەروار", ["توضیحات"] = "ڕوونکردنەوە"
            },
            ["bal"] = new Dictionary<string, string>
            {
                ["زبان ترجیحی ذخیره شد."] = "زبان ءِ ترجیح محفوظ بوت.", ["تقویم"] = "تقویم", ["برنامه‌ها"] = "پلانان", ["مناسبت‌ها"] = "مناسبتاں",
                ["پادآورها"] = "یاد دپادکناں", ["تنظیمات"] = "ترتیب", ["مدیریت تصویر زمینه"] = "پس‌منظر ءِ منیجمنٹ",
                ["ذخیره یادداشت"] = "نوشتگ ءَ محفوظ کن", ["حذف یادداشت روز"] = "روچ ءِ نوشتگ رد کن",
                ["زبان و منطقه"] = "زبان ءُ منطقه", ["زبان برنامه"] = "سافٹویر ءِ زبان",
                ["نمایش و ظاهر"] = "پیشداری ءُ شکل", ["محل قرارگیری سربرگ‌ها"] = "سربرگاں ءِ جا",
                ["جایگاه سربرگ‌ها"] = "سربرگاں ءِ مقام", ["چپ"] = "چپ", ["راست"] = "راست",
                ["گالری محلی"] = "مقامی گیلری", ["ذخیره"] = "محفوظ کن", ["حذف"] = "رد کن",
                ["ویرایش"] = "درست کن", ["لغو"] = "لغو", ["تأیید"] = "تایید",
                ["بستن"] = "بند کن", ["جستجو"] = "گُست", ["افزودن"] = "زیاد کن",
                ["نام"] = "نام", ["تاریخ"] = "تاریخ", ["توضیحات"] = "تشریح"
            }
        };

    public static string Translate(string value, string language)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // Normalize a previously translated value back to its Persian source before
        // applying another language, so changing languages repeatedly remains reversible.
        var source = value;
        foreach (var dictionary in Text.Values)
        {
            var match = dictionary.FirstOrDefault(pair => string.Equals(pair.Value, value, StringComparison.Ordinal));
            if (!string.IsNullOrEmpty(match.Key))
            {
                source = match.Key;
                break;
            }
        }

        if (language.Equals("fa", StringComparison.OrdinalIgnoreCase))
            return source;

        return Text.TryGetValue(language, out var target) && target.TryGetValue(source, out var translated)
            ? translated : source;
    }

    public static void Apply(FrameworkElement? root, string language)
    {
        if (root is null || language.Equals("fa", StringComparison.OrdinalIgnoreCase)) return;
        Visit(root, language);
    }

    private static void Visit(DependencyObject node, string language)
    {
        switch (node)
        {
            case TextBlock text:
                text.Text = Translate(text.Text, language);
                break;
            case ContentControl content when content.Content is string value:
                content.Content = Translate(value, language);
                break;
            case Control control when control is not ContentControl:
                if (control is Button button && button.Content is string buttonText)
                    button.Content = Translate(buttonText, language);
                if (control is HeaderedContentControl headered && headered.Header is string header)
                    headered.Header = Translate(header, language);
                break;
            case MenuFlyoutItem menu:
                menu.Text = Translate(menu.Text, language);
                break;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
            Visit(VisualTreeHelper.GetChild(node, i), language);
    }
}

public sealed partial class MainWindow
{
    private void ApplyDesktopLocalization()
    {
        DesktopLocalization.Apply(Content as FrameworkElement, _settings.LanguageCode);
    }
}
