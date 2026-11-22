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
  if (authError || !user || !user.email_confirmed_at) return json(401, { error: "email_not_verified" });
  let body: Record<string, unknown> = {};
  try {
    const raw = await req.text();
    if (raw.length > 1024) return json(413, { error: "payload_too_large" });
    body = raw ? JSON.parse(raw) : {};
    if (!body || typeof body !== "object" || Array.isArray(body)) throw new Error("invalid");
  } catch { return json(400, { error: "invalid_json" }); }
  // Verified accounts are enrolled automatically. Never reactivate a suspended
  // profile or license account; those require explicit administrator review.
  const { data: profile, error: profileError } = await db.from("accounts")
    .select("id,state").eq("auth_user_id", user.id).maybeSingle();
  if (profileError) return json(503, { error: "profile_lookup_failed" });
  if (profile && profile.state !== "active")
    return json(403, { error: "account_inactive" });
  if (!profile) {
    const { error } = await db.from("accounts").insert({
      auth_user_id: user.id, state: "active", recovery_email: user.email,
      recovery_verified_at: user.email_confirmed_at
    });
    if (error && error.code !== "23505") {
      console.error("Zurvan profile enrollment", error.code, error.message);
      return json(503, { error: "profile_enrollment_failed" });
    }
  }

  const { data: licenseAccount, error: accountError } = await db.from("zurvan_accounts")
    .select("id,status").eq("id", user.id).maybeSingle();
  if (accountError) return json(503, { error: "license_account_lookup_failed" });
  if (licenseAccount && licenseAccount.status !== "active")
    return json(403, { error: "license_account_inactive" });
  if (!licenseAccount) {
    const { error } = await db.from("zurvan_accounts").insert({
      id: user.id, status: "active", recovery_email: user.email,
      recovery_verified_at: user.email_confirmed_at
    });
    if (error && error.code !== "23505") {
      console.error("Zurvan license account enrollment", error.code, error.message);
      return json(503, { error: "license_account_enrollment_failed" });
    }
  }

  const fingerprint = body.key_fingerprint;
  if (fingerprint !== undefined &&
      (typeof fingerprint !== "string" || !/^[0-9a-f]{64}$/.test(fingerprint))) {
    return json(400, { error: "invalid_device_fingerprint" });
  }
  const { data, error } = await db.rpc("zurvan_start_trial", {
    p_auth_user_id: user.id,
    p_key_fingerprint: fingerprint ?? null
  });
  if (error) {
    const denied = ["account_not_registered", "license_account_inactive", "device_not_registered", "device_identity_required", "device_trial_already_used"].includes(error.message);
    if (denied) return json(error.message === "device_trial_already_used" ? 409 : 403, { error: error.message });
    if (error.message === "product_unavailable") return json(503, { error: "product_unavailable" });
    console.error("zurvan trial start failed", error.code, error.message);
    return json(503, { error: "trial_unavailable" });
  }
  return json(200, data);
});