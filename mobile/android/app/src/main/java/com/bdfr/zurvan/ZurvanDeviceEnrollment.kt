package com.bdfr.zurvan

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.bouncycastle.crypto.params.Ed25519PrivateKeyParameters
import org.bouncycastle.crypto.signers.Ed25519Signer
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.security.KeyStore
import java.security.SecureRandom
import java.security.MessageDigest
import android.provider.Settings
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Device identity for account onboarding.
 * The Ed25519 seed is AES-GCM-wrapped by Android Keystore before storage.
 * Note: it is a SOFTWARE signing key, not a hardware non-exportable Ed25519 key.
 * If Keystore is unavailable, enrollment fails closed.
 */
private class ZurvanEnrollmentDeviceKey(private val context: Context, accountScope: String) {
    private val scopeHash = MessageDigest.getInstance("SHA-256")
        .digest(accountScope.trim().lowercase().toByteArray(Charsets.UTF_8))
        .take(8).joinToString("") { "%02x".format(it) }
    private val prefs = context.getSharedPreferences("zurvan_device_enroll_key_$scopeHash", Context.MODE_PRIVATE)
    private fun aesKey(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        val existing = store.getKey("zurvan_device_enroll_wrap_v1", null) as? SecretKey
        if (existing != null) return existing
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("zurvan_device_enroll_wrap_v1",
                    KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build())
        }.generateKey()
    }
    private fun seed(): ByteArray {
        val saved = prefs.getString("seed_ciphertext", null)
        if (saved != null) {
            val iv = prefs.getString("seed_iv", null)
                ?: error("کلید دستگاه ناقص است؛ برای بازیابی از پشتیبانی استفاده کنید.")
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, aesKey(),
                GCMParameterSpec(128, Base64.decode(iv, Base64.NO_WRAP)))
            return cipher.doFinal(Base64.decode(saved, Base64.NO_WRAP))
                .also { require(it.size == 32) }
        }
        val privateSeed = ByteArray(32).also { SecureRandom().nextBytes(it) }
        try {
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.ENCRYPT_MODE, aesKey())
            val wrapped = cipher.doFinal(privateSeed)
            check(prefs.edit()
                .putString("seed_ciphertext", Base64.encodeToString(wrapped, Base64.NO_WRAP))
                .putString("seed_iv", Base64.encodeToString(cipher.iv, Base64.NO_WRAP))
                .commit()) { "خطا در ذخیره امن کلید دستگاه." }
            return privateSeed
        } catch (e: Exception) {
            privateSeed.fill(0)
            throw e
        }
    }
    private fun rawKey() = Ed25519PrivateKeyParameters(seed(), 0)
    fun publicKey(): String {
        val raw = rawKey().generatePublicKey().encoded
        return Base64.encodeToString(raw, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
    }
    fun sign(message: String): String {
        val signer = Ed25519Signer()
        signer.init(true, rawKey())
        val bytes = message.toByteArray(Charsets.UTF_8)
        signer.update(bytes, 0, bytes.size)
        return Base64.encodeToString(signer.generateSignature(),
            Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
    }
}

/** Requires a confirmed Supabase Auth session and signed device proof. */
class ZurvanDeviceEnrollment(private val context: Context) {
    suspend fun enroll(session: ZurvanAuthSession, username: String): JSONObject =
        withContext(Dispatchers.IO) {
            require(username.matches(Regex("^[a-z][a-z0-9_]{3,23}$"))) {
                "شناسه باید ۴ تا ۲۴ حرف انگلیسی کوچک، عدد یا زیرخط باشد و با حرف شروع شود."
            }
            require(BuildConfig.SUPABASE_URL.startsWith("https://") &&
                    BuildConfig.SUPABASE_PUBLISHABLE_KEY.isNotBlank()) {
                "تنظیمات Supabase نامعتبر است."
            }
            val key = ZurvanEnrollmentDeviceKey(context, session.email ?: error("ایمیل حساب در دسترس نیست."))
            // Hash the app-scoped Android ID; never transmit the raw identifier.
            val androidId = Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID)
                ?.takeIf { it.isNotBlank() } ?: error("شناسه پایدار دستگاه در دسترس نیست.")
            val hardwareFingerprint = MessageDigest.getInstance("SHA-256")
                .digest(("zurvan-android-hardware-v1:" + androidId).toByteArray(Charsets.UTF_8))
                .joinToString("") { "%02x".format(it.toInt() and 0xff) }
            val started = request(session.accessToken, JSONObject()
                .put("action", "start").put("username", username)
                .put("platform", "android").put("public_key", key.publicKey())
                .put("hardware_fingerprint", hardwareFingerprint))
            val id = started.getString("challenge_id")
            val nonce = started.getString("nonce")
            val userId = started.getString("user_id")
            val payload = "zurvan-auth-device-enrollment-v1:$id:$nonce:$userId:$username"
            request(session.accessToken, JSONObject()
                .put("action", "finish").put("challenge_id", id)
                .put("signature", key.sign(payload)))
        }

    private fun request(token: String, body: JSONObject): JSONObject {
        val url = URL(BuildConfig.SUPABASE_URL.trimEnd('/') +
            "/functions/v1/authenticated-device-enrollment")
        val c = url.openConnection() as HttpURLConnection
        try {
            c.requestMethod = "POST"
            c.connectTimeout = 15000
            c.readTimeout = 20000
            c.doOutput = true
            c.setRequestProperty("apikey", BuildConfig.SUPABASE_PUBLISHABLE_KEY)
            c.setRequestProperty("Authorization", "Bearer $token")
            c.setRequestProperty("Content-Type", "application/json")
            c.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            val status = c.responseCode
            val raw = (if (status in 200..299) c.inputStream else c.errorStream)
                ?.bufferedReader()?.use { it.readText() }.orEmpty()
            val result = JSONObject(raw.ifBlank { "{}" })
            if (status !in 200..299) {
                throw IllegalStateException(result.optString("error").ifBlank {
                    "ثبت دستگاه با خطای HTTP $status مواجه شد."
                })
            }
            return result
        } finally { c.disconnect() }
    }
}
