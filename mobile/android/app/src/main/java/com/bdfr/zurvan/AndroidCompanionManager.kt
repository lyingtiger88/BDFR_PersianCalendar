package com.bdfr.zurvan

import android.content.Context
import android.net.ConnectivityManager
import android.os.Build
import android.util.Base64
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.io.BufferedWriter
import java.io.Closeable
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.URI
import java.security.SecureRandom
import java.time.Instant
import java.time.temporal.ChronoUnit
import java.util.UUID
import java.util.concurrent.Executors

data class TrustedCompanionDevice(
    val deviceId: String,
    val displayName: String,
    val platform: CompanionPlatform,
    val publicKeyFingerprint: String,
    val pairedAtUtc: String,
    val lastSeenUtc: String
)

data class CompanionUiState(
    val mode: String = "idle",
    val status: String = "آماده برای جفت‌سازی",
    val qrPayload: String? = null,
    val pairingCode: String? = null,
    val sas: String? = null,
    val peerName: String? = null,
    val syncedItemCount: Int = 0,
    val trustedDevices: List<TrustedCompanionDevice> = emptyList(),
    val busy: Boolean = false,
    val error: String? = null
)

data class PairingJoinWire(
    val protocolVersion: Int,
    val sessionId: String,
    val deviceId: String,
    val deviceDisplayName: String,
    val platform: CompanionPlatform,
    val pairingCode: String,
    val devicePublicKey: String
)

