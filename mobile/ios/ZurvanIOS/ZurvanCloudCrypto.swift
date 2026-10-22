import CryptoKit
import Foundation

struct ZurvanCloudEncryptedPayload: Codable, Sendable {
    let protocolVersion: Int
    let vaultId: String
    let objectId: String
    let nonceBase64: String
    let ciphertextBase64: String
    let tagBase64: String
}

enum ZurvanCloudCryptoIOS {
    static func createVaultKey() -> Data {
        var bytes = [UInt8](repeating: 0, count: 32)
        let result = SecRandomCopyBytes(
            kSecRandomDefault,
            bytes.count,
            &bytes
        )
        precondition(result == errSecSuccess)
        return Data(bytes)
    }

    static func encodeRecoveryKey(_ vaultKey: Data) throws -> String {
        guard vaultKey.count == 32 else {
            throw ZurvanCloudCryptoError.invalidVaultKey
        }

        return vaultKey.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    static func decodeRecoveryKey(_ value: String) throws -> Data {
        var normalized = value
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")

        while normalized.count % 4 != 0 {
            normalized.append("=")
        }

        guard let data = Data(base64Encoded: normalized),
              data.count == 32 else {
            throw ZurvanCloudCryptoError.invalidVaultKey
        }

        return data
    }

    static func encrypt(
        plaintext: Data,
        vaultKey: Data,
        protocolVersion: Int = 1,
        vaultId: String,
        objectId: String
    ) throws -> ZurvanCloudEncryptedPayload {
        let objectKey = try deriveObjectKey(
            vaultKey: vaultKey,
            vaultId: vaultId,
            objectId: objectId
        )

        let nonce = AES.GCM.Nonce()
        let aad = associatedData(
            protocolVersion: protocolVersion,
            vaultId: vaultId,
            objectId: objectId
        )

        let box = try AES.GCM.seal(
            plaintext,
            using: objectKey,
            nonce: nonce,
            authenticating: aad
        )

        return ZurvanCloudEncryptedPayload(
            protocolVersion: protocolVersion,
            vaultId: vaultId,
            objectId: objectId,
            nonceBase64: Data(nonce).base64EncodedString(),
            ciphertextBase64: box.ciphertext.base64EncodedString(),
            tagBase64: box.tag.base64EncodedString()
        )
    }

    static func decrypt(
        _ payload: ZurvanCloudEncryptedPayload,
        vaultKey: Data
    ) throws -> Data {
        let objectKey = try deriveObjectKey(
            vaultKey: vaultKey,
            vaultId: payload.vaultId,
            objectId: payload.objectId
        )

        guard let nonceData = Data(base64Encoded: payload.nonceBase64),
              let ciphertext = Data(base64Encoded: payload.ciphertextBase64),
              let tag = Data(base64Encoded: payload.tagBase64) else {
            throw ZurvanCloudCryptoError.invalidPayload
        }

        let nonce = try AES.GCM.Nonce(data: nonceData)
        let box = try AES.GCM.SealedBox(
            nonce: nonce,
            ciphertext: ciphertext,
            tag: tag
        )

        return try AES.GCM.open(
            box,
            using: objectKey,
            authenticating: associatedData(
                protocolVersion: payload.protocolVersion,
                vaultId: payload.vaultId,
                objectId: payload.objectId
            )
        )
    }

    private static func deriveObjectKey(
        vaultKey: Data,
        vaultId: String,
        objectId: String
    ) throws -> SymmetricKey {
        guard vaultKey.count == 32 else {
            throw ZurvanCloudCryptoError.invalidVaultKey
        }

        let context = Data(
            "zurvan-cloud-v1|\(vaultId)|\(objectId)".utf8
        )

        let code = HMAC<SHA256>.authenticationCode(
            for: context,
            using: SymmetricKey(data: vaultKey)
        )

        return SymmetricKey(data: Data(code))
    }

    private static func associatedData(
        protocolVersion: Int,
        vaultId: String,
        objectId: String
    ) -> Data {
        Data(
            "zurvan-cloud-v1|\(protocolVersion)|\(vaultId)|\(objectId)".utf8
        )
    }
}

enum ZurvanCloudCryptoError: Error {
    case invalidVaultKey
    case invalidPayload
}
