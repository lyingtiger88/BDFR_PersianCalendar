package com.bdfr.zurvan

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch
import org.json.JSONObject

/**
 * Server-authoritative XP dashboard; no mutable local XP balances.
 * Owner rank badges are deliberately separate from achievements.
 */
@Composable
internal fun ZurvanUserProgressPanel() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var data by remember { mutableStateOf<JSONObject?>(null) }
    var busy by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf("") }

    suspend fun reload(action: String = "status", visible: Boolean? = null) {
        busy = true
        try {
            val result = ZurvanAndroidOnlineLicense.userProgress(context, action, visible)
            data = result
            message = when (action) {
                "claim_daily" -> if (result.optJSONObject("claim")?.optBoolean("claimed") == true)
                    "امتیاز حضور امروز ثبت شد."
                else "امتیاز امروز قبلاً دریافت شده است."
                "set_visibility" -> "تنظیم نمایش در رتبه‌بندی ذخیره شد."
                else -> ""
            }
        } catch (error: Exception) {
            message = error.message ?: "دریافت اطلاعات امتیاز ممکن نیست."
        } finally {
            busy = false
        }
    }

    LaunchedEffect(Unit) {
        if (ZurvanAndroidOnlineLicense.hasLinkedOnlineAccount(context)) reload()
    }

    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        if (!ZurvanAndroidOnlineLicense.hasLinkedOnlineAccount(context)) {
            Text("برای مشاهده امتیازها و نشان‌های حساب، ابتدا به حساب آنلاین زروان وارد شوید.")
            return@Column
        }
        if (busy) CircularProgressIndicator()
        if (message.isNotBlank()) Text(message,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        val response = data
        if (response == null) {
            Button(enabled = !busy, onClick = { scope.launch { reload() } }) {
                Text("دریافت امتیازها")
            }
            return@Column
        }
        val profile = response.optJSONObject("profile")
        if (profile == null) {
            Text("اطلاعات امتیاز از سرور دریافت نشد.")
            return@Column
        }

        Text("امتیاز کل: ${profile.optLong("total_xp")} XP",
            fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium)
        Text("سطح ${profile.optInt("level")} · ${profile.optString("level_title")}")
        LinearProgressIndicator(
            progress = { profile.optInt("progress_percent").coerceIn(0, 100) / 100f },
            modifier = Modifier.fillMaxWidth()
        )
        val next = if (profile.isNull("next_level_xp")) -1L else profile.optLong("next_level_xp")
        Text(if (next < 0) "به بالاترین سطح فعلی رسیده‌ای."
             else "${profile.optLong("xp_to_next")} امتیاز تا سطح بعدی",
            style = MaterialTheme.typography.bodySmall)
        Text("حضور متوالی: ${profile.optInt("daily_streak")} روز · بهترین رکورد: ${profile.optInt("best_streak")} روز",
            style = MaterialTheme.typography.bodySmall)
        Button(
            enabled = !busy && !profile.optBoolean("checked_in_today"),
            onClick = { scope.launch { reload("claim_daily") } }
        ) { Text(if (profile.optBoolean("checked_in_today"))
            "امتیاز امروز دریافت شده" else "ثبت حضور و دریافت امتیاز امروز") }

        response.optJSONObject("special_rank")?.let { rank ->
            HorizontalDivider()
            Text("نشان ویژه: " + rank.optString("display_name_fa"),
                fontWeight = FontWeight.Bold)
            Text("رتبه ویژه مدیریتی مستقل از امتیاز XP است.",
                style = MaterialTheme.typography.bodySmall)
        }

        HorizontalDivider()
        Text("نشان‌ها", style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold)
        val badges = response.optJSONArray("badges")
        if (badges != null) for (i in 0 until badges.length()) {
            val badge = badges.optJSONObject(i) ?: continue
            val earned = badge.optBoolean("earned")
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(if (earned) "✅" else "🔒")
                Column(Modifier.weight(1f)) {
                    Text(badge.optString("icon") + " " + badge.optString("name_fa"),
                        fontWeight = FontWeight.SemiBold)
                    Text(badge.optString("description_fa"),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }

        HorizontalDivider()
        Text("تاریخچه امتیاز", style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold)
        val history = response.optJSONArray("history")
        if (history == null || history.length() == 0) {
            Text("هنوز امتیازی ثبت نشده است.")
        } else for (i in 0 until minOf(8, history.length())) {
            val entry = history.optJSONObject(i) ?: continue
            Text("${entry.optString("title")} · +${entry.optInt("points")} XP · ${entry.optString("created_at").take(10)}",
                style = MaterialTheme.typography.bodySmall)
        }

        HorizontalDivider()
        Text("جدول رتبه‌بندی", style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Column(Modifier.weight(1f)) {
                Text("نمایش حساب در رتبه‌بندی")
                Text("اختیاری است؛ ایمیل کاربران نمایش داده نمی‌شود.",
                    style = MaterialTheme.typography.bodySmall)
            }
            Switch(checked = profile.optBoolean("leaderboard_opt_in"),enabled = !busy,
                onCheckedChange = { scope.launch { reload("set_visibility", it) } })
        }
        val leaders = response.optJSONArray("leaderboard")
        if (leaders == null || leaders.length() == 0) {
            Text("هنوز کاربری حضور در رتبه‌بندی را فعال نکرده است.")
        } else for (i in 0 until leaders.length()) {
            val user = leaders.optJSONObject(i) ?: continue
            Text("${user.optInt("position")}. ${user.optString("display_name")} · ${user.optLong("total_xp")} XP" +
                if (user.optBoolean("is_self")) " (شما)" else "",
                style = MaterialTheme.typography.bodySmall)
        }
        Spacer(Modifier.height(4.dp))
        Button(enabled = !busy, onClick = { scope.launch { reload() } }) {
            Text("به‌روزرسانی نشان‌ها و امتیاز")
        }
    }
}
