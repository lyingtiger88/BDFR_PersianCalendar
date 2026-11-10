package com.bdfr.zurvan

import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.offset
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.*
import androidx.compose.ui.graphics.Color
import kotlin.math.roundToInt
import androidx.compose.ui.unit.IntOffset

/** Independent PLANKA-like board UI; user-created projects start completely empty. */
@Composable
fun ZurvanPersonalKanban(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val store = remember(context) { ZurvanKanbanStore(context) }
    var selectedId by remember { mutableStateOf<String?>(null) }
    var modelPicker by remember { mutableStateOf<String?>(null) }
    var createBoard by remember { mutableStateOf(false) }
    var newName by remember { mutableStateOf("") }
    Column(modifier.fillMaxSize().padding(12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        val selected = store.boards.firstOrNull { it.id == selectedId }
        if (selected != null) {
            PersonalKanbanBoard(store, selected, onBack = { selectedId = null })
        } else {
            Text("برنامه‌ها", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            KanbanSection("برنامه انفرادی و شخصی", "خصوصی؛ فقط برای خودتان",
                onAdd = { modelPicker = "personal" }) {
                store.boards.forEach { board ->
                    OutlinedCard(onClick = { selectedId = board.id },
                        modifier = Modifier.fillMaxWidth().padding(vertical = 3.dp)) {
                        Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
                            Icon(Icons.Outlined.Dashboard, null)
                            Spacer(Modifier.width(12.dp))
                            Column(Modifier.weight(1f)) {
                                Text(board.name, fontWeight = FontWeight.SemiBold)
                                Text("کانبان · " + store.lanes.count { it.board == board.id } + " ستون",
                                    style = MaterialTheme.typography.bodySmall)
                            }
                            Icon(Icons.Outlined.ChevronLeft, "ورود")
                        }
                    }
                }
            }
            KanbanSection("برنامه مشارکتی و گروهی", "تیمی و مشترک",
                onAdd = { modelPicker = "team" }) {}
        }
    }
    if (modelPicker != null) {
        AlertDialog(onDismissRequest = { modelPicker = null },
            title = { Text("نوع برنامه را انتخاب کنید") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                    FilledTonalButton(onClick = {
                        if (modelPicker == "personal") {
                            modelPicker = null; createBoard = true; newName = ""
                        }
                    }, enabled = modelPicker == "personal", modifier = Modifier.fillMaxWidth()) {
                        Icon(Icons.Outlined.ViewKanban, null)
                        Spacer(Modifier.width(6.dp))
                        Text("۱. کانبان")
                    }
                    Text("۲. مدل دوم — در انتظار طراحی", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text("۳. مدل سوم — در انتظار طراحی", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    if (modelPicker == "team")
                        Text("سیستم مشارکتی در مرحله بعد طراحی می‌شود.",
                            style = MaterialTheme.typography.bodySmall)
                }
            },
            confirmButton = { TextButton(onClick = { modelPicker = null }) { Text("بستن") } })
    }
    if (createBoard) {
        KanbanNameDialog("ایجاد پروژه کانبان", "نام پروژه", newName,
            onValueChange = { newName = it }, onDismiss = { createBoard = false },
            onSave = {
                selectedId = store.newBoard(newName)
                createBoard = false
            })
    }
}

@Composable
private fun KanbanSection(title: String, description: String, onAdd: () -> Unit,
                          content: @Composable ColumnScope.() -> Unit) {
    OutlinedCard(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(title, fontWeight = FontWeight.Bold)
                    Text(description, style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Button(onClick = onAdd) {
                    Icon(Icons.Outlined.Add, null)
                    Text("افزودن")
                }
            }
            content()
        }
    }
}

@Composable
private fun PersonalKanbanBoard(store: ZurvanKanbanStore, board: ZBoard, onBack: () -> Unit) {
    var addLane by remember { mutableStateOf(false) }
    var laneName by remember { mutableStateOf("") }
    var rename by remember { mutableStateOf(false) }
    var boardName by remember(board.id) { mutableStateOf(board.name) }
    var delete by remember { mutableStateOf(false) }
    var options by remember { mutableStateOf(false) }
    var query by remember { mutableStateOf("") }
    var showArchive by remember { mutableStateOf(false) }
    val lanes = store.lanes.filter { it.board == board.id }.sortedBy { it.order }
    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = onBack) { Icon(Icons.Outlined.ArrowBack, "بازگشت") }
            Text(board.name, Modifier.weight(1f), style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Box {
                IconButton(onClick = { options = true }) {
                    Icon(Icons.Outlined.MoreVert, "گزینه‌ها")
                }
                DropdownMenu(expanded = options, onDismissRequest = { options = false }) {
                    DropdownMenuItem(text = { Text("ویرایش نام پروژه") },
                        onClick = { options = false; rename = true })
                    DropdownMenuItem(text = { Text(if (showArchive) "نمایش کارت‌های جاری" else "بایگانی") },
                        onClick = { options = false; showArchive = !showArchive })
                    DropdownMenuItem(text = { Text("حذف پروژه") },
                        onClick = { options = false; delete = true })
                }
            }
        }
        OutlinedTextField(value = query, onValueChange = { query = it },
            modifier = Modifier.fillMaxWidth(), singleLine = true,
            placeholder = { Text("جستجوی کارت‌ها") },
            leadingIcon = { Icon(Icons.Outlined.Search, null) })
        Row(Modifier.fillMaxSize().horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            lanes.forEach { lane ->
                PersonalKanbanLane(store, lane, lanes, query, showArchive,
                    modifier = Modifier.width(276.dp).fillMaxHeight())
            }
            OutlinedCard(onClick = { addLane = true }, modifier = Modifier.width(245.dp)) {
                Row(Modifier.padding(15.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Outlined.Add, null); Spacer(Modifier.width(8.dp))
                    Text("افزودن ستون")
                }
            }
        }
    }
    if (addLane) KanbanNameDialog("ستون جدید", "عنوان ستون", laneName, { laneName = it },
        onDismiss = { addLane = false }, onSave = {
            store.newLane(board.id, laneName); laneName = ""; addLane = false
        })
    if (rename) KanbanNameDialog("ویرایش نام پروژه", "نام", boardName, { boardName = it },
        onDismiss = { rename = false },
        onSave = { store.renameBoard(board.id, boardName); rename = false })
    if (delete) KanbanConfirm("پروژه به همراه ستون‌ها و کارت‌هایش حذف شود؟",
        onDismiss = { delete = false }, onConfirm = {
            store.removeBoard(board.id); delete = false; onBack()
        })
}

