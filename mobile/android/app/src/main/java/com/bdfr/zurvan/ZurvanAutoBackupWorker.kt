package com.bdfr.zurvan

import android.content.Context
import android.net.Uri
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import java.io.IOException
import java.time.Instant
import java.util.concurrent.TimeUnit

/**
 * Optional local-only backup into a SAF file explicitly selected by the user.
 * A periodic job replaces polling. Sensitive JSON remains unencrypted; UI warns.
 */
class ZurvanAutoBackupWorker(context: Context, parameters: WorkerParameters) :
    CoroutineWorker(context, parameters) {

    override suspend fun doWork(): Result {
        val prefs = applicationContext.getSharedPreferences(META, Context.MODE_PRIVATE)
        if (!prefs.getBoolean(ENABLED, false)) return Result.success()
        val uri = prefs.getString(URI, null)?.let(Uri::parse) ?: return Result.failure()
        return try {
            val bytes = ZurvanLocalBackup.export(applicationContext).toByteArray(Charsets.UTF_8)
            applicationContext.contentResolver.openOutputStream(uri, "wt")?.use {
                it.write(bytes)
                it.flush()
            } ?: throw IOException("مقصد پشتیبان قابل نوشتن نیست.")
            prefs.edit()
                .putString("last_auto_success_utc", Instant.now().toString())
                .remove("last_auto_error")
                .apply()
            Result.success()
        } catch (error: SecurityException) {
            prefs.edit().putString("last_auto_error", "دسترسی به فایل پشتیبان لغو شده است.").apply()
            Result.failure()
        } catch (error: Exception) {
            prefs.edit().putString("last_auto_error", error.message ?: "خطای ذخیره خودکار").apply()
            Result.retry()
        }
    }

    companion object {
        private const val META = "zurvan_backup_metadata"
        private const val ENABLED = "auto_enabled"
        private const val URI = "auto_uri"
        private const val UNIQUE = "zurvan-daily-local-backup"

        fun enable(context: Context, uri: Uri) {
            val prefs = context.getSharedPreferences(META, Context.MODE_PRIVATE)
            prefs.edit().putString(URI, uri.toString()).putBoolean(ENABLED, true).apply()
            val constraints = Constraints.Builder().setRequiresBatteryNotLow(true).build()
            val request = PeriodicWorkRequestBuilder<ZurvanAutoBackupWorker>(
                24, TimeUnit.HOURS, 2, TimeUnit.HOURS
            ).setConstraints(constraints).build()
            WorkManager.getInstance(context).enqueueUniquePeriodicWork(
                UNIQUE, ExistingPeriodicWorkPolicy.UPDATE, request
            )
        }

        fun disable(context: Context) {
            val prefs = context.getSharedPreferences(META, Context.MODE_PRIVATE)
            val uri = prefs.getString(URI, null)
            prefs.edit().putBoolean(ENABLED, false).remove(URI).apply()
            WorkManager.getInstance(context).cancelUniqueWork(UNIQUE)
            if (uri != null) {
                runCatching {
                    context.contentResolver.releasePersistableUriPermission(
                        Uri.parse(uri), android.content.Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                    )
                }
            }
        }
    }
}
