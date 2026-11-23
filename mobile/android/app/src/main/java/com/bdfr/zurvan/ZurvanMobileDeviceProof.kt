package com.bdfr.zurvan

import android.content.Context
import android.provider.Settings
import android.util.Base64
import com.google.android.play.core.integrity.IntegrityManagerFactory
import com.google.android.play.core.integrity.IntegrityTokenRequest
import com.google.android.gms.tasks.Tasks
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.concurrent.TimeUnit

/**
 * Device evidence for Supabase verification. Android 10+ does not expose
 * hardware serial or IMEI to ordinary apps.
 *
 * The Android ID is NOT authoritative proof: only a Google-verified Integrity
 * verdict, verified by the server and bound to the request's nonce, can prove
 * that the verdict came from the official app on an eligible device.
 * Play Integrity device recall is additionally required to resist resets.
 */
internal object ZurvanMobileDeviceProof {
    fun fingerprint(context: Context): String {
        val androidId = Settings.Secure.getString(
            context.contentResolver, Settings.Secure.ANDROID_ID
        )?.takeIf { it.isNotBlank() && it != "9774d56d682e549c" }
            ?: error("Android system identity unavailable")
        val bytes = MessageDigest.getInstance("SHA-256").digest(
            "zurvan-free-trial-device-v1|android-id|$androidId".toByteArray(Charsets.UTF_8)
        )
        return bytes.joinToString("") { "%02x".format(it.toInt() and 0xff) }
    }

    /** A short-lived, random, URL-safe nonce; include this exact value in the
     * authenticated request so the server can check the verified verdict.
     * Never treat an unverified client-supplied nonce or token as permission.
     */
    fun createNonce(): String {
        val bytes = ByteArray(32)
        SecureRandom().nextBytes(bytes)
        return Base64.encodeToString(
            bytes, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING
        )
    }

    /**
     * Runs off the UI thread. Requires the app to be configured in Play Console
     * and signed as the linked package. The returned encrypted token must be
     * decoded by Google's Play Integrity API on the Supabase server.
     */
    fun integrityToken(context: Context, nonce: String): String {
        require(nonce.length in 32..128 && nonce.matches(Regex("[A-Za-z0-9_-]+")))
        val manager = IntegrityManagerFactory.create(context.applicationContext)
        val request = IntegrityTokenRequest.builder().setNonce(nonce).build()
        return Tasks.await(manager.requestIntegrityToken(request), 30, TimeUnit.SECONDS).token()
    }
}
