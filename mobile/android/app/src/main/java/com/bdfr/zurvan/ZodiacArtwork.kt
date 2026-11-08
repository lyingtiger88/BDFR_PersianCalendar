package com.bdfr.zurvan

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.withTransform
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import org.json.JSONObject

/** Shared zodiac illustration catalog used by Windows and Android.
 *  Each sign has its own outlined vector drawing, rendered at any density.
 *  All art is separate from occasion text to avoid contrast and overlap problems.
 */
@Composable
internal fun ZodiacArtworkImage(
    month: Int,
    styleId: String,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val artwork = remember(month, styleId) {
        runCatching {
            require(month in 1..12)
            val json = context.assets.open("zodiac/catalog.json").bufferedReader().use { JSONObject(it.readText()) }
            val sign = json.getJSONArray("signs").getJSONObject(month - 1)
            val styles = json.getJSONArray("styles")
            val style = (0 until styles.length())
                .map { styles.getJSONObject(it) }
                .firstOrNull { it.optString("id") == styleId }
                ?: styles.getJSONObject(0)
            val path = PathParser().parsePathString(sign.getString("path")).toPath()
            ZodiacArt(
                path = path,
                background = Color(android.graphics.Color.parseColor(style.getString("bg"))),
                ink = Color(android.graphics.Color.parseColor(style.getString("accent"))),
                glow = Color(android.graphics.Color.parseColor(style.getString("glow"))),
                stroke = style.getDouble("stroke").toFloat()
            )
        }.getOrNull()
    }
    if (artwork == null) return

    Canvas(modifier) {
        withTransform({
            scale(size.width / 100f, size.height / 100f, pivot = Offset.Zero)
        }) {
            drawRoundRect(
                brush = Brush.radialGradient(
                    colors = listOf(artwork.glow.copy(alpha = 0.85f), artwork.background),
                    center = Offset(50f, 42f),
                    radius = 82f
                ),
                topLeft = Offset(0f, 0f),
                size = androidx.compose.ui.geometry.Size(100f, 100f),
                cornerRadius = androidx.compose.ui.geometry.CornerRadius(21f, 21f)
            )
            drawCircle(
                color = artwork.ink.copy(alpha = .33f),
                radius = 43f,
                center = Offset(50f, 50f),
                style = Stroke(width = .6f)
            )
            listOf(Offset(18f,18f), Offset(82f,18f), Offset(16f,74f), Offset(82f,80f))
                .forEach { point -> drawCircle(artwork.ink.copy(alpha = .8f), radius = 1f, center = point) }
            drawPath(
                artwork.path,
                color = artwork.background.copy(alpha = .65f),
                style = Stroke(width = artwork.stroke + 2.4f, cap = StrokeCap.Round, join = StrokeJoin.Round)
            )
            drawPath(
                artwork.path,
                brush = Brush.linearGradient(
                    colors = listOf(Color.White, artwork.ink, artwork.glow),
                    start = Offset(12f, 8f), end = Offset(90f, 94f)
                ),
                style = Stroke(width = artwork.stroke, cap = StrokeCap.Round, join = StrokeJoin.Round)
            )
        }
    }
}

private data class ZodiacArt(
    val path: Path,
    val background: Color,
    val ink: Color,
    val glow: Color,
    val stroke: Float
)
