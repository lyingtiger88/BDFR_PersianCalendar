import Foundation
import Security
import CryptoKit
import UIKit
import DeviceCheck

enum ZurvanIOSLicense {
    private static let storedCodeKey = "zurvan.license.activationCode"
    private static let installationKey = "zurvan.license.installationID"

    static func deviceCode() -> String {
        let defaults = UserDefaults.standard
        let identity: String
        // Preserve already-issued, signed offline licenses that were bound
        // to the old app-generated installation identifier.
        if let legacy = defaults.string(forKey: installationKey), !legacy.isEmpty {
            identity = "zurvan-license-device-v1|ios|\(legacy)"
        } else {
            // iOS does not expose the physical iPhone serial number to apps.
            // IDFV is provided by the OS, not created by Zurvan. It can
            // rotate after the vendor's last app is uninstalled.
            guard let idfv = UIDevice.current.identifierForVendor else {
                return "" // Fail closed: never issue a universal fallback code.
            }
            identity = "zurvan-license-device-v2|ios-idfv|\(idfv.uuidString.lowercased())"
        }
        let data = Data(identity.utf8)
        return "ZRV-" + SHA256.hash(data: data).prefix(12)
            .map { String(format: "%02X", $0) }.joined()
    }

    /// Server-side free trial fingerprint. It is NOT the device serial, nor a
    /// trustworthy fraud signal by itself; Apple's DeviceCheck bit is the
    /// authoritative one-time-use record for new iOS trials.
    static func onlineTrialFingerprint() throws -> String {
        guard let idfv = UIDevice.current.identifierForVendor else {
            throw DeviceProofError.notSupported
        }
        let identity = "zurvan-free-trial-device-v1|ios-idfv|\(idfv.uuidString.lowercased())"
        return SHA256.hash(data: Data(identity.utf8))
            .map { String(format: "%02x", $0) }.joined()
    }

    /// DeviceCheck is hardware-backed device evidence managed by Apple.
    /// The server must query Apple's two per-device bits using its own private
    /// signing key, then set the "trial consumed" bit after a successful claim.
    /// Neither the iPhone serial nor an equivalent unrestricted hardware ID
    /// is available to iOS applications.
    static func deviceCheckToken() async throws -> String {
        guard DCDevice.current.isSupported else {
            throw DeviceProofError.notSupported
        }
        let token: Data = try await withCheckedThrowingContinuation { continuation in
            DCDevice.current.generateToken { data, error in
                if let error {
                    continuation.resume(throwing: error)
                } else if let data, !data.isEmpty {
                    continuation.resume(returning: data)
                } else {
                    continuation.resume(throwing: DeviceProofError.missingToken)
                }
            }
        }
        return token.base64EncodedString()
    }

    enum DeviceProofError: Error {
        case notSupported
        case missingToken
    }

    private static let authBase = "https://yztithydgdvydaktuxlw.supabase.co"
    // A publishable key is not a secret; the Edge Function validates a real JWT.
    private static let publishableKey = "sb_publishable_UAKFYbxgWOCVRDDdv_0JLg_oIIiubmT"
    private static let sessionAccount = "zurvan-online-license-session-v1"

    private struct OnlineSession: Codable {
        var accessToken: String
        var refreshToken: String
        var userId: String
    }

