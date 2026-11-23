package com.bdfr.zurvan

import android.content.Context
import android.provider.Settings
import java.util.concurrent.TimeUnit

internal object ZurvanTrialPolicy {
    private const val PREFS = "zurvan_license_policy"
    private const val STARTED_AT = "trial_started_at"
    private const val DEVICE_BINDING = "trial_device_binding"
    private const val LAST_SEEN_AT = "trial_last_seen_at"
    private const val CLOCK_ROLLBACK = "trial_clock_rollback"
    private const val TRIAL_DAYS = 45L

    data class Entitlement(
        val canWrite: Boolean,
        val trialActive: Boolean,
        val trialExpired: Boolean,
        val daysRemaining: Int,
        val expiresAtMillis: Long
    )

    @Synchronized
    fun evaluate(context: Context): Entitlement {
        val app = context.applicationContext
        if (BuildConfig.ENFORCE_ONLINE_MOBILE_TRIAL) {
            val activation = app.getSharedPreferences("zurvan_license", Context.MODE_PRIVATE)
                .getString("activation_code", null)
            val signedOffline = activation != null && ZurvanActivation.validate(app, activation)
            val online = ZurvanAndroidOnlineLicense.hasRecentServerEntitlement(app)
            val trial = online && ZurvanAndroidOnlineLicense.activeOnlineMode(app) == "trial"
            return Entitlement(
                canWrite = signedOffline || online,
                trialActive = trial && !signedOffline,
                trialExpired = !signedOffline && !online,
                daysRemaining = if (trial) ZurvanAndroidOnlineLicense.onlineTrialDaysRemaining(app) else 0,
                expiresAtMillis = 0L
            )
        }
        val prefs = app.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val deviceId = Settings.Secure.getString(app.contentResolver, Settings.Secure.ANDROID_ID)
            ?: "android-device-unavailable"
        val binding = prefs.getString(DEVICE_BINDING, null)
        var started = prefs.getLong(STARTED_AT, 0L)
        if (started <= 0L) {
            started = System.currentTimeMillis()
            prefs.edit().putString(DEVICE_BINDING, deviceId).putLong(STARTED_AT, started).commit()
        } else if (binding != deviceId) {
            // A changed Android installation identifier is not allowed to reset the trial.
            prefs.edit().putString(DEVICE_BINDING, deviceId).commit()
        }
        val now = System.currentTimeMillis()
        val lastSeen = prefs.getLong(LAST_SEEN_AT, 0L)
        val rollbackDetected = prefs.getBoolean(CLOCK_ROLLBACK, false) ||
            (lastSeen > 0L && now + TimeUnit.MINUTES.toMillis(5) < lastSeen)
        val editor = prefs.edit().putLong(LAST_SEEN_AT, maxOf(now, lastSeen))
        if (rollbackDetected) editor.putBoolean(CLOCK_ROLLBACK, true)
        editor.commit()
        val expiry = started + TimeUnit.DAYS.toMillis(TRIAL_DAYS)
        val trialActive = !rollbackDetected && now < expiry
        val activation = app.getSharedPreferences("zurvan_license", Context.MODE_PRIVATE)
            .getString("activation_code", null)
        val paid = activation != null && ZurvanActivation.validate(app, activation)
        return Entitlement(
            canWrite = paid || trialActive,
            trialActive = trialActive && !paid,
            trialExpired = !paid && !trialActive,
            daysRemaining = if (trialActive) TimeUnit.MILLISECONDS.toDays(expiry - now).toInt().coerceAtLeast(1) else 0,
            expiresAtMillis = expiry
        )
    }

    fun canWrite(context: Context): Boolean = evaluate(context).canWrite
}