class AndroidCompanionManager(
    context: Context,
    private val repository: AndroidLocalRepository
) : Closeable {
    private val appContext = context.applicationContext
    private val executor = Executors.newCachedThreadPool()
    private val preferences =
        appContext.getSharedPreferences(
            "zurvan_companion",
            Context.MODE_PRIVATE
        )
    private val deviceId =
        repository.getOrCreateCompanionDeviceId()
    private val displayName =
        listOf(Build.MANUFACTURER, Build.MODEL)
            .filter { it.isNotBlank() }
            .joinToString(" ")
            .ifBlank { "Zurvan Android" }

    private val _state = MutableStateFlow(
        CompanionUiState(
            trustedDevices = loadTrustedDevices()
        )
    )
    val state: StateFlow<CompanionUiState> =
        _state.asStateFlow()

    @Volatile
    private var activeServer: ServerSocket? = null

    @Volatile
    private var activeEphemeral:
        CompanionEphemeralKeyAndroid? = null

    fun startMaster() {
        stopActiveSession(keepStatus = true)
        _state.value = _state.value.copy(
            mode = "master",
            status = "در حال ساخت جلسه Master…",
            qrPayload = null,
            pairingCode = null,
            sas = null,
            peerName = null,
            syncedItemCount = 0,
            busy = true,
            error = null
        )

        executor.execute {
            try {
                val address = findLanIpv4()
                    ?: throw IllegalStateException(
                        "آدرس IPv4 شبکه محلی پیدا نشد. هر دو دستگاه باید روی یک شبکه Wi‑Fi/LAN باشند."
                    )

                val server = ServerSocket().apply {
                    reuseAddress = true
                    bind(InetSocketAddress(0))
                }
                activeServer = server

                val ephemeral =
                    CompanionEphemeralKeyAndroid()
                activeEphemeral = ephemeral

                val sessionId =
                    "pair_" +
                        UUID.randomUUID()
                            .toString()
                            .replace("-", "")

                val code =
                    SecureRandom()
                        .nextInt(1_000_000)
                        .toString()
                        .padStart(6, '0')

                val expiresAt =
                    Instant.now()
                        .plus(
                            5,
                            ChronoUnit.MINUTES
                        )

                val offer = CompanionPairingOffer(
                    protocolVersion = 1,
                    sessionId = sessionId,
                    hostDeviceId = deviceId,
                    hostDisplayName = displayName,
                    hostPlatform =
                        CompanionPlatform.ANDROID,
                    endpoint =
                        "tcp://${address.hostAddress}:${server.localPort}",
                    pairingCode = code,
                    expiresAtUtc =
                        expiresAt.toString(),
                    hostPublicKey =
                        ephemeral.publicKeyBase64
                )

                val qr =
                    CompanionWire.encodeOfferQr(
                        offer
                    )

                _state.value =
                    _state.value.copy(
                        mode = "master",
                        status =
                            "QR آماده است؛ در دستگاه Secondary اسکن کنید.",
                        qrPayload = qr,
                        pairingCode = code,
                        busy = false,
                        error = null
                    )

                server.soTimeout =
                    5 * 60 * 1000

                server.accept().use { socket ->
                    handleMasterConnection(
                        socket,
                        offer,
                        ephemeral
                    )
                }
            } catch (error: Exception) {
                if (activeServer != null) {
                    _state.value =
                        _state.value.copy(
                            status =
                                "جفت‌سازی انجام نشد.",
                            busy = false,
                            error =
                                error.message
                                    ?: "خطای ناشناخته"
                        )
                }
            } finally {
                stopActiveSession(
                    keepStatus = true
                )
            }
        }
    }

    fun joinFromQr(rawQr: String) {
        stopActiveSession(keepStatus = true)
        _state.value = _state.value.copy(
            mode = "secondary",
            status =
                "در حال بررسی QR و اتصال امن…",
            qrPayload = null,
            pairingCode = null,
            sas = null,
            peerName = null,
            syncedItemCount = 0,
            busy = true,
            error = null
        )

        executor.execute {
            try {
                val offer =
                    CompanionWire.decodeOfferQr(
                        rawQr
                    )

                if (offer.protocolVersion != 1) {
                    throw IllegalStateException(
                        "نسخه پروتکل QR پشتیبانی نمی‌شود."
                    )
                }

                if (
                    Instant.parse(
                        offer.expiresAtUtc
                    ).isBefore(Instant.now())
                ) {
                    throw IllegalStateException(
                        "QR منقضی شده است؛ در Master یک QR جدید بسازید."
                    )
                }

                val endpoint =
                    URI(offer.endpoint)

                if (
                    !endpoint.scheme.equals(
                        "tcp",
                        ignoreCase = true
                    )
                ) {
                    throw IllegalStateException(
                        "Endpoint جفت‌سازی معتبر نیست."
                    )
                }

                val host = endpoint.host
                    ?: throw IllegalStateException(
                        "IP دستگاه Master در QR وجود ندارد."
                    )
                val port = endpoint.port

                if (port !in 1..65535) {
                    throw IllegalStateException(
                        "Port جفت‌سازی معتبر نیست."
                    )
                }

                val hostPublicKey =
                    offer.hostPublicKey
                        ?: throw IllegalStateException(
                            "کلید عمومی Master در QR وجود ندارد."
                        )

                CompanionEphemeralKeyAndroid()
                    .use { ephemeral ->
                        val sessionKey =
                            ephemeral
                                .deriveSessionKey(
                                    hostPublicKey,
                                    offer.sessionId
                                )

                        try {
                            val sas =
                                CompanionEphemeralKeyAndroid
                                    .computeSixDigitSas(
                                        sessionKey
                                    )

                            Socket().use { socket ->
                                socket.connect(
                                    InetSocketAddress(
                                        host,
                                        port
                                    ),
                                    12_000
                                )
                                socket.soTimeout =
                                    30_000

                                val reader =
                                    BufferedReader(
                                        InputStreamReader(
                                            socket.inputStream,
                                            Charsets.UTF_8
                                        )
                                    )
                                val writer =
                                    BufferedWriter(
                                        OutputStreamWriter(
                                            socket.outputStream,
                                            Charsets.UTF_8
                                        )
                                    )

                                val join =
                                    PairingJoinWire(
                                        protocolVersion = 1,
                                        sessionId =
                                            offer.sessionId,
                                        deviceId =
                                            deviceId,
                                        deviceDisplayName =
                                            displayName,
                                        platform =
                                            CompanionPlatform.ANDROID,
                                        pairingCode =
                                            offer.pairingCode,
                                        devicePublicKey =
                                            ephemeral
                                                .publicKeyBase64
                                    )

                                writeLine(
                                    writer,
                                    CompanionWire
                                        .joinToJson(join)
                                        .toString()
                                )

                                val resultJson =
                                    readEncryptedJson(
                                        reader,
                                        sessionKey
                                    )

                                if (
                                    !resultJson.optBoolean(
                                        "Accepted",
                                        false
                                    )
                                ) {
                                    throw IllegalStateException(
                                        resultJson.optString(
                                            "Error",
                                            "Master اتصال را رد کرد."
                                        )
                                    )
                                }

                                val remoteSas =
                                    resultJson.optString(
                                        "Sas",
                                        ""
                                    )

                                if (remoteSas != sas) {
                                    throw SecurityException(
                                        "کد امنیتی دو دستگاه یکسان نیست؛ اتصال متوقف شد."
                                    )
                                }

                                val masterBatch =
                                    CompanionWire
                                        .syncBatchFromJson(
                                            readEncryptedJson(
                                                reader,
                                                sessionKey
                                            )
                                        )

                                val received =
                                    repository
                                        .applyCompanionSnapshot(
                                            masterBatch,
                                            preferIncoming = true
                                        )

                                val localBatch =
                                    repository
                                        .createCompanionSnapshot(
                                            sourceDeviceId =
                                                deviceId,
                                            targetDeviceId =
                                                offer.hostDeviceId
                                        )

                                writeEncryptedJson(
                                    writer,
                                    CompanionWire
                                        .syncBatchToJson(
                                            localBatch
                                        ),
                                    sessionKey
                                )

                                readEncryptedJson(
                                    reader,
                                    sessionKey
                                )

                                rememberTrustedDevice(
                                    TrustedCompanionDevice(
                                        deviceId =
                                            offer.hostDeviceId,
                                        displayName =
                                            offer.hostDisplayName,
                                        platform =
                                            offer.hostPlatform,
                                        publicKeyFingerprint =
                                            fingerprintFromPublicKey(
                                                hostPublicKey
                                            ),
                                        pairedAtUtc =
                                            Instant.now()
                                                .toString(),
                                        lastSeenUtc =
                                            Instant.now()
                                                .toString()
                                    )
                                )

                                _state.value =
                                    _state.value.copy(
                                        mode = "paired",
                                        status =
                                            "اتصال امن برقرار شد و همگام‌سازی انجام شد.",
                                        sas = sas,
                                        peerName =
                                            offer.hostDisplayName,
                                        syncedItemCount =
                                            received,
                                        busy = false,
                                        trustedDevices =
                                            loadTrustedDevices(),
                                        error = null
                                    )
                            }
                        } finally {
                            sessionKey.fill(0)
                        }
                    }
            } catch (error: Exception) {
                _state.value =
                    _state.value.copy(
                        status =
                            "اتصال Secondary ناموفق بود.",
                        busy = false,
                        error =
                            error.message
                                ?: "خطای ناشناخته"
                    )
            }
        }
    }

    fun stop() {
        stopActiveSession(
            keepStatus = false
        )
    }

    override fun close() {
        stopActiveSession(
            keepStatus = true
        )
        executor.shutdownNow()
    }

    private fun handleMasterConnection(
        client: Socket,
        offer: CompanionPairingOffer,
        ephemeral:
            CompanionEphemeralKeyAndroid
    ) {
        client.soTimeout = 30_000

        val reader =
            BufferedReader(
                InputStreamReader(
                    client.inputStream,
                    Charsets.UTF_8
                )
            )
        val writer =
            BufferedWriter(
                OutputStreamWriter(
                    client.outputStream,
                    Charsets.UTF_8
                )
            )

        val join =
            CompanionWire.joinFromJson(
                JSONObject(
                    readLine(reader)
                )
            )

        if (
            join.protocolVersion != 1 ||
            join.sessionId !=
                offer.sessionId ||
            join.pairingCode !=
                offer.pairingCode ||
            Instant.parse(
                offer.expiresAtUtc
            ).isBefore(Instant.now())
        ) {
            throw SecurityException(
                "درخواست Pairing معتبر نیست یا QR منقضی شده است."
            )
        }

        val sessionKey =
            ephemeral.deriveSessionKey(
                join.devicePublicKey,
                offer.sessionId
            )

        try {
            val sas =
                CompanionEphemeralKeyAndroid
                    .computeSixDigitSas(
                        sessionKey
                    )

            writeEncryptedJson(
                writer,
                JSONObject()
                    .put("Accepted", true)
                    .put(
                        "DeviceId",
                        deviceId
                    )
                    .put(
                        "TrustedDeviceId",
                        join.deviceId
                    )
                    .put(
                        "CompletedAtUtc",
                        Instant.now()
                            .toString()
                    )
                    .put("Sas", sas),
                sessionKey
            )

            val masterBatch =
                repository.createCompanionSnapshot(
                    sourceDeviceId = deviceId,
                    targetDeviceId =
                        join.deviceId
                )

            writeEncryptedJson(
                writer,
                CompanionWire.syncBatchToJson(
                    masterBatch
                ),
                sessionKey
            )

            val secondaryBatch =
                CompanionWire.syncBatchFromJson(
                    readEncryptedJson(
                        reader,
                        sessionKey
                    )
                )

            val received =
                repository.applyCompanionSnapshot(
                    secondaryBatch,
                    preferIncoming = false
                )

            writeEncryptedJson(
                writer,
                JSONObject()
                    .put(
                        "ProtocolVersion",
                        1
                    )
                    .put(
                        "BatchId",
                        secondaryBatch.batchId
                    )
                    .put(
                        "ReceiverDeviceId",
                        deviceId
                    )
                    .put(
                        "Checkpoint",
                        Instant.now()
                            .toString()
                    )
                    .put(
                        "AppliedCount",
                        received
                    )
                    .put(
                        "AppliedAtUtc",
                        Instant.now()
                            .toString()
                    ),
                sessionKey
            )

            rememberTrustedDevice(
                TrustedCompanionDevice(
                    deviceId =
                        join.deviceId,
                    displayName =
                        join.deviceDisplayName,
                    platform =
                        join.platform,
                    publicKeyFingerprint =
                        fingerprintFromPublicKey(
                            join.devicePublicKey
                        ),
                    pairedAtUtc =
                        Instant.now()
                            .toString(),
                    lastSeenUtc =
                        Instant.now()
                            .toString()
                )
            )

            _state.value =
                _state.value.copy(
                    mode = "paired",
                    status =
                        "Secondary متصل شد و همگام‌سازی دوطرفه انجام شد.",
                    sas = sas,
                    peerName =
                        join.deviceDisplayName,
                    syncedItemCount =
                        received,
                    busy = false,
                    trustedDevices =
                        loadTrustedDevices(),
                    error = null
                )
        } finally {
            sessionKey.fill(0)
        }
    }

    private fun writeEncryptedJson(
        writer: BufferedWriter,
        json: JSONObject,
        sessionKey: ByteArray
    ) {
        val frame =
            CompanionEphemeralKeyAndroid
                .encrypt(
                    json.toString()
                        .toByteArray(
                            Charsets.UTF_8
                        ),
                    sessionKey
                )

        writeLine(
            writer,
            CompanionWire
                .frameToJson(frame)
                .toString()
        )
    }

    private fun readEncryptedJson(
        reader: BufferedReader,
        sessionKey: ByteArray
    ): JSONObject {
        val frame =
            CompanionWire.frameFromJson(
                JSONObject(
                    readLine(reader)
                )
            )

        val plaintext =
            CompanionEphemeralKeyAndroid
                .decrypt(
                    frame,
                    sessionKey
                )

        return try {
            JSONObject(
                plaintext.toString(
                    Charsets.UTF_8
                )
            )
        } finally {
            plaintext.fill(0)
        }
    }

    private fun writeLine(
        writer: BufferedWriter,
        value: String
    ) {
        require(
            value.length <= MAX_WIRE_LINE
        ) {
            "Companion frame is too large."
        }

        writer.write(value)
        writer.newLine()
        writer.flush()
    }

    private fun readLine(
        reader: BufferedReader
    ): String {
        val line =
            reader.readLine()
                ?: throw IllegalStateException(
                    "اتصال Companion بسته شد."
                )

        if (line.length > MAX_WIRE_LINE) {
            throw IllegalStateException(
                "Companion frame بیش از حد بزرگ است."
            )
        }

        return line
    }

    private fun findLanIpv4():
        Inet4Address? {
        val manager =
            appContext.getSystemService(
                ConnectivityManager::class.java
            )

        val network =
            manager.activeNetwork
                ?: return null

        return manager
            .getLinkProperties(network)
            ?.linkAddresses
            ?.map { it.address }
            ?.filterIsInstance<
                Inet4Address
                >()
            ?.firstOrNull {
                !it.isLoopbackAddress &&
                    !it.isLinkLocalAddress
            }
    }

    private fun rememberTrustedDevice(
        device: TrustedCompanionDevice
    ) {
        val current =
            loadTrustedDevices()
                .filterNot {
                    it.deviceId ==
                        device.deviceId
                }
                .toMutableList()
        current += device

        val array = JSONArray()
        current.forEach { item ->
            array.put(
                JSONObject()
                    .put(
                        "deviceId",
                        item.deviceId
                    )
                    .put(
                        "displayName",
                        item.displayName
                    )
                    .put(
                        "platform",
                        item.platform.name
                    )
                    .put(
                        "fingerprint",
                        item.publicKeyFingerprint
                    )
                    .put(
                        "pairedAtUtc",
                        item.pairedAtUtc
                    )
                    .put(
                        "lastSeenUtc",
                        item.lastSeenUtc
                    )
            )
        }

        preferences.edit()
            .putString(
                KEY_TRUSTED,
                array.toString()
            )
            .apply()
    }

    private fun loadTrustedDevices():
        List<TrustedCompanionDevice> {
        val raw =
            preferences.getString(
                KEY_TRUSTED,
                null
            )
                ?: return emptyList()

        return try {
            val array = JSONArray(raw)
            buildList {
                for (
                    index in
                    0 until array.length()
                ) {
                    val item =
                        array.getJSONObject(
                            index
                        )
                    add(
                        TrustedCompanionDevice(
                            deviceId =
                                item.getString(
                                    "deviceId"
                                ),
                            displayName =
                                item.optString(
                                    "displayName",
                                    "Zurvan"
                                ),
                            platform =
                                runCatching {
                                    CompanionPlatform
                                        .valueOf(
                                            item.optString(
                                                "platform",
                                                "ANDROID"
                                            )
                                        )
                                }
                                    .getOrDefault(
                                        CompanionPlatform
                                            .ANDROID
                                    ),
                            publicKeyFingerprint =
                                item.optString(
                                    "fingerprint",
                                    ""
                                ),
                            pairedAtUtc =
                                item.optString(
                                    "pairedAtUtc",
                                    ""
                                ),
                            lastSeenUtc =
                                item.optString(
                                    "lastSeenUtc",
                                    ""
                                )
                        )
                    )
                }
            }
        } catch (_: Exception) {
            emptyList()
        }
    }

    private fun stopActiveSession(
        keepStatus: Boolean
    ) {
        try {
            activeServer?.close()
        } catch (_: Exception) {
        }
        activeServer = null

        try {
            activeEphemeral?.close()
        } catch (_: Exception) {
        }
        activeEphemeral = null

        if (!keepStatus) {
            _state.value =
                _state.value.copy(
                    mode = "idle",
                    status =
                        "آماده برای جفت‌سازی",
                    qrPayload = null,
                    pairingCode = null,
                    busy = false,
                    error = null
                )
        }
    }

    private fun fingerprintFromPublicKey(
        publicKeyBase64: String
    ): String {
        val bytes =
            Base64.decode(
                publicKeyBase64,
                Base64.DEFAULT
            )
        val hash =
            java.security.MessageDigest
                .getInstance("SHA-256")
                .digest(bytes)

        return hash
            .take(8)
            .joinToString("") {
                "%02X".format(it)
            }
    }

    companion object {
        private const val KEY_TRUSTED =
            "trusted_devices"
        private const val MAX_WIRE_LINE =
            4 * 1024 * 1024
    }
}