    private static func store(_ session: OnlineSession) throws {
        let bytes = try JSONEncoder().encode(session)
        let item: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: sessionAccount,
            kSecAttrAccount as String: sessionAccount,
            kSecValueData as String: bytes,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        ]
        SecItemDelete(item.filter { $0.key != kSecValueData as String } as CFDictionary)
        let status = SecItemAdd(item as CFDictionary, nil)
        guard status == errSecSuccess else { throw OnlineLicenseError.keychainFailure }
    }

    private static func load() -> OnlineSession? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: sessionAccount,
            kSecAttrAccount as String: sessionAccount,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var data: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &data) == errSecSuccess,
              let bytes = data as? Data else { return nil }
        return try? JSONDecoder().decode(OnlineSession.self, from: bytes)
    }

    private static func request(_ path: String, body: [String: Any],
                                bearer: String? = nil) async throws -> [String: Any] {
        guard let url = URL(string: authBase + path) else { throw OnlineLicenseError.invalidResponse }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue(publishableKey, forHTTPHeaderField: "apikey")
        if let bearer { request.setValue("Bearer " + bearer, forHTTPHeaderField: "Authorization") }
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        let (data,response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse else { throw OnlineLicenseError.invalidResponse }
        let json = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] ?? [:]
        guard (200..<300).contains(http.statusCode) else {
            throw OnlineLicenseError.server((json["error"] as? String) ?? "HTTP \(http.statusCode)")
        }
        return json
    }

    enum OnlineLicenseError: Error, LocalizedError {
        case invalidResponse
        case keychainFailure
        case server(String)

        var errorDescription: String? {
            switch self {
            case .invalidResponse: return "پاسخ سرور نامعتبر بود."
            case .keychainFailure: return "ذخیره امن نشست حساب ممکن نشد."
            case .server(let code): return "فعالسازی سرور: " + code
            }
        }
    }

    /// Online account sign-in. New free trials require Apple DeviceCheck proof;
    /// paid subscriptions can be read without issuing another free trial.
    static func signInAndActivate(email: String, password: String) async throws -> Bool {
        let auth = try await request("/auth/v1/token?grant_type=password",
            body:["email":email.trimmingCharacters(in:.whitespacesAndNewlines),
                  "password":password])
        guard let access = auth["access_token"] as? String,
              let refresh = auth["refresh_token"] as? String,
              let user = auth["user"] as? [String: Any],
              let userId = user["id"] as? String else {
            throw OnlineLicenseError.invalidResponse
        }
        let session = OnlineSession(accessToken:access,refreshToken:refresh,userId:userId)
        try store(session)
        let status = try await request("/functions/v1/zurvan-license-status",
            body:[:],bearer:access)
        if status["can_write"] as? Bool == true {
            applyOnlineFeatures(status)
            return true
        }

        let fingerprint = try onlineTrialFingerprint()
        let proof = try await deviceCheckToken()
        _ = try await request("/functions/v1/zurvan-mobile-trial",
            body:["platform":"ios","key_fingerprint":fingerprint,
                  "devicecheck_token":proof],bearer:access)
        return try await readOnlineStatus(session)
    }

    private static let pahlaviGrantKey = "zurvan.online.pahlavi.granted"
    private static let pahlaviVerifiedAtKey = "zurvan.online.pahlavi.checkedAt"
    static var hasOnlinePahlaviAccess: Bool {
        // Never apply a previous account's cached feature to a signed-out user.
        guard load() != nil else { return false }
        let defaults = UserDefaults.standard
        let age = Date().timeIntervalSince1970 -
            defaults.double(forKey: pahlaviVerifiedAtKey)
        // Admin revocations propagate on refresh; fail closed after 24 hours
        // without a server policy instead of trusting a stale local toggle.
        return defaults.bool(forKey: pahlaviGrantKey) &&
            age >= 0 && age <= 24 * 60 * 60
    }

    private static func applyOnlineFeatures(_ result: [String: Any]) {
        let features = result["features"] as? [String: Any]
        let allowed = features?["pahlavi_occasions"] as? Bool == true
        UserDefaults.standard.set(allowed, forKey: pahlaviGrantKey)
        UserDefaults.standard.set(Date().timeIntervalSince1970,
            forKey: pahlaviVerifiedAtKey)
    }

    private static func readOnlineStatus(_ session: OnlineSession) async throws -> Bool {
        let fingerprint = try onlineTrialFingerprint()
        let result = try await request("/functions/v1/zurvan-license-status",
            body:["key_fingerprint":fingerprint],bearer:session.accessToken)
        applyOnlineFeatures(result)
        return result["can_write"] as? Bool == true
    }

    static func refreshOnlineEntitlement() async -> Bool {
        guard var session = load() else { return false }
        do {
            let data = try await request("/auth/v1/token?grant_type=refresh_token",
                body:["refresh_token":session.refreshToken])
            guard let access = data["access_token"] as? String,
                  let refresh = data["refresh_token"] as? String else { return false }
            session.accessToken = access
            session.refreshToken = refresh
            try store(session)
            return try await readOnlineStatus(session)
        } catch { return false }
    }

    /// Authenticated user progress endpoint. All XP awards and badges remain
    /// authoritative on the server; a device cannot choose the points earned.
    static func userProgress(action: String = "status",
                             visible: Bool? = nil) async throws -> [String: Any] {
        guard ["status", "claim_daily", "set_visibility"].contains(action) else {
            throw OnlineLicenseError.invalidResponse
        }
        guard action != "set_visibility" || visible != nil else {
            throw OnlineLicenseError.invalidResponse
        }
        // Refresh the session even for expired/read-only subscriptions, whose
        // owners must still be able to view their earned achievements.
        _ = await refreshOnlineEntitlement()
        guard let session = load() else {
            throw OnlineLicenseError.server("برای مشاهده امتیاز، وارد حساب زروان شوید.")
        }
        var body: [String: Any] = ["action":action]
        if let visible { body["visible"] = visible }
        return try await request("/functions/v1/zurvan-user-progress",
            body:body,bearer:session.accessToken)
    }

    /// Re-check the Pahlavi feature on entering Settings. Network failure
    /// hides the premium controls instead of relying on the 24-hour cache.
    static func refreshPahlaviGrant() async -> Bool {
        guard var session = load() else { return false }
        do {
            let refreshed = try await request("/auth/v1/token?grant_type=refresh_token",
                body:["refresh_token":session.refreshToken])
            guard let access = refreshed["access_token"] as? String,
                  let refresh = refreshed["refresh_token"] as? String else { return false }
            session.accessToken = access
            session.refreshToken = refresh
            try store(session)
            _ = try await readOnlineStatus(session)
            return hasOnlinePahlaviAccess
        } catch { return false }
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
        let currentDeviceCode = deviceCode()
        guard !currentDeviceCode.isEmpty,
              let object = try? JSONSerialization.jsonObject(with: payload) as? [String: Any],
              object["Product"] as? String == "zurvan",
              object["DeviceCode"] as? String == currentDeviceCode else { return false }
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
