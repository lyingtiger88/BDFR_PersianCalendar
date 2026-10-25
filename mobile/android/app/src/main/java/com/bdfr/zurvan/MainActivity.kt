package com.bdfr.zurvan

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items as gridItems
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import java.security.SecureRandom
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class MainActivity : ComponentActivity() {
    private val notificationPermissionLauncher =
        registerForActivityResult(
            ActivityResultContracts.RequestPermission()
        ) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        if (
            Build.VERSION.SDK_INT >=
                Build.VERSION_CODES.TIRAMISU &&
            checkSelfPermission(
                Manifest.permission.POST_NOTIFICATIONS
            ) != PackageManager.PERMISSION_GRANTED
        ) {
            notificationPermissionLauncher.launch(
                Manifest.permission.POST_NOTIFICATIONS
            )
        }

        setContent {
            ZurvanThemes.Apply(this@MainActivity) {
                val appViewModel: ZurvanViewModel = viewModel()
                val deskViewModel: ZurvanDeskViewModel = viewModel()

                ZurvanMobileShell(
                    appViewModel = appViewModel,
                    deskViewModel = deskViewModel
                )
            }
        }
    }
}

private enum class ZurvanTab(
    val label: String
) {
    Calendar("تقویم"),
    Desk("زروان دسک"),
    Plans("برنامه‌ها"),
    Occasions("مناسبت‌ها"),
    Reminders("یادآورها"),
    Companion("همراه"),
    Settings("تنظیمات")
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ZurvanMobileShell(
    appViewModel: ZurvanViewModel,
    deskViewModel: ZurvanDeskViewModel
) {
    val context = LocalContext.current
    var licenseRevision by remember { mutableIntStateOf(0) }
    val licensed = remember(licenseRevision) {
        val code = context.getSharedPreferences("zurvan_license", Context.MODE_PRIVATE)
            .getString("activation_code", null)
        code != null && ZurvanActivation.validate(context, code)
    }
    var unlocked by remember { mutableStateOf(!ZurvanAppLock.enabled(context)) }
    var unlockInput by remember { mutableStateOf("") }
    var unlockMessage by remember { mutableStateOf("") }
    if (!unlocked) {
        Column(
            modifier = Modifier.fillMaxSize().padding(24.dp),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text("زروان قفل است", style = MaterialTheme.typography.headlineMedium)
            OutlinedTextField(
                value = unlockInput,
                onValueChange = { unlockInput = it },
                label = { Text("رمز ورود برنامه") },
                visualTransformation = androidx.compose.ui.text.input.PasswordVisualTransformation()
            )
            Button(onClick = {
                if (ZurvanAppLock.check(context, unlockInput)) {
                    unlocked = true
                    unlockInput = ""
                    unlockMessage = ""
                } else unlockMessage = "رمز اشتباه است."
            }) { Text("باز کردن برنامه") }
            Text(unlockMessage)
        }
        return
    }
    var appLanguage by remember { mutableStateOf(ZurvanLanguages.load(context)) }
    val localePrefs = remember(context) { context.getSharedPreferences("zurvan_locale", Context.MODE_PRIVATE) }
    DisposableEffect(localePrefs) {
        val listener = android.content.SharedPreferences.OnSharedPreferenceChangeListener { _, key ->
            if (key == "language") appLanguage = ZurvanLanguages.load(context)
        }
        localePrefs.registerOnSharedPreferenceChangeListener(listener)
        onDispose { localePrefs.unregisterOnSharedPreferenceChangeListener(listener) }
    }
    var selected by remember { mutableStateOf(ZurvanTab.Calendar) }
    LaunchedEffect(licensed) {
        if (!licensed && selected !in listOf(ZurvanTab.Calendar, ZurvanTab.Settings)) selected = ZurvanTab.Calendar
    }

    val uiPreferences by appViewModel
        .uiPreferences
        .collectAsStateWithLifecycle()

    CompositionLocalProvider(ZurvanLanguages.current provides appLanguage) {
    MaterialTheme(
        shapes = Shapes(
            extraSmall = RoundedCornerShape(uiPreferences.cornerRadius.dp),
            small = RoundedCornerShape(uiPreferences.cornerRadius.dp),
            medium = RoundedCornerShape(uiPreferences.cornerRadius.dp),
            large = RoundedCornerShape(uiPreferences.cornerRadius.dp),
            extraLarge = RoundedCornerShape(uiPreferences.cornerRadius.dp)
        )
    ) {
    BoxWithConstraints(
        modifier = Modifier.fillMaxSize()
    ) {
        ZurvanThemes.Wallpaper(modifier = Modifier.fillMaxSize())
        val wide = maxWidth >= 760.dp

        if (wide) {
            Row(
                modifier = Modifier.fillMaxSize()
            ) {
                if (uiPreferences.railSide == "left") {
                    ZurvanNavigationRail(
                        selected = selected,
                        onSelected = { if (licensed || it == ZurvanTab.Calendar || it == ZurvanTab.Settings) selected = it }
                    )
                }

                Scaffold(
                containerColor = Color.Transparent,
                    modifier = Modifier.weight(1f),
                    topBar = {
                        TopAppBar(
                            title = {
                                ZurvanSecretTitle()
                            }
                        )
                    }
                ) { padding ->
                    ZurvanDestination(
                        tab = selected,
                        licensed = licensed,
                        onActivated = { licenseRevision++ },
                        appViewModel = appViewModel,
                        deskViewModel = deskViewModel,
                        modifier = Modifier.padding(padding)
                    )
                }

                if (uiPreferences.railSide == "right") {
                    ZurvanNavigationRail(
                        selected = selected,
                        onSelected = { selected = it }
                    )
                }
            }
        } else {
            Scaffold(
                containerColor = Color.Transparent,
                topBar = {
                    TopAppBar(
                        title = {
                            ZurvanSecretTitle()
                        }
                    )
                },
                bottomBar = {
                    NavigationBar {
                        ZurvanTab.entries.forEach { tab ->
                            NavigationBarItem(
                                selected = selected == tab,
                                enabled = licensed || tab == ZurvanTab.Calendar || tab == ZurvanTab.Settings,
                                onClick = {
                                    if (licensed || tab == ZurvanTab.Calendar || tab == ZurvanTab.Settings) selected = tab
                                },
                                icon = {
                                    Icon(
                                        navigationIcon(tab),
                                        contentDescription =
                                            tab.label
                                    )
                                },
                                label = {
                                    Text(ZurvanLanguages.text(ZurvanLanguages.current.current, tab.name.lowercase()))
                                }
                            )
                        }
                    }
                }
            ) { padding ->
                var horizontalDistance by remember { mutableFloatStateOf(0f) }
                Box(
                    modifier = Modifier
                        .padding(padding)
                        .fillMaxSize()
                        .pointerInput(selected, licensed) {
                            detectHorizontalDragGestures(
                                onDragStart = { horizontalDistance = 0f },
                                onHorizontalDrag = { _, amount -> horizontalDistance += amount },
                                onDragCancel = { horizontalDistance = 0f },
                                onDragEnd = {
                                    if (kotlin.math.abs(horizontalDistance) > 90f) {
                                        val allowed = ZurvanTab.entries.filter {
                                            licensed || it == ZurvanTab.Calendar || it == ZurvanTab.Settings
                                        }
                                        val current = allowed.indexOf(selected).coerceAtLeast(0)
                                        val change = if (horizontalDistance < 0) 1 else -1
                                        selected = allowed[(current + change).coerceIn(0, allowed.lastIndex)]
                                    }
                                    horizontalDistance = 0f
                                }
                            )
                        }
                ) {
                    ZurvanDestination(
                        tab = selected,
                        licensed = licensed,
                        onActivated = { licenseRevision++ },
                        appViewModel = appViewModel,
                        deskViewModel = deskViewModel,
                        modifier = Modifier.fillMaxSize()
                    )
                }
            }
        }
    }
    }
    }
}

@Composable
private fun ZurvanActivationScreen(modifier: Modifier, onActivated: () -> Unit) {
    val context = LocalContext.current
    var entered by remember { mutableStateOf("") }
    var message by remember { mutableStateOf("کد سخت‌افزاری را برای دریافت لایسنس ارائه دهید.") }
    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Text("فعال‌سازی زروان", style = MaterialTheme.typography.headlineSmall)
        val hardwareCode = remember { ZurvanActivation.deviceCode(context) }
        Text("کد دستگاه: " + hardwareCode)
        TextButton(onClick = {
            val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
            clipboard.setPrimaryClip(android.content.ClipData.newPlainText("Zurvan device code", hardwareCode))
            message = "کد دستگاه کپی شد."
        }) { Text("کپی کد دستگاه") }
        OutlinedTextField(entered, { entered = it.take(8192) }, label = { Text("کد فعال‌سازی") }, modifier = Modifier.fillMaxWidth())
        Button(onClick = {
            val code = entered.trim()
            if (ZurvanActivation.diagnose(context, code) == ZurvanActivation.Result.VALID) {
                context.getSharedPreferences("zurvan_license", Context.MODE_PRIVATE).edit().putString("activation_code",code).apply()
                message = "فعال‌سازی موفق"
                onActivated()
            } else message = ZurvanActivation.explanation(ZurvanActivation.diagnose(context, code))
        }) { Text("فعال‌سازی") }
        Text(message)
    }
}

@Composable
private fun ZurvanNavigationRail(
    selected: ZurvanTab,
    onSelected: (ZurvanTab) -> Unit
) {
    NavigationRail(
        modifier = Modifier.fillMaxHeight()
    ) {
        Spacer(Modifier.height(12.dp))

        ZurvanTab.entries.forEach { tab ->
            NavigationRailItem(
                selected = selected == tab,
                onClick = {
                    onSelected(tab)
                },
                icon = {
                    Icon(
                        navigationIcon(tab),
                        contentDescription = tab.label
                    )
                },
                label = {
                    Text(ZurvanLanguages.text(ZurvanLanguages.current.current, tab.name.lowercase()))
                }
            )
        }
    }
}

private fun navigationIcon(
    tab: ZurvanTab
) = when (tab) {
    ZurvanTab.Calendar ->
        Icons.Outlined.CalendarMonth
    ZurvanTab.Desk ->
        Icons.Outlined.Folder
    ZurvanTab.Plans ->
        Icons.Outlined.TaskAlt
    ZurvanTab.Occasions ->
        Icons.Outlined.Event
    ZurvanTab.Reminders ->
        Icons.Outlined.Notifications
    ZurvanTab.Companion ->
        Icons.Outlined.Devices
    ZurvanTab.Settings ->
        Icons.Outlined.Settings
}

@Composable
private fun ZurvanDestination(
    tab: ZurvanTab,
    licensed: Boolean,
    onActivated: () -> Unit,
    appViewModel: ZurvanViewModel,
    deskViewModel: ZurvanDeskViewModel,
    modifier: Modifier
) {
    if (!licensed && tab != ZurvanTab.Calendar && tab != ZurvanTab.Settings) {
        Box(modifier.fillMaxSize(), contentAlignment = Alignment.Center) { Text("برای استفاده از این بخش، زروان را فعال کنید.") }
        return
    }
    if (!licensed && tab == ZurvanTab.Settings) {
        ZurvanActivationScreen(modifier, onActivated)
        return
    }
    when (tab) {
        ZurvanTab.Calendar ->
            if (!licensed) Box(modifier.fillMaxSize()) { CalendarScreen(viewModel = appViewModel, modifier = Modifier.fillMaxSize()); Box(Modifier.fillMaxSize().clickable(enabled = true, onClick = {}).semantics { }) } else CalendarScreen(
                viewModel = appViewModel,
                modifier = modifier
            )

        ZurvanTab.Desk ->
            ZurvanDeskScreen(
                viewModel = deskViewModel,
                modifier = modifier
            )

        ZurvanTab.Plans ->
            PlansScreen(
                viewModel = appViewModel,
                modifier = modifier
            )

        ZurvanTab.Occasions ->
            OccasionsScreen(
                viewModel = appViewModel,
                modifier = modifier
            )

        ZurvanTab.Reminders ->
            RemindersScreen(
                viewModel = appViewModel,
                modifier = modifier
            )

        ZurvanTab.Companion ->
            CompanionScreen(
                viewModel = appViewModel,
                modifier = modifier
            )

        ZurvanTab.Settings ->
            SettingsScreen(
                viewModel = appViewModel,
                modifier = modifier
            )
    }
}

