package com.bdfr.zurvan

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Supabase account-based mobile licensing. All requests authenticate with the
 * user's access token. Only the trusted Edge Function verifies Play Integrity
 * and updates Google-hosted device-recall state.
 */
internal object ZurvanAndroidOnlineLicense {
    private const val BASE = "https://yztithydgdvydaktuxlw.supabase.co"
    private const val KEY_ALIAS = "zurvan-online-license-aes-v1"
    private const val PREF = "zurvan-online-license-v1"
    private const val CIPHERTEXT = "session_ciphertext"
    private const val OFFLINE_CACHE_MS = 24L * 60L * 60L * 1000L

    private data class Session(
        var accessToken: String,
        var refreshToken: String,
        val authUserId: String,
        var canWrite: Boolean = false,
        var mode: String = "unknown",
        var daysRemaining: Int = 0,
        var pahlaviOccasions: Boolean = false,
        var verifiedAtMs: Long = 0L
    ) {
        fun json() = JSONObject()
            .put("access_token", accessToken)
            .put("refresh_token", refreshToken)
            .put("auth_user_id", authUserId)
            .put("can_write", canWrite)
            .put("mode", mode)
            .put("days_remaining", daysRemaining)
            .put("pahlavi_occasions", pahlaviOccasions)
            .put("verified_at_ms", verifiedAtMs)
        companion object {
            fun fromJson(value: JSONObject) = Session(
                value.getString("access_token"), value.getString("refresh_token"),
                value.getString("auth_user_id"), value.optBoolean("can_write", false),
                value.optString("mode", "unknown"), value.optInt("days_remaining", 0),
                value.optBoolean("pahlavi_occasions", false),
                value.optLong("verified_at_ms", 0)
            )
        }
    }

    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        generator.init(KeyGenParameterSpec.Builder(
            KEY_ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
        ).setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setRandomizedEncryptionRequired(true)
            .build())
        return generator.generateKey()
    }

    @Synchronized
    private fun save(context: Context, session: Session) {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        val bytes = cipher.doFinal(session.json().toString().toByteArray(Charsets.UTF_8))
        val combined = cipher.iv + bytes
        context.applicationContext.getSharedPreferences(PREF, Context.MODE_PRIVATE).edit()
            .putString(CIPHERTEXT, Base64.encodeToString(combined, Base64.NO_WRAP)).commit()
    }

    @Synchronized
    private fun load(context: Context): Session? {
        return try {
            val value = context.applicationContext.getSharedPreferences(PREF, Context.MODE_PRIVATE)
                .getString(CIPHERTEXT, null) ?: return null
            val bytes = Base64.decode(value, Base64.DEFAULT)
            require(bytes.size > 12 + 16)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, bytes.copyOfRange(0, 12)))
            Session.fromJson(JSONObject(String(cipher.doFinal(bytes.copyOfRange(12, bytes.size)), Charsets.UTF_8)))
        } catch (_: Exception) { null }
    }

    // Fail closed for expired or absent server policy. A separately signed
    // offline license remains independently valid.
    fun hasRecentServerEntitlement(context: Context): Boolean {
        val session = load(context) ?: return false
        val age = System.currentTimeMillis() - session.verifiedAtMs
        return session.canWrite && age in 0L..OFFLINE_CACHE_MS
    }

    fun activeOnlineMode(context: Context): String? =
        load(context)?.takeIf { hasRecentServerEntitlement(context) }?.mode

    fun onlineTrialDaysRemaining(context: Context): Int =
        load(context)?.takeIf { hasRecentServerEntitlement(context) }
            ?.daysRemaining?.coerceAtLeast(0) ?: 0

    fun hasPahlaviAccess(context: Context): Boolean {
        val session = load(context) ?: return false
        val age = System.currentTimeMillis() - session.verifiedAtMs
        return session.pahlaviOccasions && age in 0L..OFFLINE_CACHE_MS
    }

    fun hasLinkedOnlineAccount(context: Context): Boolean = load(context) != null

    private fun post(path: String, body: JSONObject, token: String? = null): JSONObject {
        val connection = (URL(BASE + path).openConnection() as HttpURLConnection)
        try {
            connection.requestMethod = "POST"
            connection.connectTimeout = 12_000
            connection.readTimeout = 15_000
            connection.doOutput = true
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("apikey", BuildConfig.DESK_SUPABASE_PUBLISHABLE_KEY)
            if (token != null) connection.setRequestProperty("Authorization", "Bearer $token")
            connection.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            val result = if (connection.responseCode in 200..299) connection.inputStream
                else connection.errorStream
            val raw = result?.use { it.readBytes().toString(Charsets.UTF_8) } ?: ""
            val json = runCatching { JSONObject(raw) }.getOrDefault(JSONObject())
            if (connection.responseCode !in 200..299)
                error("سرور زروان: " + json.optString("error", "HTTP " + connection.responseCode))
            return json
        } finally { connection.disconnect() }
    }

    private fun status(context: Context, session: Session): JSONObject =
        post("/functions/v1/zurvan-license-status",
            JSONObject().put("key_fingerprint", ZurvanMobileDeviceProof.fingerprint(context)),
            session.accessToken)

    private fun applyStatus(context: Context, session: Session, response: JSONObject) {
        session.canWrite = response.optBoolean("can_write", false)
        session.mode = response.optString("mode", "unknown")
        session.daysRemaining = response.optInt("days_remaining", 0)
        session.pahlaviOccasions = response.optJSONObject("features")
            ?.optBoolean("pahlavi_occasions", false) == true
        session.verifiedAtMs = System.currentTimeMillis()
        save(context, session)
    }

    /**
     * Verify an existing paid subscription first. Otherwise request a
     * device-attested 45-day trial. Must run off the UI thread.
     */
    suspend fun signInAndActivate(context: Context, email: String, password: String): Boolean =
        withContext(Dispatchers.IO) {
            val result = post("/auth/v1/token?grant_type=password",
                JSONObject().put("email", email.trim()).put("password", password))
            val session = Session(result.getString("access_token"),
                result.getString("refresh_token"),
                result.getJSONObject("user").getString("id"))
            save(context, session)
            var current = status(context, session)
            if (!current.optBoolean("can_write", false)) {
                val fp = ZurvanMobileDeviceProof.fingerprint(context)
                val at = System.currentTimeMillis()
                val nonce = ZurvanMobileDeviceProof.nonceFor(session.authUserId, fp, at)
                val encryptedProof = ZurvanMobileDeviceProof.integrityToken(context, nonce)
                post("/functions/v1/zurvan-mobile-trial",
                    JSONObject().put("platform", "android")
                        .put("key_fingerprint", fp)
                        .put("integrity_token", encryptedProof)
                        .put("request_time_ms", at),
                    session.accessToken)
                current = status(context, session)
            }
            applyStatus(context, session, current)
            session.canWrite
        }

    /**
     * Points cannot be set on the client. The server accepts only enumerated
     * actions and enforces daily idempotency in Postgres.
     */
    suspend fun userProgress(
        context: Context, action: String = "status", visible: Boolean? = null
    ): JSONObject = withContext(Dispatchers.IO) {
        require(action in setOf("status", "claim_daily", "set_visibility"))
        require(action != "set_visibility" || visible != null)
        if (load(context) == null) error("ابتدا وارد حساب آنلاین زروان شوید.")
        // Refresh tokens through the normal secure session path. A non-writable
        // license must not block access to already-earned points and badges.
        refresh(context)
        val session = load(context) ?: error("نشست حساب زروان موجود نیست.")
        val body = JSONObject().put("action", action)
        if (visible != null) body.put("visible", visible)
        post("/functions/v1/zurvan-user-progress", body, session.accessToken)
    }

    /**
     * Fetch the restricted-feature grant directly on opening Calendar Settings.
     * Unlike the license's offline fallback, a failed server check never
     * reveals this premium setting.
     */
    suspend fun refreshPahlaviGrant(context: Context): Boolean = withContext(Dispatchers.IO) {
        val session = load(context) ?: return@withContext false
        try {
            val refreshed = post("/auth/v1/token?grant_type=refresh_token",
                JSONObject().put("refresh_token", session.refreshToken))
            session.accessToken = refreshed.getString("access_token")
            session.refreshToken = refreshed.getString("refresh_token")
            save(context, session)
            val current = status(context, session)
            applyStatus(context, session, current)
            session.pahlaviOccasions && hasPahlaviAccess(context)
        } catch (_: Exception) { false }
    }

    suspend fun refresh(context: Context): Boolean = withContext(Dispatchers.IO) {
        val session = load(context) ?: return@withContext false
        try {
            val refreshed = post("/auth/v1/token?grant_type=refresh_token",
                JSONObject().put("refresh_token", session.refreshToken))
            session.accessToken = refreshed.getString("access_token")
            session.refreshToken = refreshed.getString("refresh_token")
            // If online refresh fails, use the last server policy for up to
            // OFFLINE_CACHE_MS; never extend the cache on network failure.
            val current = status(context, session)
            applyStatus(context, session, current)
            session.canWrite
        } catch (_: Exception) { hasRecentServerEntitlement(context) }
    }
}
