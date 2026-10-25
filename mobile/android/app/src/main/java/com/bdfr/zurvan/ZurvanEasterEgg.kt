package com.bdfr.zurvan

import android.os.SystemClock
import androidx.compose.foundation.Canvas
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import kotlin.math.cos
import kotlin.math.sin

/** Discreet trigger: 24 taps on the app title, with a short inactivity reset. */
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
            if (taps >= 24) {
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

/** Original vector illustration rendered locally; no external image downloads. */
@Composable
private fun ZurvanTimeIllustration(modifier: Modifier = Modifier) {
    val gold = Color(0xFFD6A64D)
    val blue = Color(0xFF5D6BA6)
    Canvas(modifier) {
        val center = Offset(size.width / 2f, size.height / 2f)
        val radius = minOf(size.width, size.height) * .39f
        drawCircle(color = blue.copy(alpha = .09f), radius = radius * 1.15f, center = center)
        drawCircle(color = gold, radius = radius, center = center, style = Stroke(width = 5f))
        drawCircle(color = blue, radius = radius * .82f, center = center, style = Stroke(width = 2f))
        for (index in 0 until 12) {
            val angle = (index * 30.0 - 90.0) * Math.PI / 180.0
            val inner = radius * .86f
            val outer = radius * .96f
            drawLine(
                gold,
                Offset(center.x + cos(angle).toFloat() * inner, center.y + sin(angle).toFloat() * inner),
                Offset(center.x + cos(angle).toFloat() * outer, center.y + sin(angle).toFloat() * outer),
                strokeWidth = 4f, cap = StrokeCap.Round
            )
        }
        drawLine(blue, center,
            Offset(center.x + radius * .43f, center.y - radius * .39f), 7f, cap = StrokeCap.Round)
        drawLine(gold, center,
            Offset(center.x - radius * .15f, center.y - radius * .55f), 6f, cap = StrokeCap.Round)
        drawCircle(gold, radius = 7f, center = center)
        drawCircle(color = gold.copy(alpha = .6f), radius = radius * .13f,
            center = Offset(center.x + radius * 1.12f, center.y - radius * .95f))
    }
}
