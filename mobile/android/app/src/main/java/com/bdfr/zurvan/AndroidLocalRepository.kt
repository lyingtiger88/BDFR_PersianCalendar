package com.bdfr.zurvan

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.security.MessageDigest
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class AndroidLocalRepository(
    context: Context
) {
    private val appContext = context.applicationContext

    private val preferences = appContext.getSharedPreferences(
        "zurvan_android_local",
        Context.MODE_PRIVATE
    )

    private val _plans = MutableStateFlow(loadPlans().filterNot(::isExpiredPlan))
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
        isPrivate: Boolean,
        icon: String = "📌",
        deleteAfterDays: Int? = null
    ) {
        val next = _plans.value + LocalPlan(
            id = "plan_${UUID.randomUUID()}",
            title = title.trim(),
            dateKey = dateKey,
            hour = hour.coerceIn(0, 23),
            minute = minute.coerceIn(0, 59),
            kind = kind,
            isPrivate = isPrivate,
            icon = icon,
            deleteAfterDays = deleteAfterDays?.coerceIn(0,3650)
        )
        savePlans(next)
    }

    /** Expired entries are deleted from the persisted local source, not just hidden. */
    fun purgeExpiredPlans() {
        val kept = _plans.value.filterNot(::isExpiredPlan)
        if (kept.size != _plans.value.size) savePlans(kept)
        else if (loadPlans().size != kept.size) savePlans(kept)
    }

    private fun isExpiredPlan(plan: LocalPlan): Boolean {
        val days = plan.deleteAfterDays ?: return false
        if (plan.kind != "event") return false
        val at = AndroidCalendarEngine.persianDateTimeToMillis(plan.dateKey, plan.hour, plan.minute) ?: return false
        val deadline = java.time.Instant.ofEpochMilli(at).atZone(java.time.ZoneId.systemDefault())
            .plusDays(days.toLong()).toInstant().toEpochMilli()
        return System.currentTimeMillis() > deadline
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
        require(ZurvanBuiltInOccasions.validAnnualDate(calendar, month, day)) {
            "تاریخ مناسبت سالانه در تقویم انتخابی نامعتبر است"
        }
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
    ): LocalReminder {
        val reminder = LocalReminder(
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

        saveReminders(
            _reminders.value + reminder
        )

        return reminder
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

    fun getOrCreateCompanionDeviceId(): String {
        val existing = preferences.getString(
            KEY_COMPANION_DEVICE_ID,
            null
        )
        if (!existing.isNullOrBlank()) {
            return existing
        }

        val created = "dev_" +
            UUID.randomUUID()
                .toString()
                .replace("-", "")

        preferences.edit()
            .putString(
                KEY_COMPANION_DEVICE_ID,
                created
            )
            .apply()

        return created
    }

    fun createCompanionSnapshot(
        sourceDeviceId: String,
        targetDeviceId: String
    ): SyncBatch {
        val now = Instant.now().toString()
        val items = mutableListOf<SyncEnvelope>()

        fun addEnvelope(
            kind: SyncEntityKind,
            entityId: String,
            payload: JSONObject
        ) {
            val payloadJson = payload.toString()
            items += SyncEnvelope(
                envelopeId =
                    "env_" +
                        UUID.randomUUID()
                            .toString()
                            .replace("-", ""),
                entityKind = kind,
                entityId = entityId,
                mutation =
                    SyncMutationKind.UPSERT,
                revision = 1,
                modifiedAtUtc = now,
                originDeviceId =
                    sourceDeviceId,
                payloadJson = payloadJson,
                contentHash =
                    sha256(payloadJson)
            )
        }

        _plans.value.forEach { plan ->
            if (
                plan.kind.equals(
                    "task",
                    ignoreCase = true
                )
            ) {
                addEnvelope(
                    SyncEntityKind.TASK,
                    plan.id,
                    JSONObject()
                        .put("Id", plan.id)
                        .put("Title", plan.title)
                        .put(
                            "PersianDate",
                            plan.dateKey.replace("-", "/")
                        )
                        .put(
                            "DueTime",
                            "%02d:%02d".format(
                                plan.hour,
                                plan.minute
                            )
                        )
                        .put("Priority", 0)
                        .put(
                            "Completed",
                            plan.completed
                        )
                        .put(
                            "Category",
                            JSONObject.NULL
                        )
                        .put(
                            "Description",
                            JSONObject.NULL
                        )
                )
            } else {
                addEnvelope(
                    SyncEntityKind.EVENT,
                    plan.id,
                    JSONObject()
                        .put("Id", plan.id)
                        .put("Title", plan.title)
                        .put(
                            "PersianDate",
                            plan.dateKey.replace("-", "/")
                        )
                        .put(
                            "StartTime",
                            "%02d:%02d".format(
                                plan.hour,
                                plan.minute
                            )
                        )
                        .put(
                            "EndTime",
                            JSONObject.NULL
                        )
                        .put("AllDay", false)
                        .put(
                            "Description",
                            JSONObject.NULL
                        )
                        .put(
                            "Category",
                            plan.kind
                        )
                        .put(
                            "Location",
                            JSONObject.NULL
                        )
                        .put("Recurrence", 0)
                        .put(
                            "Privacy",
                            if (plan.isPrivate) {
                                1
                            } else {
                                0
                            }
                        )
                )
            }
        }

        _notes.value.forEach { note ->
            val id = "note_" +
                note.dateKey
                    .replace("/", "_")

            addEnvelope(
                SyncEntityKind.NOTE,
                id,
                JSONObject()
                    .put("Id", id)
                    .put(
                        "PersianDate",
                        note.dateKey.replace("-", "/")
                    )
                    .put("Text", note.text)
                    .put("Pinned", false)
                    .put(
                        "ModifiedAtUtc",
                        now
                    )
            )
        }

        _occasions.value.forEach { occasion ->
            val calendarSystem =
                when (occasion.calendar) {
                    CalendarPriority.PERSIAN ->
                        0
                    CalendarPriority.GREGORIAN ->
                        1
                    CalendarPriority.HIJRI ->
                        2
                }

            addEnvelope(
                SyncEntityKind.SPECIAL_OCCASION,
                occasion.id,
                JSONObject()
                    .put("Id", occasion.id)
                    .put(
                        "Title",
                        occasion.title
                    )
                    .put(
                        "CalendarSystem",
                        calendarSystem
                    )
                    .put(
                        "Month",
                        occasion.month
                    )
                    .put(
                        "Day",
                        occasion.day
                    )
                    .put(
                        "RepeatYearly",
                        occasion.annual
                    )
                    .put(
                        "ReminderDaysBefore",
                        JSONArray()
                            .put(7)
                            .put(1)
                            .put(0)
                    )
                    .put(
                        "Category",
                        JSONObject.NULL
                    )
                    .put(
                        "Note",
                        JSONObject.NULL
                    )
            )
        }

        _reminders.value.forEach { reminder ->
            val fireAt = AndroidCalendarEngine
                .persianDateTimeToInstant(
                    reminder.dateKey,
                    reminder.hour,
                    reminder.minute
                )
                ?.toString()
                ?: now

            addEnvelope(
                SyncEntityKind.REMINDER,
                reminder.id,
                JSONObject()
                    .put("Id", reminder.id)
                    .put("ItemKind", 0)
                    .put(
                        "ItemId",
                        reminder.id
                    )
                    .put(
                        "FireAtUtc",
                        fireAt
                    )
                    .put(
                        "State",
                        if (
                            reminder.acknowledged
                        ) {
                            6
                        } else {
                            0
                        }
                    )
                    .put(
                        "Title",
                        reminder.title
                    )
                    .put(
                        "Body",
                        reminder.title
                    )
                    .put(
                        "Privacy",
                        if (
                            reminder.isPrivate
                        ) {
                            1
                        } else {
                            0
                        }
                    )
                    .put(
                        "RepeatCount",
                        reminder.repeatCount
                    )
                    .put(
                        "RepeatIntervalMinutes",
                        reminder
                            .repeatIntervalMinutes
                    )
                    .put("Attempt", 0)
            )
        }

        val settingsPayload = JSONObject()
            .put(
                "CalendarOrder",
                JSONArray().apply {
                    _uiPreferences.value
                        .calendarOrder
                        .forEach {
                            put(it.key)
                        }
                }
            )
            .put(
                "CornerRadius",
                _uiPreferences.value
                    .cornerRadius
            )
            .put(
                "NavigationRailSide",
                _uiPreferences.value
                    .railSide
            )

        addEnvelope(
            SyncEntityKind.SETTING,
            "shared-ui",
            settingsPayload
        )

        return SyncBatch(
            protocolVersion = 1,
            batchId =
                "sync_" +
                    UUID.randomUUID()
                        .toString()
                        .replace("-", ""),
            sourceDeviceId =
                sourceDeviceId,
            targetDeviceId =
                targetDeviceId,
            sinceCheckpoint = null,
            items = items,
            createdAtUtc = now
        )
    }

    fun applyCompanionSnapshot(
        batch: SyncBatch,
        preferIncoming: Boolean
    ): Int {
        var applied = 0

        var plans = _plans.value.toMutableList()
        var occasions =
            _occasions.value.toMutableList()
        var reminders =
            _reminders.value.toMutableList()
        var notes = _notes.value.toMutableList()
        var ui = _uiPreferences.value

        batch.items.forEach { envelope ->
            val payloadText =
                envelope.payloadJson
                    ?: return@forEach

            if (
                envelope.mutation ==
                SyncMutationKind.DELETE
            ) {
                if (!preferIncoming) {
                    return@forEach
                }

                when (envelope.entityKind) {
                    SyncEntityKind.EVENT,
                    SyncEntityKind.TASK -> {
                        val before = plans.size
                        plans.removeAll {
                            it.id ==
                                envelope.entityId
                        }
                        if (
                            plans.size != before
                        ) {
                            applied++
                        }
                    }

                    SyncEntityKind.NOTE -> {
                        val before = notes.size
                        notes.removeAll {
                            "note_" +
                                it.dateKey
                                    .replace(
                                        "/",
                                        "_"
                                    ) ==
                                envelope.entityId
                        }
                        if (
                            notes.size != before
                        ) {
                            applied++
                        }
                    }

                    SyncEntityKind.SPECIAL_OCCASION -> {
                        val before =
                            occasions.size
                        occasions.removeAll {
                            it.id ==
                                envelope.entityId
                        }
                        if (
                            occasions.size !=
                            before
                        ) {
                            applied++
                        }
                    }

                    SyncEntityKind.REMINDER -> {
                        val before =
                            reminders.size
                        reminders.removeAll {
                            it.id ==
                                envelope.entityId
                        }
                        if (
                            reminders.size !=
                            before
                        ) {
                            applied++
                        }
                    }

                    else -> Unit
                }

                return@forEach
            }

            val payload = try {
                JSONObject(payloadText)
            } catch (_: Exception) {
                return@forEach
            }

            when (envelope.entityKind) {
                SyncEntityKind.EVENT -> {
                    val id =
                        payload.optString(
                            "Id",
                            envelope.entityId
                        )
                    val exists =
                        plans.indexOfFirst {
                            it.id == id
                        }
                    if (
                        !preferIncoming &&
                        exists >= 0
                    ) {
                        return@forEach
                    }

                    val start =
                        parseHourMinute(
                            payload.optString(
                                "StartTime",
                                "09:00"
                            )
                        )

                    val plan = LocalPlan(
                        id = id,
                        title =
                            payload.optString(
                                "Title",
                                "رویداد"
                            ),
                        dateKey =
                            payload.optString(
                                "PersianDate",
                                "1400/01/01"
                            ).replace("/", "-"),
                        hour = start.first,
                        minute =
                            start.second,
                        kind =
                            payload.optString(
                                "Category",
                                "event"
                            )
                                .ifBlank {
                                    "event"
                                },
                        isPrivate =
                            payload.optInt(
                                "Privacy",
                                0
                            ) != 0,
                        completed = false
                    )

                    if (exists >= 0) {
                        plans[exists] = plan
                    } else {
                        plans += plan
                    }
                    applied++
                }

                SyncEntityKind.TASK -> {
                    val id =
                        payload.optString(
                            "Id",
                            envelope.entityId
                        )
                    val exists =
                        plans.indexOfFirst {
                            it.id == id
                        }
                    if (
                        !preferIncoming &&
                        exists >= 0
                    ) {
                        return@forEach
                    }

                    val due =
                        parseHourMinute(
                            payload.optString(
                                "DueTime",
                                "09:00"
                            )
                        )

                    val task = LocalPlan(
                        id = id,
                        title =
                            payload.optString(
                                "Title",
                                "کار"
                            ),
                        dateKey =
                            payload.optString(
                                "PersianDate",
                                "1400/01/01"
                            ).replace("/", "-"),
                        hour = due.first,
                        minute = due.second,
                        kind = "task",
                        isPrivate = false,
                        completed =
                            payload.optBoolean(
                                "Completed",
                                false
                            )
                    )

                    if (exists >= 0) {
                        plans[exists] = task
                    } else {
                        plans += task
                    }
                    applied++
                }

                SyncEntityKind.NOTE -> {
                    val dateKey =
                        payload.optString(
                            "PersianDate",
                            ""
                        ).replace("/", "-")
                    if (dateKey.isBlank()) {
                        return@forEach
                    }

                    val exists =
                        notes.indexOfFirst {
                            it.dateKey ==
                                dateKey
                        }

                    if (
                        !preferIncoming &&
                        exists >= 0
                    ) {
                        return@forEach
                    }

                    val note =
                        LocalDayNote(
                            dateKey =
                                dateKey,
                            text =
                                payload
                                    .optString(
                                        "Text",
                                        ""
                                    ),
                            isPrivate =
                                false
                        )

                    if (exists >= 0) {
                        notes[exists] = note
                    } else {
                        notes += note
                    }
                    applied++
                }

                SyncEntityKind.SPECIAL_OCCASION -> {
                    val id =
                        payload.optString(
                            "Id",
                            envelope.entityId
                        )
                    val exists =
                        occasions
                            .indexOfFirst {
                                it.id == id
                            }

                    if (
                        !preferIncoming &&
                        exists >= 0
                    ) {
                        return@forEach
                    }

                    val calendar =
                        when (
                            payload.optInt(
                                "CalendarSystem",
                                0
                            )
                        ) {
                            1 ->
                                CalendarPriority
                                    .GREGORIAN
                            2 ->
                                CalendarPriority
                                    .HIJRI
                            else ->
                                CalendarPriority
                                    .PERSIAN
                        }

                    val item =
                        LocalOccasion(
                            id = id,
                            title =
                                payload
                                    .optString(
                                        "Title",
                                        "مناسبت"
                                    ),
                            calendar =
                                calendar,
                            month =
                                payload.optInt(
                                    "Month",
                                    1
                                ),
                            day =
                                payload.optInt(
                                    "Day",
                                    1
                                ),
                            hour = 9,
                            minute = 0,
                            annual =
                                payload
                                    .optBoolean(
                                        "RepeatYearly",
                                        true
                                    )
                        )

                    if (exists >= 0) {
                        occasions[exists] =
                            item
                    } else {
                        occasions += item
                    }
                    applied++
                }

                SyncEntityKind.REMINDER -> {
                    val id =
                        payload.optString(
                            "Id",
                            envelope.entityId
                        )
                    val exists =
                        reminders
                            .indexOfFirst {
                                it.id == id
                            }

                    if (
                        !preferIncoming &&
                        exists >= 0
                    ) {
                        return@forEach
                    }

                    val localDateTime =
                        AndroidCalendarEngine
                            .instantToPersianDateTime(
                                payload
                                    .optString(
                                        "FireAtUtc",
                                        ""
                                    )
                            )

                    val item =
                        LocalReminder(
                            id = id,
                            title =
                                payload
                                    .optString(
                                        "Title",
                                        "یادآور"
                                    ),
                            dateKey =
                                localDateTime
                                    ?.first
                                    ?: "1400/01/01",
                            hour =
                                localDateTime
                                    ?.second
                                    ?: 9,
                            minute =
                                localDateTime
                                    ?.third
                                    ?: 0,
                            isPrivate =
                                payload.optInt(
                                    "Privacy",
                                    0
                                ) != 0,
                            repeatCount =
                                payload.optInt(
                                    "RepeatCount",
                                    1
                                ),
                            repeatIntervalMinutes =
                                payload.optInt(
                                    "RepeatIntervalMinutes",
                                    5
                                ),
                            acknowledged =
                                payload.optInt(
                                    "State",
                                    0
                                ) == 6
                        )

                    if (exists >= 0) {
                        reminders[exists] =
                            item
                    } else {
                        reminders += item
                    }
                    applied++
                }

                SyncEntityKind.SETTING -> {
                    if (!preferIncoming) {
                        return@forEach
                    }

                    val orderJson =
                        payload.optJSONArray(
                            "CalendarOrder"
                        )

                    val order =
                        buildList {
                            if (
                                orderJson != null
                            ) {
                                for (
                                    index in
                                    0 until
                                        orderJson
                                            .length()
                                ) {
                                    val key =
                                        orderJson
                                            .optString(
                                                index
                                            )
                                    CalendarPriority
                                        .entries
                                        .firstOrNull {
                                            it.key ==
                                                key
                                        }
                                        ?.let {
                                            add(it)
                                        }
                                }
                            }
                        }

                    ui = ui.copy(
                        calendarOrder =
                            if (
                                order.size == 3
                            ) {
                                order
                            } else {
                                ui.calendarOrder
                            },
                        cornerRadius =
                            payload.optDouble(
                                "CornerRadius",
                                ui.cornerRadius
                                    .toDouble()
                            )
                                .toFloat()
                                .coerceIn(
                                    0f,
                                    32f
                                ),
                        railSide =
                            if (
                                payload.optString(
                                    "NavigationRailSide",
                                    ui.railSide
                                ) == "right"
                            ) {
                                "right"
                            } else {
                                "left"
                            }
                    )
                    applied++
                }

                else -> Unit
            }
        }

        savePlans(plans)
        saveOccasions(occasions)
        saveNotes(notes)
        saveReminders(reminders)
        saveUiPreferences(ui)

        reminders
            .filter {
                !it.acknowledged
            }
            .forEach {
                AndroidReminderScheduler
                    .schedule(
                        appContext,
                        it
                    )
            }

        return applied
    }

    private fun parseHourMinute(
        text: String
    ): Pair<Int, Int> {
        val parts =
            text.split(":")
        val hour =
            parts.getOrNull(0)
                ?.toIntOrNull()
                ?.coerceIn(0, 23)
                ?: 9
        val minute =
            parts.getOrNull(1)
                ?.toIntOrNull()
                ?.coerceIn(0, 59)
                ?: 0

        return hour to minute
    }

    private fun sha256(
        value: String
    ): String =
        MessageDigest
            .getInstance("SHA-256")
            .digest(
                value.toByteArray(
                    Charsets.UTF_8
                )
            )
            .joinToString("") {
                "%02x".format(it)
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
                    .put("icon", item.icon)
                    .put("deleteAfterDays", item.deleteAfterDays ?: JSONObject.NULL)
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
                ),
                icon = item.optString("icon", "📌"),
                deleteAfterDays = if (item.isNull("deleteAfterDays")) null else item.optInt("deleteAfterDays").takeIf { it in 0..3650 }
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
                    .put("annual", item.annual)
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
                minute = item.optInt("minute", 0),
                annual = item.optBoolean("annual", true)
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
        private const val KEY_COMPANION_DEVICE_ID =
            "companion_device_id"
    }
}
