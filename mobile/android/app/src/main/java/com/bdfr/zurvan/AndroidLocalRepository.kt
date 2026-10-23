package com.bdfr.zurvan

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class AndroidLocalRepository(
    context: Context
) {
    private val preferences = context.getSharedPreferences(
        "zurvan_android_local",
        Context.MODE_PRIVATE
    )

    private val _plans = MutableStateFlow(loadPlans())
    val plans: StateFlow<List<LocalPlan>> = _plans.asStateFlow()

    private val _occasions = MutableStateFlow(loadOccasions())
    val occasions: StateFlow<List<LocalOccasion>> =
        _occasions.asStateFlow()

    private val _reminders = MutableStateFlow(loadReminders())
    val reminders: StateFlow<List<LocalReminder>> =
        _reminders.asStateFlow()

    private val _notes = MutableStateFlow(loadNotes())
    val notes: StateFlow<List<LocalDayNote>> =
        _notes.asStateFlow()

    private val _uiPreferences = MutableStateFlow(loadUiPreferences())
    val uiPreferences: StateFlow<AndroidUiPreferences> =
        _uiPreferences.asStateFlow()

    fun addPlan(
        title: String,
        dateKey: String,
        hour: Int,
        minute: Int,
        kind: String,
        isPrivate: Boolean
    ) {
        val next = _plans.value + LocalPlan(
            id = "plan_${UUID.randomUUID()}",
            title = title.trim(),
            dateKey = dateKey,
            hour = hour.coerceIn(0, 23),
            minute = minute.coerceIn(0, 59),
            kind = kind,
            isPrivate = isPrivate
        )
        savePlans(next)
    }

    fun togglePlan(id: String) {
        savePlans(
            _plans.value.map {
                if (it.id == id) {
                    it.copy(completed = !it.completed)
                } else {
                    it
                }
            }
        )
    }

    fun deletePlan(id: String) {
        savePlans(_plans.value.filterNot { it.id == id })
    }

    fun addOccasion(
        title: String,
        calendar: CalendarPriority,
        month: Int,
        day: Int,
        hour: Int,
        minute: Int
    ) {
        val next = _occasions.value + LocalOccasion(
            id = "occasion_${UUID.randomUUID()}",
            title = title.trim(),
            calendar = calendar,
            month = month.coerceIn(1, 12),
            day = day.coerceIn(1, 31),
            hour = hour.coerceIn(0, 23),
            minute = minute.coerceIn(0, 59)
        )
        saveOccasions(next)
    }

    fun deleteOccasion(id: String) {
        saveOccasions(
            _occasions.value.filterNot { it.id == id }
        )
    }

    fun saveNote(
        dateKey: String,
        text: String,
        isPrivate: Boolean
    ) {
        val next = _notes.value
            .filterNot { it.dateKey == dateKey }
            .toMutableList()

        if (text.isNotBlank()) {
            next.add(
                LocalDayNote(
                    dateKey = dateKey,
                    text = text.trim(),
                    isPrivate = isPrivate
                )
            )
        }

        saveNotes(next)
    }

    fun addReminder(
        title: String,
        dateKey: String,
        hour: Int,
        minute: Int,
        isPrivate: Boolean,
        repeatCount: Int,
        repeatIntervalMinutes: Int
    ) {
        val next = _reminders.value + LocalReminder(
            id = "reminder_${UUID.randomUUID()}",
            title = title.trim(),
            dateKey = dateKey,
            hour = hour.coerceIn(0, 23),
            minute = minute.coerceIn(0, 59),
            isPrivate = isPrivate,
            repeatCount = repeatCount.coerceIn(1, 20),
            repeatIntervalMinutes =
                repeatIntervalMinutes.coerceIn(1, 1440)
        )
        saveReminders(next)
    }

    fun acknowledgeReminder(id: String) {
        saveReminders(
            _reminders.value.map {
                if (it.id == id) {
                    it.copy(acknowledged = true)
                } else {
                    it
                }
            }
        )
    }

    fun deleteReminder(id: String) {
        saveReminders(
            _reminders.value.filterNot { it.id == id }
        )
    }

    fun setCalendarOrder(
        order: List<CalendarPriority>
    ) {
        val normalized = buildList {
            order.forEach { value ->
                if (!contains(value)) add(value)
            }
            CalendarPriority.entries.forEach { value ->
                if (!contains(value)) add(value)
            }
        }.take(3)

        saveUiPreferences(
            _uiPreferences.value.copy(
                calendarOrder = normalized
            )
        )
    }

    fun setCornerRadius(value: Float) {
        saveUiPreferences(
            _uiPreferences.value.copy(
                cornerRadius = value.coerceIn(0f, 32f)
            )
        )
    }

    fun setRailSide(value: String) {
        saveUiPreferences(
            _uiPreferences.value.copy(
                railSide = if (
                    value.equals("right", ignoreCase = true)
                ) "right" else "left"
            )
        )
    }

    private fun savePlans(value: List<LocalPlan>) {
        _plans.value = value
        val json = JSONArray()
        value.forEach { item ->
            json.put(
                JSONObject()
                    .put("id", item.id)
                    .put("title", item.title)
                    .put("dateKey", item.dateKey)
                    .put("hour", item.hour)
                    .put("minute", item.minute)
                    .put("kind", item.kind)
                    .put("private", item.isPrivate)
                    .put("completed", item.completed)
            )
        }
        preferences.edit()
            .putString(KEY_PLANS, json.toString())
            .apply()
    }

    private fun loadPlans(): List<LocalPlan> =
        parseArray(KEY_PLANS) { item ->
            LocalPlan(
                id = item.getString("id"),
                title = item.getString("title"),
                dateKey = item.getString("dateKey"),
                hour = item.optInt("hour", 9),
                minute = item.optInt("minute", 0),
                kind = item.optString("kind", "event"),
                isPrivate = item.optBoolean("private", false),
                completed = item.optBoolean(
                    "completed",
                    false
                )
            )
        }

    private fun saveOccasions(
        value: List<LocalOccasion>
    ) {
        _occasions.value = value
        val json = JSONArray()
        value.forEach { item ->
            json.put(
                JSONObject()
                    .put("id", item.id)
                    .put("title", item.title)
                    .put("calendar", item.calendar.key)
                    .put("month", item.month)
                    .put("day", item.day)
                    .put("hour", item.hour)
                    .put("minute", item.minute)
            )
        }
        preferences.edit()
            .putString(KEY_OCCASIONS, json.toString())
            .apply()
    }

    private fun loadOccasions(): List<LocalOccasion> =
        parseArray(KEY_OCCASIONS) { item ->
            LocalOccasion(
                id = item.getString("id"),
                title = item.getString("title"),
                calendar = CalendarPriority.entries
                    .firstOrNull {
                        it.key == item.optString(
                            "calendar",
                            "persian"
                        )
                    }
                    ?: CalendarPriority.PERSIAN,
                month = item.optInt("month", 1),
                day = item.optInt("day", 1),
                hour = item.optInt("hour", 9),
                minute = item.optInt("minute", 0)
            )
        }

    private fun saveNotes(
        value: List<LocalDayNote>
    ) {
        _notes.value = value
        val json = JSONArray()
        value.forEach { item ->
            json.put(
                JSONObject()
                    .put("dateKey", item.dateKey)
                    .put("text", item.text)
                    .put("private", item.isPrivate)
            )
        }

        preferences.edit()
            .putString(KEY_NOTES, json.toString())
            .apply()
    }

    private fun loadNotes(): List<LocalDayNote> =
        parseArray(KEY_NOTES) { item ->
            LocalDayNote(
                dateKey = item.getString("dateKey"),
                text = item.optString("text", ""),
                isPrivate = item.optBoolean("private", false)
            )
        }

    private fun saveReminders(
        value: List<LocalReminder>
    ) {
        _reminders.value = value
        val json = JSONArray()
        value.forEach { item ->
            json.put(
                JSONObject()
                    .put("id", item.id)
                    .put("title", item.title)
                    .put("dateKey", item.dateKey)
                    .put("hour", item.hour)
                    .put("minute", item.minute)
                    .put("private", item.isPrivate)
                    .put("repeatCount", item.repeatCount)
                    .put(
                        "repeatInterval",
                        item.repeatIntervalMinutes
                    )
                    .put(
                        "acknowledged",
                        item.acknowledged
                    )
            )
        }
        preferences.edit()
            .putString(KEY_REMINDERS, json.toString())
            .apply()
    }

    private fun loadReminders(): List<LocalReminder> =
        parseArray(KEY_REMINDERS) { item ->
            LocalReminder(
                id = item.getString("id"),
                title = item.getString("title"),
                dateKey = item.getString("dateKey"),
                hour = item.optInt("hour", 9),
                minute = item.optInt("minute", 0),
                isPrivate = item.optBoolean("private", false),
                repeatCount = item.optInt("repeatCount", 3),
                repeatIntervalMinutes =
                    item.optInt("repeatInterval", 5),
                acknowledged = item.optBoolean(
                    "acknowledged",
                    false
                )
            )
        }

    private fun saveUiPreferences(
        value: AndroidUiPreferences
    ) {
        _uiPreferences.value = value

        preferences.edit()
            .putString(
                KEY_CALENDAR_ORDER,
                value.calendarOrder.joinToString(",") {
                    it.key
                }
            )
            .putFloat(
                KEY_CORNER_RADIUS,
                value.cornerRadius
            )
            .putString(
                KEY_RAIL_SIDE,
                value.railSide
            )
            .apply()
    }

    private fun loadUiPreferences(): AndroidUiPreferences {
        val order = preferences
            .getString(
                KEY_CALENDAR_ORDER,
                null
            )
            ?.split(",")
            ?.mapNotNull { key ->
                CalendarPriority.entries.firstOrNull {
                    it.key == key
                }
            }
            .orEmpty()

        return AndroidUiPreferences(
            calendarOrder = if (order.size == 3) {
                order
            } else {
                AndroidUiPreferences().calendarOrder
            },
            cornerRadius = preferences.getFloat(
                KEY_CORNER_RADIUS,
                18f
            ),
            railSide = preferences.getString(
                KEY_RAIL_SIDE,
                "left"
            ) ?: "left"
        )
    }

    private fun <T> parseArray(
        key: String,
        parser: (JSONObject) -> T
    ): List<T> {
        val raw = preferences.getString(key, null)
            ?: return emptyList()

        return try {
            val array = JSONArray(raw)
            buildList {
                for (index in 0 until array.length()) {
                    add(parser(array.getJSONObject(index)))
                }
            }
        } catch (_: Exception) {
            emptyList()
        }
    }

    companion object {
        private const val KEY_PLANS = "plans"
        private const val KEY_OCCASIONS = "occasions"
        private const val KEY_REMINDERS = "reminders"
        private const val KEY_NOTES = "notes"
        private const val KEY_CALENDAR_ORDER =
            "calendar_order"
        private const val KEY_CORNER_RADIUS =
            "corner_radius"
        private const val KEY_RAIL_SIDE =
            "rail_side"
    }
}
