package com.bdfr.zurvan

import android.content.Context
import androidx.compose.foundation.Image
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.graphics.asImageBitmap
import android.graphics.BitmapFactory
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.graphics.Color

data class ZurvanPalette(val id: String, val label: String, val background: Color,
    val surface: Color, val card: Color, val ink: Color, val accent: Color, val holiday: Color)

object ZurvanThemes {
    val presets = listOf(
        ZurvanPalette("windows-light","Windows Light",Color(0xFFF5F6F8),Color.White,Color.White,Color(0xFF202631),Color(0xFF9DC7F5),Color(0xFFD34E5E)),
        ZurvanPalette("azure-glass","Azure Glass",Color(0xFFEAF3FB),Color(0xFFF5FAFF),Color(0xFFEAF4FF),Color(0xFF17324D),Color(0xFF5DA9E8),Color(0xFFD25162)),
        ZurvanPalette("graphite-night","Graphite Night",Color(0xFF171A1F),Color(0xFF22262D),Color(0xFF2A2F37),Color(0xFFF1F5F9),Color(0xFF586D88),Color(0xFFFF7A86)),
        ZurvanPalette("warm-sand","Warm Sand",Color(0xFFFFF8F3),Color(0xFFFFF9F0),Color(0xFFFFFDF8),Color(0xFF40362C),Color(0xFFDDB78B),Color(0xFFC95C57)),
        ZurvanPalette("zara-pastel","Zara Pastel",Color(0xFFFDF9F6),Color.White,Color.White,Color(0xFF1E2A44),Color(0xFF7CC9F5),Color(0xFFD65A6F))
    )
    val active = staticCompositionLocalOf { presets.first() }
    val setActive = staticCompositionLocalOf<(String) -> Unit> { {} }
    val customColors = staticCompositionLocalOf<Map<String, String>> { emptyMap() }
    val setCustomColor = staticCompositionLocalOf<(String, String?) -> Unit> { _, _ -> }
    val wallpaper = staticCompositionLocalOf<String?> { null }
    val setWallpaper = staticCompositionLocalOf<(String?) -> Unit> { {} }

    @Composable
    fun Apply(context: Context, content: @Composable () -> Unit) {
        val prefs = remember { context.getSharedPreferences("zurvan_appearance", Context.MODE_PRIVATE) }
        var selected by remember { mutableStateOf(prefs.getString("theme", "windows-light") ?: "windows-light") }
        var wallpaperPath by remember { mutableStateOf(prefs.getString("wallpaper", null)) }
        var overrides by remember(selected) {
            mutableStateOf(listOf("background","surface","card","ink","accent","holiday").mapNotNull { key ->
                prefs.getString("color.$selected.$key", null)?.let { key to it }
            }.toMap())
        }
        fun color(key: String, fallback: Color): Color =
            overrides[key]?.let { runCatching { Color(android.graphics.Color.parseColor(it)) }.getOrNull() } ?: fallback
        val palette = presets.firstOrNull { it.id == selected } ?: presets.first()
        val isDark = selected == "graphite-night"
        val base = if (isDark) darkColorScheme() else lightColorScheme()
        val colors = base.copy(
            background=color("background",palette.background), surface=color("surface",palette.surface),
            surfaceVariant=color("card",palette.card), onSurface=color("ink",palette.ink), onBackground=color("ink",palette.ink),
            primary=color("accent",palette.accent), primaryContainer=color("accent",palette.accent),
            onPrimaryContainer=color("ink",palette.ink), error=color("holiday",palette.holiday)
        )
        CompositionLocalProvider(customColors provides overrides,
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
        val path = wallpaper.current ?: return
        val bitmap = remember(path) {
            runCatching { BitmapFactory.decodeFile(path)?.asImageBitmap() }.getOrNull()
        } ?: return
        Image(bitmap = bitmap, contentDescription = null,
            modifier = modifier, contentScale = ContentScale.Crop, alpha = 0.28f)
    }
}
