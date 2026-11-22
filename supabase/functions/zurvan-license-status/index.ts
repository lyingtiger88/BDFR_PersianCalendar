import { createClient } from "npm:@supabase/supabase-js@2";

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), {
  status, headers: { "content-type": "application/json", "cache-control": "no-store" }
});

Deno.serve(async req => {
  if (req.method !== "POST") return json(405, { error: "method_not_allowed" });
  const url = Deno.env.get("SUPABASE_URL");
  const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!url || !key) return json(503, { error: "not_configured" });
  const token = req.headers.get("authorization")?.match(/^Bearer (\S+)$/i)?.[1];
  if (!token) return json(401, { error: "unauthorized" });
  const db = createClient(url, key, { auth: { persistSession: false } });
  const { data: authData, error: authError } = await db.auth.getUser(token);
  const user = authData?.user;
  if (authError || !user || !user.email_confirmed_at) return json(401, { error: "unauthorized" });

  const { data: account, error: accountError } = await db.from("accounts")
    .select("id,username,state").eq("auth_user_id", user.id).maybeSingle();
  if (accountError) return json(503, { error: "database_unavailable" });
  if (!account) return json(200, {
    account_state: "registration_required", license_account_state: "not_linked",
    licenses: [], activations: [], trial_available: false, can_write: false,
    mode: "registration_required", checked_at: new Date().toISOString()
  });
  if (account.state !== "active") return json(403, { error: "account_inactive" });

  const { data: licenseAccount, error: mappingError } = await db.from("zurvan_accounts")
    .select("id,status,super_type,super_title").eq("id", user.id).maybeSingle();
  if (mappingError) return json(503, { error: "database_unavailable" });
  if (!licenseAccount || licenseAccount.status !== "active") return json(200, {
    account_state: "active", license_account_state: licenseAccount?.status ?? "not_found",
    licenses: [], activations: [], trial_available: false, can_write: false,
    mode: "account_setup_required", checked_at: new Date().toISOString()
  });

  const { data: licenses, error: licenseError } = await db.from("zurvan_licenses")
    .select("id,product_id,type,status,generation,created_at,expires_at")
    .eq("account_id", licenseAccount.id).eq("product_id", "zurvan")
    .order("created_at", { ascending: false }).limit(5);
  if (licenseError) return json(503, { error: "database_unavailable" });

  const specialType = typeof licenseAccount.super_type === "string"
    ? licenseAccount.super_type.trim().toLowerCase()
    : null;
  const { data: specialRank, error: specialRankError } = specialType
    ? await db.from("zurvan_special_ranks")
        .select("code,display_name,display_name_fa,description,badge_key,access_level,permissions,sort_order")
        .eq("code", specialType).maybeSingle()
    : { data: null, error: null };
  if (specialRankError) return json(503, { error: "database_unavailable" });

  const rows = licenses ?? [];
  const current = rows.find(x => x.status === "active") ?? rows[0] ?? null;
  const now = Date.now();
  const expiry = current?.expires_at ? new Date(current.expires_at).getTime() : null;
  const valid = !!current && current.status === "active" && (expiry === null || expiry > now);

  // Only Emperor and Empress receive administrator write entitlement.
  // Special Advisor is an honorary/advisory rank and does not bypass licensing.
  const isAdminRank = specialType === "emperor" || specialType === "empress";
  const canWrite = valid || isAdminRank;
  const trialAvailable = rows.length === 0 && !isAdminRank;
  const mode = isAdminRank ? "super" : valid
    ? current.type : current?.type === "trial" ? "read_only" : current ? "license_inactive" : "trial_available";
  const daysRemaining = expiry === null || !valid ? 0 : Math.max(0, Math.ceil((expiry - now) / 86400000));

  const licenseIds = rows.map(x => x.id);
  let activations: unknown[] = [];
  if (licenseIds.length) {
    const { data, error } = await db.from("zurvan_activations")
      .select("id,license_id,device_id,activated_at,revoked_at,zurvan_devices(platform,key_fingerprint,label)")
      .in("license_id", licenseIds).is("revoked_at", null);
    if (error) return json(503, { error: "database_unavailable" });
    activations = data ?? [];
  }

  return json(200, {
    account_state: "active", license_account_state: "active",
    account_username: account.username,
    licenses: rows, activations, trial_available: trialAvailable,
    can_write: canWrite, mode, days_remaining: daysRemaining,
    expires_at: current?.expires_at ?? null,
    super_type: licenseAccount.super_type, super_title: licenseAccount.super_title,
    special_rank: specialRank ? {
      code: specialRank.code,
      display_name: specialRank.display_name,
      display_name_fa: specialRank.display_name_fa,
      description: specialRank.description,
      badge_key: specialRank.badge_key,
      access_level: specialRank.access_level,
      sort_order: specialRank.sort_order
    } : null,
    special_permissions: specialRank?.permissions ?? {},
    checked_at: new Date().toISOString()
  });
});