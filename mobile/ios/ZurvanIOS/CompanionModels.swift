import Foundation

enum CompanionPlatform: String, Codable {
    case windows
    case android
    case ios
}

enum SyncMutationKind: String, Codable {
    case upsert
    case delete
}

enum SyncEntityKind: String, Codable {
    case event
    case task
    case note
    case specialOccasion
    case reminder
    case setting
    case occasionPictureMapping
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
}

struct SyncBatch: Codable, Sendable {
    let protocolVersion: Int
    let batchId: String
    let sourceDeviceId: String
    let targetDeviceId: String
    let sinceCheckpoint: String?
    let items: [SyncEnvelope]
    let createdAtUtc: Date
}
