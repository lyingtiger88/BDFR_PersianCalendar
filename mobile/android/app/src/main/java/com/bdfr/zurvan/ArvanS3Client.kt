package com.bdfr.zurvan

import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import java.security.MessageDigest
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

data class ArvanS3ProfileAndroid(
    val endpoint: String = "https://s3.ir-thr-at1.arvanstorage.ir",
    val region: String = "ir-thr-at1",
    val bucket: String,
    val accessKeyId: String,
    val secretAccessKey: String
)

class ArvanS3ClientAndroid(
    private val profile: ArvanS3ProfileAndroid
) {
    fun put(
        key: String,
        payload: ByteArray,
        contentType: String = "application/octet-stream"
    ) {
        val connection = openSignedConnection("PUT", key, payload)
        connection.doOutput = true
        connection.setRequestProperty("Content-Type", contentType)
        connection.outputStream.use { it.write(payload) }
        ensureSuccess(connection)
        connection.disconnect()
    }

    fun get(key: String): ByteArray? {
        val connection = openSignedConnection("GET", key, byteArrayOf())
        val code = connection.responseCode
        if (code == HttpURLConnection.HTTP_NOT_FOUND) {
            connection.disconnect()
            return null
        }
        ensureSuccess(connection)
        val result = connection.inputStream.use { it.readBytes() }
        connection.disconnect()
        return result
    }

    fun delete(key: String) {
        val connection = openSignedConnection("DELETE", key, byteArrayOf())
        if (connection.responseCode != HttpURLConnection.HTTP_NOT_FOUND) {
            ensureSuccess(connection)
        }
        connection.disconnect()
    }

    fun exists(key: String): Boolean {
        val connection = openSignedConnection("HEAD", key, byteArrayOf())
        val code = connection.responseCode
        if (code == HttpURLConnection.HTTP_NOT_FOUND) {
            connection.disconnect()
            return false
        }
        ensureSuccess(connection)
        connection.disconnect()
        return true
    }

    private fun openSignedConnection(
        method: String,
        key: String,
        payload: ByteArray
    ): HttpURLConnection {
        require(profile.endpoint.startsWith("https://")) {
            "Zurvan Cloud requires an HTTPS endpoint."
        }

        val now = Date()
        val amzDate = utcFormat("yyyyMMdd'T'HHmmss'Z'").format(now)
        val shortDate = utcFormat("yyyyMMdd").format(now)
        val payloadHash = sha256Hex(payload)
        val canonicalUri = canonicalPath(profile.bucket, key)
        val endpoint = profile.endpoint.trimEnd('/')
        val url = URL(endpoint + canonicalUri)

        val canonicalHeaders =
            "host:${url.host}\n" +
                "x-amz-content-sha256:$payloadHash\n" +
                "x-amz-date:$amzDate\n"

        val signedHeaders = "host;x-amz-content-sha256;x-amz-date"

        val canonicalRequest =
            "$method\n$canonicalUri\n\n" +
                "$canonicalHeaders\n" +
                "$signedHeaders\n" +
                payloadHash

        val scope =
            "$shortDate/${profile.region}/s3/aws4_request"

        val stringToSign =
            "AWS4-HMAC-SHA256\n" +
                "$amzDate\n" +
                "$scope\n" +
                sha256Hex(canonicalRequest.toByteArray(Charsets.UTF_8))

        val signingKey = signingKey(
            profile.secretAccessKey,
            shortDate,
            profile.region
        )

        val signature = hex(
            hmacSha256(
                signingKey,
                stringToSign.toByteArray(Charsets.UTF_8)
            )
        )
        signingKey.fill(0)

        val authorization =
            "AWS4-HMAC-SHA256 " +
                "Credential=${profile.accessKeyId}/$scope, " +
                "SignedHeaders=$signedHeaders, " +
                "Signature=$signature"

        return (url.openConnection() as HttpURLConnection).apply {
            requestMethod = method
            connectTimeout = 30_000
            readTimeout = 30_000
            setRequestProperty("x-amz-date", amzDate)
            setRequestProperty("x-amz-content-sha256", payloadHash)
            setRequestProperty("Authorization", authorization)
        }
    }

    private fun ensureSuccess(connection: HttpURLConnection) {
        val code = connection.responseCode
        if (code in 200..299) return

        val body = try {
            connection.errorStream?.use { stream ->
                val out = ByteArrayOutputStream()
                stream.copyTo(out)
                out.toString(Charsets.UTF_8.name()).take(1000)
            }.orEmpty()
        } catch (_: Exception) {
            ""
        }

        throw IllegalStateException(
            "Arvan S3 request failed: $code ${connection.responseMessage}. $body"
        )
    }

    private fun canonicalPath(bucket: String, key: String): String {
        val encodedBucket = encode(bucket)
        val encodedKey = key
            .trim('/')
            .split('/')
            .filter { it.isNotEmpty() }
            .joinToString("/") { encode(it) }

        return if (encodedKey.isEmpty()) {
            "/$encodedBucket"
        } else {
            "/$encodedBucket/$encodedKey"
        }
    }

    private fun encode(value: String): String =
        URLEncoder.encode(value, Charsets.UTF_8.name())
            .replace("+", "%20")
            .replace("%7E", "~", ignoreCase = true)

    private fun signingKey(
        secret: String,
        shortDate: String,
        region: String
    ): ByteArray {
        val kDate = hmacSha256(
            ("AWS4" + secret).toByteArray(Charsets.UTF_8),
            shortDate.toByteArray(Charsets.UTF_8)
        )
        val kRegion = hmacSha256(
            kDate,
            region.toByteArray(Charsets.UTF_8)
        )
        val kService = hmacSha256(
            kRegion,
            "s3".toByteArray(Charsets.UTF_8)
        )
        val result = hmacSha256(
            kService,
            "aws4_request".toByteArray(Charsets.UTF_8)
        )

        kDate.fill(0)
        kRegion.fill(0)
        kService.fill(0)
        return result
    }

    private fun hmacSha256(
        key: ByteArray,
        data: ByteArray
    ): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        return mac.doFinal(data)
    }

    private fun sha256Hex(data: ByteArray): String =
        hex(MessageDigest.getInstance("SHA-256").digest(data))

    private fun hex(data: ByteArray): String =
        data.joinToString("") { "%02x".format(it) }

    private fun utcFormat(pattern: String) =
        SimpleDateFormat(pattern, Locale.US).apply {
            timeZone = TimeZone.getTimeZone("UTC")
        }
}
