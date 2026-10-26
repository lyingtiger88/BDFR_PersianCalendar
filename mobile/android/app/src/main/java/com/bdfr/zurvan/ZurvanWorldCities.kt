package com.bdfr.zurvan

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.util.Locale
import java.util.zip.ZipInputStream

/**
 * GeoNames cities500 catalogue: populated places of >=500 inhabitants (not every settlement).
 * Downloaded only on request and kept offline. GeoNames data: CC BY 4.0, geonames.org.
 * Source columns: geonameid,name,asciiname,alternatenames,lat,lon,featureclass,
 * featurecode,countrycode,cc2,admin1,admin2,admin3,admin4,population,elevation,dem,timezone,...
 */
object ZurvanWorldCities {
    data class City(val id: Long, val name: String, val countryCode: String,
                    val latitude: Double, val longitude: Double, val timeZone: String)
    private const val SOURCE = "https://download.geonames.org/export/dump/cities500.zip"
    private const val CACHE = "geonames_cities500.txt"
    private var cached: List<City>? = null

    fun countries(): List<Pair<String, String>> = Locale.getISOCountries().map { code ->
        code to Locale("", code).getDisplayCountry(Locale.getDefault()).ifBlank { code }
    }.sortedBy { it.second.lowercase(Locale.getDefault()) }

    fun available(context: Context) = File(context.filesDir, CACHE).isFile

    suspend fun load(context: Context): List<City> = withContext(Dispatchers.IO) {
        cached?.let { return@withContext it }
        val file = File(context.filesDir, CACHE)
        if (!file.isFile) return@withContext emptyList()
        val parsed = ArrayList<City>(200000)
        file.bufferedReader(Charsets.UTF_8).useLines { lines ->
            lines.forEach { line ->
                val fields = line.split('\t')
                if (fields.size > 17 && fields[6] == "P") {
                    val id = fields[0].toLongOrNull()
                    val lat = fields[4].toDoubleOrNull()
                    val lon = fields[5].toDoubleOrNull()
                    if (id != null && lat != null && lon != null &&
                        lat in -90.0..90.0 && lon in -180.0..180.0 &&
                        fields[17].isNotBlank()) {
                        parsed.add(City(id, fields[1], fields[8], lat, lon, fields[17]))
                    }
                }
            }
        }
        cached = parsed
        parsed
    }

    suspend fun download(context: Context): List<City> = withContext(Dispatchers.IO) {
        val output = File(context.filesDir, CACHE)
        val temp = File(context.filesDir, "$CACHE.tmp")
        try {
            val connection = URL(SOURCE).openConnection() as HttpURLConnection
            connection.connectTimeout = 20000
            connection.readTimeout = 90000
            try {
                if (connection.responseCode != 200) error("GeoNames HTTP ${connection.responseCode}")
                ZipInputStream(connection.inputStream.buffered()).use { zip ->
                    var found = false
                    while (true) {
                        val entry = zip.nextEntry ?: break
                        if (entry.name == "cities500.txt") {
                            temp.outputStream().buffered().use { zip.copyTo(it) }
                            found = true
                            break
                        }
                    }
                    if (!found) error("GeoNames dataset entry missing")
                }
                if (temp.length() == 0L) error("Empty GeoNames dataset")
                if (!temp.renameTo(output)) {
                    temp.copyTo(output, overwrite = true)
                    temp.delete()
                }
                cached = null
            } finally { connection.disconnect() }
            load(context)
        } finally { temp.delete() }
    }
}
