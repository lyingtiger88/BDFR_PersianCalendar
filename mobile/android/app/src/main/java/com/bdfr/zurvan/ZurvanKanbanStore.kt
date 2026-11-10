package com.bdfr.zurvan

import android.content.Context
import androidx.compose.runtime.*
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID
import java.time.Instant

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
    val attachments: List<ZAttachment> = emptyList(), val activity: List<ZActivity> = emptyList()
)

internal class ZurvanKanbanStore(context: Context) {
    private val prefs = context.getSharedPreferences("zurvan_personal_kanban_v1", Context.MODE_PRIVATE)
    var boards by mutableStateOf<List<ZBoard>>(emptyList())
        private set
    var lanes by mutableStateOf<List<ZLane>>(emptyList())
        private set
    var cards by mutableStateOf<List<ZCard>>(emptyList())
        private set
    var loadWarning by mutableStateOf("")
        private set
    init { load() }
    private val validPriority = setOf("low", "normal", "high", "urgent")
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
                loadWarning = "داده‌های کانبان از نسخه پشتیبان بازیابی شدند."
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
        boards = (0 until b.length()).map { b.getJSONObject(it).let { o -> ZBoard(o.getString("id"), o.getString("name")) } }
        lanes = (0 until l.length()).map { l.getJSONObject(it).let { o ->
            ZLane(o.getString("id"), o.getString("board"), o.getString("name"), o.optInt("order"))
        } }
        cards = (0 until c.length()).map { c.getJSONObject(it).let { o ->
            val tasks = o.optJSONArray("tasks") ?: JSONArray()
            val comments = o.optJSONArray("comments") ?: JSONArray()
            ZCard(o.getString("id"), o.getString("lane"), o.getString("title"),
                o.optString("description"), o.optString("label"), o.optString("due"),
                (0 until tasks.length()).map { j -> tasks.getJSONObject(j).let { t ->
                    ZTask(t.getString("text"), t.optBoolean("done"))
                } }, (0 until comments.length()).map { j -> comments.getString(j) },
                o.optBoolean("archived"), o.optInt("order"),
                (o.optJSONArray("labels") ?: JSONArray()).let { arr -> (0 until arr.length()).map { j -> arr.getString(j) } },
                o.optString("priority", "normal"),
                (o.optJSONArray("attachments") ?: JSONArray()).let { arr ->
                    (0 until arr.length()).map { j -> arr.getJSONObject(j).let { x ->
                        ZAttachment(x.getString("id"), x.getString("name"), x.getString("path"), x.optLong("bytes")) } } },
                (o.optJSONArray("activity") ?: JSONArray()).let { arr ->
                    (0 until arr.length()).map { j -> arr.getJSONObject(j).let { x ->
                        ZActivity(x.optString("at"), x.optString("action"), x.optString("detail")) } } })
        } }
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
                    put(JSONObject().put("at", it.at).put("action", it.action).put("detail", it.detail)) } }))
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
        val id = UUID.randomUUID().toString()
        boards = boards + ZBoard(id, normalizeName(title)); persist(); return id
    }
    fun renameBoard(id: String, title: String) {
        boards = boards.map { if (it.id == id) it.copy(name = normalizeName(title)) else it }; persist()
    }
    fun removeBoard(id: String) {
        val ids = lanes.filter { it.board == id }.map { it.id }.toSet()
        boards = boards.filterNot { it.id == id }
        lanes = lanes.filterNot { it.board == id }
        cards = cards.filterNot { it.lane in ids }
        persist()
    }
    fun newLane(board: String, title: String) {
        lanes = lanes + ZLane(UUID.randomUUID().toString(), board, normalizeName(title),
            (lanes.filter { it.board == board }.maxOfOrNull { it.order } ?: -1) + 1)
        persist()
    }
    fun renameLane(id: String, title: String) {
        lanes = lanes.map { if (it.id == id) it.copy(name = title.trim()) else it }; persist()
    }
    fun removeLane(id: String) {
        lanes = lanes.filterNot { it.id == id }
        cards = cards.filterNot { it.lane == id }; persist()
    }
    fun newCard(lane: String, title: String) {
        cards = cards + ZCard(UUID.randomUUID().toString(), lane, normalizeName(title),
            order = (cards.filter { it.lane == lane }.maxOfOrNull { it.order } ?: -1) + 1)
        persist()
    }
    fun updateCard(card: ZCard) {
        require(card.priority in validPriority) { "Invalid priority" }
        cards = cards.map { if (it.id == card.id) {
            val updated = card.copy(title = normalizeName(card.title),
                labels = card.labels.map { x -> x.trim() }.filter { x -> x.isNotBlank() }.distinct().take(12))
            activity(updated, "updated", "کارت ویرایش شد")
        } else it }; persist()
    }
    fun removeCard(id: String) { cards = cards.filterNot { it.id == id }; persist() }
    fun moveCard(id: String, lane: String) {
        val last = (cards.filter { it.lane == lane }.maxOfOrNull { it.order } ?: -1) + 1
        cards = cards.map { if (it.id == id) activity(it.copy(lane = lane, order = last), "moved", "انتقال به ستون") else it }
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