@Composable
private fun PersonalKanbanLane(
    store: ZurvanKanbanStore, lane: ZLane, lanes: List<ZLane>, query: String,
    showArchive: Boolean, modifier: Modifier
) {
    var add by remember { mutableStateOf(false) }
    var title by remember { mutableStateOf("") }
    var menu by remember { mutableStateOf(false) }
    var rename by remember { mutableStateOf(false) }
    var name by remember(lane.id) { mutableStateOf(lane.name) }
    var delete by remember { mutableStateOf(false) }
    val cards = store.cards.filter {
        it.lane == lane.id && it.archived == showArchive &&
            (query.isBlank() || it.title.contains(query, ignoreCase = true) ||
                it.description.contains(query, ignoreCase = true) ||
                it.label.contains(query, ignoreCase = true))
    }.sortedBy { it.order }
    Surface(modifier, shape = RoundedCornerShape(10.dp),
        color = MaterialTheme.colorScheme.surfaceVariant) {
        Column(Modifier.padding(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(lane.name, Modifier.weight(1f), fontWeight = FontWeight.Bold)
                Text(cards.size.toString(), style = MaterialTheme.typography.labelSmall)
                Box {
                    IconButton(onClick = { menu = true }) { Icon(Icons.Outlined.MoreHoriz, "ستون") }
                    DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                        DropdownMenuItem(text = { Text("تغییر عنوان") },
                            onClick = { menu = false; rename = true })
                        DropdownMenuItem(text = { Text("حذف ستون") },
                            onClick = { menu = false; delete = true })
                    }
                }
            }
            Column(Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(8.dp)) {
                cards.forEach { card -> PersonalKanbanCard(store, card, lanes) }
            }
            if (!showArchive) TextButton(onClick = { add = true }) {
                Icon(Icons.Outlined.Add, null); Text("افزودن کارت")
            }
        }
    }
    if (add) KanbanNameDialog("کارت جدید", "عنوان کارت", title, { title = it },
        onDismiss = { add = false }, onSave = { store.newCard(lane.id, title); title = ""; add = false })
    if (rename) KanbanNameDialog("ویرایش ستون", "عنوان", name, { name = it },
        onDismiss = { rename = false },
        onSave = { store.renameLane(lane.id, name); rename = false })
    if (delete) KanbanConfirm("این ستون و کارت‌های آن حذف شوند؟",
        onDismiss = { delete = false },
        onConfirm = { store.removeLane(lane.id); delete = false })
}

