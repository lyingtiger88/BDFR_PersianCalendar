using BDFR.PersianCalendar.Core;

var nowruz = new PersianDate(1405, 1, 1);
if (nowruz.ToDateOnly() != new DateOnly(2026, 3, 21))
    throw new Exception("PersianDate conversion failed.");

var parser = new PersianQuickAddParser();
var parsed = parser.Parse("فردا ساعت 16:30 جلسه تیم", new PersianDate(1405, 7, 14));
if (parsed.Date != new PersianDate(1405, 7, 15) || parsed.Time != new TimeOnly(16, 30) || parsed.Title != "جلسه تیم")
    throw new Exception("QuickAdd parser failed.");

var grid = MonthGridBuilder.Build(1405, 7, new PersianDate(1405, 7, 14));
if (grid.Count != 42 || grid.Count(x => x.IsToday) != 1)
    throw new Exception("Month grid failed.");

Console.WriteLine("BDFR Persian Calendar smoke checks passed.");