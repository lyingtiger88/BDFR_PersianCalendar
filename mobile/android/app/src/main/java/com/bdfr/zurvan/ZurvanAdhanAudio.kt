package com.bdfr.zurvan

import android.app.*
import android.content.*
import android.media.AudioAttributes
import android.media.MediaPlayer
import android.net.Uri
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import java.io.File

/** User-supplied, locally stored adhan recordings. No audio is included or downloaded. */
object ZurvanAdhanAudio {
    private const val PREFS = "zurvan_adhan_audio"
    fun profile(school: String) = if (school == "jafari") "jafari" else "sunni"
    fun file(context: Context, school: String): File =
        File(context.filesDir, "adhan_${profile(school)}.audio")
    fun hasRecording(context: Context, school: String) = file(context, school).length() > 0
    fun enabled(context: Context) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean("enabled", false)
    fun setEnabled(context: Context, enabled: Boolean) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putBoolean("enabled", enabled).apply()
    }
    fun importRecording(context: Context, school: String, uri: Uri) {
        val destination = file(context, school)
        val temp = File(context.filesDir, destination.name + ".tmp")
        try {
            context.contentResolver.openInputStream(uri)?.use { input ->
                temp.outputStream().use { output ->
                    val buffer = ByteArray(8192)
                    var total = 0L
                    while (true) {
                        val n = input.read(buffer)
                        if (n < 0) break
                        total += n
                        require(total <= 20_000_000L) { "حجم فایل بیشتر از ۲۰ مگابایت است." }
                        output.write(buffer, 0, n)
                    }
                }
            } ?: error("فایل صوتی قابل خواندن نیست.")
            require(temp.length() > 0) { "فایل صوتی خالی است." }
            temp.copyTo(destination, overwrite = true)
        } finally { temp.delete() }
    }
    fun play(context: Context, school: String, preview: Boolean) {
        if (!hasRecording(context, school)) return
        val intent = Intent(context, ZurvanAdhanService::class.java).apply {
            action = "com.bdfr.zurvan.ADHAN_PLAY"
            putExtra("school", school)
            putExtra("preview", preview)
        }
        androidx.core.content.ContextCompat.startForegroundService(context, intent)
    }
    fun stop(context: Context) {
        context.stopService(Intent(context, ZurvanAdhanService::class.java))
    }
}

class ZurvanAdhanService : Service() {
    private var player: MediaPlayer? = null
    override fun onBind(intent: Intent?): IBinder? = null
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action != "com.bdfr.zurvan.ADHAN_PLAY") {
            stopSelf(startId)
            return START_NOT_STICKY
        }
        val school = intent.getStringExtra("school") ?: "jafari"
        val audio = ZurvanAdhanAudio.file(this, school)
        if (!audio.isFile || audio.length() == 0L) {
            stopSelf(startId)
            return START_NOT_STICKY
        }
        val channel = "zurvan_adhan_playback"
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel(channel, "پخش اذان",
            NotificationManager.IMPORTANCE_LOW))
        val stop = PendingIntent.getService(this, 7422,
            Intent(this, ZurvanAdhanService::class.java).apply {
                action = "com.bdfr.zurvan.ADHAN_STOP"
            }, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val notification = NotificationCompat.Builder(this, channel)
            .setSmallIcon(android.R.drawable.ic_lock_idle_alarm)
            .setContentTitle("زروان · پخش اذان")
            .setContentText(if (school == "jafari") "پروفایل جعفری" else "پروفایل اهل‌سنت")
            .addAction(android.R.drawable.ic_media_pause, "توقف", stop)
            .setOngoing(true).build()
        startForeground(7421, notification)
        try {
            player?.release()
            player = MediaPlayer().apply {
                setAudioAttributes(AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_MEDIA)
                    .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
                setDataSource(audio.absolutePath)
                setOnCompletionListener { stopSelf() }
                setOnErrorListener { _, _, _ -> stopSelf(); true }
                prepare()
                start()
            }
        } catch (_: Exception) { stopSelf(startId) }
        return START_NOT_STICKY
    }
    override fun onDestroy() {
        player?.runCatching { if (isPlaying) stop() }
        player?.release()
        player = null
        super.onDestroy()
    }
}
