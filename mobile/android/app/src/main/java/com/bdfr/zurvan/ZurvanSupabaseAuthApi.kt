package com.bdfr.zurvan

import java.net.HttpURLConnection
import java.net.URL
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject

data class SupabaseAuthSession(
    val accessToken: String,
    val refreshToken: String,
    val userId: String,
    val email: String?,
    val expiresIn: Long,
    val tokenType: String
)

data class SupabaseSignUpResult(
    val session: SupabaseAuthSession?,
    val confirmationRequired: Boolean
)

/**
 * Supabase Auth REST client.
 *
 * Uses only the project's publishable/anon key. The service-role key must
 * never be shipped in a desktop or mobile client.
 */
class ZurvanSupabaseAuthApi(
    private val projectUrl: String = BuildConfig.SUPABASE_URL,
    private val publishableKey: String = BuildConfig.SUPABASE_PUBLISHABLE_KEY
) {
    suspend fun signUp(email: String, password: String): SupabaseSignUpResult =
        request("POST", "/auth/v1/signup", body = JSONObject()
            .put("email", email.trim())
            .put("password", password)) { json ->
            val session = parseSessionOrNull(json)
            SupabaseSignUpResult(
                session = session,
                confirmationRequired = session == null
            )
        }

    suspend fun signIn(email: String, password: String): SupabaseAuthSession =
        request("POST", "/auth/v1/token?grant_type=password", body = JSONObject()
            .put("email", email.trim())
            .put("password", password)) { json ->
            parseSessionOrNull(json) ?: error("پاسخ ورود Supabase فاقد نشست معتبر است.")
        }

    suspend fun refresh(refreshToken: String): SupabaseAuthSession =
        request("POST", "/auth/v1/token?grant_type=refresh_token", body = JSONObject()
            .put("refresh_token", refreshToken)) { json ->
            parseSessionOrNull(json) ?: error("تمدید نشست Supabase ناموفق بود.")
        }

    suspend fun getUser(accessToken: String): JSONObject =
        request("GET", "/auth/v1/user", accessToken = accessToken, body = null) { it }

    suspend fun signOut(accessToken: String) {
        request("POST", "/auth/v1/logout", accessToken = accessToken,
            body = JSONObject()) { JSONObject() }
    }

    private suspend fun <T> request(
        method: String,
        path: String,
        accessToken: String? = null,
        body: JSONObject? = null,
        parse: (JSONObject) -> T
    ): T = withContext(Dispatchers.IO) {
        require(projectUrl.startsWith("https://")) {
            "نشانی Supabase باید HTTPS باشد."
        }
        require(publishableKey.isNotBlank()) {
            "کلید Publishable مربوط به Supabase در تنظیمات Build قرار نگرفته است."
        }
        val connection = (URL(projectUrl.trimEnd('/') + path).openConnection() as HttpURLConnection)
        try {
            connection.requestMethod = method
            connection.connectTimeout = 15_000
            connection.readTimeout = 20_000
            connection.setRequestProperty("apikey", publishableKey)
            connection.setRequestProperty("Accept", "application/json")
            if (!accessToken.isNullOrBlank()) {
                connection.setRequestProperty("Authorization", "Bearer $accessToken")
            }
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json")
                connection.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            }
            val status = connection.responseCode
            val stream = if (status in 200..299) connection.inputStream else connection.errorStream
            val response = stream?.bufferedReader()?.use { it.readText() }.orEmpty()
            if (status !in 200..299) {
                val message = runCatching {
                    val error = JSONObject(response)
                    error.optString("msg").ifBlank {
                        error.optString("message").ifBlank {
                            error.optString("error_description").ifBlank { error.optString("error") }
                        }
                    }
                }.getOrNull().orEmpty()
                throw IllegalStateException(message.ifBlank { "Supabase Auth error: HTTP $status" })
            }
            parse(if (response.isBlank()) JSONObject() else JSONObject(response))
        } finally {
            connection.disconnect()
        }
    }

    private fun parseSessionOrNull(json: JSONObject): SupabaseAuthSession? {
        val access = json.optString("access_token").takeIf { it.isNotBlank() } ?: return null
        val refresh = json.optString("refresh_token").takeIf { it.isNotBlank() } ?: return null
        val user = json.optJSONObject("user") ?: return null
        val userId = user.optString("id").takeIf { it.isNotBlank() } ?: return null
        return SupabaseAuthSession(
            accessToken = access,
            refreshToken = refresh,
            userId = userId,
            email = user.optString("email").takeIf { it.isNotBlank() },
            expiresIn = json.optLong("expires_in", 0L),
            tokenType = json.optString("token_type", "bearer")
        )
    }
}