@Composable
private fun CalendarScreen(
    viewModel: ZurvanViewModel,
    modifier: Modifier = Modifier
) {
    val plans by viewModel.plans
        .collectAsStateWithLifecycle()

    val reminders by viewModel.reminders
        .collectAsStateWithLifecycle()

    val occasions by viewModel.occasions
        .collectAsStateWithLifecycle()

    val notes by viewModel.notes
        .collectAsStateWithLifecycle()

    val preferences by viewModel
        .uiPreferences
        .collectAsStateWithLifecycle()

    val initial = remember {
        AndroidCalendarEngine
            .currentPersianYearMonth()
    }

    var year by remember {
        mutableIntStateOf(initial.first)
    }

    var month by remember {
        mutableIntStateOf(initial.second)
    }

    var selectedDay by remember {
        mutableStateOf<CalendarDayCell?>(null)
    }

    var addPlanDate by remember {
        mutableStateOf<String?>(null)
    }

    var addReminderDate by remember {
        mutableStateOf<String?>(null)
    }

    var showJumpDialog by remember {
        mutableStateOf(false)
    }

    var showConverterDialog by remember {
        mutableStateOf(false)
    }

    var pendingSelectedDay by remember {
        mutableIntStateOf(0)
    }

    val model = remember(
        year,
        month
    ) {
        AndroidCalendarEngine
            .buildPersianMonth(
                year,
                month
            )
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .padding(12.dp),
        verticalArrangement =
            Arrangement.spacedBy(8.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment =
                Alignment.CenterVertically,
            horizontalArrangement =
                Arrangement.SpaceBetween
        ) {
            IconButton(
                onClick = {
                    val shifted =
                        AndroidCalendarEngine
                            .shiftMonth(
                                year,
                                month,
                                -1
                            )
                    year = shifted.first
                    month = shifted.second
                }
            ) {
                Icon(
                    Icons.Outlined.ChevronRight,
                    contentDescription =
                        "ماه قبل"
                )
            }

            Column(
                horizontalAlignment =
                    Alignment.CenterHorizontally
            ) {
                var monthsMenu by remember { mutableStateOf(false) }
                var yearsMenu by remember { mutableStateOf(false) }
                val monthSymbols = listOf("🌷","🌱","☀️","🌞","🌻","🌾","🍂","🍁","🌧️","❄️","☃️","🌸")
                val monthNames = listOf("فروردین","اردیبهشت","خرداد","تیر","مرداد","شهریور","مهر","آبان","آذر","دی","بهمن","اسفند")
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box {
                        TextButton(onClick = { monthsMenu = true }) {
                            Text(monthSymbols[month - 1] + " " + model.monthName + " ▾",
                                fontWeight = FontWeight.Bold)
                        }
                        DropdownMenu(expanded = monthsMenu, onDismissRequest = { monthsMenu = false }) {
                            monthNames.forEachIndexed { index, name ->
                                DropdownMenuItem(
                                    text = { Text(monthSymbols[index] + " " + name) },
                                    onClick = { month = index + 1; monthsMenu = false }
                                )
                            }
                        }
                    }
                    Box {
                        TextButton(onClick = { yearsMenu = true }) {
                            Text(toPersianDigits(year.toString()) + " ▾", fontWeight = FontWeight.Bold)
                        }
                        DropdownMenu(expanded = yearsMenu, onDismissRequest = { yearsMenu = false }) {
                            ((year - 20)..(year + 20)).forEach { value ->
                                DropdownMenuItem(
                                    text = { Text(toPersianDigits(value.toString())) },
                                    onClick = { year = value; yearsMenu = false }
                                )
                            }
                        }
                    }
                }

                Text(
                    text =
                        preferences
                            .calendarOrder
                            .joinToString(
                                " · "
                            ) {
                                it.label
                            },
                    style =
                        MaterialTheme
                            .typography
                            .labelMedium
                )
            }

            IconButton(
                onClick = {
                    val shifted =
                        AndroidCalendarEngine
                            .shiftMonth(
                                year,
                                month,
                                1
                            )
                    year = shifted.first
                    month = shifted.second
                }
            ) {
                Icon(
                    Icons.Outlined.ChevronLeft,
                    contentDescription =
                        "ماه بعد"
                )
            }
        }

        Row(
            modifier =
                Modifier.fillMaxWidth(),
            horizontalArrangement =
                Arrangement.Center
        ) {
            TextButton(
                onClick = {
                    showJumpDialog = true
                }
            ) {
                Icon(
                    Icons.Outlined.NearMe,
                    contentDescription = null
                )
                Spacer(
                    Modifier.width(4.dp)
                )
                Text("پرش به تاریخ")
            }

            TextButton(
                onClick = {
                    showConverterDialog = true
                }
            ) {
                Icon(
                    Icons.Outlined.SwapHoriz,
                    contentDescription = null
                )
                Spacer(
                    Modifier.width(4.dp)
                )
                Text("مبدل تاریخ")
            }
        }

        val prayerContext = LocalContext.current
        val prayerSettings = remember { ZurvanPrayerTimes.settings(prayerContext) }
        val todayPrayers = remember(prayerSettings) {
            runCatching {
                ZurvanPrayerTimes.calculate(
                    java.time.LocalDate.now(java.time.ZoneId.of(prayerSettings.zone)), prayerSettings
                )
            }.getOrDefault(emptyList())
        }
        if (todayPrayers.isNotEmpty()) {
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.fillMaxWidth().padding(8.dp)) {
                    Text("اوقات شرعی امروز · ${prayerSettings.city}",
                        style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.SemiBold)
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        todayPrayers.filter { it.key in listOf("fajr","sunrise","dhuhr","maghrib","isha") }
                            .forEach { entry ->
                                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                    Text(entry.label, style = MaterialTheme.typography.labelSmall)
                                    Text(entry.time.toString(), style = MaterialTheme.typography.labelSmall)
                                }
                            }
                    }
                }
            }
        }

        WeekdayHeader()

        LazyVerticalGrid(
            columns = GridCells.Fixed(7),
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth(),
            contentPadding =
                PaddingValues(2.dp),
            horizontalArrangement =
                Arrangement.spacedBy(4.dp),
            verticalArrangement =
                Arrangement.spacedBy(4.dp)
        ) {
            items(
                count =
                    model.leadingBlankCount
            ) {
                Spacer(
                    modifier =
                        Modifier.height(82.dp)
                )
            }

            gridItems(
                model.days,
                key = {
                    it.persianKey
                }
            ) { day ->
                val dayHasContent =
                    plans.any {
                        it.dateKey ==
                            day.persianKey
                    } ||
                    reminders.any {
                        it.dateKey ==
                            day.persianKey
                    } ||
                    notes.any {
                        it.dateKey ==
                            day.persianKey
                    } ||
                    occasions.any {
                        occasionMatches(
                            day,
                            it
                        )
                    }

                CalendarDayCard(
                    day = day,
                    isHoliday = ZurvanDayFlags.isHoliday(day, occasions),
                    isMourning = ZurvanDayFlags.isMourning(day, occasions),
                    hasOccasion = ZurvanBuiltInOccasions.onDay(day, occasions).isNotEmpty(),
                    isCelebration = ZurvanBuiltInOccasions.onDay(day, occasions).any {
                        listOf("عید", "میلاد", "جشن", "ولادت").any { key -> it.title.contains(key) } },
                    calendarOrder =
                        preferences
                            .calendarOrder,
                    cornerRadius =
                        preferences
                            .cornerRadius,
                    hasContent =
                        dayHasContent,
                    onClick = {
                        selectedDay = day
                    }
                )
            }
        }
    }

    LaunchedEffect(
        year,
        month,
        pendingSelectedDay
    ) {
        if (pendingSelectedDay > 0) {
            selectedDay =
                model.days.firstOrNull {
                    it.persianDay ==
                        pendingSelectedDay
                }
            pendingSelectedDay = 0
        }
    }

    selectedDay?.let { day ->
        DayDetailsSheet(
            day = day,
            plans = plans.filter {
                it.dateKey ==
                    day.persianKey
            },
            reminders =
                reminders.filter {
                    it.dateKey ==
                        day.persianKey
                },
            occasions = ZurvanBuiltInOccasions.onDay(day, occasions),
            note = notes.firstOrNull {
                it.dateKey ==
                    day.persianKey
            },
            onDismiss = {
                selectedDay = null
            },
            onSaveNote = {
                    text,
                    isPrivate ->
                viewModel.saveNote(
                    day.persianKey,
                    text,
                    isPrivate
                )
            },
            onAddPlan = {
                addPlanDate =
                    day.persianKey
            },
            onAddReminder = {
                addReminderDate =
                    day.persianKey
            },
            onTogglePlan = {
                viewModel.togglePlan(it)
            },
            onAcknowledgeReminder = {
                viewModel
                    .acknowledgeReminder(it)
            }
        )
    }

    addPlanDate?.let { dateKey ->
        PlanDialog(
            initialDateKey = dateKey,
            onDismiss = {
                addPlanDate = null
            },
            onSave = {
                    title,
                    finalDateKey,
                    hour,
                    minute,
                    kind,
                    isPrivate,
                    icon ->
                viewModel.addPlan(
                    title,
                    finalDateKey,
                    hour,
                    minute,
                    kind,
                    isPrivate,
                    icon
                )
                addPlanDate = null
            }
        )
    }

    addReminderDate?.let { dateKey ->
        ReminderDialog(
            initialDateKey = dateKey,
            onDismiss = {
                addReminderDate = null
            },
            onSave = {
                    title,
                    finalDateKey,
                    hour,
                    minute,
                    isPrivate,
                    repeatCount,
                    repeatInterval ->
                viewModel.addReminder(
                    title,
                    finalDateKey,
                    hour,
                    minute,
                    isPrivate,
                    repeatCount,
                    repeatInterval
                )
                addReminderDate = null
            }
        )
    }

    if (showJumpDialog) {
        JumpToDateDialog(
            onDismiss = {
                showJumpDialog = false
            },
            onJump = { result ->
                year = result.persianYear
                month = result.persianMonth
                pendingSelectedDay =
                    result.persianDay
                showJumpDialog = false
            }
        )
    }

    if (showConverterDialog) {
        DateConverterDialog(
            onDismiss = {
                showConverterDialog = false
            }
        )
    }
}

@Composable
private fun WeekdayHeader() {
    val names = listOf(
        "ش",
        "ی",
        "د",
        "س",
        "چ",
        "پ",
        "ج"
    )

    Row(
        modifier =
            Modifier.fillMaxWidth()
    ) {
        names.forEachIndexed {
                index,
                value ->
            Text(
                text = value,
                modifier =
                    Modifier.weight(1f),
                textAlign =
                    TextAlign.Center,
                fontWeight =
                    FontWeight.SemiBold,
                color = if (
                    index == 6
                ) {
                    MaterialTheme
                        .colorScheme
                        .error
                } else {
                    MaterialTheme
                        .colorScheme
                        .onSurface
                }
            )
        }
    }
}

