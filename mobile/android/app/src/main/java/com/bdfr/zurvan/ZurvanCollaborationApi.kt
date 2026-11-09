package com.bdfr.zurvan

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

data class CollabGroup(val id:String,val name:String,val agenda:Boolean,val kanban:Boolean,val school:Boolean)
data class CollabMember(val accountId:String,val identifier:String,val role:String,val canChat:Boolean,val canReport:Boolean,val canEdit:Boolean)
data class CollabEvent(val sequence:Long,val kind:String,val resourceId:String?)

class ZurvanCollaborationApi(private val baseUrl:String = BuildConfig.COLLAB_API_BASE_URL) {
    private fun connection(method:String,path:String,token:String,body:String?=null):HttpURLConnection {
        return (URL(baseUrl.trimEnd('/')+"/"+path.trimStart('/')).openConnection() as HttpURLConnection).apply {
            requestMethod=method;connectTimeout=15000;readTimeout=30000
            setRequestProperty("Accept","application/json")
            setRequestProperty("Authorization","Bearer $token")
            setRequestProperty("apikey",BuildConfig.DESK_SUPABASE_PUBLISHABLE_KEY)
            if(body!=null){doOutput=true;setRequestProperty("Content-Type","application/json; charset=utf-8")}
        }
    }
    private suspend fun request(token:String,method:String,path:String,body:JSONObject?=null):String=withContext(Dispatchers.IO){
        val c=connection(method,path,token,body?.toString())
        try{
            if(body!=null)c.outputStream.use{it.write(body.toString().toByteArray(Charsets.UTF_8))}
            val code=c.responseCode
            val stream=if(code in 200..299)c.inputStream else c.errorStream
            val text=stream?.bufferedReader()?.use{it.readText()}?: ""
            if(code !in 200..299){
                val message=runCatching{JSONObject(text).optString("error","HTTP $code")}.getOrDefault("HTTP $code")
                throw IllegalStateException(message)
            }
            text
        }finally{c.disconnect()}
    }
    suspend fun groups(token:String):List<CollabGroup>{
        val a=JSONArray(request(token,"GET","/groups"))
        return (0 until a.length()).map{val o=a.getJSONObject(it);CollabGroup(o.getString("id"),o.optString("name"),o.optBoolean("agenda_enabled"),o.optBoolean("kanban_enabled"),o.optBoolean("school_enabled"))}
    }
    suspend fun createGroup(token:String,name:String,agenda:Boolean,kanban:Boolean,school:Boolean):CollabGroup{
        val o=JSONObject(request(token,"POST","/groups",JSONObject().put("name",name).put("agenda",agenda).put("kanban",kanban).put("school",school)))
        return CollabGroup(o.getString("id"),o.getString("name"),o.optBoolean("agenda_enabled"),o.optBoolean("kanban_enabled"),o.optBoolean("school_enabled"))
    }
    suspend fun invite(token:String,groupId:String,identifier:String,assistant:Boolean){
        request(token,"POST","/groups/$groupId/invites",JSONObject().put("identifier",identifier).put("role",if(assistant)"assistant" else "member"))
    }
    suspend fun accept(token:String,inviteId:String){request(token,"POST","/invitations/$inviteId/accept",JSONObject())}
    suspend fun invitations(token:String):JSONArray=JSONArray(request(token,"GET","/invitations"))
    suspend fun createItem(token:String,groupId:String,title:String,mode:String="agenda"){
        request(token,"POST","/groups/$groupId/items",JSONObject().put("title",title).put("mode",mode).put("state","todo"))
    }
    suspend fun sendMessage(token:String,groupId:String,body:String){
        request(token,"POST","/groups/$groupId/messages",JSONObject().put("body",body))
    }
    suspend fun removeMember(token:String,groupId:String,memberId:String){
        request(token,"DELETE","/groups/$groupId/members/$memberId")
    }
    suspend fun setPermissions(token:String,groupId:String,memberId:String,chat:Boolean,report:Boolean,edit:Boolean){
        request(token,"PUT","/groups/$groupId/members/$memberId/permissions",
            JSONObject().put("canChat",chat).put("canReport",report).put("canEdit",edit))
    }
    suspend fun members(token:String,groupId:String):List<CollabMember>{
        val o=JSONObject(request(token,"GET","/groups/$groupId"))
        val a=o.optJSONArray("members")?:JSONArray()
        return (0 until a.length()).map{val x=a.getJSONObject(it);val account=x.optJSONObject("account")?:JSONObject()
            CollabMember(x.getString("account_id"),account.optString("identifier",x.getString("account_id")),x.optString("role"),x.optBoolean("can_chat"),x.optBoolean("can_report"),x.optBoolean("can_edit"))}
    }
    suspend fun changes(token:String,groupId:String,after:Long):Pair<List<CollabEvent>,Long>{
        val o=JSONObject(request(token,"GET","/groups/$groupId/changes?after=$after&wait=20"))
        val a=o.optJSONArray("events")?:JSONArray()
        val rows=(0 until a.length()).map{val x=a.getJSONObject(it);CollabEvent(x.optLong("seq"),x.optString("kind"),x.optString("resource_id").takeIf{v->v.isNotBlank()})}
        return rows to o.optLong("nextCursor",after)
    }

