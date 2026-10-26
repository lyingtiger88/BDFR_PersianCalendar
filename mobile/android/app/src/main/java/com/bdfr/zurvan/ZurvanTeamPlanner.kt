package com.bdfr.zurvan

import android.content.Context
import android.graphics.BitmapFactory
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID

/** On-device team planner. Remote membership and cross-device sync require a service. */
private class TeamStore(private val context: Context) {
    private val prefs = context.getSharedPreferences("zurvan_team_planner_v1", Context.MODE_PRIVATE)
    fun get(key: String) = prefs.getString(key, "") ?: ""
    fun set(key: String, value: String) { prefs.edit().putString(key, value).apply() }
    fun members(): MutableList<Pair<String, String>> {
        val a = JSONArray(get("members").ifBlank { "[]" })
        return (0 until a.length()).map {
            val row = a.getJSONObject(it)
            row.getString("id") to row.getString("role")
        }.toMutableList()
    }
    fun saveMembers(rows: List<Pair<String, String>>) {
        val a = JSONArray()
        rows.forEach { (id, role) -> a.put(JSONObject().put("id", id).put("role", role)) }
        set("members", a.toString())
    }
}
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ZurvanTeamPlanner(
    modifier: Modifier = Modifier,
    onAddSchedule: () -> Unit,
    plans: List<LocalPlan>
) {
    val context = LocalContext.current
    val store = remember { TeamStore(context) }
    var profileName by remember { mutableStateOf(store.get("profile_name")) }
    var profileId by remember { mutableStateOf(store.get("profile_id")) }
    var groupName by remember { mutableStateOf(store.get("group_name")) }
    var newMember by remember { mutableStateOf("") }
    var members by remember { mutableStateOf(store.members().toList()) }
    var selectedRole by remember { mutableStateOf("عضو") }
    var photoRevision by remember { mutableIntStateOf(0) }
    var weekOffset by remember { mutableIntStateOf(0) }
    var expandedDay by remember { mutableStateOf<String?>(null) }
    var alertMinutes by remember { mutableStateOf(store.get("alert_minutes").ifBlank { "30" }) }
    var activeSection by remember { mutableStateOf(0) }
    var status by remember { mutableStateOf("") }
    val avatar = remember(photoRevision) {
        val f = File(context.filesDir, "zurvan_profile_photo")
        if (f.exists()) BitmapFactory.decodeFile(f.absolutePath)?.asImageBitmap() else null
    }
    var pendingPhoto by remember { mutableStateOf<ImageBitmap?>(null) }
    var pendingError by remember { mutableStateOf("") }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) runCatching {
            context.contentResolver.openInputStream(uri)?.use { input ->
                val data = input.readBytes()
                require(data.size <= 12_000_000) { "حجم عکس باید کمتر از ۱۲ مگابایت باشد." }
                BitmapFactory.decodeByteArray(data, 0, data.size)?.asImageBitmap()
                    ?: error("این تصویر قابل خواندن نیست.")
            } ?: error("فایل باز نشد.")
        }.onSuccess { pendingPhoto = it; pendingError = "" }
            .onFailure { pendingError = it.message ?: "خواندن عکس انجام نشد." }
    }
    if (pendingPhoto != null) {
        var zoom by remember(pendingPhoto) { mutableFloatStateOf(1f) }
        var moveX by remember(pendingPhoto) { mutableFloatStateOf(0f) }
        var moveY by remember(pendingPhoto) { mutableFloatStateOf(0f) }
        AlertDialog(
            onDismissRequest = { pendingPhoto = null },
            title = { Text("ویرایش عکس پروفایل") },
            text = {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("عکس را جابه‌جا کنید و با دو انگشت بزرگ‌نمایی کنید.")
                    Box(Modifier.size(240.dp).clip(CircleShape), contentAlignment = Alignment.Center) {
                        pendingPhoto?.let { picture ->
                            Image(
                                bitmap = picture, contentDescription = "پیش‌نمایش برش دایره‌ای",
                                contentScale = ContentScale.Crop,
                                modifier = Modifier.fillMaxSize()
                                    .pointerInput(picture) {
                                        detectTransformGestures { _, pan, scale, _ ->
                                            zoom = (zoom * scale).coerceIn(1f, 5f)
                                            moveX += pan.x
                                            moveY += pan.y
                                        }
                                    }
                                    .graphicsLayer {
                                        scaleX = zoom; scaleY = zoom
                                        translationX = moveX; translationY = moveY
                                    }
                            )
                        }
                    }
                    Text("بزرگ‌نمایی")
                    Slider(value = zoom, onValueChange = { zoom = it }, valueRange = 1f..5f)
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    val picture = pendingPhoto ?: return@TextButton
                    runCatching {
                        val size = 512
                        val source = picture.asAndroidBitmap()
                        // Save the same center-crop framing that was approved in the preview.
                        val raw = android.graphics.Bitmap.createBitmap(size, size, android.graphics.Bitmap.Config.ARGB_8888)
                        val canvas = android.graphics.Canvas(raw)
                        val paint = android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG)
                        val path = android.graphics.Path().apply {
                            addCircle(size / 2f, size / 2f, size / 2f, android.graphics.Path.Direction.CW)
                        }
                        canvas.clipPath(path)
                        val initialScale = maxOf(size.toFloat() / picture.width, size.toFloat() / picture.height)
                        val factor = initialScale * zoom
                        val width = picture.width * factor
                        val height = picture.height * factor
                        val offsetX = (size - width) / 2f + moveX * size / 240f
                        val offsetY = (size - height) / 2f + moveY * size / 240f
                        canvas.drawBitmap(source, null,
                            android.graphics.RectF(offsetX, offsetY, offsetX + width, offsetY + height), paint)
                        File(context.filesDir, "zurvan_profile_photo").outputStream().use {
                            raw.compress(android.graphics.Bitmap.CompressFormat.PNG, 95, it)
                        }
                        raw.recycle()
                    }.onSuccess { photoRevision++; pendingPhoto = null }
                        .onFailure { pendingError = it.message ?: "ذخیره عکس ناموفق بود." }
                }) { Text("اعمال عکس") }
            },
            dismissButton = { TextButton(onClick = { pendingPhoto = null }) { Text("انصراف") } },
        )
    }
    val tabs = listOf("زمان‌بندی", "گروه من", "پروفایل")
    Column(modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text("برنامه‌ریزی و گروه‌ها", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
        TabRow(selectedTabIndex = activeSection) {
            tabs.forEachIndexed { i, title ->
                Tab(selected = activeSection == i, onClick = { activeSection = i }, text = { Text(title) })
            }
        }
        when (activeSection) {
            0 -> {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    TextButton(onClick = { weekOffset-- }) { Text("هفته قبل") }
                    Text("هفته ${if (weekOffset == 0) "جاری" else (if (weekOffset > 0) "+$weekOffset" else "$weekOffset")}")
                    TextButton(onClick = { weekOffset++ }) { Text("هفته بعد") }
                }
                Button(onClick = onAddSchedule) { Text("افزودن برنامه") }
                val today = remember { java.time.LocalDate.now() }
                val saturday = remember(weekOffset) {
                    today.minusDays(((today.dayOfWeek.value + 1) % 7).toLong()).plusWeeks(weekOffset.toLong())
                }
                val dates = (0..6).map { saturday.plusDays(it.toLong()) }
                Text("برنامه هفتگی · برای باز کردن برنامه‌ها روی هر روز بزنید",
                    style = MaterialTheme.typography.titleMedium)
                val weekdays = listOf("شنبه","یکشنبه","دوشنبه","سه‌شنبه","چهارشنبه","پنجشنبه","جمعه")
                val palette = listOf(0xFFF8DDD4,0xFFFFF0C5,0xFFDFF0DD,0xFFD5EFED,0xFFDCE9FB,0xFFE8E0FA,0xFFF8DDE7)
                val weekDays = dates.mapIndexed { index, date ->
                    val persian = android.icu.util.Calendar.getInstance(
                        android.icu.util.ULocale("fa_IR@calendar=persian")
                    ).apply {
                        timeInMillis = date.atStartOfDay(java.time.ZoneId.systemDefault())
                            .toInstant().toEpochMilli()
                    }
                    val key = "%04d-%02d-%02d".format(
                        persian.get(android.icu.util.Calendar.YEAR),
                        persian.get(android.icu.util.Calendar.MONTH) + 1,
                        persian.get(android.icu.util.Calendar.DAY_OF_MONTH)
                    )
                    Triple(index, key, plans.filter { it.dateKey == key }.sortedWith(compareBy({ it.hour }, { it.minute })))
                }
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(2.dp)
                ) {
                    weekDays.forEach { (index, key, dayPlans) ->
                        val selected = expandedDay == key
                        Card(
                            modifier = Modifier.weight(1f).clickable {
                                expandedDay = if (selected) null else key
                            },
                            colors = CardDefaults.cardColors(
                                containerColor = androidx.compose.ui.graphics.Color(palette[index])
                            ),
                            border = if (selected) androidx.compose.foundation.BorderStroke(
                                2.dp, MaterialTheme.colorScheme.primary
                            ) else null
                        ) {
                            Column(
                                modifier = Modifier.fillMaxWidth().padding(vertical = 9.dp, horizontal = 1.dp),
                                horizontalAlignment = Alignment.CenterHorizontally,
                                verticalArrangement = Arrangement.spacedBy(5.dp)
                            ) {
                                Text(weekdays[index],
                                    fontSize = 9.sp,
                                    style = MaterialTheme.typography.labelSmall,
                                    maxLines = 2,
                                    textAlign = androidx.compose.ui.text.style.TextAlign.Center)
                                Text(key.takeLast(2), style = MaterialTheme.typography.labelSmall)
                                Text(if (dayPlans.isEmpty()) "–" else dayPlans.size.toString(),
                                    style = MaterialTheme.typography.labelSmall,
                                    fontWeight = FontWeight.Bold)
                            }
                        }
                    }
                }
                expandedDay?.let { selectedKey ->
                    val expanded = weekDays.firstOrNull { it.second == selectedKey }
                    if (expanded != null) {
                        val (index, key, selectedPlans) = expanded
                        Card(
                            modifier = Modifier.fillMaxWidth(),
                            colors = CardDefaults.cardColors(
                                containerColor = androidx.compose.ui.graphics.Color(palette[index])
                            )
                        ) {
                            Column(
                                modifier = Modifier.fillMaxWidth().padding(12.dp),
                                verticalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Text(weekdays[index] + " · " + key, fontWeight = FontWeight.Bold)
                                    TextButton(onClick = { expandedDay = null }) { Text("بستن") }
                                }
                                if (selectedPlans.isEmpty()) Text("برنامه‌ای ثبت نشده است.")
                                selectedPlans.forEach { plan ->
                                    ElevatedCard {
                                        Column(Modifier.fillMaxWidth().padding(9.dp)) {
                                            Text(plan.icon + " " + plan.title, fontWeight = FontWeight.SemiBold)
                                            Text("%02d:%02d".format(plan.hour, plan.minute),
                                                style = MaterialTheme.typography.labelSmall)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

            }
            1 -> {
                OutlinedTextField(groupName, { groupName = it }, label = { Text("نام گروه") }, modifier = Modifier.fillMaxWidth())
                Button(onClick = {
                    if (groupName.isNotBlank()) {
                        store.set("group_name", groupName.trim())
                        status = "گروه در این دستگاه ذخیره شد."
                    }
                }) { Text(if (store.get("group_name").isBlank()) "ساخت گروه" else "ذخیره نام گروه") }
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    if (avatar != null) {
                        Image(avatar, contentDescription = "عکس مدیر گروه", contentScale = ContentScale.Crop,
                            modifier = Modifier.size(52.dp).clip(CircleShape))
                    } else {
                        Surface(shape = CircleShape, color = MaterialTheme.colorScheme.primaryContainer) {
                            Text("👤", modifier = Modifier.padding(12.dp))
                        }
                    }
                    Column {
                        Text("مدیر گروه: ${profileName.ifBlank { "پروفایل من" }}", fontWeight = FontWeight.SemiBold)
                        Text(profileId.ifBlank { "شناسه هنوز ثبت نشده" }, style = MaterialTheme.typography.labelSmall)
                    }
                }
                OutlinedTextField(newMember, { newMember = it }, label = { Text("شناسه کاربری عضو") }, modifier = Modifier.fillMaxWidth())
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf("عضو", "دستیار مدیر").forEach { role ->
                        FilterChip(selected = selectedRole == role, onClick = { selectedRole = role }, label = { Text(role) })
                    }
                }
                Button(onClick = {
                    val id = newMember.trim()
                    if (groupName.isBlank()) status = "ابتدا یک گروه بسازید."
                    else if (id.isBlank() || id == profileId) status = "شناسه معتبر وارد کنید."
                    else {
                        members = members.filterNot { it.first == id } + (id to selectedRole)
                        store.saveMembers(members)
                        newMember = ""
                        status = "عضو در فهرست محلی افزوده شد."
                    }
                }) { Text("افزودن با شناسه کاربری") }
                members.forEach { (id, role) ->
                    ListItem(headlineContent = { Text(id) }, supportingContent = { Text(role) },
                        leadingContent = {
                            Surface(shape = CircleShape, color = MaterialTheme.colorScheme.secondaryContainer) {
                                Text(id.take(1).uppercase(), modifier = Modifier.padding(12.dp))
                            }
                        },
                        trailingContent = {
                            TextButton(onClick = {
                                members = members.filterNot { it.first == id }
                                store.saveMembers(members)
                            }) { Text("حذف") }
                        })
                }
                Text("اشتراک میان دستگاه‌ها هنوز به سرویس گروهی متصل نیست. عضویت فعلاً فقط روی این دستگاه ثبت می‌شود.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
            }
            2 -> {
                if (avatar != null) Image(avatar, contentDescription = "تصویر پروفایل", contentScale = ContentScale.Crop, modifier = Modifier.size(88.dp).clip(CircleShape))
                else Surface(shape = CircleShape) { Text("👤", modifier = Modifier.padding(20.dp)) }
                Button(onClick = { picker.launch("image/*") }) { Text("انتخاب و ویرایش عکس پروفایل") }
                if (pendingError.isNotBlank()) Text(pendingError, color = MaterialTheme.colorScheme.error)
                OutlinedTextField(profileName, { profileName = it }, label = { Text("نام نمایشی") }, modifier = Modifier.fillMaxWidth())
                OutlinedTextField(profileId, { profileId = it }, label = { Text("شناسه کاربری دلخواه") }, modifier = Modifier.fillMaxWidth())
                Button(onClick = {
                    if (profileName.isNotBlank() && profileId.matches(Regex("[A-Za-z0-9_]{4,32}"))) {
                        store.set("profile_name", profileName.trim())
                        store.set("profile_id", profileId)
                        status = "پروفایل روی دستگاه ذخیره شد."
                    } else status = "نام و شناسه ۴ تا ۳۲ کاراکتری لاتین وارد کنید."
                }) { Text("ذخیره پروفایل") }
                Text("شناسه فعلاً محلی است؛ یکتایی آن در سرور تأیید نمی‌شود.", style = MaterialTheme.typography.bodySmall)
            }
        }
        if (activeSection != 0) {
            Text("یادآوری برنامه‌های گروه (پس از اتصال همگام‌سازی)", style = MaterialTheme.typography.labelMedium)
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                listOf("10", "15", "30", "60").forEach { value ->
                    FilterChip(selected = alertMinutes == value, onClick = {
                        alertMinutes = value
                        store.set("alert_minutes", value)
                    }, label = { Text("$value دقیقه") })
                }
            }
        }
        if (status.isNotBlank()) Text(status)
    }
}
