import CryptoKit
import Foundation

struct ArvanS3ProfileIOS: Sendable {
    var endpoint: String = "https://s3.ir-thr-at1.arvanstorage.ir"
    var region: String = "ir-thr-at1"
    var bucket: String
    var accessKeyId: String
    var secretAccessKey: String
}

final class ArvanS3ClientIOS {
    private let profile: ArvanS3ProfileIOS
    private let session: URLSession

    init(
        profile: ArvanS3ProfileIOS,
        session: URLSession = .shared
    ) {
        self.profile = profile
        self.session = session
    }

    func put(
        key: String,
        payload: Data,
        contentType: String = "application/octet-stream"
    ) async throws {
        var request = try signedRequest(
            method: "PUT",
            key: key,
            payload: payload
        )
        request.httpBody = payload
        request.setValue(contentType, forHTTPHeaderField: "Content-Type")

        let (_, response) = try await session.data(for: request)
        try validate(response)
    }

    func get(key: String) async throws -> Data? {
        let request = try signedRequest(
            method: "GET",
            key: key,
            payload: Data()
        )

        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw ArvanS3Error.invalidResponse
        }

        if http.statusCode == 404 {
            return nil
        }

        try validate(http)
        return data
    }

    func delete(key: String) async throws {
        let request = try signedRequest(
            method: "DELETE",
            key: key,
            payload: Data()
        )

        let (_, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw ArvanS3Error.invalidResponse
        }

        if http.statusCode != 404 {
            try validate(http)
        }
    }

    func exists(key: String) async throws -> Bool {
        let request = try signedRequest(
            method: "HEAD",
            key: key,
            payload: Data()
        )

        let (_, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw ArvanS3Error.invalidResponse
        }

        if http.statusCode == 404 {
            return false
        }

        try validate(http)
        return true
    }

    private func signedRequest(
        method: String,
        key: String,
        payload: Data
    ) throws -> URLRequest {
        guard profile.endpoint.lowercased().hasPrefix("https://") else {
            throw ArvanS3Error.insecureEndpoint
        }

        let now = Date()
        let amzDate = Self.format(now, pattern: "yyyyMMdd'T'HHmmss'Z'")
        let shortDate = Self.format(now, pattern: "yyyyMMdd")
        let payloadHash = Self.sha256Hex(payload)
        let canonicalURI = Self.canonicalPath(
            bucket: profile.bucket,
            key: key
        )

        guard let url = URL(
            string: profile.endpoint.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
                + canonicalURI
        ) else {
            throw ArvanS3Error.invalidEndpoint
        }

        let canonicalHeaders =
            "host:\(url.host ?? "")\n" +
            "x-amz-content-sha256:\(payloadHash)\n" +
            "x-amz-date:\(amzDate)\n"

        let signedHeaders = "host;x-amz-content-sha256;x-amz-date"

        let canonicalRequest =
            "\(method)\n" +
            "\(canonicalURI)\n\n" +
            "\(canonicalHeaders)\n" +
            "\(signedHeaders)\n" +
            payloadHash

        let scope =
            "\(shortDate)/\(profile.region)/s3/aws4_request"

        let stringToSign =
            "AWS4-HMAC-SHA256\n" +
            "\(amzDate)\n" +
            "\(scope)\n" +
            Self.sha256Hex(Data(canonicalRequest.utf8))

        let signingKey = Self.signingKey(
            secret: profile.secretAccessKey,
            shortDate: shortDate,
            region: profile.region
        )

        let signature = Self.hex(
            Self.hmac(
                key: signingKey,
                data: Data(stringToSign.utf8)
            )
        )

        let authorization =
            "AWS4-HMAC-SHA256 " +
            "Credential=\(profile.accessKeyId)/\(scope), " +
            "SignedHeaders=\(signedHeaders), " +
            "Signature=\(signature)"

        var request = URLRequest(url: url)
        request.httpMethod = method
        request.timeoutInterval = 30
        request.setValue(amzDate, forHTTPHeaderField: "x-amz-date")
        request.setValue(
            payloadHash,
            forHTTPHeaderField: "x-amz-content-sha256"
        )
        request.setValue(
            authorization,
            forHTTPHeaderField: "Authorization"
        )
        return request
    }

    private func validate(_ response: URLResponse) throws {
        guard let http = response as? HTTPURLResponse else {
            throw ArvanS3Error.invalidResponse
        }

        try validate(http)
    }

    private func validate(_ response: HTTPURLResponse) throws {
        guard (200...299).contains(response.statusCode) else {
            throw ArvanS3Error.httpStatus(response.statusCode)
        }
    }

    private static func canonicalPath(
        bucket: String,
        key: String
    ) -> String {
        let encodedBucket = encodePathSegment(bucket)
        let encodedKey = key
            .split(separator: "/")
            .map { encodePathSegment(String($0)) }
            .joined(separator: "/")

        return encodedKey.isEmpty
            ? "/\(encodedBucket)"
            : "/\(encodedBucket)/\(encodedKey)"
    }

    private static func encodePathSegment(_ value: String) -> String {
        var allowed = CharacterSet.alphanumerics
        allowed.insert(charactersIn: "-._~")
        return value.addingPercentEncoding(
            withAllowedCharacters: allowed
        ) ?? value
    }

    private static func signingKey(
        secret: String,
        shortDate: String,
        region: String
    ) -> Data {
        let kDate = hmac(
            key: Data(("AWS4" + secret).utf8),
            data: Data(shortDate.utf8)
        )
        let kRegion = hmac(
            key: kDate,
            data: Data(region.utf8)
        )
        let kService = hmac(
            key: kRegion,
            data: Data("s3".utf8)
        )
        return hmac(
            key: kService,
            data: Data("aws4_request".utf8)
        )
    }

    private static func hmac(
        key: Data,
        data: Data
    ) -> Data {
        let code = HMAC<SHA256>.authenticationCode(
            for: data,
            using: SymmetricKey(data: key)
        )
        return Data(code)
    }

    private static func sha256Hex(_ data: Data) -> String {
        hex(Data(SHA256.hash(data: data)))
    }

    private static func hex(_ data: Data) -> String {
        data.map { String(format: "%02x", $0) }.joined()
    }

    private static func format(
        _ date: Date,
        pattern: String
    ) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        formatter.dateFormat = pattern
        return formatter.string(from: date)
    }
}

enum ArvanS3Error: Error {
    case insecureEndpoint
    case invalidEndpoint
    case invalidResponse
    case httpStatus(Int)
}
