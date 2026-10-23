import Foundation
import Network
import SwiftUI

@MainActor
final class CompanionPairingViewModel: ObservableObject {
    @Published var status =
        "آماده برای اسکن QR دستگاه Master"
    @Published var isBusy = false
    @Published var isScannerPresented = false
    @Published var peerName: String?
    @Published var sas: String?
    @Published var receivedItemCount = 0
    @Published var errorMessage: String?

    private let deviceId: String = {
        let key = "zurvan_companion_device_id"
        if let existing =
            UserDefaults.standard.string(
                forKey: key
            ),
           !existing.isEmpty {
            return existing
        }

        let created =
            "dev_" +
            UUID()
                .uuidString
                .replacingOccurrences(
                    of: "-",
                    with: ""
                )

        UserDefaults.standard.set(
            created,
            forKey: key
        )
        return created
    }()

    func scan() {
        errorMessage = nil
        isScannerPresented = true
    }

    func handleScannedPayload(
        _ payload: String
    ) {
        isScannerPresented = false
        isBusy = true
        status =
            "QR خوانده شد؛ در حال اتصال امن…"
        errorMessage = nil

        Task {
            do {
                let result =
                    try await Self.joinMaster(
                        qrPayload: payload,
                        deviceId: deviceId
                    )

                peerName =
                    result.peerName
                sas = result.sas
                receivedItemCount =
                    result.receivedItems
                status =
                    "اتصال امن برقرار شد و Snapshot همگام‌سازی دریافت شد."
                isBusy = false
            } catch {
                status =
                    "جفت‌سازی انجام نشد."
                errorMessage =
                    error.localizedDescription
                isBusy = false
            }
        }
    }

    func cancelScanner() {
        isScannerPresented = false
        status =
            "اسکن QR لغو شد."
    }

    private static func joinMaster(
        qrPayload: String,
        deviceId: String
    ) async throws -> (
        peerName: String,
        sas: String,
        receivedItems: Int
    ) {
        let offer =
            try CompanionWireIOS
                .decodeOfferQR(qrPayload)

        guard offer.protocolVersion == 1 else {
            throw CompanionPairingError
                .unsupportedProtocol
        }

        guard offer.expiresAtUtc >
                Date() else {
            throw CompanionPairingError
                .expiredQR
        }

        guard let hostKey =
                offer.hostPublicKey,
              let endpointURL =
                URL(string: offer.endpoint),
              endpointURL.scheme?.lowercased() ==
                "tcp",
              let host =
                endpointURL.host,
              let portValue =
                endpointURL.port,
              let port =
                NWEndpoint.Port(
                    rawValue:
                        UInt16(portValue)
                ) else {
            throw CompanionPairingError
                .invalidEndpoint
        }

        let ephemeral =
            CompanionEphemeralKeyIOS()

        let sessionKey =
            try ephemeral
                .deriveSessionKey(
                    peerPublicKeyBase64:
                        hostKey,
                    sessionId:
                        offer.sessionId
                )

        let localSas =
            try CompanionEphemeralKeyIOS
                .computeSixDigitSas(
                    sessionKey:
                        sessionKey
                )

        let connection =
            CompanionTCPConnection(
                host:
                    NWEndpoint.Host(host),
                port: port
            )

        try await connection.connect()

        defer {
            connection.cancel()
        }

        let join =
            CompanionPairingJoin(
                protocolVersion: 1,
                sessionId:
                    offer.sessionId,
                deviceId: deviceId,
                deviceDisplayName:
                    "Zurvan iPhone/iPad",
                platform: .ios,
                pairingCode:
                    offer.pairingCode,
                devicePublicKey:
                    ephemeral
                        .publicKeyBase64
            )

        try await connection.sendLine(
            CompanionWireIOS
                .encode(join)
        )

        let pairingResult:
            CompanionPairingResultWire =
            try await connection
                .receiveEncrypted(
                    sessionKey:
                        sessionKey
                )

        guard pairingResult.accepted else {
            throw CompanionPairingError
                .rejected(
                    pairingResult.error ??
                        "Master اتصال را رد کرد."
                )
        }

        guard pairingResult.sas ==
                localSas else {
            throw CompanionPairingError
                .sasMismatch
        }

        let batch: SyncBatch =
            try await connection
                .receiveEncrypted(
                    sessionKey:
                        sessionKey
                )

        try CompanionSnapshotFileStore
            .save(batch)

        let emptyReply =
            SyncBatch(
                protocolVersion: 1,
                batchId:
                    "sync_" +
                    UUID()
                        .uuidString
                        .replacingOccurrences(
                            of: "-",
                            with: ""
                        ),
                sourceDeviceId:
                    deviceId,
                targetDeviceId:
                    offer.hostDeviceId,
                sinceCheckpoint: nil,
                items: [],
                createdAtUtc: Date()
            )

        try await connection
            .sendEncrypted(
                emptyReply,
                sessionKey:
                    sessionKey
            )

        let _: SyncAckWire =
            try await connection
                .receiveEncrypted(
                    sessionKey:
                        sessionKey
                )

        CompanionTrustedHostStore
            .remember(
                offer: offer,
                fingerprint:
                    Self.fingerprint(
                        publicKeyBase64:
                            hostKey
                    )
            )

        return (
            offer.hostDisplayName,
            localSas,
            batch.items.count
        )
    }

