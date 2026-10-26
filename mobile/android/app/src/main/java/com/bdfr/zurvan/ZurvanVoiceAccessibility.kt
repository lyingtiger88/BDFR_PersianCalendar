package com.bdfr.zurvan

import android.content.Intent
import android.speech.RecognizerIntent
import android.speech.tts.TextToSpeech
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import java.util.Locale

/** Voice accessibility tools based on Android's installed speech services. */
@Composable
fun ZurvanVoiceAccessibility() {
    val context = LocalContext.current
    var text by remember { mutableStateOf("") }
    var status by remember { mutableStateOf("") }
    var speaking by remember { mutableStateOf(false) }
    var ttsReady by remember { mutableStateOf(false) }
    var engine by remember { mutableStateOf<TextToSpeech?>(null) }
    val speechInput = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        val spoken = result.data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)?.firstOrNull()
        if (result.resultCode == android.app.Activity.RESULT_OK && !spoken.isNullOrBlank()) {
            text = spoken
            status = "گفتار به متن تبدیل شد؛ پیش از ذخیره یا استفاده متن را بررسی کنید."
        } else status = "متنی دریافت نشد یا تشخیص گفتار لغو شد."
    }
    DisposableEffect(context) {
        val tts = TextToSpeech(context) { code ->
            if (code == TextToSpeech.SUCCESS) {
                engine?.let { instance ->
                    val available = instance.setLanguage(Locale.forLanguageTag("fa-IR"))
                    ttsReady = available != TextToSpeech.LANG_MISSING_DATA &&
                        available != TextToSpeech.LANG_NOT_SUPPORTED
                    status = if (ttsReady) "موتور گفتار فارسی آماده است." else
                        "صدای فارسی نصب نیست؛ از تنظیمات گوشی بسته فارسی موتور گفتار را نصب کنید."
                }
            } else status = "موتور متن‌به‌گفتار روی دستگاه آماده نیست."
        }
        engine = tts
        onDispose {
            tts.stop()
            tts.shutdown()
            engine = null
        }
    }
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text("گفتار به متن و متن به گفتار", style = MaterialTheme.typography.titleMedium)
        Text("پشتیبانی از فارسی به سرویس تشخیص گفتار و موتور صوتی نصب‌شده روی گوشی بستگی دارد. پردازش گفتار ممکن است توسط سرویس دستگاه به‌صورت آنلاین انجام شود.",
            style = MaterialTheme.typography.bodySmall)
        OutlinedTextField(value = text, onValueChange = { text = it },
            label = { Text("متن برای خواندن یا ویرایش") },
            modifier = Modifier.fillMaxWidth(), minLines = 3)
        Button(onClick = {
            val request = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
                putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
                putExtra(RecognizerIntent.EXTRA_LANGUAGE, "fa-IR")
                putExtra(RecognizerIntent.EXTRA_LANGUAGE_PREFERENCE, "fa-IR")
                putExtra(RecognizerIntent.EXTRA_PROMPT, "متن خود را به فارسی بگویید")
            }
            try { speechInput.launch(request) }
            catch (_: Exception) { status = "سرویس تشخیص گفتار روی این گوشی در دسترس نیست." }
        }, modifier = Modifier.fillMaxWidth()) { Text("تبدیل صدا به متن") }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(enabled = ttsReady && text.isNotBlank(), onClick = {
                engine?.speak(text, TextToSpeech.QUEUE_FLUSH, null, "zurvan_voice_preview")
                speaking = true
            }) { Text("خواندن متن") }
            OutlinedButton(onClick = {
                engine?.stop()
                speaking = false
            }, enabled = speaking) { Text("توقف صدا") }
        }
        if (status.isNotBlank()) Text(status, style = MaterialTheme.typography.bodySmall)
    }
}