@Composable
private fun CalendarDayCard(
    day: CalendarDayCell,
    isHoliday: Boolean,
    isMourning: Boolean,
    hasOccasion: Boolean,
    isCelebration: Boolean,
    calendarOrder:
        List<CalendarPriority>,
    cornerRadius: Float,
    hasContent: Boolean,
    onClick: () -> Unit
) {
    val weekdayColors = ZurvanThemes.weekdayColors.current
    val order = if (
        calendarOrder.size == 3
    ) {
        calendarOrder
    } else {
        AndroidUiPreferences()
            .calendarOrder
    }

    val primary =
        calendarDayValue(
            day,
            order[0]
        )

    val secondary =
        calendarDayValue(
            day,
            order[1]
        )

    val tertiary =
        calendarDayValue(
            day,
            order[2]
        )

    Card(
        modifier = Modifier
            .fillMaxWidth()
            .height(82.dp)
            .clickable(
                onClick = onClick
            ),
        shape =
            RoundedCornerShape(
                cornerRadius.dp
            ),
        colors =
            CardDefaults.cardColors(
                containerColor =
                    if (day.isToday) {
                        MaterialTheme.colorScheme.primaryContainer
                    } else if (day.weekColumn in 0..6 && weekdayColors.size == 7) {
                        weekdayColors[day.weekColumn]
                    } else {
                        MaterialTheme
                            .colorScheme
                            .surfaceVariant
                    }
            )
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(6.dp)
        ) {
            Text(
                text = primary,
                modifier =
                    Modifier.align(
                        Alignment.Center
                    ),
                style =
                    MaterialTheme
                        .typography
                        .titleMedium,
                fontWeight =
                    FontWeight.Bold,
                color = if (
                    isHoliday
                ) {
                    MaterialTheme
                        .colorScheme
                        .error
                } else {
                    MaterialTheme
                        .colorScheme
                        .onSurface
                }
            )

            Text(
                text = secondary,
                modifier =
                    Modifier.align(
                        Alignment.TopEnd
                    ),
                style =
                    MaterialTheme
                        .typography
                        .labelSmall
            )

            Text(
                text = tertiary,
                modifier =
                    Modifier.align(
                        Alignment.BottomStart
                    ),
                style =
                    MaterialTheme
                        .typography
                        .labelSmall
            )

            if (isMourning) {
                Text(
                    text = "◤",
                    color = Color.Black,
                    style = MaterialTheme.typography.titleMedium,
                    modifier = Modifier.align(androidx.compose.ui.AbsoluteAlignment.TopLeft),
                    // Physical top-left: independent of RTL layout direction.
                )
            }

            if (!isMourning && isCelebration) {
                Text("🎁", modifier = Modifier.align(Alignment.TopStart),
                    style = MaterialTheme.typography.labelMedium)
            } else if (!isMourning && hasOccasion) {
                Box(Modifier.align(Alignment.TopStart).size(7.dp).background(
                    if (isHoliday) Color.Red else MaterialTheme.colorScheme.primary,
                    RoundedCornerShape(100)
                ))
            }
            if (isHoliday && !day.isFriday) {
                Box(Modifier.align(Alignment.TopEnd).size(6.dp)
                    .background(Color.Red, RoundedCornerShape(100)))
            }

            if (hasContent) {
                Box(
                    modifier =
                        Modifier
                            .size(7.dp)
                            .align(
                                Alignment.BottomEnd
                            )
                ) {
                    Surface(
                        modifier =
                            Modifier.fillMaxSize(),
                        shape =
                            RoundedCornerShape(
                                100
                            ),
                        color =
                            MaterialTheme
                                .colorScheme
                                .primary
                    ) {}
                }
            }
        }
    }
}

private fun calendarDayValue(
    day: CalendarDayCell,
    priority: CalendarPriority
): String = when (priority) {
    CalendarPriority.PERSIAN ->
        toPersianDigits(
            day.persianDay.toString()
        )

    CalendarPriority.HIJRI ->
        toArabicDigits(
            day.hijriDay.toString()
        )

    CalendarPriority.GREGORIAN ->
        day.gregorianDay.toString()
}