    private static func fingerprint(
        publicKeyBase64: String
    ) -> String {
        guard let data =
                Data(
                    base64Encoded:
                        publicKeyBase64
                ) else {
            return ""
        }

        return SHA256
            .hash(data: data)
            .prefix(8)
            .map {
                String(
                    format: "%02X",
                    $0
                )
            }
            .joined()
    }
}

private final class CompanionTCPConnection {
    private let connection: NWConnection
    private var buffer = Data()
    private let maxLineSize =
        4 * 1024 * 1024

    init(
        host: NWEndpoint.Host,
        port: NWEndpoint.Port
    ) {
        connection =
            NWConnection(
                host: host,
                port: port,
                using: .tcp
            )
    }

    func connect() async throws {
        try await withCheckedThrowingContinuation {
            continuation in

            let lock = NSLock()
            var resumed = false

            func finish(
                _ result:
                    Result<Void, Error>
            ) {
                lock.lock()
                defer { lock.unlock() }

                guard !resumed else {
                    return
                }

                resumed = true
                continuation.resume(
                    with: result
                )
            }

            connection.stateUpdateHandler = {
                state in
                switch state {
                case .ready:
                    finish(.success(()))
                case .failed(let error):
                    finish(.failure(error))
                case .cancelled:
                    finish(
                        .failure(
                            CompanionPairingError
                                .connectionClosed
                        )
                    )
                default:
                    break
                }
            }

            connection.start(
                queue:
                    DispatchQueue(
                        label:
                            "zurvan-companion-ios"
                    )
            )
        }
    }

    func cancel() {
        connection.cancel()
    }

    func sendLine(
        _ line: String
    ) async throws {
        let payload =
            Data(
                (line + "\n").utf8
            )

        guard payload.count <=
                maxLineSize else {
            throw CompanionPairingError
                .frameTooLarge
        }

        try await withCheckedThrowingContinuation {
            continuation in
            connection.send(
                content: payload,
                completion:
                    .contentProcessed {
                        error in
                        if let error {
                            continuation
                                .resume(
                                    throwing:
                                        error
                                )
                        } else {
                            continuation
                                .resume()
                        }
                    }
            )
        }
    }

    func receiveLine() async throws
        -> String {
        while true {
            if let newline =
                buffer.firstIndex(
                    of: 0x0A
                ) {
                let lineData =
                    buffer.prefix(
                        upTo: newline
                    )
                buffer.removeSubrange(
                    ...newline
                )

                guard let line =
                        String(
                            data:
                                lineData,
                            encoding:
                                .utf8
                        ) else {
                    throw CompanionPairingError
                        .invalidWireData
                }

                return line
            }

            if buffer.count >
                maxLineSize {
                throw CompanionPairingError
                    .frameTooLarge
            }

            let chunk =
                try await receiveChunk()

            guard !chunk.isEmpty else {
                throw CompanionPairingError
                    .connectionClosed
            }

            buffer.append(chunk)
        }
    }

    func sendEncrypted<T: Encodable>(
        _ value: T,
        sessionKey: Data
    ) async throws {
        let plaintext =
            try CompanionWireIOS
                .encoder
                .encode(value)

        let frame =
            try CompanionEphemeralKeyIOS
                .encrypt(
                    plaintext:
                        plaintext,
                    sessionKey:
                        sessionKey
                )

        try await sendLine(
            CompanionWireIOS
                .encode(frame)
        )
    }

    func receiveEncrypted<T: Decodable>(
        sessionKey: Data
    ) async throws -> T {
        let frame:
            EncryptedCompanionFrame =
            try CompanionWireIOS
                .decode(
                    await receiveLine()
                )

        let plaintext =
            try CompanionEphemeralKeyIOS
                .decrypt(
                    frame: frame,
                    sessionKey:
                        sessionKey
                )

        return try CompanionWireIOS
            .decoder
            .decode(
                T.self,
                from: plaintext
            )
    }

