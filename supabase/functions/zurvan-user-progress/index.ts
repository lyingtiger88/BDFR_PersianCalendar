import { createClient } from "npm:@supabase/supabase-js@2";

const reply = (status: number, body: unknown) => new Response(JSON.stringify(body), {
  status, headers: { "content-type":"application/json", "cache-control":"no-store",
    "x-content-type-options":"nosniff" }
});
const levels = [
  {xp:0,title:"تازه‌وارد"},
  {xp:100,title:"رهرو زمان"},
  {xp:250,title:"همراه زروان"},
  {xp:500,title:"کوشای زروان"},
  {xp:900,title:"زمان‌بان"},
  {xp:1500,title:"استاد زمان"},
  {xp:2500,title:"فرزانه زمان"},
  {xp:4000,title:"اسطوره زروان"}
];
// Never publish email addresses, phone numbers or UUIDs as public aliases
// even if a legacy account stored them in the username field.
function publicAlias(value: string): string {
  const name=value.trim();
  if(!name || name.length>28 || /[@\\/\\]|[\\r\\n]/.test(name) ||
      /^\\+?\\d[\\d\\s-]{7,}$/.test(name) ||
      /^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(name)) return "کاربر زروان";
  return name;
}

function progress(totalXp: number) {
  const xp=Math.max(0,Math.floor(Number(totalXp)||0));
  let i=levels.length-1;
  while(i>0 && xp<levels[i].xp)i--;
  const next=levels[i+1]??null;
  return {level:i+1,level_title:levels[i].title,total_xp:xp,
    level_start_xp:levels[i].xp,next_level_xp:next?.xp??null,
    xp_to_next:next?Math.max(0,next.xp-xp):0,
    progress_percent:next?Math.min(100,Math.round(100*(xp-levels[i].xp)/
      (next.xp-levels[i].xp))):100};
}

Deno.serve(async req => {
  if(req.method!=="POST")return reply(405,{error:"method_not_allowed"});
  const url=Deno.env.get("SUPABASE_URL");
  let key=Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")??"";
  if(!key){
    try{key=JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")??"{}").default??"";}
    catch{/* fail closed */}
  }
  if(!url||!key)return reply(503,{error:"not_configured"});
  const bearer=req.headers.get("authorization")?.match(/^Bearer (\S+)$/i)?.[1];
  if(!bearer)return reply(401,{error:"unauthorized"});
  const db=createClient(url,key,{auth:{persistSession:false}});
  const {data:auth,error:authError}=await db.auth.getUser(bearer);
  const user=auth?.user;
  if(authError||!user||!user.email_confirmed_at)
    return reply(401,{error:"verified_account_required"});
  let body:Record<string,unknown>={};
  try{
    const raw=await req.text();
    if(raw.length>2048)return reply(413,{error:"payload_too_large"});
    body=raw?JSON.parse(raw):{};
    if(!body||Array.isArray(body)||typeof body!=="object")throw Error("bad body");
  }catch{return reply(400,{error:"invalid_json"});}
  const action=body.action??"status";
  if(!["status","claim_daily","set_visibility"].includes(String(action)))
    return reply(400,{error:"unknown_action"});
  const {data:account,error:ae}=await db.from("zurvan_accounts")
    .select("id,status,username,super_type").eq("id",user.id).maybeSingle();
  if(ae)return reply(503,{error:"account_lookup_failed"});
  if(!account||account.status!=="active")
    return reply(403,{error:"active_account_required"});

  let claimResult:unknown=null;
  try{
    if(action==="claim_daily"){
      const {data,error}=await db.rpc("zurvan_claim_daily_xp",{p_account:user.id});
      if(error)throw Error("claim_failed");
      claimResult=data;
    }
    if(action==="set_visibility"){
      if(typeof body.visible!=="boolean")return reply(400,{error:"invalid_visibility"});
      const {error}=await db.rpc("zurvan_set_score_visibility",{
        p_account:user.id,p_visible:body.visible
      });
      if(error)throw Error("visibility_update_failed");
    }
    const [scoreResult,historyResult,earnedResult,catalogResult,boardResult]=await Promise.all([
      db.from("zurvan_user_scores")
        .select("total_xp,daily_streak,best_streak,last_claim_date,leaderboard_opt_in")
        .eq("account_id",user.id).maybeSingle(),
      db.from("zurvan_xp_events")
        .select("event_type,points,created_at").eq("account_id",user.id)
        .order("created_at",{ascending:false}).limit(30),
      db.from("zurvan_user_badges")
        .select("badge_key,earned_at").eq("account_id",user.id)
        .order("earned_at",{ascending:true}),
      db.from("zurvan_badge_catalog")
        .select("badge_key,name_fa,description_fa,icon,required_xp,required_streak,display_order")
        .order("display_order"),
      db.from("zurvan_user_scores").select("account_id,total_xp")
        .eq("leaderboard_opt_in",true).gt("total_xp",0)
        .order("total_xp",{ascending:false})
        .order("account_id",{ascending:true}).limit(100)
    ]);
    if([scoreResult,historyResult,earnedResult,catalogResult,boardResult].some(x=>x.error))
      throw Error("progress_query_failed");
    const score=scoreResult.data;
    const today=new Date().toISOString().slice(0,10);
    const allBadges=catalogResult.data??[];
    const earned=new Map((earnedResult.data??[]).map(b=>[b.badge_key,b.earned_at]));
    const board=boardResult.data??[];
    let usernames=new Map<string,string>();
    if(board.length){
      const {data:users,error:ue}=await db.from("zurvan_accounts")
        .select("id,username,status").in("id",board.map(x=>x.account_id));
      if(ue)throw Error("leaderboard_lookup_failed");
      usernames=new Map((users??[]).filter(x=>x.status==="active")
        .map(x=>[x.id,publicAlias(x.username??"")]));
    }
    const rank=account.super_type??null;
    const {data:special,error:se}=rank?await db.from("zurvan_special_ranks")
      .select("code,display_name_fa,badge_key,access_level").eq("code",rank).maybeSingle()
      :{data:null,error:null};
    if(se)throw Error("rank_lookup_failed");
    return reply(200,{
      profile:{...progress(Number(score?.total_xp??0)),
        daily_streak:score?.daily_streak??0,best_streak:score?.best_streak??0,
        last_claim_date:score?.last_claim_date??null,
        checked_in_today:score?.last_claim_date===today,
        leaderboard_opt_in:score?.leaderboard_opt_in===true},
      badges:allBadges.map(x=>({...x,earned:earned.has(x.badge_key),
        earned_at:earned.get(x.badge_key)??null})),
      history:(historyResult.data??[]).map(x=>({
        title:"حضور روزانه",points:x.points,created_at:x.created_at,
        event_type:x.event_type})),
      leaderboard:board.filter(x=>usernames.has(x.account_id)).slice(0,20)
        .map((x,i)=>({
          position:i+1,is_self:x.account_id===user.id,
          display_name:usernames.get(x.account_id)??"کاربر زروان",
          total_xp:x.total_xp})),
      special_rank:special??null,
      claim:claimResult,
      rules:{daily_base:20,streak_bonus_per_day:5,streak_bonus_cap:30,
        timezone:"UTC",leaderboard_default_private:true}
    });
  }catch(e){
    console.error("ZURVAN_PROGRESS_ERROR",e instanceof Error?e.message:"unknown");
    return reply(503,{error:"progress_unavailable"});
  }
});
