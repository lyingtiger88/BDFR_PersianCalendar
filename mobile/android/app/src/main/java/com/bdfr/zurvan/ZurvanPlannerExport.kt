package com.bdfr.zurvan

import android.graphics.Color
import android.graphics.pdf.PdfDocument
import android.text.Layout
import android.text.StaticLayout
import android.text.TextDirectionHeuristics
import android.text.TextPaint
import java.io.OutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

/**
 * Exports selected local planner records; no network access or external library.
 * Saving is initiated only through Android's system file picker.
 */
object ZurvanPlannerExport {
    fun selected(
        plans: List<LocalPlan>,
        from: String?,
        to: String?,
        includePrivate: Boolean
    ): List<LocalPlan> {
        fun dateOrNull(value: String?): String? = value?.trim()?.takeIf { it.isNotBlank() }
        val fromDate = dateOrNull(from)
        val toDate = dateOrNull(to)
        val pattern = Regex("""^\d{4}-\d{2}-\d{2}$""")
        require(fromDate == null || pattern.matches(fromDate)) { "تاریخ شروع باید YYYY-MM-DD باشد." }
        require(toDate == null || pattern.matches(toDate)) { "تاریخ پایان باید YYYY-MM-DD باشد." }
        require(fromDate == null || toDate == null || fromDate <= toDate) { "بازه زمانی نامعتبر است." }
        return plans.asSequence()
            .filter { includePrivate || !it.isPrivate }
            .filter { fromDate == null || it.dateKey >= fromDate }
            .filter { toDate == null || it.dateKey <= toDate }
            .sortedWith(compareBy<LocalPlan> { it.dateKey }.thenBy { it.hour }.thenBy { it.minute })
            .take(5000)
            .toList()
    }

    fun pdf(plans: List<LocalPlan>, output: OutputStream) {
        val document = PdfDocument()
        val width = 595
        val height = 842
        val margin = 38
        val usable = width - margin * 2
        val textPaint = TextPaint(TextPaint.ANTI_ALIAS_FLAG).apply {
            color = Color.BLACK
            textSize = 12f
        }
        var pageNumber = 0
        var page: PdfDocument.Page? = null
        var y = 0f

        fun startPage() {
            page?.let { document.finishPage(it) }
            pageNumber++
            page = document.startPage(
                PdfDocument.PageInfo.Builder(width, height, pageNumber).create()
            )
            page!!.canvas.drawColor(Color.WHITE)
            y = margin.toFloat()
        }

        fun writeLine(text: String, headline: Boolean = false) {
            textPaint.textSize = if (headline) 18f else 12f
            val layout = StaticLayout.Builder.obtain(text, 0, text.length, textPaint, usable)
                .setAlignment(Layout.Alignment.ALIGN_NORMAL)
                .setTextDirection(TextDirectionHeuristics.RTL)
                .setIncludePad(false)
                .build()
            val required = layout.height + if (headline) 18 else 13
            if (page == null || y + required > height - margin) startPage()
            val canvas = page!!.canvas
            canvas.save()
            canvas.translate(margin.toFloat(), y)
            layout.draw(canvas)
            canvas.restore()
            y += required
        }

        try {
            writeLine("زروان | خروجی برنامه‌ریزی", headline = true)
            writeLine("تعداد موارد: " + plans.size)
            plans.forEach { item ->
                val kind = if (item.kind == "task") "کار" else "رویداد"
                val time = "%02d:%02d".format(java.util.Locale.US, item.hour, item.minute)
                writeLine(item.dateKey + "  |  " + time + "  |  " + kind + "  |  " +
                    item.title + "  |  " + if (item.completed) "انجام‌شده" else "باز")
            }
            if (plans.isEmpty()) writeLine("موردی در بازه انتخابی وجود ندارد.")
            page?.let { document.finishPage(it) }
            document.writeTo(output)
            output.flush()
        } finally {
            document.close()
        }
    }

    fun excel(plans: List<LocalPlan>, output: OutputStream) {
        val rows = listOf(
            listOf("تاریخ شمسی", "ساعت", "عنوان", "نوع", "وضعیت", "خصوصی", "نماد", "حذف خودکار (روز)")
        ) + plans.map { item ->
            listOf(
                item.dateKey,
                "%02d:%02d".format(java.util.Locale.US, item.hour, item.minute),
                item.title,
                if (item.kind == "task") "کار" else "رویداد",
                if (item.completed) "انجام‌شده" else "باز",
                if (item.isPrivate) "بله" else "خیر",
                item.icon,
                item.deleteAfterDays?.toString() ?: ""
            )
        }
        fun escaped(value: String): String = buildString {
            value.forEach { ch ->
                when (ch) {
                    '&' -> append("&amp;")
                    '<' -> append("&lt;")
                    '>' -> append("&gt;")
                    '"' -> append("&quot;")
                    '\'' -> append("&apos;")
                    else -> if (ch.code >= 32 || ch == '\t' || ch == '\n') append(ch)
                }
            }
        }
        val worksheet = StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetViews><sheetView workbookViewId="0" rightToLeft="1"/></sheetViews><sheetData>""")
        rows.forEachIndexed { rowIndex, cells ->
            worksheet.append("<row r=\"").append(rowIndex + 1).append("\">")
            cells.forEachIndexed { colIndex, value ->
                val ref = ('A' + colIndex).toString() + (rowIndex + 1)
                worksheet.append("<c r=\"").append(ref).append("\" t=\"inlineStr\"><is><t>")
                    .append(escaped(value)).append("</t></is></c>")
            }
            worksheet.append("</row>")
        }
        worksheet.append("</sheetData></worksheet>")
        ZipOutputStream(output).use { zip ->
            fun entry(name: String, xml: String) {
                zip.putNextEntry(ZipEntry(name))
                zip.write(xml.toByteArray(Charsets.UTF_8))
                zip.closeEntry()
            }
            entry("[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""")
            entry("_rels/.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""")
            entry("xl/workbook.xml", """<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Zurvan Planner" sheetId="1" r:id="rId1"/></sheets></workbook>""")
            entry("xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""")
            entry("xl/worksheets/sheet1.xml", worksheet.toString())
        }
    }
}
