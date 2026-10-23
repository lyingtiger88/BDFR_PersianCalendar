package com.bdfr.zurvan

import android.content.Context
import android.util.Base64
import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec

object ZurvanAppLock {
    private const val PREFS = "zurvan_app_lock"
    fun enabled(context: Context): Boolean = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        .contains("verifier")
    private fun hash(pin: String, salt: ByteArray): ByteArray {
        val spec = PBEKeySpec(pin.toCharArray(), salt, 120_000, 256)
        return try { SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(spec).encoded }
        finally { spec.clearPassword() }
    }
    fun set(context: Context, pin: String): Boolean {
        if (pin.length !in 4..64) return false
        val salt = ByteArray(16).also { SecureRandom().nextBytes(it) }
        val verifier = hash(pin, salt)
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putString("salt", Base64.encodeToString(salt, Base64.NO_WRAP))
            .putString("verifier", Base64.encodeToString(verifier, Base64.NO_WRAP)).apply()
        return true
    }
    fun check(context: Context, pin: String): Boolean {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val salt = prefs.getString("salt", null) ?: return false
        val verifier = prefs.getString("verifier", null) ?: return false
        return runCatching {
            MessageDigest.isEqual(hash(pin, Base64.decode(salt, Base64.NO_WRAP)),
                Base64.decode(verifier, Base64.NO_WRAP))
        }.getOrDefault(false)
    }
    fun remove(context: Context, currentPin: String): Boolean {
        if (!check(context, currentPin)) return false
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().clear().apply()
        return true
    }
}
