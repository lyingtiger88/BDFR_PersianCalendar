import { createClient } from "npm:@supabase/supabase-js@2";

// Vendor credentials belong in Supabase Edge Function secrets, never in apps.
const reply = (status: number, body: unknown) => new Response(JSON.stringify(body), {
  status, headers: { "content-type": "application/json", "cache-control": "no-store" }
});
const utf8 = new TextEncoder();
function b64url(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-")
    .replace(/\//g, "_").replace(/=+$/g, "");
}
async function sha256(input: string): Promise<string> {
  return b64url(new Uint8Array(await crypto.subtle.digest("SHA-256", utf8.encode(input))));
}
function parsePrivateKey(pem: string): Uint8Array {
  return Uint8Array.from(atob(pem.replace(/-----[^-]+-----|\s/g, "")), c => c.charCodeAt(0));
}
async function jwt(
  header: Record<string, unknown>, body: Record<string, unknown>,
  key: CryptoKey, algorithm: AlgorithmIdentifier | RsaPssParams | EcdsaParams
): Promise<string> {
  const base = b64url(utf8.encode(JSON.stringify(header))) + "." +
    b64url(utf8.encode(JSON.stringify(body)));
  const signature = new Uint8Array(await crypto.subtle.sign(algorithm, key, utf8.encode(base)));
  return base + "." + b64url(signature);
}
async function googleAccessToken(): Promise<string> {
  const raw = Deno.env.get("ZURVAN_GOOGLE_SERVICE_ACCOUNT_JSON");
  if (!raw) throw Error("google_credentials_unconfigured");
  const service = JSON.parse(raw);
  if (typeof service.client_email !== "string" || typeof service.private_key !== "string")
    throw Error("google_credentials_invalid");
  const key = await crypto.subtle.importKey("pkcs8", parsePrivateKey(service.private_key),
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["sign"]);
  const now = Math.floor(Date.now() / 1000);
  const assertion = await jwt({ alg:"RS256",typ:"JWT" }, {
    iss:service.client_email, scope:"https://www.googleapis.com/auth/playintegrity",
    aud:"https://oauth2.googleapis.com/token", iat:now, exp:now+300
  },key,"RSASSA-PKCS1-v1_5");
  const form = new URLSearchParams({
    grant_type:"urn:ietf:params:oauth:grant-type:jwt-bearer", assertion
  });
  const result = await fetch("https://oauth2.googleapis.com/token", {
    method:"POST", headers:{"content-type":"application/x-www-form-urlencoded"},
    body:form, signal:AbortSignal.timeout(10000)
  });
  if (!result.ok) throw Error("google_oauth_failed");
  const value = await result.json();
  if (typeof value.access_token !== "string") throw Error("google_oauth_invalid");
  return value.access_token;
}
async function playRequest(path: string, token: string, body: unknown): Promise<Record<string, any>> {
  const response = await fetch("https://playintegrity.googleapis.com/v1/" + path, {
    method:"POST", headers:{authorization:"Bearer "+token,"content-type":"application/json"},
    body:JSON.stringify(body), signal:AbortSignal.timeout(10000)
  });
  if (!response.ok) throw Error("play_integrity_upstream_failed");
  return await response.json();
}
async function appleJwt(): Promise<string> {
  const privatePem = Deno.env.get("ZURVAN_APPLE_DEVICECHECK_PRIVATE_KEY");
  const teamId = Deno.env.get("ZURVAN_APPLE_TEAM_ID");
  const keyId = Deno.env.get("ZURVAN_APPLE_DEVICECHECK_KEY_ID");
  if (!privatePem || !teamId || !keyId) throw Error("apple_credentials_unconfigured");
  const key = await crypto.subtle.importKey("pkcs8", parsePrivateKey(privatePem.replace(/\\n/g,"\n")),
    {name:"ECDSA",namedCurve:"P-256"},false,["sign"]);
  return jwt({alg:"ES256",kid:keyId},{iss:teamId,iat:Math.floor(Date.now()/1000)},
    key,{name:"ECDSA",hash:"SHA-256"});
}
async function appleRequest(path: string, token: string, body: unknown): Promise<Record<string, any>> {
  const env = Deno.env.get("ZURVAN_APPLE_DEVICECHECK_ENV");
  if (env !== "production" && env !== "development") throw Error("apple_environment_unconfigured");
  const domain = env==="production" ? "api.devicecheck.apple.com" :
    "api.development.devicecheck.apple.com";
  const response = await fetch("https://"+domain+"/v1/"+path, {
    method:"POST", headers:{authorization:"Bearer "+token,"content-type":"application/json"},
    body:JSON.stringify(body), signal:AbortSignal.timeout(10000)
  });
  if (!response.ok) throw Error("apple_devicecheck_upstream_failed");
  const text = await response.text();
  if (text.trim() === "Bit State Not Found" && path === "query_two_bits")
    return { bit0: false, bit1: false };
  if (text.trim() === "" && path === "update_two_bits") return {};
  if (!text.trim().startsWith("{")) throw Error("apple_unexpected_response");
  return JSON.parse(text);
}
function isFingerprint(input: unknown): input is string {
  return typeof input==="string" && /^[a-f0-9]{64}$/.test(input);
}
async function ensureAccount(db: any, user: any): Promise<string | null> {
  const {data:p,error:pe} = await db.from("accounts").select("state")
    .eq("auth_user_id",user.id).maybeSingle();
  if (pe) return "profile_lookup_failed";
  if (p && p.state!=="active") return "account_inactive";
  if (!p) {
    const {error} = await db.from("accounts").insert({
      auth_user_id:user.id,state:"active",recovery_email:user.email,
      recovery_verified_at:user.email_confirmed_at
    });
    if (error && error.code!=="23505") return "profile_enrollment_failed";
  }
  const {data:a,error:ae} = await db.from("zurvan_accounts")
    .select("status").eq("id",user.id).maybeSingle();
  if (ae) return "license_account_lookup_failed";
  if (a && a.status!=="active") return "account_inactive";
  if (!a) {
    const {error} = await db.from("zurvan_accounts").insert({
      id:user.id,status:"active",recovery_email:user.email,
      recovery_verified_at:user.email_confirmed_at
    });
    if (error && error.code!=="23505") return "license_account_enrollment_failed";
  }
  return null;
}
Deno.serve(async req => {
  if (req.method!=="POST") return reply(405,{error:"method_not_allowed"});
  const url=Deno.env.get("SUPABASE_URL"),key=Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!url || !key) return reply(503,{error:"not_configured"});
  const bearer=req.headers.get("authorization")?.match(/^Bearer (\S+)$/i)?.[1];
  if (!bearer) return reply(401,{error:"unauthorized"});
  const db=createClient(url,key,{auth:{persistSession:false}});
  const {data:auth,error:authError}=await db.auth.getUser(bearer);
  const user=auth?.user;
  if (authError || !user?.email_confirmed_at) return reply(401,{error:"unauthorized"});
  let body:Record<string,unknown>;
  try {
    const raw=await req.text();
    if (raw.length>16000) return reply(413,{error:"payload_too_large"});
    body=JSON.parse(raw);
    if (!body || typeof body!=="object" || Array.isArray(body)) throw Error("bad");
  } catch {return reply(400,{error:"invalid_json"});}
  const platform=body.platform,fp=body.key_fingerprint;
  if (!["android","ios"].includes(String(platform)) || !isFingerprint(fp))
    return reply(400,{error:"invalid_device_claim"});
  const device=String(platform);

  // A paid or historical trial license must not be overwritten; if a mobile
  // trial was left pending due to an upstream failure, allow the same user
  // to retry the vendor confirmation below.
  const {data:existing,error:licenseError}=await db.from("zurvan_licenses")
    .select("id,type,status,expires_at").eq("account_id",user.id)
    .eq("product_id","zurvan").maybeSingle();
  if (licenseError) return reply(503,{error:"license_lookup_failed"});
  // A paid/lifetime account is not asking for another free trial and must
  // never be blocked just because another account used this phone's trial.
  if (existing && existing.type!=="trial")
    return reply(200,{trial_created:false,license_type:existing.type,status:existing.status});

  const {data:priorClaim,error:claimError}=await db.from("zurvan_trial_devices")
    .select("first_account_id,platform,verified").eq("fingerprint",fp).maybeSingle();
  if (claimError) return reply(503,{error:"device_claim_lookup_failed"});
  if (priorClaim && (priorClaim.first_account_id!==user.id || priorClaim.platform!==device))
    return reply(409,{error:"device_trial_already_used"});
  if (existing?.type==="trial" && !priorClaim) {
    const {data:otherClaims,error:otherError}=await db.from("zurvan_trial_devices")
      .select("fingerprint").eq("first_account_id",user.id).limit(1);
    if(otherError) return reply(503,{error:"device_claim_lookup_failed"});
    if(otherClaims && otherClaims.length>0)
      return reply(409,{error:"trial_bound_to_another_device"});
    // Grandfathered account trial from before hardware enrollment.
    return reply(200,{trial_created:false,license_type:existing.type,status:existing.status});
  }
  if (existing && (!priorClaim || existing.type!=="trial"))
    return reply(200,{trial_created:false,license_type:existing.type,status:existing.status});
  if (priorClaim?.verified && existing)
    return reply(200,{trial_created:false,license_type:existing.type,status:existing.status});

  // For new claims the vendor's per-hardware repeat-use state must be
  // available; otherwise do not issue the 45-day trial.
  let vendorToken: string;
  let marked: () => Promise<void>;
  try {
    if (device==="android") {
      const integrityToken=body.integrity_token,at=body.request_time_ms;
      if(typeof integrityToken!=="string" || integrityToken.length>12000 ||
         typeof at!=="number" || Math.abs(Date.now()-at)>120000)
        return reply(400,{error:"invalid_play_proof"});
      const expectedNonce=await sha256("zurvan-trial-v1|"+user.id+"|"+fp+"|"+at);
      const access=await googleAccessToken();
      const packageName="com.bdfr.zurvan";
      const encoded=await playRequest(packageName+":decodeIntegrityToken",access,
        {integrityToken});
      const decoded=encoded.tokenPayloadExternal;
      const details=decoded?.requestDetails;
      if(details?.requestPackageName!==packageName ||
         details?.nonce!==expectedNonce ||
         !["string","number"].includes(typeof details.timestampMillis) ||
         Math.abs(Date.now()-Number(details.timestampMillis))>120000)
        return reply(403,{error:"invalid_play_integrity"});
      if(decoded?.appIntegrity?.appRecognitionVerdict!=="PLAY_RECOGNIZED" ||
         !decoded?.deviceIntegrity?.deviceRecognitionVerdict?.includes("MEETS_DEVICE_INTEGRITY"))
        return reply(403,{error:"untrusted_android_device"});
      const recall=decoded?.deviceIntegrity?.deviceRecall;
      // A fresh, evaluated device commonly returns values: {} with no bits.
      // Missing the deviceRecall object altogether means the feature has
      // not been enabled/evaluated; do not issue an unprotected trial.
      if (!recall || !recall.values || typeof recall.values!=="object")
        return reply(503,{error:"play_device_recall_not_enabled"});
      if (recall.values.bitFirst===true && !priorClaim)
        return reply(409,{error:"device_trial_already_used"});
      vendorToken=integrityToken;
      marked=async()=>{
        await playRequest(packageName+"/deviceRecall:write",access,{
          integrityToken:vendorToken,newValues:{bitFirst:true}
        });
      };
    } else {
      const token=body.devicecheck_token;
      if(typeof token!=="string" || token.length<20 || token.length>6000 ||
         !/^[A-Za-z0-9+/=]+$/.test(token))
        return reply(400,{error:"invalid_apple_device_token"});
      vendorToken=await appleJwt();
      const reqBody={device_token:token,transaction_id:crypto.randomUUID(),timestamp:Date.now()};
      const state=await appleRequest("query_two_bits",vendorToken,reqBody);
      if (state.bit0===true && !priorClaim)
        return reply(409,{error:"device_trial_already_used"});
      marked=async()=>{
        await appleRequest("update_two_bits",vendorToken,{...reqBody,
          transaction_id:crypto.randomUUID(),timestamp:Date.now(),bit0:true});
      };
    }
  } catch(error) {
    console.error("mobile proof provider failed",error instanceof Error?error.message:"unknown");
    return reply(503,{error:"device_verification_unavailable"});
  }

  const enrollmentError=await ensureAccount(db,user);
  if (enrollmentError) return reply(403,{error:enrollmentError});
  // Reserve an unverified device fingerprint before creating the trial.
  // Status API MUST deny claims with verified=false.
  if (!priorClaim) {
    const {error}=await db.from("zurvan_trial_devices").insert({
      fingerprint:fp,first_account_id:user.id,platform:device,verified:false
    });
    if(error) return reply(error.code==="23505"?409:503,
      {error:error.code==="23505"?"device_trial_already_used":"device_claim_failed"});
  }
  const {data:trial,error:trialError}=await db.rpc("zurvan_start_trial",{
    p_auth_user_id:user.id,p_key_fingerprint:fp
  });
  if(trialError){
    console.error("mobile trial RPC",trialError.code,trialError.message);
    return reply(503,{error:"trial_unavailable"});
  }
  try { await marked(); }
  catch(e){
    console.error("mobile vendor recall write failed",e instanceof Error?e.message:"unknown");
    return reply(503,{error:"device_confirmation_pending"});
  }
  const {error:markError}=await db.from("zurvan_trial_devices")
    .update({verified:true})
    .eq("fingerprint",fp).eq("first_account_id",user.id)
    .eq("platform",device).eq("verified",false);
  if(markError) return reply(503,{error:"device_confirmation_pending"});
  return reply(200,{...trial,verified:true,platform:device});
});
