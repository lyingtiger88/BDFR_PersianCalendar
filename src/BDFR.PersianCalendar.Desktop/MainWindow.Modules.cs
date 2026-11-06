using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BDFR.PersianCalendar.Desktop;

public sealed partial class MainWindow
{
    private void ShowDesktopModule(string module)
    {
        _activeDesktopModule = module;
        if (_desktopCalendarPage is null || _desktopModulePage is null || _desktopSelectedDayPanel is null)
            return;

        var isCalendar = module == "تقویم";
        _desktopCalendarPage.Visibility = isCalendar ? Visibility.Visible : Visibility.Collapsed;
        _desktopSelectedDayPanel.Visibility = isCalendar ? Visibility.Visible : Visibility.Collapsed;
        _desktopModulePage.Visibility = isCalendar ? Visibility.Collapsed : Visibility.Visible;
        _desktopModulePage.Child = BuildDesktopModulePage(module);
        _desktopModulePage.FlowDirection = FlowDirection.RightToLeft;
    }

    private FrameworkElement BuildDesktopModulePage(string module)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FlowDirection = FlowDirection.RightToLeft
        };
        var page = new StackPanel
        {
            Spacing = 16,
            Padding = new Thickness(12, 8, 12, 20),
            MaxWidth = 1100,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FlowDirection = FlowDirection.RightToLeft
        };
        page.Children.Add(new TextBlock
        {
            Text = module,
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ThemeService.Brush(_theme.PrimaryText)
        });
        page.Children.Add(new TextBlock
        {
            Text = module switch
            {
                "برنامه‌ها" => "مدیریت برنامه‌ها و کارهای روزانه در یک فضای مستقل.",
                "مناسبت‌ها" => "مشاهده و ثبت مناسبت‌های رسمی، شخصی و سالانه.",
                "یادآورها" => "مدیریت رویدادها و یادآورهای زمان‌بندی‌شده.",
                "زروان دسک" => "فضای فایل و همگام‌سازی ابری زروان دسک.",
                "همراه" => "مدیریت اتصال و همراهی دستگاه‌ها.",
                "فعالیت‌ها" => "گزارش رویدادها و تغییرات انجام‌شده در برنامه.",
                "لایسنس و فعال‌سازی" => "وضعیت مجوز و اتصال این نصب به حساب لایسنس زروان.",
                "تنظیمات" => "شخصی‌سازی ظاهر، تقویم، فونت، تصاویر و رفتار برنامه.",
                _ => "مدیریت و تنظیمات زروان."
            },
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeService.Brush(_theme.SecondaryText)
        });

        FrameworkElement Card(string title, string description, string actionText, Action action)
        {
            var body = new StackPanel { Spacing = 9, Padding = new Thickness(18) };
            body.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 17,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ThemeService.Brush(_theme.PrimaryText)
            });
            body.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeService.Brush(_theme.SecondaryText)
            });
            var button = MakeButton(actionText);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Click += (_, _) => action();
            body.Children.Add(button);
            return new Border
            {
                Background = CreateSideSurfaceBrush(),
                BorderBrush = ThemeService.Brush(_theme.Accent),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = body,
                MaxWidth = 820,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        switch (module)
        {
            case "برنامه‌ها":
                page.Children.Add(Card("برنامه جدید", "برای تاریخ انتخاب‌شده در تقویم یک برنامه یا کار ثبت کن.", "＋ ثبت برنامه برای روز انتخاب‌شده", () => _ = AddDayTaskFromContextAsync(_selected)));
                page.Children.Add(Card("مشاهده برنامه‌های روز", "برای مشاهده جزئیات و ویرایش برنامه‌ها، روز موردنظر را در تقویم انتخاب کن.", "رفتن به تقویم", () => ShowDesktopModule("تقویم")));
                break;
            case "مناسبت‌ها":
                page.Children.Add(Card("مناسبت شخصی سالانه", "مناسبت‌هایی مانند تولد، سالگرد آشنایی و سالگرد ازدواج را ثبت کن.", "＋ ثبت مناسبت برای روز انتخاب‌شده", () => _ = AddDayOccasionFromContextAsync(_selected)));
                page.Children.Add(Card("مناسبت‌های تقویم", "مناسبت‌های رسمی و مناسبت‌های روز انتخاب‌شده در نمای تقویم نمایش داده می‌شوند.", "مشاهده تقویم", () => ShowDesktopModule("تقویم")));
                break;
            case "یادآورها":
                page.Children.Add(Card("رویداد و یادآور جدید", "رویداد را با تاریخ و ساعت ثبت کن و در صورت نیاز یادآور آن را تنظیم کن.", "＋ ثبت رویداد و یادآور", () => AddEvent_Click(this, new RoutedEventArgs())));
                page.Children.Add(Card("یادآورهای روز انتخاب‌شده", "روز موردنظر را در تقویم انتخاب کن تا رویدادها و یادآورهای همان روز را ببینی.", "رفتن به تقویم", () => ShowDesktopModule("تقویم")));
                break;
            case "فعالیت‌ها":
                page.Children.Add(Card("به‌روزرسانی گزارش فعالیت", "گزارش فعالیت‌های ثبت‌شده در برنامه را تازه‌سازی کن.", "تازه‌سازی فعالیت‌ها", () => _ = LoadActivitiesAsync()));
                page.Children.Add(Card("پاکسازی تاریخچه", "این عملیات فقط تاریخچه فعالیت را پاک می‌کند و برنامه‌ها، یادداشت‌ها و مناسبت‌ها را حذف نمی‌کند.", "پاکسازی تاریخچه فعالیت", () => ClearActivities_Click(this, new RoutedEventArgs())));
                break;
            case "لایسنس و فعال‌سازی":
                page.Children.Add(BuildLicensePanel());
                break;
            case "تنظیمات":
                page.Children.Add(BuildSettingsPanelSafe());
                break;
            case "زروان دسک":
                page.Children.Add(Card("فضای ذخیره‌سازی ابری", "این بخش برای فایل‌ها و همگام‌سازی ابری است و مستقل از فعال‌سازی لایسنس عمل می‌کند.", "باز کردن بخش زروان دسک", () => StatusText.Text = "مدیریت فایل‌های زروان دسک از بخش حساب ابری انجام می‌شود." ));
                break;
            case "همراه":
                page.Children.Add(Card("اتصال دستگاه‌ها", "وضعیت همراهی و همگام‌سازی دستگاه‌های متصل را بررسی کن.", "نمایش وضعیت همراه", () => _ = ShowCompanionOverviewAsync()));
                break;
            default:
                page.Children.Add(Card("تقویم زروان", "برای مشاهده روزها و جزئیات روزانه به صفحه تقویم بازگرد.", "بازگشت به تقویم", () => ShowDesktopModule("تقویم")));
                break;
        }

        scroll.Content = page;
        return scroll;
    }
}
