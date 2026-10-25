package com.bdfr.zurvan

import android.content.Context
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Opt-in, validated update of fixed-date occasions; preserves old cache on errors. */
object ZurvanOccasionDatabase {
    private const val endpoint =
        "https://raw.githubusercontent.com/lyingtiger88/BDFR_PersianCalendar/main/data/occasions/fixed-date-catalog.json"
    private const val prefs = "zurvan_occasion_catalog"
    data class Result(val version: String, val count: Int)

    suspend fun update(context: Context): Result = withContext(Dispatchers.IO) {
        val connection = (URL(endpoint).openConnection() as HttpURLConnection).apply {
            connectTimeout = 10000
            readTimeout = 10000
            requestMethod = "GET"
            instanceFollowRedirects = false
        }
        try {
            check(connection.responseCode == 200) { "خطای دریافت: ${connection.responseCode}" }
            val data = connection.inputStream.use { stream ->
                val output = java.io.ByteArrayOutputStream()
                val buffer = ByteArray(8192)
                while (true) {
                    val n = stream.read(buffer)
                    if (n < 0) break
                    output.write(buffer, 0, n)
                    require(output.size() <= 500_000) { "فایل بیش از حد بزرگ است" }
                }
                output.toByteArray()
            }
            require(data.size in 1..500_000) { "اندازه پایگاه داده نامعتبر است" }
            val text = data.toString(Charsets.UTF_8)
            val root = JSONObject(text)
            require(root.getInt("schema") == 1) { "نسخه ساختار داده ناشناخته است" }
            val version = root.getString("version").take(40)
            require(version.isNotBlank()) { "شناسه نسخه نامعتبر است" }
            val entries = root.getJSONArray("entries")
            require(entries.length() in 1..1000) { "تعداد مناسبت‌ها نامعتبر است" }
            repeat(entries.length()) { i ->
                val item = entries.getJSONObject(i)
                require(item.getString("calendar") in listOf("persian", "gregorian"))
                val month = item.getInt("month")
                val day = item.getInt("day")
                require(month in 1..12 && day in 1..31)
                require(item.getString("title").length in 1..180)
                if (item.getString("calendar") == "gregorian")
                    require(runCatching { java.time.LocalDate.of(2024,month,day) }.isSuccess)
                if (item.getString("calendar") == "persian")
                    require(day <= if (month <= 6) 31 else if (month <= 11) 30 else 29)
            }
            context.getSharedPreferences(prefs, Context.MODE_PRIVATE).edit()
                .putString("catalog", text).putString("version", version).apply()
            Result(version, entries.length())
        } finally { connection.disconnect() }
    }
    fun current(context: Context): List<LocalOccasion>? {
        val raw = context.getSharedPreferences(prefs, Context.MODE_PRIVATE)
            .getString("catalog", null) ?: return null
        return runCatching {
            val a = JSONObject(raw).getJSONArray("entries")
            (0 until a.length()).map { i ->
                val it = a.getJSONObject(i)
                val type = if (it.getString("calendar") == "persian")
                    CalendarPriority.PERSIAN else CalendarPriority.GREGORIAN
                LocalOccasion("database-${type.key}-${it.getInt("month")}-${it.getInt("day")}-$i",
                    it.getString("title"),type,it.getInt("month"),it.getInt("day"),0,0)
            }
        }.getOrNull()
    }
    fun version(context: Context): String =
        context.getSharedPreferences(prefs,Context.MODE_PRIVATE)
            .getString("version","نسخه داخلی") ?: "نسخه داخلی"
}
