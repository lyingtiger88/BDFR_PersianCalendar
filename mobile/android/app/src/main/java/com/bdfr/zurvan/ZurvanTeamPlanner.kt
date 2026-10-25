package com.bdfr.zurvan

import android.content.Context
import android.graphics.BitmapFactory
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
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
    var alertMinutes by remember { mutableStateOf(store.get("alert_minutes").ifBlank { "30" }) }
    var activeSection by remember { mutableStateOf(0) }
    var status by remember { mutableStateOf("") }
    val avatar = remember(photoRevision) {
        val f = File(context.filesDir, "zurvan_profile_photo")
        if (f.exists()) BitmapFactory.decodeFile(f.absolutePath)?.asImageBitmap() else null
    }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) runCatching {
            context.contentResolver.openInputStream(uri)?.use { input ->
                File(context.filesDir, "zurvan_profile_photo").outputStream().use { output -> input.copyTo(output) }
            }
        }.onSuccess { photoRevision++ }
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
                Text("جدول هفتگی · برای دیدن روزها به چپ و راست بکشید", style = MaterialTheme.typography.titleMedium)
                Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                    dates.forEachIndexed { index, date ->
                        val persian = android.icu.util.Calendar.getInstance(android.icu.util.ULocale("fa_IR@calendar=persian")).apply {
                            timeInMillis = date.atStartOfDay(java.time.ZoneId.systemDefault()).toInstant().toEpochMilli()
                        }
                        val key = "%04d-%02d-%02d".format(persian.get(android.icu.util.Calendar.YEAR),
                            persian.get(android.icu.util.Calendar.MONTH) + 1, persian.get(android.icu.util.Calendar.DAY_OF_MONTH))
                        val dayPlans = plans.filter { it.dateKey == key }.sortedWith(compareBy({ it.hour }, { it.minute }))
                        Card(modifier = Modifier.width(142.dp).heightIn(min = 290.dp),
                            colors = CardDefaults.cardColors(containerColor =
                                listOf(0xFFF8DDD4,0xFFFFF0C5,0xFFDFF0DD,0xFFD5EFED,0xFFDCE9FB,0xFFE8E0FA,0xFFF8DDE7)[index].let {
                                    androidx.compose.ui.graphics.Color(it)
                                })) {
                            Column(Modifier.padding(9.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                Text(listOf("شنبه","یکشنبه","دوشنبه","سه‌شنبه","چهارشنبه","پنجشنبه","جمعه")[index],
                                    fontWeight = FontWeight.Bold)
                                Text(key, style = MaterialTheme.typography.labelSmall)
                                if (dayPlans.isEmpty()) Text("بدون برنامه", style = MaterialTheme.typography.labelSmall)
                                dayPlans.forEach { plan ->
                                    ElevatedCard(shape = RoundedCornerShape(10.dp)) {
                                        Column(Modifier.padding(8.dp)) {
                                            Text(plan.title, fontWeight = FontWeight.SemiBold)
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
                Text("مدیر گروه: ${profileName.ifBlank { "پروفایل من" }}", fontWeight = FontWeight.SemiBold)
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
                if (avatar != null) Image(avatar, contentDescription = "تصویر پروفایل", modifier = Modifier.size(88.dp))
                else Surface(shape = CircleShape) { Text("👤", modifier = Modifier.padding(20.dp)) }
                Button(onClick = { picker.launch("image/*") }) { Text("انتخاب عکس پروفایل") }
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
