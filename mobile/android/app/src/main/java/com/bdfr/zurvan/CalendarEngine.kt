package com.bdfr.zurvan

import android.icu.util.Calendar
import android.icu.util.ULocale
import java.util.Date
import java.time.Instant

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

data class AndroidDateConversion(
    val persianYear: Int,
    val persianMonth: Int,
    val persianDay: Int,
    val gregorianYear: Int,
    val gregorianMonth: Int,
    val gregorianDay: Int,
    val hijriYear: Int,
    val hijriMonth: Int,
    val hijriDay: Int
)

data class PersianMonthModel(
    val year: Int,
    val month: Int,
    val monthName: String,
    val leadingBlankCount: Int,
    val days: List<CalendarDayCell>
)

object AndroidCalendarEngine {
    @Volatile var hijriDayOffset: Int = 0
        set(value) { field = value.coerceIn(-1, 1) }

    private val persianMonthNames = listOf(
        "فروردین", "اردیبهشت", "خرداد",
        "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر",
        "دی", "بهمن", "اسفند"
    )

    fun currentPersianYearMonth(): Pair<Int, Int> {
        val calendar = newPersianCalendar()
        return calendar.get(Calendar.YEAR) to
            (calendar.get(Calendar.MONTH) + 1)
    }

    fun buildPersianMonth(
        year: Int,
        month: Int
    ): PersianMonthModel {
        require(month in 1..12)

        val first = newPersianCalendar().apply {
            clear()
            set(Calendar.YEAR, year)
            set(Calendar.MONTH, month - 1)
            set(Calendar.DAY_OF_MONTH, 1)
            set(Calendar.HOUR_OF_DAY, 12)
            set(Calendar.MINUTE, 0)
            set(Calendar.SECOND, 0)
        }

        val maxDay =
            first.getActualMaximum(
                Calendar.DAY_OF_MONTH
            )

        val leading =
            weekColumn(
                first.get(
                    Calendar.DAY_OF_WEEK
                )
            )

        val today = newPersianCalendar()
        val todayYear =
            today.get(Calendar.YEAR)
        val todayMonth =
            today.get(Calendar.MONTH) + 1
        val todayDay =
            today.get(
                Calendar.DAY_OF_MONTH
            )

        val days = (1..maxDay).map { day ->
            val persian =
                newPersianCalendar().apply {
                    clear()
                    set(
                        Calendar.YEAR,
                        year
                    )
                    set(
                        Calendar.MONTH,
                        month - 1
                    )
                    set(
                        Calendar.DAY_OF_MONTH,
                        day
                    )
                    set(
                        Calendar.HOUR_OF_DAY,
                        12
                    )
                    set(
                        Calendar.MINUTE,
                        0
                    )
                    set(
                        Calendar.SECOND,
                        0
                    )
                }

            val millis =
                persian.timeInMillis

            val gregorian =
                Calendar.getInstance(
                    ULocale(
                        "en_US@calendar=gregorian"
                    )
                ).apply {
                    time = Date(millis)
                }

            val hijri =
                Calendar.getInstance(
                    ULocale(
                        "ar_SA@calendar=islamic-civil"
                    )
                ).apply {
                    // Correction affects Hijri labels only, never Gregorian/Persian dates.
                    time = Date(millis + hijriDayOffset * 86_400_000L)
                }

            val dow =
                persian.get(
                    Calendar.DAY_OF_WEEK
                )

            CalendarDayCell(
                persianYear = year,
                persianMonth = month,
                persianDay = day,
                gregorianYear =
                    gregorian.get(
                        Calendar.YEAR
                    ),
                gregorianMonth =
                    gregorian.get(
                        Calendar.MONTH
                    ) + 1,
                gregorianDay =
                    gregorian.get(
                        Calendar.DAY_OF_MONTH
                    ),
                hijriYear =
                    hijri.get(
                        Calendar.YEAR
                    ),
                hijriMonth =
                    hijri.get(
                        Calendar.MONTH
                    ) + 1,
                hijriDay =
                    hijri.get(
                        Calendar.DAY_OF_MONTH
                    ),
                weekColumn =
                    weekColumn(dow),
                isFriday =
                    dow ==
                        Calendar.FRIDAY,
                isToday =
                    year == todayYear &&
                        month ==
                            todayMonth &&
                        day ==
                            todayDay
            )
        }

        return PersianMonthModel(
            year = year,
            month = month,
            monthName =
                persianMonthNames[
                    month - 1
                ],
            leadingBlankCount =
                leading,
            days = days
        )
    }

