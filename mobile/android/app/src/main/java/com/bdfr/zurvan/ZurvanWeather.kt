package com.bdfr.zurvan

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

data class ZurvanWeatherHour(val time: LocalDateTime, val temp: Double, val code: Int)
data class ZurvanWeatherDay(val date: LocalDate, val low: Double, val high: Double, val code: Int, val rain: Int)
data class ZurvanWeatherData(
    val city: String, val nowTemperature: Double, val nowCode: Int,
    val hours: List<ZurvanWeatherHour>, val days: Map<LocalDate, ZurvanWeatherDay>
)

object ZurvanWeather {
    private var cachedKey = ""
    private var cachedAt = 0L
    private var cached: ZurvanWeatherData? = null

    fun icon(code: Int): String = when(code) {
        0 -> "☀️"
        1,2 -> "🌤️"
        3 -> "☁️"
        45,48 -> "🌫️"
        51,53,55,56,57,61,63,65,66,67,80,81,82 -> "🌧️"
        71,73,75,77,85,86 -> "❄️"
        95,96,99 -> "⛈️"
        else -> "🌡️"
    }

    fun enabled(context: Context): Boolean =
        context.getSharedPreferences("zurvan_weather", Context.MODE_PRIVATE).getBoolean("enabled", true)

    suspend fun load(context: Context, force: Boolean = false): ZurvanWeatherData = withContext(Dispatchers.IO) {
        val place = ZurvanPrayerTimes.settings(context)
        require(place.latitude in -90.0..90.0 && place.longitude in -180.0..180.0)
        val key = "${place.latitude},${place.longitude},${place.zone}"
        synchronized(this@ZurvanWeather) {
            if (!force && cachedKey == key && System.currentTimeMillis() - cachedAt < 30*60*1000L)
                cached?.let { return@withContext it }
        }
        val url = URL("https://api.open-meteo.com/v1/forecast?latitude=${place.latitude}&longitude=${place.longitude}" +
            "&current=temperature_2m,weather_code" +
            "&hourly=temperature_2m,weather_code" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
            "&past_days=7&forecast_days=8&timezone=auto")
        val connection = (url.openConnection() as HttpURLConnection).apply {
            connectTimeout = 12000
            readTimeout = 12000
            requestMethod = "GET"
        }
        val root = try {
            if (connection.responseCode !in 200..299) error("Weather HTTP ${connection.responseCode}")
            JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
        } finally { connection.disconnect() }
        val now = root.getJSONObject("current")
        val hourly = root.getJSONObject("hourly")
        val times = hourly.getJSONArray("time")
        val temperatures = hourly.getJSONArray("temperature_2m")
        val codes = hourly.getJSONArray("weather_code")
        val allHours = (0 until minOf(times.length(), temperatures.length(), codes.length())).mapNotNull { index ->
            runCatching {
                ZurvanWeatherHour(LocalDateTime.parse(times.getString(index)),
                    temperatures.getDouble(index), codes.getInt(index))
            }.getOrNull()
        }
        val offset = root.optInt("utc_offset_seconds",0)
        val localNow = java.time.Instant.now().plusSeconds(offset.toLong())
            .atZone(java.time.ZoneOffset.UTC).toLocalDateTime()
        val recent = allHours.filter { !it.time.isAfter(localNow) && it.time.isAfter(localNow.minusHours(24)) }.takeLast(24)
        val daily = root.getJSONObject("daily")
        val dates = daily.getJSONArray("time")
        val highs = daily.getJSONArray("temperature_2m_max")
        val lows = daily.getJSONArray("temperature_2m_min")
        val dCodes = daily.getJSONArray("weather_code")
        val rain = daily.getJSONArray("precipitation_probability_max")
        val days = (0 until minOf(dates.length(), highs.length(), lows.length(), dCodes.length(), rain.length()))
            .associate { index ->
                val day = ZurvanWeatherDay(LocalDate.parse(dates.getString(index)), lows.getDouble(index),
                    highs.getDouble(index), dCodes.getInt(index), rain.optInt(index,0))
                day.date to day
            }
        val result = ZurvanWeatherData(place.city, now.getDouble("temperature_2m"),
            now.getInt("weather_code"), recent, days)
        synchronized(this@ZurvanWeather) { cachedKey=key; cachedAt=System.currentTimeMillis(); cached=result }
        result
    }
}
