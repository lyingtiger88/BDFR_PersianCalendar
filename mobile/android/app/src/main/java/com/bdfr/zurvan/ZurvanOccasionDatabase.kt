package com.bdfr.zurvan

import android.content.Context
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Opt-in, validated update of fixed-date occasions; preserves old cache on errors. */
object ZurvanOccasionDatabase {
    private fun endpoint(year: Int) =
        "https://raw.githubusercontent.com/lyingtiger88/BDFR_PersianCalendar/main/data/occasions/years/$year.json"
    private const val prefs = "zurvan_occasion_catalog"
    data class Result(val version: String, val count: Int)

    suspend fun update(context: Context, year: Int = 1405): Result = withContext(Dispatchers.IO) {
        require(year in 1300..1500)
        val connection = (URL(endpoint(year)).openConnection() as HttpURLConnection).apply {
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
            require(root.getInt("schema") == 2 && root.getInt("year") == year) { "نسخه ساختار داده ناشناخته است" }
            val version = root.getString("version").take(40)
            require(version.isNotBlank()) { "شناسه نسخه نامعتبر است" }
            val entries = root.getJSONArray("entries")
            require(entries.length() in 100..1000) { "تعداد مناسبت‌ها نامعتبر است" }
            repeat(entries.length()) { i ->
                val item = entries.getJSONObject(i)
                require(item.getInt("year") == year && item.getString("calendar") == "persian")
                val month = item.getInt("month")
                val day = item.getInt("day")
                require(month in 1..12 && day in 1..31)
                require(item.getString("title").length in 1..180)
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
                    it.getString("title"),type,it.getInt("month"),it.getInt("day"),0,0,
                    annual = false, targetPersianYear = it.optInt("year", 0).takeIf { y -> y > 0 })
            }
        }.getOrNull()
    }
    fun version(context: Context): String =
        context.getSharedPreferences(prefs,Context.MODE_PRIVATE)
            .getString("version","نسخه داخلی") ?: "نسخه داخلی"
}
