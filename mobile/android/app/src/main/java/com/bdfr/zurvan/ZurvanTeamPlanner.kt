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
import kotlinx.coroutines.launch
import kotlinx.coroutines.delay

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
    plans: List<LocalPlan>,
    onTogglePlan: (String) -> Unit
) {
    val context = LocalContext.current
    val store = remember { TeamStore(context) }
    val collaborationApi = remember { ZurvanCollaborationApi() }
    val secureStore = remember { ZurvanCloudSecureStore(context) }
    val sessionToken = secureStore.get(ZurvanCloudSecureStore.DESK_SESSION_TOKEN).orEmpty()
    val scope = rememberCoroutineScope()
    var remoteGroupId by remember { mutableStateOf(store.get("remote_group_id")) }
    var remoteSequence by remember { mutableLongStateOf(store.get("remote_sequence").toLongOrNull() ?: 0L) }
    var remoteMembers by remember { mutableStateOf<List<CollabMember>>(emptyList()) }
    var sharedTaskTitle by remember { mutableStateOf("") }
    var sharedMessage by remember { mutableStateOf("") }
    var sharedItems by remember { mutableStateOf<List<String>>(emptyList()) }
    var sharedMessages by remember { mutableStateOf<List<String>>(emptyList()) }
    var pendingInvites by remember { mutableStateOf<List<Pair<String,String>>>(emptyList()) }
    var remoteSyncStatus by remember { mutableStateOf("همگام‌سازی گروه هنوز انجام نشده است.") }
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
                ZurvanPlanningViews(
                    plans = plans,
                    onAddSchedule = onAddSchedule,
                    onToggleCompleted = onTogglePlan
                )
            }
            1 -> {
                OutlinedTextField(groupName, { groupName = it }, label = { Text("نام گروه") }, modifier = Modifier.fillMaxWidth())
                var agendaEnabled by remember { mutableStateOf(true) }
                var kanbanEnabled by remember { mutableStateOf(true) }
                var schoolEnabled by remember { mutableStateOf(false) }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(selected=agendaEnabled,onClick={agendaEnabled=!agendaEnabled},label={Text("Agenda")})
                    FilterChip(selected=kanbanEnabled,onClick={kanbanEnabled=!kanbanEnabled},label={Text("Kanban")})
                    FilterChip(selected=schoolEnabled,onClick={schoolEnabled=!schoolEnabled},label={Text("مدرسه")})
                }
                Button(onClick = {
                    if (sessionToken.isBlank()) status = "برای ساخت گروه ابتدا وارد حساب Zurvan Desk شوید."
                    else if (groupName.isBlank() || !(agendaEnabled || kanbanEnabled || schoolEnabled)) status = "نام گروه و حداقل یک حالت کاری را انتخاب کنید."
                    else scope.launch {
                        runCatching { collaborationApi.createGroup(sessionToken,groupName.trim(),agendaEnabled,kanbanEnabled,schoolEnabled) }
                            .onSuccess { g ->
                                remoteGroupId=g.id;store.set("remote_group_id",g.id);store.set("group_name",g.name)
                                status="گروه آنلاین ساخته شد.";remoteSyncStatus="گروه به سرور متصل است."
                                remoteMembers=runCatching{collaborationApi.members(sessionToken,g.id)}.getOrDefault(emptyList())
                            }.onFailure { status=it.message ?: "ساخت گروه انجام نشد." }
                    }
                }) { Text("ساخت گروه آنلاین") }
                Text("شناسه حساب: ${secureStore.get(ZurvanCloudSecureStore.DESK_ACCOUNT_IDENTIFIER).orEmpty().ifBlank { "وارد نشده" }}")
                if(remoteGroupId.isNotBlank()) Text("شناسه گروه: $remoteGroupId",style=MaterialTheme.typography.labelSmall)
                OutlinedTextField(newMember, { newMember = it }, label = { Text("شناسه زروان عضو") }, modifier = Modifier.fillMaxWidth())
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf("عضو", "دستیار مدیر").forEach { role ->
                        FilterChip(selected = selectedRole == role, onClick = { selectedRole = role }, label = { Text(role) })
                    }
                }
                Button(onClick = {
                    val identifier=newMember.trim()
                    if(sessionToken.isBlank())status="ابتدا وارد حساب Zurvan Desk شوید."
                    else if(remoteGroupId.isBlank())status="ابتدا یک گروه آنلاین بسازید."
                    else if(identifier.isBlank())status="شناسه معتبر وارد کنید."
                    else scope.launch {
                        runCatching { collaborationApi.invite(sessionToken,remoteGroupId,identifier,selectedRole=="دستیار مدیر") }
                            .onSuccess { newMember="";status="دعوت برای حساب زروان ارسال شد." }
                            .onFailure { status=it.message ?: "ارسال دعوت انجام نشد." }
                    }
                }) { Text("ارسال دعوت آنلاین") }
                OutlinedTextField(sharedTaskTitle,{sharedTaskTitle=it},label={Text("عنوان کار مشترک")},modifier=Modifier.fillMaxWidth())
                Button(onClick={
                    if(sessionToken.isBlank()||remoteGroupId.isBlank()||sharedTaskTitle.isBlank())status="ابتدا وارد حساب شوید، گروه را انتخاب کنید و عنوان کار را بنویسید."
                    else scope.launch {
                        runCatching { collaborationApi.createItem(sessionToken,remoteGroupId,sharedTaskTitle.trim()) }
                            .onSuccess { sharedTaskTitle="";status="کار مشترک ثبت شد." }
                            .onFailure { status=it.message ?: "ثبت کار انجام نشد." }
                    }
                }) { Text("ثبت کار مشترک آنلاین") }
                OutlinedTextField(sharedMessage,{sharedMessage=it},label={Text("پیام گروه")},modifier=Modifier.fillMaxWidth())
                Button(onClick={
                    if(sessionToken.isBlank()||remoteGroupId.isBlank()||sharedMessage.isBlank())status="متن پیام را وارد کنید."
                    else scope.launch {
                        runCatching { collaborationApi.sendMessage(sessionToken,remoteGroupId,sharedMessage.trim()) }
                            .onSuccess { sharedMessage="";status="پیام ارسال شد." }
                            .onFailure { status=it.message ?: "ارسال پیام انجام نشد." }
                    }
                }) { Text("ارسال پیام گروه") }
                Button(onClick={
                    if(sessionToken.isBlank())status="ابتدا وارد حساب شوید."
                    else scope.launch {
                        runCatching {
                            val inv=collaborationApi.invitations(sessionToken)
                            pendingInvites=(0 until inv.length()).map { val x=inv.getJSONObject(it); x.getString("id") to "دعوت گروه: "+x.optJSONObject("collab_groups")?.optString("name").orEmpty() }
                            if(pendingInvites.isEmpty())status="دعوت جدیدی ندارید."
                        }.onFailure { status=it.message ?: "دریافت دعوت‌ها انجام نشد." }
                    }
                }) { Text("بررسی دعوت‌های دریافتی") }
                pendingInvites.forEach { (inviteId,label) ->
                    Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                        Text(label,modifier=Modifier.weight(1f))
                        TextButton(onClick={
                            scope.launch { runCatching { collaborationApi.accept(sessionToken,inviteId) }
                                .onSuccess { status="عضویت در گروه پذیرفته شد." }
                                .onFailure { status=it.message ?: "پذیرش دعوت انجام نشد." } }
                        }) { Text("پذیرش") }
                    }
                }
                Text("اعضای گروه",fontWeight=FontWeight.SemiBold)
                remoteMembers.forEach { member ->
                    ListItem(headlineContent={Text(member.identifier)},supportingContent={Text(member.role)})
                }
                Text("کارهای مشترک",fontWeight=FontWeight.SemiBold)
                sharedItems.forEach { Text("• $it",style=MaterialTheme.typography.bodySmall) }
                Text("گفت‌وگوی گروه",fontWeight=FontWeight.SemiBold)
                sharedMessages.takeLast(8).forEach { Text(it,style=MaterialTheme.typography.bodySmall) }
                Text(remoteSyncStatus,style=MaterialTheme.typography.bodySmall)
                if(sessionToken.isBlank()) Text("برای استفاده از گروه آنلاین، ورود به حساب Zurvan Desk الزامی است.",color=MaterialTheme.colorScheme.error)
                LaunchedEffect(remoteGroupId,sessionToken,activeSection) {
                    if(remoteGroupId.isNotBlank() && sessionToken.isNotBlank() && activeSection==1) {
                        if(remoteGroupId.isBlank()) {
                            runCatching { collaborationApi.groups(sessionToken) }
                                .onSuccess { groups -> groups.firstOrNull()?.let { remoteGroupId=it.id;store.set("remote_group_id",it.id);groupName=it.name } }
                        }
                        while(remoteGroupId.isNotBlank()) {
                            runCatching {
                                val (events,next)=collaborationApi.changes(sessionToken,remoteGroupId,remoteSequence)
                                if(next>remoteSequence) {
                                    remoteSequence=next;store.set("remote_sequence",next.toString())
                                    remoteMembers=collaborationApi.members(sessionToken,remoteGroupId)
                                    val items=collaborationApi.items(sessionToken,remoteGroupId)
                                    sharedItems=(0 until items.length()).map { val x=items.getJSONObject(it); x.optString("title")+" • "+x.optString("state") }
                                    val messages=collaborationApi.messages(sessionToken,remoteGroupId)
                                    sharedMessages=(0 until messages.length()).map { val x=messages.getJSONObject(it); x.optString("body") }
                                    remoteSyncStatus="همگام‌سازی آنلاین فعال • آخرین تغییر: ${events.size}"
                                } else remoteSyncStatus="متصل • بدون تغییر جدید"
                            }.onFailure { remoteSyncStatus="اتصال موقتاً قطع است؛ تلاش مجدد انجام می‌شود." }
                            delay(3000)
                        }
                    }
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
