package com.bdfr.zurvan

enum class CompanionPlatform { WINDOWS, ANDROID, IOS }
enum class SyncMutationKind { UPSERT, DELETE }
enum class SyncEntityKind {
    EVENT, TASK, NOTE, SPECIAL_OCCASION, REMINDER, SETTING, OCCASION_PICTURE_MAPPING
}

data class CompanionPairingOffer(
    val protocolVersion: Int,
    val sessionId: String,
    val hostDeviceId: String,
    val hostDisplayName: String,
    val hostPlatform: CompanionPlatform,
    val endpoint: String,
    val pairingCode: String,
    val expiresAtUtc: String,
    val hostPublicKey: String? = null
)

data class SyncEnvelope(
    val envelopeId: String,
    val entityKind: SyncEntityKind,
    val entityId: String,
    val mutation: SyncMutationKind,
    val revision: Long,
    val modifiedAtUtc: String,
    val originDeviceId: String,
    val payloadJson: String?,
    val contentHash: String? = null
)

data class SyncBatch(
    val protocolVersion: Int,
    val batchId: String,
    val sourceDeviceId: String,
    val targetDeviceId: String,
    val sinceCheckpoint: String?,
    val items: List<SyncEnvelope>,
    val createdAtUtc: String
)


data class EncryptedCompanionFrame(
    val nonceBase64: String,
    val ciphertextBase64: String,
    val tagBase64: String
)
