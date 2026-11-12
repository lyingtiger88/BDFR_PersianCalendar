package com.bdfr.zurvan

import android.content.Context
import androidx.compose.runtime.*
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID
import java.time.Instant
import java.io.File

// Independently developed Zurvan Kanban data model; no PLANKA code is embedded.
internal data class ZBoard(val id: String, val name: String)
internal data class ZLane(val id: String, val board: String, val name: String, val order: Int)
internal data class ZTask(val text: String, val done: Boolean)
internal data class ZAttachment(val id: String, val name: String, val path: String, val bytes: Long)
internal data class ZActivity(val at: String, val action: String, val detail: String)
internal data class ZCard(
    val id: String, val lane: String, val title: String, val description: String = "",
    val label: String = "", val due: String = "", val tasks: List<ZTask> = emptyList(),
    val comments: List<String> = emptyList(), val archived: Boolean = false, val order: Int = 0,
    val labels: List<String> = emptyList(), val priority: String = "normal",
    val attachments: List<ZAttachment> = emptyList(), val activity: List<ZActivity> = emptyList(),
    val customFields: Map<String, String> = emptyMap(), val cardType: String = "task",
    val start: String = "", val coverAttachmentId: String = "", val cardColor: String = "",
    val completedAt: String = ""
)

internal class ZurvanKanbanStore(context: Context) {
    private val prefs = context.getSharedPreferences("zurvan_personal_kanban_v1", Context.MODE_PRIVATE)
    private val attachmentDirectory = File(context.filesDir, "zurvan_kanban_files")
    var boards by mutableStateOf<List<ZBoard>>(emptyList())
        private set
    var lanes by mutableStateOf<List<ZLane>>(emptyList())
        private set
    var cards by mutableStateOf<List<ZCard>>(emptyList())
        private set
    var loadWarning by mutableStateOf("")
        private set
    private val validPriority = setOf("low", "normal", "high", "urgent")
    init { load() }
    private fun normalizeName(value: String) = value.trim().take(200)
    private fun activity(old: ZCard, action: String, detail: String) = old.copy(
        activity = (old.activity + ZActivity(Instant.now().toString(), action, detail.take(400))).takeLast(250))
    private fun load() {
        val main = prefs.getString("data", "{}") ?: "{}"
        val fallback = prefs.getString("previous_good", null)
        runCatching {
            decode(main)
        }.onFailure { cause ->
            if (fallback != null && runCatching { decode(fallback) }.isSuccess) {
                if (!prefs.edit().putString("data", fallback).commit())
                    loadWarning = "بازیابی داده‌ها انجام شد، اما ذخیره نسخه بازیابی‌شده ناموفق بود."
            } else {
                loadWarning = "خواندن داده‌های کانبان ناموفق بود؛ داده‌های ذخیره‌شده تغییر نکرده‌اند: " +
                    (cause.message ?: "خطای ناشناخته")
            }
        }
    }

