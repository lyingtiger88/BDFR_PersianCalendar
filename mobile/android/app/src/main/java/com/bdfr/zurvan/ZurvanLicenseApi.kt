package com.bdfr.zurvan

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

data class ZurvanLicenseSummary(val id:String,val productId:String,val type:String,val status:String,val generation:Long)
data class ZurvanLicenseStatus(val accountState:String,val licenseAccountState:String,val licenses:List<ZurvanLicenseSummary>,val checkedAt:String?)

class ZurvanLicenseApi {
 suspend fun status(accessToken:String):ZurvanLicenseStatus=withContext(Dispatchers.IO) {
  require(BuildConfig.SUPABASE_URL.startsWith("https://") && BuildConfig.SUPABASE_PUBLISHABLE_KEY.isNotBlank())
  val c=URL(BuildConfig.SUPABASE_URL.trimEnd('/')+"/functions/v1/zurvan-license-status").openConnection() as HttpURLConnection
  try {
   c.requestMethod="POST";c.connectTimeout=15000;c.readTimeout=20000;c.doOutput=true
   c.setRequestProperty("Content-Type","application/json");c.setRequestProperty("apikey",BuildConfig.SUPABASE_PUBLISHABLE_KEY)
   c.setRequestProperty("Authorization","Bearer $accessToken")
   c.outputStream.use{it.write("{}".toByteArray())}
   val code=c.responseCode
   val raw=(if(code in 200..299)c.inputStream else c.errorStream)?.bufferedReader()?.use{it.readText()}.orEmpty()
   val j=JSONObject(raw.ifBlank{"{}"})
   if(code !in 200..299) error(j.optString("error").ifBlank{"License status HTTP $code"})
   val items=mutableListOf<ZurvanLicenseSummary>();val a=j.optJSONArray("licenses")
   if(a!=null)for(i in 0 until a.length()){val x=a.getJSONObject(i);items+=ZurvanLicenseSummary(x.optString("id"),x.optString("product_id"),x.optString("type"),x.optString("status"),x.optLong("generation"))}
   ZurvanLicenseStatus(j.optString("account_state"),j.optString("license_account_state"),items,j.optString("checked_at").takeIf{it.isNotBlank()})
  } finally {c.disconnect()}
 }
}
