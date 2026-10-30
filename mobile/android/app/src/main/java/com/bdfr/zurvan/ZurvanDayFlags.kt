package com.bdfr.zurvan

/**
 * Holiday and mourning markers must come from verified occasion records.
 * The islamic-civil calendar is astronomical/tabular and can differ from official
 * Iranian lunar dates; never mark a day as a public holiday solely from that date.
 */
object ZurvanDayFlags {
    fun isHoliday(day: CalendarDayCell, occasions: List<LocalOccasion>): Boolean {
        val p = day.persianMonth to day.persianDay
        val fixedPersian = (p.first == 1 && p.second in 1..4) ||
            (p.first == 1 && p.second in listOf(12, 13)) ||
            (p.first == 3 && p.second in listOf(14, 15)) ||
            p == (11 to 22) || p == (12 to 29)
        return day.isFriday || fixedPersian ||
            occasions.any { matches(day, it) && it.title.contains("تعطیل رسمی") }
    }

    fun isMourning(day: CalendarDayCell, occasions: List<LocalOccasion>): Boolean =
        occasions.any { occasion ->
            matches(day, occasion) &&
                listOf("عزا", "شهادت", "رحلت", "وفات", "سوگواری", "اربعین", "عاشورا", "تاسوعا")
                    .any { word -> occasion.title.contains(word) }
        }

    private fun matches(day: CalendarDayCell, occasion: LocalOccasion): Boolean {
        if (occasion.targetPersianYear != null && occasion.targetPersianYear != day.persianYear) return false
        return when (occasion.calendar) {
            CalendarPriority.PERSIAN -> occasion.month == day.persianMonth && occasion.day == day.persianDay
            CalendarPriority.HIJRI -> occasion.month == day.hijriMonth && occasion.day == day.hijriDay
            CalendarPriority.GREGORIAN -> occasion.month == day.gregorianMonth && occasion.day == day.gregorianDay
        }
    }
}
