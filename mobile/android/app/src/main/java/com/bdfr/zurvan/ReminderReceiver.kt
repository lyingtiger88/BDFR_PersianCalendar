package com.bdfr.zurvan

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build

class ReminderReceiver :
    BroadcastReceiver() {
    override fun onReceive(
        context: Context,
        intent: Intent
    ) {
        val id = intent.getStringExtra(
            AndroidReminderScheduler.EXTRA_ID
        ) ?: return

        val originalTitle =
            intent.getStringExtra(
                AndroidReminderScheduler
                    .EXTRA_TITLE
            )
                ?: "یادآور"

        val isPrivate =
            intent.getBooleanExtra(
                AndroidReminderScheduler
                    .EXTRA_PRIVATE,
                false
            )

        val repeatCount =
            intent.getIntExtra(
                AndroidReminderScheduler
                    .EXTRA_REPEAT_COUNT,
                1
            )

        val interval =
            intent.getIntExtra(
                AndroidReminderScheduler
                    .EXTRA_INTERVAL,
                5
            )

        val attempt =
            intent.getIntExtra(
                AndroidReminderScheduler
                    .EXTRA_ATTEMPT,
                1
            )

        val manager =
            context.getSystemService(
                NotificationManager::class.java
            )

        val channelId =
            "zurvan_reminders"

        if (Build.VERSION.SDK_INT >=
            Build.VERSION_CODES.O) {
            manager.createNotificationChannel(
                NotificationChannel(
                    channelId,
                    "یادآورهای زروان",
                    NotificationManager
                        .IMPORTANCE_HIGH
                )
            )
        }

        val title = if (isPrivate) {
            "یادآور خصوصی"
        } else {
            originalTitle
        }

        val body = if (isPrivate) {
            "زروان یک یادآور خصوصی دارد."
        } else {
            "زمان یادآور فرا رسیده است."
        }

        val notification =
            Notification.Builder(
                context,
                channelId
            )
                .setSmallIcon(
                    android.R.drawable
                        .ic_lock_idle_alarm
                )
                .setContentTitle(title)
                .setContentText(body)
                .setAutoCancel(true)
                .setVisibility(
                    if (isPrivate) {
                        Notification
                            .VISIBILITY_PRIVATE
                    } else {
                        Notification
                            .VISIBILITY_PUBLIC
                    }
                )
                .build()

        val canNotify =
            Build.VERSION.SDK_INT <
                Build.VERSION_CODES.TIRAMISU ||
                context.checkSelfPermission(
                    Manifest.permission
                        .POST_NOTIFICATIONS
                ) ==
                PackageManager
                    .PERMISSION_GRANTED

        if (canNotify) {
            manager.notify(
                id.hashCode(),
                notification
            )
        }

        AndroidReminderScheduler
            .scheduleRepeat(
                context = context,
                reminderId = id,
                title = originalTitle,
                isPrivate = isPrivate,
                repeatCount =
                    repeatCount,
                intervalMinutes =
                    interval,
                nextAttempt =
                    attempt + 1
            )
    }
}
