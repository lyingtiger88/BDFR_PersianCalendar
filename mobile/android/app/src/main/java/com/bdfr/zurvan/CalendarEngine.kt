package com.bdfr.zurvan

import android.icu.util.Calendar
import android.icu.util.GregorianCalendar
import android.icu.util.IslamicCalendar
import android.icu.util.PersianCalendar
import java.util.Date

data class CalendarDayCell(
    val persianYear: Int,
    val persianMonth: Int,
    val persianDay: Int,
    val gregorianYear: Int,
    val gregorianMonth: Int,
    val gregorianDay: Int,
    val hijriYear: Int,
    val hijriMonth: Int,
    val hijriDay: Int,
    val weekColumn: Int,
    val isFriday: Boolean,
    val isToday: Boolean
) {
    val persianKey: String
        get() = "%04d-%02d-%02d".format(
            persianYear,
            persianMonth,
            persianDay
        )
}

data class PersianMonthModel(
    val year: Int,
    val month: Int,
    val monthName: String,
    val leadingBlankCount: Int,
    val days: List<CalendarDayCell>
)

object AndroidCalendarEngine {
    private val persianMonthNames = listOf(
        "فروردین", "اردیبهشت", "خرداد",
        "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر",
        "دی", "بهمن", "اسفند"
    )

    fun currentPersianYearMonth(): Pair<Int, Int> {
        val calendar = PersianCalendar()
        return calendar.get(Calendar.YEAR) to
            (calendar.get(Calendar.MONTH) + 1)
    }

    fun buildPersianMonth(
        year: Int,
        month: Int
    ): PersianMonthModel {
        require(month in 1..12)

        val first = PersianCalendar().apply {
            clear()
            set(year, month - 1, 1, 12, 0, 0)
        }

        val maxDay = first.getActualMaximum(Calendar.DAY_OF_MONTH)
        val leading = weekColumn(first.get(Calendar.DAY_OF_WEEK))

        val today = PersianCalendar()
        val todayYear = today.get(Calendar.YEAR)
        val todayMonth = today.get(Calendar.MONTH) + 1
        val todayDay = today.get(Calendar.DAY_OF_MONTH)

        val days = (1..maxDay).map { day ->
            val persian = PersianCalendar().apply {
                clear()
                set(year, month - 1, day, 12, 0, 0)
            }

            val millis = persian.timeInMillis

            val gregorian = GregorianCalendar().apply {
                time = Date(millis)
            }

            val hijri = IslamicCalendar().apply {
                calculationType =
                    IslamicCalendar.CalculationType.ISLAMIC_CIVIL
                time = Date(millis)
            }

            val dow = persian.get(Calendar.DAY_OF_WEEK)

            CalendarDayCell(
                persianYear = year,
                persianMonth = month,
                persianDay = day,
                gregorianYear = gregorian.get(Calendar.YEAR),
                gregorianMonth = gregorian.get(Calendar.MONTH) + 1,
                gregorianDay = gregorian.get(Calendar.DAY_OF_MONTH),
                hijriYear = hijri.get(Calendar.YEAR),
                hijriMonth = hijri.get(Calendar.MONTH) + 1,
                hijriDay = hijri.get(Calendar.DAY_OF_MONTH),
                weekColumn = weekColumn(dow),
                isFriday = dow == Calendar.FRIDAY,
                isToday =
                    year == todayYear &&
                    month == todayMonth &&
                    day == todayDay
            )
        }

        return PersianMonthModel(
            year = year,
            month = month,
            monthName = persianMonthNames[month - 1],
            leadingBlankCount = leading,
            days = days
        )
    }

    fun shiftMonth(
        year: Int,
        month: Int,
        delta: Int
    ): Pair<Int, Int> {
        val zeroBased = (year * 12 + (month - 1)) + delta
        val newYear = Math.floorDiv(zeroBased, 12)
        val newMonth = Math.floorMod(zeroBased, 12) + 1
        return newYear to newMonth
    }

    private fun weekColumn(dayOfWeek: Int): Int =
        when (dayOfWeek) {
            Calendar.SATURDAY -> 0
            Calendar.SUNDAY -> 1
            Calendar.MONDAY -> 2
            Calendar.TUESDAY -> 3
            Calendar.WEDNESDAY -> 4
            Calendar.THURSDAY -> 5
            else -> 6
        }
}
