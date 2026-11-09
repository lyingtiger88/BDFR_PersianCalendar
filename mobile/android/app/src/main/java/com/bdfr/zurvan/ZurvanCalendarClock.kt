package com.bdfr.zurvan

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
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

private enum class ZurvanNumeralStyle(val title: String) {
    KHWARAZMI("خوارزمی"), PAHLAVI("پهلوی"), CUNEIFORM("میخی"), ROMAN("رومی")
}

private fun clockNumerals(style: ZurvanNumeralStyle): List<String> = when (style) {
    ZurvanNumeralStyle.ROMAN -> listOf("XII","I","II","III","IV","V","VI","VII","VIII","IX","X","XI")
    ZurvanNumeralStyle.PAHLAVI -> listOf("𐭠","𐭡","𐭢","𐭣","𐭤","𐭥","𐭦","𐭧","𐭨","𐭩","𐭪","𐭫")
    ZurvanNumeralStyle.CUNEIFORM -> listOf("𒐕","𒐖","𒐗","𒐘","𒐙","𒐚","𒐛","𒐜","𒐝","𒐞","𒐟","𒐠")
    ZurvanNumeralStyle.KHWARAZMI -> listOf("۱۲","۱","۲","۳","۴","۵","۶","۷","۸","۹","۱۰","۱۱")
}

@Composable
fun ZurvanCalendarClock(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val clockPrefs = remember(context) { context.getSharedPreferences("zurvan_clock_preferences", android.content.Context.MODE_PRIVATE) }
    var digital by rememberSaveable { mutableStateOf(clockPrefs.getBoolean("digital", false)) }
    var numeralStyle by rememberSaveable {
        mutableStateOf(
            ZurvanNumeralStyle.entries.firstOrNull { it.name == clockPrefs.getString("numeral_style", "KHWARAZMI") }
                ?: ZurvanNumeralStyle.KHWARAZMI
        )
    }
    var styleMenu by remember { mutableStateOf(false) }
    var now by remember { mutableStateOf(LocalTime.now()) }

    LaunchedEffect(Unit) {
        while (true) {
            now = LocalTime.now()
            delay(250)
        }
    }

    val foreground = MaterialTheme.colorScheme.onSurface
    val accent = MaterialTheme.colorScheme.primary
    Column(modifier = modifier, horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            TextButton(onClick = {
                digital = !digital
                clockPrefs.edit().putBoolean("digital", digital).apply()
            }) {
                Text(if (digital) "نمایش آنالوگ" else "نمایش دیجیتال")
            }
            Box {
                TextButton(onClick = { styleMenu = true }) { Text(numeralStyle.title + " ▾") }
                DropdownMenu(expanded = styleMenu, onDismissRequest = { styleMenu = false }) {
                    ZurvanNumeralStyle.entries.forEach { style ->
                        DropdownMenuItem(
                            text = { Text(style.title) },
                            onClick = {
                                numeralStyle = style
                                clockPrefs.edit().putString("numeral_style", style.name).apply()
                                styleMenu = false
                            }
                        )
                    }
                }
            }
        }
        if (digital) {
            Text(
                text = "%02d:%02d:%02d".format(now.hour, now.minute, now.second),
                style = MaterialTheme.typography.headlineMedium,
                color = foreground,
                modifier = Modifier.padding(vertical = 18.dp)
            )
        } else {
            Canvas(Modifier.size(204.dp)) {
                val c = Offset(size.width / 2f, size.height / 2f)
                val r = size.minDimension * .46f

                // Twelve-petal radial body, kept independent from the hands and numerals.
                val petal = Path().apply {
                    moveTo(c.x, c.y - r * .12f)
                    cubicTo(c.x - r * .19f, c.y - r * .32f, c.x - r * .20f, c.y - r * .70f, c.x, c.y - r * .96f)
                    cubicTo(c.x + r * .20f, c.y - r * .70f, c.x + r * .19f, c.y - r * .32f, c.x, c.y - r * .12f)
                    close()
                }
                repeat(12) { i ->
                    rotate(i * 30f, pivot = c) {
                        drawPath(petal, color = accent.copy(alpha = .22f))
                        drawPath(petal, color = foreground.copy(alpha = .55f), style = Stroke(width = 1.dp.toPx()))
                    }
                }
                drawCircle(foreground.copy(alpha = .62f), radius = r * .79f, center = c, style = Stroke(width = 1.4.dp.toPx()))
                drawCircle(accent.copy(alpha = .45f), radius = r * .96f, center = c, style = Stroke(width = .8.dp.toPx()))

                val paint = android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG).apply {
                    color = foreground.toArgb()
                    textSize = if (numeralStyle == ZurvanNumeralStyle.ROMAN) 10.sp.toPx() else 12.sp.toPx()
                    textAlign = android.graphics.Paint.Align.CENTER
                    typeface = android.graphics.Typeface.create("serif", android.graphics.Typeface.NORMAL)
                }
                drawIntoCanvas { canvas ->
                    clockNumerals(numeralStyle).forEachIndexed { i, value ->
                        val angle = Math.toRadians(i * 30.0 - 90.0)
                        val x = c.x + cos(angle).toFloat() * r * .68f
                        val y = c.y + sin(angle).toFloat() * r * .68f + paint.textSize * .34f
                        canvas.nativeCanvas.drawText(value, x, y, paint)
                    }
                }

                fun hand(angle: Double, length: Float, thickness: Float, color: androidx.compose.ui.graphics.Color) {
                    val a = Math.toRadians(angle - 90.0)
                    drawLine(
                        color = color,
                        start = c,
                        end = Offset(c.x + cos(a).toFloat() * length, c.y + sin(a).toFloat() * length),
                        strokeWidth = thickness,
                        cap = androidx.compose.ui.graphics.StrokeCap.Round
                    )
                }
                hand((now.hour % 12 + now.minute / 60.0) * 30.0, r * .38f, 5.dp.toPx(), foreground)
                hand((now.minute + now.second / 60.0) * 6.0, r * .55f, 3.dp.toPx(), foreground)
                hand((now.second + now.nano / 1_000_000_000.0) * 6.0, r * .64f, 1.2.dp.toPx(), accent)
                drawCircle(accent, 4.dp.toPx(), c)
            }
        }
    }
}
