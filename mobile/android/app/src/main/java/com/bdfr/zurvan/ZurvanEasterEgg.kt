package com.bdfr.zurvan

import android.os.SystemClock
import android.content.Context
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.border
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.cos
import kotlin.math.sin
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.Locale
import kotlinx.coroutines.delay

/** Discreet trigger: 7 taps on the app title, with a short inactivity reset. */
@Composable
fun ZurvanSecretTitle() {
    var taps by remember { mutableIntStateOf(0) }
    var lastTapAt by remember { mutableLongStateOf(0L) }
    var showStory by remember { mutableStateOf(false) }
    Text(
        "Zurvan | زروان",
        modifier = Modifier.clickable {
            val now = SystemClock.elapsedRealtime()
            taps = if (now - lastTapAt > 8000L) 1 else taps + 1
            lastTapAt = now
            if (taps >= 7) {
                taps = 0
                showStory = true
            }
        },
        style = MaterialTheme.typography.titleLarge,
        fontWeight = FontWeight.SemiBold
    )
    if (showStory) {
        AlertDialog(
            onDismissRequest = { showStory = false },
            title = { Text("فلسفه نام زروان", fontWeight = FontWeight.Bold) },
            text = {
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    ZurvanTimeIllustration(Modifier.fillMaxWidth().height(186.dp))
                    Text("زُروان، نماد زمان در اندیشه ایران باستان",
                        style = MaterialTheme.typography.titleMedium,
                        textAlign = TextAlign.Center)
                    Text(
                        "زروان در سنت‌های کهن ایرانی با مفهوم زمان پیوند دارد؛ در روایت‌های زروانی، «زروان اکرانه» به معنای زمان بی‌کران است. " +
                        "نام تقویم زروان یادآور گذر روزها، گردش فصل‌ها و پیوند گذشته، اکنون و آینده است.",
                        style = MaterialTheme.typography.bodyMedium,
                        textAlign = TextAlign.Center
                    )
                    Text("زمان، حافظه زندگی است.", fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.primary)
                }
            },
            confirmButton = {
                TextButton(onClick = { showStory = false }) { Text("بستن") }
            },
            shape = RoundedCornerShape(24.dp)
        )
    }
}

/** Twelve-point Persian interlace emblem with a live three-hand analog clock. */
@Composable
private fun ZurvanTimeIllustration(modifier: Modifier = Modifier) {
    val ink = Color(0xFF111111)
    val gold = Color(0xFFD6A64D)
    val secondsColor = Color(0xFFB63D35)
    val context = LocalContext.current
    val settings = remember(context) { context.getSharedPreferences("zurvan_clock", Context.MODE_PRIVATE) }
    var clockMode by remember { mutableStateOf(settings.getString("mode", "analog") ?: "analog") }
    var clockStyle by remember { mutableStateOf(settings.getString("digital_style", "classic") ?: "classic") }
    var twentyFourHours by remember { mutableStateOf(settings.getBoolean("24_hour", true)) }
    var showSeconds by remember { mutableStateOf(settings.getBoolean("show_seconds", true)) }
    DisposableEffect(settings) {
        val listener = android.content.SharedPreferences.OnSharedPreferenceChangeListener { _, key ->
            when (key) {
                "mode" -> clockMode = settings.getString("mode", "analog") ?: "analog"
                "digital_style" -> clockStyle = settings.getString("digital_style", "classic") ?: "classic"
                "24_hour" -> twentyFourHours = settings.getBoolean("24_hour", true)
                "show_seconds" -> showSeconds = settings.getBoolean("show_seconds", true)
            }
        }
        settings.registerOnSharedPreferenceChangeListener(listener)
        onDispose { settings.unregisterOnSharedPreferenceChangeListener(listener) }
    }
    var time by remember { mutableStateOf(LocalTime.now()) }

    LaunchedEffect(Unit) {
        while (true) {
            time = LocalTime.now()
            delay(1000L)
        }
    }

    Box(modifier = modifier, contentAlignment = Alignment.Center) {
        Image(
            painter = painterResource(id = R.drawable.zurvan_clock_body),
            contentDescription = "بدنه ساعت با نقش گره هندسی دوازده‌پر",
            modifier = Modifier.fillMaxSize(),
            contentScale = ContentScale.Fit
        )
        if (clockMode == "digital") {
            val formatter = remember(twentyFourHours, showSeconds) {
                DateTimeFormatter.ofPattern(
                    if (twentyFourHours) (if (showSeconds) "HH:mm:ss" else "HH:mm")
                    else (if (showSeconds) "hh:mm:ss a" else "hh:mm a"),
                    Locale.ENGLISH
                )
            }
            val accent = when (clockStyle) {
                "modern" -> Color(0xFF67E5F4)
                "dots" -> Color(0xFFE8D8FB)
                "flip" -> Color(0xFFFFFFFF)
                else -> Color(0xFFF5D28D)
            }
            val background = when (clockStyle) {
                "flip" -> Color(0xFF272B3A)
                "modern" -> Color(0xFF092B3A)
                "dots" -> Color(0xFF261E3D)
                else -> Color(0xFF201A31)
            }
            Box(
                Modifier.fillMaxWidth(.77f)
                    .background(background, RoundedCornerShape(14.dp))
                    .border(2.dp, gold.copy(alpha = .7f), RoundedCornerShape(14.dp))
                    .padding(vertical = 10.dp, horizontal = 6.dp),
                contentAlignment = Alignment.Center
            ) {
                Text(
                    time.format(formatter),
                    fontSize = if (showSeconds) 23.sp else 29.sp,
                    color = accent,
                    fontWeight = FontWeight.Bold,
                    maxLines = 1,
                    textAlign = TextAlign.Center
                )
            }
        } else Canvas(Modifier.fillMaxSize()) {
            val center = Offset(size.width / 2f, size.height / 2f)
            val radius = minOf(size.width, size.height) * .47f

            fun hand(angleDegrees: Float, length: Float, color: Color, width: Float,
                     tail: Float = 0f) {
                val angle = (angleDegrees - 90f) * Math.PI / 180.0
                val directionX = cos(angle).toFloat()
                val directionY = sin(angle).toFloat()
                val from = Offset(
                    center.x - directionX * radius * tail,
                    center.y - directionY * radius * tail
                )
                val to = Offset(
                    center.x + directionX * radius * length,
                    center.y + directionY * radius * length
                )
                drawLine(color, from, to, strokeWidth = width, cap = StrokeCap.Round)
            }

            val hourAngle = ((time.hour % 12) * 30f) + time.minute * .5f + time.second / 120f
            val minuteAngle = time.minute * 6f + time.second * .1f
            val secondAngle = time.second * 6f

            // Three distinct hands: short hour, long minute, fine red seconds.
            hand(hourAngle, .27f, ink, 6.5f)
            hand(minuteAngle, .39f, gold, 4.5f)
            hand(secondAngle, .46f, secondsColor, 1.9f, tail = .09f)

            drawCircle(Color.White, radius = 7.2f, center = center)
            drawCircle(ink, radius = 5.3f, center = center)
            drawCircle(gold, radius = 2.4f, center = center)
        }
    }
}
