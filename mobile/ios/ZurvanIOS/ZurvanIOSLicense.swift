import Foundation
import Security
import CryptoKit
import UIKit

enum ZurvanIOSLicense {
    private static let storedCodeKey = "zurvan.license.activationCode"
    private static let installationKey = "zurvan.license.installationID"

    static func deviceCode() -> String {
        let defaults = UserDefaults.standard
        let identifier: String
        if let existing = defaults.string(forKey: installationKey) {
            identifier = existing
        } else {
            let newIdentifier = UUID().uuidString.lowercased()
            defaults.set(newIdentifier, forKey: installationKey)
            identifier = newIdentifier
        }
        let data = Data("zurvan-license-device-v1|ios|\(identifier)".utf8)
        return "ZRV-" + SHA256.hash(data: data).prefix(12).map { String(format: "%02X", $0) }.joined()
    }

    static var isLicensed: Bool {
        guard let stored = UserDefaults.standard.string(forKey: storedCodeKey) else { return false }
        return verify(stored)
    }

    static func activate(_ code: String) -> Bool {
        guard verify(code) else { return false }
        UserDefaults.standard.set(code, forKey: storedCodeKey)
        return true
    }

    static func verify(_ code: String) -> Bool {
        let parts = code.split(separator: ".", omittingEmptySubsequences: false).map(String.init)
        guard parts.count == 3, parts[0] == "ZRV1",
              code.utf8.count < 8192,
              let payload = decode(parts[1]),
              let signature = decode(parts[2]),
              let pem = Bundle.main.object(forInfoDictionaryKey: "ZurvanLicensePublicKeyPEM") as? String,
              pem.contains("BEGIN PUBLIC KEY") else { return false }

        let b64 = pem.components(separatedBy: .newlines).filter { !$0.hasPrefix("-----") }.joined()
        guard let keyData = Data(base64Encoded: b64),
              let publicKey = SecKeyCreateWithData(keyData as CFData, [
                kSecAttrKeyType: kSecAttrKeyTypeRSA,
                kSecAttrKeyClass: kSecAttrKeyClassPublic
              ] as CFDictionary, nil) else { return false }

        guard SecKeyVerifySignature(publicKey, .rsaSignatureMessagePSSSHA256,
                                    payload as CFData, signature as CFData, nil) else { return false }
        guard let object = try? JSONSerialization.jsonObject(with: payload) as? [String: Any],
              object["Product"] as? String == "zurvan",
              object["DeviceCode"] as? String == deviceCode() else { return false }
        if let expiry = object["ExpiresUtc"] as? String, !expiry.isEmpty {
            let formatter = ISO8601DateFormatter()
            formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            guard let date = formatter.date(from: expiry) ??
                ISO8601DateFormatter().date(from: expiry), date > Date() else { return false }
        }
        return true
    }

    private static func decode(_ text: String) -> Data? {
        var encoded = text.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        encoded += String(repeating: "=", count: (4 - encoded.count % 4) % 4)
        return Data(base64Encoded: encoded)
    }
}
