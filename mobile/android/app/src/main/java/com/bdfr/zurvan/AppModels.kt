package com.bdfr.zurvan

enum class CalendarPriority(val key: String, val label: String) {
    PERSIAN("persian", "شمسی"),
    HIJRI("hijri", "قمری"),
    GREGORIAN("gregorian", "میلادی")
}

data class LocalPlan(
    val id: String,
    val title: String,
    val dateKey: String,
    val hour: Int,
    val minute: Int,
    val kind: String,
    val isPrivate: Boolean,
    val completed: Boolean = false,
    val icon: String = "📌",
    val deleteAfterDays: Int? = null
)

data class LocalOccasion(
    val id: String,
    val title: String,
    val calendar: CalendarPriority,
    val month: Int,
    val day: Int,
    val hour: Int,
    val minute: Int,
    val annual: Boolean = true
)

data class LocalDayNote(
    val dateKey: String,
    val text: String,
    val isPrivate: Boolean
)

data class LocalReminder(
    val id: String,
    val title: String,
    val dateKey: String,
    val hour: Int,
    val minute: Int,
    val isPrivate: Boolean,
    val repeatCount: Int,
    val repeatIntervalMinutes: Int,
    val acknowledged: Boolean = false
)

data class AndroidUiPreferences(
    val calendarOrder: List<CalendarPriority> = listOf(
        CalendarPriority.PERSIAN,
        CalendarPriority.HIJRI,
        CalendarPriority.GREGORIAN
    ),
    val cornerRadius: Float = 18f,
    val railSide: String = "left",
    val hijriDayOffset: Int = 0
)
