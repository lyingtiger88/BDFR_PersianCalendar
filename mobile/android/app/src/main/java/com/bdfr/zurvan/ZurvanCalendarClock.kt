package com.bdfr.zurvan

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.graphics.drawscope.drawIntoCanvas
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import java.time.LocalTime
import kotlin.math.cos
import kotlin.math.sin

/**
 * A vector-drawn, twelve-petal clock face. The original approved artwork
 * should replace these procedural petals after its vector asset is checked in.
 */
@Composable
fun ZurvanCalendarClock(modifier: Modifier = Modifier) {
    var digital by rememberSaveable { mutableStateOf(false) }
    var now by remember { mutableStateOf(LocalTime.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            now = LocalTime.now()
            delay(1000)
        }
    }
    val foreground = MaterialTheme.colorScheme.onSurface
    val accent = MaterialTheme.colorScheme.primary
    Column(modifier = modifier, horizontalAlignment = Alignment.CenterHorizontally) {
        TextButton(onClick = { digital = !digital }) {
            Text(if (digital) "ساعت دیجیتال · تغییر به آنالوگ" else "ساعت آنالوگ · تغییر به دیجیتال")
        }
        if (digital) {
            Text(
                text = "%02d:%02d:%02d".format(now.hour, now.minute, now.second),
                style = MaterialTheme.typography.headlineMedium,
                color = foreground
            )
        } else {
            Canvas(Modifier.size(132.dp)) {
                val c = Offset(size.width / 2f, size.height / 2f)
                val r = size.minDimension * .46f
                // Twelve separate petals, all drawn in scalable vector geometry.
                repeat(12) { i ->
                    rotate(i * 30f, pivot = c) {
                        drawOval(
                            color = accent.copy(alpha = .16f),
                            topLeft = Offset(c.x - r * .18f, c.y - r),
                            size = Size(r * .36f, r * .88f)
                        )
                    }
                }
                drawCircle(color = foreground.copy(alpha = .4f), radius = r * .72f, center = c, style = androidx.compose.ui.graphics.drawscope.Stroke(width = 1.5.dp.toPx()))
                val paint = android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG).apply {
                    color = foreground.toArgb()
                    textSize = 10.sp.toPx()
                    textAlign = android.graphics.Paint.Align.CENTER
                }
                drawIntoCanvas { canvas ->
                    repeat(12) { i ->
                        val angle = Math.toRadians(i * 30.0 - 90.0)
                        val x = c.x + cos(angle).toFloat() * r * .60f
                        val y = c.y + sin(angle).toFloat() * r * .60f + paint.textSize * .34f
                        canvas.nativeCanvas.drawText(((i + 11) % 12 + 1).toString(), x, y, paint)
                    }
                }
                fun hand(angle: Double, length: Float, thickness: Float, second: Boolean = false) {
                    val a = Math.toRadians(angle - 90.0)
                    drawLine(
                        color = if (second) accent else foreground,
                        start = c,
                        end = Offset(c.x + cos(a).toFloat() * length, c.y + sin(a).toFloat() * length),
                        strokeWidth = thickness
                    )
                }
                hand((now.hour % 12 + now.minute / 60.0) * 30.0, r * .34f, 4.dp.toPx())
                hand((now.minute + now.second / 60.0) * 6.0, r * .48f, 2.6.dp.toPx())
                hand(now.second * 6.0, r * .57f, 1.dp.toPx(), second = true)
                drawCircle(accent, 3.dp.toPx(), c)
            }
        }
    }
}
