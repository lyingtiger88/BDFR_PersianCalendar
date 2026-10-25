package com.bdfr.zurvan

import android.content.Context
import androidx.compose.runtime.compositionLocalOf

/**
 * First rollout of in-app localization. Remaining hard-coded copy can migrate
 * to this catalog gradually without changing persisted calendar data.
 */
object ZurvanLanguages {
    val options = linkedMapOf(
        "fa" to "فارسی", "en" to "English", "ar" to "العربية",
        "de" to "Deutsch", "tr" to "Türkçe", "fr" to "Français",
        "es" to "Español", "ru" to "Русский"
    )
    val current = compositionLocalOf { "fa" }
    val translations: Map<String, Map<String, String>> = mapOf(
        "fa" to mapOf("calendar" to "تقویم", "desk" to "زروان دسک", "plans" to "برنامه‌ها", "occasions" to "مناسبت‌ها", "reminders" to "یادآورها", "companion" to "همراه", "settings" to "تنظیمات", "language" to "زبان برنامه", "languageNote" to "زبان رابط برنامه را انتخاب کنید. ترجمه بخش‌های قدیمی به‌تدریج کامل می‌شود."),
        "en" to mapOf("calendar" to "Calendar", "desk" to "Zurvan Desk", "plans" to "Plans", "occasions" to "Occasions", "reminders" to "Reminders", "companion" to "Companion", "settings" to "Settings", "language" to "App language", "languageNote" to "Select the app language. Some older screens are still being translated."),
        "ar" to mapOf("calendar" to "التقويم", "desk" to "مكتب زروان", "plans" to "الخطط", "occasions" to "المناسبات", "reminders" to "التذكيرات", "companion" to "المرافق", "settings" to "الإعدادات", "language" to "لغة التطبيق", "languageNote" to "اختر لغة التطبيق. بعض الشاشات لم تترجم بعد."),
        "de" to mapOf("calendar" to "Kalender", "desk" to "Zurvan Desk", "plans" to "Pläne", "occasions" to "Gedenktage", "reminders" to "Erinnerungen", "companion" to "Begleiter", "settings" to "Einstellungen", "language" to "App-Sprache", "languageNote" to "Sprache wählen. Einige Ansichten sind noch nicht übersetzt."),
        "tr" to mapOf("calendar" to "Takvim", "desk" to "Zurvan Desk", "plans" to "Planlar", "occasions" to "Özel günler", "reminders" to "Hatırlatmalar", "companion" to "Yardımcı", "settings" to "Ayarlar", "language" to "Uygulama dili", "languageNote" to "Dilinizi seçin. Bazı ekranlar henüz çevrilmedi."),
        "fr" to mapOf("calendar" to "Calendrier", "desk" to "Zurvan Desk", "plans" to "Projets", "occasions" to "Événements", "reminders" to "Rappels", "companion" to "Compagnon", "settings" to "Paramètres", "language" to "Langue", "languageNote" to "Choisissez la langue. Certaines pages restent à traduire."),
        "es" to mapOf("calendar" to "Calendario", "desk" to "Zurvan Desk", "plans" to "Planes", "occasions" to "Efemérides", "reminders" to "Recordatorios", "companion" to "Compañero", "settings" to "Ajustes", "language" to "Idioma", "languageNote" to "Elige el idioma. Algunas pantallas aún no están traducidas."),
        "ru" to mapOf("calendar" to "Календарь", "desk" to "Zurvan Desk", "plans" to "Планы", "occasions" to "События", "reminders" to "Напоминания", "companion" to "Помощник", "settings" to "Настройки", "language" to "Язык", "languageNote" to "Выберите язык. Некоторые экраны пока не переведены.")
    )
    fun text(lang: String, key: String): String =
        translations[lang]?.get(key) ?: translations["fa"]?.get(key) ?: key
    fun load(context: Context): String =
        context.getSharedPreferences("zurvan_locale", Context.MODE_PRIVATE).getString("language", "fa") ?: "fa"
    fun save(context: Context, code: String) =
        context.getSharedPreferences("zurvan_locale", Context.MODE_PRIVATE).edit().putString("language", code).apply()
}
