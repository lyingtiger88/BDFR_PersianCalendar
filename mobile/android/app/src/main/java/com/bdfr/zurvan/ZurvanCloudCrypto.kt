package com.bdfr.zurvan

import android.util.Base64
import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

data class ZurvanCloudEncryptedPayload(
    val nonceBase64: String,
    val ciphertextBase64: String,
    val tagBase64: String
)

object ZurvanCloudCryptoAndroid {
    private val associatedData = "zurvan-cloud-v1".toByteArray(Charsets.UTF_8)

    fun createVaultKey(): ByteArray =
        ByteArray(32).also(SecureRandom()::nextBytes)

    fun encodeRecoveryKey(vaultKey: ByteArray): String {
        require(vaultKey.size == 32) {
            "Zurvan Cloud vault key must be 32 bytes."
        }
        return Base64.encodeToString(
            vaultKey,
            Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING
        )
    }

    fun decodeRecoveryKey(value: String): ByteArray {
        val key = Base64.decode(value.trim(), Base64.URL_SAFE or Base64.NO_WRAP)
        require(key.size == 32) {
            "Zurvan Cloud recovery key is invalid."
        }
        return key
    }

    fun encrypt(
        plaintext: ByteArray,
        vaultKey: ByteArray,
        vaultId: String,
        objectId: String
    ): ZurvanCloudEncryptedPayload {
        val objectKey = deriveObjectKey(vaultKey, vaultId, objectId)
        val nonce = ByteArray(12).also(SecureRandom()::nextBytes)

        return try {
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(
                Cipher.ENCRYPT_MODE,
                SecretKeySpec(objectKey, "AES"),
                GCMParameterSpec(128, nonce)
            )
            cipher.updateAAD(associatedData)

            val combined = cipher.doFinal(plaintext)
            val ciphertext = combined.copyOfRange(0, combined.size - 16)
            val tag = combined.copyOfRange(combined.size - 16, combined.size)

            ZurvanCloudEncryptedPayload(
                Base64.encodeToString(nonce, Base64.NO_WRAP),
                Base64.encodeToString(ciphertext, Base64.NO_WRAP),
                Base64.encodeToString(tag, Base64.NO_WRAP)
            )
        } finally {
            objectKey.fill(0)
        }
    }

    fun decrypt(
        payload: ZurvanCloudEncryptedPayload,
        vaultKey: ByteArray,
        vaultId: String,
        objectId: String
    ): ByteArray {
        val objectKey = deriveObjectKey(vaultKey, vaultId, objectId)

        return try {
            val nonce = Base64.decode(payload.nonceBase64, Base64.DEFAULT)
            val ciphertext = Base64.decode(payload.ciphertextBase64, Base64.DEFAULT)
            val tag = Base64.decode(payload.tagBase64, Base64.DEFAULT)

            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(
                Cipher.DECRYPT_MODE,
                SecretKeySpec(objectKey, "AES"),
                GCMParameterSpec(128, nonce)
            )
            cipher.updateAAD(associatedData)
            cipher.doFinal(ciphertext + tag)
        } finally {
            objectKey.fill(0)
        }
    }

    private fun deriveObjectKey(
        vaultKey: ByteArray,
        vaultId: String,
        objectId: String
    ): ByteArray {
        require(vaultKey.size == 32) {
            "Zurvan Cloud vault key must be 32 bytes."
        }

        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(vaultKey, "HmacSHA256"))
        return mac.doFinal(
            "zurvan-cloud-v1|$vaultId|$objectId"
                .toByteArray(Charsets.UTF_8)
        )
    }
}