object CompanionWire {
    fun encodeOfferQr(
        offer: CompanionPairingOffer
    ): String {
        val json =
            JSONObject()
                .put(
                    "ProtocolVersion",
                    offer.protocolVersion
                )
                .put(
                    "SessionId",
                    offer.sessionId
                )
                .put(
                    "HostDeviceId",
                    offer.hostDeviceId
                )
                .put(
                    "HostDisplayName",
                    offer.hostDisplayName
                )
                .put(
                    "HostPlatform",
                    platformName(
                        offer.hostPlatform
                    )
                )
                .put(
                    "Endpoint",
                    offer.endpoint
                )
                .put(
                    "PairingCode",
                    offer.pairingCode
                )
                .put(
                    "ExpiresAtUtc",
                    offer.expiresAtUtc
                )
                .put(
                    "HostPublicKey",
                    offer.hostPublicKey
                        ?: JSONObject.NULL
                )
                .toString()

        val encoded =
            Base64.encodeToString(
                json.toByteArray(
                    Charsets.UTF_8
                ),
                Base64.URL_SAFE or
                    Base64.NO_WRAP or
                    Base64.NO_PADDING
            )

        return "zurvan://pair/v1/$encoded"
    }

    fun decodeOfferQr(
        rawQr: String
    ): CompanionPairingOffer {
        val prefix =
            "zurvan://pair/v1/"

        require(
            rawQr.startsWith(
                prefix,
                ignoreCase = true
            )
        ) {
            "این QR متعلق به Zurvan Companion نیست."
        }

        val encoded =
            rawQr.substring(
                prefix.length
            )

        val json =
            String(
                Base64.decode(
                    encoded,
                    Base64.URL_SAFE or
                        Base64.NO_WRAP
                ),
                Charsets.UTF_8
            )

        val obj =
            JSONObject(json)

        return CompanionPairingOffer(
            protocolVersion =
                obj.getInt(
                    "ProtocolVersion"
                ),
            sessionId =
                obj.getString(
                    "SessionId"
                ),
            hostDeviceId =
                obj.getString(
                    "HostDeviceId"
                ),
            hostDisplayName =
                obj.getString(
                    "HostDisplayName"
                ),
            hostPlatform =
                platformFromName(
                    obj.getString(
                        "HostPlatform"
                    )
                ),
            endpoint =
                obj.getString(
                    "Endpoint"
                ),
            pairingCode =
                obj.getString(
                    "PairingCode"
                ),
            expiresAtUtc =
                obj.getString(
                    "ExpiresAtUtc"
                ),
            hostPublicKey =
                if (
                    obj.isNull(
                        "HostPublicKey"
                    )
                ) {
                    null
                } else {
                    obj.getString(
                        "HostPublicKey"
                    )
                }
        )
    }

