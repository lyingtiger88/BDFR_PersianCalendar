package com.bdfr.zurvan

import android.content.Context
import android.icu.util.Calendar
import android.icu.util.ULocale
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items as gridItems
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import java.time.LocalDate
import java.time.ZoneId

/**
 * Local, multi-plan planner. Views are projections of existing LocalPlan records.
 * No remote groups, membership, or cloud writes are performed here.
 */
@Composable
fun ZurvanPlanningViews(
    plans: List<LocalPlan>,
    onAddSchedule: () -> Unit,
    onToggleCompleted: (String) -> Unit
) {
    val context = LocalContext.current
    val prefs = remember(context) { context.getSharedPreferences("zurvan_planning_views", Context.MODE_PRIVATE) }
    val modes = listOf("هفتگی", "ماهانه", "سالانه", "جدول", "برد")
    var mode by remember { mutableStateOf(prefs.getString("mode", "هفتگی") ?: "هفتگی") }
    var kind by remember { mutableStateOf("همه") }
    var weekOffset by remember { mutableIntStateOf(0) }
    val initial = remember { AndroidCalendarEngine.currentPersianYearMonth() }
    var year by remember { mutableIntStateOf(initial.first) }
    var month by remember { mutableIntStateOf(initial.second) }
    var selectedKey by remember { mutableStateOf<String?>(null) }
    val visible = remember(plans,kind) {
        plans.filter { kind == "همه" || (kind == "رویداد" && it.kind == "event") ||
            (kind == "کار" && it.kind == "task") }
    }
    val chronological = remember(visible) { visible.sortedWith(compareBy<LocalPlan>({ it.dateKey }, { it.hour }, { it.minute })) }
    Column(verticalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
        Text("نماهای برنامه‌ریزی", fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.titleMedium)
        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(5.dp)) {
            modes.forEach { name ->
                FilterChip(selected = mode == name, onClick = {
                    mode = name
                    prefs.edit().putString("mode",name).apply()
                    selectedKey = null
                }, label = { Text(name) })
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            listOf("همه", "کار", "رویداد").forEach { name ->
                FilterChip(selected = kind == name, onClick = { kind = name }, label = { Text(name) })
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Text("${visible.size} برنامه", style = MaterialTheme.typography.labelLarge)
            Button(onClick = onAddSchedule) { Text("افزودن برنامه") }
        }
        if (mode in listOf("هفتگی","ماهانه","سالانه")) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = {
                    when (mode) {
                        "هفتگی" -> weekOffset--
                        "ماهانه" -> { val p = AndroidCalendarEngine.shiftMonth(year,month,-1); year=p.first; month=p.second }
                        else -> year--
                    }
                    selectedKey = null
                }) { Text("قبلی") }
                Text(when(mode) {
                    "هفتگی" -> if(weekOffset==0) "هفته جاری" else "هفته ${if(weekOffset>0) "+" else ""}$weekOffset"
                    "ماهانه" -> "${persianMonthName(month)} $year"
                    else -> "سال $year"
                }, fontWeight = FontWeight.SemiBold)
                TextButton(onClick = {
                    when(mode) {
                        "هفتگی" -> weekOffset++
                        "ماهانه" -> { val p = AndroidCalendarEngine.shiftMonth(year,month,1); year=p.first; month=p.second }
                        else -> year++
                    }
                    selectedKey = null
                }) { Text("بعدی") }
                TextButton(onClick = {
                    val p=AndroidCalendarEngine.currentPersianYearMonth()
                    year=p.first; month=p.second; weekOffset=0; selectedKey=null
                }) { Text("امروز") }
            }
        }
        when (mode) {
            "هفتگی" -> {
                val today = remember { LocalDate.now() }
                val saturday = remember(weekOffset) {
                    today.minusDays(((today.dayOfWeek.value + 1) % 7).toLong()).plusWeeks(weekOffset.toLong())
                }
                val keys=(0..6).map { persianKey(saturday.plusDays(it.toLong())) }
                val weekdayNames=listOf("ش","ی","د","س","چ","پ","ج")
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(3.dp)) {
                    keys.forEachIndexed { i,key ->
                        val rows=visible.filter { it.dateKey==key }
                        ElevatedCard(
                            modifier=Modifier.weight(1f).clickable { selectedKey=key },
                            colors=CardDefaults.elevatedCardColors(containerColor=if(selectedKey==key)
                                MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant)
                        ) {
                            Column(Modifier.fillMaxWidth().padding(vertical=12.dp, horizontal=2.dp),
                                horizontalAlignment=Alignment.CenterHorizontally) {
                                Text(weekdayNames[i], style=MaterialTheme.typography.labelSmall)
                                Text(key.takeLast(2), style=MaterialTheme.typography.labelSmall)
                                Text(rows.size.toString(), fontWeight=FontWeight.Bold)
                            }
                        }
                    }
                }
                Text("برای دیدن چند برنامه یک روز، روی همان روز بزنید.", style=MaterialTheme.typography.bodySmall)
                val current=selectedKey?.takeIf { it in keys } ?: keys[((today.dayOfWeek.value+1)%7)]
                PlanRows(visible.filter { it.dateKey==current }.sortedWith(compareBy({it.hour},{it.minute})), onToggleCompleted)
            }
            "ماهانه" -> {
                val model=remember(year,month) { AndroidCalendarEngine.buildPersianMonth(year,month) }
                val days=model.days.filter { it.persianYear == year && it.persianMonth == month }
                val leading=model.leadingBlankCount
                val cells: List<String?>=List(leading) { null } + days.map { it.persianKey }
                Text("در هر خانه تعداد برنامه‌ها نمایش داده می‌شود؛ روز را برای مشاهده انتخاب کنید.",
                    style=MaterialTheme.typography.bodySmall)
                LazyVerticalGrid(columns=GridCells.Fixed(7), modifier=Modifier.height(330.dp),
                    horizontalArrangement=Arrangement.spacedBy(3.dp), verticalArrangement=Arrangement.spacedBy(3.dp)) {
                    gridItems(cells) { key ->
                        if(key == null) Spacer(Modifier.height(43.dp))
                        else {
                            val count=visible.count { it.dateKey==key }
                            Card(modifier=Modifier.fillMaxWidth().height(43.dp).clickable { selectedKey=key },
                                border=if(selectedKey==key) BorderStroke(1.dp,MaterialTheme.colorScheme.primary) else null,
                                colors=CardDefaults.cardColors(containerColor=if(count>0) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant)) {
                                Column(Modifier.fillMaxSize().padding(3.dp), horizontalAlignment=Alignment.CenterHorizontally) {
                                    Text(key.takeLast(2).toIntOrNull()?.toString() ?: "", style=MaterialTheme.typography.labelSmall)
                                    if(count>0) Text("$count برنامه", style=MaterialTheme.typography.labelSmall, maxLines=1)
                                }
                            }
                        }
                    }
                }
                selectedKey?.let { key ->
                    Text("برنامه‌های $key", fontWeight=FontWeight.SemiBold)
                    PlanRows(visible.filter { it.dateKey==key }.sortedWith(compareBy({it.hour},{it.minute})),onToggleCompleted)
                }
            }
            "سالانه" -> {
                Text("نمای کلی دوازده ماه؛ برای ورود به جزئیات یک ماه را انتخاب کنید.",
                    style=MaterialTheme.typography.bodySmall)
                (0..3).forEach { row ->
                    Row(Modifier.fillMaxWidth(), horizontalArrangement=Arrangement.spacedBy(6.dp)) {
                        (1..3).forEach { column ->
                            val m=row*3+column
                            val prefix="%04d-%02d-".format(java.util.Locale.US,year,m)
                            val count=visible.count { it.dateKey.startsWith(prefix) }
                            ElevatedCard(onClick = { month=m; mode="ماهانه"; prefs.edit().putString("mode",mode).apply(); selectedKey=null },
                                modifier=Modifier.weight(1f)) {
                                Column(Modifier.fillMaxWidth().padding(10.dp), horizontalAlignment=Alignment.CenterHorizontally) {
                                    Text(persianMonthName(m), style=MaterialTheme.typography.labelMedium)
                                    Text("$count برنامه", fontWeight=FontWeight.SemiBold)
                                }
                            }
                        }
                    }
                }
            }
            "جدول" -> {
                Text("نمای مرتب‌شده بر اساس تاریخ؛ کارها و رویدادهای متعدد در هر روز حفظ می‌شوند.",
                    style=MaterialTheme.typography.bodySmall)
                if(chronological.isEmpty()) Text("برنامه‌ای ثبت نشده است.")
                else LazyColumn(modifier=Modifier.heightIn(max=550.dp), verticalArrangement=Arrangement.spacedBy(4.dp)) {
                    items(chronological,key={it.id}) { item -> PlanCard(item,onToggleCompleted,table=true) }
                }
            }
            "برد" -> {
                Text("برد وضعیت: برنامه‌های گذشته، برنامه‌های پیش‌رو و تکمیل‌شده. جابه‌جایی وضعیت با علامت انجام‌شده است.",
                    style=MaterialTheme.typography.bodySmall)
                val today=remember { persianKey(LocalDate.now()) }
                val lanes=listOf(
                    "عقب‌افتاده" to chronological.filter { !it.completed && it.dateKey<today },
                    "پیش‌رو" to chronological.filter { !it.completed && it.dateKey>=today },
                    "انجام‌شده" to chronological.filter { it.completed }
                )
                Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()),
                    horizontalArrangement=Arrangement.spacedBy(10.dp)) {
                    lanes.forEach { (label,items) ->
                        Column(Modifier.width(235.dp), verticalArrangement=Arrangement.spacedBy(6.dp)) {
                            Text("$label (${items.size})", fontWeight=FontWeight.Bold)
                            if(items.isEmpty()) Text("بدون کارت", style=MaterialTheme.typography.bodySmall)
                            items.forEach { item -> PlanCard(item,onToggleCompleted) }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun PlanRows(items: List<LocalPlan>, onToggleCompleted: (String)->Unit) {
    if(items.isEmpty()) Text("برای این روز برنامه‌ای ثبت نشده است.",style=MaterialTheme.typography.bodySmall)
    items.forEach { item -> PlanCard(item,onToggleCompleted) }
}

@Composable
private fun PlanCard(item: LocalPlan, onToggleCompleted: (String)->Unit, table: Boolean=false) {
    ElevatedCard(modifier=Modifier.fillMaxWidth()) {
        Row(Modifier.fillMaxWidth().padding(8.dp), verticalAlignment=Alignment.CenterVertically) {
            Checkbox(checked=item.completed,onCheckedChange={onToggleCompleted(item.id)})
            Column(Modifier.weight(1f)) {
                Text("${item.icon} ${item.title}",fontWeight=FontWeight.Medium)
                Text("${item.dateKey} · %02d:%02d · ${if(item.kind=="event") "رویداد" else "کار"}".format(item.hour,item.minute),
                    style=MaterialTheme.typography.labelSmall)
            }
            if(table) Text(if(item.completed) "✓" else "○")
        }
    }
}
private fun persianMonthName(month:Int) =
    listOf("فروردین","اردیبهشت","خرداد","تیر","مرداد","شهریور","مهر","آبان","آذر","دی","بهمن","اسفند").getOrElse(month-1) { "" }

private fun persianKey(date:LocalDate):String {
    val cal=Calendar.getInstance(ULocale("fa_IR@calendar=persian")).apply {
        timeInMillis=date.atStartOfDay(ZoneId.systemDefault()).toInstant().toEpochMilli()
    }
    return "%04d-%02d-%02d".format(java.util.Locale.US,cal.get(Calendar.YEAR),
        cal.get(Calendar.MONTH)+1,cal.get(Calendar.DAY_OF_MONTH))
}