    private fun decode(payload: String) {
        val root = JSONObject(payload)
        val b = root.optJSONArray("boards") ?: JSONArray()
        val l = root.optJSONArray("lanes") ?: JSONArray()
        val c = root.optJSONArray("cards") ?: JSONArray()
        val newBoards = (0 until b.length()).map { b.getJSONObject(it).let { o ->
            ZBoard(o.getString("id"), o.getString("name"))
        } }
        val newLanes = (0 until l.length()).map { l.getJSONObject(it).let { o ->
            ZLane(o.getString("id"), o.getString("board"), o.getString("name"), o.optInt("order"))
        } }
        val newCards = (0 until c.length()).map { c.getJSONObject(it).let { o ->
            val tasks = o.optJSONArray("tasks") ?: JSONArray()
            val comments = o.optJSONArray("comments") ?: JSONArray()
            ZCard(o.getString("id"), o.getString("lane"), o.getString("title"),
                o.optString("description"), o.optString("label"), o.optString("due"),
                (0 until tasks.length()).map { j -> tasks.getJSONObject(j).let { t ->
                    ZTask(t.getString("text"), t.optBoolean("done"))
                } }, (0 until comments.length()).map { j -> comments.getString(j) },
                o.optBoolean("archived"), o.optInt("order"),
                (o.optJSONArray("labels") ?: JSONArray()).let { arr ->
                    (0 until arr.length()).map { j -> arr.getString(j) } },
                o.optString("priority", "normal"),
                (o.optJSONArray("attachments") ?: JSONArray()).let { arr ->
                    (0 until arr.length()).map { j -> arr.getJSONObject(j).let { x ->
                        ZAttachment(x.getString("id"), x.getString("name"), x.getString("path"), x.optLong("bytes"))
                    } } },
                (o.optJSONArray("activity") ?: JSONArray()).let { arr ->
                    (0 until arr.length()).map { j -> arr.getJSONObject(j).let { x ->
                        ZActivity(x.optString("at"), x.optString("action"), x.optString("detail"))
                    } } },
                o.optJSONObject("customFields")?.let { fields ->
                    fields.keys().asSequence().associateWith { key -> fields.optString(key) }
                } ?: emptyMap(),
                o.optString("cardType", "task"), o.optString("start"),
                o.optString("coverAttachmentId"), o.optString("cardColor"), o.optString("completedAt"))
        } }
        require(newBoards.map { it.id }.distinct().size == newBoards.size) { "شناسه پروژه تکراری است." }
        require(newLanes.map { it.id }.distinct().size == newLanes.size) { "شناسه ستون تکراری است." }
        require(newCards.map { it.id }.distinct().size == newCards.size) { "شناسه کارت تکراری است." }
        val boardIds = newBoards.map { it.id }.toSet()
        val laneIds = newLanes.map { it.id }.toSet()
        require(newLanes.all { it.board in boardIds }) { "ستون فاقد پروژه معتبر است." }
        require(newCards.all { it.lane in laneIds }) { "کارت فاقد ستون معتبر است." }
        require(newCards.all { it.priority in validPriority }) { "اولویت نامعتبر است." }
        require(newCards.all { it.cardType in setOf("task", "story", "project") }) { "نوع کارت معتبر نیست." }
        require(newCards.all { it.cardColor.isBlank() || it.cardColor.matches(Regex("#[0-9A-Fa-f]{8}")) }) { "رنگ کارت معتبر نیست." }
        boards = newBoards
        lanes = newLanes
        cards = newCards
    }

    /** Replace the private Kanban workspace with a validated export; existing data is backed up. */
    fun importMetadata(payload: String) {
        require(payload.length <= 8_000_000) { "حجم اطلاعات کانبان بیش از حد مجاز است." }
        val previousBoards = boards
        val previousLanes = lanes
        val previousCards = cards
        val previousWarning = loadWarning
        try {
            decode(payload)
            loadWarning = ""
            persist()
        } catch (cause: Exception) {
            boards = previousBoards
            lanes = previousLanes
            cards = previousCards
            loadWarning = previousWarning
            throw cause
        }
    }

    private fun cleanupAttachments(deleted: List<ZCard>) {
        val root = attachmentDirectory.canonicalFile
        deleted.flatMap { it.attachments }.forEach { attachment ->
            runCatching {
                val file = File(attachment.path).canonicalFile
                if (file.parentFile == root && file.isFile) file.delete()
            }
        }
    }