    fun joinToJson(
        join: PairingJoinWire
    ): JSONObject =
        JSONObject()
            .put(
                "ProtocolVersion",
                join.protocolVersion
            )
            .put(
                "SessionId",
                join.sessionId
            )
            .put(
                "DeviceId",
                join.deviceId
            )
            .put(
                "DeviceDisplayName",
                join.deviceDisplayName
            )
            .put(
                "Platform",
                platformName(
                    join.platform
                )
            )
            .put(
                "PairingCode",
                join.pairingCode
            )
            .put(
                "DevicePublicKey",
                join.devicePublicKey
            )

    fun joinFromJson(
        obj: JSONObject
    ): PairingJoinWire =
        PairingJoinWire(
            protocolVersion =
                obj.getInt(
                    "ProtocolVersion"
                ),
            sessionId =
                obj.getString(
                    "SessionId"
                ),
            deviceId =
                obj.getString(
                    "DeviceId"
                ),
            deviceDisplayName =
                obj.getString(
                    "DeviceDisplayName"
                ),
            platform =
                platformFromName(
                    obj.getString(
                        "Platform"
                    )
                ),
            pairingCode =
                obj.getString(
                    "PairingCode"
                ),
            devicePublicKey =
                obj.getString(
                    "DevicePublicKey"
                )
        )