    suspend fun kanbanColumns(token:String,groupId:String):JSONArray=JSONArray(request(token,"GET","/groups/$groupId/kanban/columns"))
    suspend fun createKanbanColumn(token:String,groupId:String,name:String,color:String="slate"):JSONObject=
        JSONObject(request(token,"POST","/groups/$groupId/kanban/columns",JSONObject().put("name",name).put("color",color)))
    suspend fun reorderKanbanColumns(token:String,groupId:String,ids:List<String>){
        val a=JSONArray();ids.forEach{a.put(it)}
        request(token,"PUT","/groups/$groupId/kanban/columns/reorder",JSONObject().put("columnIds",a))
    }
    suspend fun createItemAdvanced(token:String,groupId:String,title:String,mode:String,details:String="",startsAt:String?=null,dueAt:String?=null,columnId:String?=null,recurrenceRule:String?=null,recurrenceUntil:String?=null,timezone:String="Asia/Tehran"):JSONObject{
        val p=JSONObject().put("title",title).put("mode",mode).put("details",details).put("state","todo").put("timezone",timezone)
        if(startsAt!=null)p.put("startsAt",startsAt);if(dueAt!=null)p.put("dueAt",dueAt)
        if(columnId!=null)p.put("columnId",columnId);if(recurrenceRule!=null)p.put("recurrenceRule",recurrenceRule)
        if(recurrenceUntil!=null)p.put("recurrenceUntil",recurrenceUntil)
        return JSONObject(request(token,"POST","/groups/$groupId/items",p))
    }
    suspend fun updatePlanningItem(token:String,groupId:String,itemId:String,expectedVersion:Int,title:String,details:String,state:String,
        dueAt:String?=null,assignedTo:String?=null,columnId:String?=null,startsAt:String?=null,recurrenceRule:String?=null,recurrenceUntil:String?=null,timezone:String="Asia/Tehran"):JSONObject {
        val p=JSONObject().put("expectedVersion",expectedVersion).put("title",title).put("details",details).put("state",state).put("timezone",timezone)
        if(dueAt!=null)p.put("dueAt",dueAt);if(assignedTo!=null)p.put("assignedTo",assignedTo)
        if(columnId!=null)p.put("columnId",columnId);if(startsAt!=null)p.put("startsAt",startsAt)
        if(recurrenceRule!=null)p.put("recurrenceRule",recurrenceRule);if(recurrenceUntil!=null)p.put("recurrenceUntil",recurrenceUntil)
        return JSONObject(request(token,"PUT","/groups/$groupId/items/$itemId",p))
    }
    suspend fun replayCommand(token:String,groupId:String,commandId:String,kind:String,payload:JSONObject):JSONObject=
        JSONObject(request(token,"POST","/groups/$groupId/commands",JSONObject().put("commandId",commandId).put("kind",kind).put("payload",payload)))
    suspend fun items(token:String,groupId:String):JSONArray=JSONArray(request(token,"GET","/groups/$groupId/items"))
    suspend fun messages(token:String,groupId:String):JSONArray=JSONArray(request(token,"GET","/groups/$groupId/messages"))
    suspend fun subjects(token:String,groupId:String):JSONArray=JSONArray(request(token,"GET","/groups/$groupId/subjects"))
    suspend fun createSubject(token:String,groupId:String,name:String):JSONObject=
        JSONObject(request(token,"POST","/groups/$groupId/subjects",JSONObject().put("name",name)))
    suspend fun timetable(token:String,groupId:String):JSONArray=JSONArray(request(token,"GET","/groups/$groupId/timetable"))
    suspend fun createTimetableSlot(token:String,groupId:String,subjectId:String,weekday:Int,startTime:String,endTime:String,room:String="",teacherId:String?=null):JSONObject {
        val payload=JSONObject().put("subjectId",subjectId).put("weekday",weekday).put("startTime",startTime).put("endTime",endTime).put("room",room)
        if(teacherId!=null)payload.put("teacherId",teacherId)
        return JSONObject(request(token,"POST","/groups/$groupId/timetable",payload))
    }
    suspend fun homework(token:String,groupId:String):JSONArray=JSONArray(request(token,"GET","/groups/$groupId/homework"))
    suspend fun createHomework(token:String,groupId:String,subjectId:String,title:String,instructions:String,dueAt:String?=null):JSONObject {
        val payload=JSONObject().put("subjectId",subjectId).put("title",title).put("instructions",instructions)
        if(dueAt!=null)payload.put("dueAt",dueAt)
        return JSONObject(request(token,"POST","/groups/$groupId/homework",payload))
    }
    suspend fun submissions(token:String,homeworkId:String):JSONArray=JSONArray(request(token,"GET","/homework/$homeworkId/submissions"))
    suspend fun submitHomework(token:String,homeworkId:String,body:String):JSONObject=
        JSONObject(request(token,"POST","/homework/$homeworkId/submissions",JSONObject().put("body",body)))
    suspend fun gradeSubmission(token:String,homeworkId:String,submissionId:String,grade:Double,feedback:String):JSONObject=
        JSONObject(request(token,"PUT","/homework/$homeworkId/submissions/$submissionId/grade",JSONObject().put("grade",grade).put("feedback",feedback)))
}
