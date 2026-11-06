package com.bdfr.zurvan

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch

private val occasionLabels = linkedMapOf(
    "birthday" to "Birthday Celebration · جشن تولد",
    "meeting_anniversary" to "First Meeting Anniversary · سالگرد آشنایی",
    "wedding_anniversary" to "Wedding Anniversary · سالگرد ازدواج"
)

/** All privileges verified by the Supabase Edge Function, not client flags. */
@Composable
fun ZurvanCreatorCelebrationPanel() {
    val context = LocalContext.current
    val api = remember(context) { ZurvanCelebrationApi(context.applicationContext) }
    val scope = rememberCoroutineScope()
    var directory by remember { mutableStateOf<CelebrationDirectory?>(null) }
    var busy by remember { mutableStateOf(false) }
    var errorText by remember { mutableStateOf("") }
    var selectedAccount by remember { mutableStateOf("") }
    var selectedOccasion by remember { mutableStateOf("birthday") }
    var monthText by remember { mutableStateOf("1") }
    var dayText by remember { mutableStateOf("1") }
    var selectedTheme by remember { mutableStateOf("zara-pastel") }
    var title by remember { mutableStateOf("") }
    var enabled by remember { mutableStateOf(true) }

    fun reload() {
        scope.launch {
            busy = true
            try {
                val result = api.load()
                directory = result
                if (selectedAccount.isBlank()) selectedAccount = result.currentAccountId
                errorText = ""
            } catch (e: Exception) { errorText = e.message ?: "خطای دریافت اطلاعات" }
            finally { busy = false }
        }
    }

    LaunchedEffect(Unit) { reload() }

    Column(verticalArrangement = Arrangement.spacedBy(12.dp),
        modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)) {
        Text("تم‌های مناسبتی خصوصی", style = MaterialTheme.typography.titleMedium)
        if (busy) LinearProgressIndicator(Modifier.fillMaxWidth())
        if (errorText.isNotBlank()) Text(errorText, color = MaterialTheme.colorScheme.error)
        val data = directory
        if (data != null && !data.isCreator) {
            Text("فقط سازنده زروان می‌تواند این تنظیمات را مدیریت کند.")
        }
        if (data?.isCreator == true) {
            Text("تاریخ‌ها شمسی هستند و تم فقط در همان روز برای حساب انتخابی اعمال می‌شود.",
                style = MaterialTheme.typography.bodySmall)
            var accountsExpanded by remember { mutableStateOf(false) }
            ExposedDropdownMenuBox(expanded = accountsExpanded, onExpandedChange = { accountsExpanded = it }) {
                val label = data.accounts.firstOrNull { it.id == selectedAccount }?.username ?: "انتخاب حساب"
                OutlinedTextField(value = label, onValueChange = {}, readOnly = true,
                    label = { Text("حساب دریافت‌کننده") }, trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(accountsExpanded) },
                    modifier = Modifier.fillMaxWidth().menuAnchor())
                ExposedDropdownMenu(expanded = accountsExpanded, onDismissRequest = { accountsExpanded = false }) {
                    data.accounts.forEach {
                        DropdownMenuItem(text = { Text(it.username) },
                            onClick = { selectedAccount = it.id; accountsExpanded = false })
                    }
                }
            }
            var occasionExpanded by remember { mutableStateOf(false) }
            ExposedDropdownMenuBox(expanded = occasionExpanded, onExpandedChange = { occasionExpanded = it }) {
                OutlinedTextField(value = occasionLabels[selectedOccasion] ?: selectedOccasion, onValueChange = {},
                    readOnly = true, label = { Text("نوع مناسبت") },
                    trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(occasionExpanded) },
                    modifier = Modifier.fillMaxWidth().menuAnchor())
                ExposedDropdownMenu(expanded = occasionExpanded, onDismissRequest = { occasionExpanded = false }) {
                    occasionLabels.forEach { (id, label) ->
                        DropdownMenuItem(text = { Text(label) },
                            onClick = { selectedOccasion = id; occasionExpanded = false })
                    }
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(value = monthText, onValueChange = { monthText = it.filter(Char::isDigit).take(2) },
                    label = { Text("ماه شمسی") }, modifier = Modifier.weight(1f), singleLine = true)
                OutlinedTextField(value = dayText, onValueChange = { dayText = it.filter(Char::isDigit).take(2) },
                    label = { Text("روز شمسی") }, modifier = Modifier.weight(1f), singleLine = true)
            }
            var themeExpanded by remember { mutableStateOf(false) }
            ExposedDropdownMenuBox(expanded = themeExpanded, onExpandedChange = { themeExpanded = it }) {
                OutlinedTextField(value = ZurvanThemes.presets.firstOrNull { it.id == selectedTheme }?.label ?: selectedTheme,
                    onValueChange = {}, readOnly = true, label = { Text("تم") },
                    trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(themeExpanded) },
                    modifier = Modifier.fillMaxWidth().menuAnchor())
                ExposedDropdownMenu(expanded = themeExpanded, onDismissRequest = { themeExpanded = false }) {
                    ZurvanThemes.presets.forEach {
                        DropdownMenuItem(text = { Text(it.label) },
                            onClick = { selectedTheme = it.id; themeExpanded = false })
                    }
                }
            }
            OutlinedTextField(value = title, onValueChange = { title = it.take(80) },
                label = { Text("عنوان اختصاصی") }, modifier = Modifier.fillMaxWidth())
            Row {
                Checkbox(checked = enabled, onCheckedChange = { enabled = it })
                Text("فعال", modifier = Modifier.padding(top = 12.dp))
            }
            val month = monthText.toIntOrNull() ?: 0
            val day = dayText.toIntOrNull() ?: 0
            val validDate = month in 1..12 && day in 1..(if (month <= 6) 31 else 30)
            Button(onClick = {
                scope.launch {
                    busy = true
                    try {
                        api.save(selectedAccount, selectedOccasion, month, day, selectedTheme, title, enabled)
                        directory = api.load()
                        errorText = "ذخیره شد."
                    } catch (e: Exception) { errorText = e.message ?: "خطای ذخیره" }
                    finally { busy = false }
                }
            }, enabled = !busy && selectedAccount.isNotBlank() && validDate,
                modifier = Modifier.fillMaxWidth()) { Text("ذخیره تنظیم مناسبت") }
            data.schedules.forEach { schedule ->
                ElevatedCard(modifier = Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(10.dp)) {
                        Text("${occasionLabels[schedule.occasionType] ?: schedule.occasionType}: " +
                            "${schedule.persianMonth}/${schedule.persianDay}  ·  ${schedule.themeId}")
                        Text(data.accounts.firstOrNull { it.id == schedule.accountId }?.username ?: schedule.accountId)
                        Row {
                            TextButton(onClick = {
                                selectedAccount = schedule.accountId
                                selectedOccasion = schedule.occasionType
                                monthText = schedule.persianMonth.toString()
                                dayText = schedule.persianDay.toString()
                                selectedTheme = schedule.themeId
                                title = schedule.title
                                enabled = schedule.enabled
                            }) { Text("ویرایش") }
                            TextButton(onClick = {
                                scope.launch {
                                    busy = true
                                    try {
                                        api.delete(schedule.id)
                                        directory = api.load()
                                        errorText = ""
                                    } catch (e: Exception) { errorText = e.message ?: "حذف ناموفق" }
                                    finally { busy = false }
                                }
                            }, enabled = !busy) { Text("حذف") }
                        }
                    }
                }
            }
            OutlinedButton(onClick = { reload() }, modifier = Modifier.fillMaxWidth()) { Text("بازخوانی از سرور") }
        }
    }
}
