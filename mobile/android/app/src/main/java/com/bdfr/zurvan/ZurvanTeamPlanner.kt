package com.bdfr.zurvan

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
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
    private fun offlineQueueSecret(): SecretKey {
        val alias = "zurvan.collaboration.offline.v1"
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey(alias, null) as? SecretKey)?.let { return it }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        generator.init(KeyGenParameterSpec.Builder(alias,
            KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setRandomizedEncryptionRequired(true).build())
        return generator.generateKey()
    }
    private fun encryptQueue(value:String):String {
        val cipher=Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE,offlineQueueSecret())
        val encrypted=cipher.doFinal(value.toByteArray(Charsets.UTF_8))
        return Base64.encodeToString(cipher.iv+encrypted,Base64.NO_WRAP)
    }
    private fun decryptQueue(value:String):String {
        val raw=Base64.decode(value,Base64.NO_WRAP)
        require(raw.size>12) { "صف آفلاین رمزگذاری‌شده معتبر نیست." }
        val cipher=Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE,offlineQueueSecret(),GCMParameterSpec(128,raw.copyOfRange(0,12)))
        return String(cipher.doFinal(raw.copyOfRange(12,raw.size)),Charsets.UTF_8)
    }
    private fun offlineQueueText():String {
        val stored=prefs.getString("offline_commands","")?.takeIf{it.isNotBlank()} ?: return "[]"
        return try { decryptQueue(stored) } catch (e:Exception) {
            if(stored.trimStart().startsWith("[")) {
                prefs.edit().putString("offline_commands",encryptQueue(stored)).apply()
                stored
            } else throw e
        }
    }
    fun enqueueCommand(groupId:String,commandId:String,kind:String,payload:JSONObject) {
        val rows=JSONArray(offlineQueueText())
        if((0 until rows.length()).any { rows.getJSONObject(it).optString("commandId")==commandId }) return
        rows.put(JSONObject().put("groupId",groupId).put("commandId",commandId)
            .put("kind",kind).put("payload",payload).put("createdAt",System.currentTimeMillis()))
        prefs.edit().putString("offline_commands",encryptQueue(rows.toString())).apply()
    }
    fun queuedCommands():List<JSONObject> {
        val rows=JSONArray(prefs.getString("offline_commands","")?.takeIf{it.isNotBlank()}?.let(::decryptQueue) ?: "[]")
        return (0 until rows.length()).map { rows.getJSONObject(it) }
    }
    fun removeQueuedCommand(commandId:String) {
        val old=queuedCommands().filterNot { it.optString("commandId")==commandId }
        val rows=JSONArray();old.forEach{rows.put(it)}
        prefs.edit().putString("offline_commands",encryptQueue(rows.toString())).apply()
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
    var agendaRecurrence by remember { mutableStateOf<String?>(null) }
    var permissionMemberId by remember { mutableStateOf<String?>(null) }
    var permissionChat by remember { mutableStateOf(true) }
    var permissionReport by remember { mutableStateOf(true) }
    var permissionEdit by remember { mutableStateOf(false) }
    var kanbanCards by remember { mutableStateOf<List<JSONObject>>(emptyList()) }
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
                        val commandId=UUID.randomUUID().toString()
                        val payload=JSONObject().put("title",sharedTaskTitle.trim()).put("mode","agenda").put("state","todo").put("timezone",java.util.TimeZone.getDefault().id);agendaRecurrence?.let{payload.put("recurrenceRule",it)}
                        runCatching { collaborationApi.replayCommand(sessionToken,remoteGroupId,commandId,"create_item",payload) }
                            .onSuccess { sharedTaskTitle="";status="کار مشترک ثبت شد." }
                            .onFailure { store.enqueueCommand(remoteGroupId,commandId,"create_item",payload);sharedTaskTitle="";status="کار در صف آفلاین ذخیره شد و پس از اتصال ارسال می‌شود." }
                    }
                }) { Text("ثبت کار مشترک آنلاین") }
                OutlinedTextField(sharedMessage,{sharedMessage=it},label={Text("پیام گروه")},modifier=Modifier.fillMaxWidth())
                Button(onClick={
                    if(sessionToken.isBlank()||remoteGroupId.isBlank()||sharedMessage.isBlank())status="متن پیام را وارد کنید."
                    else scope.launch {
                        val commandId=UUID.randomUUID().toString()
                        val payload=JSONObject().put("body",sharedMessage.trim())
                        runCatching { collaborationApi.replayCommand(sessionToken,remoteGroupId,commandId,"send_message",payload) }
                            .onSuccess { sharedMessage="";status="پیام ارسال شد." }
                            .onFailure { store.enqueueCommand(remoteGroupId,commandId,"send_message",payload);sharedMessage="";status="پیام در صف آفلاین ذخیره شد و پس از اتصال ارسال می‌شود." }
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

                Text("تکرار برنامه‌های Agenda",fontWeight=FontWeight.SemiBold)
                Row(horizontalArrangement=Arrangement.spacedBy(6.dp)) {
                    listOf("بدون تکرار" to null,"روزانه" to "daily","روزهای کاری" to "weekdays","هفتگی" to "weekly","ماهانه" to "monthly").forEach { (label,value) ->
                        FilterChip(selected=agendaRecurrence==value,onClick={agendaRecurrence=value},label={Text(label)})
                    }
                }
                var kanbanColumns by remember { mutableStateOf<List<Pair<String,String>>>(emptyList()) }
                var kanbanColumnName by remember { mutableStateOf("") }
                var kanbanTaskTitle by remember { mutableStateOf("") }
                var selectedKanbanColumn by remember { mutableStateOf<String?>(null) }
                Text("برد Kanban",fontWeight=FontWeight.SemiBold)
                OutlinedTextField(kanbanColumnName,{kanbanColumnName=it},label={Text("نام ستون جدید")},modifier=Modifier.fillMaxWidth())
                Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick={
                        scope.launch { runCatching { collaborationApi.kanbanColumns(sessionToken,remoteGroupId) }
                            .onSuccess { a ->
                                kanbanColumns=(0 until a.length()).map { val x=a.getJSONObject(it);x.getString("id") to x.getString("name") }
                                if(selectedKanbanColumn==null)selectedKanbanColumn=kanbanColumns.firstOrNull()?.first
                                scope.launch { runCatching { collaborationApi.items(sessionToken,remoteGroupId) }
                                    .onSuccess { all -> kanbanCards=(0 until all.length()).map{all.getJSONObject(it)}.filter{it.optString("mode")=="kanban"} }
                                    .onFailure { status=it.message ?: "بارگذاری کارت‌ها ناموفق بود." } }
                            }
                            .onFailure { status=it.message ?: "بارگذاری ستون‌ها ناموفق بود." } }
                    }) { Text("بارگذاری ستون‌ها") }
                    Button(onClick={
                        if(kanbanColumnName.isBlank())status="نام ستون را وارد کنید."
                        else scope.launch { runCatching { collaborationApi.createKanbanColumn(sessionToken,remoteGroupId,kanbanColumnName.trim()) }
                            .onSuccess { kanbanColumnName="";val a=collaborationApi.kanbanColumns(sessionToken,remoteGroupId);kanbanColumns=(0 until a.length()).map { val x=a.getJSONObject(it);x.getString("id") to x.getString("name") };status="ستون ایجاد شد." }
                            .onFailure { status=it.message ?: "ایجاد ستون ناموفق بود." } }
                    }) { Text("افزودن ستون") }
                }
                kanbanColumns.forEachIndexed { index, column ->
                    Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(6.dp)) {
                        FilterChip(selected=selectedKanbanColumn==column.first,onClick={selectedKanbanColumn=column.first},label={Text(column.second)})
                        if(index>0) TextButton(onClick={
                            scope.launch { val ids=kanbanColumns.map{it.first}.toMutableList();val v=ids.removeAt(index);ids.add(index-1,v)
                                runCatching { collaborationApi.reorderKanbanColumns(sessionToken,remoteGroupId,ids) }
                                    .onSuccess { val a=collaborationApi.kanbanColumns(sessionToken,remoteGroupId);kanbanColumns=(0 until a.length()).map { val x=a.getJSONObject(it);x.getString("id") to x.getString("name") } }
                                    .onFailure { status=it.message ?: "تغییر ترتیب ناموفق بود." } }
                        }) { Text("↑") }
                        if(index<kanbanColumns.lastIndex) TextButton(onClick={
                            scope.launch { val ids=kanbanColumns.map{it.first}.toMutableList();val v=ids.removeAt(index);ids.add(index+1,v)
                                runCatching { collaborationApi.reorderKanbanColumns(sessionToken,remoteGroupId,ids) }
                                    .onSuccess { val a=collaborationApi.kanbanColumns(sessionToken,remoteGroupId);kanbanColumns=(0 until a.length()).map { val x=a.getJSONObject(it);x.getString("id") to x.getString("name") } }
                                    .onFailure { status=it.message ?: "تغییر ترتیب ناموفق بود." } }
                        }) { Text("↓") }
                    }
                }
                Text("کارت‌های برد",fontWeight=FontWeight.SemiBold)
                kanbanCards.forEach { card ->
                    val currentColumn=card.optString("column_id")
                    val currentIndex=kanbanColumns.indexOfFirst{it.first==currentColumn}
                    Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(6.dp)) {
                        Text(card.optString("title")+" • "+card.optString("state"),modifier=Modifier.weight(1f))
                        if(currentIndex>=0&&currentIndex<kanbanColumns.lastIndex) TextButton(onClick={
                            scope.launch { runCatching {
                                collaborationApi.updatePlanningItem(sessionToken,remoteGroupId,card.getString("id"),
                                    card.optInt("version",1),card.optString("title"),card.optString("details"),
                                    card.optString("state","todo"),columnId=kanbanColumns[currentIndex+1].first)
                                val all=collaborationApi.items(sessionToken,remoteGroupId)
                                kanbanCards=(0 until all.length()).map{all.getJSONObject(it)}.filter{it.optString("mode")=="kanban"}
                            }.onFailure { status=it.message ?: "انتقال کارت ناموفق بود." } }
                        }) { Text("انتقال ←") }
                    }
                }
                OutlinedTextField(kanbanTaskTitle,{kanbanTaskTitle=it},label={Text("عنوان کارت جدید")},modifier=Modifier.fillMaxWidth())
                Button(onClick={
                    if(selectedKanbanColumn==null||kanbanTaskTitle.isBlank())status="ستون و عنوان کارت را انتخاب کنید."
                    else scope.launch { runCatching { collaborationApi.createItemAdvanced(sessionToken,remoteGroupId,kanbanTaskTitle.trim(),"kanban",columnId=selectedKanbanColumn) }
                        .onSuccess { kanbanTaskTitle="";status="کارت به برد اضافه شد." }
                        .onFailure { status=it.message ?: "ثبت کارت ناموفق بود." } }
                }) { Text("افزودن کارت به ستون") }
                var subjectName by remember { mutableStateOf("") }
                var schoolSubjects by remember { mutableStateOf<List<Pair<String,String>>>(emptyList()) }
                var homeworkTitle by remember { mutableStateOf("") }
                var homeworkInstructions by remember { mutableStateOf("") }
                var schoolHomework by remember { mutableStateOf<List<Pair<String,String>>>(emptyList()) }
                var schoolTimetable by remember { mutableStateOf<List<JSONObject>>(emptyList()) }
                var gradeValue by remember { mutableStateOf("0") }
                var gradeFeedback by remember { mutableStateOf("") }
                var submissionText by remember { mutableStateOf("") }
                Text("محیط مدرسه",fontWeight=FontWeight.SemiBold)
                OutlinedTextField(subjectName,{subjectName=it},label={Text("نام درس")},modifier=Modifier.fillMaxWidth())
                Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                    Button(onClick={
                        if(subjectName.isBlank())status="نام درس را وارد کنید."
                        else scope.launch { runCatching { collaborationApi.createSubject(sessionToken,remoteGroupId,subjectName.trim()) }
                            .onSuccess { subjectName="";status="درس ثبت شد." }
                            .onFailure { status=it.message ?: "ثبت درس ناموفق بود." } }
                    }) { Text("افزودن درس") }
                    OutlinedButton(onClick={
                        scope.launch { runCatching {
                            val a=collaborationApi.subjects(sessionToken,remoteGroupId)
                            schoolSubjects=(0 until a.length()).map { val x=a.getJSONObject(it);x.getString("id") to x.getString("name") }
                            val h=collaborationApi.homework(sessionToken,remoteGroupId)
                            schoolHomework=(0 until h.length()).map { val x=h.getJSONObject(it);x.getString("id") to x.getString("title") }
                            val t=collaborationApi.timetable(sessionToken,remoteGroupId)
                            schoolTimetable=(0 until t.length()).map{t.getJSONObject(it)}
                        }.onFailure { status=it.message ?: "بارگذاری مدرسه ناموفق بود." } }
                    }) { Text("بارگذاری مدرسه") }
                }
                schoolSubjects.forEach { (_,name) -> Text("درس: $name") }
                schoolSubjects.firstOrNull()?.let { (subjectId,subjectTitle) ->
                    Text("برنامه هفتگی • $subjectTitle",fontWeight=FontWeight.Medium)
                    Button(onClick={
                        scope.launch { runCatching { collaborationApi.createTimetableSlot(sessionToken,remoteGroupId,subjectId,0,"08:00","09:00") }
                            .onSuccess { status="جلسه هفتگی ثبت شد.";val t=collaborationApi.timetable(sessionToken,remoteGroupId);schoolTimetable=(0 until t.length()).map{t.getJSONObject(it)} }
                            .onFailure { status=it.message ?: "ثبت جلسه ناموفق بود." } }
                    }) { Text("افزودن جلسه دوشنبه ۸ تا ۹") }
                }
                schoolTimetable.forEach { slot -> Text("روز "+slot.optInt("weekday")+" • "+slot.optString("start_time")+" تا "+slot.optString("end_time")) }
                OutlinedTextField(homeworkTitle,{homeworkTitle=it},label={Text("عنوان تکلیف")},modifier=Modifier.fillMaxWidth())
                OutlinedTextField(homeworkInstructions,{homeworkInstructions=it},label={Text("شرح تکلیف")},modifier=Modifier.fillMaxWidth())
                Button(onClick={
                    val subject=schoolSubjects.firstOrNull()?.first
                    if(subject==null||homeworkTitle.isBlank())status="ابتدا درس بسازید/بارگذاری کنید و عنوان تکلیف را بنویسید."
                    else scope.launch { runCatching { collaborationApi.createHomework(sessionToken,remoteGroupId,subject,homeworkTitle.trim(),homeworkInstructions) }
                        .onSuccess { homeworkTitle="";homeworkInstructions="";status="تکلیف ثبت شد." }
                        .onFailure { status=it.message ?: "ثبت تکلیف ناموفق بود." } }
                }) { Text("ثبت تکلیف برای اولین درس") }
                schoolHomework.forEach { (id,title) ->
                    Text(title,fontWeight=FontWeight.Medium)
                    OutlinedTextField(submissionText,{submissionText=it},label={Text("پاسخ این تکلیف")},modifier=Modifier.fillMaxWidth())
                    Button(onClick={
                        if(submissionText.isBlank())status="متن پاسخ را وارد کنید."
                        else scope.launch { runCatching { collaborationApi.submitHomework(sessionToken,id,submissionText) }
                            .onSuccess { submissionText="";status="پاسخ ارسال شد." }
                            .onFailure { status=it.message ?: "ارسال پاسخ ناموفق بود." } }
                    }) { Text("ارسال پاسخ") }
                }
                Text("اعضای گروه",fontWeight=FontWeight.SemiBold)
                remoteMembers.forEach { member ->
                    ListItem(headlineContent={Text(member.identifier)},supportingContent={Text(member.role)},
                        trailingContent={
                            Row {
                                TextButton(onClick={
                                    permissionMemberId=member.accountId
                                    permissionChat=member.canChat;permissionReport=member.canReport;permissionEdit=member.canEdit
                                }) { Text("مجوزها") }
                                if(member.role!="owner") TextButton(onClick={
                                    scope.launch { runCatching { collaborationApi.removeMember(sessionToken,remoteGroupId,member.accountId);remoteMembers=collaborationApi.members(sessionToken,remoteGroupId);status="عضو از گروه حذف شد." }
                                        .onFailure { status=it.message ?: "حذف عضو ناموفق بود." } }
                                }) { Text("حذف") }
                            }
                        })
                }
                if(permissionMemberId!=null) {
                    Text("تنظیم مجوز عضو",fontWeight=FontWeight.SemiBold)
                    Row(verticalAlignment=Alignment.CenterVertically) { Checkbox(permissionChat,{permissionChat=it});Text("گفت‌وگو") }
                    Row(verticalAlignment=Alignment.CenterVertically) { Checkbox(permissionReport,{permissionReport=it});Text("گزارش") }
                    Row(verticalAlignment=Alignment.CenterVertically) { Checkbox(permissionEdit,{permissionEdit=it});Text("ویرایش") }
                    Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                        Button(onClick={
                            val id=permissionMemberId ?: return@Button
                            scope.launch { runCatching { collaborationApi.setPermissions(sessionToken,remoteGroupId,id,permissionChat,permissionReport,permissionEdit);remoteMembers=collaborationApi.members(sessionToken,remoteGroupId);permissionMemberId=null;status="مجوزها ذخیره شد." }
                                .onFailure { status=it.message ?: "ذخیره مجوزها ناموفق بود." } }
                        }) { Text("ذخیره مجوزها") }
                        TextButton(onClick={permissionMemberId=null}) { Text("انصراف") }
                    }
                }
                Text("کارهای مشترک",fontWeight=FontWeight.SemiBold)
                sharedItems.forEach { Text("• $it",style=MaterialTheme.typography.bodySmall) }
                Text("گفت‌وگوی گروه",fontWeight=FontWeight.SemiBold)
                sharedMessages.takeLast(8).forEach { Text(it,style=MaterialTheme.typography.bodySmall) }
                OutlinedTextField(gradeValue,{gradeValue=it},label={Text("نمره ۰ تا ۱۰۰")},modifier=Modifier.fillMaxWidth())
                OutlinedTextField(gradeFeedback,{gradeFeedback=it},label={Text("بازخورد معلم")},modifier=Modifier.fillMaxWidth())
                Button(onClick={
                    val homeworkId=schoolHomework.firstOrNull()?.first
                    val grade=gradeValue.toDoubleOrNull()
                    if(homeworkId==null||grade==null||grade !in 0.0..100.0)status="تکلیف و نمره معتبر انتخاب کنید."
                    else scope.launch { runCatching {
                        val a=collaborationApi.submissions(sessionToken,homeworkId)
                        if(a.length()==0)error("هنوز پاسخی ثبت نشده است.")
                        val sub=a.getJSONObject(0)
                        collaborationApi.gradeSubmission(sessionToken,homeworkId,sub.getString("id"),grade,gradeFeedback)
                    }.onSuccess { status="نمره و بازخورد ثبت شد." }
                        .onFailure { status=it.message ?: "ثبت نمره ناموفق بود." } }
                }) { Text("تصحیح اولین پاسخ تکلیف") }
                Text(remoteSyncStatus,style=MaterialTheme.typography.bodySmall)
                if(sessionToken.isBlank()) Text("برای استفاده از گروه آنلاین، ورود به حساب Zurvan Desk الزامی است.",color=MaterialTheme.colorScheme.error)
                LaunchedEffect(remoteGroupId,sessionToken,activeSection) {
                    if(sessionToken.isNotBlank() && activeSection==1) {
                        if(remoteGroupId.isBlank()) {
                            runCatching { collaborationApi.groups(sessionToken) }
                                .onSuccess { groups -> groups.firstOrNull()?.let { remoteGroupId=it.id;store.set("remote_group_id",it.id);groupName=it.name } }
                        }
                        while(remoteGroupId.isNotBlank()) {
                            for (queued in store.queuedCommands().filter { it.optString("groupId")==remoteGroupId }.take(20)) {
                                val replay=runCatching {
                                    collaborationApi.replayCommand(sessionToken,remoteGroupId,queued.getString("commandId"),
                                        queued.getString("kind"),queued.getJSONObject("payload"))
                                }
                                if(replay.isSuccess) store.removeQueuedCommand(queued.getString("commandId")) else break
                            }
                            runCatching {
                                val (events,next)=collaborationApi.changes(sessionToken,remoteGroupId,remoteSequence)
                                if(next>remoteSequence) {
                                    remoteSequence=next;store.set("remote_sequence",next.toString())
                                    remoteMembers=collaborationApi.members(sessionToken,remoteGroupId)
                                    val items=collaborationApi.items(sessionToken,remoteGroupId)
                                    sharedItems=(0 until items.length()).map { val x=items.getJSONObject(it); val rule=x.optString("recurrence_rule"); val occurrences=x.optJSONArray("occurrences"); val next=occurrences?.optJSONObject(0)?.optString("at").orEmpty(); x.optString("title")+" • "+x.optString("state")+(if(rule.isNotBlank()&&rule!="null")" • تکرار: $rule"+(if(next.isNotBlank())" • $next" else "") else "") }
                                    val messages=collaborationApi.messages(sessionToken,remoteGroupId)
                                    sharedMessages=(0 until messages.length()).map { val x=messages.getJSONObject(it); x.optString("body") }
                                    remoteSyncStatus="همگام‌سازی آنلاین فعال • آخرین تغییر: ${events.size}"
                                } else remoteSyncStatus="متصل • بدون تغییر جدید"
                            }.onFailure { remoteSyncStatus="اتصال موقتاً قطع است؛ تلاش مجدد انجام می‌شود." }
                            delay(250)
                        }
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
