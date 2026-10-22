import CryptoKit
import Foundation

final class CompanionEphemeralKeyIOS {
    private let privateKey = P256.KeyAgreement.PrivateKey()

    var publicKeyBase64: String {
        privateKey.publicKey.x963Representation.base64EncodedString()
    }

    var publicKeyFingerprint: String {
        let hash = SHA256.hash(data: privateKey.publicKey.x963Representation)
        return hash.prefix(8)
            .map { String(format: "%02X", $0) }
            .joined()
    }

    func deriveSessionKey(
        peerPublicKeyBase64: String,
        sessionId: String
    ) throws -> Data {
        guard let peerData = Data(base64Encoded: peerPublicKeyBase64),
              peerData.count == 65,
              peerData.first == 0x04 else {
            throw CompanionCryptoError.invalidPublicKey
        }

        let peerPublicKey = try P256.KeyAgreement.PublicKey(
            x963Representation: peerData
        )

        let sharedSecret = try privateKey.sharedSecretFromKeyAgreement(
            with: peerPublicKey
        )

        let rawSecret = sharedSecret.withUnsafeBytes { Data($0) }
        let hmacKey = SymmetricKey(data: rawSecret)
        let context = Data(
            "zurvan-companion-v1|\(sessionId)".utf8
        )

        let authenticationCode = HMAC<SHA256>.authenticationCode(
            for: context,
            using: hmacKey
        )

        return Data(authenticationCode)
    }

    static func computeSixDigitSas(
        sessionKey: Data
    ) throws -> String {
        guard sessionKey.count == 32 else {
            throw CompanionCryptoError.invalidSessionKey
        }

        let hash = Data(SHA256.hash(data: sessionKey))
        let value =
            (UInt32(hash[0]) << 24) |
            (UInt32(hash[1]) << 16) |
            (UInt32(hash[2]) << 8) |
            UInt32(hash[3])

        return String(format: "%06u", value % 1_000_000)
    }

    static func encrypt(
        plaintext: Data,
        sessionKey: Data
    ) throws -> EncryptedCompanionFrame {
        guard sessionKey.count == 32 else {
            throw CompanionCryptoError.invalidSessionKey
        }

        let key = SymmetricKey(data: sessionKey)
        let nonce = AES.GCM.Nonce()
        let associatedData = Data("zurvan-companion-v1".utf8)

        let box = try AES.GCM.seal(
            plaintext,
            using: key,
            nonce: nonce,
            authenticating: associatedData
        )

        return EncryptedCompanionFrame(
            nonceBase64: Data(nonce).base64EncodedString(),
            ciphertextBase64: box.ciphertext.base64EncodedString(),
            tagBase64: box.tag.base64EncodedString()
        )
    }

    static func decrypt(
        frame: EncryptedCompanionFrame,
        sessionKey: Data
    ) throws -> Data {
        guard sessionKey.count == 32 else {
            throw CompanionCryptoError.invalidSessionKey
        }

        guard let nonceData = Data(base64Encoded: frame.nonceBase64),
              let ciphertext = Data(base64Encoded: frame.ciphertextBase64),
              let tag = Data(base64Encoded: frame.tagBase64) else {
            throw CompanionCryptoError.invalidEncryptedFrame
        }

        let nonce = try AES.GCM.Nonce(data: nonceData)
        let box = try AES.GCM.SealedBox(
            nonce: nonce,
            ciphertext: ciphertext,
            tag: tag
        )

        return try AES.GCM.open(
            box,
            using: SymmetricKey(data: sessionKey),
            authenticating: Data("zurvan-companion-v1".utf8)
        )
    }
}

enum CompanionCryptoError: Error {
    case invalidPublicKey
    case invalidSessionKey
    case invalidEncryptedFrame
}
