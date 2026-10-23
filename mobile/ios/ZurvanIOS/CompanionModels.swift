import Foundation

enum CompanionPlatform: String, Codable, Sendable {
    case windows = "Windows"
    case android = "Android"
    case ios = "IOS"
}

enum SyncMutationKind: String, Codable, Sendable {
    case upsert = "Upsert"
    case delete = "Delete"
}

enum SyncEntityKind: String, Codable, Sendable {
    case event = "Event"
    case task = "Task"
    case note = "Note"
    case specialOccasion = "SpecialOccasion"
    case reminder = "Reminder"
    case setting = "Setting"
    case occasionPictureMapping = "OccasionPictureMapping"
}

struct CompanionPairingOffer: Codable, Sendable {
    let protocolVersion: Int
    let sessionId: String
    let hostDeviceId: String
    let hostDisplayName: String
    let hostPlatform: CompanionPlatform
    let endpoint: String
    let pairingCode: String
    let expiresAtUtc: Date
    let hostPublicKey: String?

    enum CodingKeys: String, CodingKey {
        case protocolVersion = "ProtocolVersion"
        case sessionId = "SessionId"
        case hostDeviceId = "HostDeviceId"
        case hostDisplayName = "HostDisplayName"
        case hostPlatform = "HostPlatform"
        case endpoint = "Endpoint"
        case pairingCode = "PairingCode"
        case expiresAtUtc = "ExpiresAtUtc"
        case hostPublicKey = "HostPublicKey"
    }
}

struct CompanionPairingJoin: Codable, Sendable {
    let protocolVersion: Int
    let sessionId: String
    let deviceId: String
    let deviceDisplayName: String
    let platform: CompanionPlatform
    let pairingCode: String
    let devicePublicKey: String

    enum CodingKeys: String, CodingKey {
        case protocolVersion = "ProtocolVersion"
        case sessionId = "SessionId"
        case deviceId = "DeviceId"
        case deviceDisplayName = "DeviceDisplayName"
        case platform = "Platform"
        case pairingCode = "PairingCode"
        case devicePublicKey = "DevicePublicKey"
    }
}

struct CompanionPairingResultWire: Codable, Sendable {
    let accepted: Bool
    let deviceId: String
    let trustedDeviceId: String?
    let completedAtUtc: Date
    let sas: String
    let error: String?

    enum CodingKeys: String, CodingKey {
        case accepted = "Accepted"
        case deviceId = "DeviceId"
        case trustedDeviceId = "TrustedDeviceId"
        case completedAtUtc = "CompletedAtUtc"
        case sas = "Sas"
        case error = "Error"
    }
}

struct SyncEnvelope: Codable, Sendable, Identifiable {
    var id: String { envelopeId }

    let envelopeId: String
    let entityKind: SyncEntityKind
    let entityId: String
    let mutation: SyncMutationKind
    let revision: Int64
    let modifiedAtUtc: Date
    let originDeviceId: String
    let payloadJson: String?
    let contentHash: String?

    enum CodingKeys: String, CodingKey {
        case envelopeId = "EnvelopeId"
        case entityKind = "EntityKind"
        case entityId = "EntityId"
        case mutation = "Mutation"
        case revision = "Revision"
        case modifiedAtUtc = "ModifiedAtUtc"
        case originDeviceId = "OriginDeviceId"
        case payloadJson = "PayloadJson"
        case contentHash = "ContentHash"
    }
}

struct SyncBatch: Codable, Sendable {
    let protocolVersion: Int
    let batchId: String
    let sourceDeviceId: String
    let targetDeviceId: String
    let sinceCheckpoint: String?
    let items: [SyncEnvelope]
    let createdAtUtc: Date

    enum CodingKeys: String, CodingKey {
        case protocolVersion = "ProtocolVersion"
        case batchId = "BatchId"
        case sourceDeviceId = "SourceDeviceId"
        case targetDeviceId = "TargetDeviceId"
        case sinceCheckpoint = "SinceCheckpoint"
        case items = "Items"
        case createdAtUtc = "CreatedAtUtc"
    }
}

struct SyncAckWire: Codable, Sendable {
    let protocolVersion: Int
    let batchId: String
    let receiverDeviceId: String
    let checkpoint: String
    let appliedEnvelopeIds: [String]?
    let appliedCount: Int?
    let appliedAtUtc: Date

    enum CodingKeys: String, CodingKey {
        case protocolVersion = "ProtocolVersion"
        case batchId = "BatchId"
        case receiverDeviceId = "ReceiverDeviceId"
        case checkpoint = "Checkpoint"
        case appliedEnvelopeIds = "AppliedEnvelopeIds"
        case appliedCount = "AppliedCount"
        case appliedAtUtc = "AppliedAtUtc"
    }
}

struct EncryptedCompanionFrame: Codable, Sendable {
    let nonceBase64: String
    let ciphertextBase64: String
    let tagBase64: String

    enum CodingKeys: String, CodingKey {
        case nonceBase64 = "NonceBase64"
        case ciphertextBase64 = "CiphertextBase64"
        case tagBase64 = "TagBase64"
    }
}
