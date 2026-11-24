package com.bdfr.zurvan

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONArray
import org.json.JSONObject

/**
 * Achievement telemetry is completely separate from calendar writes and XP claims.
 * No server response can block a note save or a UI render. Pending events survive
 * network failures and are deduplicated by the authenticated server.
 */
internal object ZurvanAchievementTracker {
    private const val STORAGE = "zurvan_achievement_pending"
    private const val KEY = "events_v2"
    private const val LEGACY_KEY = "events"
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val mutex = Mutex()
    private val ioLock = Any()

    fun record(context: Context, kind: String, section: String? = null,
               year: Int? = null, month: Int? = null) {
        val app = context.applicationContext
        if (kind !in setOf("app_open", "content_saved", "explore", "calendar_month")) return
        if (kind == "explore" && section !in setOf(
                "calendar", "plans", "occasions", "reminders", "desk", "settings")) return
        if (kind == "calendar_month" && (year == null || year !in 1200..1700
                    || month == null || month !in 1..12)) return
        scope.launch {
            runCatching {
                val identity = ZurvanXpClient(app).signedInEmail?.trim()?.lowercase() ?: "guest"
                val event = JSONObject().put("account", identity).put("kind", kind).apply {
                    if (section != null) put("section", section)
                    if (year != null) put("year", year)
                    if (month != null) put("month", month)
                }.toString()
                val prefs = app.getSharedPreferences(STORAGE, Context.MODE_PRIVATE)
                synchronized(ioLock) {
                    val old = runCatching { JSONArray(prefs.getString(KEY, "[]")) }
                        .getOrElse { JSONArray() }
                    val events = linkedSetOf<String>()
                    for (i in 0 until old.length()) old.optString(i).takeIf(String::isNotBlank)
                        ?.let(events::add)
                    events.add(event)
                    // Bounded offline queue; badge awards themselves live on the server.
                    val latest = events.toList().takeLast(512)
                    prefs.edit().putString(KEY, JSONArray(latest).toString()).apply()
                }
                flush(app)
            }
        }
    }

    fun clearPending(context: Context) {
        // Old unscoped queue cannot be safely attached to the next account.
        context.applicationContext.getSharedPreferences(STORAGE, Context.MODE_PRIVATE)
            .edit().remove(KEY).remove(LEGACY_KEY).apply()
    }

    suspend fun flush(context: Context) = mutex.withLock {
        val app = context.applicationContext
        val client = ZurvanXpClient(app)
        val signedIn = client.signedInEmail?.trim()?.lowercase() ?: return@withLock
        val prefs = app.getSharedPreferences(STORAGE, Context.MODE_PRIVATE)
        val snapshot = synchronized(ioLock) {
            val items = runCatching { JSONArray(prefs.getString(KEY, "[]")) }
                .getOrElse { JSONArray() }
            (0 until items.length()).mapNotNull {
                items.optString(it).takeIf(String::isNotBlank)
            }
        }
        for (item in snapshot) {
            val event = runCatching { JSONObject(item) }.getOrNull()
            if (event == null) {
                synchronized(ioLock) { remove(prefs, item) }
                continue
            }
            val owner = event.optString("account", "guest")
            if (owner != signedIn && owner != "guest") continue
            val ok = runCatching {
                client.reportAchievement(
                    event.getString("kind"),
                    event.optString("section").takeIf { it.isNotEmpty() },
                    if (event.has("year")) event.optInt("year") else null,
                    if (event.has("month")) event.optInt("month") else null
                )?.optBoolean("ok") == true
            }.getOrDefault(false)
            if (!ok) break // Retain offline events for a future retry.
            synchronized(ioLock) { remove(prefs, item) }
        }
    }

    private fun remove(prefs: android.content.SharedPreferences, item: String) {
        val stored = runCatching { JSONArray(prefs.getString(KEY, "[]")) }
            .getOrElse { JSONArray() }
        val remaining = JSONArray()
        for (i in 0 until stored.length()) {
            val value = stored.optString(i)
            if (value != item) remaining.put(value)
        }
        prefs.edit().putString(KEY, remaining.toString()).apply()
    }
}
