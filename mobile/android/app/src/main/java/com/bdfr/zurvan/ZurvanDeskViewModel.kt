package com.bdfr.zurvan

import android.app.Application
import android.content.ContentValues
import android.os.Environment
import android.provider.MediaStore
import android.util.Base64
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import java.util.UUID
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import org.json.JSONObject

data class DeskUiFile(
    val id: String,
    val name: String,
    val size: Long,
    val modifiedAtUtc: String,
    val contentType: String,
    val clientObjectId: String
)

data class DeskUiState(
    val loading: Boolean = false,
    val authenticated: Boolean = false,
    val identifier: String? = null,
    val displayName: String? = null,
    val vaultId: String? = null,
    val needsRecovery: Boolean = false,
    val newRecoveryPhrase: String? = null,
    val files: List<DeskUiFile> = emptyList(),
    val error: String? = null,
    val message: String? = null
)

class ZurvanDeskViewModel(
    application: Application
) : AndroidViewModel(application) {
    private val api = ZurvanDeskApi()
    private val secureStore =
        ZurvanCloudSecureStore(application)

    private val _state = MutableStateFlow(
        DeskUiState(
            authenticated = secureStore
                .get(
                    ZurvanCloudSecureStore
                        .DESK_SESSION_TOKEN
                )
                .isNullOrBlank()
                .not(),
            identifier = secureStore.get(
                ZurvanCloudSecureStore
                    .DESK_ACCOUNT_IDENTIFIER
            ),
            vaultId = secureStore.get(
                ZurvanCloudSecureStore
                    .DESK_VAULT_ID
            )
        )
    )

    val state: StateFlow<DeskUiState> =
        _state.asStateFlow()

    init {
        if (_state.value.authenticated) {
            refreshFiles()
        }
    }

    fun register(
        identifier: String,
        password: String,
        displayName: String?
    ) {
        viewModelScope.launch {
            setLoading(true)

            try {
                val session = api.register(
                    identifier.trim(),
                    password,
                    displayName
                )

                persistSession(session)

                val vaultKey =
                    ZurvanCloudCryptoAndroid
                        .createVaultKey()

                val phrase =
                    ZurvanRecoveryPhraseAndroid
                        .generate(
                            getApplication()
                        )

                try {
                    val envelope =
                        ZurvanRecoveryPhraseAndroid
                            .wrapVaultKey(
                                getApplication(),
                                phrase,
                                session.vaultId,
                                vaultKey
                            )

                    api.putRecoveryEnvelope(
                        session.token,
                        recoveryEnvelopeToJson(
                            envelope
                        )
                    )

                    secureStore.put(
                        ZurvanCloudSecureStore
                            .VAULT_KEY,
                        ZurvanCloudCryptoAndroid
                            .encodeRecoveryKey(
                                vaultKey
                            )
                    )
                } finally {
                    vaultKey.fill(0)
                }

                _state.value = DeskUiState(
                    authenticated = true,
                    identifier =
                        session.identifier,
                    displayName =
                        session.displayName,
                    vaultId =
                        session.vaultId,
                    newRecoveryPhrase = phrase,
                    message =
                        "حساب ساخته شد. عبارت بازیابی را در محل امن یادداشت کنید."
                )

                refreshFiles()
            } catch (ex: Exception) {
                setError(ex)
            } finally {
                setLoading(false)
            }
        }
    }

    fun login(
        identifier: String,
        password: String
    ) {
        viewModelScope.launch {
            setLoading(true)

            try {
                val session = api.login(
                    identifier.trim(),
                    password
                )

                val existingVaultId =
                    secureStore.get(
                        ZurvanCloudSecureStore
                            .DESK_VAULT_ID
                    )

                val existingVaultKey =
                    secureStore.get(
                        ZurvanCloudSecureStore
                            .VAULT_KEY
                    )

                persistSession(session)

                val canUnlock =
                    !existingVaultKey.isNullOrBlank() &&
                    existingVaultId ==
                        session.vaultId

                _state.value =
                    _state.value.copy(
                        authenticated = true,
                        identifier =
                            session.identifier,
                        displayName =
                            session.displayName,
                        vaultId =
                            session.vaultId,
                        needsRecovery =
                            !canUnlock,
                        error = null,
                        message = if (
                            canUnlock
                        ) {
                            "ورود انجام شد."
                        } else {
                            "برای بازکردن فایل‌ها، عبارت بازیابی ۲۴ کلمه‌ای را وارد کنید."
                        }
                    )

                if (canUnlock) {
                    refreshFiles()
                }
            } catch (ex: Exception) {
                setError(ex)
            } finally {
                setLoading(false)
            }
        }
    }

    fun restoreVault(
        phrase: String
    ) {
        viewModelScope.launch {
            setLoading(true)

            try {
                val token = requireToken()
                val payload =
                    api.getRecoveryEnvelope(
                        token
                    )

                val envelope =
                    parseRecoveryEnvelope(
                        payload
                    )

                val vaultKey =
                    ZurvanRecoveryPhraseAndroid
                        .unwrapVaultKey(
                            getApplication(),
                            phrase,
                            envelope
                        )

                try {
                    secureStore.put(
                        ZurvanCloudSecureStore
                            .VAULT_KEY,
                        ZurvanCloudCryptoAndroid
                            .encodeRecoveryKey(
                                vaultKey
                            )
                    )
                } finally {
                    vaultKey.fill(0)
                }

                _state.value =
                    _state.value.copy(
                        needsRecovery = false,
                        error = null,
                        message =
                            "Vault با موفقیت بازیابی شد."
                    )

                refreshFiles()
            } catch (ex: Exception) {
                setError(ex)
            } finally {
                setLoading(false)
            }
        }
    }

    fun logout() {
        viewModelScope.launch {
            val token = secureStore.get(
                ZurvanCloudSecureStore
                    .DESK_SESSION_TOKEN
            )

            try {
                if (!token.isNullOrBlank()) {
                    api.logout(token)
                }
            } catch (_: Exception) {
                // Local logout must still succeed
                // even if the network is unavailable.
            }

            secureStore.remove(
                ZurvanCloudSecureStore
                    .DESK_SESSION_TOKEN
            )
            secureStore.remove(
                ZurvanCloudSecureStore
                    .DESK_ACCOUNT_IDENTIFIER
            )

            _state.value = DeskUiState(
                vaultId = secureStore.get(
                    ZurvanCloudSecureStore
                        .DESK_VAULT_ID
                )
            )
        }
    }

    fun refreshFiles() {
        viewModelScope.launch {
            val token = secureStore.get(
                ZurvanCloudSecureStore
                    .DESK_SESSION_TOKEN
            ) ?: return@launch

            val vaultKey = readVaultKeyOrNull()
            if (vaultKey == null) {
                _state.value =
                    _state.value.copy(
                        needsRecovery = true
                    )
                return@launch
            }

            setLoading(true)

            try {
                val remote =
                    api.listFiles(token)

                val files = remote.map { file ->
                    decryptRemoteFile(
                        file,
                        vaultKey
                    )
                }

                _state.value =
                    _state.value.copy(
                        files = files,
                        authenticated = true,
                        needsRecovery = false,
                        error = null
                    )
            } catch (ex: Exception) {
                setError(ex)
            } finally {
                vaultKey.fill(0)
                setLoading(false)
            }
        }
    }

    fun uploadFile(
        displayName: String,
        mimeType: String?,
        bytes: ByteArray
    ) {
        viewModelScope.launch {
            val token = requireToken()
            val vaultId = requireVaultId()
            val vaultKey = readVaultKeyOrNull()

            if (vaultKey == null) {
                _state.value =
                    _state.value.copy(
                        needsRecovery = true
                    )
                return@launch
            }

            setLoading(true)

            try {
                val clientObjectId =
                    "desk_${UUID.randomUUID()}"

                val encryptedContent =
                    ZurvanCloudCryptoAndroid
                        .encrypt(
                            bytes,
                            vaultKey,
                            vaultId,
                            clientObjectId
                        )

                val encryptedName =
                    ZurvanCloudCryptoAndroid
                        .encrypt(
                            displayName
                                .toByteArray(
                                    Charsets.UTF_8
                                ),
                            vaultKey,
                            vaultId,
                            "$clientObjectId:name"
                        )

                val nameMetadata =
                    JSONObject()
                        .put(
                            "objectId",
                            clientObjectId
                        )
                        .put(
                            "nonce",
                            encryptedName
                                .nonceBase64
                        )
                        .put(
                            "ciphertext",
                            encryptedName
                                .ciphertextBase64
                        )
                        .put(
                            "tag",
                            encryptedName
                                .tagBase64
                        )
                        .toString()

                val contentEnvelope =
                    JSONObject()
                        .put(
                            "objectId",
                            clientObjectId
                        )
                        .put(
                            "nonce",
                            encryptedContent
                                .nonceBase64
                        )
                        .put(
                            "ciphertext",
                            encryptedContent
                                .ciphertextBase64
                        )
                        .put(
                            "tag",
                            encryptedContent
                                .tagBase64
                        )
                        .put(
                            "mime",
                            mimeType
                                ?: "application/octet-stream"
                        )
                        .toString()
                        .toByteArray(
                            Charsets.UTF_8
                        )

                api.uploadFile(
                    token = token,
                    encryptedName =
                        Base64.encodeToString(
                            nameMetadata.toByteArray(
                                Charsets.UTF_8
                            ),
                            Base64.NO_WRAP
                        ),
                    encryptedPayload =
                        contentEnvelope
                )

                _state.value =
                    _state.value.copy(
                        message =
                            "فایل با رمزگذاری سمت کلاینت آپلود شد."
                    )

                refreshFiles()
            } catch (ex: Exception) {
                setError(ex)
            } finally {
                bytes.fill(0)
                vaultKey.fill(0)
                setLoading(false)
            }
        }
    }

    fun downloadToDownloads(
        file: DeskUiFile
    ) {
        viewModelScope.launch {
            val token = requireToken()
            val vaultId = requireVaultId()
            val vaultKey = readVaultKeyOrNull()

            if (vaultKey == null) {
                _state.value =
                    _state.value.copy(
                        needsRecovery = true
                    )
                return@launch
            }

            setLoading(true)

            try {
                val envelopeBytes =
                    api.downloadFile(
                        token,
                        file.id
                    )

                val json = JSONObject(
                    envelopeBytes.toString(
                        Charsets.UTF_8
                    )
                )

                val encrypted =
                    ZurvanCloudEncryptedPayload(
                        nonceBase64 =
                            json.getString("nonce"),
                        ciphertextBase64 =
                            json.getString(
                                "ciphertext"
                            ),
                        tagBase64 =
                            json.getString("tag")
                    )

                val plaintext =
                    ZurvanCloudCryptoAndroid
                        .decrypt(
                            encrypted,
                            vaultKey,
                            vaultId,
                            json.getString(
                                "objectId"
                            )
                        )

                try {
                    saveToDownloads(
                        file.name,
                        file.contentType,
                        plaintext
                    )
                } finally {
                    plaintext.fill(0)
                }

                _state.value =
                    _state.value.copy(
                        message =
                            "فایل رمزگشایی و در Downloads ذخیره شد."
                    )
            } catch (ex: Exception) {
                setError(ex)
            } finally {
                vaultKey.fill(0)
                setLoading(false)
            }
        }
    }

    fun deleteFile(
        file: DeskUiFile
    ) {
        viewModelScope.launch {
            setLoading(true)

            try {
                api.deleteFile(
                    requireToken(),
                    file.id
                )

                _state.value =
                    _state.value.copy(
                        message =
                            "فایل حذف شد."
                    )

                refreshFiles()
            } catch (ex: Exception) {
                setError(ex)
            } finally {
                setLoading(false)
            }
        }
    }

    fun clearRecoveryPhraseNotice() {
        _state.value =
            _state.value.copy(
                newRecoveryPhrase = null
            )
    }

    fun clearMessage() {
        _state.value =
            _state.value.copy(
                message = null,
                error = null
            )
    }

    private fun persistSession(
        session: DeskAuthSession
    ) {
        secureStore.put(
            ZurvanCloudSecureStore
                .DESK_SESSION_TOKEN,
            session.token
        )

        secureStore.put(
            ZurvanCloudSecureStore
                .DESK_VAULT_ID,
            session.vaultId
        )

        secureStore.put(
            ZurvanCloudSecureStore
                .DESK_ACCOUNT_IDENTIFIER,
            session.identifier
        )
    }

    private fun readVaultKeyOrNull():
        ByteArray? {
        val encoded = secureStore.get(
            ZurvanCloudSecureStore
                .VAULT_KEY
        ) ?: return null

        return try {
            ZurvanCloudCryptoAndroid
                .decodeRecoveryKey(
                    encoded
                )
        } catch (_: Exception) {
            null
        }
    }

    private fun decryptRemoteFile(
        remote: DeskRemoteFile,
        vaultKey: ByteArray
    ): DeskUiFile {
        return try {
            val metadataBytes =
                Base64.decode(
                    remote.encryptedName,
                    Base64.DEFAULT
                )

            val json = JSONObject(
                metadataBytes.toString(
                    Charsets.UTF_8
                )
            )

            val objectId =
                json.getString("objectId")

            val encrypted =
                ZurvanCloudEncryptedPayload(
                    nonceBase64 =
                        json.getString("nonce"),
                    ciphertextBase64 =
                        json.getString(
                            "ciphertext"
                        ),
                    tagBase64 =
                        json.getString("tag")
                )

            val nameBytes =
                ZurvanCloudCryptoAndroid
                    .decrypt(
                        encrypted,
                        vaultKey,
                        requireVaultId(),
                        "$objectId:name"
                    )

            val name = try {
                nameBytes.toString(
                    Charsets.UTF_8
                )
            } finally {
                nameBytes.fill(0)
            }

            DeskUiFile(
                id = remote.id,
                name = name,
                size = remote.size,
                modifiedAtUtc =
                    remote.modifiedAtUtc,
                contentType =
                    remote.contentType,
                clientObjectId =
                    objectId
            )
        } catch (_: Exception) {
            DeskUiFile(
                id = remote.id,
                name = "فایل رمزگذاری‌شده",
                size = remote.size,
                modifiedAtUtc =
                    remote.modifiedAtUtc,
                contentType =
                    remote.contentType,
                clientObjectId = ""
            )
        }
    }

    private fun saveToDownloads(
        name: String,
        mimeType: String,
        bytes: ByteArray
    ) {
        val resolver =
            getApplication<Application>()
                .contentResolver

        val values = ContentValues()
            .apply {
                put(
                    MediaStore.Downloads
                        .DISPLAY_NAME,
                    name
                )
                put(
                    MediaStore.Downloads
                        .MIME_TYPE,
                    mimeType
                )
                put(
                    MediaStore.Downloads
                        .RELATIVE_PATH,
                    Environment
                        .DIRECTORY_DOWNLOADS +
                        "/Zurvan"
                )
            }

        val uri = resolver.insert(
            MediaStore.Downloads
                .EXTERNAL_CONTENT_URI,
            values
        ) ?: throw IllegalStateException(
            "ساخت فایل خروجی در Downloads انجام نشد."
        )

        resolver.openOutputStream(uri)
            ?.use { it.write(bytes) }
            ?: throw IllegalStateException(
                "نوشتن فایل خروجی انجام نشد."
            )
    }

    private fun recoveryEnvelopeToJson(
        envelope:
            ZurvanCloudRecoveryEnvelopeAndroid
    ): String =
        JSONObject()
            .put(
                "protocolVersion",
                envelope.protocolVersion
            )
            .put(
                "vaultId",
                envelope.vaultId
            )
            .put(
                "wordCount",
                envelope.wordCount
            )
            .put(
                "phraseFingerprint",
                envelope.phraseFingerprint
            )
            .put(
                "nonceBase64",
                envelope.nonceBase64
            )
            .put(
                "wrappedVaultKeyBase64",
                envelope
                    .wrappedVaultKeyBase64
            )
            .put(
                "tagBase64",
                envelope.tagBase64
            )
            .toString()

    private fun parseRecoveryEnvelope(
        value: String
    ): ZurvanCloudRecoveryEnvelopeAndroid {
        val json = JSONObject(value)

        return ZurvanCloudRecoveryEnvelopeAndroid(
            protocolVersion =
                json.getInt(
                    "protocolVersion"
                ),
            vaultId =
                json.getString("vaultId"),
            wordCount =
                json.getInt("wordCount"),
            phraseFingerprint =
                json.getString(
                    "phraseFingerprint"
                ),
            nonceBase64 =
                json.getString(
                    "nonceBase64"
                ),
            wrappedVaultKeyBase64 =
                json.getString(
                    "wrappedVaultKeyBase64"
                ),
            tagBase64 =
                json.getString(
                    "tagBase64"
                )
        )
    }

    private fun requireToken(): String =
        secureStore.get(
            ZurvanCloudSecureStore
                .DESK_SESSION_TOKEN
        ) ?: throw IllegalStateException(
            "ابتدا وارد حساب شوید."
        )

    private fun requireVaultId(): String =
        secureStore.get(
            ZurvanCloudSecureStore
                .DESK_VAULT_ID
        ) ?: throw IllegalStateException(
            "Vault ID در دسترس نیست."
        )

    private fun setLoading(value: Boolean) {
        _state.value =
            _state.value.copy(
                loading = value
            )
    }

    private fun setError(ex: Exception) {
        _state.value =
            _state.value.copy(
                error = ex.message
                    ?: "خطای نامشخص",
                message = null
            )
    }
}
