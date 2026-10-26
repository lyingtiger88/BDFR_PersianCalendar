package com.bdfr.zurvan

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.platform.LocalContext
import kotlinx.coroutines.launch
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
fun ZurvanPrayerSettings() {
    val context = LocalContext.current
    var settings by remember { mutableStateOf(ZurvanPrayerTimes.settings(context)) }
    var city by remember { mutableStateOf(settings.city) }
    var lat by remember { mutableStateOf(settings.latitude.toString()) }
    var lon by remember { mutableStateOf(settings.longitude.toString()) }
    var zone by remember { mutableStateOf(settings.zone) }
    var message by remember { mutableStateOf("") }
    val scope = rememberCoroutineScope()
    var worldCities by remember { mutableStateOf<List<ZurvanWorldCities.City>>(emptyList()) }
    var loadingCities by remember { mutableStateOf(false) }
    var datasetMessage by remember { mutableStateOf("") }
    var selectedCountry by remember { mutableStateOf("") }
    var countrySearch by remember { mutableStateOf("") }
    var citySearch by remember { mutableStateOf("") }
    var countriesExpanded by remember { mutableStateOf(false) }
    var citiesExpanded by remember { mutableStateOf(false) }
    val countries = remember { ZurvanWorldCities.countries() }
    LaunchedEffect(context) {
        if (ZurvanWorldCities.available(context)) {
            worldCities = runCatching { ZurvanWorldCities.load(context) }.getOrDefault(emptyList())
        }
    }
    val schools = ZurvanPrayerTimes.schools
    val shia = settings.school == "jafari"
    val allowed = ZurvanPrayerTimes.methods.filter {
        if (shia) it.id == "tehran" || it.id == "jafari"
        else it.id != "tehran" && it.id != "jafari"
    }
    val current = if (allowed.any { it.id == settings.method }) settings.method else allowed.first().id
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("مذهب و فقه", fontWeight = FontWeight.SemiBold)
        var schoolExpanded by remember { mutableStateOf(false) }
        Box {
            OutlinedButton(onClick = { schoolExpanded = true }) {
                Text(schools[settings.school] ?: "جعفری")
            }
            DropdownMenu(expanded = schoolExpanded, onDismissRequest = { schoolExpanded = false }) {
                schools.forEach { (id,label) ->
                    DropdownMenuItem(text = { Text(label) }, onClick = {
                        settings = settings.copy(school = id, method = if(id=="jafari") "tehran" else "mwl")
                        schoolExpanded = false
                    })
                }
            }
        }
        Text("روش محاسبه", fontWeight = FontWeight.SemiBold)
        var methodExpanded by remember { mutableStateOf(false) }
        Box {
            OutlinedButton(onClick = { methodExpanded = true }) {
                Text(allowed.firstOrNull { it.id == current }?.name ?: "")
            }
            DropdownMenu(expanded = methodExpanded, onDismissRequest = { methodExpanded = false }) {
                allowed.forEach { method ->
                    DropdownMenuItem(text = { Text(method.name) }, onClick = {
                        settings = settings.copy(method = method.id)
                        methodExpanded = false
                    })
                }
            }
        }
        Text("انتخاب موقعیت جهانی", fontWeight = FontWeight.SemiBold)
        Text("پایگاه GeoNames شامل شهرها و آبادی‌های با جمعیت حداقل ۵۰۰ نفر است؛ همه آبادی‌های جهان را پوشش نمی‌دهد.",
            style = MaterialTheme.typography.bodySmall)
        Button(onClick = {
            scope.launch {
                loadingCities = true
                datasetMessage = ""
                try {
                    worldCities = ZurvanWorldCities.download(context)
                    datasetMessage = "${worldCities.size} موقعیت دریافت و برای استفاده آفلاین ذخیره شد."
                } catch (ex: Exception) {
                    datasetMessage = "دریافت فهرست ناموفق بود: ${ex.message}"
                } finally { loadingCities = false }
            }
        }, enabled = !loadingCities) {
            Text(if (loadingCities) "در حال دریافت پایگاه جهانی…" else
                if (worldCities.isEmpty()) "دریافت فهرست جهانی شهرها" else "به‌روزرسانی فهرست شهرها")
        }
        if (datasetMessage.isNotBlank()) Text(datasetMessage)
        if (worldCities.isNotEmpty()) {
            Text("کشور")
            OutlinedTextField(value = countrySearch, onValueChange = {
                countrySearch = it
                countriesExpanded = true
            }, label = { Text("جست‌وجوی کشور") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
            Box {
                OutlinedButton(onClick = { countriesExpanded = true }) {
                    Text(countries.firstOrNull { it.first == selectedCountry }?.second ?: "انتخاب کشور")
                }
                DropdownMenu(expanded = countriesExpanded, onDismissRequest = { countriesExpanded = false }) {
                    val validCodes = remember(worldCities) { worldCities.asSequence().map { it.countryCode }.toSet() }
                    countries.filter { it.first in validCodes &&
                        (countrySearch.isBlank() || it.second.contains(countrySearch, true) ||
                            it.first.contains(countrySearch, true)) }
                        .take(80).forEach { (code, name) ->
                            DropdownMenuItem(text = { Text(name) }, onClick = {
                                selectedCountry = code
                                countrySearch = name
                                citySearch = ""
                                countriesExpanded = false
                            })
                        }
                }
            }
            if (selectedCountry.isNotEmpty()) {
                OutlinedTextField(value = citySearch, onValueChange = {
                    citySearch = it
                    citiesExpanded = true
                }, label = { Text("جست‌وجوی شهر") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                Box {
                    OutlinedButton(onClick = { citiesExpanded = true }) { Text("انتخاب شهر") }
                    DropdownMenu(expanded = citiesExpanded, onDismissRequest = { citiesExpanded = false }) {
                        worldCities.asSequence().filter { it.countryCode == selectedCountry &&
                            (citySearch.isBlank() || it.name.contains(citySearch, true)) }
                            .take(80).forEach { place ->
                                DropdownMenuItem(text = { Text("${place.name} · ${place.latitude}, ${place.longitude}") },
                                    onClick = {
                                        city = place.name
                                        lat = place.latitude.toString()
                                        lon = place.longitude.toString()
                                        zone = place.timeZone
                                        citySearch = place.name
                                        citiesExpanded = false
                                    })
                            }
                    }
                }
            }
        }
        Text("موقعیت انتخاب‌شده (قابل ویرایش دستی)", fontWeight = FontWeight.SemiBold)
        OutlinedTextField(city,{ city=it },label={ Text("شهر") },modifier=Modifier.fillMaxWidth(),singleLine=true)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedTextField(lat,{ lat=it },label={ Text("عرض جغرافیایی") },modifier=Modifier.weight(1f),singleLine=true)
            OutlinedTextField(lon,{ lon=it },label={ Text("طول جغرافیایی") },modifier=Modifier.weight(1f),singleLine=true)
        }
        OutlinedTextField(zone,{ zone=it },label={ Text("منطقه زمانی؛ مانند Asia/Tehran") },
            modifier=Modifier.fillMaxWidth(),singleLine=true)
        Row {
            Checkbox(checked=settings.alerts,onCheckedChange={ settings=settings.copy(alerts=it) })
            Text("اعلان اوقات شرعی",modifier=Modifier.padding(top=12.dp))
        }
        if(settings.alerts) {
            Text("زمان اعلان نسبت به هر وقت")
            Row(horizontalArrangement=Arrangement.spacedBy(5.dp)) {
                listOf(0,5,10,15).forEach { m ->
                    FilterChip(selected=settings.beforeMinutes==m,onClick={ settings=settings.copy(beforeMinutes=m) },
                        label={ Text(if(m==0) "همان لحظه" else "$m دقیقه قبل") })
                }
            }
        }
        Button(onClick={
            val latitude=lat.toDoubleOrNull()
            val longitude=lon.toDoubleOrNull()
            val tz=runCatching { ZoneId.of(zone.trim()) }.getOrNull()
            if(latitude==null || longitude==null || latitude !in -90.0..90.0 || longitude !in -180.0..180.0 || tz==null) {
                message="مختصات یا منطقه زمانی نامعتبر است."
            } else {
                val value=settings.copy(method=current,city=city.trim(),latitude=latitude,
                    longitude=longitude,zone=zone.trim())
                ZurvanPrayerTimes.save(context,value)
                settings=value
                ZurvanPrayerAlerts.schedule(context,value)
                message="تنظیمات ذخیره شد؛ اوقات بر اساس موقعیت انتخاب‌شده محاسبه می‌شوند."
            }
        }) { Text("ذخیره تنظیمات شرعی") }
        if(message.isNotBlank()) Text(message)
        val sample=runCatching {
            ZurvanPrayerTimes.calculate(LocalDate.now(ZoneId.of(settings.zone)),settings.copy(method=current))
        }.getOrDefault(emptyList())
        Text("اوقات امروز — ${settings.city}", style=MaterialTheme.typography.titleMedium)
        if(sample.isEmpty()) Text("محاسبه در این عرض جغرافیایی/تاریخ ممکن نیست؛ مرجع محلی را بررسی کنید.")
        sample.forEach { row ->
            Row(modifier=Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.SpaceBetween) {
                Text(row.label)
                Text(row.time.format(DateTimeFormatter.ofPattern("HH:mm")))
            }
        }
        Text("زمان‌ها محاسبات نجومی تخمینی هستند، نه فتوای یک فقیه خاص یا جایگزین تقویم رسمی. در عرض‌های بالا و ماه رمضان اختلاف روش‌ها مهم است.",
            style=MaterialTheme.typography.bodySmall,color=MaterialTheme.colorScheme.onSurfaceVariant)
    }
}
