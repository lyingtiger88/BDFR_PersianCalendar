package com.bdfr.zurvan

import android.util.Base64
import java.math.BigInteger
import java.nio.ByteBuffer
import java.security.AlgorithmParameters
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.interfaces.ECPublicKey
import java.security.spec.ECGenParameterSpec
import java.security.spec.ECParameterSpec
import java.security.spec.ECPoint
import java.security.spec.ECPublicKeySpec
import javax.crypto.Cipher
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

class CompanionEphemeralKeyAndroid : AutoCloseable {
    private val keyPair: KeyPair = KeyPairGenerator.getInstance("EC").run {
        initialize(ECGenParameterSpec("secp256r1"), SecureRandom())
        generateKeyPair()
    }

    val publicKeyBase64: String
        get() = Base64.encodeToString(exportX963PublicKey(), Base64.NO_WRAP)

    val publicKeyFingerprint: String
        get() {
            val digest = MessageDigest.getInstance("SHA-256")
                .digest(exportX963PublicKey())
            return digest.take(8).joinToString("") { "%02X".format(it) }
        }

    fun deriveSessionKey(
        peerPublicKeyBase64: String,
        sessionId: String
    ): ByteArray {
        val peerBytes = Base64.decode(peerPublicKeyBase64, Base64.DEFAULT)
        require(peerBytes.size == 65 && peerBytes[0] == 0x04.toByte()) {
            "Companion public key must be a 65-byte P-256 X9.63 point."
        }

        val parameters = AlgorithmParameters.getInstance("EC").apply {
            init(ECGenParameterSpec("secp256r1"))
        }.getParameterSpec(ECParameterSpec::class.java)

        val x = BigInteger(1, peerBytes.copyOfRange(1, 33))
        val y = BigInteger(1, peerBytes.copyOfRange(33, 65))
        val peerSpec = ECPublicKeySpec(ECPoint(x, y), parameters)
        val peerPublic = java.security.KeyFactory
            .getInstance("EC")
            .generatePublic(peerSpec)

        val agreement = KeyAgreement.getInstance("ECDH")
        agreement.init(keyPair.private)
        agreement.doPhase(peerPublic, true)
        val rawSecret = agreement.generateSecret()

        return try {
            val mac = Mac.getInstance("HmacSHA256")
            mac.init(SecretKeySpec(rawSecret, "HmacSHA256"))
            mac.doFinal(
                "zurvan-companion-v1|$sessionId"
                    .toByteArray(Charsets.UTF_8)
            )
        } finally {
            rawSecret.fill(0)
        }
    }

    override fun close() {
        // Android KeyPair private material is managed by the provider and is not
        // directly zeroizable from application code.
    }

    private fun exportX963PublicKey(): ByteArray {
        val publicKey = keyPair.public as ECPublicKey
        val x = toFixed32(publicKey.w.affineX)
        val y = toFixed32(publicKey.w.affineY)

        return ByteArray(65).also { output ->
            output[0] = 0x04
            x.copyInto(output, 1)
            y.copyInto(output, 33)
        }
    }

    private fun toFixed32(value: BigInteger): ByteArray {
        val raw = value.toByteArray()
        val unsigned = if (raw.size > 32) raw.copyOfRange(raw.size - 32, raw.size) else raw
        return ByteArray(32).also {
            unsigned.copyInto(it, 32 - unsigned.size)
        }
    }

    companion object {
        private val associatedData =
            "zurvan-companion-v1".toByteArray(Charsets.UTF_8)

        fun computeSixDigitSas(sessionKey: ByteArray): String {
            val hash = MessageDigest.getInstance("SHA-256").digest(sessionKey)
            val value = ByteBuffer.wrap(hash, 0, 4).int.toLong() and 0xFFFFFFFFL
            return (value % 1_000_000L).toString().padStart(6, '0')
        }

        fun encrypt(
            plaintext: ByteArray,
            sessionKey: ByteArray
        ): EncryptedCompanionFrame {
            require(sessionKey.size == 32) {
                "Companion session key must be 32 bytes."
            }

            val nonce = ByteArray(12).also(SecureRandom()::nextBytes)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(
                Cipher.ENCRYPT_MODE,
                SecretKeySpec(sessionKey, "AES"),
                GCMParameterSpec(128, nonce)
            )
            cipher.updateAAD(associatedData)

            val combined = cipher.doFinal(plaintext)
            val ciphertext = combined.copyOfRange(0, combined.size - 16)
            val tag = combined.copyOfRange(combined.size - 16, combined.size)

            return EncryptedCompanionFrame(
                nonceBase64 = Base64.encodeToString(nonce, Base64.NO_WRAP),
                ciphertextBase64 = Base64.encodeToString(ciphertext, Base64.NO_WRAP),
                tagBase64 = Base64.encodeToString(tag, Base64.NO_WRAP)
            )
        }

        fun decrypt(
            frame: EncryptedCompanionFrame,
            sessionKey: ByteArray
        ): ByteArray {
            require(sessionKey.size == 32) {
                "Companion session key must be 32 bytes."
            }

            val nonce = Base64.decode(frame.nonceBase64, Base64.DEFAULT)
            val ciphertext = Base64.decode(frame.ciphertextBase64, Base64.DEFAULT)
            val tag = Base64.decode(frame.tagBase64, Base64.DEFAULT)
            val combined = ciphertext + tag

            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(
                Cipher.DECRYPT_MODE,
                SecretKeySpec(sessionKey, "AES"),
                GCMParameterSpec(128, nonce)
            )
            cipher.updateAAD(associatedData)
            return cipher.doFinal(combined)
        }
    }
}
