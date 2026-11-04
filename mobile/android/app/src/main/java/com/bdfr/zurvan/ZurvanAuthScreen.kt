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
    var busy by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf("") }
    LaunchedEffect(Unit) {
        if (current != null) current = auth.validSession()
    }
    Column(modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("حساب زروان", style = MaterialTheme.typography.titleLarge)
        Text("ورود به حساب مستقل از فعال‌سازی لایسنس است.", style = MaterialTheme.typography.bodySmall)
        if (!auth.configured) {
            Text("اتصال Supabase برای این نسخه تنظیم نشده است.", color = MaterialTheme.colorScheme.error)
        } else if (current != null) {
            Text("وارد شده: " + (current?.email ?: "حساب کاربری"))
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
