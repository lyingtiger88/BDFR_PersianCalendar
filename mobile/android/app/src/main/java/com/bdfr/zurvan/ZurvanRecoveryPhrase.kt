package com.bdfr.zurvan

import android.content.Context
import android.util.Base64
import java.security.MessageDigest
import java.security.SecureRandom
import java.text.Normalizer
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

data class ZurvanCloudRecoveryEnvelopeAndroid(
    val protocolVersion: Int,
    val vaultId: String,
    val wordCount: Int,
    val phraseFingerprint: String,
    val nonceBase64: String,
    val wrappedVaultKeyBase64: String,
    val tagBase64: String
)

object ZurvanRecoveryPhraseAndroid {
    const val DEFAULT_WORD_COUNT = 24
    private val supportedWordCounts = setOf(12, 15, 18, 21, 24)

    fun generate(
        context: Context,
        wordCount: Int = DEFAULT_WORD_COUNT
    ): String {
        require(wordCount in supportedWordCounts) {
            "BIP39 recovery phrase word count is invalid."
        }

        val entropyBits = wordCount * 11 * 32 / 33
        val entropy = ByteArray(entropyBits / 8)
        SecureRandom().nextBytes(entropy)

        return try {
            encodeEntropy(context, entropy)
        } finally {
            entropy.fill(0)
        }
    }

    fun encodeEntropy(
        context: Context,
        entropy: ByteArray
    ): String {
        val entropyBits = entropy.size * 8
        require(entropyBits in setOf(128, 160, 192, 224, 256)) {
            "BIP39 entropy length is invalid."
        }

        val checksumBits = entropyBits / 32
        val totalBits = entropyBits + checksumBits
        val wordCount = totalBits / 11
        val checksum = MessageDigest
            .getInstance("SHA-256")
            .digest(entropy)
        val words = loadWordList(context)

        return (0 until wordCount).joinToString(" ") { word ->
            var index = 0

            repeat(11) { bit ->
                val globalBit = word * 11 + bit
                val value = if (globalBit < entropyBits) {
                    readBit(entropy, globalBit)
                } else {
                    readBit(checksum, globalBit - entropyBits)
                }

                index = (index shl 1) or value
            }

            words[index]
        }
    }

    fun decodeEntropy(
        context: Context,
        phrase: String
    ): ByteArray {
        val phraseWords = normalizePhrase(phrase)
            .split(" ")
            .filter { it.isNotEmpty() }

        require(phraseWords.size in supportedWordCounts) {
            "Recovery phrase word count is invalid."
        }

        val words = loadWordList(context)
        val index = words.withIndex().associate {
            it.value to it.index
        }

        val totalBits = phraseWords.size * 11
        val entropyBits = totalBits * 32 / 33
        val checksumBits = entropyBits / 32
        val entropy = ByteArray(entropyBits / 8)
        var checksumValue = 0

        phraseWords.forEachIndexed { wordIndex, word ->
            val value = index[word]
                ?: throw IllegalArgumentException(
                    "Unknown recovery word at position ${wordIndex + 1}."
                )

            repeat(11) { bit ->
                val bitValue = (value shr (10 - bit)) and 1
                val globalBit = wordIndex * 11 + bit

                if (globalBit < entropyBits) {
                    writeBit(entropy, globalBit, bitValue)
                } else {
                    checksumValue =
                        (checksumValue shl 1) or bitValue
                }
            }
        }

        val hash = MessageDigest.getInstance("SHA-256").digest(entropy)
        var expected = 0
        repeat(checksumBits) { bit ->
            expected = (expected shl 1) or readBit(hash, bit)
        }

        if (checksumValue != expected) {
            entropy.fill(0)
            throw IllegalArgumentException(
                "Recovery phrase checksum is invalid."
            )
        }

        return entropy
    }

    fun fingerprint(
        context: Context,
        phrase: String
    ): String {
        val entropy = decodeEntropy(context, phrase)

        return try {
            MessageDigest.getInstance("SHA-256")
                .digest(entropy)
                .take(8)
                .joinToString("") { "%02x".format(it) }
        } finally {
            entropy.fill(0)
        }
    }

