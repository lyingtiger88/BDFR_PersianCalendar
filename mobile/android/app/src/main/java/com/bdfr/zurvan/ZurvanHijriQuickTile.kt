package com.bdfr.zurvan

import android.graphics.drawable.Icon
import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService

/**
 * An optional Quick Settings tile, not a modification of Android's system clock/header.
 * The tile is refreshed when the user opens Quick Settings; no background polling.
 */
class ZurvanHijriQuickTile : TileService() {
    override fun onStartListening() {
        super.onStartListening()
        refresh()
    }

    override fun onClick() {
        super.onClick()
        refresh()
    }

    private fun refresh() {
        val tile = qsTile ?: return
        val (year, month) = AndroidCalendarEngine.currentPersianYearMonth()
        val today = AndroidCalendarEngine.buildPersianMonth(year, month).days.firstOrNull { it.isToday }
        val hijriMonths = listOf(
            "محرم", "صفر", "ربیع‌الاول", "ربیع‌الثانی",
            "جمادی‌الاول", "جمادی‌الثانی", "رجب", "شعبان",
            "رمضان", "شوال", "ذیقعده", "ذیحجه"
        )
        if (today != null) {
            val monthName = hijriMonths.getOrElse(today.hijriMonth - 1) { "" }
            tile.label = persianDigits(today.hijriDay.toString()) + " " + monthName
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                tile.subtitle = "هجری قمری " + persianDigits(today.hijriYear.toString())
            }
        } else {
            tile.label = "تاریخ هجری قمری"
        }
        tile.icon = Icon.createWithResource(this, R.drawable.zurvan_launcher)
        tile.state = Tile.STATE_ACTIVE
        tile.updateTile()
    }

    private fun persianDigits(raw: String): String {
        val digits = "۰۱۲۳۴۵۶۷۸۹"
        return raw.map { if (it in '0'..'9') digits[it - '0'] else it }.joinToString("")
    }
}