    private fun persist() {
        require(loadWarning.isBlank()) { "Fix or export damaged Kanban data before saving." }
        val b = JSONArray()
        boards.forEach { b.put(JSONObject().put("id", it.id).put("name", it.name)) }
        val l = JSONArray()
        lanes.forEach { l.put(JSONObject().put("id", it.id).put("board", it.board)
            .put("name", it.name).put("order", it.order)) }
        val c = JSONArray()
        cards.forEach { card ->
            val tasks = JSONArray()
            card.tasks.forEach { tasks.put(JSONObject().put("text", it.text).put("done", it.done)) }
            val comments = JSONArray()
            card.comments.forEach { comments.put(it) }
            c.put(JSONObject().put("id", card.id).put("lane", card.lane)
                .put("title", card.title).put("description", card.description)
                .put("label", card.label).put("due", card.due)
                .put("archived", card.archived).put("order", card.order)
                .put("tasks", tasks).put("comments", comments)
                .put("labels", JSONArray().apply { card.labels.forEach { put(it) } })
                .put("priority", card.priority)
                .put("attachments", JSONArray().apply { card.attachments.forEach {
                    put(JSONObject().put("id", it.id).put("name", it.name).put("path", it.path).put("bytes", it.bytes)) } })
                .put("activity", JSONArray().apply { card.activity.forEach {
                    put(JSONObject().put("at", it.at).put("action", it.action).put("detail", it.detail)) } })
                .put("customFields", JSONObject().apply { card.customFields.forEach { (key, value) -> put(key, value) } })
                .put("cardType", card.cardType).put("start", card.start)
                .put("coverAttachmentId", card.coverAttachmentId).put("cardColor", card.cardColor)
                .put("completedAt", card.completedAt))
        }
        val newData = JSONObject().put("boards", b).put("lanes", l).put("cards", c).toString()
        val oldData = prefs.getString("data", "{}") ?: "{}"
        val saved = prefs.edit().putString("previous_good", oldData)
            .putString("data", newData).commit()
        if (!saved) {
            decode(oldData)
            throw IllegalStateException("ذخیره کانبان ناموفق بود؛ اطلاعات قبلی بازیابی شدند.")
        }
    }
    /** Only metadata is exported here. Attachment file bytes are stored separately. */
    fun exportMetadata(): String = prefs.getString("data", "{}") ?: "{}"

