package com.bdfr.zurvan

import android.graphics.Bitmap
import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel

object CompanionQrCode {
    fun generate(
        payload: String,
        size: Int = 720
    ): Bitmap {
        val matrix = QRCodeWriter().encode(
            payload,
            BarcodeFormat.QR_CODE,
            size,
            size,
            mapOf(
                EncodeHintType.MARGIN to 1,
                EncodeHintType.ERROR_CORRECTION to
                    ErrorCorrectionLevel.M
            )
        )

        val pixels = IntArray(
            size * size
        )

        for (y in 0 until size) {
            for (x in 0 until size) {
                pixels[y * size + x] =
                    if (matrix[x, y]) {
                        android.graphics.Color.BLACK
                    } else {
                        android.graphics.Color.WHITE
                    }
            }
        }

        return Bitmap.createBitmap(
            size,
            size,
            Bitmap.Config.ARGB_8888
        ).apply {
            setPixels(
                pixels,
                0,
                size,
                0,
                0,
                size,
                size
            )
        }
    }
}
