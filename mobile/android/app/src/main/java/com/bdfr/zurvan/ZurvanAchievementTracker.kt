package com.bdfr.zurvan

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONObject

/** Queues actual local UI events and only acknowledges them after server verification. */
internal object ZurvanAchievementTracker {
    private val mutex = Mutex()
    private const val STORAGE = "zurvan_achievement_pending"
    private const val KEY = "events"

    fun record(context: Context, kind: String, section: String? = null,
               year: Int? = null, month: Int? = null) {
        val app = context.applicationContext
        val event = JSONObject().put("kind", kind).apply {
            if (section != null) put("section", section)
            if (year != null) put("year", year)
            if (month != null) put("month", month)
        }
        val key = event.toString()
        val prefs = app.getSharedPreferences(STORAGE, Context.MODE_PRIVATE)
        synchronized(this) {
            val items = prefs.getStringSet(KEY, emptySet()).orEmpty().toMutableSet()
            items.add(key)
            prefs.edit().putStringSet(KEY, items).apply()
        }
        CoroutineScope(Dispatchers.IO).launch { flush(app) }
    }

    fun clearPending(context: Context) {
        context.applicationContext.getSharedPreferences(STORAGE, Context.MODE_PRIVATE)
            .edit().remove(KEY).apply()
    }

    suspend fun flush(context: Context) = mutex.withLock {
        val prefs = context.applicationContext.getSharedPreferences(STORAGE, Context.MODE_PRIVATE)
        val client = ZurvanXpClient(context.applicationContext)
        if (client.signedInEmail == null) return@withLock
        val snapshot = synchronized(this) { prefs.getStringSet(KEY, emptySet()).orEmpty().toList() }
        for (item in snapshot) {
            val obj = runCatching { JSONObject(item) }.getOrNull() ?: continue
            val result = runCatching {
                client.reportAchievement(
                    kind = obj.getString("kind"),
                    section = obj.optString("section").takeIf { it.isNotEmpty() },
                    year = if (obj.has("year")) obj.optInt("year") else null,
                    month = if (obj.has("month")) obj.optInt("month") else null
                )
            }
            if (result.isFailure || result.getOrNull() == null) break
            synchronized(this) {
                val remaining = prefs.getStringSet(KEY, emptySet()).orEmpty().toMutableSet()
                remaining.remove(item)
                prefs.edit().putStringSet(KEY, remaining).apply()
            }
        }
    }
}
