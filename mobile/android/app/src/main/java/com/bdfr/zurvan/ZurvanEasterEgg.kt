package com.bdfr.zurvan

import android.os.SystemClock
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import kotlin.math.cos
import kotlin.math.sin
import java.time.LocalTime
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
        Canvas(Modifier.fillMaxSize()) {
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
