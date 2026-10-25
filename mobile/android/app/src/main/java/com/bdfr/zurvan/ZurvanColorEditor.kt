package com.bdfr.zurvan

import android.graphics.Color as AndroidColor
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ContentCopy
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import kotlin.math.atan2
import kotlin.math.min
import kotlin.math.sqrt

private val targets = listOf(
    "surface" to "نوار بالا",
    "accent" to "دکمه‌ها",
    "card" to "کارت‌ها",
    "holiday" to "روز تعطیل",
    "background" to "پس‌زمینه",
    "ink" to "رنگ متن",
    "weekday0" to "شنبه", "weekday1" to "یکشنبه", "weekday2" to "دوشنبه",
    "weekday3" to "سه‌شنبه", "weekday4" to "چهارشنبه", "weekday5" to "پنجشنبه", "weekday6" to "جمعه"
)

private fun asHex(h: Float, s: Float, v: Float): String =
    String.format(java.util.Locale.US, "#%06X", 0xFFFFFF and AndroidColor.HSVToColor(floatArrayOf(h, s, v)))

@Composable
fun ZurvanColorEditor(
    initialKey: String,
    overrides: Map<String, String>,
    defaults: ZurvanPalette,
    weekdayDefaults: List<Color> = emptyList(),
    onApply: (String, String) -> Unit,
    onReset: (String) -> Unit,
    onDismiss: () -> Unit
) {
    var target by remember { mutableStateOf(initialKey) }
    fun fallback(key: String): Color = when (key) {
        in listOf("weekday0","weekday1","weekday2","weekday3","weekday4","weekday5","weekday6") -> weekdayDefaults.getOrNull(key.last().digitToInt()) ?: Color.White
        "surface" -> defaults.surface
        "accent" -> defaults.accent
        "card" -> defaults.card
        "holiday" -> defaults.holiday
        "background" -> defaults.background
        else -> defaults.ink
    }
    fun original(key: String): String {
        val argb = overrides[key]?.let { runCatching { AndroidColor.parseColor(it) }.getOrNull() }
            ?: fallback(key).let { AndroidColor.argb(255, (it.red * 255).toInt(), (it.green * 255).toInt(), (it.blue * 255).toInt()) }
        return String.format(java.util.Locale.US, "#%06X", argb and 0xFFFFFF)
    }
    var hue by remember { mutableFloatStateOf(0f) }
    var saturation by remember { mutableFloatStateOf(1f) }
    var brightness by remember { mutableFloatStateOf(1f) }
    fun load(key: String) {
        val hsv = FloatArray(3)
        AndroidColor.colorToHSV(AndroidColor.parseColor(original(key)), hsv)
        hue = hsv[0]; saturation = hsv[1]; brightness = hsv[2]
    }
    LaunchedEffect(target) { load(target) }
    val hex = asHex(hue, saturation, brightness)
    val chosen = Color(AndroidColor.HSVToColor(floatArrayOf(hue, saturation, brightness)))
    var typed by remember(target) { mutableStateOf("") }
    val context = LocalContext.current

    Dialog(onDismissRequest = onDismiss, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Surface(
            modifier = Modifier.fillMaxWidth(0.96f).fillMaxHeight(0.94f),
            shape = RoundedCornerShape(25.dp),
            color = Color(0xFFF8F7FA)
        ) {
            Column(
                modifier = Modifier.verticalScroll(rememberScrollState()).padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                Text("تنظیمات تم", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
                Surface(shape = RoundedCornerShape(20.dp), color = Color.White) {
                    Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        Text("انتخاب رنگ عناصر", fontWeight = FontWeight.Bold, fontSize = 18.sp)
                        Text("رنگ دلخواه خود را برای بخش‌های مختلف برنامه انتخاب کنید.", fontSize = 12.sp, color = Color.Gray)
                        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(7.dp)) {
                            targets.forEach { (key, title) ->
                                val selected = key == target
                                Surface(
                                    modifier = Modifier.clickable { target = key },
                                    color = if (selected) Color(0xFFFCE8EF) else Color(0xFFF4F4F7),
                                    shape = RoundedCornerShape(12.dp),
                                    border = androidx.compose.foundation.BorderStroke(if (selected) 2.dp else 1.dp,
                                        if (selected) chosen else Color.Transparent)
                                ) {
                                    Text(title, Modifier.padding(horizontal = 12.dp, vertical = 15.dp),
                                        color = if (selected) Color(0xFFB92D62) else Color(0xFF253045), fontSize = 12.sp)
                                }
                            }
                        }
                    }
                }
                Surface(shape = RoundedCornerShape(20.dp), color = Color.White) {
                    Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        Text("پیش‌نمایش", fontWeight = FontWeight.Bold, fontSize = 18.sp)
                        Text("تغییرات رنگ را در نمای واقعی برنامه مشاهده کنید.", fontSize = 12.sp, color = Color.Gray)
                        Surface(color = Color(0xFFFCE8EF), shape = RoundedCornerShape(12.dp)) {
                            Text("بخش در حال تغییر: " + targets.first { it.first == target }.second,
                                Modifier.padding(10.dp), fontSize = 12.sp, color = Color(0xFFA92258))
                        }
                        val surfaceColor = if (target == "surface") chosen else defaults.surface
                        val backgroundColor = if (target == "background") chosen else defaults.background
                        val cardColor = if (target == "card") chosen else defaults.card
                        val accentColor = if (target == "accent") chosen else defaults.accent
                        val holidayColor = if (target == "holiday") chosen else defaults.holiday
                        val inkColor = if (target == "ink") chosen else defaults.ink
                        Column(Modifier.fillMaxWidth().border(1.dp, Color(0xFFE9E4E9), RoundedCornerShape(15.dp))
                            .background(backgroundColor, RoundedCornerShape(15.dp))) {
                            Row(Modifier.fillMaxWidth().background(surfaceColor).padding(13.dp),
                                horizontalArrangement = Arrangement.SpaceBetween) {
                                Text("تقویم زروان", color = inkColor, fontWeight = FontWeight.Bold)
                                Text("☰    ◷", color = inkColor)
                            }
                            Row(Modifier.fillMaxWidth().padding(12.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                                Text("فروردین ۱۴۰۳", color = inkColor, fontWeight = FontWeight.Bold)
                                Text("‹   ›", color = inkColor)
                            }
                            Row(Modifier.fillMaxWidth().padding(horizontal = 10.dp),
                                horizontalArrangement = Arrangement.SpaceEvenly) {
                                listOf("شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه").forEach {
                                    Text(it, color = inkColor, fontSize = 8.sp)
                                }
                            }
                            Row(Modifier.fillMaxWidth().padding(10.dp), horizontalArrangement = Arrangement.SpaceEvenly) {
                                listOf("۲۵", "۲۶", "۲۷", "۲۸", "۲۹", "۳۰", "۱").forEachIndexed { i, day ->
                                    Text(day, Modifier.background(if (target == "weekday$i") chosen else weekdayDefaults.getOrNull(i) ?: cardColor,
                                        RoundedCornerShape(6.dp)).padding(6.dp), color = inkColor, fontSize = 12.sp)
                                }
                            }
                            Row(Modifier.fillMaxWidth().padding(10.dp), horizontalArrangement = Arrangement.End) {
                                Surface(shape = RoundedCornerShape(12.dp), color = accentColor) {
                                    Text("+ رویداد جدید", Modifier.padding(12.dp), color = Color.White, fontSize = 12.sp)
                                }
                            }
                            Surface(Modifier.fillMaxWidth().padding(10.dp), shape = RoundedCornerShape(12.dp), color = cardColor) {
                                Text("جلسه تیم طراحی      ۱۰:۰۰ - ۱۱:۳۰", Modifier.padding(12.dp), color = inkColor, fontSize = 12.sp)
                            }
                        }
                    }
                }
                Surface(shape = RoundedCornerShape(20.dp), color = Color.White) {
                    Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        Text("رنگ انتخاب‌شده", fontWeight = FontWeight.SemiBold)
                        // Stack controls on phones: a side-by-side layout makes the wheel
                        // compete with the HEX field and clips the interaction surface.
                        Column(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(10.dp)
                        ) {
                            ZurvanHueWheel(
                                hue, saturation, brightness,
                                { hue = it },
                                { valueS, valueV -> saturation = valueS; brightness = valueV },
                                Modifier.size(216.dp)
                            )
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Box(Modifier.size(48.dp).background(chosen, RoundedCornerShape(10.dp)))
                                OutlinedTextField(
                                    value = typed.ifEmpty { hex },
                                    onValueChange = { input ->
                                        typed = input.take(7).uppercase()
                                        if (Regex("^#[0-9A-Fa-f]{6}$").matches(typed)) {
                                            val hsv = FloatArray(3)
                                            AndroidColor.colorToHSV(AndroidColor.parseColor(typed), hsv)
                                            hue = hsv[0]; saturation = hsv[1]; brightness = hsv[2]
                                        }
                                    },
                                    label = { Text("HEX") },
                                    singleLine = true,
                                    modifier = Modifier.weight(1f),
                                    textStyle = androidx.compose.ui.text.TextStyle(fontSize = 14.sp),
                                    keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters)
                                )
                                IconButton(onClick = {
                                    val clipboard = context.getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
                                    clipboard.setPrimaryClip(android.content.ClipData.newPlainText("Color HEX", hex))
                                }) {
                                    Icon(Icons.Outlined.ContentCopy, contentDescription = "کپی کد HEX")
                                }
                            }
                            Text(
                                "H: ${hue.toInt()}°     S: ${(saturation * 100).toInt()}%     V: ${(brightness * 100).toInt()}%",
                                fontSize = 12.sp, color = Color.DarkGray
                            )
                        }
                    }
                }
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    OutlinedButton(onClick = { onReset(target); load(target) }, modifier = Modifier.weight(1f)) {
                        Text("بازنشانی")
                    }
                    Button(onClick = { onApply(target, hex); onDismiss() }, modifier = Modifier.weight(1f),
                        colors = ButtonDefaults.buttonColors(containerColor = chosen)) {
                        Text("اعمال", color = if (brightness < 0.5f) Color.White else Color.Black)
                    }
                }
            }
        }
    }
}

