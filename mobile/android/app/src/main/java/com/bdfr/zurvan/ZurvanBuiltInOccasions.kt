package com.bdfr.zurvan

/**
 * Small offline starter catalog for day details.
 * These are fixed Gregorian/Persian dates only; lunar occasions require a yearly verified source.
 */
object ZurvanBuiltInOccasions {
    private data class Record(val calendar: CalendarPriority, val month: Int, val day: Int, val title: String)
    private val entries = listOf(
        Record(CalendarPriority.GREGORIAN, 11, 14, "روز جهانی دیابت"),
        Record(CalendarPriority.GREGORIAN, 11, 10, "روز جهانی علم در خدمت صلح و توسعه"),
        Record(CalendarPriority.GREGORIAN, 3, 8, "روز جهانی زنان"),
        Record(CalendarPriority.GREGORIAN, 6, 5, "روز جهانی محیط زیست"),
        Record(CalendarPriority.GREGORIAN, 12, 10, "روز جهانی حقوق بشر"),
        Record(CalendarPriority.PERSIAN, 1, 1, "عید نوروز · تعطیل رسمی"),
        Record(CalendarPriority.PERSIAN, 1, 2, "عید نوروز · تعطیل رسمی"),
        Record(CalendarPriority.PERSIAN, 1, 3, "عید نوروز · تعطیل رسمی"),
        Record(CalendarPriority.PERSIAN, 1, 4, "عید نوروز · تعطیل رسمی"),
        Record(CalendarPriority.PERSIAN, 1, 12, "روز جمهوری اسلامی ایران · تعطیل رسمی"),
        Record(CalendarPriority.PERSIAN, 1, 13, "روز طبیعت · تعطیل رسمی"),
        Record(CalendarPriority.PERSIAN, 11, 22, "سالگرد پیروزی انقلاب اسلامی · تعطیل رسمی"),
        Record(CalendarPriority.PERSIAN, 12, 29, "روز ملی شدن صنعت نفت · تعطیل رسمی")
    )

    fun onDay(day: CalendarDayCell, custom: List<LocalOccasion>): List<LocalOccasion> {
        val builtIn = entries.filter { entry ->
            when (entry.calendar) {
                CalendarPriority.PERSIAN -> entry.month == day.persianMonth && entry.day == day.persianDay
                CalendarPriority.GREGORIAN -> entry.month == day.gregorianMonth && entry.day == day.gregorianDay
                CalendarPriority.HIJRI -> entry.month == day.hijriMonth && entry.day == day.hijriDay
            }
        }.mapIndexed { index, entry ->
            LocalOccasion("builtin-${entry.calendar.key}-${entry.month}-${entry.day}-$index",
                entry.title, entry.calendar, entry.month, entry.day, 0, 0)
        }
        val userOccasions = custom.filter { occasion ->
            // Recurrence follows its source calendar; avoid nonexistent leap dates.
            val existsThisYear = when (occasion.calendar) {
                CalendarPriority.GREGORIAN -> runCatching {
                    java.time.LocalDate.of(day.gregorianYear, occasion.month, occasion.day)
                }.isSuccess
                CalendarPriority.PERSIAN -> {
                    occasion.month in 1..12 && occasion.day in 1..31 &&
                        (occasion.month != 12 || occasion.day <= 29 ||
                            AndroidCalendarEngine.buildPersianMonth(day.persianYear, 12)
                                .days.any { it.persianMonth == 12 && it.persianDay == 30 })
                }
                CalendarPriority.HIJRI -> occasion.month in 1..12 && occasion.day in 1..30
            }
            if (!occasion.annual || !existsThisYear) return@filter false
            when (occasion.calendar) {
                CalendarPriority.PERSIAN -> occasion.month == day.persianMonth && occasion.day == day.persianDay
                CalendarPriority.GREGORIAN -> occasion.month == day.gregorianMonth && occasion.day == day.gregorianDay
                CalendarPriority.HIJRI -> occasion.month == day.hijriMonth && occasion.day == day.hijriDay
            }
        }
        return (builtIn + userOccasions).distinctBy { it.calendar.key + ":" + it.title }
    }
}
