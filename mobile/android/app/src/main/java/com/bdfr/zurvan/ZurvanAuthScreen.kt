package com.bdfr.zurvan

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch

/** Can be embedded in both legacy activation and full settings screens. */
@Composable
fun ZurvanAuthScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val auth = remember(context) { ZurvanAuth(context.applicationContext) }
    val scope = rememberCoroutineScope()
    var current by remember { mutableStateOf(auth.session()) }
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var enrollmentUsername by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf("") }
    var licenseStatus by remember { mutableStateOf<ZurvanLicenseStatus?>(null) }
    var licenseBusy by remember { mutableStateOf(false) }
    val needsEnrollment = licenseStatus?.let { it.accountState == "registration_required" || it.accountState == "provisional" } == true
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
    Column(modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("حساب زروان", style = MaterialTheme.typography.titleLarge)
        Text("ورود به حساب مستقل از فعال‌سازی لایسنس است.", style = MaterialTheme.typography.bodySmall)
        if (!auth.configured) {
            Text("اتصال Supabase برای این نسخه تنظیم نشده است.", color = MaterialTheme.colorScheme.error)
        } else if (current != null) {
            Text("وارد شده: " + (current?.email ?: "حساب کاربری"))
            Text("نسخه ${BuildConfig.VERSION_NAME} | بیلد ${BuildConfig.VERSION_CODE} | اکشن #${BuildConfig.CI_RUN_NUMBER}", style = MaterialTheme.typography.bodySmall)
            Text("وضعیت لایسنس", style = MaterialTheme.typography.titleMedium)
            licenseStatus?.let { Text("وضعیت سرور: حساب=${it.accountState} | لایسنس=${it.licenseAccountState}", style = MaterialTheme.typography.bodySmall) }
            if (licenseBusy) LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
            licenseStatus?.let { status ->
                when {
                    status.accountState == "registration_required" || status.accountState == "provisional" ->
                        Text("ایمیل تأیید شده است، اما ثبت دستگاه و تکمیل حساب زروان هنوز انجام نشده است.")
                    status.licenseAccountState == "not_found" || status.licenseAccountState == "not_linked" ->
                        Text("حساب لایسنس هنوز به حساب زروان متصل نشده است.")
                    status.licenses.isEmpty() ->
                        Text("حساب متصل است؛ هنوز لایسنسی برای آن صادر نشده است.")
                    else -> Text("تعداد لایسنس‌ها: ${status.licenses.size}")
                }
                if (needsEnrollment) {
                    Text("مرحله بعد: ثبت شناسه و اتصال دستگاه", style = MaterialTheme.typography.titleMedium)
                    Button(
                        modifier = Modifier.fillMaxWidth(),
                        enabled = !busy && !licenseBusy,
                        onClick = {
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
                    }) { Text("تکمیل ثبت‌نام و اتصال دستگاه") }
                    OutlinedTextField(
                        value = enrollmentUsername,
                        onValueChange = { enrollmentUsername = it.lowercase().take(24) },
                        label = { Text("شناسه کاربری زروان") },
                        supportingText = { Text("ابتدا شناسه را وارد کن، سپس دکمه بالای آن را بزن.") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                }
                status.licenses.forEach { item ->
                    Text("${item.productId} • ${item.type} • ${item.status}")
                }
            }
            OutlinedButton(enabled = !busy && !licenseBusy, onClick = {
                val token = current?.accessToken ?: return@OutlinedButton
                licenseBusy = true
                scope.launch {
                    try { licenseStatus = ZurvanLicenseApi().status(token); message = "وضعیت لایسنس به‌روز شد." }
                    catch (e: Exception) { message = e.message ?: "خطا در بررسی لایسنس" }
                    finally { licenseBusy = false }
                }
            }) { Text("بررسی مجدد لایسنس") }
            Button(enabled = !busy, onClick = {
                busy = true
                scope.launch {
                    try { auth.signOut(); current = null; message = "از حساب خارج شدید." }
                    catch (e: Exception) { message = e.message ?: "خطا در خروج" }
                    finally { busy = false }
                }
            }) { Text("خروج از حساب") }
        } else {
            OutlinedTextField(email, { email = it.take(254) }, label = { Text("ایمیل") },
                singleLine = true, modifier = Modifier.fillMaxWidth())
            OutlinedTextField(password, { password = it.take(128) }, label = { Text("رمز عبور") },
                singleLine = true, visualTransformation = PasswordVisualTransformation(),
                modifier = Modifier.fillMaxWidth())
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(enabled = !busy, onClick = {
                    busy = true
                    scope.launch {
                        try { current = auth.signIn(email, password); password = ""; message = "ورود موفق" }
                        catch (e: Exception) { message = e.message ?: "ورود ناموفق" }
                        finally { busy = false }
                    }
                }) { Text("ورود") }
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
                }) { Text("ثبت‌نام") }
            }
            TextButton(enabled = !busy, onClick = {
                busy = true
                scope.launch {
                    try {
                        auth.resendSignupConfirmation(email)
                        message = "لینک تأیید جدید ارسال شد. صندوق ورودی و پوشه هرزنامه را بررسی کنید."
                    } catch (e: Exception) { message = e.message ?: "ارسال مجدد ناموفق" }
                    finally { busy = false }
                }
            }) { Text("ارسال مجدد ایمیل تأیید") }
            TextButton(enabled = !busy, onClick = {
                busy = true
                scope.launch {
                    try {
                        auth.sendPasswordRecovery(email)
                        message = "اگر این ایمیل حسابی داشته باشد، راهنمای بازیابی ارسال می‌شود."
                    } catch (e: Exception) { message = e.message ?: "ارسال بازیابی ناموفق" }
                    finally { busy = false }
                }
            }) { Text("بازیابی رمز عبور") }
        }
        if (busy) LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
        if (message.isNotBlank()) Text(message, style = MaterialTheme.typography.bodySmall)
    }
}