@Composable
private fun ZurvanHueWheel(
    hue: Float, saturation: Float, brightness: Float,
    onHue: (Float) -> Unit, onSV: (Float, Float) -> Unit, modifier: Modifier = Modifier
) {
    val hueStops = listOf(Color.Red, Color.Magenta, Color.Blue, Color.Cyan, Color.Green, Color.Yellow, Color.Red)
    fun handle(position: Offset, width: Float, height: Float) {
        val center = Offset(width / 2f, height / 2f)
        val size = min(width, height)
        val dx = position.x - center.x
        val dy = position.y - center.y
        val distance = sqrt(dx * dx + dy * dy)
        val squareSide = size * 0.45f
        if (distance >= size * 0.32f) {
            val degrees = (Math.toDegrees(atan2(dy.toDouble(), dx.toDouble())).toFloat() + 90f + 360f) % 360f
            onHue(degrees)
        } else if (kotlin.math.abs(dx) <= squareSide / 2 && kotlin.math.abs(dy) <= squareSide / 2) {
            onSV(((dx / squareSide) + 0.5f).coerceIn(0f, 1f),
                (0.5f - dy / squareSide).coerceIn(0f, 1f))
        }
    }
    Canvas(modifier
        .pointerInput(Unit) { detectTapGestures { handle(it, size.width.toFloat(), size.height.toFloat()) } }
        .pointerInput(Unit) { detectDragGestures(onDragStart = {
            handle(it, size.width.toFloat(), size.height.toFloat())
        }, onDrag = { change, _ ->
            handle(change.position, size.width.toFloat(), size.height.toFloat())
            change.consume()
        }) }
    ) {
        val side = min(size.width, size.height)
        val center = Offset(size.width / 2f, size.height / 2f)
        val radius = side * 0.43f
        val ringWidth = side * 0.14f
        drawCircle(Brush.sweepGradient(hueStops, center = center), radius = radius,
            center = center, style = Stroke(width = ringWidth, cap = StrokeCap.Butt))
        val radians = Math.toRadians((hue - 90f).toDouble())
        val ringPointer = Offset(center.x + kotlin.math.cos(radians).toFloat() * radius,
            center.y + kotlin.math.sin(radians).toFloat() * radius)
        drawCircle(Color.White, radius = ringWidth * 0.43f, center = ringPointer, style = Stroke(3.dp.toPx()))
        val square = side * 0.45f
        val topLeft = Offset(center.x - square / 2, center.y - square / 2)
        val pure = Color(AndroidColor.HSVToColor(floatArrayOf(hue, 1f, 1f)))
        drawRect(Brush.horizontalGradient(listOf(Color.White, pure), topLeft.x, topLeft.x + square),
            topLeft = topLeft, size = Size(square, square))
        drawRect(Brush.verticalGradient(listOf(Color.Transparent, Color.Black), topLeft.y, topLeft.y + square),
            topLeft = topLeft, size = Size(square, square))
        val selected = Offset(topLeft.x + saturation * square, topLeft.y + (1f - brightness) * square)
        drawCircle(Color.White, radius = 6.dp.toPx(), center = selected, style = Stroke(2.dp.toPx()))
        drawCircle(Color.DarkGray, radius = 7.7.dp.toPx(), center = selected, style = Stroke(0.8.dp.toPx()))
    }
}
