@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)

package com.bdfr.zurvan

import androidx.compose.foundation.*
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import android.net.Uri
import android.provider.OpenableColumns
import android.content.Intent
import java.io.File
import java.util.UUID
import java.time.LocalDate
import android.graphics.BitmapFactory
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.*
import androidx.compose.ui.graphics.Color
import kotlin.math.roundToInt
import kotlin.math.abs
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
    var backupStatus by remember { mutableStateOf("") }
    val scope = rememberCoroutineScope()
    val exportPicker = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument("application/zip")) { uri ->
        if (uri != null) {
            val snapshot = store.exportMetadata()
            scope.launch {
                val result = withContext(Dispatchers.IO) {
                    runCatching { ZurvanKanbanBackup.export(context, uri, snapshot) }
                }
                backupStatus = result.fold(
                    onSuccess = { "نسخه پشتیبان کامل همراه پیوست‌ها ذخیره شد." },
                    onFailure = { "خطا در ذخیره پشتیبان: " + (it.message ?: "ناشناخته") }
                )
            }
        }
    }
    val importPicker = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) {
            scope.launch {
                val result = withContext(Dispatchers.IO) {
                    runCatching { ZurvanKanbanBackup.prepareImport(context, uri) }
                }
                result.onSuccess { prepared ->
                    try {
                        store.importMetadata(prepared.snapshot)
                        selectedId = null
                        backupStatus = "پروژه‌ها و پیوست‌ها با موفقیت از فایل پشتیبان بازیابی شدند."
                    } catch (cause: Exception) {
                        prepared.discardIfNotImported()
                        backupStatus = "بازیابی اطلاعات ناموفق بود: " + (cause.message ?: "ناشناخته")
                    }
                }.onFailure { cause ->
                    backupStatus = "خواندن فایل پشتیبان ناموفق بود: " + (cause.message ?: "ناشناخته")
                }
            }
        }
    }
    Column(modifier.fillMaxSize().padding(12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        if (store.loadWarning.isNotBlank()) {
            Text(store.loadWarning, color = MaterialTheme.colorScheme.error)
        }
        val selected = store.boards.firstOrNull { it.id == selectedId }
        if (selected != null) {
            Box(Modifier.weight(1f)) {
                PersonalKanbanBoard(store, selected, onBack = { selectedId = null })
            }
        } else {
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(12.dp)) {
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
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = { exportPicker.launch("Zurvan-personal-kanban.zip") }) {
                Icon(Icons.Outlined.FileDownload, null)
                Spacer(Modifier.width(5.dp))
                Text("پشتیبان‌گیری")
            }
            OutlinedButton(onClick = { importPicker.launch(arrayOf("application/zip", "application/octet-stream")) }) {
                Icon(Icons.Outlined.FileUpload, null)
                Spacer(Modifier.width(5.dp))
                Text("بازیابی")
            }
        }
        if (backupStatus.isNotBlank())
            Text(backupStatus, style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
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
    var priorityFilter by remember { mutableStateOf("all") }
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
        Text("کارت را لمس و نگه‌دار، سپس برای انتقال به ستون دیگر افقی و برای تغییر ترتیب عمودی بکش.",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            listOf("all" to "همه", "urgent" to "فوری", "high" to "مهم",
                "normal" to "عادی", "low" to "کم").forEach { (key, text) ->
                FilterChip(
                    selected = priorityFilter == key,
                    onClick = { priorityFilter = key },
                    label = { Text(text) })
            }
        }
        val boardCards = store.cards.filter { item ->
            item.lane in lanes.map { it.id } && item.archived == showArchive
        }
        Text("${boardCards.size} کارت · ${boardCards.count { row -> row.due.isNotBlank() &&
            runCatching { LocalDate.parse(row.due) < LocalDate.now() }.getOrDefault(false) }} سررسید گذشته",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        Row(Modifier.fillMaxSize().horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            lanes.forEach { lane ->
                PersonalKanbanLane(store, lane, lanes, query, showArchive, priorityFilter,
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
    showArchive: Boolean, priorityFilter: String, modifier: Modifier
) {
    var add by remember { mutableStateOf(false) }
    var title by remember { mutableStateOf("") }
    var menu by remember { mutableStateOf(false) }
    var rename by remember { mutableStateOf(false) }
    var name by remember(lane.id) { mutableStateOf(lane.name) }
    var delete by remember { mutableStateOf(false) }
    val cards = store.cards.filter {
        it.lane == lane.id && it.archived == showArchive &&
            (priorityFilter == "all" || it.priority == priorityFilter) &&
            (query.isBlank() || it.title.contains(query, ignoreCase = true) ||
                it.description.contains(query, ignoreCase = true) ||
                it.label.contains(query, ignoreCase = true) ||
                it.labels.any { label -> label.contains(query, ignoreCase = true) } ||
                it.customFields.any { (key, value) ->
                    key.contains(query, ignoreCase = true) || value.contains(query, ignoreCase = true)
                })
    }.sortedBy { it.order }
    Surface(modifier, shape = RoundedCornerShape(10.dp),
        color = MaterialTheme.colorScheme.surfaceVariant) {
        Column(Modifier.padding(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Surface(color = kanbanLaneTint(lane.name),
                shape = RoundedCornerShape(8.dp)) {
                Row(Modifier.padding(start = 8.dp, end = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(lane.name, Modifier.weight(1f), fontWeight = FontWeight.Bold,
                    color = Color(0xFF26364A))
                Text(cards.size.toString(), style = MaterialTheme.typography.labelSmall)
                Box {
                    IconButton(onClick = { menu = true }) { Icon(Icons.Outlined.MoreHoriz, "ستون") }
                    DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                        DropdownMenuItem(text = { Text("تغییر عنوان") },
                            onClick = { menu = false; rename = true })
                    DropdownMenuItem(text = { Text("انتقال ستون به راست") },
                            onClick = { menu = false; store.reorderLane(lane.board, lane.id, -1) })
                    DropdownMenuItem(text = { Text("انتقال ستون به چپ") },
                            onClick = { menu = false; store.reorderLane(lane.board, lane.id, 1) })
                        DropdownMenuItem(text = { Text("حذف ستون") },
                            onClick = { menu = false; delete = true })
                    }
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


private fun kanbanCoverImage(context: android.content.Context, card: ZCard): ImageBitmap? {
    val images = card.attachments.filter {
        File(it.path).extension.lowercase() in setOf("png", "jpg", "jpeg", "webp", "bmp")
    }
    val fileInfo = images.firstOrNull { it.id == card.coverAttachmentId } ?: images.firstOrNull()
        ?: return null
    return runCatching {
        val root = File(context.filesDir, "zurvan_kanban_files").canonicalFile
        val file = File(fileInfo.path).canonicalFile
        require(file.parentFile == root && file.isFile && file.length() <= 15_000_000L)
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(file.path, bounds)
        require(bounds.outWidth > 0 && bounds.outHeight > 0)
        var sample = 1
        while (bounds.outWidth / sample > 960 || bounds.outHeight / sample > 720) sample *= 2
        val bitmap = BitmapFactory.decodeFile(file.path, BitmapFactory.Options().apply {
            inSampleSize = sample
        }) ?: return null
        bitmap.asImageBitmap()
    }.getOrNull()
}

private fun kanbanLaneTint(name: String): Color = when {
    name.contains("انجام‌شده") || name.contains("Done", ignoreCase = true) -> Color(0xFFD9EFDF)
    name.contains("بررسی") || name.contains("Testing", ignoreCase = true) -> Color(0xFFF9E8B9)
    name.contains("در حال") || name.contains("Doing", ignoreCase = true) -> Color(0xFFD2E6FE)
    else -> Color(0xFFE7E5F8)
}

private fun kanbanLabelTint(label: String): Color {
    val palette = listOf(Color(0xFFDDE8FA), Color(0xFFFFE1D6),
        Color(0xFFE0F0DF), Color(0xFFF9EDC0), Color(0xFFE9DEFA))
    val index = (label.hashCode().toLong() and 0x7FFFFFFF) % palette.size
    return palette[index.toInt()]
}

private val kanbanCardPalette = listOf(
    "" to "پیش‌فرض", "#FFE9F3FF" to "آبی", "#FFE8F7EB" to "سبز",
    "#FFFFF4D9" to "زرد", "#FFFFE8EF" to "صورتی", "#FFF1EAFE" to "بنفش"
)

@Composable
private fun PersonalKanbanCard(store: ZurvanKanbanStore, card: ZCard, lanes: List<ZLane>) {
    var open by remember { mutableStateOf(false) }
    val context = LocalContext.current
    val cover = remember(card.attachments, card.coverAttachmentId) { kanbanCoverImage(context, card) }
    var drag by remember(card.id) { mutableFloatStateOf(0f) }
    var dragY by remember(card.id) { mutableFloatStateOf(0f) }
    val gesture = Modifier.pointerInput(card.id, card.lane, lanes) {
        detectDragGesturesAfterLongPress(
            onDragEnd = {
                val index = lanes.indexOfFirst { it.id == card.lane }
                if (index >= 0 && abs(drag) > 85.dp.toPx()) {
                    val steps = (abs(drag) / 276.dp.toPx()).roundToInt().coerceAtLeast(1)
                    val target = (index + if (drag < 0f) steps else -steps)
                        .coerceIn(0, lanes.lastIndex)
                    if (target != index) store.moveCard(card.id, lanes[target].id)
                } else if (abs(dragY) > 55.dp.toPx()) {
                    val steps = (abs(dragY) / 95.dp.toPx()).roundToInt().coerceIn(1, 10)
                    repeat(steps) { store.reorderCard(card.id, if (dragY > 0f) 1 else -1) }
                }
                drag = 0f
                dragY = 0f
            },
            onDragCancel = { drag = 0f; dragY = 0f },
            onDrag = { change, delta ->
                change.consume()
                drag += delta.x
                dragY += delta.y
            }
        )
    }
    ElevatedCard(onClick = { open = true },
        modifier = Modifier.fillMaxWidth().offset {
            IntOffset(drag.roundToInt(), dragY.roundToInt())
        }.then(gesture),
        colors = CardDefaults.elevatedCardColors(
            containerColor = if (abs(drag) + abs(dragY) > 4f)
                MaterialTheme.colorScheme.primaryContainer else if (card.cardColor.isNotBlank())
                runCatching { Color(android.graphics.Color.parseColor(card.cardColor)) }
                    .getOrDefault(MaterialTheme.colorScheme.surface)
            else MaterialTheme.colorScheme.surface
        )) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            if (cover != null) Image(
                bitmap = cover, contentDescription = "کاور کارت",
                modifier = Modifier.fillMaxWidth().height(132.dp),
                contentScale = ContentScale.Crop
            )
            val displayLabels = if (card.labels.isNotEmpty()) card.labels else listOf(card.label).filter { it.isNotBlank() }
            if (displayLabels.isNotEmpty()) Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                displayLabels.take(3).forEach { tag ->
                    Surface(shape = RoundedCornerShape(4.dp),
                        color = kanbanLabelTint(tag)) {
                        Text(tag, Modifier.padding(horizontal = 6.dp, vertical = 3.dp),
                            fontSize = 10.sp, color = Color(0xFF26364A),
                            maxLines = 1)
                    }
                }
            }
            if (card.priority != "normal") Text(
                when (card.priority) {
                    "urgent" -> "● فوری"
                    "high" -> "● مهم"
                    else -> "● کم‌اهمیت"
                }, fontSize = 11.sp,
                color = if (card.priority == "urgent") MaterialTheme.colorScheme.error
                    else MaterialTheme.colorScheme.primary
            )
            Text(card.title, fontWeight = FontWeight.Medium)
            if (card.description.isNotBlank())
                Text(card.description, maxLines = 3, overflow = TextOverflow.Ellipsis,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (card.tasks.isNotEmpty()) {
                LinearProgressIndicator(
                    progress = { card.tasks.count { it.done }.toFloat() / card.tasks.size },
                    modifier = Modifier.fillMaxWidth().height(3.dp))
            }
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                if (card.start.isNotBlank())
                    Text("شروع: " + card.start, style = MaterialTheme.typography.labelSmall)
                if (card.completedAt.isNotBlank())
                    Text("✓ " + card.completedAt.take(10),
                        style = MaterialTheme.typography.labelSmall)
                if (card.description.isNotBlank()) Icon(Icons.Outlined.Notes, null, Modifier.size(15.dp))
                if (card.due.isNotBlank()) {
                    val overdue = runCatching {
                        LocalDate.parse(card.due) < LocalDate.now()
                    }.getOrDefault(false)
                    Text("◷ " + card.due, style = MaterialTheme.typography.labelSmall,
                        color = if (overdue) MaterialTheme.colorScheme.error
                            else MaterialTheme.colorScheme.onSurfaceVariant)
                }
                if (card.tasks.isNotEmpty())
                    Text("☑ " + card.tasks.count { it.done } + "/" + card.tasks.size,
                        style = MaterialTheme.typography.labelSmall)
                if (card.comments.isNotEmpty())
                    Text("◉ " + card.comments.size, style = MaterialTheme.typography.labelSmall)
                if (card.attachments.isNotEmpty())
                    Text("📎 " + card.attachments.size, style = MaterialTheme.typography.labelSmall)
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
    var label by remember(card.id) { mutableStateOf(card.labels.joinToString(", ").ifBlank { card.label }) }
    var due by remember(card.id) { mutableStateOf(card.due) }
    var start by remember(card.id) { mutableStateOf(card.start) }
    var selectedColor by remember(card.id) { mutableStateOf(card.cardColor) }
    var selectedCover by remember(card.id) { mutableStateOf(card.coverAttachmentId) }
    var priority by remember(card.id) { mutableStateOf(card.priority) }
    var cardType by remember(card.id) { mutableStateOf(card.cardType) }
    var customFields by remember(card.id) { mutableStateOf(card.customFields.toMap()) }
    var fieldKey by remember { mutableStateOf("") }
    var fieldValue by remember { mutableStateOf("") }
    var typeMenu by remember { mutableStateOf(false) }
    var priorityMenu by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf("") }
    val context = LocalContext.current
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) {
            var pendingFile: File? = null
            runCatching {
                val resolver = context.contentResolver
                val name = resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE),
                    null, null, null)?.use { cursor ->
                    if (cursor.moveToFirst()) {
                        val nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                        val sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE)
                        (if (nameIndex >= 0) cursor.getString(nameIndex).orEmpty().ifBlank { "attachment" }
                            else "attachment") to
                        (if (sizeIndex >= 0 && !cursor.isNull(sizeIndex)) cursor.getLong(sizeIndex) else -1L)
                    } else "attachment" to -1L
                } ?: ("attachment" to -1L)
                if (name.second > 15_000_000L) throw IllegalArgumentException("فایل باید کوچک‌تر از ۱۵ مگابایت باشد")
                val dir = File(context.filesDir, "zurvan_kanban_files").apply { mkdirs() }
                val ext = name.first.substringAfterLast('.', "").take(10)
                    .takeIf { it.isNotBlank() && it.all { ch -> ch.isLetterOrDigit() } } ?: ""
                val file = File(dir, UUID.randomUUID().toString() +
                    (if (ext.isEmpty()) "" else "." + ext))
                pendingFile = file
                resolver.openInputStream(uri)?.use { input ->
                    file.outputStream().use { output ->
                        val buffer = ByteArray(8192)
                        var count: Int
                        var total = 0L
                        while (input.read(buffer).also { count = it } != -1) {
                            total += count
                            require(total <= 15_000_000L) { "حجم فایل بیشتر از ۱۵ مگابایت است" }
                            output.write(buffer, 0, count)
                        }
                    }
                } ?: throw IllegalArgumentException("خواندن فایل ممکن نشد")
                store.addAttachment(current.id,
                    ZAttachment(UUID.randomUUID().toString(), name.first.take(150), file.absolutePath, file.length()))
                pendingFile = null
                error = ""
            }.onFailure { ex ->
                pendingFile?.delete()
                error = ex.message ?: "افزودن فایل انجام نشد"
            }
        }
    }
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
                OutlinedTextField(label, { label = it },
                    label = { Text("برچسب‌ها (جدا با ویرگول)") },
                    modifier = Modifier.fillMaxWidth())
                Box {
                    OutlinedButton(onClick = { priorityMenu = true }) {
                        Text("اولویت: " + when (priority) {
                            "urgent" -> "فوری"; "high" -> "زیاد"; "low" -> "کم"; else -> "معمولی"
                        })
                        Icon(Icons.Outlined.ArrowDropDown, null)
                    }
                    DropdownMenu(expanded = priorityMenu, onDismissRequest = { priorityMenu = false }) {
                        listOf("low" to "کم", "normal" to "معمولی",
                            "high" to "زیاد", "urgent" to "فوری").forEach { (id, name) ->
                            DropdownMenuItem(text = { Text(name) },
                                onClick = { priority = id; priorityMenu = false })
                        }
                    }
                }
                Box {
                    OutlinedButton(onClick = { typeMenu = true }) {
                        Text("نوع کارت: " + when (cardType) {
                            "story" -> "داستان"; "project" -> "پروژه"; else -> "کار"
                        })
                        Icon(Icons.Outlined.ArrowDropDown, null)
                    }
                    DropdownMenu(expanded = typeMenu, onDismissRequest = { typeMenu = false }) {
                        listOf("task" to "کار", "story" to "داستان", "project" to "پروژه")
                            .forEach { (id, label) ->
                                DropdownMenuItem(text = { Text(label) }, onClick = {
                                    cardType = id; typeMenu = false
                                })
                            }
                    }
                }
                Text("رنگ کارت", fontWeight = FontWeight.SemiBold)
                Row(Modifier.horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    kanbanCardPalette.forEach { (value, text) ->
                        FilterChip(selected = selectedColor == value, onClick = { selectedColor = value },
                            label = { Text(text) })
                    }
                }
                OutlinedTextField(start, { start = it }, label = { Text("تاریخ شروع YYYY-MM-DD") },
                    modifier = Modifier.fillMaxWidth())
                OutlinedTextField(due, { due = it }, label = { Text("تاریخ پایان YYYY-MM-DD") },
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
                Text("فیلدهای سفارشی", fontWeight = FontWeight.Bold)
                customFields.entries.toList().forEach { (key, value) ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("$key: $value", Modifier.weight(1f),
                            style = MaterialTheme.typography.bodySmall)
                        IconButton(onClick = { customFields = customFields - key }) {
                            Icon(Icons.Outlined.Close, "حذف فیلد")
                        }
                    }
                }
                OutlinedTextField(fieldKey, { fieldKey = it.take(60) },
                    label = { Text("نام فیلد") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                OutlinedTextField(fieldValue, { fieldValue = it.take(300) },
                    label = { Text("مقدار فیلد") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                TextButton(
                    enabled = fieldKey.isNotBlank() && customFields.size < 30,
                    onClick = {
                        customFields = customFields + (fieldKey.trim() to fieldValue.trim())
                        fieldKey = ""; fieldValue = ""
                    }
                ) { Icon(Icons.Outlined.Add, null); Text("افزودن فیلد") }
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
                HorizontalDivider()
                Text("پیوست‌ها", fontWeight = FontWeight.Bold)
                TextButton(onClick = { picker.launch(arrayOf("*/*")) }) {
                    Icon(Icons.Outlined.AttachFile, null); Text("افزودن فایل تا ۱۵ مگابایت")
                }
                Text("انتخاب تصویر کاور", fontWeight = FontWeight.SemiBold)
                TextButton(onClick = { selectedCover = "" }) {
                    Text(if (selectedCover.isBlank()) "✓ بدون کاور" else "بدون کاور")
                }
                current.attachments.filter {
                    File(it.path).extension.lowercase() in setOf("png", "jpg", "jpeg", "webp", "bmp")
                }.forEach { attachment ->
                    TextButton(onClick = { selectedCover = attachment.id }) {
                        Text((if (selectedCover == attachment.id) "✓ " else "") + attachment.name)
                    }
                }
                current.attachments.forEach { attachment ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(attachment.name, Modifier.weight(1f), maxLines = 1,
                            style = MaterialTheme.typography.bodySmall)
                        IconButton(onClick = {
                            runCatching {
                                val file = File(attachment.path)
                                if (file.exists()) {
                                    val contentUri = androidx.core.content.FileProvider.getUriForFile(
                                        context, context.packageName + ".kanbanfiles", file)
                                    val ext = attachment.name.substringAfterLast('.', "").lowercase()
                                    val mime = android.webkit.MimeTypeMap.getSingleton()
                                        .getMimeTypeFromExtension(ext) ?: "application/octet-stream"
                                    val intent = Intent(Intent.ACTION_VIEW)
                                        .setDataAndType(contentUri, mime)
                                        .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                                    context.startActivity(Intent.createChooser(intent, "باز کردن فایل"))
                                } else error = "فایل پیوست پیدا نشد"
                            }.onFailure { error = "برنامه‌ای برای نمایش فایل پیدا نشد" }
                        }) { Icon(Icons.Outlined.OpenInNew, "باز کردن") }
                        IconButton(onClick = {
                            store.removeAttachment(current.id, attachment.id)
                        }) { Icon(Icons.Outlined.DeleteOutline, "حذف فایل") }
                    }
                }
                HorizontalDivider()
                Text("تاریخچه فعالیت", fontWeight = FontWeight.Bold)
                current.activity.takeLast(15).asReversed().forEach { entry ->
                    Text(entry.at.take(16).replace("T", " ") + " · " + entry.detail,
                        style = MaterialTheme.typography.labelSmall)
                }
                if (error.isNotBlank()) Text(error, color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodySmall)
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
            val valid = (due.isBlank() || runCatching { LocalDate.parse(due.trim()) }.isSuccess) &&
                (start.isBlank() || runCatching { LocalDate.parse(start.trim()) }.isSuccess) &&
                (start.isBlank() || due.isBlank() || start.trim() <= due.trim())
            if (!valid) {
                error = "تاریخ شروع و پایان باید YYYY-MM-DD باشند و پایان قبل از شروع نباشد."
            } else {
                val tags = label.split(",", "،").map { it.trim() }.filter { it.isNotBlank() }.distinct()
                store.updateCard(current.copy(title = name.trim(),
                    description = description, label = tags.firstOrNull() ?: "",
                    labels = tags.take(12), priority = priority, due = due.trim(),
                    start = start.trim(), cardColor = selectedColor,
                    coverAttachmentId = selectedCover.takeIf { id ->
                        current.attachments.any { it.id == id }
                    } ?: "",
                    cardType = cardType, customFields = customFields))
                onDismiss()
            }
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
