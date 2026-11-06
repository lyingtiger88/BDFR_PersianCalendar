package com.bdfr.zurvan

import android.content.Context
import android.icu.util.Calendar
import android.icu.util.ULocale
import java.net.HttpURLConnection
import java.net.URL
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject

data class CelebrationSchedule(
    val id: String,
    val accountId: String,
    val occasionType: String,
    val persianMonth: Int,
    val persianDay: Int,
    val themeId: String,
    val enabled: Boolean,
    val title: String
)
data class CelebrationAccount(val id: String, val username: String)
data class CelebrationDirectory(
    val isCreator: Boolean,
    val currentAccountId: String,
    val accounts: List<CelebrationAccount>,
    val schedules: List<CelebrationSchedule>
)

object CelebrationCalendar {
    // Date is based on the device's local time zone; month/day use ICU Persian calendar.
    fun today(): Pair<Int, Int> =
        Calendar.getInstance(ULocale("fa_IR@calendar=persian")).let {
            (it.get(Calendar.MONTH) + 1) to it.get(Calendar.DAY_OF_MONTH)
        }

    fun themeForToday(data: CelebrationDirectory): String? {
        val (month, day) = today()
        return data.schedules.firstOrNull {
            it.accountId == data.currentAccountId && it.enabled &&
                it.persianMonth == month && it.persianDay == day &&
                ZurvanThemes.presets.any { preset -> preset.id == it.themeId }
        }?.themeId
    }
}

class ZurvanCelebrationApi(private val context: Context) {
    private val url = BuildConfig.SUPABASE_URL.trimEnd('/') + "/functions/v1/zurvan-celebration-schedules"
    private val publicKey = BuildConfig.SUPABASE_PUBLISHABLE_KEY

    suspend fun load(): CelebrationDirectory = withContext(Dispatchers.IO) {
        val token = ZurvanAuth(context).validSession()?.accessToken
            ?: error("برای دریافت مناسبت‌ها وارد حساب زروان شوید.")
        val json = call("GET", token)
        val accounts = json.optJSONArray("accounts") ?: JSONArray()
        val schedules = json.optJSONArray("schedules") ?: JSONArray()
        CelebrationDirectory(
            isCreator = json.optBoolean("is_creator"),
            currentAccountId = json.optString("account_id"),
            accounts = (0 until accounts.length()).map {
                val row = accounts.getJSONObject(it)
                CelebrationAccount(row.optString("id"), row.optString("username"))
            },
            schedules = (0 until schedules.length()).map {
                val row = schedules.getJSONObject(it)
                CelebrationSchedule(
                    row.optString("id"), row.optString("account_id"),
                    row.optString("occasion_type"), row.optInt("persian_month"),
                    row.optInt("persian_day"), row.optString("theme_id"),
                    row.optBoolean("enabled"), row.optString("display_title")
                )
            }
        )
    }

    suspend fun save(
        accountId: String, type: String, month: Int, day: Int,
        themeId: String, title: String, enabled: Boolean
    ) = withContext(Dispatchers.IO) {
        val token = ZurvanAuth(context).validSession()?.accessToken ?: error("نشست منقضی شده است.")
        call("POST", token, JSONObject()
            .put("account_id", accountId).put("occasion_type", type)
            .put("persian_month", month).put("persian_day", day)
            .put("theme_id", themeId).put("display_title", title)
            .put("enabled", enabled))
        Unit
    }

    suspend fun delete(id: String) = withContext(Dispatchers.IO) {
        val token = ZurvanAuth(context).validSession()?.accessToken ?: error("نشست منقضی شده است.")
        call("DELETE", token, pathSuffix = "?id=" + java.net.URLEncoder.encode(id, "UTF-8"))
        Unit
    }

    private fun call(method: String, token: String, payload: JSONObject? = null,
                     pathSuffix: String = ""): JSONObject {
        require(publicKey.isNotBlank()) { "اتصال Supabase پیکربندی نشده است." }
        val connection = URL(url + pathSuffix).openConnection() as HttpURLConnection
        try {
            connection.requestMethod = method
            connection.connectTimeout = 15_000
            connection.readTimeout = 20_000
            connection.setRequestProperty("apikey", publicKey)
            connection.setRequestProperty("Authorization", "Bearer $token")
            connection.setRequestProperty("Accept", "application/json")
            if (payload != null) {
                connection.setRequestProperty("Content-Type", "application/json")
                connection.doOutput = true
                connection.outputStream.use { it.write(payload.toString().toByteArray(Charsets.UTF_8)) }
            }
            val status = connection.responseCode
            val raw = (if (status in 200..299) connection.inputStream else connection.errorStream)
                ?.bufferedReader()?.use { it.readText() }.orEmpty()
            val response = JSONObject(raw.ifBlank { "{}" })
            if (status !in 200..299)
                error(response.optString("error").ifBlank { "خطای ارتباط با سرور: $status" })
            return response
        } finally { connection.disconnect() }
    }
}
