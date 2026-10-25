package com.bdfr.zurvan

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkerParameters
import androidx.work.WorkManager
import java.util.concurrent.TimeUnit

/** Best-effort periodic deletion; Android may defer background execution. */
class ZurvanPlanExpiryWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {
    override suspend fun doWork(): Result = try {
        AndroidLocalRepository(applicationContext).purgeExpiredPlans()
        Result.success()
    } catch (error: Exception) {
        Result.retry()
    }
}

object ZurvanPlanExpirySchedule {
    fun register(context: Context) {
        WorkManager.getInstance(context).enqueueUniquePeriodicWork(
            "zurvan_expired_events",
            ExistingPeriodicWorkPolicy.KEEP,
            PeriodicWorkRequestBuilder<ZurvanPlanExpiryWorker>(1, TimeUnit.DAYS).build()
        )
    }
}
