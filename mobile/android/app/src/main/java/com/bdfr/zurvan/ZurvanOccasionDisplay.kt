package com.bdfr.zurvan

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue

/**
 * Occasion feeds are independent from the three calendar date labels.
 * User preferences never grant restricted features: that is checked on every read.
 */
object ZurvanOccasionDisplay {
    private const val PREFS = "zurvan_occasion_display"
    private const val ENABLED = "enabled"
    private const val PERSIAN = "persian"
    private const val HIJRI = "hijri"
    private const val GREGORIAN = "gregorian"
    private const val PAHLAVI = "pahlavi"
    private const val IMPERIAL_YEAR = "imperial_year"
    private var revision by mutableIntStateOf(0)

    data class Selection(
        val enabled: Boolean = true,
        val persian: Boolean = true,
        val hijri: Boolean = true,
        val gregorian: Boolean = true,
        val pahlavi: Boolean = false,
        val imperialYear: Boolean = false
    ) {
        fun includes(item: LocalOccasion): Boolean {
            if (!enabled) return false
            return when (item.sourceCategory) {
                PAHLAVI -> pahlavi
                HIJRI -> hijri
                GREGORIAN -> gregorian
                else -> persian
            }
        }
        fun includesOfficial(item: OfficialAnnualOccasion): Boolean =
            enabled && if ("religious" in item.categories) hijri else persian
    }

    fun current(context: Context): Selection {
        @Suppress("UNUSED_VARIABLE") val observedRevision = revision
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val authorized = ZurvanAndroidOnlineLicense.hasPahlaviAccess(context)
        return Selection(
            enabled = prefs.getBoolean(ENABLED, true),
            persian = prefs.getBoolean(PERSIAN, true),
            hijri = prefs.getBoolean(HIJRI, true),
            gregorian = prefs.getBoolean(GREGORIAN, true),
            pahlavi = authorized && prefs.getBoolean(PAHLAVI, false),
            imperialYear = authorized && prefs.getBoolean(IMPERIAL_YEAR, false)
        )
    }

    fun set(context: Context, key: String, checked: Boolean) {
        require(key in setOf(ENABLED, PERSIAN, HIJRI, GREGORIAN, PAHLAVI, IMPERIAL_YEAR))
        if ((key == PAHLAVI || key == IMPERIAL_YEAR) &&
            !ZurvanAndroidOnlineLicense.hasPahlaviAccess(context)) return
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putBoolean(key, checked).apply()
        revision++
    }

    fun refresh() { revision++ }
}

/**
 * Curated historical observances, not contemporary public holidays.
 * Dates are interpreted in the solar Hijri month/day system.
 */
internal object ZurvanPahlaviOccasions {
    private data class Entry(val month: Int, val day: Int, val title: String)
    private val entries = listOf(
        Entry(1, 1, "نوروز شاهنشاهی؛ آغاز سال جدید (یادبود تاریخی)"),
        Entry(6, 25, "آغاز سلطنت محمدرضا پهلوی در ۱۳۲۰ (رویداد تاریخی)"),
        Entry(8, 4, "زادروز محمدرضا پهلوی (مناسبت تاریخی)"),
        Entry(8, 4, "سالروز تاج‌گذاری ۱۳۴۶ (مناسبت تاریخی)"),
        Entry(10, 26, "خروج محمدرضا پهلوی از ایران در ۱۳۵۷ (رویداد تاریخی)"),
        Entry(11, 6, "همه‌پرسی انقلاب سفید در ۱۳۴۱ (رویداد تاریخی)"),
        Entry(12, 24, "تصویب تغییر مبدأ سال‌شماری شاهنشاهی در ۱۳۵۴ (رویداد تاریخی)")
    )

    fun onDay(day: CalendarDayCell): List<LocalOccasion> =
        entries.filter { it.month == day.persianMonth && it.day == day.persianDay }
            .mapIndexed { index, item ->
                LocalOccasion(
                    id = "historical-pahlavi-${item.month}-${item.day}-${index}",
                    title = item.title,
                    calendar = CalendarPriority.PERSIAN,
                    month = item.month,
                    day = item.day,
                    hour = 0,
                    minute = 0,
                    sourceCategory = "pahlavi"
                )
            }
}