    fun frameToJson(
        frame:
            EncryptedCompanionFrame
    ): JSONObject =
        JSONObject()
            .put(
                "NonceBase64",
                frame.nonceBase64
            )
            .put(
                "CiphertextBase64",
                frame.ciphertextBase64
            )
            .put(
                "TagBase64",
                frame.tagBase64
            )

    fun frameFromJson(
        obj: JSONObject
    ): EncryptedCompanionFrame =
        EncryptedCompanionFrame(
            nonceBase64 =
                obj.getString(
                    "NonceBase64"
                ),
            ciphertextBase64 =
                obj.getString(
                    "CiphertextBase64"
                ),
            tagBase64 =
                obj.getString(
                    "TagBase64"
                )
        )

    fun syncBatchToJson(
        batch: SyncBatch
    ): JSONObject {
        val items = JSONArray()

        batch.items.forEach { item ->
            items.put(
                JSONObject()
                    .put(
                        "EnvelopeId",
                        item.envelopeId
                    )
                    .put(
                        "EntityKind",
                        entityKindName(
                            item.entityKind
                        )
                    )
                    .put(
                        "EntityId",
                        item.entityId
                    )
                    .put(
                        "Mutation",
                        mutationName(
                            item.mutation
                        )
                    )
                    .put(
                        "Revision",
                        item.revision
                    )
                    .put(
                        "ModifiedAtUtc",
                        item.modifiedAtUtc
                    )
                    .put(
                        "OriginDeviceId",
                        item.originDeviceId
                    )
                    .put(
                        "PayloadJson",
                        item.payloadJson
                            ?: JSONObject.NULL
                    )
                    .put(
                        "ContentHash",
                        item.contentHash
                            ?: JSONObject.NULL
                    )
            )
        }

        return JSONObject()
            .put(
                "ProtocolVersion",
                batch.protocolVersion
            )
            .put(
                "BatchId",
                batch.batchId
            )
            .put(
                "SourceDeviceId",
                batch.sourceDeviceId
            )
            .put(
                "TargetDeviceId",
                batch.targetDeviceId
            )
            .put(
                "SinceCheckpoint",
                batch.sinceCheckpoint
                    ?: JSONObject.NULL
            )
            .put("Items", items)
            .put(
                "CreatedAtUtc",
                batch.createdAtUtc
            )
    }

