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
import javax.crypto.spec.GCMParameterSpec

/** Independent account sign-in; deliberately does not grant a software license. */
data class ZurvanAuthSession(val accessToken: String, val refreshToken: String, val expiresAt: Long, val email: String?)

class ZurvanAuth(private val context: Context) {
    private val prefs = context.getSharedPreferences("zurvan_auth_secure_v1", Context.MODE_PRIVATE)
    private val apiUrl = BuildConfig.SUPABASE_URL.trimEnd('/')
    private val apiKey = BuildConfig.SUPABASE_PUBLISHABLE_KEY
    val configured get() = apiUrl.startsWith("https://") && apiKey.isNotBlank()

    private fun secretKey(): javax.crypto.SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey("zurvan_auth_session_v1", null) as? javax.crypto.SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("zurvan_auth_session_v1", KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build())
        }.generateKey()
    }
    private fun persist(session: ZurvanAuthSession) {
        val payload = JSONObject().put("access_token", session.accessToken)
            .put("refresh_token", session.refreshToken).put("expires_at", session.expiresAt)
            .put("email", session.email).toString().toByteArray(Charsets.UTF_8)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, secretKey())
        val encrypted = cipher.doFinal(payload)
        prefs.edit().putString("iv", Base64.encodeToString(cipher.iv, Base64.NO_WRAP))
            .putString("payload", Base64.encodeToString(encrypted, Base64.NO_WRAP)).apply()
    }
    fun session(): ZurvanAuthSession? { return try {
        val iv = Base64.decode(prefs.getString("iv", null) ?: return null, Base64.DEFAULT)
        val payload = Base64.decode(prefs.getString("payload", null) ?: return null, Base64.DEFAULT)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, secretKey(), GCMParameterSpec(128, iv))
        JSONObject(String(cipher.doFinal(payload), Charsets.UTF_8)).let {
            ZurvanAuthSession(it.getString("access_token"), it.getString("refresh_token"),
                it.getLong("expires_at"), it.optString("email").takeIf(String::isNotBlank))
        }
    } catch (_: Exception) {
        clearLocal()
        null
    } }
    private fun clearLocal() { prefs.edit().clear().apply() }
    suspend fun signIn(email: String, password: String): ZurvanAuthSession = withContext(Dispatchers.IO) {
        require(configured) { "Supabase is not configured for this build" }
        require(email.isNotBlank() && password.isNotBlank()) { "Enter email and password" }
        val reply = request("/auth/v1/token?grant_type=password",
            JSONObject().put("email", email.trim()).put("password", password))
        saveResponse(reply)
    }
    suspend fun signUp(email: String, password: String): Boolean = withContext(Dispatchers.IO) {
        require(configured) { "Supabase is not configured for this build" }
        require(email.isNotBlank() && password.length >= 8) { "Password must be at least 8 characters" }
        val reply = request("/auth/v1/signup", JSONObject().put("email", email.trim()).put("password", password))
        // Email confirmation may be required. Do not treat an unconfirmed signup as signed in.
        if (reply.optString("access_token").isNotBlank()) saveResponse(reply)
        reply.optString("access_token").isNotBlank()
    }
    suspend fun resendSignupConfirmation(email: String) = withContext(Dispatchers.IO) {
        require(configured) { "Supabase is not configured for this build" }
        require(email.isNotBlank()) { "Enter your email address" }
        request("/auth/v1/resend", JSONObject().put("type", "signup").put("email", email.trim())
            .put("options", JSONObject().put("emailRedirectTo", "com.bdfr.zurvan://auth/callback")))
        Unit
    }
    suspend fun completeEmailCallback(uri: android.net.Uri) = withContext(Dispatchers.IO) {
        require(configured) { "Supabase is not configured for this build" }
        val fragment = uri.fragment?.let { android.net.Uri.parse("https://callback.invalid/?$it") }
        val access = uri.getQueryParameter("access_token") ?: fragment?.getQueryParameter("access_token")
        val refresh = uri.getQueryParameter("refresh_token") ?: fragment?.getQueryParameter("refresh_token")
        if (!access.isNullOrBlank() && !refresh.isNullOrBlank()) {
            val expiresIn = (uri.getQueryParameter("expires_in") ?: fragment?.getQueryParameter("expires_in"))?.toLongOrNull() ?: 3600L
            val email = uri.getQueryParameter("email") ?: fragment?.getQueryParameter("email")
            persist(ZurvanAuthSession(access, refresh, System.currentTimeMillis() / 1000 + expiresIn, email))
            return@withContext
        }
        val tokenHash = uri.getQueryParameter("token_hash") ?: fragment?.getQueryParameter("token_hash")
        val type = uri.getQueryParameter("type") ?: fragment?.getQueryParameter("type") ?: "signup"
        if (!tokenHash.isNullOrBlank()) {
            saveResponse(request("/auth/v1/verify", JSONObject().put("token_hash", tokenHash).put("type", type)))
            return@withContext
        }
        throw IllegalStateException("لینک تأیید ناقص یا منقضی شده است. ایمیل تأیید جدید درخواست کنید.")
    }
    suspend fun sendPasswordRecovery(email: String) = withContext(Dispatchers.IO) {
        require(configured) { "Supabase is not configured for this build" }
        require(email.isNotBlank()) { "Enter your email address" }
        request("/auth/v1/recover", JSONObject().put("email", email.trim()))
        Unit
    }
    suspend fun validSession(): ZurvanAuthSession? = withContext(Dispatchers.IO) {
        val current = session() ?: return@withContext null
        if (System.currentTimeMillis() / 1000 + 60 < current.expiresAt) return@withContext current
        if (!configured) return@withContext null
        try {
            saveResponse(request("/auth/v1/token?grant_type=refresh_token",
                JSONObject().put("refresh_token", current.refreshToken)), current.email)
        } catch (_: Exception) { null }
    }
    suspend fun signOut() = withContext(Dispatchers.IO) {
        val access = session()?.accessToken
        try {
            if (configured && access != null) request("/auth/v1/logout", JSONObject(), access)
        } catch (_: Exception) { /* Still clear local tokens if remote logout fails. */ }
        clearLocal()
    }
    private fun saveResponse(reply: JSONObject, fallbackEmail: String? = null): ZurvanAuthSession {
        val access = reply.getString("access_token")
        val refresh = reply.getString("refresh_token")
        val email = reply.optJSONObject("user")?.optString("email")?.takeIf { it.isNotBlank() } ?: fallbackEmail
        val record = ZurvanAuthSession(access, refresh,
            System.currentTimeMillis() / 1000 + reply.optLong("expires_in", 3600), email)
        persist(record)
        return record
    }
    private fun request(path: String, payload: JSONObject, accessToken: String? = null): JSONObject {
        val connection = (URL(apiUrl + path).openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            connectTimeout = 12000
            readTimeout = 12000
            doOutput = true
            setRequestProperty("Content-Type", "application/json")
            setRequestProperty("apikey", apiKey)
            if (accessToken != null) setRequestProperty("Authorization", "Bearer $accessToken")
        }
        try {
            connection.outputStream.use { it.write(payload.toString().toByteArray(Charsets.UTF_8)) }
            val raw = (if (connection.responseCode in 200..299) connection.inputStream else connection.errorStream)
                ?.bufferedReader()?.use { it.readText() } ?: "{}"
            val body = JSONObject(raw)
            if (connection.responseCode !in 200..299) {
                throw IllegalStateException(body.optString("msg",
                    body.optString("error_description", body.optString("message", "Authentication failed"))))
            }
            return body
        } finally { connection.disconnect() }
    }
}
