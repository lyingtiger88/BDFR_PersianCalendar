package com.bdfr.zurvan

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material.icons.outlined.TaskAlt
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme {
                ZurvanMobileShell()
            }
        }
    }
}

private enum class ZurvanTab(val label: String) {
    Calendar("تقویم"),
    Plans("برنامه‌ها"),
    Reminders("یادآورها"),
    Companion("همراه"),
    Settings("تنظیمات")
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ZurvanMobileShell() {
    var selected by remember { mutableStateOf(ZurvanTab.Calendar) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Zurvan | زروان") }
            )
        },
        bottomBar = {
            NavigationBar {
                ZurvanTab.entries.forEach { tab ->
                    NavigationBarItem(
                        selected = selected == tab,
                        onClick = { selected = tab },
                        icon = {
                            Icon(
                                imageVector = when (tab) {
                                    ZurvanTab.Calendar -> Icons.Outlined.CalendarMonth
                                    ZurvanTab.Plans -> Icons.Outlined.TaskAlt
                                    ZurvanTab.Reminders -> Icons.Outlined.Notifications
                                    ZurvanTab.Companion -> Icons.Outlined.Devices
                                    ZurvanTab.Settings -> Icons.Outlined.Settings
                                },
                                contentDescription = tab.label
                            )
                        },
                        label = { Text(tab.label) }
                    )
                }
            }
        },
        floatingActionButton = {
            if (selected == ZurvanTab.Calendar || selected == ZurvanTab.Plans) {
                FloatingActionButton(onClick = { }) {
                    Text("+")
                }
            }
        }
    ) { padding ->
        when (selected) {
            ZurvanTab.Calendar -> CalendarShell(Modifier.padding(padding))
            ZurvanTab.Companion -> CompanionShell(Modifier.padding(padding))
            else -> PlaceholderScreen(selected.label, Modifier.padding(padding))
        }
    }
}

@Composable
private fun CalendarShell(modifier: Modifier = Modifier) {
    Column(
        modifier = modifier
            .fillMaxSize()
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text("‹", style = MaterialTheme.typography.headlineMedium)
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text("مهر ۱۴۰۵", style = MaterialTheme.typography.titleLarge)
                Text("Persian · Hijri · Gregorian", style = MaterialTheme.typography.labelMedium)
            }
            Text("›", style = MaterialTheme.typography.headlineMedium)
        }

        Card(
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(18.dp)
        ) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Text("نمای تقویم ماهانه", style = MaterialTheme.typography.titleMedium)
                Text("تقویم اول بزرگ، تقویم دوم بالا-راست و تقویم سوم پایین-چپ نمایش داده می‌شود.")
                Text("این پوسته به Feature Parity Contract متصل است و قابلیت‌های Windows به همین UI منتقل می‌شوند.")
            }
        }
    }
}

@Composable
private fun CompanionShell(modifier: Modifier = Modifier) {
    Column(
        modifier = modifier
            .fillMaxSize()
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Text("Companion", style = MaterialTheme.typography.headlineSmall)
        Card(
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(18.dp)
        ) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text("جفت‌سازی با رایانه", style = MaterialTheme.typography.titleMedium)
                Text("QR یا کد ۶ رقمی برای Pairing؛ سپس همگام‌سازی دوطرفه و Local-first.")
                Button(onClick = { }) {
                    Text("جفت‌سازی دستگاه")
                }
            }
        }
    }
}

@Composable
private fun PlaceholderScreen(title: String, modifier: Modifier = Modifier) {
    Box(modifier = modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text("$title · در حال توسعه")
    }
}
