package com.bdfr.zurvan

import android.app.AlarmManager
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import java.time.LocalDate
import java.time.ZoneId

/** Schedules next day's estimated prayer-time reminders without exact-alarm privilege. */
object ZurvanPrayerAlerts {
    private val keys = setOf("fajr","dhuhr","asr","maghrib","isha")
    private fun token(context:Context,key:String):PendingIntent {
        val i=Intent(context,ZurvanPrayerReceiver::class.java).apply {
            action="com.bdfr.zurvan.PRAYER_$key"
            putExtra("key",key)
        }
        return PendingIntent.getBroadcast(context,20000+key.hashCode().and(0x3fff),i,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
    }
    fun schedule(context:Context,s:ZurvanPrayerTimes.Settings) {
        val alarm=context.getSystemService(AlarmManager::class.java)
        keys.forEach { alarm.cancel(token(context,it)) }
        if(!s.alerts) return
        val zone=runCatching { ZoneId.of(s.zone) }.getOrNull() ?: return
        val today=LocalDate.now(zone)
        val now=System.currentTimeMillis()
        for(key in keys) {
            val instant=(0..2).asSequence().mapNotNull { dayOffset ->
                val date=today.plusDays(dayOffset.toLong())
                ZurvanPrayerTimes.calculate(date,s).firstOrNull { it.key==key }?.let {
                    date.atTime(it.time).atZone(zone).toInstant().toEpochMilli()-s.beforeMinutes*60000L
                }
            }.firstOrNull { it>now } ?: continue
            alarm.setWindow(AlarmManager.RTC_WAKEUP,instant,60_000L,token(context,key))
        }
    }
}

class ZurvanPrayerReceiver:BroadcastReceiver() {
    override fun onReceive(context:Context,intent:Intent?) {
        val settings=ZurvanPrayerTimes.settings(context)
        if(!settings.alerts) return
        val key=intent?.getStringExtra("key") ?: return
        val labels=mapOf("fajr" to "اذان صبح","dhuhr" to "اذان ظهر",
            "asr" to "عصر","maghrib" to "اذان مغرب","isha" to "عشا")
        val title=labels[key] ?: return
        val manager=context.getSystemService(NotificationManager::class.java)
        if(Build.VERSION.SDK_INT>=26) manager.createNotificationChannel(
            NotificationChannel("zurvan_prayer_times","اوقات شرعی",NotificationManager.IMPORTANCE_DEFAULT))
        val notification=NotificationCompat.Builder(context,"zurvan_prayer_times")
            .setSmallIcon(android.R.drawable.ic_lock_idle_alarm)
            .setContentTitle("زروان · $title")
            .setContentText(if(settings.beforeMinutes>0) "${settings.beforeMinutes} دقیقه تا $title در ${settings.city}"
                else "زمان $title در ${settings.city}")
            .setAutoCancel(true).build()
        if(Build.VERSION.SDK_INT<33 || context.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)==android.content.pm.PackageManager.PERMISSION_GRANTED)
            manager.notify(30000+key.hashCode().and(0x3fff),notification)
        ZurvanPrayerAlerts.schedule(context,settings)
    }
}
