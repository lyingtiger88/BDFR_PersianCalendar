using BDFR.PersianCalendar.Core;
using BDFR.PersianCalendar.Infrastructure;

var nowruz = new PersianDate(1405, 1, 1);
if (nowruz.ToDateOnly() != new DateOnly(2026, 3, 21))
    throw new Exception("PersianDate conversion failed.");

var parser = new PersianQuickAddParser();
var parsed = parser.Parse("فردا ساعت 16:30 جلسه تیم", new PersianDate(1405, 7, 14));
if (parsed.Date != new PersianDate(1405, 7, 15) ||
    parsed.Time != new TimeOnly(16, 30) ||
    parsed.Title != "جلسه تیم")
{
    throw new Exception(
        $"QuickAdd parser failed. Date={parsed.Date}, Time={parsed.Time}, Title=[{parsed.Title}]");
}

var grid = MonthGridBuilder.Build(1405, 7, new PersianDate(1405, 7, 14));
if (grid.Count != 42 || grid.Count(x => x.IsToday) != 1)
    throw new Exception("Month grid failed.");

var timeIrFixture = """
<!doctype html>
<html lang="fa">
<body>
  <div id="Month_0">
    <div class="month-title">فروردین ۱۴۰۵</div>
    <div class="event-list">
      <div>
        <div>
          <div class="event holiday"><span>۱ فروردین نوروز</span></div>
          <div class="event"><span>۱۲ فروردین روز جمهوری اسلامی ایران</span></div>
        </div>
      </div>
    </div>
  </div>
</body>
</html>
""";

var occasions = await TimeIrOccasionSource.ParseAnnualHtmlAsync(timeIrFixture, 1405);
var newYear = occasions.SingleOrDefault(x => x.Date == new PersianDate(1405, 1, 1));
if (newYear is null || !newYear.IsHoliday || newYear.Title != "نوروز")
    throw new Exception("time.ir parser fixture failed for holiday event.");

var republicDay = occasions.SingleOrDefault(x => x.Date == new PersianDate(1405, 1, 12));
if (republicDay is null || republicDay.IsHoliday)
    throw new Exception("time.ir parser fixture failed for regular event.");

Console.WriteLine("BDFR Persian Calendar smoke checks passed.");