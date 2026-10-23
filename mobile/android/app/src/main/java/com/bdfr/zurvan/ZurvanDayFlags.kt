package com.bdfr.zurvan

/** Matches the fixed-date rules in OfficialHolidayClassifier.cs.
 * Observed lunar holidays can move by a day; date source must be verified before a release.
 */
object ZurvanDayFlags {
    fun isHoliday(day: CalendarDayCell, occasions: List<LocalOccasion>): Boolean {
        val p = day.persianMonth to day.persianDay
        val h = day.hijriMonth to day.hijriDay
        val fixedPersian = (p.first == 1 && p.second in 1..4) ||
            (p.first == 1 && p.second in listOf(12, 13)) ||
            (p.first == 3 && p.second in listOf(14, 15)) ||
            p == (11 to 22) || p == (12 to 29)
        val fixedHijri = when (h.first) {
            1 -> h.second in listOf(9, 10)
            2 -> h.second in listOf(20, 28)
            3 -> h.second in listOf(8, 17)
            6 -> h.second == 3
            7 -> h.second in listOf(13, 27)
            8 -> h.second == 15
            9 -> h.second == 21
            10 -> h.second in listOf(1, 2, 25)
            11 -> h.second == 11
            12 -> h.second in listOf(7, 10, 18)
            else -> false
        }
        return day.isFriday || fixedPersian || fixedHijri ||
            occasions.any { matches(day, it) && it.title.contains("تعطیل") }
    }

    fun isMourning(day: CalendarDayCell, occasions: List<LocalOccasion>): Boolean {
        val h = day.hijriMonth to day.hijriDay
        val fixed = h in setOf(1 to 9, 1 to 10, 2 to 20, 2 to 28, 3 to 8,
            6 to 3, 9 to 21, 10 to 25, 12 to 7)
        return fixed || occasions.any {
            matches(day, it) && listOf("عزا", "شهادت", "رحلت", "وفات", "سوگواری", "اربعین", "عاشورا", "تاسوعا")
                .any { word -> it.title.contains(word) }
        }
    }

    private fun matches(day: CalendarDayCell, occasion: LocalOccasion): Boolean =
        when (occasion.calendar) {
            CalendarPriority.PERSIAN -> occasion.month == day.persianMonth && occasion.day == day.persianDay
            CalendarPriority.HIJRI -> occasion.month == day.hijriMonth && occasion.day == day.hijriDay
            CalendarPriority.GREGORIAN -> occasion.month == day.gregorianMonth && occasion.day == day.gregorianDay
        }
}
