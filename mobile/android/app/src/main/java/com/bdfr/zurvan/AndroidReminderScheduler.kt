package com.bdfr.zurvan

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent

object AndroidReminderScheduler {
    fun schedule(
        context: Context,
        reminder: LocalReminder
    ) {
        val parts = reminder.dateKey
            .split("-")
            .mapNotNull {
                it.toIntOrNull()
            }

        if (parts.size != 3)
            return

        val triggerAt =
            AndroidCalendarEngine
                .persianDateTimeToMillis(
                    reminder.dateKey,
                    reminder.hour,
                    reminder.minute
                )
                ?: return

        if (triggerAt <=
            System.currentTimeMillis()) {
            return
        }

        scheduleAt(
            context = context,
            reminderId = reminder.id,
            title = reminder.title,
            isPrivate = reminder.isPrivate,
            repeatCount = reminder.repeatCount,
            intervalMinutes =
                reminder.repeatIntervalMinutes,
            attempt = 1,
            triggerAtMillis = triggerAt
        )
    }

    fun cancel(
        context: Context,
        reminderId: String
    ) {
        val alarmManager =
            context.getSystemService(
                AlarmManager::class.java
            )

        alarmManager.cancel(
            pendingIntent(
                context,
                reminderId
            )
        )
    }

    internal fun scheduleRepeat(
        context: Context,
        reminderId: String,
        title: String,
        isPrivate: Boolean,
        repeatCount: Int,
        intervalMinutes: Int,
        nextAttempt: Int
    ) {
        if (nextAttempt > repeatCount)
            return

        val triggerAt =
            System.currentTimeMillis() +
                intervalMinutes
                    .coerceIn(
                        1,
                        1440
                    ) *
                60_000L

        scheduleAt(
            context = context,
            reminderId = reminderId,
            title = title,
            isPrivate = isPrivate,
            repeatCount =
                repeatCount,
            intervalMinutes =
                intervalMinutes,
            attempt = nextAttempt,
            triggerAtMillis = triggerAt
        )
    }

    private fun scheduleAt(
        context: Context,
        reminderId: String,
        title: String,
        isPrivate: Boolean,
        repeatCount: Int,
        intervalMinutes: Int,
        attempt: Int,
        triggerAtMillis: Long
    ) {
        val intent = Intent(
            context,
            ReminderReceiver::class.java
        ).apply {
            putExtra(
                EXTRA_ID,
                reminderId
            )
            putExtra(
                EXTRA_TITLE,
                title
            )
            putExtra(
                EXTRA_PRIVATE,
                isPrivate
            )
            putExtra(
                EXTRA_REPEAT_COUNT,
                repeatCount
            )
            putExtra(
                EXTRA_INTERVAL,
                intervalMinutes
            )
            putExtra(
                EXTRA_ATTEMPT,
                attempt
            )
        }

        val pendingIntent =
            PendingIntent.getBroadcast(
                context,
                reminderId.hashCode(),
                intent,
                PendingIntent.FLAG_UPDATE_CURRENT or
                    PendingIntent.FLAG_IMMUTABLE
            )

        val alarmManager =
            context.getSystemService(
                AlarmManager::class.java
            )

        alarmManager.setWindow(
            AlarmManager.RTC_WAKEUP,
            triggerAtMillis,
            60_000L,
            pendingIntent
        )
    }

    private fun pendingIntent(
        context: Context,
        reminderId: String
    ): PendingIntent {
        val intent = Intent(
            context,
            ReminderReceiver::class.java
        )

        return PendingIntent.getBroadcast(
            context,
            reminderId.hashCode(),
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or
                PendingIntent.FLAG_IMMUTABLE
        )
    }

    const val EXTRA_ID =
        "zurvan_reminder_id"

    const val EXTRA_TITLE =
        "zurvan_reminder_title"

    const val EXTRA_PRIVATE =
        "zurvan_reminder_private"

    const val EXTRA_REPEAT_COUNT =
        "zurvan_reminder_repeat_count"

    const val EXTRA_INTERVAL =
        "zurvan_reminder_interval"

    const val EXTRA_ATTEMPT =
        "zurvan_reminder_attempt"
}
