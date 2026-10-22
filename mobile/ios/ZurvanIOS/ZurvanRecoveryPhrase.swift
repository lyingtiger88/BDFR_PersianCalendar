import CryptoKit
import Foundation
import Security

struct ZurvanCloudRecoveryEnvelopeIOS: Codable, Sendable {
    let protocolVersion: Int
    let vaultId: String
    let wordCount: Int
    let phraseFingerprint: String
    let nonceBase64: String
    let wrappedVaultKeyBase64: String
    let tagBase64: String
}

enum ZurvanRecoveryPhraseIOS {
    static let defaultWordCount = 24
    private static let supportedWordCounts: Set<Int> =
        [12, 15, 18, 21, 24]

    static func generate(
        wordCount: Int = defaultWordCount
    ) throws -> String {
        guard supportedWordCounts.contains(wordCount) else {
            throw ZurvanRecoveryError.invalidWordCount
        }

        let entropyBits = wordCount * 11 * 32 / 33
        var bytes = [UInt8](
            repeating: 0,
            count: entropyBits / 8
        )

        let result = SecRandomCopyBytes(
            kSecRandomDefault,
            bytes.count,
            &bytes
        )

        guard result == errSecSuccess else {
            throw ZurvanRecoveryError.randomGenerationFailed
        }

        return try encodeEntropy(Data(bytes))
    }

    static func encodeEntropy(
        _ entropy: Data
    ) throws -> String {
        let entropyBits = entropy.count * 8

        guard [128, 160, 192, 224, 256]
            .contains(entropyBits) else {
            throw ZurvanRecoveryError.invalidEntropy
        }

        let checksumBits = entropyBits / 32
        let totalBits = entropyBits + checksumBits
        let wordCount = totalBits / 11
        let checksum = Data(SHA256.hash(data: entropy))
        let entropyBytes = [UInt8](entropy)
        let checksumBytes = [UInt8](checksum)
        let words = try loadWordList()

        var output: [String] = []
        output.reserveCapacity(wordCount)

        for wordIndex in 0..<wordCount {
            var index = 0

            for bit in 0..<11 {
                let globalBit = wordIndex * 11 + bit
                let value: Int

                if globalBit < entropyBits {
                    value = readBit(
                        entropyBytes,
                        globalBit
                    )
                } else {
                    value = readBit(
                        checksumBytes,
                        globalBit - entropyBits
                    )
                }

                index = (index << 1) | value
            }

            output.append(words[index])
        }

        return output.joined(separator: " ")
    }

    static func decodeEntropy(
        _ phrase: String
    ) throws -> Data {
        let phraseWords = normalizePhrase(phrase)
            .split(separator: " ")
            .map(String.init)

        guard supportedWordCounts
            .contains(phraseWords.count) else {
            throw ZurvanRecoveryError.invalidWordCount
        }

        let words = try loadWordList()
        let wordIndex = Dictionary(
            uniqueKeysWithValues: words
                .enumerated()
                .map { ($0.element, $0.offset) }
        )

        let totalBits = phraseWords.count * 11
        let entropyBits = totalBits * 32 / 33
        let checksumBits = entropyBits / 32
        var entropy = [UInt8](
            repeating: 0,
            count: entropyBits / 8
        )
        var checksumValue = 0

        for (wordPosition, word) in phraseWords.enumerated() {
            guard let index = wordIndex[word] else {
                throw ZurvanRecoveryError.unknownWord(
                    wordPosition + 1
                )
            }

            for bit in 0..<11 {
                let bitValue =
                    (index >> (10 - bit)) & 1
                let globalBit =
                    wordPosition * 11 + bit

                if globalBit < entropyBits {
                    writeBit(
                        &entropy,
                        globalBit,
                        bitValue
                    )
                } else {
                    checksumValue =
                        (checksumValue << 1) |
                        bitValue
                }
            }
        }

        let hash = [UInt8](
            SHA256.hash(data: Data(entropy))
        )
        var expectedChecksum = 0

        for bit in 0..<checksumBits {
            expectedChecksum =
                (expectedChecksum << 1) |
                readBit(hash, bit)
        }

        guard checksumValue == expectedChecksum else {
            throw ZurvanRecoveryError.invalidChecksum
        }

        return Data(entropy)
    }

    static func fingerprint(
        _ phrase: String
    ) throws -> String {
        let entropy = try decodeEntropy(phrase)
        let hash = SHA256.hash(data: entropy)

        return hash.prefix(8)
            .map { String(format: "%02x", $0) }
            .joined()
    }