    fun newBoard(title: String): String {
        require(title.isNotBlank()) { "نام پروژه نمی‌تواند خالی باشد." }
        val id = UUID.randomUUID().toString()
        boards = boards + ZBoard(id, normalizeName(title)); persist(); return id
    }
    fun renameBoard(id: String, title: String) {
        require(boards.any { it.id == id }) { "پروژه پیدا نشد." }
        require(title.isNotBlank()) { "نام پروژه نمی‌تواند خالی باشد." }
        boards = boards.map { if (it.id == id) it.copy(name = normalizeName(title)) else it }; persist()
    }
    fun removeBoard(id: String) {
        val ids = lanes.filter { it.board == id }.map { it.id }.toSet()
        boards = boards.filterNot { it.id == id }
        lanes = lanes.filterNot { it.board == id }
        val deleted = cards.filter { it.lane in ids }
        cards = cards.filterNot { it.lane in ids }
        persist()
        cleanupAttachments(deleted)
    }
    fun newLane(board: String, title: String) {
        require(boards.any { it.id == board }) { "پروژه پیدا نشد." }
        require(title.isNotBlank()) { "نام ستون نمی‌تواند خالی باشد." }
        lanes = lanes + ZLane(UUID.randomUUID().toString(), board, normalizeName(title),
            (lanes.filter { it.board == board }.maxOfOrNull { it.order } ?: -1) + 1)
        persist()
    }
    fun renameLane(id: String, title: String) {
        require(lanes.any { it.id == id }) { "ستون پیدا نشد." }
        require(title.isNotBlank()) { "نام ستون نمی‌تواند خالی باشد." }
        lanes = lanes.map { if (it.id == id) it.copy(name = normalizeName(title)) else it }; persist()
    }
    fun removeLane(id: String) {
        lanes = lanes.filterNot { it.id == id }
        val deleted = cards.filter { it.lane == id }
        cards = cards.filterNot { it.lane == id }; persist()
        cleanupAttachments(deleted)
    }
    fun newCard(lane: String, title: String) {
        require(lanes.any { it.id == lane }) { "ستون پیدا نشد." }
        require(title.isNotBlank()) { "عنوان کارت نمی‌تواند خالی باشد." }
        cards = cards + ZCard(UUID.randomUUID().toString(), lane, normalizeName(title),
            order = (cards.filter { it.lane == lane }.maxOfOrNull { it.order } ?: -1) + 1)
        persist()
    }
    fun updateCard(card: ZCard) {
        require(cards.any { it.id == card.id }) { "کارت پیدا نشد." }
        require(lanes.any { it.id == card.lane }) { "ستون مقصد پیدا نشد." }
        require(card.title.isNotBlank()) { "عنوان کارت نمی‌تواند خالی باشد." }
        require(card.priority in validPriority) { "Invalid priority" }
        require(card.cardType in setOf("task", "story", "project")) { "Invalid card type" }
        require(card.cardColor.isBlank() || card.cardColor.matches(Regex("#[0-9A-Fa-f]{8}"))) { "Invalid card color" }
        cards = cards.map { if (it.id == card.id) {
            val updated = card.copy(title = normalizeName(card.title),
                labels = card.labels.map { x -> x.trim() }.filter { x -> x.isNotBlank() }.distinct().take(12))
            activity(updated, "updated", "کارت ویرایش شد")
        } else it }; persist()
    }
    fun removeCard(id: String) {
        val deleted = cards.filter { it.id == id }
        cards = cards.filterNot { it.id == id }
        persist()
        cleanupAttachments(deleted)
    }
    fun moveCard(id: String, lane: String) {
        require(cards.any { it.id == id }) { "کارت پیدا نشد." }
        require(lanes.any { it.id == lane }) { "ستون مقصد پیدا نشد." }
        val last = (cards.filter { it.lane == lane }.maxOfOrNull { it.order } ?: -1) + 1
        val isDone = lanes.firstOrNull { it.id == lane }?.name?.let {
            it.contains("انجام‌شده") || it.contains("Done", ignoreCase = true)
        } == true
        cards = cards.map { if (it.id == id) activity(it.copy(lane = lane, order = last,
            completedAt = if (isDone) Instant.now().toString() else ""), "moved", "انتقال به ستون") else it }
        persist()
    }
    fun reorderLane(board: String, id: String, delta: Int) {
        val ordered = lanes.filter { it.board == board }.sortedBy { it.order }.toMutableList()
        val old = ordered.indexOfFirst { it.id == id }
        if (old < 0) return
        val target = (old + delta).coerceIn(0, ordered.lastIndex)
        if (old == target) return
        ordered.add(target, ordered.removeAt(old))
        val positions = ordered.mapIndexed { index, row -> row.id to index }.toMap()
        lanes = lanes.map { if (it.id in positions) it.copy(order = positions.getValue(it.id)) else it }
        persist()
    }
    fun addAttachment(cardId: String, attachment: ZAttachment) {
        require(cards.any { it.id == cardId }) { "کارت پیدا نشد." }
        require(attachment.bytes in 1..15_000_000) { "Attachment must be between 1 byte and 15MB" }
        cards = cards.map { if (it.id == cardId) activity(
            it.copy(attachments = it.attachments + attachment), "attachment", attachment.name) else it }
        persist()
    }
    fun removeAttachment(cardId: String, attachmentId: String): ZAttachment? {
        val attachment = cards.firstOrNull { it.id == cardId }?.attachments?.firstOrNull { it.id == attachmentId }
            ?: return null
        cards = cards.map { if (it.id == cardId) activity(it.copy(
            attachments = it.attachments.filterNot { row -> row.id == attachmentId }),
            "attachment_removed", attachment.name) else it }
        persist()
        cleanupAttachments(listOf(ZCard(id = cardId, lane = "", title = "",
            attachments = listOf(attachment))))
        return attachment
    }
    fun reorderCard(id: String, delta: Int) {
        val card = cards.firstOrNull { it.id == id } ?: return
        val ordered = cards.filter { it.lane == card.lane && !it.archived }
            .sortedBy { it.order }.toMutableList()
        val index = ordered.indexOfFirst { it.id == id }
        if (index < 0) return
        val target = (index + delta).coerceIn(0, ordered.lastIndex)
        ordered.add(target, ordered.removeAt(index))
        val ranks = ordered.mapIndexed { i, row -> row.id to i }.toMap()
        cards = cards.map { if (ranks.containsKey(it.id)) it.copy(order = ranks.getValue(it.id)) else it }
        persist()
    }
}
