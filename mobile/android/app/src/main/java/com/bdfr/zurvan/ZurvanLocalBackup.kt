package com.bdfr.zurvan

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.time.Instant

/**
 * Versioned, user-controlled local JSON backup. Never exports licensing keys, device
 * identifiers or access tokens. Linked media entries contain references only, not bytes.
 * Import fully validates the document before writing any preferences.
 */
object ZurvanLocalBackup {
    private const val PREFS = "zurvan_android_local"
    private const val VERSION = 1
    private const val MAX_ITEMS = 5000
    private val collections = listOf("plans", "occasions", "reminders", "notes", "media_tags")
    private val keyFields = mapOf(
        "plans" to "id",
        "occasions" to "id",
        "reminders" to "id",
        "notes" to "dateKey",
        "media_tags" to "id"
    )
    private val requiredFields = mapOf(
        "plans" to listOf("id", "title", "dateKey", "hour", "minute", "kind"),
        "occasions" to listOf("id", "title", "calendar", "month", "day"),
        "reminders" to listOf("id", "title", "dateKey", "hour", "minute"),
        "notes" to listOf("dateKey", "text"),
        "media_tags" to listOf("id", "uri", "dateKey")
    )
    private val dateFormat = Regex("""^\d{4}-\d{2}-\d{2}$""")

    fun export(context: Context): String {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val data = JSONObject()
        for (key in collections) {
            data.put(key, JSONArray(prefs.getString(key, "[]") ?: "[]"))
        }
        val settings = JSONObject()
            .put("calendar_order", prefs.getString("calendar_order", "persian,hijri,gregorian"))
            .put("corner_radius", prefs.getFloat("corner_radius", 18f).toDouble())
            .put("rail_side", prefs.getString("rail_side", "left"))
            .put("day_media_enabled", prefs.getBoolean("day_media_enabled", false))
        data.put("ui", settings)
        return JSONObject()
            .put("format", "zurvan-android-backup")
            .put("version", VERSION)
            .put("createdAtUtc", Instant.now().toString())
            .put("mediaFilesIncluded", false)
            .put("data", data)
            .toString(2)
    }

    /** Returns the total number of records after restoring. Existing records win in merge mode. */
    fun import(context: Context, raw: String, merge: Boolean): Int {
        require(raw.toByteArray(Charsets.UTF_8).size <= 2_000_000) { "حجم فایل پشتیبان بیش از حد مجاز است." }
        val root = JSONObject(raw)
        require(root.optString("format") == "zurvan-android-backup" &&
            root.optInt("version") == VERSION) { "ساختار یا نسخه پشتیبان پشتیبانی نمی‌شود." }
        val data = root.getJSONObject("data")
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val staged = mutableMapOf<String, String>()
        var total = 0
        for (key in collections) {
            val incoming = data.getJSONArray(key)
            validate(key, incoming)
            val result = if (merge) {
                val existing = JSONArray(prefs.getString(key, "[]") ?: "[]")
                validate(key, existing)
                mergeItems(key, existing, incoming)
            } else incoming
            require(result.length() <= MAX_ITEMS) { "تعداد رکوردها از حد مجاز بیشتر است." }
            staged[key] = result.toString()
            total += result.length()
        }
        val ui = data.getJSONObject("ui")
        val order = ui.getString("calendar_order")
        val parts = order.split(",")
        require(parts.size == 3 &&
            parts.toSet() == setOf("persian", "hijri", "gregorian")) { "ترتیب تقویم‌ها در فایل معتبر نیست." }
        val radius = ui.getDouble("corner_radius")
        require(radius.isFinite() && radius in 0.0..32.0) { "اندازه گوشه‌ها نامعتبر است." }
        val rail = ui.getString("rail_side")
        require(rail == "left" || rail == "right") { "سمت نوار پیمایش نامعتبر است." }
        val mediaEnabled = ui.getBoolean("day_media_enabled")

        // The complete document is verified before a single atomic SharedPreferences commit.
        val editor = prefs.edit()
        staged.forEach { (key, value) -> editor.putString(key, value) }
        if (!merge) {
            editor.putString("calendar_order", order)
                .putFloat("corner_radius", radius.toFloat())
                .putString("rail_side", rail)
                .putBoolean("day_media_enabled", mediaEnabled)
        }
        check(editor.commit()) { "ذخیره پشتیبان ناموفق بود. داده‌های قبلی را بررسی کنید." }
        return total
    }

    private fun mergeItems(key: String, existing: JSONArray, incoming: JSONArray): JSONArray {
        val identity = keyFields.getValue(key)
        val seen = mutableSetOf<String>()
        val result = JSONArray()
        for (array in listOf(existing, incoming)) {
            for (index in 0 until array.length()) {
                val item = array.getJSONObject(index)
                if (seen.add(item.getString(identity))) result.put(item)
            }
        }
        return result
    }

    private fun validate(key: String, array: JSONArray) {
        require(array.length() <= MAX_ITEMS) { "تعداد $key بیش از حد مجاز است." }
        val identities = mutableSetOf<String>()
        for (index in 0 until array.length()) {
            val item = array.getJSONObject(index)
            for (field in requiredFields.getValue(key)) {
                require(item.has(field) && !item.isNull(field)) { "رکورد $key ناقص است: $field" }
            }
            val id = item.getString(keyFields.getValue(key))
            require(id.isNotBlank() && id.length <= 240 && identities.add(id)) { "شناسه تکراری یا نامعتبر در $key" }
            if (key == "plans" || key == "reminders" || key == "notes") {
                require(dateFormat.matches(item.getString("dateKey"))) { "تاریخ در $key نامعتبر است." }
            }
            if (key == "media_tags") {
                require(item.getString("uri").length in 1..2048) { "پیوند فایل نامعتبر است." }
            }
            if (key == "occasions") {
                require(item.getString("calendar") in setOf("persian", "hijri", "gregorian"))
                require(item.getInt("month") in 1..12 && item.getInt("day") in 1..31)
            }
            if (key == "plans" || key == "reminders") {
                require(item.getInt("hour") in 0..23 && item.getInt("minute") in 0..59)
            }
        }
    }
}