    static func wrapVaultKey(
        phrase: String,
        vaultId: String,
        vaultKey: Data
    ) throws -> ZurvanCloudRecoveryEnvelopeIOS {
        guard vaultKey.count == 32 else {
            throw ZurvanRecoveryError.invalidVaultKey
        }

        let normalized = normalizePhrase(phrase)
        let wordCount = normalized
            .split(separator: " ")
            .count

        guard supportedWordCounts.contains(wordCount) else {
            throw ZurvanRecoveryError.invalidWordCount
        }

        let recoveryKey = try deriveRecoveryKey(
            phrase: normalized,
            vaultId: vaultId
        )

        let nonce = AES.GCM.Nonce()
        let aad = recoveryAad(
            protocolVersion: 1,
            vaultId: vaultId,
            wordCount: wordCount
        )

        let box = try AES.GCM.seal(
            vaultKey,
            using: SymmetricKey(data: recoveryKey),
            nonce: nonce,
            authenticating: aad
        )

        return ZurvanCloudRecoveryEnvelopeIOS(
            protocolVersion: 1,
            vaultId: vaultId,
            wordCount: wordCount,
            phraseFingerprint: try fingerprint(normalized),
            nonceBase64: Data(nonce).base64EncodedString(),
            wrappedVaultKeyBase64:
                box.ciphertext.base64EncodedString(),
            tagBase64:
                box.tag.base64EncodedString()
        )
    }

    static func unwrapVaultKey(
        phrase: String,
        envelope: ZurvanCloudRecoveryEnvelopeIOS
    ) throws -> Data {
        guard envelope.protocolVersion == 1 else {
            throw ZurvanRecoveryError.unsupportedProtocol
        }

        let normalized = normalizePhrase(phrase)
        let wordCount = normalized
            .split(separator: " ")
            .count

        guard wordCount == envelope.wordCount else {
            throw ZurvanRecoveryError.invalidWordCount
        }

        let actualFingerprint =
            try fingerprint(normalized)

        guard actualFingerprint.caseInsensitiveCompare(
            envelope.phraseFingerprint
        ) == .orderedSame else {
            throw ZurvanRecoveryError.phraseMismatch
        }

        guard let nonceData = Data(
            base64Encoded: envelope.nonceBase64
        ),
        let ciphertext = Data(
            base64Encoded:
                envelope.wrappedVaultKeyBase64
        ),
        let tag = Data(
            base64Encoded: envelope.tagBase64
        ) else {
            throw ZurvanRecoveryError.invalidEnvelope
        }

        let recoveryKey = try deriveRecoveryKey(
            phrase: normalized,
            vaultId: envelope.vaultId
        )

        let nonce = try AES.GCM.Nonce(
            data: nonceData
        )

        let box = try AES.GCM.SealedBox(
            nonce: nonce,
            ciphertext: ciphertext,
            tag: tag
        )

        let key = try AES.GCM.open(
            box,
            using: SymmetricKey(data: recoveryKey),
            authenticating: recoveryAad(
                protocolVersion:
                    envelope.protocolVersion,
                vaultId: envelope.vaultId,
                wordCount: envelope.wordCount
            )
        )

        guard key.count == 32 else {
            throw ZurvanRecoveryError.invalidVaultKey
        }

        return key
    }

    private static func deriveRecoveryKey(
        phrase: String,
        vaultId: String
    ) throws -> Data {
        let entropy = try decodeEntropy(phrase)
        let code = HMAC<SHA256>.authenticationCode(
            for: Data(
                "zurvan-recovery-v1|\(vaultId)".utf8
            ),
            using: SymmetricKey(data: entropy)
        )

        return Data(code)
    }

    private static func recoveryAad(
        protocolVersion: Int,
        vaultId: String,
        wordCount: Int
    ) -> Data {
        Data(
            "zurvan-recovery-v1|\(protocolVersion)|\(vaultId)|\(wordCount)"
                .utf8
        )
    }

    private static func normalizePhrase(
        _ value: String
    ) -> String {
        value
            .decomposedStringWithCompatibilityMapping
            .lowercased()
            .split(whereSeparator: { $0.isWhitespace })
            .joined(separator: " ")
    }

    private static func loadWordList() throws -> [String] {
        guard let url = Bundle.main.url(
            forResource: "bip39-english",
            withExtension: "txt"
        ) else {
            throw ZurvanRecoveryError.wordListMissing
        }

        let content = try String(
            contentsOf: url,
            encoding: .utf8
        )

        let words = content
            .split(whereSeparator: { $0.isNewline })
            .map {
                $0.trimmingCharacters(
                    in: .whitespacesAndNewlines
                )
            }
            .filter { !$0.isEmpty }

        guard words.count == 2048 else {
            throw ZurvanRecoveryError.wordListInvalid
        }

        return words
    }

    private static func readBit(
        _ bytes: [UInt8],
        _ bitIndex: Int
    ) -> Int {
        let byteIndex = bitIndex / 8
        let shift = 7 - (bitIndex % 8)
        return Int(
            (bytes[byteIndex] >> shift) & 1
        )
    }

    private static func writeBit(
        _ bytes: inout [UInt8],
        _ bitIndex: Int,
        _ value: Int
    ) {
        guard value != 0 else { return }

        let byteIndex = bitIndex / 8
        let shift = 7 - (bitIndex % 8)

        bytes[byteIndex] |= UInt8(1 << shift)
    }
}

enum ZurvanRecoveryError: Error {
    case invalidWordCount
    case invalidEntropy
    case invalidChecksum
    case unknownWord(Int)
    case wordListMissing
    case wordListInvalid
    case invalidVaultKey
    case invalidEnvelope
    case unsupportedProtocol
    case phraseMismatch
    case randomGenerationFailed
}
