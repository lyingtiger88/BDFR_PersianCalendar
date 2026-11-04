package com.bdfr.zurvan

import android.app.AlarmManager
import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.widget.RemoteViews
import java.time.LocalDate
import java.time.ZoneId

/** Optional home-screen widget. Intentionally hides private and completed tasks. */
class ZurvanHomeWidget : AppWidgetProvider() {
    override fun onUpdate(context: Context, manager: AppWidgetManager, widgetIds: IntArray) {
        widgetIds.forEach { updateOne(context, manager, it) }
        scheduleTomorrow(context, widgetIds.isNotEmpty())
    }

    override fun onEnabled(context: Context) = updateAll(context)

    override fun onDisabled(context: Context) {
        context.getSystemService(AlarmManager::class.java).cancel(refreshIntent(context))
    }

    override fun onReceive(context: Context, intent: Intent) {
        super.onReceive(context, intent)
        when (intent.action) {
            ACTION_REFRESH, Intent.ACTION_DATE_CHANGED,
            Intent.ACTION_TIMEZONE_CHANGED, Intent.ACTION_BOOT_COMPLETED -> updateAll(context)
        }
    }

    companion object {
        private const val ACTION_REFRESH = "com.bdfr.zurvan.action.REFRESH_HOME_WIDGET"
        private val months = arrayOf(
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
        )
        private val digits = "۰۱۲۳۴۵۶۷۸۹"

        fun updateAll(context: Context) {
            val manager = AppWidgetManager.getInstance(context)
            val ids = manager.getAppWidgetIds(ComponentName(context, ZurvanHomeWidget::class.java))
            if (ids.isEmpty()) return
            ids.forEach { updateOne(context, manager, it) }
            scheduleTomorrow(context, true)
        }

        private fun updateOne(context: Context, manager: AppWidgetManager, id: Int) {
            val now = java.time.LocalTime.now()
            val (year, month) = AndroidCalendarEngine.currentPersianYearMonth()
            val today = AndroidCalendarEngine.buildPersianMonth(year, month).days.firstOrNull { it.isToday }
            val dateKey = today?.persianKey ?: "%04d-%02d-01".format(year, month)
            val weekdayName = when (LocalDate.now().dayOfWeek) {
                java.time.DayOfWeek.SATURDAY -> "شنبه"
                java.time.DayOfWeek.SUNDAY -> "یکشنبه"
                java.time.DayOfWeek.MONDAY -> "دوشنبه"
                java.time.DayOfWeek.TUESDAY -> "سه‌شنبه"
                java.time.DayOfWeek.WEDNESDAY -> "چهارشنبه"
                java.time.DayOfWeek.THURSDAY -> "پنجشنبه"
                else -> "جمعه"
            }
            val views = RemoteViews(context.packageName, R.layout.zurvan_widget)
            views.setTextViewText(R.id.zurvan_widget_date,
                "$weekdayName ${persian((today?.persianDay ?: 1).toString())} ${months[month - 1]} ${persian(year.toString())}")
            val tasks = AndroidLocalRepository(context).plans.value.asSequence()
                .filter { it.kind == "task" && !it.completed && !it.isPrivate }
                .filter {
                    it.dateKey > dateKey || (it.dateKey == dateKey &&
                        (it.hour > now.hour || (it.hour == now.hour && it.minute >= now.minute)))
                }
                .sortedWith(compareBy<LocalPlan> { it.dateKey }.thenBy { it.hour }.thenBy { it.minute })
                .take(3).toList()
            val fields = intArrayOf(R.id.zurvan_widget_task1, R.id.zurvan_widget_task2, R.id.zurvan_widget_task3)
            fields.forEachIndexed { index, field ->
                val task = tasks.getOrNull(index)
                val label = if (task == null) {
                    if (index == 0) "وظیفه‌ای در پیش نیست" else ""
                } else {
                    "• " + persian(task.dateKey.replace("-", "/")) + " " +
                        persian("%02d:%02d".format(task.hour, task.minute)) + " " + task.title
                }
                views.setTextViewText(field, label)
            }
            views.setOnClickPendingIntent(R.id.zurvan_widget_root, PendingIntent.getActivity(
                context, 0, Intent(context, MainActivity::class.java),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE))
            manager.updateAppWidget(id, views)
        }

        private fun persian(raw: String) = raw.map {
            if (it in '0'..'9') digits[it - '0'] else it
        }.joinToString("")

        private fun refreshIntent(context: Context) = PendingIntent.getBroadcast(
            context, 41245,
            Intent(context, ZurvanHomeWidget::class.java).apply { action = ACTION_REFRESH },
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        private fun scheduleTomorrow(context: Context, enabled: Boolean) {
            if (!enabled) return
            val next = LocalDate.now().plusDays(1).atStartOfDay(ZoneId.systemDefault())
                .plusMinutes(10).toInstant().toEpochMilli()
            context.getSystemService(AlarmManager::class.java).setWindow(
                AlarmManager.RTC_WAKEUP, next, 60 * 60 * 1000L, refreshIntent(context))
        }
    }
}