@Composable
private fun PersonalKanbanCard(store: ZurvanKanbanStore, card: ZCard, lanes: List<ZLane>) {
    var open by remember { mutableStateOf(false) }
    var drag by remember(card.id) { mutableFloatStateOf(0f) }
    val gesture = Modifier.pointerInput(card.id, lanes) {
        detectDragGesturesAfterLongPress(
            onDragEnd = {
                val index = lanes.indexOfFirst { it.id == card.lane }
                val target = index + when {
                    drag < -90.dp.toPx() -> 1
                    drag > 90.dp.toPx() -> -1
                    else -> 0
                }
                if (target in lanes.indices && target != index)
                    store.moveCard(card.id, lanes[target].id)
                drag = 0f
            },
            onDragCancel = { drag = 0f },
            onDrag = { change, delta -> change.consume(); drag += delta.x }
        )
    }
    ElevatedCard(onClick = { open = true },
        modifier = Modifier.fillMaxWidth().offset { IntOffset(drag.roundToInt(), 0) }.then(gesture),
        colors = CardDefaults.elevatedCardColors(containerColor = MaterialTheme.colorScheme.surface)) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            if (card.label.isNotBlank()) Surface(shape = RoundedCornerShape(4.dp),
                color = MaterialTheme.colorScheme.tertiaryContainer) {
                Text(card.label, Modifier.padding(horizontal = 8.dp, vertical = 3.dp),
                    fontSize = 11.sp, color = MaterialTheme.colorScheme.onTertiaryContainer)
            }
            Text(card.title, fontWeight = FontWeight.Medium)
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                if (card.description.isNotBlank()) Icon(Icons.Outlined.Notes, null, Modifier.size(15.dp))
                if (card.due.isNotBlank()) Text("◷ " + card.due, style = MaterialTheme.typography.labelSmall)
                if (card.tasks.isNotEmpty())
                    Text("☑ " + card.tasks.count { it.done } + "/" + card.tasks.size,
                        style = MaterialTheme.typography.labelSmall)
                if (card.comments.isNotEmpty())
                    Text("◉ " + card.comments.size, style = MaterialTheme.typography.labelSmall)
            }
        }
    }
    if (open) KanbanCardDialog(store, card, lanes, onDismiss = { open = false })
}

