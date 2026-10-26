package com.bdfr.zurvan

import android.content.Context
import androidx.compose.ui.platform.LocalContext
import androidx.compose.foundation.Image
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.graphics.asImageBitmap
import android.graphics.BitmapFactory
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Brush
import androidx.compose.foundation.background

data class ZurvanPalette(val id: String, val label: String, val background: Color,
    val surface: Color, val card: Color, val ink: Color, val accent: Color, val holiday: Color)

object ZurvanThemes {
    val presets = listOf(
        ZurvanPalette("windows-light","Windows Light",Color(0xFFF5F6F8),Color.White,Color.White,Color(0xFF202631),Color(0xFF9DC7F5),Color(0xFFD34E5E)),
        ZurvanPalette("liquid-glass","Liquid Glass · شیشه مایع",Color(0xFFF0F5FA),Color(0xDDF8FCFF),Color(0xB8F5FAFF),Color(0xFF152D44),Color(0xFF598FC9),Color(0xFFB73C55)),
        ZurvanPalette("azure-glass","Azure Glass",Color(0xFFEAF3FB),Color(0xFFF5FAFF),Color(0xFFEAF4FF),Color(0xFF17324D),Color(0xFF5DA9E8),Color(0xFFD25162)),
        ZurvanPalette("graphite-night","Graphite Night",Color(0xFF171A1F),Color(0xFF22262D),Color(0xFF2A2F37),Color(0xFFF1F5F9),Color(0xFF586D88),Color(0xFFFF7A86)),
        ZurvanPalette("warm-sand","Warm Sand",Color(0xFFFFF8F3),Color(0xFFFFF9F0),Color(0xFFFFFDF8),Color(0xFF40362C),Color(0xFFDDB78B),Color(0xFFC95C57)),
        ZurvanPalette("zara-pastel","Zara Pastel",Color(0xFFFDF9F6),Color.White,Color.White,Color(0xFF1E2A44),Color(0xFF7CC9F5),Color(0xFFD65A6F)),
        ZurvanPalette("season-vibe","Season Vibe",Color(0xFFE7F1FB),Color(0xFFF1F7FC),Color(0xFFE4F0FC),Color(0xFF19304A),Color(0xFF6AA4D8),Color(0xFFCE4452)),
        ZurvanPalette("elena-mode","Elena Mode",Color(0xFFFFF5EA),Color(0xFFFFF9F2),Color(0xFFF6E8DA),Color(0xFF523D34),Color(0xFFC08A61),Color(0xFFC35159))
    )
    val active = staticCompositionLocalOf { presets.first() }
    val setActive = staticCompositionLocalOf<(String) -> Unit> { {} }
    val customColors = staticCompositionLocalOf<Map<String, String>> { emptyMap() }
    val setCustomColor = staticCompositionLocalOf<(String, String?) -> Unit> { { _, _ -> } }
    val weekdayColors = staticCompositionLocalOf<List<Color>> { emptyList() }
    val setWeekdayColor = staticCompositionLocalOf<(Int, String?) -> Unit> { { _, _ -> } }
    val wallpaper = staticCompositionLocalOf<String?> { null }
    val setWallpaper = staticCompositionLocalOf<(String?) -> Unit> { {} }
    val refreshAppearance = staticCompositionLocalOf<() -> Unit> { {} }
    val elenaImage = staticCompositionLocalOf<Int> { 0 }
    val setElenaImage = staticCompositionLocalOf<(Int) -> Unit> { {} }

    private val zaraWeekdays = listOf(
        "#F8DDD4", "#FFF0C5", "#DFF0DD", "#D5EFED", "#DCE9FB", "#E8E0FA", "#F8DDE7"
    )
    private val neutralWeekdays = List(7) { "#FFFFFF" }

