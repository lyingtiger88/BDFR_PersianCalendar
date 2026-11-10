package com.bdfr.zurvan

import android.content.Context
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.pager.VerticalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter

object ZurvanCalendarPanelPreferences {
    const val SWIPE = "swipe"
    const val SMOOTH_SCROLL = "smooth_scroll"
    private const val FILE = "zurvan_calendar_panels"
    fun mode(context: Context): String =
        context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
            .getString("mode", SWIPE) ?: SWIPE
    fun save(context: Context, value: String) {
        require(value == SWIPE || value == SMOOTH_SCROLL)
        context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
            .edit().putString("mode", value).apply()
    }
}

@Composable
fun ZurvanCalendarPanels(
    viewModel: ZurvanViewModel,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    var mode by remember(context) { mutableStateOf(ZurvanCalendarPanelPreferences.mode(context)) }
    if (mode == ZurvanCalendarPanelPreferences.SWIPE) {
        val pagerState = rememberPagerState(pageCount = { 3 })
        Column(modifier.fillMaxSize()) {
            Row(
                Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 4.dp),
                horizontalArrangement = Arrangement.Center,
                verticalAlignment = Alignment.CenterVertically
            ) {
                listOf("تقویم", "ساعت و اوقات شرعی", "آب‌وهوا").forEachIndexed { index, label ->
                    Text(
                        text = if (pagerState.currentPage == index) "● $label" else "○ $label",
                        modifier = Modifier.padding(horizontal = 5.dp),
                        color = if (pagerState.currentPage == index) MaterialTheme.colorScheme.primary
                            else MaterialTheme.colorScheme.onSurfaceVariant,
                        style = MaterialTheme.typography.labelSmall
                    )
                }
            }
            VerticalPager(
                state = pagerState,
                modifier = Modifier.fillMaxSize(),
                beyondViewportPageCount = 1
            ) { page ->
                when (page) {
                    0 -> CalendarMonthPage(viewModel, Modifier.fillMaxSize(), scrollMode = false)
                    1 -> PrayerClockPanel(Modifier.fillMaxSize())
                    else -> WeatherPanel(Modifier.fillMaxSize())
                }
            }
        }
    } else {
        Column(
            modifier.fillMaxSize().verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(18.dp)
        ) {
            CalendarMonthPage(viewModel, Modifier.fillMaxWidth(), scrollMode = true)
            HorizontalDivider()
            PrayerClockPanel(Modifier.fillMaxWidth().heightIn(min = 520.dp))
            HorizontalDivider()
            WeatherPanel(Modifier.fillMaxWidth().heightIn(min = 420.dp))
        }
    }
}