private fun occasionMatches(
    day: CalendarDayCell,
    occasion: LocalOccasion
): Boolean = when (
    occasion.calendar
) {
    CalendarPriority.PERSIAN ->
        occasion.month ==
            day.persianMonth &&
            occasion.day ==
                day.persianDay

    CalendarPriority.HIJRI ->
        occasion.month ==
            day.hijriMonth &&
            occasion.day ==
                day.hijriDay

    CalendarPriority.GREGORIAN ->
        occasion.month ==
            day.gregorianMonth &&
            occasion.day ==
                day.gregorianDay
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun DayDetailsSheet(
    day: CalendarDayCell,
    plans: List<LocalPlan>,
    reminders: List<LocalReminder>,
    occasions: List<LocalOccasion>,
    note: LocalDayNote?,
    onDismiss: () -> Unit,
    onSaveNote: (
        String,
        Boolean
    ) -> Unit,
    onAddPlan: () -> Unit,
    onAddReminder: () -> Unit,
    onTogglePlan: (String) -> Unit,
    onAcknowledgeReminder:
        (String) -> Unit
) {
    var noteText by remember(
        note?.text,
        day.persianKey
    ) {
        mutableStateOf(
            note?.text.orEmpty()
        )
    }

    var privateNote by remember(
        note?.isPrivate,
        day.persianKey
    ) {
        mutableStateOf(
            note?.isPrivate == true
        )
    }

    ModalBottomSheet(
        onDismissRequest =
            onDismiss
    ) {
        LazyColumn(
            modifier = Modifier
                .fillMaxWidth()
                .padding(
                    horizontal = 16.dp
                ),
            verticalArrangement =
                Arrangement.spacedBy(
                    10.dp
                ),
            contentPadding =
                PaddingValues(
                    bottom = 28.dp
                )
        ) {
            item {
                Text(
                    text =
                        toPersianDigits(
                            day.persianYear
                                .toString()
                        ) +
                            "/" +
                            toPersianDigits(
                                day.persianMonth
                                    .toString()
                            ) +
                            "/" +
                            toPersianDigits(
                                day.persianDay
                                    .toString()
                            ),
                    style =
                        MaterialTheme
                            .typography
                            .headlineSmall
                )
            }

            item {
                Text(
                    text =
                        "میلادی: " +
                            day.gregorianYear +
                            "/" +
                            day.gregorianMonth +
                            "/" +
                            day.gregorianDay +
                            " · قمری: " +
                            toArabicDigits(
                                day.hijriYear
                                    .toString()
                            ) +
                            "/" +
                            toArabicDigits(
                                day.hijriMonth
                                    .toString()
                            ) +
                            "/" +
                            toArabicDigits(
                                day.hijriDay
                                    .toString()
                            )
                )
            }

            item {
                Row(
                    horizontalArrangement =
                        Arrangement.spacedBy(
                            8.dp
                        )
                ) {
                    Button(
                        onClick = onAddPlan
                    ) {
                        Text(
                            "رویداد/کار"
                        )
                    }

                    Button(
                        onClick =
                            onAddReminder
                    ) {
                        Text("یادآور")
                    }
                }
            }

            item {
                SectionTitle("مناسبت‌های این روز")
            }

            if (occasions.isEmpty()) {
                item {
                    Text(
                        "برای این تاریخ مناسبتی در اطلاعات برنامه ثبت نشده است.",
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)
                    )
                }
            } else {
                items(occasions, key = { "occasion-" + it.id }) { occasion ->
                    val title = occasion.title
                    val isMourning = listOf("شهادت", "رحلت", "وفات", "عزاداری", "سوگواری", "عاشورا", "تاسوعا", "اربعین")
                        .any { title.contains(it) }
                    val isHoliday = title.contains("تعطیل")
                    val isCelebration = listOf("عید", "میلاد", "ولادت", "جشن")
                        .any { title.contains(it) }
                    val symbol = when {
                        isMourning -> "🎗"
                        isCelebration -> "🎉"
                        isHoliday -> "🔴"
                        title.contains("دیابت") -> "🔵"
                        title.contains("صلح") -> "🕊️"
                        title.contains("علم") -> "🎓"
                        else -> "📅"
                    }
                    Card(
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(16.dp),
                        colors = CardDefaults.cardColors(
                            containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.55f)
                        )
                    ) {
                        Row(
                            modifier = Modifier.fillMaxWidth().padding(14.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(12.dp)
                        ) {
                            Text(symbol, style = MaterialTheme.typography.titleLarge)
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    title,
                                    style = MaterialTheme.typography.titleSmall,
                                    fontWeight = FontWeight.SemiBold,
                                    color = if (isHoliday) MaterialTheme.colorScheme.error
                                            else MaterialTheme.colorScheme.onSurface
                                )
                                Text(
                                    occasion.calendar.label,
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }
                }
            }

            if (plans.isNotEmpty()) {
                item {
                    SectionTitle(
                        "برنامه‌ها"
                    )
                }

                items(
                    plans,
                    key = { it.id }
                ) { item ->
                    ListItem(
                        headlineContent = {
                            Text(
                                if (
                                    item.isPrivate
                                ) {
                                    "رویداد خصوصی"
                                } else {
                                    item.title
                                }
                            )
                        },
                        supportingContent = {
                            Text(
                                formatTime(
                                    item.hour,
                                    item.minute
                                )
                            )
                        },
                        leadingContent = {
                            Checkbox(
                                checked =
                                    item.completed,
                                onCheckedChange = {
                                    onTogglePlan(
                                        item.id
                                    )
                                }
                            )
                        }
                    )
                }
            }

            if (reminders.isNotEmpty()) {
                item {
                    SectionTitle(
                        "یادآورها"
                    )
                }

                items(
                    reminders,
                    key = { it.id }
                ) { item ->
                    ListItem(
                        headlineContent = {
                            Text(
                                if (
                                    item.isPrivate
                                ) {
                                    "یادآور خصوصی"
                                } else {
                                    item.title
                                }
                            )
                        },
                        supportingContent = {
                            Text(
                                formatTime(
                                    item.hour,
                                    item.minute
                                ) +
                                    " · " +
                                    item.repeatCount +
                                    " هشدار"
                            )
                        },
                        trailingContent = {
                            if (
                                !item.acknowledged
                            ) {
                                TextButton(
                                    onClick = {
                                        onAcknowledgeReminder(
                                            item.id
                                        )
                                    }
                                ) {
                                    Text(
                                        "متوجه شدم"
                                    )
                                }
                            }
                        }
                    )
                }
            }

            item {
                SectionTitle("یادداشت")
            }

            item {
                OutlinedTextField(
                    value = noteText,
                    onValueChange = {
                        noteText = it
                    },
                    modifier =
                        Modifier.fillMaxWidth(),
                    minLines = 3,
                    label = {
                        Text(
                            "یادداشت روز"
                        )
                    }
                )
            }

            item {
                Row(
                    verticalAlignment =
                        Alignment.CenterVertically
                ) {
                    Checkbox(
                        checked =
                            privateNote,
                        onCheckedChange = {
                            privateNote =
                                it
                        }
                    )
                    Text(
                        "یادداشت خصوصی"
                    )
                }
            }

            item {
                Button(
                    onClick = {
                        onSaveNote(
                            noteText,
                            privateNote
                        )
                    },
                    modifier =
                        Modifier.fillMaxWidth()
                ) {
                    Text(
                        "ذخیره یادداشت"
                    )
                }
            }
        }
    }
}

@Composable
private fun PlansScreen(
    viewModel: ZurvanViewModel,
    modifier: Modifier = Modifier
) {
    val plans by viewModel.plans
        .collectAsStateWithLifecycle()

    var showDialog by remember {
        mutableStateOf(false)
    }

    val today = remember {
        val pair =
            AndroidCalendarEngine
                .currentPersianYearMonth()
        val month =
            AndroidCalendarEngine
                .buildPersianMonth(
                    pair.first,
                    pair.second
                )
        month.days.firstOrNull {
            it.isToday
        }?.persianKey
            ?: String.format(
                "%04d-%02d-01",
                pair.first,
                pair.second
            )
    }

    ZurvanTeamPlanner(
        modifier = modifier,
        plans = plans,
        onAddSchedule = { showDialog = true }
    )

    if (showDialog) {
        PlanDialog(
            initialDateKey = today,
            onDismiss = {
                showDialog = false
            },
            onSave = {
                    title,
                    dateKey,
                    hour,
                    minute,
                    kind,
                    isPrivate,
                    icon ->
                viewModel.addPlan(
                    title,
                    dateKey,
                    hour,
                    minute,
                    kind,
                    isPrivate,
                    icon
                )
                showDialog = false
            }
        )
    }
}

@Composable
private fun OccasionsScreen(
    viewModel: ZurvanViewModel,
    modifier: Modifier = Modifier
) {
    val occasions by viewModel
        .occasions
        .collectAsStateWithLifecycle()

    var showDialog by remember {
        mutableStateOf(false)
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .padding(12.dp),
        verticalArrangement =
            Arrangement.spacedBy(8.dp)
    ) {
        ScreenHeader(
            title = "مناسبت‌ها",
            actionText = "افزودن",
            onAction = {
                showDialog = true
            }
        )

        if (occasions.isEmpty()) {
            EmptyState(
                title =
                    "مناسبت شخصی ثبت نشده",
                icon =
                    Icons.Outlined.Event
            )
        } else {
            LazyColumn(
                verticalArrangement =
                    Arrangement.spacedBy(
                        6.dp
                    )
            ) {
                items(
                    occasions.sortedWith(
                        compareBy<LocalOccasion> {
                            it.calendar.ordinal
                        }.thenBy {
                            it.month
                        }.thenBy {
                            it.day
                        }
                    ),
                    key = { it.id }
                ) { item ->
                    Card(
                        modifier =
                            Modifier
                                .fillMaxWidth()
                    ) {
                        ListItem(
                            headlineContent = {
                                Text(item.title)
                            },
                            supportingContent = {
                                Text(
                                    item.calendar
                                        .label +
                                        " · " +
                                        item.month +
                                        "/" +
                                        item.day +
                                        " · " +
                                        formatTime(
                                            item.hour,
                                            item.minute
                                        ) +
                                        " · سالانه"
                                )
                            },
                            leadingContent = {
                                Icon(
                                    Icons.Outlined.Event,
                                    contentDescription =
                                        null
                                )
                            },
                            trailingContent = {
                                IconButton(
                                    onClick = {
                                        viewModel
                                            .deleteOccasion(
                                                item.id
                                            )
                                    }
                                ) {
                                    Icon(
                                        Icons.Outlined.Delete,
                                        contentDescription =
                                            "حذف"
                                    )
                                }
                            }
                        )
                    }
                }
            }
        }
    }

    if (showDialog) {
        OccasionDialog(
            onDismiss = {
                showDialog = false
            },
            onSave = {
                    title,
                    calendar,
                    month,
                    day,
                    hour,
                    minute ->
                viewModel.addOccasion(
                    title,
                    calendar,
                    month,
                    day,
                    hour,
                    minute
                )
                showDialog = false
            }
        )
    }
}

@Composable
private fun RemindersScreen(
    viewModel: ZurvanViewModel,
    modifier: Modifier = Modifier
) {
    val reminders by viewModel
        .reminders
        .collectAsStateWithLifecycle()

    var showDialog by remember {
        mutableStateOf(false)
    }

    val today = remember {
        val pair =
            AndroidCalendarEngine
                .currentPersianYearMonth()
        val model =
            AndroidCalendarEngine
                .buildPersianMonth(
                    pair.first,
                    pair.second
                )
        model.days.firstOrNull {
            it.isToday
        }?.persianKey
            ?: String.format(
                "%04d-%02d-01",
                pair.first,
                pair.second
            )
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .padding(12.dp),
        verticalArrangement =
            Arrangement.spacedBy(8.dp)
    ) {
        ScreenHeader(
            title = "یادآورها",
            actionText = "افزودن",
            onAction = {
                showDialog = true
            }
        )

        if (reminders.isEmpty()) {
            EmptyState(
                title =
                    "یادآوری ثبت نشده",
                icon =
                    Icons.Outlined.Notifications
            )
        } else {
            LazyColumn(
                verticalArrangement =
                    Arrangement.spacedBy(
                        6.dp
                    )
            ) {
                items(
                    reminders.sortedWith(
                        compareBy<LocalReminder> {
                            it.dateKey
                        }.thenBy {
                            it.hour
                        }.thenBy {
                            it.minute
                        }
                    ),
                    key = { it.id }
                ) { item ->
                    Card(
                        modifier =
                            Modifier.fillMaxWidth()
                    ) {
                        ListItem(
                            headlineContent = {
                                Text(
                                    if (
                                        item.isPrivate
                                    ) {
                                        "یادآور خصوصی"
                                    } else {
                                        item.title
                                    }
                                )
                            },
                            supportingContent = {
                                Text(
                                    item.dateKey +
                                        " · " +
                                        formatTime(
                                            item.hour,
                                            item.minute
                                        ) +
                                        " · " +
                                        item.repeatCount +
                                        " مرتبه / " +
                                        item.repeatIntervalMinutes +
                                        " دقیقه"
                                )
                            },
                            leadingContent = {
                                Icon(
                                    if (
                                        item.acknowledged
                                    ) {
                                        Icons.Outlined.CheckCircle
                                    } else {
                                        Icons.Outlined.Notifications
                                    },
                                    contentDescription =
                                        null
                                )
                            },
                            trailingContent = {
                                Row {
                                    if (
                                        !item.acknowledged
                                    ) {
                                        TextButton(
                                            onClick = {
                                                viewModel
                                                    .acknowledgeReminder(
                                                        item.id
                                                    )
                                            }
                                        ) {
                                            Text(
                                                "تأیید"
                                            )
                                        }
                                    }

                                    IconButton(
                                        onClick = {
                                            viewModel
                                                .deleteReminder(
                                                    item.id
                                                )
                                        }
                                    ) {
                                        Icon(
                                            Icons.Outlined.Delete,
                                            contentDescription =
                                                "حذف"
                                        )
                                    }
                                }
                            }
                        )
                    }
                }
            }
        }
    }

    if (showDialog) {
        ReminderDialog(
            initialDateKey = today,
            onDismiss = {
                showDialog = false
            },
            onSave = {
                    title,
                    dateKey,
                    hour,
                    minute,
                    isPrivate,
                    repeatCount,
                    repeatInterval ->
                viewModel.addReminder(
                    title,
                    dateKey,
                    hour,
                    minute,
                    isPrivate,
                    repeatCount,
                    repeatInterval
                )
                showDialog = false
            }
        )
    }
}

@Composable
private fun CompanionScreen(
    viewModel: ZurvanViewModel,
    modifier: Modifier = Modifier
) {
    val state by viewModel
        .companionState
        .collectAsStateWithLifecycle()

    val context = LocalContext.current

    var scannerMessage by remember {
        mutableStateOf<String?>(null)
    }

    val scannerOptions = remember {
        GmsBarcodeScannerOptions
            .Builder()
            .setBarcodeFormats(
                Barcode.FORMAT_QR_CODE
            )
            .enableAutoZoom()
            .build()
    }

    val scanner = remember(
        context,
        scannerOptions
    ) {
        GmsBarcodeScanning.getClient(
            context,
            scannerOptions
        )
    }

    val qrBitmap = remember(
        state.qrPayload
    ) {
        state.qrPayload?.let {
            payload ->
            runCatching {
                CompanionQrCode.generate(
                    payload,
                    720
                )
            }.getOrNull()
        }
    }

    LazyColumn(
        modifier = modifier
            .fillMaxSize()
            .padding(16.dp),
        verticalArrangement =
            Arrangement.spacedBy(12.dp),
        contentPadding =
            PaddingValues(
                bottom = 28.dp
            )
    ) {
        item {
            Text(
                "Zurvan Companion",
                style =
                    MaterialTheme
                        .typography
                        .headlineSmall
            )
        }

        item {
            Card(
                modifier =
                    Modifier.fillMaxWidth()
            ) {
                Column(
                    modifier =
                        Modifier.padding(
                            16.dp
                        ),
                    verticalArrangement =
                        Arrangement.spacedBy(
                            10.dp
                        )
                ) {
                    Text(
                        "اتصال به نرم افزار همراه (دسکتاپ - موبایل)",
                        style =
                            MaterialTheme
                                .typography
                                .titleMedium,
                        fontWeight =
                            FontWeight.Bold
                    )

                    Text(
                        "Master یک QR یک‌بارمصرف ۵ دقیقه‌ای می‌سازد. Secondary با اسکن QR روی همان شبکه محلی وصل می‌شود؛ سپس ECDH P-256 کلید جلسه را می‌سازد و تمام Sync Frameها با AES-GCM رمز می‌شوند.",
                        style =
                            MaterialTheme
                                .typography
                                .bodySmall
                    )

                    Row(
                        modifier =
                            Modifier.fillMaxWidth(),
                        horizontalArrangement =
                            Arrangement.spacedBy(
                                8.dp
                            )
                    ) {
                        Button(
                            onClick = {
                                scannerMessage =
                                    null
                                viewModel
                                    .startCompanionMaster()
                            },
                            modifier =
                                Modifier.weight(
                                    1f
                                ),
                            enabled =
                                !state.busy
                        ) {
                            Icon(
                                Icons.Outlined
                                    .QrCode2,
                                contentDescription =
                                    null
                            )
                            Spacer(
                                Modifier.width(
                                    6.dp
                                )
                            )
                            Text("Master")
                        }

                        Button(
                            onClick = {
                                scannerMessage =
                                    "دوربین QR باز شد…"

                                scanner
                                    .startScan()
                                    .addOnSuccessListener {
                                        barcode ->
                                        val raw =
                                            barcode
                                                .rawValue

                                        if (
                                            raw
                                                .isNullOrBlank()
                                        ) {
                                            scannerMessage =
                                                "QR خوانده شد ولی داده‌ای نداشت."
                                        } else {
                                            scannerMessage =
                                                "QR تأیید شد؛ در حال اتصال امن…"

                                            viewModel
                                                .joinCompanionFromQr(
                                                    raw
                                                )
                                        }
                                    }
                                    .addOnCanceledListener {
                                        scannerMessage =
                                            "اسکن QR لغو شد."
                                    }
                                    .addOnFailureListener {
                                        error ->
                                        scannerMessage =
                                            "اسکن QR انجام نشد: " +
                                                (
                                                    error
                                                        .message
                                                        ?: "خطای ناشناخته"
                                                    )
                                    }
                            },
                            modifier =
                                Modifier.weight(
                                    1f
                                ),
                            enabled =
                                !state.busy
                        ) {
                            Icon(
                                Icons.Outlined
                                    .QrCodeScanner,
                                contentDescription =
                                    null
                            )
                            Spacer(
                                Modifier.width(
                                    6.dp
                                )
                            )
                            Text("Secondary")
                        }
                    }

                    if (
                        state.mode ==
                        "master"
                    ) {
                        TextButton(
                            onClick = {
                                viewModel
                                    .stopCompanionSession()
                            }
                        ) {
                            Text(
                                "لغو جلسه Master"
                            )
                        }
                    }
                }
            }
        }

        if (
            qrBitmap != null &&
            state.qrPayload != null
        ) {
            item {
                Card(
                    modifier =
                        Modifier.fillMaxWidth()
                ) {
                    Column(
                        modifier =
                            Modifier.padding(
                                16.dp
                            ),
                        horizontalAlignment =
                            Alignment.CenterHorizontally,
                        verticalArrangement =
                            Arrangement.spacedBy(
                                10.dp
                            )
                    ) {
                        Text(
                            "این QR را در دستگاه Secondary اسکن کنید",
                            fontWeight =
                                FontWeight.SemiBold,
                            textAlign =
                                TextAlign.Center
                        )

                        Image(
                            bitmap =
                                qrBitmap
                                    .asImageBitmap(),
                            contentDescription =
                                "Zurvan Companion pairing QR",
                            modifier =
                                Modifier
                                    .size(
                                        270.dp
                                    )
                        )

                        state.pairingCode
                            ?.let {
                                Text(
                                    "کد جلسه: $it",
                                    style =
                                        MaterialTheme
                                            .typography
                                            .titleMedium,
                                    fontWeight =
                                        FontWeight.Bold
                                )
                            }

                        Text(
                            "QR پس از ۵ دقیقه یا اولین Pair موفق باطل می‌شود.",
                            style =
                                MaterialTheme
                                    .typography
                                    .bodySmall,
                            textAlign =
                                TextAlign.Center
                        )
                    }
                }
            }
        }

        item {
            Card(
                modifier =
                    Modifier.fillMaxWidth()
            ) {
                Column(
                    modifier =
                        Modifier.padding(
                            16.dp
                        ),
                    verticalArrangement =
                        Arrangement.spacedBy(
                            8.dp
                        )
                ) {
                    Row(
                        verticalAlignment =
                            Alignment.CenterVertically,
                        horizontalArrangement =
                            Arrangement.spacedBy(
                                8.dp
                            )
                    ) {
                        if (state.busy) {
                            CircularProgressIndicator(
                                modifier =
                                    Modifier.size(
                                        20.dp
                                    ),
                                strokeWidth =
                                    2.dp
                            )
                        }

                        Text(
                            state.status,
                            fontWeight =
                                FontWeight.SemiBold
                        )
                    }

                    scannerMessage
                        ?.let {
                            Text(
                                it,
                                style =
                                    MaterialTheme
                                        .typography
                                        .bodySmall
                            )
                        }

                    state.peerName
                        ?.let {
                            Text(
                                "دستگاه مقابل: $it"
                            )
                        }

                    state.sas
                        ?.let {
                            Text(
                                "کد امنیتی مشترک: $it",
                                style =
                                    MaterialTheme
                                        .typography
                                        .titleMedium,
                                fontWeight =
                                    FontWeight.Bold
                            )
                        }

                    if (
                        state.syncedItemCount >
                        0
                    ) {
                        Text(
                            "آیتم‌های اعمال‌شده: " +
                                toPersianDigits(
                                    state
                                        .syncedItemCount
                                        .toString()
                                )
                        )
                    }

                    state.error
                        ?.let {
                            Text(
                                it,
                                color =
                                    MaterialTheme
                                        .colorScheme
                                        .error,
                                style =
                                    MaterialTheme
                                        .typography
                                        .bodySmall
                            )
                        }
                }
            }
        }

        if (
            state.trustedDevices
                .isNotEmpty()
        ) {
            item {
                Text(
                    "دستگاه‌های جفت‌شده",
                    style =
                        MaterialTheme
                            .typography
                            .titleMedium,
                    fontWeight =
                        FontWeight.Bold
                )
            }

            items(
                state.trustedDevices,
                key = {
                    it.deviceId
                }
            ) {
                device ->
                Card(
                    modifier =
                        Modifier.fillMaxWidth()
                ) {
                    Column(
                        modifier =
                            Modifier.padding(
                                14.dp
                            ),
                        verticalArrangement =
                            Arrangement.spacedBy(
                                4.dp
                            )
                    ) {
                        Text(
                            device.displayName,
                            fontWeight =
                                FontWeight.SemiBold
                        )
                        Text(
                            device.platform.name +
                                " · " +
                                device.publicKeyFingerprint,
                            style =
                                MaterialTheme
                                    .typography
                                    .bodySmall
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun SettingsScreen(
    viewModel: ZurvanViewModel,
    modifier: Modifier = Modifier
) {
    val preferences by viewModel
        .uiPreferences
        .collectAsStateWithLifecycle()

    var corner by remember(
        preferences.cornerRadius
    ) {
        mutableFloatStateOf(
            preferences.cornerRadius
        )
    }

    val palette = ZurvanThemes.active.current
    val selectedElenaImage = ZurvanThemes.elenaImage.current
    val selectElenaImage = ZurvanThemes.setElenaImage.current
    val setPalette = ZurvanThemes.setActive.current
    val colorOverrides = ZurvanThemes.customColors.current
    val setColorOverride = ZurvanThemes.setCustomColor.current
    val weekdayColors = ZurvanThemes.weekdayColors.current
    val setWeekdayColor = ZurvanThemes.setWeekdayColor.current
    val context = LocalContext.current
    val wallpaperPath = ZurvanThemes.wallpaper.current
    val setWallpaper = ZurvanThemes.setWallpaper.current
    var pickingColorFor by remember { mutableStateOf<String?>(null) }
    val refreshAppearance = ZurvanThemes.refreshAppearance.current
    var themeImportMessage by remember { mutableStateOf("") }
    val themePicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) {
            runCatching { ZurvanThemeImporter.import(context, uri) }
                .onSuccess { theme ->
                    refreshAppearance()
                    themeImportMessage = "تم «${theme.name}» نصب و فعال شد."
                }
                .onFailure { error -> themeImportMessage = error.message ?: "درون‌ریزی تم انجام نشد." }
        }
    }
    val imagePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) {
            runCatching {
                val target = java.io.File(context.filesDir, "zurvan_custom_background")
                context.contentResolver.openInputStream(uri)?.use { input ->
                    target.outputStream().use { output -> input.copyTo(output) }
                } ?: error("تصویر قابل خواندن نیست")
                setWallpaper(target.absolutePath)
            }
        }
    }

    pickingColorFor?.let { colorKey ->
        ZurvanColorEditor(
            initialKey = colorKey,
            overrides = colorOverrides + weekdayColors.mapIndexed { index, color ->
                "weekday$index" to String.format(java.util.Locale.US, "#%06X", (android.graphics.Color.rgb((color.red * 255).toInt(), (color.green * 255).toInt(), (color.blue * 255).toInt())) and 0xFFFFFF)
            }.toMap(),
            defaults = palette,
            weekdayDefaults = weekdayColors,
            onApply = { key, hex ->
                if (key.startsWith("weekday")) setWeekdayColor(key.last().digitToInt(), hex)
                else setColorOverride(key, hex)
            },
            onReset = { key ->
                if (key.startsWith("weekday")) setWeekdayColor(key.last().digitToInt(), null)
                else setColorOverride(key, null)
            },
            onDismiss = { pickingColorFor = null }
        )
    }

    LazyColumn(
        modifier = modifier
            .fillMaxSize()
            .padding(12.dp),
        verticalArrangement =
            Arrangement.spacedBy(10.dp),
        contentPadding =
            PaddingValues(
                bottom = 24.dp
            )
    ) {
        item {
            val language = ZurvanLanguages.current.current
            var languageMenu by remember { mutableStateOf(false) }
            SettingsCard(title = ZurvanLanguages.text(language, "language")) {
                Text(ZurvanLanguages.text(language, "languageNote"))
                Box {
                    OutlinedButton(onClick = { languageMenu = true }) {
                        Text(ZurvanLanguages.options[language] ?: "فارسی")
                        Icon(Icons.Outlined.ArrowDropDown, contentDescription = null)
                    }
                    DropdownMenu(expanded = languageMenu, onDismissRequest = { languageMenu = false }) {
                        ZurvanLanguages.options.forEach { (code, label) ->
                            DropdownMenuItem(text = { Text(label) }, onClick = {
                                ZurvanLanguages.save(context, code)
                                languageMenu = false
                            })
                        }
                    }
                }
            }
        }
        item {
            SettingsCard(title = "اوقات شرعی و اذان") {
                ZurvanPrayerSettings()
            }
        }
        item {
            SettingsCard(title = "افزودن قالب از فایل") {
                Text("تم ساخته‌شده در Zurvan Theme Studio را از JSON یا ZIP وارد کنید.")
                Button(onClick = { themePicker.launch(arrayOf("application/json", "application/zip", "application/octet-stream", "*/*")) }) {
                    Text("درون‌ریزی تم (JSON / ZIP)")
                }
                if (themeImportMessage.isNotBlank()) Text(themeImportMessage)
            }
        }
        item {
            SettingsCard(title = "تصویر پس‌زمینه سفارشی") {
                Button(onClick = { imagePicker.launch("image/*") }) { Text("انتخاب عکس از دستگاه") }
                if (wallpaperPath != null) {
                    Button(onClick = { setWallpaper(null) }) { Text("حذف تصویر سفارشی") }
                }
            }
        }
        item {
            SettingsCard(title = "انتخاب رنگ عناصر") {
                Text("چرخه رنگ حرفه‌ای با پیش‌نمایش زنده برای هر بخش برنامه")
                Button(
                    onClick = { pickingColorFor = "accent" },
                    modifier = Modifier.fillMaxWidth()
                ) { Text("باز کردن پالت رنگ و پیش‌نمایش") }
                listOf(
                    "surface" to "نوار بالا",
                    "accent" to "دکمه‌ها",
                    "card" to "کارت‌ها",
                    "holiday" to "روز تعطیل",
                    "background" to "پس‌زمینه",
                    "ink" to "رنگ متن",
                    "weekday0" to "شنبه", "weekday1" to "یکشنبه", "weekday2" to "دوشنبه",
                    "weekday3" to "سه‌شنبه", "weekday4" to "چهارشنبه", "weekday5" to "پنجشنبه", "weekday6" to "جمعه"
                ).forEach { (key, label) ->
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(label)
                        TextButton(onClick = { pickingColorFor = key }) { Text("تغییر رنگ") }
                    }
                }
            }
        }

        item {
            SettingsCard(title = "وضعیت لایسنس زروان") {
                var activatedCode by remember {
                    mutableStateOf(
                        context.getSharedPreferences("zurvan_license", Context.MODE_PRIVATE)
                            .getString("activation_code", null)
                    )
                }
                val active = activatedCode?.let { ZurvanActivation.validate(context, it) } == true
                if (active) {
                    val ownerName = when (activatedCode?.trim()) {
                        "elena13731005" -> "محبوب زیبای من ❤️❤️❤️"
                        "zarasamtestmode" -> "ارزشمندترین دوست 😁🤔😍"
                        "delaramsoftheart" -> "دل آرام و قلب پاک من 🌷🤍"
                        "shirinquietfox" -> "شیطون باهوش و دوست‌داشتنی 🦊😉"
                        "nazaninstrongcare" -> "لجباز مهربون و حامی قشنگم 💪💗"
                        "mehrbanmomfriend" -> "مامان مهربون و رفیق بی‌نظیرم 🌸❤️"
                        "jonoubsweetpearl" -> "ممه بزرگ جنوب"
                        else -> "دارنده لایسنس معتبر زروان"
                    }
                    Text("ثبت شده به نام : $ownerName")
                    Text("لایسنس فعال است.", style = MaterialTheme.typography.bodySmall)
                } else {
                    val deviceCode = remember { ZurvanActivation.deviceCode(context) }
                    var enteredCode by remember { mutableStateOf("") }
                    var status by remember { mutableStateOf("کد دستگاه را به صادرکننده لایسنس ارائه کنید.") }
                    Text("کد دستگاه: " + deviceCode)
                    TextButton(onClick = {
                        (context.getSystemService(Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager)
                            .setPrimaryClip(android.content.ClipData.newPlainText("Zurvan device code", deviceCode))
                    }) { Text("کپی کد دستگاه") }
                    OutlinedTextField(
                        value = enteredCode,
                        onValueChange = { enteredCode = it.take(8192) },
                        label = { Text("کد فعال‌سازی") },
                        modifier = Modifier.fillMaxWidth()
                    )
                    Button(onClick = {
                        val code = enteredCode.trim()
                        if (ZurvanActivation.validate(context, code)) {
                            context.getSharedPreferences("zurvan_license", Context.MODE_PRIVATE)
                                .edit().putString("activation_code", code).apply()
                            activatedCode = code
                            status = "لایسنس معتبر است."
                        } else {
                            status = ZurvanActivation.explanation(ZurvanActivation.diagnose(context, code))
                        }
                    }) { Text("بررسی و فعال‌سازی") }
                    Text(status, style = MaterialTheme.typography.bodySmall)
                }
            }
        }
        item {
            SettingsCard(title = "قفل برنامه") {
                var lockEnabled by remember { mutableStateOf(ZurvanAppLock.enabled(context)) }
                var pin by remember { mutableStateOf("") }
                var oldPin by remember { mutableStateOf("") }
                var feedback by remember { mutableStateOf("") }
                Text(if (lockEnabled) "قفل فعال است" else "قفل غیرفعال است")
                if (lockEnabled) {
                    OutlinedTextField(
                        value = oldPin, onValueChange = { oldPin = it },
                        label = { Text("رمز فعلی") },
                        visualTransformation = androidx.compose.ui.text.input.PasswordVisualTransformation()
                    )
                    Button(onClick = {
                        if (ZurvanAppLock.remove(context, oldPin)) {
                            lockEnabled = false
                            oldPin = ""
                            feedback = "قفل غیرفعال شد."
                        } else feedback = "رمز فعلی صحیح نیست."
                    }) { Text("حذف قفل") }
                } else {
                    OutlinedTextField(
                        value = pin, onValueChange = { pin = it },
                        label = { Text("رمز جدید (حداقل ۴ کاراکتر)") },
                        visualTransformation = androidx.compose.ui.text.input.PasswordVisualTransformation()
                    )
                    Button(onClick = {
                        if (ZurvanAppLock.set(context, pin)) {
                            lockEnabled = true
                            pin = ""
                            feedback = "قفل فعال شد. هنگام باز کردن مجدد برنامه رمز لازم است."
                        } else feedback = "رمز باید حداقل ۴ کاراکتر باشد."
                    }) { Text("فعال کردن قفل") }
                }
                Text(feedback)
            }
        }
        item {
            SettingsCard(title = "تم ظاهری") {
                ZurvanThemes.presets.forEach { option ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        RadioButton(
                            selected = palette.id == option.id,
                            onClick = { setPalette(option.id) }
                        )
                        Text(option.label, modifier = Modifier.clickable {
                            setPalette(option.id)
                        })
                    }
                }
            }
        }

        if (palette.id == "elena-mode") {
            item {
                SettingsCard(title = "تصاویر اختصاصی Elena Mode") {
                    Text("۱۱ تصویر در خود برنامه قرار دارند. حالت خودکار هر روز یک تصویر انتخاب می‌کند.")
                    var elenaMenu by remember { mutableStateOf(false) }
                    Box {
                        OutlinedButton(onClick = { elenaMenu = true }) {
                            Text(if (selectedElenaImage == 0) "تعویض خودکار روزانه" else "تصویر شماره $selectedElenaImage")
                        }
                        DropdownMenu(expanded = elenaMenu, onDismissRequest = { elenaMenu = false }) {
                            DropdownMenuItem(text = { Text("خودکار (هر روز)") }, onClick = { selectElenaImage(0); elenaMenu = false })
                            (1..11).forEach { imageIndex ->
                                DropdownMenuItem(text = { Text("تصویر $imageIndex") }, onClick = {
                                    selectElenaImage(imageIndex)
                                    elenaMenu = false
                                })
                            }
                        }
                    }
                }
            }
        }

        item {
            Text(
                "تنظیمات",
                style =
                    MaterialTheme
                        .typography
                        .headlineSmall
            )
        }

        item {
            SettingsCard(
                title =
                    "ترتیب نمایش تقویم‌ها"
            ) {
                preferences
                    .calendarOrder
                    .forEachIndexed {
                            index,
                            value ->
                        Button(
                            onClick = {
                                val order =
                                    preferences
                                        .calendarOrder
                                        .toMutableList()

                                val next =
                                    CalendarPriority
                                        .entries[
                                        (
                                            value.ordinal +
                                                1
                                            ) %
                                            CalendarPriority
                                                .entries
                                                .size
                                    ]

                                val duplicate =
                                    order.indexOf(
                                        next
                                    )

                                if (
                                    duplicate >= 0 &&
                                    duplicate != index
                                ) {
                                    val old =
                                        order[index]
                                    order[index] =
                                        next
                                    order[duplicate] =
                                        old
                                } else {
                                    order[index] =
                                        next
                                }

                                viewModel
                                    .setCalendarOrder(
                                        order
                                    )
                            },
                            modifier =
                                Modifier
                                    .fillMaxWidth()
                        ) {
                            Text(
                                when (index) {
                                    0 ->
                                        "تقویم اول (اصلی): "
                                    1 ->
                                        "تقویم دوم: "
                                    else ->
                                        "تقویم سوم: "
                                } +
                                    value.label
                            )
                        }
                    }
            }
        }

        item {
            SettingsCard(
                title =
                    "گردی گوشه‌ها"
            ) {
                Text(
                    toPersianDigits(
                        corner
                            .toInt()
                            .toString()
                    ) +
                        " px"
                )

                Slider(
                    value = corner,
                    onValueChange = {
                        corner = it
                        viewModel
                            .setCornerRadius(
                                it
                            )
                    },
                    valueRange =
                        0f..32f,
                    steps = 31
                )
            }
        }

        item {
            SettingsCard(
                title =
                    "محل Navigation Rail"
            ) {
                Row(
                    horizontalArrangement =
                        Arrangement.spacedBy(
                            8.dp
                        )
                ) {
                    FilterChip(
                        selected =
                            preferences
                                .railSide ==
                                "left",
                        onClick = {
                            viewModel
                                .setRailSide(
                                    "left"
                                )
                        },
                        label = {
                            Text("چپ")
                        }
                    )

                    FilterChip(
                        selected =
                            preferences
                                .railSide ==
                                "right",
                        onClick = {
                            viewModel
                                .setRailSide(
                                    "right"
                                )
                        },
                        label = {
                            Text("راست")
                        }
                    )
                }

                Text(
                    "در گوشی Bottom Navigation استفاده می‌شود؛ در تبلت و نمایشگر عریض Rail در سمت انتخابی قرار می‌گیرد.",
                    style =
                        MaterialTheme
                            .typography
                            .bodySmall
                )
            }
        }

        item {
            SettingsCard(
                title =
                    "Zurvan Desk"
            ) {
                Text(
                    "اتصال S3 و credentialهای ابر آروان در UI نمایش داده نمی‌شوند. کاربر فقط حساب و فایل‌های خود را مدیریت می‌کند.",
                    style =
                        MaterialTheme
                            .typography
                            .bodySmall
                )
            }
        }
    }
}

@Composable
private fun ZurvanDeskScreen(
    viewModel: ZurvanDeskViewModel,
    modifier: Modifier = Modifier
) {
    val state by viewModel.state
        .collectAsStateWithLifecycle()

    val context = LocalContext.current
    val scope = rememberCoroutineScope()

    var registerMode by remember {
        mutableStateOf(false)
    }

    var identifier by remember {
        mutableStateOf("")
    }

    var password by remember {
        mutableStateOf("")
    }

    var displayName by remember {
        mutableStateOf("")
    }

    var recoveryPhrase by remember {
        mutableStateOf("")
    }

    var renameFile by remember {
        mutableStateOf<DeskUiFile?>(null)
    }

    var renameText by remember {
        mutableStateOf("")
    }

    val filePicker =
        rememberLauncherForActivityResult(
            ActivityResultContracts
                .OpenDocument()
        ) { uri ->
            if (uri != null) {
                scope.launch {
                    val payload =
                        readUriPayload(
                            context,
                            uri
                        )

                    if (payload != null) {
                        viewModel.uploadFile(
                            displayName =
                                payload.first,
                            mimeType =
                                payload.second,
                            bytes =
                                payload.third
                        )
                    }
                }
            }
        }

    Column(
        modifier = modifier
            .fillMaxSize()
            .padding(12.dp),
        verticalArrangement =
            Arrangement.spacedBy(10.dp)
    ) {
        Text(
            "Zurvan Desk",
            style =
                MaterialTheme
                    .typography
                    .headlineSmall
        )

        if (!state.authenticated) {
            Card(
                modifier =
                    Modifier.fillMaxWidth()
            ) {
                Column(
                    modifier =
                        Modifier.padding(
                            16.dp
                        ),
                    verticalArrangement =
                        Arrangement.spacedBy(
                            10.dp
                        )
                ) {
                    Text(
                        if (registerMode) {
                            "ساخت حساب"
                        } else {
                            "ورود به حساب"
                        },
                        style =
                            MaterialTheme
                                .typography
                                .titleMedium
                    )

                    OutlinedTextField(
                        value = identifier,
                        onValueChange = {
                            identifier = it
                        },
                        modifier =
                            Modifier
                                .fillMaxWidth(),
                        label = {
                            Text(
                                "ایمیل یا شماره"
                            )
                        },
                        singleLine = true
                    )

                    if (registerMode) {
                        OutlinedTextField(
                            value =
                                displayName,
                            onValueChange = {
                                displayName =
                                    it
                            },
                            modifier =
                                Modifier
                                    .fillMaxWidth(),
                            label = {
                                Text(
                                    "نام نمایشی"
                                )
                            },
                            singleLine =
                                true
                        )
                    }

                    OutlinedTextField(
                        value = password,
                        onValueChange = {
                            password = it
                        },
                        modifier =
                            Modifier
                                .fillMaxWidth(),
                        label = {
                            Text("رمز عبور")
                        },
                        singleLine = true
                    )

                    Button(
                        onClick = {
                            if (registerMode) {
                                viewModel
                                    .register(
                                        identifier,
                                        password,
                                        displayName
                                            .ifBlank {
                                                null
                                            }
                                    )
                            } else {
                                viewModel
                                    .login(
                                        identifier,
                                        password
                                    )
                            }
                        },
                        modifier =
                            Modifier
                                .fillMaxWidth(),
                        enabled =
                            !state.loading
                    ) {
                        Text(
                            if (registerMode) {
                                "ساخت حساب"
                            } else {
                                "ورود"
                            }
                        )
                    }

                    TextButton(
                        onClick = {
                            registerMode =
                                !registerMode
                        }
                    ) {
                        Text(
                            if (registerMode) {
                                "حساب دارم؛ ورود"
                            } else {
                                "حساب ندارم؛ ثبت‌نام"
                            }
                        )
                    }

                    Text(
                        "تنظیمات سرور و ابر آروان توسط سرویس Zurvan Desk مدیریت می‌شود و در این صفحه نمایش داده نمی‌شود.",
                        style =
                            MaterialTheme
                                .typography
                                .bodySmall
                    )
                }
            }
        } else if (state.needsRecovery) {
            Card(
                modifier =
                    Modifier.fillMaxWidth()
            ) {
                Column(
                    modifier =
                        Modifier.padding(
                            16.dp
                        ),
                    verticalArrangement =
                        Arrangement.spacedBy(
                            10.dp
                        )
                ) {
                    Text(
                        "بازیابی Vault",
                        style =
                            MaterialTheme
                                .typography
                                .titleMedium
                    )

                    Text(
                        "برای بازکردن فایل‌های رمزگذاری‌شده، عبارت بازیابی ۲۴ کلمه‌ای را وارد کنید."
                    )

                    OutlinedTextField(
                        value =
                            recoveryPhrase,
                        onValueChange = {
                            recoveryPhrase =
                                it
                        },
                        modifier =
                            Modifier
                                .fillMaxWidth(),
                        minLines = 4,
                        label = {
                            Text(
                                "عبارت بازیابی"
                            )
                        }
                    )

                    Button(
                        onClick = {
                            viewModel
                                .restoreVault(
                                    recoveryPhrase
                                )
                            recoveryPhrase = ""
                        },
                        modifier =
                            Modifier
                                .fillMaxWidth()
                    ) {
                        Text(
                            "بازیابی Vault"
                        )
                    }

                    TextButton(
                        onClick = {
                            viewModel.logout()
                        }
                    ) {
                        Text("خروج از حساب")
                    }
                }
            }
        } else {
            Row(
                modifier =
                    Modifier.fillMaxWidth(),
                verticalAlignment =
                    Alignment.CenterVertically,
                horizontalArrangement =
                    Arrangement.SpaceBetween
            ) {
                Column {
                    Text(
                        state.displayName
                            ?: state.identifier
                            ?: "حساب زروان",
                        fontWeight =
                            FontWeight.Bold
                    )

                    Text(
                        state.identifier
                            .orEmpty(),
                        style =
                            MaterialTheme
                                .typography
                                .bodySmall
                    )
                }

                Row {
                    IconButton(
                        onClick = {
                            viewModel
                                .refreshFiles()
                        }
                    ) {
                        Icon(
                            Icons.Outlined.Refresh,
                            contentDescription =
                                "بروزرسانی"
                        )
                    }

                    TextButton(
                        onClick = {
                            viewModel.logout()
                        }
                    ) {
                        Text("خروج")
                    }
                }
            }

            Button(
                onClick = {
                    filePicker.launch(
                        arrayOf("*/*")
                    )
                },
                modifier =
                    Modifier.fillMaxWidth()
            ) {
                Icon(
                    Icons.Outlined.UploadFile,
                    contentDescription =
                        null
                )
                Spacer(
                    Modifier.width(6.dp)
                )
                Text("آپلود فایل")
            }

            if (state.files.isEmpty()) {
                EmptyState(
                    title =
                        "فایلی در Zurvan Desk نیست",
                    icon =
                        Icons.Outlined.Folder
                )
            } else {
                LazyColumn(
                    modifier =
                        Modifier.weight(1f),
                    verticalArrangement =
                        Arrangement.spacedBy(
                            6.dp
                        )
                ) {
                    items(
                        state.files,
                        key = { it.id }
                    ) { file ->
                        Card(
                            modifier =
                                Modifier
                                    .fillMaxWidth()
                        ) {
                            ListItem(
                                headlineContent = {
                                    Text(
                                        file.name
                                    )
                                },
                                supportingContent = {
                                    Text(
                                        formatBytes(
                                            file.size
                                        )
                                    )
                                },
                                leadingContent = {
                                    Icon(
                                        Icons.Outlined.InsertDriveFile,
                                        contentDescription =
                                            null
                                    )
                                },
                                trailingContent = {
                                    Row {
                                        IconButton(
                                            onClick = {
                                                viewModel
                                                    .downloadToDownloads(
                                                        file
                                                    )
                                            }
                                        ) {
                                            Icon(
                                                Icons.Outlined.Download,
                                                contentDescription =
                                                    "دانلود"
                                            )
                                        }

                                        IconButton(
                                            onClick = {
                                                renameFile =
                                                    file
                                                renameText =
                                                    file.name
                                            }
                                        ) {
                                            Icon(
                                                Icons.Outlined.Edit,
                                                contentDescription =
                                                    "تغییر نام"
                                            )
                                        }

                                        IconButton(
                                            onClick = {
                                                viewModel
                                                    .deleteFile(
                                                        file
                                                    )
                                            }
                                        ) {
                                            Icon(
                                                Icons.Outlined.Delete,
                                                contentDescription =
                                                    "حذف"
                                            )
                                        }
                                    }
                                }
                            )
                        }
                    }
                }
            }
        }

        state.message?.let {
            Text(
                it,
                color =
                    MaterialTheme
                        .colorScheme
                        .primary
            )
        }

        state.error?.let {
            Text(
                it,
                color =
                    MaterialTheme
                        .colorScheme
                        .error
            )
        }

        if (state.loading) {
            LinearProgressIndicator(
                modifier =
                    Modifier.fillMaxWidth()
            )
        }
    }

    state.newRecoveryPhrase?.let {
            phrase ->
        AlertDialog(
            onDismissRequest = {},
            title = {
                Text(
                    "عبارت بازیابی ۲۴ کلمه‌ای"
                )
            },
            text = {
                Column(
                    verticalArrangement =
                        Arrangement.spacedBy(
                            8.dp
                        )
                ) {
                    Text(
                        "این عبارت فقط همین‌جا نمایش داده می‌شود. آن را آفلاین یادداشت کنید."
                    )

                    Text(
                        phrase
                            .split(" ")
                            .mapIndexed {
                                    index,
                                    word ->
                                String.format(
                                    "%02d. %s",
                                    index + 1,
                                    word
                                )
                            }
                            .joinToString(
                                "\n"
                            )
                    )
                }
            },
            confirmButton = {
                Button(
                    onClick = {
                        viewModel
                            .clearRecoveryPhraseNotice()
                    }
                ) {
                    Text(
                        "یادداشت کردم"
                    )
                }
            }
        )
    }

    renameFile?.let { file ->
        AlertDialog(
            onDismissRequest = {
                renameFile = null
            },
            title = {
                Text("تغییر نام فایل")
            },
            text = {
                OutlinedTextField(
                    value = renameText,
                    onValueChange = {
                        renameText = it
                    },
                    singleLine = true
                )
            },
            confirmButton = {
                Button(
                    onClick = {
                        viewModel.renameFile(
                            file,
                            renameText
                        )
                        renameFile = null
                    }
                ) {
                    Text("ذخیره")
                }
            },
            dismissButton = {
                TextButton(
                    onClick = {
                        renameFile = null
                    }
                ) {
                    Text("انصراف")
                }
            }
        )
    }
}

private suspend fun readUriPayload(
    context: Context,
    uri: Uri
): Triple<String, String?, ByteArray>? =
    withContext(Dispatchers.IO) {
        val resolver =
            context.contentResolver

        val name = uri.lastPathSegment
            ?.substringAfterLast('/')
            ?.ifBlank {
                "ZurvanFile"
            }
            ?: "ZurvanFile"

        val mime =
            resolver.getType(uri)

        val bytes =
            resolver.openInputStream(uri)
                ?.use {
                    it.readBytes()
                }
                ?: return@withContext null

        Triple(
            name,
            mime,
            bytes
        )
    }

@Composable
private fun PlanDialog(
    initialDateKey: String,
    onDismiss: () -> Unit,
    onSave: (
        String,
        String,
        Int,
        Int,
        String,
        Boolean,
        String
    ) -> Unit
) {
    val initialParts =
        initialDateKey
            .split("-")

    var title by remember {
        mutableStateOf("")
    }

    var year by remember {
        mutableStateOf(
            initialParts
                .getOrNull(0)
                .orEmpty()
        )
    }

    var month by remember {
        mutableStateOf(
            initialParts
                .getOrNull(1)
                .orEmpty()
        )
    }

    var day by remember {
        mutableStateOf(
            initialParts
                .getOrNull(2)
                .orEmpty()
        )
    }

    var hour by remember {
        mutableStateOf("09")
    }

    var minute by remember {
        mutableStateOf("00")
    }

    var kind by remember {
        mutableStateOf("event")
    }

    var isPrivate by remember {
        mutableStateOf(false)
    }
    val iconOptions = listOf("📌","📚","🎓","💼","🏥","🏃","🎨","🎵","🎉","📝","💻","🏠","✈️","⚽","🛒","💊")
    var chosenIcon by remember { mutableStateOf("📌") }

    AlertDialog(
        onDismissRequest =
            onDismiss,
        title = {
            Text(
                "ثبت رویداد / کار"
            )
        },
        text = {
            Column(
                verticalArrangement =
                    Arrangement.spacedBy(
                        8.dp
                    )
            ) {
                OutlinedTextField(
                    value = title,
                    onValueChange = {
                        title = it
                    },
                    modifier =
                        Modifier.fillMaxWidth(),
                    label = {
                        Text("عنوان")
                    }
                )

                SeparateDateFields(
                    year = year,
                    month = month,
                    day = day,
                    onYear = {
                        year = it
                    },
                    onMonth = {
                        month = it
                    },
                    onDay = {
                        day = it
                    }
                )

                SeparateTimeFields(
                    hour = hour,
                    minute = minute,
                    onHour = {
                        hour = it
                    },
                    onMinute = {
                        minute = it
                    }
                )

                Row(
                    horizontalArrangement =
                        Arrangement.spacedBy(
                            8.dp
                        )
                ) {
                    FilterChip(
                        selected =
                            kind == "event",
                        onClick = {
                            kind = "event"
                        },
                        label = {
                            Text("رویداد")
                        }
                    )

                    FilterChip(
                        selected =
                            kind == "task",
                        onClick = {
                            kind = "task"
                        },
                        label = {
                            Text("کار")
                        }
                    )
                }

                Text("نماد برنامه روی کارت روز")
                androidx.compose.foundation.lazy.LazyRow(horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                    items(iconOptions) { symbol ->
                        FilterChip(selected = chosenIcon == symbol,
                            onClick = { chosenIcon = symbol }, label = { Text(symbol) })
                    }
                }

                Row(
                    verticalAlignment =
                        Alignment.CenterVertically
                ) {
                    Checkbox(
                        checked =
                            isPrivate,
                        onCheckedChange = {
                            isPrivate = it
                        }
                    )
                    Text("خصوصی")
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (title.isNotBlank()) {
                        onSave(
                            title,
                            dateKey(
                                year,
                                month,
                                day
                            ),
                            hour
                                .toIntOrNull()
                                ?.coerceIn(
                                    0,
                                    23
                                )
                                ?: 9,
                            minute
                                .toIntOrNull()
                                ?.coerceIn(
                                    0,
                                    59
                                )
                                ?: 0,
                            kind,
                            isPrivate,
                            chosenIcon
                        )
                    }
                }
            ) {
                Text("ثبت")
            }
        },
        dismissButton = {
            TextButton(
                onClick = onDismiss
            ) {
                Text("انصراف")
            }
        }
    )
}

@Composable
private fun ReminderDialog(
    initialDateKey: String,
    onDismiss: () -> Unit,
    onSave: (
        String,
        String,
        Int,
        Int,
        Boolean,
        Int,
        Int
    ) -> Unit
) {
    val parts =
        initialDateKey.split("-")

    var title by remember {
        mutableStateOf("")
    }

    var year by remember {
        mutableStateOf(
            parts
                .getOrNull(0)
                .orEmpty()
        )
    }

    var month by remember {
        mutableStateOf(
            parts
                .getOrNull(1)
                .orEmpty()
        )
    }

    var day by remember {
        mutableStateOf(
            parts
                .getOrNull(2)
                .orEmpty()
        )
    }

    var hour by remember {
        mutableStateOf("09")
    }

    var minute by remember {
        mutableStateOf("00")
    }

    var repeatCount by remember {
        mutableStateOf("3")
    }

    var repeatInterval by remember {
        mutableStateOf("5")
    }

    var isPrivate by remember {
        mutableStateOf(false)
    }

    AlertDialog(
        onDismissRequest =
            onDismiss,
        title = {
            Text("ثبت یادآور")
        },
        text = {
            Column(
                verticalArrangement =
                    Arrangement.spacedBy(
                        8.dp
                    )
            ) {
                OutlinedTextField(
                    value = title,
                    onValueChange = {
                        title = it
                    },
                    modifier =
                        Modifier.fillMaxWidth(),
                    label = {
                        Text("عنوان")
                    }
                )

                SeparateDateFields(
                    year,
                    month,
                    day,
                    { year = it },
                    { month = it },
                    { day = it }
                )

                SeparateTimeFields(
                    hour,
                    minute,
                    { hour = it },
                    { minute = it }
                )

                Row(
                    horizontalArrangement =
                        Arrangement.spacedBy(
                            8.dp
                        )
                ) {
                    OutlinedTextField(
                        value =
                            repeatCount,
                        onValueChange = {
                            repeatCount = it
                        },
                        modifier =
                            Modifier.weight(1f),
                        label = {
                            Text(
                                "تعداد هشدار"
                            )
                        }
                    )

                    OutlinedTextField(
                        value =
                            repeatInterval,
                        onValueChange = {
                            repeatInterval = it
                        },
                        modifier =
                            Modifier.weight(1f),
                        label = {
                            Text(
                                "فاصله دقیقه"
                            )
                        }
                    )
                }

                Row(
                    verticalAlignment =
                        Alignment.CenterVertically
                ) {
                    Checkbox(
                        checked =
                            isPrivate,
                        onCheckedChange = {
                            isPrivate = it
                        }
                    )
                    Text(
                        "خصوصی؛ متن اعلان مخفی شود"
                    )
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (title.isNotBlank()) {
                        onSave(
                            title,
                            dateKey(
                                year,
                                month,
                                day
                            ),
                            hour
                                .toIntOrNull()
                                ?.coerceIn(
                                    0,
                                    23
                                )
                                ?: 9,
                            minute
                                .toIntOrNull()
                                ?.coerceIn(
                                    0,
                                    59
                                )
                                ?: 0,
                            isPrivate,
                            repeatCount
                                .toIntOrNull()
                                ?.coerceIn(
                                    1,
                                    20
                                )
                                ?: 3,
                            repeatInterval
                                .toIntOrNull()
                                ?.coerceIn(
                                    1,
                                    1440
                                )
                                ?: 5
                        )
                    }
                }
            ) {
                Text("ثبت")
            }
        },
        dismissButton = {
            TextButton(
                onClick = onDismiss
            ) {
                Text("انصراف")
            }
        }
    )
}

@Composable
private fun OccasionDialog(
    onDismiss: () -> Unit,
    onSave: (
        String,
        CalendarPriority,
        Int,
        Int,
        Int,
        Int
    ) -> Unit
) {
    var title by remember {
        mutableStateOf("")
    }

    var calendar by remember {
        mutableStateOf(
            CalendarPriority.PERSIAN
        )
    }

    var month by remember {
        mutableStateOf("1")
    }

    var day by remember {
        mutableStateOf("1")
    }

    var hour by remember {
        mutableStateOf("09")
    }

    var minute by remember {
        mutableStateOf("00")
    }

    AlertDialog(
        onDismissRequest =
            onDismiss,
        title = {
            Text("ثبت مناسبت")
        },
        text = {
            Column(
                verticalArrangement =
                    Arrangement.spacedBy(
                        8.dp
                    )
            ) {
                OutlinedTextField(
                    value = title,
                    onValueChange = {
                        title = it
                    },
                    modifier =
                        Modifier.fillMaxWidth(),
                    label = {
                        Text("عنوان")
                    }
                )

                Row(
                    horizontalArrangement =
                        Arrangement.spacedBy(
                            4.dp
                        )
                ) {
                    CalendarPriority
                        .entries
                        .forEach {
                            FilterChip(
                                selected =
                                    calendar ==
                                        it,
                                onClick = {
                                    calendar =
                                        it
                                },
                                label = {
                                    Text(
                                        it.label
                                    )
                                }
                            )
                        }
                }

                Row(
                    horizontalArrangement =
                        Arrangement.spacedBy(
                            8.dp
                        )
                ) {
                    OutlinedTextField(
                        value = month,
                        onValueChange = {
                            month = it
                        },
                        modifier =
                            Modifier.weight(1f),
                        label = {
                            Text("ماه")
                        }
                    )

                    OutlinedTextField(
                        value = day,
                        onValueChange = {
                            day = it
                        },
                        modifier =
                            Modifier.weight(1f),
                        label = {
                            Text("روز")
                        }
                    )
                }

                SeparateTimeFields(
                    hour,
                    minute,
                    { hour = it },
                    { minute = it }
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (title.isNotBlank()) {
                        onSave(
                            title,
                            calendar,
                            month
                                .toIntOrNull()
                                ?.coerceIn(
                                    1,
                                    12
                                )
                                ?: 1,
                            day
                                .toIntOrNull()
                                ?.coerceIn(
                                    1,
                                    31
                                )
                                ?: 1,
                            hour
                                .toIntOrNull()
                                ?.coerceIn(
                                    0,
                                    23
                                )
                                ?: 9,
                            minute
                                .toIntOrNull()
                                ?.coerceIn(
                                    0,
                                    59
                                )
                                ?: 0
                        )
                    }
                }
            ) {
                Text("ثبت")
            }
        },
        dismissButton = {
            TextButton(
                onClick = onDismiss
            ) {
                Text("انصراف")
            }
        }
    )
}

@Composable
private fun JumpToDateDialog(
    onDismiss: () -> Unit,
    onJump: (
        AndroidDateConversion
    ) -> Unit
) {
    var calendar by remember {
        mutableStateOf(
            CalendarPriority.PERSIAN
        )
    }

    var year by remember {
        mutableStateOf("1405")
    }

    var month by remember {
        mutableStateOf("1")
    }

    var day by remember {
        mutableStateOf("1")
    }

    var error by remember {
        mutableStateOf<String?>(null)
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text("پرش به تاریخ")
        },
        text = {
            Column(
                verticalArrangement =
                    Arrangement.spacedBy(
                        8.dp
                    )
            ) {
                Row(
                    horizontalArrangement =
                        Arrangement.spacedBy(
                            4.dp
                        )
                ) {
                    CalendarPriority
                        .entries
                        .forEach {
                            FilterChip(
                                selected =
                                    calendar ==
                                        it,
                                onClick = {
                                    calendar =
                                        it
                                },
                                label = {
                                    Text(
                                        it.label
                                    )
                                }
                            )
                        }
                }

                SeparateDateFields(
                    year,
                    month,
                    day,
                    { year = it },
                    { month = it },
                    { day = it }
                )

                error?.let {
                    Text(
                        it,
                        color =
                            MaterialTheme
                                .colorScheme
                                .error
                    )
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    try {
                        val result =
                            AndroidCalendarEngine
                                .convert(
                                    calendar,
                                    year
                                        .toInt(),
                                    month
                                        .toInt(),
                                    day
                                        .toInt()
                                )
                        onJump(result)
                    } catch (_: Exception) {
                        error =
                            "تاریخ واردشده معتبر نیست."
                    }
                }
            ) {
                Text("پرش")
            }
        },
        dismissButton = {
            TextButton(
                onClick = onDismiss
            ) {
                Text("انصراف")
            }
        }
    )
}

@Composable
private fun DateConverterDialog(
    onDismiss: () -> Unit
) {
    var calendar by remember {
        mutableStateOf(
            CalendarPriority.PERSIAN
        )
    }

    var year by remember {
        mutableStateOf("1405")
    }

    var month by remember {
        mutableStateOf("1")
    }

    var day by remember {
        mutableStateOf("1")
    }

    var result by remember {
        mutableStateOf<
            AndroidDateConversion?
        >(null)
    }

    var error by remember {
        mutableStateOf<String?>(null)
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text("مبدل تاریخ")
        },
        text = {
            Column(
                verticalArrangement =
                    Arrangement.spacedBy(
                        8.dp
                    )
            ) {
                Row(
                    horizontalArrangement =
                        Arrangement.spacedBy(
                            4.dp
                        )
                ) {
                    CalendarPriority
                        .entries
                        .forEach {
                            FilterChip(
                                selected =
                                    calendar ==
                                        it,
                                onClick = {
                                    calendar =
                                        it
                                },
                                label = {
                                    Text(
                                        it.label
                                    )
                                }
                            )
                        }
                }

                SeparateDateFields(
                    year,
                    month,
                    day,
                    { year = it },
                    { month = it },
                    { day = it }
                )

                Button(
                    onClick = {
                        try {
                            result =
                                AndroidCalendarEngine
                                    .convert(
                                        calendar,
                                        year
                                            .toInt(),
                                        month
                                            .toInt(),
                                        day
                                            .toInt()
                                    )
                            error = null
                        } catch (_: Exception) {
                            result = null
                            error =
                                "تاریخ واردشده معتبر نیست."
                        }
                    },
                    modifier =
                        Modifier.fillMaxWidth()
                ) {
                    Text("تبدیل")
                }

                result?.let {
                    Card(
                        modifier =
                            Modifier
                                .fillMaxWidth()
                    ) {
                        Column(
                            modifier =
                                Modifier.padding(
                                    12.dp
                                ),
                            verticalArrangement =
                                Arrangement.spacedBy(
                                    5.dp
                                )
                        ) {
                            Text(
                                "شمسی: " +
                                    toPersianDigits(
                                        it.persianYear
                                            .toString()
                                    ) +
                                    "/" +
                                    toPersianDigits(
                                        it.persianMonth
                                            .toString()
                                    ) +
                                    "/" +
                                    toPersianDigits(
                                        it.persianDay
                                            .toString()
                                    )
                            )

                            Text(
                                "میلادی: " +
                                    it.gregorianYear +
                                    "/" +
                                    it.gregorianMonth +
                                    "/" +
                                    it.gregorianDay
                            )

                            Text(
                                "قمری: " +
                                    toArabicDigits(
                                        it.hijriYear
                                            .toString()
                                    ) +
                                    "/" +
                                    toArabicDigits(
                                        it.hijriMonth
                                            .toString()
                                    ) +
                                    "/" +
                                    toArabicDigits(
                                        it.hijriDay
                                            .toString()
                                    )
                            )
                        }
                    }
                }

                error?.let {
                    Text(
                        it,
                        color =
                            MaterialTheme
                                .colorScheme
                                .error
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = onDismiss
            ) {
                Text("بستن")
            }
        }
    )
}

@Composable
private fun SeparateDateFields(
    year: String,
    month: String,
    day: String,
    onYear: (String) -> Unit,
    onMonth: (String) -> Unit,
    onDay: (String) -> Unit
) {
    Row(
        horizontalArrangement =
            Arrangement.spacedBy(6.dp)
    ) {
        OutlinedTextField(
            value = year,
            onValueChange = onYear,
            modifier =
                Modifier.weight(1.3f),
            label = {
                Text("سال")
            }
        )

        OutlinedTextField(
            value = month,
            onValueChange = onMonth,
            modifier =
                Modifier.weight(1f),
            label = {
                Text("ماه")
            }
        )

        OutlinedTextField(
            value = day,
            onValueChange = onDay,
            modifier =
                Modifier.weight(1f),
            label = {
                Text("روز")
            }
        )
    }
}

@Composable
private fun SeparateTimeFields(
    hour: String,
    minute: String,
    onHour: (String) -> Unit,
    onMinute: (String) -> Unit
) {
    Row(
        horizontalArrangement =
            Arrangement.spacedBy(8.dp)
    ) {
        OutlinedTextField(
            value = hour,
            onValueChange = onHour,
            modifier =
                Modifier.weight(1f),
            label = {
                Text("ساعت")
            }
        )

        OutlinedTextField(
            value = minute,
            onValueChange = onMinute,
            modifier =
                Modifier.weight(1f),
            label = {
                Text("دقیقه")
            }
        )
    }
}

@Composable
private fun ScreenHeader(
    title: String,
    actionText: String,
    onAction: () -> Unit
) {
    Row(
        modifier =
            Modifier.fillMaxWidth(),
        verticalAlignment =
            Alignment.CenterVertically,
        horizontalArrangement =
            Arrangement.SpaceBetween
    ) {
        Text(
            title,
            style =
                MaterialTheme
                    .typography
                    .headlineSmall
        )

        Button(
            onClick = onAction
        ) {
            Icon(
                Icons.Outlined.Add,
                contentDescription = null
            )
            Spacer(
                Modifier.width(4.dp)
            )
            Text(actionText)
        }
    }
}

@Composable
private fun SectionTitle(
    text: String
) {
    Text(
        text,
        style =
            MaterialTheme
                .typography
                .titleMedium,
        fontWeight =
            FontWeight.SemiBold
    )
}

@Composable
private fun EmptyState(
    title: String,
    icon:
        androidx.compose.ui.graphics.vector.ImageVector
) {
    Column(
        modifier =
            Modifier.fillMaxWidth(),
        horizontalAlignment =
            Alignment.CenterHorizontally,
        verticalArrangement =
            Arrangement.spacedBy(
                8.dp
            )
    ) {
        Icon(
            icon,
            contentDescription = null,
            modifier =
                Modifier.size(48.dp)
        )
        Text(title)
    }
}

@Composable
private fun SettingsCard(
    title: String,
    content:
        @Composable ColumnScope.() -> Unit
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = MaterialTheme.shapes.medium
    ) {
        Column(
            modifier =
                Modifier.padding(16.dp),
            verticalArrangement =
                Arrangement.spacedBy(
                    8.dp
                )
        ) {
            Text(
                title,
                style =
                    MaterialTheme
                        .typography
                        .titleMedium,
                fontWeight =
                    FontWeight.SemiBold
            )
            content()
        }
    }
}

private fun dateKey(
    year: String,
    month: String,
    day: String
): String {
    val y =
        year.toIntOrNull()
            ?: 1400
    val m =
        month.toIntOrNull()
            ?.coerceIn(
                1,
                12
            )
            ?: 1
    val d =
        day.toIntOrNull()
            ?.coerceIn(
                1,
                31
            )
            ?: 1

    return String.format(
        "%04d-%02d-%02d",
        y,
        m,
        d
    )
}

private fun formatTime(
    hour: Int,
    minute: Int
): String = String.format(
    "%02d:%02d",
    hour,
    minute
)

private fun formatBytes(
    value: Long
): String = when {
    value >= 1024L * 1024L ->
        String.format(
            "%.1f MB",
            value /
                (1024.0 * 1024.0)
        )

    value >= 1024L ->
        String.format(
            "%.1f KB",
            value / 1024.0
        )

    else ->
        value.toString() +
            " B"
}

private fun toPersianDigits(
    value: String
): String =
    value
        .replace('0', '۰')
        .replace('1', '۱')
        .replace('2', '۲')
        .replace('3', '۳')
        .replace('4', '۴')
        .replace('5', '۵')
        .replace('6', '۶')
        .replace('7', '۷')
        .replace('8', '۸')
        .replace('9', '۹')

private fun toArabicDigits(
    value: String
): String =
    value
        .replace('0', '٠')
        .replace('1', '١')
        .replace('2', '٢')
        .replace('3', '٣')
        .replace('4', '٤')
        .replace('5', '٥')
        .replace('6', '٦')
        .replace('7', '٧')
        .replace('8', '٨')
        .replace('9', '٩')
