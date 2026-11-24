import { createClient } from "npm:@supabase/supabase-js@2";

const origins = { "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store", "x-content-type-options": "nosniff" };
const response = (status: number, payload: unknown) =>
  new Response(JSON.stringify(payload), { status, headers: origins });

Deno.serve(async (request: Request) => {
  if (request.method !== "POST") return response(405, { error: "method_not_allowed" });
  const token = request.headers.get("authorization")?.match(/^Bearer (\S+)$/i)?.[1];
  if (!token) return response(401, { error: "unauthorized" });
  const url = Deno.env.get("SUPABASE_URL");
  let secret = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";
  if (!secret) {
    try { secret = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS") ?? "{}").default ?? ""; }
    catch { /* fail closed */ }
  }
  if (!url || !secret) return response(503, { error: "not_configured" });

  const db = createClient(url, secret, { auth: { persistSession: false } });
  const { data: auth, error: authError } = await db.auth.getUser(token);
  const user = auth?.user;
  if (authError || !user || !user.email_confirmed_at)
    return response(401, { error: "verified_account_required" });
  const { data: account, error: accountError } = await db.from("zurvan_accounts")
    .select("id,status").eq("id", user.id).maybeSingle();
  if (accountError) return response(503, { error: "account_lookup_failed" });
  if (!account || account.status !== "active")
    return response(403, { error: "active_account_required" });

  let body: Record<string, unknown>;
  try {
    const raw = await request.text();
    if (raw.length > 1024) return response(413, { error: "payload_too_large" });
    body = raw ? JSON.parse(raw) : {};
    if (!body || typeof body !== "object" || Array.isArray(body))
      return response(400, { error: "invalid_payload" });
  } catch { return response(400, { error: "invalid_json" }); }

  const action = body.action === "record" ? "record" : body.action === "status" ? "status" : null;
  if (!action) return response(400, { error: "invalid_action" });
  let result: unknown = null;
  if (action === "record") {
    const kind = body.kind;
    const section = typeof body.section === "string" ? body.section : null;
    const year = typeof body.year === "number" && Number.isInteger(body.year) ? body.year : null;
    const month = typeof body.month === "number" && Number.isInteger(body.month) ? body.month : null;
    if (kind !== "app_open" && kind !== "content_saved"
      && kind !== "explore" && kind !== "calendar_month")
      return response(400, { error: "invalid_activity" });
    if ((kind === "explore" && !["calendar","plans","occasions","reminders","desk","settings"].includes(section ?? ""))
      || (kind === "calendar_month" && (year === null || month === null
        || year < 1200 || year > 1700 || month < 1 || month > 12)))
      return response(400, { error: "invalid_activity_fields" });
    const recorded = await db.rpc("zurvan_record_achievement_activity", {
      p_account: user.id, p_kind: kind, p_section: section, p_year: year, p_month: month
    });
    if (recorded.error) {
      console.error("ZURVAN_ACHIEVEMENT_RECORD_FAILED", recorded.error.code);
      return response(503, { error: "record_unavailable" });
    }
    result = recorded.data;
  }

  const [catalog, earned] = await Promise.all([
    db.from("zurvan_badge_catalog").select("badge_key,name_fa,description_fa,icon,display_order")
      .order("display_order", { ascending: true }),
    db.from("zurvan_user_badges").select("badge_key,earned_at").eq("account_id", user.id)
  ]);
  if (catalog.error || earned.error) return response(503, { error: "status_unavailable" });
  const earnedMap = new Map((earned.data ?? []).map(item => [item.badge_key, item.earned_at]));
  return response(200, { ok: true, result, badges: (catalog.data ?? []).map(badge => ({
    ...badge, earned: earnedMap.has(badge.badge_key),
    earned_at: earnedMap.get(badge.badge_key) ?? null
  })) });
});
