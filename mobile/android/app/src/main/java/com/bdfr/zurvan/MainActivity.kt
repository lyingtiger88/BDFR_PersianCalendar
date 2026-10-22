package com.bdfr.zurvan

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Cloud
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
    Cloud("کلاد"),
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
                                    ZurvanTab.Cloud -> Icons.Outlined.Cloud
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
            ZurvanTab.Cloud -> ZurvanCloudShell(Modifier.padding(padding))
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
private fun ZurvanCloudShell(modifier: Modifier = Modifier) {
    var endpoint by remember {
        mutableStateOf("https://s3.ir-thr-at1.arvanstorage.ir")
    }
    var region by remember { mutableStateOf("ir-thr-at1") }
    var bucket by remember { mutableStateOf("") }

    Column(
        modifier = modifier
            .fillMaxSize()
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Text("Zurvan Cloud", style = MaterialTheme.typography.headlineSmall)

        Card(
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(18.dp)
        ) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text(
                    "End-to-End Encrypted",
                    style = MaterialTheme.typography.titleMedium
                )
                Text(
                    "اطلاعات قبل از Upload روی همین گوشی رمز می‌شوند. ابر آروان فقط ciphertext دریافت می‌کند."
                )

                OutlinedTextField(
                    value = endpoint,
                    onValueChange = { endpoint = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text("S3 Endpoint") }
                )

                OutlinedTextField(
                    value = region,
                    onValueChange = { region = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text("Region") }
                )

                OutlinedTextField(
                    value = bucket,
                    onValueChange = { bucket = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text("Bucket") }
                )

                Text(
                    "Vault Key در Android Keystore محافظت می‌شود و برای انتقال به دستگاه دیگر از Companion یا Recovery Key استفاده خواهد شد.",
                    style = MaterialTheme.typography.bodySmall
                )

                Button(onClick = { }) {
                    Text("تنظیم Zurvan Cloud")
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
