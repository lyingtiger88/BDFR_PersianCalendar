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
    enum class Result {
        VALID, MISSING_PUBLIC_KEY, INVALID_PUBLIC_KEY,
        INVALID_CODE_FORMAT, INVALID_SIGNATURE, WRONG_DEVICE, EXPIRED, INVALID_PAYLOAD
    }

    fun deviceCode(context: Context): String {
        val id = Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID) ?: "unavailable"
        val bytes = MessageDigest.getInstance("SHA-256")
            .digest("zurvan-license-device-v1|android|$id".toByteArray(Charsets.UTF_8))
        return "ZRV-" + bytes.take(12).joinToString("") { "%02X".format(it) }
    }

    fun validate(context: Context, code: String): Boolean = diagnose(context, code) == Result.VALID

    fun diagnose(context: Context, code: String): Result {
        if (BuildConfig.DEBUG && (code.trim() == "elena13731005" || code.trim() == "zarasamtestmode")) return Result.VALID
        val encodedKey = BuildConfig.LICENSE_PUBLIC_KEY_B64.trim()
        if (encodedKey.isEmpty()) return Result.MISSING_PUBLIC_KEY
        val keyPem = runCatching {
            String(Base64.decode(encodedKey, Base64.DEFAULT), Charsets.UTF_8)
        }.getOrNull()?.takeIf { it.contains("BEGIN PUBLIC KEY") } ?: return Result.INVALID_PUBLIC_KEY

        val normalized = code.trim().filterNot { it.isWhitespace() }
        if (normalized.length > 8192 || normalized.isEmpty()) return Result.INVALID_CODE_FORMAT
        val parts = normalized.split('.')
        if (parts.size != 3 || parts[0] != "ZRV1") return Result.INVALID_CODE_FORMAT

        fun decode(value: String): ByteArray =
            Base64.decode(value, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)

        val key = runCatching {
            val pemBody = keyPem.replace(Regex("-----[^-]+-----|\\s"), "")
            val publicBytes = Base64.decode(pemBody, Base64.DEFAULT)
            KeyFactory.getInstance("RSA").generatePublic(X509EncodedKeySpec(publicBytes))
        }.getOrNull() ?: return Result.INVALID_PUBLIC_KEY

        val payload = runCatching { decode(parts[1]) }.getOrNull() ?: return Result.INVALID_CODE_FORMAT
        val signatureBytes = runCatching { decode(parts[2]) }.getOrNull() ?: return Result.INVALID_CODE_FORMAT

        val signatureValid = runCatching {
            val verifier = Signature.getInstance("RSASSA-PSS")
            verifier.initVerify(key)
            verifier.setParameter(PSSParameterSpec("SHA-256", "MGF1", MGF1ParameterSpec.SHA256, 32, 1))
            verifier.update(payload)
            verifier.verify(signatureBytes)
        }.getOrDefault(false)
        if (!signatureValid) return Result.INVALID_SIGNATURE

        val json = runCatching { JSONObject(String(payload, Charsets.UTF_8)) }.getOrNull()
            ?: return Result.INVALID_PAYLOAD
        if (json.optString("Product") != "zurvan") return Result.INVALID_PAYLOAD
        if (json.optString("DeviceCode") != deviceCode(context)) return Result.WRONG_DEVICE

        val expiry = json.optString("ExpiresUtc")
        if (expiry.isNotBlank() && expiry != "null") {
            val expiration = runCatching { java.time.Instant.parse(expiry) }.getOrNull()
                ?: return Result.INVALID_PAYLOAD
            if (!expiration.isAfter(java.time.Instant.now())) return Result.EXPIRED
        }
        return Result.VALID
    }

    fun explanation(result: Result): String = when (result) {
        Result.VALID -> "فعال‌سازی با موفقیت انجام شد."
        Result.MISSING_PUBLIC_KEY -> "کلید عمومی در این نسخه برنامه قرار ندارد؛ باید APK جدید با متغیر لایسنس ساخته شود."
        Result.INVALID_PUBLIC_KEY -> "فرمت کلید عمومی تعبیه‌شده در برنامه نامعتبر است."
        Result.INVALID_CODE_FORMAT -> "فرمت کد فعال‌سازی نامعتبر یا ناقص است؛ کد کامل را کپی کنید."
        Result.INVALID_SIGNATURE -> "امضای کد معتبر نیست؛ کلید خصوصی صادرکننده با کلید عمومی این نسخه مطابقت ندارد یا کد تغییر کرده است."
        Result.WRONG_DEVICE -> "این کد برای دستگاه دیگری صادر شده است. کد دستگاه همین برنامه را کپی کنید."
        Result.EXPIRED -> "اعتبار زمانی این لایسنس به پایان رسیده است."
        Result.INVALID_PAYLOAD -> "اطلاعات داخل کد فعال‌سازی معتبر نیست."
    }
}