@Composable
private fun PrayerClockPanel(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val settings = remember(context) { ZurvanPrayerTimes.settings(context) }
    val zone = remember(settings.zone) { runCatching { ZoneId.of(settings.zone) }.getOrDefault(ZoneId.systemDefault()) }
    val today = remember(settings.zone) { LocalDate.now(zone) }
    val prayers = remember(settings, today) {
        runCatching { ZurvanPrayerTimes.calculate(today, settings) }.getOrDefault(emptyList())
    }
    Column(
        modifier.fillMaxWidth().padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text("ساعت و اوقات شرعی", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
        Text(settings.city, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Card(Modifier.fillMaxWidth()) {
            Column(Modifier.fillMaxWidth().padding(10.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                ZurvanCalendarClock(Modifier.fillMaxWidth())
            }
        }
        Text("اوقات شرعی امروز", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
        if (prayers.isEmpty()) {
            Text("برای موقعیت انتخاب‌شده امکان محاسبه اوقات شرعی وجود ندارد.")
        } else {
            prayers.forEach { entry ->
                Row(
                    Modifier.fillMaxWidth().background(
                        MaterialTheme.colorScheme.surfaceVariant,
                        RoundedCornerShape(12.dp)
                    ).padding(horizontal = 14.dp, vertical = 11.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(entry.label, fontWeight = FontWeight.Medium)
                    Text(entry.time.format(DateTimeFormatter.ofPattern("HH:mm")), fontWeight = FontWeight.Bold, fontSize = 17.sp)
                }
            }
        }
        Text(
            "روش محاسبه و موقعیت مکانی از تنظیمات «اوقات شرعی و اذان» کنترل می‌شود.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
    }
}

private data class MobileWeather(
    val temperature: Double,
    val apparent: Double,
    val humidity: Int,
    val wind: Double,
    val code: Int,
    val daily: List<DailyWeather>
)
private data class DailyWeather(val date: String, val code: Int, val max: Double, val min: Double)

@Composable
private fun WeatherPanel(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val settings = remember(context) { ZurvanPrayerTimes.settings(context) }
    var weather by remember { mutableStateOf<MobileWeather?>(null) }
    var error by remember { mutableStateOf("") }
    var loading by remember { mutableStateOf(true) }
    LaunchedEffect(settings.latitude, settings.longitude) {
        loading = true
        error = ""
        try {
            weather = withContext(Dispatchers.IO) {
                val url = URL(
                    "https://api.open-meteo.com/v1/forecast?latitude=${settings.latitude}&longitude=${settings.longitude}" +
                        "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m" +
                        "&daily=weather_code,temperature_2m_max,temperature_2m_min&forecast_days=7&timezone=auto"
                )
                val connection = (url.openConnection() as HttpURLConnection).apply {
                    connectTimeout = 10000
                    readTimeout = 10000
                    requestMethod = "GET"
                }
                try {
                    val json = JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
                    val current = json.getJSONObject("current")
                    val daily = json.getJSONObject("daily")
                    val dates = daily.getJSONArray("time")
                    val codes = daily.getJSONArray("weather_code")
                    val highs = daily.getJSONArray("temperature_2m_max")
                    val lows = daily.getJSONArray("temperature_2m_min")
                    MobileWeather(
                        current.getDouble("temperature_2m"),
                        current.getDouble("apparent_temperature"),
                        current.getInt("relative_humidity_2m"),
                        current.getDouble("wind_speed_10m"),
                        current.getInt("weather_code"),
                        (0 until dates.length()).map { i ->
                            DailyWeather(dates.getString(i), codes.getInt(i), highs.getDouble(i), lows.getDouble(i))
                        }
                    )
                } finally { connection.disconnect() }
            }
        } catch (ex: Exception) {
            error = "دریافت وضعیت هوا ممکن نشد. اتصال اینترنت و موقعیت شهر را بررسی کنید."
        } finally { loading = false }
    }
    Column(
        modifier.fillMaxWidth().padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Text("آب‌وهوا", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
        Text("پیش‌بینی برای ${settings.city}", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        when {
            loading -> Box(Modifier.fillMaxWidth().padding(40.dp), contentAlignment = Alignment.Center) {
                CircularProgressIndicator()
            }
            weather == null -> Text(error, color = MaterialTheme.colorScheme.error)
            else -> {
                val current = weather!!
                Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                    Column(Modifier.fillMaxWidth().padding(20.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(weatherLabel(current.code), style = MaterialTheme.typography.titleMedium)
                        Text(weatherEmoji(current.code), fontSize = 56.sp)
                        Text("${current.temperature.toInt()}°", style = MaterialTheme.typography.displayMedium, fontWeight = FontWeight.Bold)
                        Text("احساس واقعی ${current.apparent.toInt()}°  ·  رطوبت ${current.humidity}%  ·  باد ${current.wind.toInt()} km/h")
                    }
                }
                Text("پیش‌بینی هفت روز آینده", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                current.daily.forEach { day ->
                    Row(
                        Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(12.dp)).padding(12.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(day.date.takeLast(5))
                        Text(weatherEmoji(day.code), fontSize = 22.sp)
                        Text("${day.max.toInt()}° / ${day.min.toInt()}°", fontWeight = FontWeight.SemiBold)
                    }
                }
                Text("منبع: Open-Meteo", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

private fun weatherEmoji(code: Int): String = when (code) {
    0 -> "☀️"
    1, 2 -> "🌤️"
    3 -> "☁️"
    45, 48 -> "🌫️"
    in 51..67, in 80..82 -> "🌧️"
    in 71..77, 85, 86 -> "❄️"
    in 95..99 -> "⛈️"
    else -> "🌡️"
}
private fun weatherLabel(code: Int): String = when (code) {
    0 -> "آسمان صاف"
    1, 2 -> "کمی ابری"
    3 -> "ابری"
    45, 48 -> "مه‌آلود"
    in 51..67, in 80..82 -> "بارندگی"
    in 71..77, 85, 86 -> "بارش برف"
    in 95..99 -> "رعدوبرق"
    else -> "وضعیت جوی"
}
