package com.bdfr.zurvan

import android.content.Context
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import kotlin.math.*

/** Astronomical estimates; local official timetables and high-latitude rules may differ. */
object ZurvanPrayerTimes {
    data class Method(val id: String, val name: String, val fajr: Double, val isha: Double,
                      val maghribAngle: Double = 0.833, val ishaMinutes: Int = 0)
    val methods = listOf(
        Method("tehran", "مؤسسه ژئوفیزیک تهران (شیعه)", 17.7, 14.0, 4.5),
        Method("jafari", "جعفری / قم (شیعه)", 16.0, 14.0, 4.0),
        Method("mwl", "رابطة العالم الإسلامي", 18.0, 17.0),
        Method("isna", "انجمن اسلامی آمریکای شمالی", 15.0, 15.0),
        Method("egypt", "سازمان نقشه‌برداری مصر", 19.5, 17.5),
        Method("karachi", "دانشگاه کراچی", 18.0, 18.0),
        Method("ummalqura", "ام‌القری مکه", 18.5, 0.0, ishaMinutes = 90)
    )
    val schools = mapOf(
        "jafari" to "شیعه امامیه (جعفری)",
        "hanafi" to "سنی حنفی", "shafii" to "سنی شافعی",
        "maliki" to "سنی مالکی", "hanbali" to "سنی حنبلی"
    )
    data class Settings(val school: String, val method: String, val city: String,
                        val latitude: Double, val longitude: Double, val zone: String,
                        val alerts: Boolean, val beforeMinutes: Int)
    data class Entry(val key: String, val label: String, val time: LocalTime)
    fun settings(context: Context): Settings {
        val p = context.getSharedPreferences("zurvan_prayer", Context.MODE_PRIVATE)
        return Settings(p.getString("school","jafari") ?: "jafari",
            p.getString("method","tehran") ?: "tehran",
            p.getString("city","تهران") ?: "تهران",
            p.getString("lat","35.6892")!!.toDoubleOrNull() ?: 35.6892,
            p.getString("lon","51.3890")!!.toDoubleOrNull() ?: 51.3890,
            p.getString("zone","Asia/Tehran") ?: "Asia/Tehran",
            p.getBoolean("alerts",false),p.getInt("before",0))
    }
    fun save(context: Context, s: Settings) {
        require(s.latitude in -90.0..90.0 && s.longitude in -180.0..180.0)
        ZoneId.of(s.zone)
        context.getSharedPreferences("zurvan_prayer", Context.MODE_PRIVATE).edit()
            .putString("school",s.school).putString("method",s.method).putString("city",s.city)
            .putString("lat",s.latitude.toString()).putString("lon",s.longitude.toString())
            .putString("zone",s.zone).putBoolean("alerts",s.alerts)
            .putInt("before",s.beforeMinutes).apply()
    }
    private fun rad(x: Double) = Math.toRadians(x)
    private fun deg(x: Double) = Math.toDegrees(x)
    private fun norm(x: Double) = (x % 360.0 + 360.0) % 360.0
    private fun hours(x: Double) = (x % 24.0 + 24.0) % 24.0
    private fun solar(day: LocalDate): Pair<Double, Double> {
        val n=day.dayOfYear.toDouble()
        val g=2.0*Math.PI/365.0*(n-1.0)
        val eq=229.18*(0.000075+0.001868*cos(g)-0.032077*sin(g)-0.014615*cos(2*g)-0.040849*sin(2*g))
        val dec=0.006918-0.399912*cos(g)+0.070257*sin(g)-0.006758*cos(2*g)+0.000907*sin(2*g)-0.002697*cos(3*g)+0.00148*sin(3*g)
        return eq to dec
    }
    private fun angleHour(latitude: Double, declination: Double, altitude: Double): Double? {
        val c=(sin(rad(altitude))-sin(rad(latitude))*sin(declination))/(cos(rad(latitude))*cos(declination))
        if(c < -1 || c > 1) return null
        return deg(acos(c))/15.0
    }
    private fun asrHour(latitude: Double, declination: Double, factor: Int): Double? {
        val altitude=deg(atan(1.0/(factor+tan(abs(rad(latitude)-declination)))))
        return angleHour(latitude,declination,altitude)
    }
    fun calculate(day: LocalDate, s: Settings): List<Entry> {
        val method=methods.firstOrNull { it.id==s.method } ?: methods.first()
        val noonUtc=12.0-s.longitude/15.0-solar(day).first/60.0
        val dec=solar(day).second
        val sunrise=angleHour(s.latitude,dec,-0.833) ?: return emptyList()
        val fajr=angleHour(s.latitude,dec,-method.fajr) ?: return emptyList()
        val isha=angleHour(s.latitude,dec,-method.isha)
        val sunsetHour=angleHour(s.latitude,dec,-method.maghribAngle) ?: return emptyList()
        val asr=asrHour(s.latitude,dec,if(s.school=="hanafi") 2 else 1) ?: return emptyList()
        val zone=ZoneId.of(s.zone)
        fun clock(utcHours: Double): LocalTime {
            val instant=day.atStartOfDay(java.time.ZoneOffset.UTC).toInstant()
                .plusSeconds((utcHours*3600).roundToLong())
            return instant.atZone(zone).toLocalTime().withSecond(0).withNano(0)
        }
        val maghrib=noonUtc+sunsetHour
        return listOf(
            Entry("fajr","اذان صبح",clock(noonUtc-fajr)),
            Entry("sunrise","طلوع آفتاب",clock(noonUtc-sunrise)),
            Entry("dhuhr","اذان ظهر",clock(noonUtc)),
            Entry("asr","عصر",clock(noonUtc+asr)),
            Entry("sunset","غروب آفتاب",clock(noonUtc+sunrise)),
            Entry("maghrib","اذان مغرب",clock(maghrib)),
            Entry("isha","عشا",clock(if(method.ishaMinutes>0) maghrib+method.ishaMinutes/60.0 else noonUtc+(isha ?: sunsetHour)))
        )
    }
}