    fun syncBatchFromJson(
        obj: JSONObject
    ): SyncBatch {
        val itemsJson =
            obj.getJSONArray("Items")

        val items = buildList {
            for (
                index in
                0 until
                    itemsJson.length()
            ) {
                val item =
                    itemsJson.getJSONObject(
                        index
                    )

                add(
                    SyncEnvelope(
                        envelopeId =
                            item.getString(
                                "EnvelopeId"
                            ),
                        entityKind =
                            entityKindFromName(
                                item.getString(
                                    "EntityKind"
                                )
                            ),
                        entityId =
                            item.getString(
                                "EntityId"
                            ),
                        mutation =
                            mutationFromName(
                                item.getString(
                                    "Mutation"
                                )
                            ),
                        revision =
                            item.getLong(
                                "Revision"
                            ),
                        modifiedAtUtc =
                            item.getString(
                                "ModifiedAtUtc"
                            ),
                        originDeviceId =
                            item.getString(
                                "OriginDeviceId"
                            ),
                        payloadJson =
                            if (
                                item.isNull(
                                    "PayloadJson"
                                )
                            ) {
                                null
                            } else {
                                item.getString(
                                    "PayloadJson"
                                )
                            },
                        contentHash =
                            if (
                                item.isNull(
                                    "ContentHash"
                                )
                            ) {
                                null
                            } else {
                                item.getString(
                                    "ContentHash"
                                )
                            }
                    )
                )
            }
        }

        return SyncBatch(
            protocolVersion =
                obj.getInt(
                    "ProtocolVersion"
                ),
            batchId =
                obj.getString(
                    "BatchId"
                ),
            sourceDeviceId =
                obj.getString(
                    "SourceDeviceId"
                ),
            targetDeviceId =
                obj.getString(
                    "TargetDeviceId"
                ),
            sinceCheckpoint =
                if (
                    obj.isNull(
                        "SinceCheckpoint"
                    )
                ) {
                    null
                } else {
                    obj.getString(
                        "SinceCheckpoint"
                    )
                },
            items = items,
            createdAtUtc =
                obj.getString(
                    "CreatedAtUtc"
                )
        )
    }

