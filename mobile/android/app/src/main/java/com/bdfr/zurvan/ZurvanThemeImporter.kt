package com.bdfr.zurvan

import android.content.Context
import android.net.Uri
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.util.zip.ZipInputStream

/** Imports Theme Studio JSON/ZIP with explicit limits and no arbitrary ZIP extraction. */
object ZurvanThemeImporter {
    private val colorKeys = setOf("background","surface","card","ink","accent","holiday") +
        (0..6).map { "weekday$it" }
    data class Imported(val name: String, val baseTheme: String, val colors: Map<String,String>, val wallpaper: String?)

    fun import(context: Context, uri: Uri): Imported {
        val maxArchive = 16L * 1024 * 1024
        val bytes = context.contentResolver.openInputStream(uri)?.use { stream ->
            stream.readBytes().also { require(it.size <= maxArchive) { "بسته بیش از حد بزرگ است." } }
        } ?: error("فایل قابل خواندن نیست.")
        val isZip = bytes.size > 4 && bytes[0] == 0x50.toByte() && bytes[1] == 0x4B.toByte()
        var rawJson: ByteArray? = null
        var rawImage: ByteArray? = null
        var imageName: String? = null
        if (isZip) {
            ZipInputStream(bytes.inputStream()).use { zip ->
                var count = 0
                while (true) {
                    val entry = zip.nextEntry ?: break
                    require(++count <= 8) { "تعداد فایل‌های بسته بیش از حد مجاز است." }
                    require(!entry.name.contains('/') && !entry.name.contains('\\')) { "مسیر بسته نامعتبر است." }
                    val content = zip.readBytes()
                    require(content.size <= 12_000_000) { "حجم فایل درون بسته بیش از حد است." }
                    if (entry.name == "theme.json") rawJson = content
                    else if (entry.name.matches(Regex("wallpaper\\.(jpg|jpeg|png|webp)", RegexOption.IGNORE_CASE))) {
                        rawImage = content
                        imageName = entry.name
                    }
                    zip.closeEntry()
                }
            }
        } else rawJson = bytes
        val raw = rawJson ?: error("فایل theme.json یافت نشد.")
        require(raw.size < 128_000) { "فایل تنظیمات تم بیش از حد بزرگ است." }
        val json = JSONObject(String(raw, Charsets.UTF_8))
        require(json.optInt("schemaVersion") == 1 && json.optString("format") == "zurvan-android-theme") { "فرمت تم پشتیبانی نمی‌شود." }
        val baseTheme = json.optString("themeId")
        require(ZurvanThemes.presets.any { it.id == baseTheme }) { "تم پایه ناشناخته است." }
        val name = json.optString("name").take(80)
        require(name.isNotBlank()) { "نام تم خالی است." }
        val obj = json.getJSONObject("colors")
        val colors = mutableMapOf<String,String>()
        for (key in colorKeys) {
            if (obj.has(key)) {
                val hex = obj.getString(key)
                require(hex.matches(Regex("^#[0-9a-fA-F]{6}$"))) { "رنگ نامعتبر: $key" }
                colors[key] = hex.uppercase()
            }
        }
        val described = json.optJSONObject("wallpaper")?.optString("file")
        if (described != null) require(isZip && described == imageName && rawImage != null) { "تصویر اعلام‌شده در بسته موجود نیست." }
        val imageBytes = rawImage
        val wallpaperPath = if (described != null && imageBytes != null) {
            val directory = File(context.filesDir, "zurvan_themes").apply { mkdirs() }
            val ext = described.substringAfterLast('.').lowercase()
            val file = File(directory, "imported_wallpaper.$ext")
            FileOutputStream(file).use { it.write(imageBytes) }
            file.absolutePath
        } else null
        val prefs = context.getSharedPreferences("zurvan_appearance", Context.MODE_PRIVATE)
        prefs.edit().apply {
            putString("theme", baseTheme)
            colorKeys.forEach { key ->
                val value = colors[key]
                if (key.startsWith("weekday")) {
                    val index = key.removePrefix("weekday")
                    if (value != null) putString("weekday.$baseTheme.$index", value)
                } else if (value != null) putString("color.$baseTheme.$key", value)
            }
            putString("installed_theme_name", name)
            if (wallpaperPath == null) remove("wallpaper") else putString("wallpaper", wallpaperPath)
        }.apply()
        return Imported(name, baseTheme, colors, wallpaperPath)
    }
}
