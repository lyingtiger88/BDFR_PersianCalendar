import Foundation

struct DeskSessionIOS {
    let token: String
    let vaultId: String
    let identifier: String
    let displayName: String?
}

struct DeskFileIOS: Identifiable, Decodable {
    let id: String
    let encryptedName: String
    let size: Int64
    let modifiedAtUtc: String
    let contentType: String
}

enum ZurvanDeskAPIError: LocalizedError {
    case notConfigured
    case invalidResponse
    case serviceError(Int)

    var errorDescription: String? {
        switch self {
        case .notConfigured: return "سرویس حساب زروان دسک پیکربندی نشده است."
        case .invalidResponse: return "پاسخ سرویس حساب معتبر نیست."
        case .serviceError(let status): return "درخواست با خطای \(status) مواجه شد."
        }
    }
}

/// Identity API only. Vault keys and recovery words are never sent to this service.
struct ZurvanDeskAccountAPI {
    private let baseURL: URL?

    init() {
        let raw = (Bundle.main.object(forInfoDictionaryKey: "ZurvanDeskAPIBaseURL") as? String) ?? ""
        if let url = URL(string: raw), url.scheme == "https", url.host != nil {
            baseURL = url
        } else {
            baseURL = nil
        }
    }

    var isConfigured: Bool { baseURL != nil }

    func register(identifier: String, password: String, displayName: String) async throws -> DeskSessionIOS {
        let body: [String: Any] = ["identifier": identifier, "password": password,
                                   "displayName": displayName]
        return try parseSession(await request("POST", "api/v1/auth/register", json: body))
    }

    func login(identifier: String, password: String) async throws -> DeskSessionIOS {
        try parseSession(await request("POST", "api/v1/auth/login",
                                       json: ["identifier": identifier, "password": password]))
    }

    func logout(token: String) async throws {
        _ = try await request("POST", "api/v1/auth/logout", token: token)
    }

    func listFiles(token: String) async throws -> [DeskFileIOS] {
        let data = try await request("GET", "api/v1/files", token: token)
        return try JSONDecoder().decode([DeskFileIOS].self, from: data)
    }

    func deleteFile(token: String, fileID: String) async throws {
        guard fileID.range(of: "^[A-Za-z0-9_-]+$", options: .regularExpression) != nil else {
            throw ZurvanDeskAPIError.invalidResponse
        }
        _ = try await request("DELETE", "api/v1/files/\(fileID)", token: token)
    }

    private func parseSession(_ data: Data) throws -> DeskSessionIOS {
        let object = try JSONSerialization.jsonObject(with: data)
        guard let json = object as? [String: Any],
              let token = json["token"] as? String,
              let vaultId = json["vaultId"] as? String,
              let identifier = json["identifier"] as? String else {
            throw ZurvanDeskAPIError.invalidResponse
        }
        return DeskSessionIOS(token: token, vaultId: vaultId, identifier: identifier,
                              displayName: json["displayName"] as? String)
    }

    private func request(_ method: String, _ path: String, token: String? = nil,
                         json: [String: Any]? = nil) async throws -> Data {
        guard let baseURL else { throw ZurvanDeskAPIError.notConfigured }
        let url = baseURL.appendingPathComponent(path)
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let token { request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization") }
        if let json {
            request.httpBody = try JSONSerialization.data(withJSONObject: json)
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let response = response as? HTTPURLResponse else {
            throw ZurvanDeskAPIError.invalidResponse
        }
        guard (200..<300).contains(response.statusCode) else {
            throw ZurvanDeskAPIError.serviceError(response.statusCode)
        }
        return data
    }
}
