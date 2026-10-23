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
              let pkcs1 = rsaPublicKeyBytes(from: keyData),
              let publicKey = SecKeyCreateWithData(pkcs1 as CFData, [
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

    // Extract RSAPublicKey from X.509 SubjectPublicKeyInfo (PKCS#8 PEM public-key container).
    private static func rsaPublicKeyBytes(from data: Data) -> Data? {
        let bytes = [UInt8](data)
        func field(_ index: Int) -> (start: Int, end: Int)? {
            guard index + 2 <= bytes.count else { return nil }
            var length = Int(bytes[index + 1])
            var header = 2
            if length & 0x80 != 0 {
                let count = length & 0x7f
                guard count > 0, count <= 4, index + header + count <= bytes.count else { return nil }
                length = 0
                for p in 0..<count { length = (length << 8) | Int(bytes[index + header + p]) }
                header += count
            }
            let start = index + header
            guard length <= bytes.count - start else { return nil }
            return (start, start + length)
        }
        guard bytes.first == 0x30, let top = field(0), top.end == bytes.count else { return nil }
        let algorithm = top.start
        guard bytes[algorithm] == 0x30, let alg = field(algorithm) else { return nil }
        let bitString = alg.end
        guard bitString < top.end, bytes[bitString] == 0x03,
              let bits = field(bitString), bits.start < bits.end,
              bytes[bits.start] == 0,
              bits.start + 1 < bits.end, bytes[bits.start + 1] == 0x30 else { return nil }
        return Data(bytes[(bits.start + 1)..<bits.end])
    }

    private static func decode(_ text: String) -> Data? {
        var encoded = text.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        encoded += String(repeating: "=", count: (4 - encoded.count % 4) % 4)
        return Data(base64Encoded: encoded)
    }
}
