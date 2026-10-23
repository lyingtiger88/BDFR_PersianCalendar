package com.bdfr.zurvan

import android.content.Context
import android.provider.Settings
import android.util.Base64
import java.security.KeyFactory
import java.security.MessageDigest
import java.security.Signature
import java.security.spec.MGF1ParameterSpec
import java.security.spec.PSSParameterSpec
import java.security.spec.X509EncodedKeySpec
import org.json.JSONObject

object ZurvanActivation {
    fun deviceCode(context: Context): String {
        val id = Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID) ?: "unavailable"
        val bytes = MessageDigest.getInstance("SHA-256")
            .digest("zurvan-license-device-v1|android|$id".toByteArray(Charsets.UTF_8))
        return "ZRV-" + bytes.take(12).joinToString("") { "%02X".format(it) }
    }

    fun validate(context: Context, code: String): Boolean {
        val keyPem = context.getSharedPreferences("zurvan_license", Context.MODE_PRIVATE)
            .getString("public_key_pem", null) ?: return false
        return runCatching {
            val parts = code.trim().split('.')
            if (parts.size != 3 || parts[0] != "ZRV1") return false
            fun decode(value: String): ByteArray = Base64.decode(value, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
            val payload = decode(parts[1])
            val signatureBytes = decode(parts[2])
            val keyBytes = Base64.decode(keyPem.replace(Regex("-----[^-]+-----|\\s"), ""), Base64.DEFAULT)
            val key = KeyFactory.getInstance("RSA").generatePublic(X509EncodedKeySpec(keyBytes))
            val verifier = Signature.getInstance("RSASSA-PSS").apply {
                setParameter(PSSParameterSpec("SHA-256", "MGF1", MGF1ParameterSpec.SHA256, 32, 1))
                initVerify(key)
                update(payload)
            }
            if (!verifier.verify(signatureBytes)) return false
            val json = JSONObject(String(payload, Charsets.UTF_8))
            if (json.optString("Product") != "zurvan" || json.optString("DeviceCode") != deviceCode(context)) return false
            val expiry = json.optString("ExpiresUtc")
            if (expiry.isNotBlank() && expiry != "null") {
                if (java.time.Instant.parse(expiry).isBefore(java.time.Instant.now())) return false
            }
            true
        }.getOrDefault(false)
    }
}
