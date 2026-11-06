package com.bdfr.zurvan

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import java.time.Instant
import kotlin.math.ceil

private data class ExpiryUi(val type:String,val status:String,val expiresAt:Instant)

@Composable
fun ZurvanExpiryIndicator(modifier:Modifier=Modifier) {
    val context= LocalContext.current
    val auth= remember(context){ ZurvanAuth(context.applicationContext) }
    var expiry by remember { mutableStateOf<ExpiryUi?>(null) }
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }

    LaunchedEffect(Unit) {
        while(true) {
            runCatching {
                val session=auth.validSession()
                if(session==null) {
                    expiry=null
                } else {
                    val status=ZurvanLicenseApi().status(session.accessToken)
                    val candidates=status.licenses
                        .filter { it.type=="trial" || it.type=="subscription" }
                        .mapNotNull { item ->
                            val end=item.expiresAt?.let { runCatching { Instant.parse(it) }.getOrNull() }
                            end?.let { ExpiryUi(item.type,item.status,it) }
                        }
                        .filter { it.status=="active" || it.status=="expired" }
                        .sortedBy { it.expiresAt }
                    expiry=candidates.firstOrNull()
                }
            }.onFailure { /* Keep the last server-confirmed expiry while offline. */ }
            now=System.currentTimeMillis()
            delay(30*60*1000L)
        }
    }
    LaunchedEffect(Unit) {
        while(true) {
            delay(60_000L)
            now=System.currentTimeMillis()
        }
    }

    val item=expiry ?: return
    val secondsLeft=item.expiresAt.epochSecond-now/1000
    val daysLeft=if(secondsLeft>0) ceil(secondsLeft/86400.0).toInt() else 0
    val show=item.type=="trial" || (item.type=="subscription" && daysLeft<=30)
    if(!show) return
    val expired=secondsLeft<=0 || item.status=="expired"
    val progress=(daysLeft.toFloat()/(if(item.type=="trial")45f else 365f)).coerceIn(0f,1f)
    val color=if(expired || daysLeft<=3) Color(0xFFCF4545) else if(daysLeft<=10) Color(0xFFE09A2D) else Color(0xFF2879E8)
    Surface(
        modifier=modifier.fillMaxWidth(),
        color=color.copy(alpha=0.09f),
        contentColor=MaterialTheme.colorScheme.onSurface,
        shape=RoundedCornerShape(12.dp)
    ) {
        Column(Modifier.padding(horizontal=12.dp, vertical=7.dp),verticalArrangement=Arrangement.spacedBy(4.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(6.dp)) {
                Text(
                    when {
                        expired -> "اعتبار اشتراک به پایان رسیده است"
                        item.type=="trial" -> "دوره آزمایشی رایگان"
                        else -> "اشتراک سالیانه"
                    },
                    style=MaterialTheme.typography.labelSmall,
                    fontWeight=FontWeight.SemiBold,
                    color=color
                )
                Spacer(Modifier.weight(1f))
                Text(
                    if(expired) "منقضی‌شده" else "$daysLeft روز باقی‌مانده",
                    style=MaterialTheme.typography.labelMedium,
                    fontWeight=FontWeight.Bold,
                    color=color
                )
            }
            if(!expired) LinearProgressIndicator(
                progress={progress},
                modifier=Modifier.fillMaxWidth().height(4.dp),
                color=color,
                trackColor=color.copy(alpha=0.16f)
            )
        }
    }
}
