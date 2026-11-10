package com.bdfr.zurvan

import android.content.Context
import androidx.compose.runtime.*
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

// Independently developed Zurvan Kanban data model; no PLANKA code is embedded.
internal data class ZBoard(val id: String, val name: String)
internal data class ZLane(val id: String, val board: String, val name: String, val order: Int)
internal data class ZTask(val text: String, val done: Boolean)
internal data class ZCard(
    val id: String, val lane: String, val title: String, val description: String = "",
    val label: String = "", val due: String = "", val tasks: List<ZTask> = emptyList(),
    val comments: List<String> = emptyList(), val archived: Boolean = false, val order: Int = 0
)

internal class ZurvanKanbanStore(context: Context) {
    private val prefs = context.getSharedPreferences("zurvan_personal_kanban_v1", Context.MODE_PRIVATE)
    var boards by mutableStateOf<List<ZBoard>>(emptyList()); private set
    var lanes by mutableStateOf<List<ZLane>>(emptyList()); private set
    var cards by mutableStateOf<List<ZCard>>(emptyList()); private set
    init { load() }
    private fun load() = runCatching {
        val root = JSONObject(prefs.getString("data", "{}") ?: "{}")
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
                o.optBoolean("archived"), o.optInt("order"))
        } }
    }
    private fun persist() {
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
                .put("tasks", tasks).put("comments", comments))
        }
        check(prefs.edit().putString("data",
            JSONObject().put("boards", b).put("lanes", l).put("cards", c).toString()).commit())
    }
    fun newBoard(title: String): String {
        val id = UUID.randomUUID().toString()
        boards = boards + ZBoard(id, title.trim()); persist(); return id
    }
    fun renameBoard(id: String, title: String) {
        boards = boards.map { if (it.id == id) it.copy(name = title.trim()) else it }; persist()
    }
    fun removeBoard(id: String) {
        val ids = lanes.filter { it.board == id }.map { it.id }.toSet()
        boards = boards.filterNot { it.id == id }
        lanes = lanes.filterNot { it.board == id }
        cards = cards.filterNot { it.lane in ids }
        persist()
    }
    fun newLane(board: String, title: String) {
        lanes = lanes + ZLane(UUID.randomUUID().toString(), board, title.trim(),
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
        cards = cards + ZCard(UUID.randomUUID().toString(), lane, title.trim(),
            order = (cards.filter { it.lane == lane }.maxOfOrNull { it.order } ?: -1) + 1)
        persist()
    }
    fun updateCard(card: ZCard) {
        cards = cards.map { if (it.id == card.id) card else it }; persist()
    }
    fun removeCard(id: String) { cards = cards.filterNot { it.id == id }; persist() }
    fun moveCard(id: String, lane: String) {
        val last = (cards.filter { it.lane == lane }.maxOfOrNull { it.order } ?: -1) + 1
        cards = cards.map { if (it.id == id) it.copy(lane = lane, order = last) else it }
        persist()
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