    fun wrapVaultKey(
        context: Context,
        phrase: String,
        vaultId: String,
        vaultKey: ByteArray
    ): ZurvanCloudRecoveryEnvelopeAndroid {
        require(vaultKey.size == 32) {
            "Vault key must be 32 bytes."
        }

        val normalized = normalizePhrase(phrase)
        val wordCount = normalized
            .split(" ")
            .count { it.isNotEmpty() }
        require(wordCount in supportedWordCounts)

        val recoveryKey = deriveRecoveryKey(
            context,
            normalized,
            vaultId
        )

        return try {
            val nonce = ByteArray(12).also(
                SecureRandom()::nextBytes
            )
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(
                Cipher.ENCRYPT_MODE,
                SecretKeySpec(recoveryKey, "AES"),
                GCMParameterSpec(128, nonce)
            )
            cipher.updateAAD(
                recoveryAad(1, vaultId, wordCount)
            )

            val combined = cipher.doFinal(vaultKey)
            val wrapped = combined.copyOfRange(
                0,
                combined.size - 16
            )
            val tag = combined.copyOfRange(
                combined.size - 16,
                combined.size
            )

            ZurvanCloudRecoveryEnvelopeAndroid(
                protocolVersion = 1,
                vaultId = vaultId,
                wordCount = wordCount,
                phraseFingerprint = fingerprint(
                    context,
                    normalized
                ),
                nonceBase64 = Base64.encodeToString(
                    nonce,
                    Base64.NO_WRAP
                ),
                wrappedVaultKeyBase64 =
                    Base64.encodeToString(
                        wrapped,
                        Base64.NO_WRAP
                    ),
                tagBase64 = Base64.encodeToString(
                    tag,
                    Base64.NO_WRAP
                )
            )
        } finally {
            recoveryKey.fill(0)
        }
    }

    fun unwrapVaultKey(
        context: Context,
        phrase: String,
        envelope: ZurvanCloudRecoveryEnvelopeAndroid
    ): ByteArray {
        require(envelope.protocolVersion == 1) {
            "Unsupported recovery protocol version."
        }

        val normalized = normalizePhrase(phrase)
        val wordCount = normalized
            .split(" ")
            .count { it.isNotEmpty() }

        require(wordCount == envelope.wordCount) {
            "Recovery phrase word count does not match."
        }

        val actualFingerprint = fingerprint(
            context,
            normalized
        )

        require(
            actualFingerprint.equals(
                envelope.phraseFingerprint,
                ignoreCase = true
            )
        ) {
            "Recovery phrase does not match this vault."
        }

        val recoveryKey = deriveRecoveryKey(
            context,
            normalized,
            envelope.vaultId
        )

        return try {
            val nonce = Base64.decode(
                envelope.nonceBase64,
                Base64.DEFAULT
            )
            val wrapped = Base64.decode(
                envelope.wrappedVaultKeyBase64,
                Base64.DEFAULT
            )
            val tag = Base64.decode(
                envelope.tagBase64,
                Base64.DEFAULT
            )

            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(
                Cipher.DECRYPT_MODE,
                SecretKeySpec(recoveryKey, "AES"),
                GCMParameterSpec(128, nonce)
            )
            cipher.updateAAD(
                recoveryAad(
                    envelope.protocolVersion,
                    envelope.vaultId,
                    envelope.wordCount
                )
            )

            val key = cipher.doFinal(wrapped + tag)
            require(key.size == 32) {
                "Recovered vault key length is invalid."
            }
            key
        } finally {
            recoveryKey.fill(0)
        }
    }

    private fun deriveRecoveryKey(
        context: Context,
        phrase: String,
        vaultId: String
    ): ByteArray {
        val entropy = decodeEntropy(context, phrase)

        return try {
            val mac = Mac.getInstance("HmacSHA256")
            mac.init(
                SecretKeySpec(entropy, "HmacSHA256")
            )
            mac.doFinal(
                "zurvan-recovery-v1|$vaultId"
                    .toByteArray(Charsets.UTF_8)
            )
        } finally {
            entropy.fill(0)
        }
    }

    private fun recoveryAad(
        protocolVersion: Int,
        vaultId: String,
        wordCount: Int
    ): ByteArray =
        "zurvan-recovery-v1|$protocolVersion|$vaultId|$wordCount"
            .toByteArray(Charsets.UTF_8)

    private fun normalizePhrase(value: String): String =
        Normalizer.normalize(
            value.trim().lowercase(),
            Normalizer.Form.NFKD
        )
            .split(Regex("\\s+"))
            .filter { it.isNotEmpty() }
            .joinToString(" ")

    private fun loadWordList(context: Context): List<String> =
        context.assets
            .open("bip39-english.txt")
            .bufferedReader()
            .useLines { lines ->
                lines
                    .map { it.trim() }
                    .filter { it.isNotEmpty() }
                    .toList()
            }
            .also {
                require(it.size == 2048) {
                    "BIP39 wordlist must contain 2048 words."
                }
            }

    private fun readBit(
        bytes: ByteArray,
        bitIndex: Int
    ): Int {
        val byteIndex = bitIndex / 8
        val shift = 7 - (bitIndex % 8)
        return (bytes[byteIndex].toInt() ushr shift) and 1
    }

    private fun writeBit(
        bytes: ByteArray,
        bitIndex: Int,
        value: Int
    ) {
        if (value == 0) return
        val byteIndex = bitIndex / 8
        val shift = 7 - (bitIndex % 8)
        bytes[byteIndex] =
            (bytes[byteIndex].toInt() or (1 shl shift)).toByte()
    }
}
