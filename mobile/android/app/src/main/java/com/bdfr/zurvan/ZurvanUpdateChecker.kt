package com.bdfr.zurvan

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/**
 * Only checks GitHub's latest stable release; no silent download or installation.
 * Do not use test/pre-release artifacts as production updates.
 */
object ZurvanUpdateChecker {
    data class Release(val version: String, val page: String)

    suspend fun latestStable(): Release = withContext(Dispatchers.IO) {
        val connection = (URL("https://api.github.com/repos/lyingtiger88/BDFR_PersianCalendar/releases/latest")
            .openConnection() as HttpURLConnection).apply {
            connectTimeout = 8000
            readTimeout = 8000
            setRequestProperty("Accept", "application/vnd.github+json")
            setRequestProperty("User-Agent", "Zurvan-Android")
        }
        try {
            check(connection.responseCode == HttpURLConnection.HTTP_OK) {
                "نسخه پایدار منتشرشده‌ای یافت نشد یا سرویس در دسترس نیست (HTTP ${connection.responseCode})."
            }
            val body = connection.inputStream.bufferedReader().use { it.readText().take(100_000) }
            val json = JSONObject(body)
            check(!json.optBoolean("draft") && !json.optBoolean("prerelease")) {
                "نسخه منتشرشده هنوز پایدار نیست."
            }
            val tag = json.optString("tag_name").trim()
            val page = json.optString("html_url").trim()
            check(tag.isNotBlank() && page.startsWith("https://github.com/lyingtiger88/BDFR_PersianCalendar/releases/")) {
                "اطلاعات انتشار نامعتبر است."
            }
            Release(tag, page)
        } finally {
            connection.disconnect()
        }
    }
}