    private func receiveChunk()
        async throws -> Data {
        try await withCheckedThrowingContinuation {
            continuation in
            connection.receive(
                minimumIncompleteLength: 1,
                maximumLength: 64 * 1024
            ) {
                data,
                _,
                isComplete,
                error in

                if let error {
                    continuation
                        .resume(
                            throwing:
                                error
                        )
                    return
                }

                if let data,
                   !data.isEmpty {
                    continuation
                        .resume(
                            returning:
                                data
                        )
                    return
                }

                if isComplete {
                    continuation
                        .resume(
                            returning:
                                Data()
                        )
                    return
                }

                continuation
                    .resume(
                        returning:
                            Data()
                    )
            }
        }
    }
}

enum CompanionWireIOS {
    static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy =
            .iso8601
        return encoder
    }()

    static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy =
            .iso8601
        return decoder
    }()

    static func encode<T: Encodable>(
        _ value: T
    ) throws -> String {
        String(
            data:
                try encoder.encode(
                    value
                ),
            encoding: .utf8
        ) ?? ""
    }

    static func decode<T: Decodable>(
        _ line: String
    ) throws -> T {
        try decoder.decode(
            T.self,
            from: Data(line.utf8)
        )
    }

    static func decodeOfferQR(
        _ raw: String
    ) throws -> CompanionPairingOffer {
        let prefix =
            "zurvan://pair/v1/"

        guard raw.lowercased()
                .hasPrefix(prefix) else {
            throw CompanionPairingError
                .invalidQR
        }

        var encoded =
            String(
                raw.dropFirst(
                    prefix.count
                )
            )
                .replacingOccurrences(
                    of: "-",
                    with: "+"
                )
                .replacingOccurrences(
                    of: "_",
                    with: "/"
                )

        while encoded.count % 4 != 0 {
            encoded.append("=")
        }

        guard let data =
                Data(
                    base64Encoded:
                        encoded
                ) else {
            throw CompanionPairingError
                .invalidQR
        }

        return try decoder.decode(
            CompanionPairingOffer.self,
            from: data
        )
    }
}

private enum CompanionSnapshotFileStore {
    static func save(
        _ batch: SyncBatch
    ) throws {
        let manager =
            FileManager.default

        let folder =
            try manager.url(
                for: .applicationSupportDirectory,
                in: .userDomainMask,
                appropriateFor: nil,
                create: true
            )
            .appendingPathComponent(
                "Zurvan",
                isDirectory: true
            )

        try manager.createDirectory(
            at: folder,
            withIntermediateDirectories:
                true
        )

        let url =
            folder
                .appendingPathComponent(
                    "companion-latest-snapshot.json"
                )

        let data =
            try CompanionWireIOS
                .encoder
                .encode(batch)

        try data.write(
            to: url,
            options: .atomic
        )
    }
}

private enum CompanionTrustedHostStore {
    static func remember(
        offer: CompanionPairingOffer,
        fingerprint: String
    ) {
        let key =
            "zurvan_companion_trusted_hosts"

        var values =
            UserDefaults.standard
                .array(
                    forKey: key
                ) as? [[String: String]]
            ?? []

        values.removeAll {
            $0["deviceId"] ==
                offer.hostDeviceId
        }

        values.append([
            "deviceId":
                offer.hostDeviceId,
            "displayName":
                offer.hostDisplayName,
            "platform":
                offer.hostPlatform
                    .rawValue,
            "fingerprint":
                fingerprint,
            "lastSeenUtc":
                ISO8601DateFormatter()
                    .string(
                        from: Date()
                    )
        ])

        UserDefaults.standard.set(
            values,
            forKey: key
        )
    }
}

enum CompanionPairingError:
    LocalizedError {
    case invalidQR
    case unsupportedProtocol
    case expiredQR
    case invalidEndpoint
    case sasMismatch
    case rejected(String)
    case connectionClosed
    case frameTooLarge
    case invalidWireData

    var errorDescription: String? {
        switch self {
        case .invalidQR:
            return "این QR متعلق به Zurvan Companion نیست."
        case .unsupportedProtocol:
            return "نسخه پروتکل Pairing پشتیبانی نمی‌شود."
        case .expiredQR:
            return "QR جفت‌سازی منقضی شده است."
        case .invalidEndpoint:
            return "آدرس Master داخل QR معتبر نیست."
        case .sasMismatch:
            return "کد امنیتی دو دستگاه یکسان نیست؛ اتصال متوقف شد."
        case .rejected(let message):
            return message
        case .connectionClosed:
            return "اتصال Companion بسته شد."
        case .frameTooLarge:
            return "حجم Frame همگام‌سازی بیش از حد مجاز است."
        case .invalidWireData:
            return "داده دریافتی Companion معتبر نیست."
        }
    }
}