    @Composable
    fun Apply(context: Context, content: @Composable () -> Unit) {
        val prefs = remember { context.getSharedPreferences("zurvan_appearance", Context.MODE_PRIVATE) }
        var selected by remember { mutableStateOf(prefs.getString("theme", "windows-light") ?: "windows-light") }
        var wallpaperPath by remember { mutableStateOf(prefs.getString("wallpaper", null)) }
        var appearanceRevision by remember { mutableIntStateOf(0) }
        var elenaImageIndex by remember { mutableIntStateOf(prefs.getInt("elena_picture", 0).coerceIn(0, 11)) }
        var overrides by remember(selected, appearanceRevision) {
            mutableStateOf(listOf("background","surface","card","ink","accent","holiday").mapNotNull { key ->
                prefs.getString("color.$selected.$key", null)?.let { key to it }
            }.toMap())
        }
        var dayOverrides by remember(selected, appearanceRevision) {
            mutableStateOf((0..6).mapNotNull { index ->
                prefs.getString("weekday.$selected.$index", null)?.let { index to it }
            }.toMap())
        }
        fun color(key: String, fallback: Color): Color =
            overrides[key]?.let { runCatching { Color(android.graphics.Color.parseColor(it)) }.getOrNull() } ?: fallback
        val season = when (AndroidCalendarEngine.currentPersianYearMonth().second) {
            in 1..3 -> 0
            in 4..6 -> 1
            in 7..9 -> 2
            else -> 3
        }
        val seasonalPalette = listOf(
            ZurvanPalette("season-vibe","Season Vibe",Color(0xFFE7F5E8),Color(0xFFF6FCF4),Color(0xFFE8F5DF),Color(0xFF254630),Color(0xFF66AD79),Color(0xFFC74652)),
            ZurvanPalette("season-vibe","Season Vibe",Color(0xFFDFF7FC),Color(0xFFF2FCFF),Color(0xFFE1F9F9),Color(0xFF17465D),Color(0xFF29B6B8),Color(0xFFD34E5E)),
            ZurvanPalette("season-vibe","Season Vibe",Color(0xFFF9EBDD),Color(0xFFFFF7ED),Color(0xFFF7E3CF),Color(0xFF51362A),Color(0xFFC18751),Color(0xFFC65B52)),
            ZurvanPalette("season-vibe","Season Vibe",Color(0xFFE7F1FB),Color(0xFFF1F7FC),Color(0xFFE4F0FC),Color(0xFF19304A),Color(0xFF6AA4D8),Color(0xFFCE4452))
        )[season]
        val palette = if (selected == "season-vibe") seasonalPalette else presets.firstOrNull { it.id == selected } ?: presets.first()
        val weekdayPalette = (0..6).map { index ->
            val custom = dayOverrides[index]
            if (custom != null) Color(android.graphics.Color.parseColor(custom))
            else if (selected == "zara-pastel") Color(android.graphics.Color.parseColor(zaraWeekdays[index]))
            else palette.card
        }
        val isDark = selected == "graphite-night"
        val base = if (isDark) darkColorScheme() else lightColorScheme()
        val colors = base.copy(
            background=color("background",palette.background), surface=color("surface",palette.surface),
            surfaceVariant=color("card",palette.card), onSurface=color("ink",palette.ink), onBackground=color("ink",palette.ink),
            primary=color("accent",palette.accent), primaryContainer=color("accent",palette.accent),
            onPrimaryContainer=color("ink",palette.ink), error=color("holiday",palette.holiday)
        )
        CompositionLocalProvider(
            refreshAppearance provides {
                selected = prefs.getString("theme", "windows-light") ?: "windows-light"
                wallpaperPath = prefs.getString("wallpaper", null)
                elenaImageIndex = prefs.getInt("elena_picture", 0).coerceIn(0, 11)
                appearanceRevision++
            },
            elenaImage provides elenaImageIndex,
            setElenaImage provides { index ->
                if (index in 0..11) {
                    elenaImageIndex = index
                    prefs.edit().putInt("elena_picture", index).apply()
                }
            },
            weekdayColors provides weekdayPalette,
            setWeekdayColor provides { index, value ->
                if (index in 0..6 && (value == null || Regex("^#[0-9A-Fa-f]{6}$").matches(value))) {
                    dayOverrides = dayOverrides.toMutableMap().apply {
                        if (value == null) remove(index) else put(index, value)
                    }
                    prefs.edit().apply {
                        if (value == null) remove("weekday.$selected.$index")
                        else putString("weekday.$selected.$index", value)
                    }.apply()
                }
            },
            customColors provides overrides,
            setCustomColor provides { key, hex ->
                val allowed = setOf("background","surface","card","ink","accent","holiday")
                if (key in allowed && (hex == null || Regex("^#[0-9A-Fa-f]{6}$").matches(hex))) {
                    val next = overrides.toMutableMap()
                    if (hex == null) next.remove(key) else next[key] = hex
                    overrides = next
                    prefs.edit().apply {
                        if (hex == null) remove("color.$selected.$key")
                        else putString("color.$selected.$key", hex)
                    }.apply()
                }
            },
            wallpaper provides wallpaperPath,
            setWallpaper provides { value ->
                wallpaperPath = value
                prefs.edit().apply {
                    if (value == null) remove("wallpaper") else putString("wallpaper", value)
                }.apply()
            },
            active provides palette, setActive provides { id ->
            selected = id
            prefs.edit().putString("theme", id).apply()
        }) {
            MaterialTheme(colorScheme=colors, content=content)
        }
    }
    @Composable
    fun Wallpaper(modifier: Modifier = Modifier) {
        val context = LocalContext.current
        val path = wallpaper.current
        val theme = active.current.id
        val seasonMonth = AndroidCalendarEngine.currentPersianYearMonth().second
        val season = when (seasonMonth) {
            in 1..3 -> "spring"
            in 4..6 -> "summer"
            in 7..9 -> "autumn"
            else -> "winter"
        }
        val prefs = remember { context.getSharedPreferences("zurvan_appearance", Context.MODE_PRIVATE) }
        val chosenIndex = elenaImage.current
        val todayIndex = remember {
            val cal = java.util.Calendar.getInstance()
            (cal.get(java.util.Calendar.DAY_OF_YEAR) - 1) % 11 + 1
        }
        val elenaIndex = if (chosenIndex == 0) todayIndex else chosenIndex
        val asset = when (theme) {
            "season-vibe" -> "themes/season-vibe/$season.jpg"
            "elena-mode" -> "themes/elena-mode/elena-%02d.jpg".format(java.util.Locale.US, elenaIndex)
            else -> null
        }
        // Theme artwork takes precedence over unrelated custom wallpaper from another theme.
        val activePath = if (asset != null) null else path
        if (theme == "liquid-glass") {
            androidx.compose.foundation.layout.Box(modifier.background(
                Brush.linearGradient(listOf(Color(0xFF9FC8E8), Color(0xFFDAE8F8),
                    Color(0xFFCAD7F1), Color(0xFF8CCCD1)))))
        }
        val bitmap = remember(activePath, asset) {
            runCatching {
                if (activePath != null) BitmapFactory.decodeFile(activePath)?.asImageBitmap()
                else if (asset != null) context.assets.open(asset).use { stream ->
                    BitmapFactory.decodeStream(stream)?.asImageBitmap()
                }
                else null
            }.getOrNull()
        } ?: return
        Image(bitmap = bitmap, contentDescription = null,
            modifier = modifier, contentScale = ContentScale.Crop,
            alpha = if (theme == "liquid-glass") 0.5f else 0.28f)
    }
}
