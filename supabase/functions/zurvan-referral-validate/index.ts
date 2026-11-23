import { createClient } from "npm:@supabase/supabase-js@2";

// Public signup helper: verifies only whether a referral identifier exists.
// Never discloses the referring user's identity, email, UUID or statistics.
const allowedOrigins = new Set([
  "https://kaliia.ir",
  "https://www.kaliia.ir",
  "https://yztithydgdvydaktuxlw.supabase.co"
]);
Deno.serve(async req => {
  const origin=req.headers.get("origin")??"";
  const headers:Record<string,string>={
    "content-type":"application/json","cache-control":"no-store",
    "x-content-type-options":"nosniff","vary":"Origin"
  };
  if(allowedOrigins.has(origin)){
    headers["access-control-allow-origin"]=origin;
    headers["access-control-allow-methods"]="POST, OPTIONS";
    headers["access-control-allow-headers"]="content-type,apikey";
  }
  const answer=(status:number,data:unknown)=>new Response(JSON.stringify(data),{status,headers});
  if(req.method==="OPTIONS")return new Response(null,{status:204,headers});
  if(req.method!=="POST")return answer(405,{error:"method_not_allowed"});
  let code="";
  try{
    const raw=await req.text();
    if(raw.length>512)return answer(413,{valid:false});
    const data=JSON.parse(raw);
    code=typeof data.code==="string"?data.code.trim().toUpperCase():"";
  }catch{return answer(400,{valid:false});}
  if(!/^([0-9A-F]{8}|[0-9A-F]{16})$/.test(code))return answer(200,{valid:false});
  const url=Deno.env.get("SUPABASE_URL");
  let key=Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")??"";
  if(!key){
    try{key=JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")??"{}").default??"";}
    catch{/* fail closed */}
  }
  if(!url||!key)return answer(503,{error:"not_configured"});
  const db=createClient(url,key,{auth:{persistSession:false}});
  const {data,error}=await db.from("zurvan_invite_codes")
    .select("account_id,zurvan_accounts!inner(status)")
    .or(`code.eq.${code},legacy_code.eq.${code}`).eq("zurvan_accounts.status","active")
    .maybeSingle();
  if(error)return answer(503,{error:"referral_lookup_unavailable"});
  return answer(200,{valid:data!==null});
});
