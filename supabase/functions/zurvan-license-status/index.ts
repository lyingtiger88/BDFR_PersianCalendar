
import { createClient } from "npm:@supabase/supabase-js@2";

// --------------------------------------------------
// RESPONSE HELPER
// --------------------------------------------------

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), {
    status,
    headers: {
      "content-type": "application/json",
      "cache-control": "no-store"
    }
  });

const databaseUnavailable = (
  stage: string,
  error: unknown
) => {
  const e = error && typeof error === "object"
    ? error as Record<string, unknown>
    : {};

  console.error("ZURVAN_DATABASE_ERROR", {
    stage,
    code: e.code ?? null,
    message: e.message ?? String(error),
    details: e.details ?? null,
    hint: e.hint ?? null
  });

  return json(503, {
    error: "database_unavailable",
    stage
  });
};


// --------------------------------------------------
// MAIN EDGE FUNCTION
// --------------------------------------------------

Deno.serve(async (req) => {

  if (req.method !== "POST") {
    return json(405, { error: "method_not_allowed" });
  }


const url = Deno.env.get("SUPABASE_URL");
const secretKeysRaw = Deno.env.get("SUPABASE_SECRET_KEYS");

let key: string | undefined;

try {
  const secretKeys = JSON.parse(secretKeysRaw ?? "{}");
  key = secretKeys.default;
} catch (error) {
  console.error("SUPABASE_SECRET_KEYS_PARSE_ERROR", error);
}

if (!url || !key) {
  console.error("SUPABASE_CONFIGURATION_ERROR", {
    hasUrl: Boolean(url),
    hasSecretKeys: Boolean(secretKeysRaw),
    hasDefaultKey: Boolean(key)
  });

  return json(503, { error: "not_configured" });
}


  // --------------------------------------------------
  // AUTHENTICATION
  // --------------------------------------------------

  let deviceFingerprint: string | null = null;
  try {
    const body = await req.json();
    if (body?.key_fingerprint !== undefined && body?.key_fingerprint !== null) {
      if (typeof body.key_fingerprint !== "string" ||
          !/^[a-f0-9]{64}$/.test(body.key_fingerprint))
        return json(400,{error:"invalid_device_fingerprint"});
      deviceFingerprint=body.key_fingerprint;
    }
  } catch { return json(400,{error:"invalid_json"}); }

  const token = req.headers
    .get("authorization")
    ?.match(/^Bearer (\S+)$/i)?.[1];

  if (!token) {
    return json(401, { error: "unauthorized" });
  }

  const db = createClient(url, key, {
    auth: { persistSession: false }
  });

  const { data: authData, error: authError } =
    await db.auth.getUser(token);

  const user = authData?.user;

  if (authError || !user || !user.email_confirmed_at) {
    return json(401, { error: "unauthorized" });
  }

  // --------------------------------------------------
  // ACCOUNT PROFILE
  // --------------------------------------------------

  const { data: account, error: accountError } =
    await db
      .from("accounts")
      .select("id,username,state")
      .eq("auth_user_id", user.id)
      .maybeSingle();

if (accountError) {
  return databaseUnavailable(
    "accounts_lookup",
    accountError
  );
}

  if (!account) {
    return json(200, {
      account_state: "registration_required",
      license_account_state: "not_linked",
      registration_required: true,
      account_username: null,
      licenses: [],
      activations: [],
      super_type: null,
      super_title: null,
      special_rank: null,
      special_permissions: {},
      mode: "read_only",
      can_write: false,
      trial_available: false,
      expires_at: null,
      days_remaining: 0,
      checked_at: new Date().toISOString()
    });
  }

  if (account.state === "provisional") {
    return json(200, {
      account_state: "provisional",
      license_account_state: "not_linked",
      registration_required: true,
      account_username: account.username,
      licenses: [],
      activations: [],
      super_type: null,
      super_title: null,
      special_rank: null,
      special_permissions: {},
      mode: "read_only",
      can_write: false,
      trial_available: false,
      expires_at: null,
      days_remaining: 0,
      checked_at: new Date().toISOString()
    });
  }

  if (account.state !== "active") {
    return json(403, { error: "account_inactive" });
  }

  // User identity is the verified Supabase Auth UUID; usernames may not
  // exist yet for automatically enrolled users. Never invent a username.

  // --------------------------------------------------
  // LICENSE ACCOUNT
  // --------------------------------------------------

  const {
    data: licenseAccount,
    error: mappingError
  } = await db
    .from("zurvan_accounts")
    .select("id,status,super_type,super_title")
    .eq("id", user.id)
    .maybeSingle();

if (mappingError) {
  return databaseUnavailable(
    "license_account_lookup",
    mappingError
  );
}

  if (!licenseAccount || licenseAccount.status !== "active") {
    return json(200, {
      account_state: "active",
      license_account_state:
        licenseAccount?.status ?? "not_found",
      account_username: account.username,
      licenses: [],
      activations: [],
      super_type: licenseAccount?.super_type ?? null,
      super_title: licenseAccount?.super_title ?? null,
      special_rank: null,
      special_permissions: {},
      mode: "read_only",
      can_write: false,
      trial_available: false,
      expires_at: null,
      days_remaining: 0,
      checked_at: new Date().toISOString()
    });
  }

  // --------------------------------------------------
  // SPECIAL RANK ENTITLEMENT
  // --------------------------------------------------

  const specialType =
    licenseAccount.super_type?.trim().toLowerCase() ?? null;

  const isEmperor =
    specialType === "emperor";

  const isEmpress =
    specialType === "empress";

  const isSpecialAdvisor =
    specialType === "special_advisor";

  const isAdminRank = isEmperor || isEmpress;

  // --------------------------------------------------
  // SPECIAL RANK DETAILS
  // --------------------------------------------------

  let specialRank = null;

  if (specialType) {
    const { data, error } = await db
      .from("zurvan_special_ranks")
      .select(`
        code,
        display_name,
        display_name_fa,
        description,
        badge_key,
        access_level,
        permissions
      `)
      .eq("code", specialType)
      .maybeSingle();

if (error) {
  return databaseUnavailable(
    "special_rank_lookup",
    error
  );
}

    specialRank = data;
  }

  // Account-scoped feature grants independent of annual payment.
  const {data:featureGrant,error:featureError}=await db
    .from("zurvan_account_features").select("pahlavi_occasions")
    .eq("account_id",user.id).maybeSingle();
  if(featureError)return databaseUnavailable("feature_lookup",featureError);

  // --------------------------------------------------
  // FETCH LICENSES
  // --------------------------------------------------

  const {
    data: licenses,
    error: licenseError
  } = await db
    .from("zurvan_licenses")
    .select(
      "id,product_id,type,status,generation,created_at,expires_at"
    )
    .eq("account_id", licenseAccount.id)
    .order("created_at", { ascending: false })
    .limit(50);

 if (licenseError) {
  return databaseUnavailable("licenses.lookup", licenseError);
}

  // --------------------------------------------------
  // DEVICE ACTIVATIONS
  // --------------------------------------------------

  const licenseIds = (licenses ?? []).map(
    license => license.id
  );

  let activations: unknown[] = [];

  if (licenseIds.length > 0) {
    const { data, error } = await db
      .from("zurvan_activations")
      .select(
        "id,license_id,device_id,activated_at,revoked_at,zurvan_devices(platform,key_fingerprint,label)"
      )
      .in("license_id", licenseIds)
      .is("revoked_at", null);

   if (error) {
  return databaseUnavailable("activations.lookup", error);
  }

    activations = data ?? [];
  }

  // --------------------------------------------------
  // LICENSE EXPIRATION
  // --------------------------------------------------

  const now = Date.now();

  const normalized = (licenses ?? []).map(license => {

    const expired =
      license.status === "active" &&
      license.expires_at !== null &&
      new Date(license.expires_at).getTime() <= now;

    const effectiveStatus = expired
      ? "expired"
      : license.status;

    const validForZurvan =
      license.product_id?.toLowerCase() === "zurvan";

    return {
      ...license,
      status: effectiveStatus,
      can_write:
        validForZurvan &&
        effectiveStatus === "active"
    };
  });

  // Referral Premium Silver is an independent one-year entitlement. It
  // never overwrites an existing paid, lifetime or trial license row.
  const {data:silverRewards,error:silverError}=await db
    .from("zurvan_referral_silver_rewards")
    .select("id,plan_code,awarded_at,starts_at,expires_at,status")
    .eq("account_id",user.id).eq("status","active")
    .order("expires_at",{ascending:false}).limit(20);
  if(silverError)return databaseUnavailable("referral_silver_lookup",silverError);
  const eligibleSilver=(silverRewards??[])
    .filter(x=>new Date(x.starts_at).getTime()<=now &&
      new Date(x.expires_at).getTime()>now)
    .map(x=>({
      id:x.id,product_id:"zurvan",type:"subscription",status:"active",
      generation:1,created_at:x.awarded_at,expires_at:x.expires_at,
      plan_code:"premium_silver",license_source:"referral_reward",can_write:true
    }));
  const allLicenses=[...normalized,...eligibleSilver];

  // --------------------------------------------------
  // ACTIVE ZURVAN LICENSE
  // --------------------------------------------------

  // Paid or lifetime plans take precedence, followed by a current referral
  // Silver entitlement, and only then a hardware-bound free trial.
  const activeZurvanLicense =
    normalized.find(x=>x.product_id?.toLowerCase()==="zurvan" &&
      x.status==="active" && x.type!=="trial") ??
    eligibleSilver[0] ??
    normalized.find(x=>x.product_id?.toLowerCase()==="zurvan" &&
      x.status==="active");

  const hasActiveLicense =
    activeZurvanLicense !== undefined;

  // --------------------------------------------------
  // FINAL ENTITLEMENT
  // --------------------------------------------------

  // A new free trial is bound to the verified device claim, not only
  // the user's account. Existing pre-migration trials remain grandfathered.
  let trialDeviceAllowed=true;
  if(activeZurvanLicense?.type==="trial"){
    const {data:claims,error:claimError}=await db.from("zurvan_trial_devices")
      .select("fingerprint,verified").eq("first_account_id",user.id).limit(10);
    if(claimError)return databaseUnavailable("trial_device_lookup",claimError);
    if(claims && claims.length>0)
      trialDeviceAllowed=!!deviceFingerprint && claims.some(row=>
        row.fingerprint===deviceFingerprint && row.verified===true);
  }
  const canWrite=isAdminRank || (hasActiveLicense && trialDeviceAllowed);
  const mode=isAdminRank?"super":
    hasActiveLicense && !trialDeviceAllowed?"device_trial_mismatch":
    activeZurvanLicense && "plan_code" in activeZurvanLicense &&
      activeZurvanLicense.plan_code==="premium_silver" ? "premium_silver" :
    hasActiveLicense?activeZurvanLicense?.type??"licensed":"read_only";

  // --------------------------------------------------
  // EXPIRATION INFORMATION
  // --------------------------------------------------

  const expiresAt =
    activeZurvanLicense?.expires_at ?? null;

  const daysRemaining = expiresAt
    ? Math.max(
        0,
        Math.ceil(
          (new Date(expiresAt).getTime() - now) /
          (1000 * 60 * 60 * 24)
        )
      )
    : 0;

  // --------------------------------------------------
  // FINAL RESPONSE
  // --------------------------------------------------

  return json(200, {

    account_state: "active",

    license_account_state: "active",

    account_username: account.username,

    licenses: allLicenses,

    activations,

    super_type: licenseAccount.super_type,

    super_title: licenseAccount.super_title,

    special_rank: specialRank ? {
      code: specialRank.code,
      display_name: specialRank.display_name,
      display_name_fa: specialRank.display_name_fa,
      description: specialRank.description,
      badge_key: specialRank.badge_key,
      access_level: specialRank.access_level
    } : null,

    special_permissions:
      specialRank?.permissions ?? {},
    features:{pahlavi_occasions:featureGrant?.pahlavi_occasions===true},

    is_emperor: isEmperor,

    is_empress: isEmpress,

    is_special_advisor: isSpecialAdvisor,

    mode,

    can_write: canWrite,

    // An earned Premium Silver already grants app access: never start a new
    // hardware-bound free trial over an active referral reward.
    trial_available: allLicenses.length===0 && !isAdminRank,

    expires_at: expiresAt,

    days_remaining: daysRemaining,

    checked_at: new Date().toISOString()

  });

});