    fun convert(
        calendar: CalendarPriority,
        year: Int,
        month: Int,
        day: Int
    ): AndroidDateConversion {
        require(month in 1..12)
        require(day in 1..31)

        val source = when (calendar) {
            CalendarPriority.PERSIAN ->
                Calendar.getInstance(
                    ULocale(
                        "fa_IR@calendar=persian"
                    )
                )

            CalendarPriority.HIJRI ->
                Calendar.getInstance(
                    ULocale(
                        "ar_SA@calendar=islamic-civil"
                    )
                )

            CalendarPriority.GREGORIAN ->
                Calendar.getInstance(
                    ULocale(
                        "en_US@calendar=gregorian"
                    )
                )
        }.apply {
            clear()
            set(Calendar.YEAR, year)
            set(Calendar.MONTH, month - 1)
            set(Calendar.DAY_OF_MONTH, day)
            set(Calendar.HOUR_OF_DAY, 12)
            set(Calendar.MINUTE, 0)
            set(Calendar.SECOND, 0)
        }

        val millis = source.timeInMillis

        val persian = Calendar.getInstance(
            ULocale(
                "fa_IR@calendar=persian"
            )
        ).apply {
            time = Date(millis)
        }

        val gregorian = Calendar.getInstance(
            ULocale(
                "en_US@calendar=gregorian"
            )
        ).apply {
            time = Date(millis)
        }

        val hijri = Calendar.getInstance(
            ULocale(
                "ar_SA@calendar=islamic-civil"
            )
        ).apply {
            time = Date(millis)
        }

        return AndroidDateConversion(
            persianYear = persian.get(Calendar.YEAR),
            persianMonth =
                persian.get(Calendar.MONTH) + 1,
            persianDay =
                persian.get(Calendar.DAY_OF_MONTH),
            gregorianYear =
                gregorian.get(Calendar.YEAR),
            gregorianMonth =
                gregorian.get(Calendar.MONTH) + 1,
            gregorianDay =
                gregorian.get(Calendar.DAY_OF_MONTH),
            hijriYear = hijri.get(Calendar.YEAR),
            hijriMonth =
                hijri.get(Calendar.MONTH) + 1,
            hijriDay =
                hijri.get(Calendar.DAY_OF_MONTH)
        )
    }

    fun shiftMonth(
        year: Int,
        month: Int,
        delta: Int
    ): Pair<Int, Int> {
        val zeroBased =
            (year * 12 +
                (month - 1)) +
                delta

        val newYear =
            Math.floorDiv(
                zeroBased,
                12
            )

        val newMonth =
            Math.floorMod(
                zeroBased,
                12
            ) + 1

        return newYear to newMonth
    }

    fun persianDateTimeToMillis(
        dateKey: String,
        hour: Int,
        minute: Int
    ): Long? {
        val parts = dateKey
            .split("-")
            .mapNotNull {
                it.toIntOrNull()
            }

        if (parts.size != 3)
            return null

        return newPersianCalendar()
            .apply {
                clear()
                set(
                    Calendar.YEAR,
                    parts[0]
                )
                set(
                    Calendar.MONTH,
                    parts[1] - 1
                )
                set(
                    Calendar.DAY_OF_MONTH,
                    parts[2]
                )
                set(
                    Calendar.HOUR_OF_DAY,
                    hour.coerceIn(0, 23)
                )
                set(
                    Calendar.MINUTE,
                    minute.coerceIn(0, 59)
                )
                set(
                    Calendar.SECOND,
                    0
                )
            }
            .timeInMillis
    }

    fun persianDateTimeToInstant(
        dateKey: String,
        hour: Int,
        minute: Int
    ): Instant? =
        persianDateTimeToMillis(
            dateKey.replace("/", "-"),
            hour,
            minute
        )?.let {
            Instant.ofEpochMilli(it)
        }

    fun instantToPersianDateTime(
        instantText: String
    ): Triple<String, Int, Int>? {
        val instant = try {
            Instant.parse(instantText)
        } catch (_: Exception) {
            return null
        }

        val calendar =
            newPersianCalendar().apply {
                timeInMillis =
                    instant.toEpochMilli()
            }

        val key = "%04d-%02d-%02d".format(
            calendar.get(Calendar.YEAR),
            calendar.get(Calendar.MONTH) + 1,
            calendar.get(Calendar.DAY_OF_MONTH)
        )

        return Triple(
            key,
            calendar.get(Calendar.HOUR_OF_DAY),
            calendar.get(Calendar.MINUTE)
        )
    }

    private fun newPersianCalendar():
        Calendar =
        Calendar.getInstance(
            ULocale(
                "fa_IR@calendar=persian"
            )
        )

    private fun weekColumn(
        dayOfWeek: Int
    ): Int = when (dayOfWeek) {
        Calendar.SATURDAY -> 0
        Calendar.SUNDAY -> 1
        Calendar.MONDAY -> 2
        Calendar.TUESDAY -> 3
        Calendar.WEDNESDAY -> 4
        Calendar.THURSDAY -> 5
        else -> 6
    }
}
