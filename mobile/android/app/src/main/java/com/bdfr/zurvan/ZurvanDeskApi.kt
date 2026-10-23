package com.bdfr.zurvan

import java.net.HttpURLConnection
import java.net.URL
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject

data class DeskAuthSession(
    val token: String,
    val vaultId: String,
    val displayName: String?,
    val identifier: String
)

data class DeskRemoteFile(
    val id: String,
    val encryptedName: String,
    val size: Long,
    val modifiedAtUtc: String,
    val contentType: String
)

class ZurvanDeskApi(
    private val baseUrl: String = BuildConfig.DESK_API_BASE_URL
) {
    suspend fun register(
        identifier: String,
        password: String,
        displayName: String?
    ): DeskAuthSession = requestJson(
        method = "POST",
        path = "/api/v1/auth/register",
        body = JSONObject()
            .put("identifier", identifier)
            .put("password", password)
            .put("displayName", displayName)
            .toString()
    ) { parseAuth(it) }

    suspend fun login(
        identifier: String,
        password: String
    ): DeskAuthSession = requestJson(
        method = "POST",
        path = "/api/v1/auth/login",
        body = JSONObject()
            .put("identifier", identifier)
            .put("password", password)
            .toString()
    ) { parseAuth(it) }

    suspend fun logout(token: String) {
        requestBytes(
            method = "POST",
            path = "/api/v1/auth/logout",
            token = token,
            contentType = "application/json",
            body = ByteArray(0)
        )
    }

    suspend fun putRecoveryEnvelope(
        token: String,
        payload: String
    ) {
        requestBytes(
            method = "PUT",
            path = "/api/v1/recovery",
            token = token,
            contentType = "application/json",
            body = JSONObject()
                .put("payload", payload)
                .toString()
                .toByteArray(Charsets.UTF_8)
        )
    }

    suspend fun getRecoveryEnvelope(
        token: String
    ): String = requestJson(
        method = "GET",
        path = "/api/v1/recovery",
        token = token
    ) { json ->
        json.getString("payload")
    }

    suspend fun listFiles(
        token: String
    ): List<DeskRemoteFile> = withContext(
        Dispatchers.IO
    ) {
        val connection = open(
            method = "GET",
            path = "/api/v1/files",
            token = token
        )

        try {
            val code = connection.responseCode
            val text = readBody(connection, code)
            ensureSuccess(code, text)

            val array = JSONArray(text)
            buildList {
                for (index in 0 until array.length()) {
                    add(parseFile(array.getJSONObject(index)))
                }
            }
        } finally {
            connection.disconnect()
        }
    }

    suspend fun uploadFile(
        token: String,
        encryptedName: String,
        encryptedPayload: ByteArray,
        contentType: String =
            "application/vnd.zurvan.encrypted-file+json"
    ): DeskRemoteFile = withContext(Dispatchers.IO) {
        val connection = open(
            method = "POST",
            path = "/api/v1/files",
            token = token
        ).apply {
            doOutput = true
            setRequestProperty(
                "Content-Type",
                contentType
            )
            setRequestProperty(
                "X-Zurvan-Encrypted-Name",
                encryptedName
            )
        }

        try {
            connection.outputStream.use {
                it.write(encryptedPayload)
            }

            val code = connection.responseCode
            val text = readBody(connection, code)
            ensureSuccess(code, text)
            parseFile(JSONObject(text))
        } finally {
            connection.disconnect()
        }
    }

    suspend fun downloadFile(
        token: String,
        fileId: String
    ): ByteArray = withContext(Dispatchers.IO) {
        val connection = open(
            method = "GET",
            path = "/api/v1/files/$fileId",
            token = token
        )

        try {
            val code = connection.responseCode
            if (code !in 200..299) {
                val text = readBody(connection, code)
                ensureSuccess(code, text)
            }
            connection.inputStream.use {
                it.readBytes()
            }
        } finally {
            connection.disconnect()
        }
    }

    suspend fun renameFile(
        token: String,
        fileId: String,
        encryptedName: String
    ): DeskRemoteFile = requestJson(
        method = "PUT",
        path = "/api/v1/files/$fileId/metadata",
        token = token,
        body = JSONObject()
            .put("encryptedName", encryptedName)
            .toString()
    ) {
        parseFile(it)
    }

    suspend fun deleteFile(
        token: String,
        fileId: String
    ) {
        requestBytes(
            method = "DELETE",
            path = "/api/v1/files/$fileId",
            token = token,
            contentType = "application/json",
            body = ByteArray(0)
        )
    }

    private suspend fun <T> requestJson(
        method: String,
        path: String,
        token: String? = null,
        body: String? = null,
        parser: (JSONObject) -> T
    ): T = withContext(Dispatchers.IO) {
        val payload = body?.toByteArray(Charsets.UTF_8)

        val connection = open(
            method = method,
            path = path,
            token = token
        ).apply {
            if (payload != null) {
                doOutput = true
                setRequestProperty(
                    "Content-Type",
                    "application/json"
                )
            }
        }

        try {
            if (payload != null) {
                connection.outputStream.use {
                    it.write(payload)
                }
            }

            val code = connection.responseCode
            val text = readBody(connection, code)
            ensureSuccess(code, text)
            parser(JSONObject(text))
        } finally {
            connection.disconnect()
        }
    }

    private suspend fun requestBytes(
        method: String,
        path: String,
        token: String? = null,
        contentType: String,
        body: ByteArray
    ) = withContext(Dispatchers.IO) {
        val connection = open(
            method = method,
            path = path,
            token = token
        ).apply {
            if (body.isNotEmpty()) {
                doOutput = true
                setRequestProperty(
                    "Content-Type",
                    contentType
                )
            }
        }

        try {
            if (body.isNotEmpty()) {
                connection.outputStream.use {
                    it.write(body)
                }
            }

            val code = connection.responseCode
            val text = readBody(connection, code)
            ensureSuccess(code, text)
        } finally {
            connection.disconnect()
        }
    }

    private fun open(
        method: String,
        path: String,
        token: String?
    ): HttpURLConnection {
        val url = URL(
            baseUrl.trimEnd('/') +
                "/" +
                path.trimStart('/')
        )

        return (url.openConnection() as HttpURLConnection)
            .apply {
                requestMethod = method
                connectTimeout = 20_000
                readTimeout = 30_000
                setRequestProperty(
                    "Accept",
                    "application/json"
                )

                if (!token.isNullOrBlank()) {
                    setRequestProperty(
                        "Authorization",
                        "Bearer $token"
                    )
                }
            }
    }

    private fun parseAuth(
        json: JSONObject
    ) = DeskAuthSession(
        token = json.getString("token"),
        vaultId = json.getString("vaultId"),
        displayName = json
            .optString("displayName")
            .takeIf { it.isNotBlank() },
        identifier = json.getString("identifier")
    )

    private fun parseFile(
        json: JSONObject
    ) = DeskRemoteFile(
        id = json.getString("id"),
        encryptedName = json.getString("encryptedName"),
        size = json.getLong("size"),
        modifiedAtUtc =
            json.getString("modifiedAtUtc"),
        contentType =
            json.optString(
                "contentType",
                "application/octet-stream"
            )
    )

    private fun readBody(
        connection: HttpURLConnection,
        code: Int
    ): String {
        val stream = if (code in 200..299) {
            connection.inputStream
        } else {
            connection.errorStream
        }

        return stream
            ?.bufferedReader()
            ?.use { it.readText() }
            .orEmpty()
    }

    private fun ensureSuccess(
        code: Int,
        body: String
    ) {
        if (code in 200..299)
            return

        val message = try {
            JSONObject(body)
                .optString("error")
                .ifBlank { body }
        } catch (_: Exception) {
            body
        }

        throw IllegalStateException(
            message.ifBlank {
                "Zurvan Desk request failed: $code"
            }
        )
    }
}
