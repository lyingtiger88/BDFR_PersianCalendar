package com.bdfr.zurvan

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Link
import androidx.compose.material.icons.outlined.Person
import androidx.compose.material.icons.outlined.Security
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch

private val ZurvanBlue = Color(0xFF2879E8)
private val ZurvanInk = Color(0xFF14233D)
private val ZurvanMuted = Color(0xFF65738A)
private val ZurvanPaleBlue = Color(0xFFEAF3FF)

/** Account and device-link panel, styled to match the approved Zurvan settings mockup. */
@Composable
fun ZurvanAuthScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val auth = remember(context) { ZurvanAuth(context.applicationContext) }
    val scope = rememberCoroutineScope()
    var current by remember { mutableStateOf(auth.session()) }
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var enrollmentUsername by remember { mutableStateOf("") }
    var showEnrollmentDialog by remember { mutableStateOf(false) }
    var busy by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf("") }
    var licenseStatus by remember { mutableStateOf<ZurvanLicenseStatus?>(null) }
    var licenseBusy by remember { mutableStateOf(false) }
    val needsEnrollment = licenseStatus?.let {
        it.accountState == "registration_required" || it.accountState == "provisional"
    } == true

    LaunchedEffect(Unit) {
        if (current != null) current = auth.validSession()
    }
    LaunchedEffect(current?.accessToken) {
        val token = current?.accessToken ?: return@LaunchedEffect
        licenseBusy = true
        try { licenseStatus = ZurvanLicenseApi().status(token) }
        catch (e: Exception) { message = e.message ?: "دریافت وضعیت لایسنس ناموفق بود." }
        finally { licenseBusy = false }
    }

    if (showEnrollmentDialog) {
        AlertDialog(
            onDismissRequest = { if (!busy) showEnrollmentDialog = false },
            title = { Text("تکمیل ثبت‌نام زروان", color = ZurvanInk, fontWeight = FontWeight.Bold) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("شناسه کاربری دلخواه خود را وارد کنید تا حساب و دستگاه به زروان متصل شوند.")
                    OutlinedTextField(
                        value = enrollmentUsername,
                        onValueChange = { enrollmentUsername = it.lowercase().filter { c -> c.isLetterOrDigit() || c == '_' || c == '-' }.take(24) },
                        label = { Text("شناسه کاربری زروان") },
                        supportingText = { Text("حروف انگلیسی، عدد، خط تیره یا زیرخط") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                }
            },
            confirmButton = {
                Button(
                    enabled = !busy && enrollmentUsername.length >= 3,
                    onClick = {
                        showEnrollmentDialog = false
                        busy = true
                        scope.launch {
                            try {
                                val valid = auth.validSession()
                                    ?: error("نشست منقضی شده است؛ دوباره وارد شوید.")
                                current = valid
                                val result = ZurvanDeviceEnrollment(context.applicationContext)
                                    .enroll(valid, enrollmentUsername.trim())
                                licenseStatus = ZurvanLicenseApi().status(valid.accessToken)
                                message = "حساب و دستگاه با موفقیت ثبت شدند: " + result.optString("username")
                            } catch (e: Exception) {
                                message = e.message ?: "ثبت دستگاه ناموفق بود."
                            } finally { busy = false }
                        }
                    }
                ) { Text("ثبت و اتصال دستگاه") }
            },
            dismissButton = {
                TextButton(onClick = { showEnrollmentDialog = false }) { Text("انصراف") }
            }
        )
    }

    Column(
        modifier = modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(horizontal = 18.dp, vertical = 14.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Surface(shape = RoundedCornerShape(50), color = ZurvanPaleBlue, modifier = Modifier.size(46.dp)) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(Icons.Outlined.Person, contentDescription = null, tint = ZurvanBlue, modifier = Modifier.size(27.dp))
                }
            }
            Column(modifier = Modifier.weight(1f)) {
                Text("حساب زروان", style = MaterialTheme.typography.headlineSmall, color = ZurvanInk, fontWeight = FontWeight.Bold)
                Text("ورود به حساب مستقل از فعال‌سازی لایسنس است.", style = MaterialTheme.typography.bodySmall, color = ZurvanMuted)
            }
        }

        if (!auth.configured) {
            Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer)) {
                Text("اتصال Supabase برای این نسخه تنظیم نشده است.", color = MaterialTheme.colorScheme.error, modifier = Modifier.padding(16.dp))
            }
        } else if (current != null) {
            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(20.dp),
                border = androidx.compose.foundation.BorderStroke(1.dp, Color(0xFFCADFFF)),
                colors = CardDefaults.cardColors(containerColor = Color.White.copy(alpha = 0.82f))
            ) {
                Column(modifier = Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Text("ایمیل حساب", color = ZurvanMuted, style = MaterialTheme.typography.labelMedium)
                        Text(current?.email ?: "حساب کاربری", color = ZurvanInk, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                    }
                    HorizontalDivider(color = Color(0xFFDCE6F3))
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Icon(Icons.Outlined.Security, contentDescription = null, tint = ZurvanBlue)
                        Text("وضعیت لایسنس", fontWeight = FontWeight.SemiBold, color = ZurvanInk)
                        Spacer(Modifier.weight(1f))
                        val licenseLabel = licenseStatus?.licenseAccountState ?: if (licenseBusy) "در حال بررسی" else "نامشخص"
                        Surface(shape = RoundedCornerShape(50), color = ZurvanPaleBlue) {
                            Text(licenseLabel, color = ZurvanInk, modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp), style = MaterialTheme.typography.labelMedium)
                        }
                    }
                    Text(
                        "نسخه ${BuildConfig.VERSION_NAME}  |  بیلد ${BuildConfig.VERSION_CODE}  |  اکشن #${BuildConfig.CI_RUN_NUMBER}",
                        color = ZurvanMuted,
                        style = MaterialTheme.typography.bodySmall
                    )
                    licenseStatus?.let { status ->
                        Text("وضعیت سرور: حساب ${status.accountState}  |  لایسنس ${status.licenseAccountState}", color = ZurvanMuted, style = MaterialTheme.typography.bodySmall)
                        when {
                            status.accountState == "registration_required" || status.accountState == "provisional" ->
                                Text("برای تکمیل حساب، شناسه کاربری و اتصال دستگاه را ثبت کنید.", color = ZurvanInk)
                            status.licenseAccountState == "not_found" || status.licenseAccountState == "not_linked" ->
                                Text("حساب کاربری فعال است؛ حساب لایسنس هنوز به آن متصل نشده است.", color = ZurvanInk)
                            status.licenses.isEmpty() ->
                                Text("حساب متصل است؛ هنوز لایسنسی برای آن صادر نشده است.", color = ZurvanInk)
                            else -> Text("تعداد لایسنس‌ها: ${status.licenses.size}", color = ZurvanInk)
                        }
                    }
                    if (licenseBusy) LinearProgressIndicator(modifier = Modifier.fillMaxWidth(), color = ZurvanBlue)
                    if (needsEnrollment) {
                        Button(
                            onClick = { showEnrollmentDialog = true },
                            enabled = !busy && !licenseBusy,
                            modifier = Modifier.fillMaxWidth().heightIn(min = 54.dp),
                            shape = RoundedCornerShape(18.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = ZurvanBlue)
                        ) {
                            Icon(Icons.Outlined.Link, contentDescription = null)
                            Spacer(Modifier.width(8.dp))
                            Text("تکمیل ثبت‌نام و اتصال دستگاه", fontWeight = FontWeight.Bold)
                        }
                    } else {
                        Button(
                            onClick = {
                                val token = current?.accessToken ?: return@Button
                                licenseBusy = true
                                scope.launch {
                                    try { licenseStatus = ZurvanLicenseApi().status(token); message = "وضعیت لایسنس به‌روز شد." }
                                    catch (e: Exception) { message = e.message ?: "خطا در بررسی لایسنس" }
                                    finally { licenseBusy = false }
                                }
                            },
                            enabled = !busy && !licenseBusy,
                            modifier = Modifier.fillMaxWidth(),
                            shape = RoundedCornerShape(16.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = ZurvanBlue)
                        ) { Text("بررسی مجدد لایسنس") }
                    }
                }
            }
            OutlinedButton(
                enabled = !busy,
                onClick = {
                    busy = true
                    scope.launch {
                        try { auth.signOut(); current = null; licenseStatus = null; message = "از حساب خارج شدید." }
                        catch (e: Exception) { message = e.message ?: "خطا در خروج" }
                        finally { busy = false }
                    }
                },
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp)
            ) { Text("خروج از حساب") }
        } else {
            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(20.dp),
                colors = CardDefaults.cardColors(containerColor = Color.White.copy(alpha = 0.86f)),
                border = androidx.compose.foundation.BorderStroke(1.dp, Color(0xFFCADFFF))
            ) {
                Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    OutlinedTextField(email, { email = it.take(254) }, label = { Text("ایمیل") },
                        singleLine = true, modifier = Modifier.fillMaxWidth())
                    OutlinedTextField(password, { password = it.take(128) }, label = { Text("رمز عبور") },
                        singleLine = true, visualTransformation = PasswordVisualTransformation(),
                        modifier = Modifier.fillMaxWidth())
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Button(enabled = !busy, onClick = {
                            busy = true
                            scope.launch {
                                try { current = auth.signIn(email, password); password = ""; message = "ورود موفق" }
                                catch (e: Exception) { message = e.message ?: "ورود ناموفق" }
                                finally { busy = false }
                            }
                        }, modifier = Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = ZurvanBlue)) { Text("ورود") }
                        OutlinedButton(enabled = !busy, onClick = {
                            busy = true
                            scope.launch {
                                try {
                                    val signedIn = auth.signUp(email, password)
                                    password = ""
                                    current = if (signedIn) auth.session() else null
                                    message = if (signedIn) "ثبت‌نام و ورود موفق" else "ایمیل تأیید را بررسی کنید."
                                } catch (e: Exception) { message = e.message ?: "ثبت‌نام ناموفق" }
                                finally { busy = false }
                            }
                        }, modifier = Modifier.weight(1f)) { Text("ثبت‌نام") }
                    }
                    TextButton(enabled = !busy, onClick = {
                        busy = true
                        scope.launch {
                            try { auth.resendSignupConfirmation(email); message = "لینک تأیید جدید ارسال شد. صندوق ورودی و هرزنامه را بررسی کنید." }
                            catch (e: Exception) { message = e.message ?: "ارسال مجدد ناموفق" }
                            finally { busy = false }
                        }
                    }) { Text("ارسال مجدد ایمیل تأیید") }
                    TextButton(enabled = !busy, onClick = {
                        busy = true
                        scope.launch {
                            try { auth.sendPasswordRecovery(email); message = "اگر این ایمیل حسابی داشته باشد، راهنمای بازیابی ارسال می‌شود." }
                            catch (e: Exception) { message = e.message ?: "ارسال بازیابی ناموفق" }
                            finally { busy = false }
                        }
                    }) { Text("بازیابی رمز عبور") }
                }
            }
        }

        if (busy) LinearProgressIndicator(modifier = Modifier.fillMaxWidth(), color = ZurvanBlue)
        if (message.isNotBlank()) {
            Text(message, color = ZurvanMuted, style = MaterialTheme.typography.bodySmall, textAlign = TextAlign.Start)
        }
    }
}