@Composable
private fun KanbanCardDialog(store: ZurvanKanbanStore, card: ZCard, lanes: List<ZLane>, onDismiss: () -> Unit) {
    val current = store.cards.firstOrNull { it.id == card.id } ?: return
    var name by remember(card.id) { mutableStateOf(card.title) }
    var description by remember(card.id) { mutableStateOf(card.description) }
    var label by remember(card.id) { mutableStateOf(card.label) }
    var due by remember(card.id) { mutableStateOf(card.due) }
    var task by remember { mutableStateOf("") }
    var comment by remember { mutableStateOf("") }
    var laneMenu by remember { mutableStateOf(false) }
    var delete by remember { mutableStateOf(false) }
    AlertDialog(onDismissRequest = onDismiss, title = { Text("کارت: " + current.title) },
        text = {
            Column(Modifier.fillMaxWidth().heightIn(max = 520.dp).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(name, { name = it }, label = { Text("عنوان کارت") },
                    modifier = Modifier.fillMaxWidth(), singleLine = true)
                OutlinedTextField(description, { description = it }, label = { Text("توضیحات") },
                    modifier = Modifier.fillMaxWidth(), minLines = 3)
                OutlinedTextField(label, { label = it }, label = { Text("برچسب") },
                    modifier = Modifier.fillMaxWidth())
                OutlinedTextField(due, { due = it }, label = { Text("تاریخ سررسید YYYY-MM-DD") },
                    modifier = Modifier.fillMaxWidth())
                Box {
                    OutlinedButton(onClick = { laneMenu = true }) {
                        Text("انتقال به ستون: " + (lanes.firstOrNull { it.id == current.lane }?.name ?: ""))
                        Icon(Icons.Outlined.ArrowDropDown, null)
                    }
                    DropdownMenu(expanded = laneMenu, onDismissRequest = { laneMenu = false }) {
                        lanes.forEach { lane ->
                            DropdownMenuItem(text = { Text(lane.name) }, onClick = {
                                store.moveCard(current.id, lane.id)
                                laneMenu = false
                            })
                        }
                    }
                }
                Row {
                    TextButton(onClick = { store.reorderCard(current.id, -1) }) { Text("↑ بالاتر") }
                    TextButton(onClick = { store.reorderCard(current.id, 1) }) { Text("↓ پایین‌تر") }
                }
                HorizontalDivider()
                Text("چک‌لیست", fontWeight = FontWeight.Bold)
                current.tasks.forEachIndexed { index, item ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(item.done, onCheckedChange = { checked ->
                            store.updateCard(current.copy(tasks = current.tasks.mapIndexed { i, t ->
                                if (i == index) t.copy(done = checked) else t
                            }))
                        })
                        Text(item.text, Modifier.weight(1f))
                        IconButton(onClick = { store.updateCard(current.copy(tasks =
                            current.tasks.filterIndexed { i, _ -> i != index })) }) {
                            Icon(Icons.Outlined.Close, "حذف")
                        }
                    }
                }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    OutlinedTextField(task, { task = it }, modifier = Modifier.weight(1f),
                        label = { Text("مورد جدید") }, singleLine = true)
                    IconButton(enabled = task.isNotBlank(), onClick = {
                        store.updateCard(current.copy(tasks = current.tasks + ZTask(task.trim(), false)))
                        task = ""
                    }) { Icon(Icons.Outlined.Add, "افزودن") }
                }
                HorizontalDivider()
                Text("نظرات", fontWeight = FontWeight.Bold)
                current.comments.forEach { Text("• " + it) }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    OutlinedTextField(comment, { comment = it }, modifier = Modifier.weight(1f),
                        label = { Text("نظر جدید") })
                    IconButton(enabled = comment.isNotBlank(), onClick = {
                        store.updateCard(current.copy(comments = current.comments + comment.trim()))
                        comment = ""
                    }) { Icon(Icons.Outlined.Send, "ثبت") }
                }
                Row {
                    TextButton(onClick = {
                        store.updateCard(current.copy(archived = !current.archived)); onDismiss()
                    }) { Text(if (current.archived) "خروج از بایگانی" else "بایگانی") }
                    TextButton(onClick = { delete = true }) {
                        Text("حذف", color = MaterialTheme.colorScheme.error)
                    }
                }
            }
        },
        confirmButton = { TextButton(enabled = name.isNotBlank(), onClick = {
            store.updateCard(current.copy(title = name.trim(),
                description = description, label = label, due = due))
            onDismiss()
        }) { Text("ذخیره") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("بستن") } }
    )
    if (delete) KanbanConfirm("این کارت برای همیشه حذف شود؟",
        onDismiss = { delete = false },
        onConfirm = { store.removeCard(current.id); onDismiss() })
}

@Composable
private fun KanbanNameDialog(title: String, hint: String, value: String,
                             onValueChange: (String) -> Unit, onDismiss: () -> Unit, onSave: () -> Unit) {
    AlertDialog(onDismissRequest = onDismiss, title = { Text(title) },
        text = { OutlinedTextField(value, onValueChange, label = { Text(hint) }, singleLine = true) },
        confirmButton = {
            TextButton(enabled = value.isNotBlank(), onClick = onSave) { Text("ذخیره") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("انصراف") } })
}

@Composable
private fun KanbanConfirm(message: String, onDismiss: () -> Unit, onConfirm: () -> Unit) {
    AlertDialog(onDismissRequest = onDismiss, title = { Text("تأیید حذف") },
        text = { Text(message) },
        confirmButton = { TextButton(onClick = onConfirm) { Text("حذف") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("انصراف") } })
}