    private fun platformName(
        value: CompanionPlatform
    ) = when (value) {
        CompanionPlatform.WINDOWS ->
            "Windows"
        CompanionPlatform.ANDROID ->
            "Android"
        CompanionPlatform.IOS ->
            "IOS"
    }

    private fun platformFromName(
        value: String
    ) = when (
        value.lowercase()
    ) {
        "windows" ->
            CompanionPlatform.WINDOWS
        "ios" ->
            CompanionPlatform.IOS
        else ->
            CompanionPlatform.ANDROID
    }

    private fun entityKindName(
        value: SyncEntityKind
    ) = when (value) {
        SyncEntityKind.EVENT ->
            "Event"
        SyncEntityKind.TASK ->
            "Task"
        SyncEntityKind.NOTE ->
            "Note"
        SyncEntityKind.SPECIAL_OCCASION ->
            "SpecialOccasion"
        SyncEntityKind.REMINDER ->
            "Reminder"
        SyncEntityKind.SETTING ->
            "Setting"
        SyncEntityKind.OCCASION_PICTURE_MAPPING ->
            "OccasionPictureMapping"
    }

    private fun entityKindFromName(
        value: String
    ) = when (
        value.lowercase()
    ) {
        "event" ->
            SyncEntityKind.EVENT
        "task" ->
            SyncEntityKind.TASK
        "note" ->
            SyncEntityKind.NOTE
        "specialoccasion" ->
            SyncEntityKind.SPECIAL_OCCASION
        "reminder" ->
            SyncEntityKind.REMINDER
        "setting" ->
            SyncEntityKind.SETTING
        else ->
            SyncEntityKind.OCCASION_PICTURE_MAPPING
    }

    private fun mutationName(
        value: SyncMutationKind
    ) = when (value) {
        SyncMutationKind.UPSERT ->
            "Upsert"
        SyncMutationKind.DELETE ->
            "Delete"
    }

    private fun mutationFromName(
        value: String
    ) =
        if (
            value.equals(
                "Delete",
                ignoreCase = true
            )
        ) {
            SyncMutationKind.DELETE
        } else {
            SyncMutationKind.UPSERT
        }
}
