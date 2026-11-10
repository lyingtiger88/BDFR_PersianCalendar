package com.bdfr.zurvan

import android.content.Context
import android.net.Uri
import org.json.JSONObject
import org.json.JSONArray
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.util.UUID
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

/**
 * Full private Kanban backup: validated metadata plus all file bytes.
 * No cloud access, no external storage permission, no PLANKA dependencies.
 */
internal object ZurvanKanbanBackup {
    private const val FORMAT = "zurvan-personal-kanban-v1"
    private const val SINGLE_FILE_LIMIT = 15_000_000L
    private const val MANIFEST_LIMIT = 8_000_000L
    private const val TOTAL_LIMIT = 250_000_000L
    private val allowedName = Regex("[A-Za-z0-9._-]{1,120}")

    internal class PreparedImport(val snapshot: String, private val copied: List<File>) {
        fun discardIfNotImported() {
            copied.forEach { runCatching { it.delete() } }
        }
    }

    fun export(context: Context, destination: Uri, snapshot: String) {
        require(snapshot.length < MANIFEST_LIMIT) { "حجم اطلاعات پروژه بیش از حد مجاز است." }
        val data = JSONObject(snapshot)
        val attachments = mutableMapOf<String, File>()
        val root = File(context.filesDir, "zurvan_kanban_files").canonicalFile
        val cards = data.optJSONArray("cards") ?: JSONArray()
        for (i in 0 until cards.length()) {
            val card = cards.getJSONObject(i)
            val files = card.optJSONArray("attachments") ?: JSONArray()
            for (j in 0 until files.length()) {
                val item = files.getJSONObject(j)
                val file = File(item.getString("path")).canonicalFile
                require(file.parentFile == root && file.isFile) { "فایل پیوست پیدا نشد: " + item.optString("name") }
                require(file.length() in 1..SINGLE_FILE_LIMIT) { "حجم فایل پیوست مجاز نیست." }
                require(allowedName.matches(file.name)) { "نام داخلی فایل معتبر نیست." }
                val entry = "attachments/" + file.name
                attachments[entry] = file
                item.put("path", entry)
            }
        }
        val manifest = JSONObject().put("format", FORMAT).put("data", data).toString()
        require(manifest.toByteArray(Charsets.UTF_8).size < MANIFEST_LIMIT) { "فایل پشتیبان بیش از حد بزرگ است." }
        require(attachments.size <= 500) { "تعداد پیوست‌ها بیش از حد مجاز است." }
        require(attachments.values.sumOf { it.length() } <= TOTAL_LIMIT) { "حجم کلی فایل‌ها بیش از حد مجاز است." }

        val stream = context.contentResolver.openOutputStream(destination, "w")
            ?: throw IllegalStateException("فایل خروجی قابل باز کردن نیست.")
        stream.use { output ->
            ZipOutputStream(output).use { zip ->
                zip.putNextEntry(ZipEntry("manifest.json"))
                zip.write(manifest.toByteArray(Charsets.UTF_8))
                zip.closeEntry()
                attachments.forEach { (entry, file) ->
                    zip.putNextEntry(ZipEntry(entry))
                    file.inputStream().use { input -> input.copyTo(zip) }
                    zip.closeEntry()
                }
            }
        }
    }

    fun prepareImport(context: Context, source: Uri): PreparedImport {
        val staging = File(context.cacheDir, "kanban-restore-" + UUID.randomUUID()).apply { mkdirs() }
        val copied = mutableListOf<File>()
        try {
            var manifest: String? = null
            var bytesRead = 0L
            val stagedFiles = mutableMapOf<String, File>()
            val stream = context.contentResolver.openInputStream(source)
                ?: throw IllegalStateException("فایل پشتیبان باز نشد.")
            stream.use { input ->
                ZipInputStream(input).use { zip ->
                    while (true) {
                        val entry = zip.nextEntry ?: break
                        require(!entry.isDirectory) { "ورودی پوشه در فایل پشتیبان مجاز نیست." }
                        val name = entry.name
                        require(name == "manifest.json" ||
                            (name.startsWith("attachments/") &&
                                allowedName.matches(name.removePrefix("attachments/")))) {
                            "مسیر غیرمجاز در فایل پشتیبان."
                        }
                        require(name != "manifest.json" || manifest == null) { "فایل اطلاعات تکراری است." }
                        require(name !in stagedFiles) { "فایل پیوست تکراری است." }
                        val maxBytes = if (name == "manifest.json") MANIFEST_LIMIT else SINGLE_FILE_LIMIT
                        var size = 0L
                        val target = File(staging, UUID.randomUUID().toString())
                        target.outputStream().use { output ->
                            val buffer = ByteArray(8192)
                            while (true) {
                                val read = zip.read(buffer)
                                if (read < 0) break
                                size += read
                                bytesRead += read
                                require(size <= maxBytes && bytesRead <= TOTAL_LIMIT) {
                                    "حجم فایل پشتیبان بیش از حد مجاز است."
                                }
                                output.write(buffer, 0, read)
                            }
                        }
                        if (name == "manifest.json") {
                            manifest = target.readText(Charsets.UTF_8)
                            target.delete()
                        } else {
                            stagedFiles[name] = target
                        }
                        zip.closeEntry()
                    }
                }
            }
            require(stagedFiles.size <= 500) { "تعداد پیوست‌ها بیش از حد مجاز است." }
            val root = JSONObject(manifest ?: throw IllegalArgumentException("فایل manifest.json وجود ندارد."))
            require(root.optString("format") == FORMAT) { "نسخه فایل پشتیبان پشتیبانی نمی‌شود." }
            val data = root.getJSONObject("data")
            val cards = data.optJSONArray("cards") ?: JSONArray()
            val referenced = mutableSetOf<String>()
            val targetDir = File(context.filesDir, "zurvan_kanban_files").apply { mkdirs() }
            for (i in 0 until cards.length()) {
                val items = cards.getJSONObject(i).optJSONArray("attachments") ?: JSONArray()
                for (j in 0 until items.length()) {
                    val item = items.getJSONObject(j)
                    val path = item.getString("path")
                    require(path in stagedFiles) { "فایل پیوست داخل نسخه پشتیبان موجود نیست." }
                    referenced += path
                }
            }
            require(referenced == stagedFiles.keys) { "پیوست‌های بدون ارجاع در فایل پشتیبان وجود دارند." }
            val copiedPaths = mutableMapOf<String, String>()
            for (name in referenced) {
                val file = stagedFiles.getValue(name)
                val ext = name.substringAfterLast('.', "").take(10).takeIf {
                    it.isNotBlank() && it.all { char -> char.isLetterOrDigit() }
                } ?: ""
                val dest = File(targetDir, UUID.randomUUID().toString() +
                    (if (ext.isEmpty()) "" else "." + ext))
                file.copyTo(dest)
                copied += dest
                copiedPaths[name] = dest.absolutePath
            }
            for (i in 0 until cards.length()) {
                val items = cards.getJSONObject(i).optJSONArray("attachments") ?: JSONArray()
                for (j in 0 until items.length())
                    items.getJSONObject(j).put("path", copiedPaths.getValue(items.getJSONObject(j).getString("path")))
            }
            return PreparedImport(data.toString(), copied)
        } catch (cause: Exception) {
            copied.forEach { runCatching { it.delete() } }
            throw cause
        } finally {
            staging.deleteRecursively()
        }
    }
}
