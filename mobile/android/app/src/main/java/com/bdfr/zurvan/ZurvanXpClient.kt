package com.bdfr.zurvan

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import java.net.HttpURLConnection
import java.net.URL
import org.json.JSONObject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Only the public publishable key is shipped; rewards are authorised by Supabase JWT. */
internal class ZurvanXpClient(context: Context) {
    private val storageContext = context.applicationContext
    private val storage = ZurvanCloudSecureStore(storageContext)
    private val base = "https://yztithydgdvydaktuxlw.supabase.co"
    val signedInEmail: String? get() = storage.get("xp_account_email")

    private fun call(path: String, body: JSONObject, token: String? = null): JSONObject {
        val connection = URL(base + path).openConnection() as HttpURLConnection
        return try {
            connection.requestMethod = "POST"
            connection.connectTimeout = 12000
            connection.readTimeout = 12000
            connection.doOutput = true
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("apikey", BuildConfig.DESK_SUPABASE_PUBLISHABLE_KEY)
            if (token != null) connection.setRequestProperty("Authorization", "Bearer $token")
            connection.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            val stream = if (connection.responseCode in 200..299) connection.inputStream else connection.errorStream
            val response = stream?.bufferedReader()?.use { it.readText() }.orEmpty()
            if (connection.responseCode !in 200..299) {
                throw IllegalStateException("درخواست حساب زروان ناموفق بود (HTTP ${connection.responseCode}).")
            }
            JSONObject(response)
        } finally {
            connection.disconnect()
        }
    }

    private fun storeSession(email: String, data: JSONObject) {
        storage.put("xp_access_token", data.getString("access_token"))
        storage.put("xp_refresh_token", data.getString("refresh_token"))
        storage.put("xp_account_email", email)
        storage.put("xp_expiry_seconds", ((System.currentTimeMillis() / 1000) +
            data.optLong("expires_in", 3600)).toString())
    }

    private fun accessToken(): String? {
        val token = storage.get("xp_access_token") ?: return null
        val expiration = storage.get("xp_expiry_seconds")?.toLongOrNull() ?: 0L
        if (System.currentTimeMillis()/1000 < expiration - 90) return token
        val refresh = storage.get("xp_refresh_token") ?: return null
        val result = call("/auth/v1/token?grant_type=refresh_token", JSONObject()
            .put("refresh_token", refresh))
        storeSession(storage.get("xp_account_email").orEmpty(), result)
        return result.getString("access_token")
    }

    suspend fun signIn(email: String, password: String): JSONObject = withContext(Dispatchers.IO) {
        val response = call("/auth/v1/token?grant_type=password",
            JSONObject().put("email", email.trim()).put("password", password))
        storeSession(email.trim(), response)
        val claim = runCatching { claimOpen() }.getOrNull()
        runCatching { refreshEntitlement() }.onFailure {
            ZurvanAccountEntitlement.canWrite = false
            ZurvanAccountEntitlement.mode = "unavailable"
        }
        runCatching { ZurvanAchievementTracker.flush(storageContext) }
        claim ?: JSONObject()
    }

    suspend fun signUp(email: String, password: String): Boolean = withContext(Dispatchers.IO) {
        val result = call("/auth/v1/signup", JSONObject()
            .put("email", email.trim()).put("password", password))
        if (result.has("access_token") && !result.isNull("access_token")) {
            storeSession(email.trim(), result)
            runCatching { claimOpen() }
            runCatching { refreshEntitlement() }.onFailure {
                ZurvanAccountEntitlement.canWrite = false
                ZurvanAccountEntitlement.mode = "unavailable"
            }
            runCatching { ZurvanAchievementTracker.flush(storageContext) }
            true
        } else false // Confirm email before signing in.
    }

    suspend fun claimOpen(): JSONObject? = withContext(Dispatchers.IO) {
        val token = accessToken() ?: return@withContext null
        call("/functions/v1/zurvan-user-progress", JSONObject().put("action", "claim_open"), token)
    }

    /** Signed-in and verified server entitlements are authoritative. */
    suspend fun licenseStatus(): JSONObject? = withContext(Dispatchers.IO) {
        val token = accessToken() ?: return@withContext null
        call("/functions/v1/zurvan-license-status", JSONObject(), token)
    }

    suspend fun refreshEntitlement(): Boolean = withContext(Dispatchers.IO) {
        val response = licenseStatus()
        val granted = response?.optBoolean("can_write", false) == true
        ZurvanAccountEntitlement.canWrite = granted
        ZurvanAccountEntitlement.mode = response?.optString("mode") ?: "read_only"
        granted
    }

    suspend fun reportAchievement(kind: String, section: String? = null,
                                  year: Int? = null, month: Int? = null): JSONObject? =
        withContext(Dispatchers.IO) {
            val token = accessToken() ?: return@withContext null
            val payload = JSONObject().put("action", "record").put("kind", kind)
            if (section != null) payload.put("section", section)
            if (year != null) payload.put("year", year)
            if (month != null) payload.put("month", month)
            call("/functions/v1/zurvan-achievements", payload, token)
        }

    suspend fun achievementStatus(): JSONObject? = withContext(Dispatchers.IO) {
        val token = accessToken() ?: return@withContext null
        call("/functions/v1/zurvan-achievements",
            JSONObject().put("action", "status"), token)
    }

    suspend fun progress(): JSONObject? = withContext(Dispatchers.IO) {
        val token = accessToken() ?: return@withContext null
        call("/functions/v1/zurvan-user-progress", JSONObject().put("action", "status"), token)
    }

    fun signOut() {
        // Keep account-scoped pending achievements for later reconnection.
        ZurvanAccountEntitlement.canWrite = false
        ZurvanAccountEntitlement.mode = "signed_out"
        listOf("xp_access_token", "xp_refresh_token", "xp_account_email", "xp_expiry_seconds")
            .forEach(storage::remove)
    }
}

internal object ZurvanAccountEntitlement {
    var canWrite by androidx.compose.runtime.mutableStateOf(false)
    var mode by androidx.compose.runtime.mutableStateOf("unknown")
}
