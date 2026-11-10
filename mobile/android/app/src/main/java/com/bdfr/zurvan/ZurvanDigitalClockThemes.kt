package com.bdfr.zurvan

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import java.time.LocalDate
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/** Six independent visual themes for the Zurvan digital clock. */
enum class ZurvanDigitalClockTheme(val title: String) {
    WOODEN_AMBER("چوبی کهربایی"),
    MINIMAL_SILVER("مینیمال نقره‌ای"),
    ART_DECO_EMERALD("آرت‌دکو زمردی"),
    COSMIC_GOLD("کیهانی طلایی"),
    NEON_GLASS("نئون شیشه‌ای"),
    PERSIAN_GEOMETRIC("هندسی ایرانی")
}

private data class DigitalPalette(
    val frame: Color,
    val body: Color,
    val bodyEnd: Color,
    val digits: Color,
    val secondary: Color,
    val ornament: String,
    val border: Color
)

private fun digitalPalette(theme: ZurvanDigitalClockTheme): DigitalPalette = when (theme) {
    ZurvanDigitalClockTheme.WOODEN_AMBER ->
        DigitalPalette(Color(0xFFB77732), Color(0xFF3C1B12), Color(0xFF7F4320), Color(0xFFFFB642), Color(0xFFFFDBA1), "❀", Color(0xFFFFB15E))
    ZurvanDigitalClockTheme.MINIMAL_SILVER ->
        DigitalPalette(Color(0xFFBDC8D4), Color(0xFF2A3748), Color(0xFF465367), Color.White, Color(0xFFDCE6F4), "—", Color(0xFFB1DDFE))
    ZurvanDigitalClockTheme.ART_DECO_EMERALD ->
        DigitalPalette(Color(0xFFD0A34E), Color(0xFF002F2A), Color(0xFF07584F), Color(0xFFFFE6AA), Color(0xFFFFD690), "◆", Color(0xFFD4AC5C))
    ZurvanDigitalClockTheme.COSMIC_GOLD ->
        DigitalPalette(Color(0xFFE5B957), Color(0xFF080E2D), Color(0xFF263760), Color(0xFFFFCB68), Color(0xFFFFDDA6), "✦  ☾  ✧", Color(0xFFFFC96B))
    ZurvanDigitalClockTheme.NEON_GLASS ->
        DigitalPalette(Color(0xFF7D67E8), Color(0xFF111B49), Color(0xFF2D2063), Color(0xFF65E8FF), Color(0xFFE0BBFF), "◌", Color(0xFF66E7FF))
    ZurvanDigitalClockTheme.PERSIAN_GEOMETRIC ->
        DigitalPalette(Color(0xFF087F9F), Color(0xFF051B3C), Color(0xFF103D60), Color(0xFFD9F6FF), Color(0xFFFFDBA6), "❖  ✺  ❖", Color(0xFFCFA858))
}

/** The clock and date remain real text, rather than being baked into a static image. */
@Composable
fun ZurvanDigitalClockFace(
    theme: ZurvanDigitalClockTheme,
    now: LocalTime,
    modifier: Modifier = Modifier
) {
    val p = digitalPalette(theme)
    val clock = now.format(DateTimeFormatter.ofPattern("HH:mm", Locale.ROOT))
    val seconds = now.format(DateTimeFormatter.ofPattern("ss", Locale.ROOT))
    val persianDate = runCatching {
        val calendar = android.icu.util.PersianCalendar()
        val monthNames = arrayOf("فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند")
        "${calendar.get(android.icu.util.Calendar.DAY_OF_MONTH)} ${monthNames[calendar.get(android.icu.util.Calendar.MONTH)]} ${calendar.get(android.icu.util.Calendar.YEAR)}"
    }.getOrDefault(LocalDate.now().toString())
    Box(
        modifier = modifier
            .padding(4.dp)
            .shadow(7.dp, CircleShape)
            .background(
                Brush.linearGradient(listOf(p.frame, p.border, p.frame)),
                CircleShape
            )
            .padding(5.dp)
            .clip(CircleShape)
            .background(
                Brush.linearGradient(listOf(p.body, p.bodyEnd, p.body)),
                CircleShape
            ),
        contentAlignment = Alignment.Center
    ) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 18.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(6.dp)
        ) {
            Text(p.ornament, color = p.secondary, fontSize = 22.sp, maxLines = 1)
            Text(
                text = clock,
                color = p.digits,
                fontSize = 44.sp,
                fontWeight = FontWeight.Bold,
                fontFamily = FontFamily.Monospace,
                textAlign = TextAlign.Center,
                maxLines = 1
            )
            Text(
                text = seconds + " ثانیه",
                color = p.secondary,
                style = MaterialTheme.typography.labelLarge,
                fontFamily = FontFamily.Monospace
            )
            HorizontalDivider(
                modifier = Modifier.padding(horizontal = 30.dp),
                color = p.border.copy(alpha = 0.6f),
                thickness = 1.dp
            )
            Text(
                text = persianDate,
                color = p.secondary,
                fontSize = 14.sp,
                textAlign = TextAlign.Center
            )
            Text(theme.title, color = p.secondary.copy(alpha = 0.84f),
                fontSize = 10.sp, textAlign = TextAlign.Center)
        }
    }
}
