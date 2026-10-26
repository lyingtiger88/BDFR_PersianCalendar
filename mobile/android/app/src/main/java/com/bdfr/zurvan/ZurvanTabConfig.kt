package com.bdfr.zurvan

import android.content.Context
import androidx.compose.runtime.staticCompositionLocalOf

internal object ZurvanTabConfig {
    val defaults = listOf("Calendar","Desk","Plans","Occasions","Settings")
    val available = listOf("Calendar","Desk","Plans","Occasions","Reminders","Companion","Settings")
    val visible = staticCompositionLocalOf { defaults }
    val update = staticCompositionLocalOf<(List<String>) -> Unit> { { _: List<String> -> } }
    val open = staticCompositionLocalOf<(String) -> Unit> { { _: String -> } }
    fun load(context: Context): List<String> {
        val raw = context.getSharedPreferences("zurvan_navigation_v1",Context.MODE_PRIVATE)
            .getString("tabs",null) ?: return defaults
        val names = raw.split(",").filter { it in available }.distinct()
        return available.filter { it in names || it == "Calendar" || it == "Settings" }
    }
    fun save(context: Context, requested: List<String>): List<String> {
        val result = available.filter { it in requested || it == "Calendar" || it == "Settings" }
        context.getSharedPreferences("zurvan_navigation_v1",Context.MODE_PRIVATE)
            .edit().putString("tabs",result.joinToString(",")).apply()
        return result
    }
}
