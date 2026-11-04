package com.bdfr.zurvan

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/**
 * OS alarms are cleared by reboot. Restore only future, unacknowledged reminders.
 * No polling loop or permanent foreground service is required.
 */
class ReminderRecoveryReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.action) {
            Intent.ACTION_BOOT_COMPLETED,
            Intent.ACTION_TIME_CHANGED,
            Intent.ACTION_TIMEZONE_CHANGED,
            Intent.ACTION_MY_PACKAGE_REPLACED -> {
                val pending = goAsync()
                try {
                    val repository = AndroidLocalRepository(context.applicationContext)
                    repository.reminders.value
                        .asSequence()
                        .filterNot { it.acknowledged }
                        .forEach { reminder ->
                            runCatching {
                                AndroidReminderScheduler.schedule(context, reminder)
                            }
                        }
                } finally {
                    pending.finish()
                }
            }
        }
    }
}
