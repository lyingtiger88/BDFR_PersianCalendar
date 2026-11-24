-- Zurvan Achievements v2: separate from paid license, XP and referrals.
-- Existing earned achievements remain untouched. Re-running is idempotent.
CREATE TABLE IF NOT EXISTS public.zurvan_achievement_activity (
    account_id uuid NOT NULL REFERENCES public.zurvan_accounts(id) ON DELETE CASCADE,
    event_key text NOT NULL CHECK (length(event_key) BETWEEN 1 AND 80),
    occurred_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(account_id,event_key)
);
ALTER TABLE public.zurvan_achievement_activity ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.zurvan_achievement_activity FROM PUBLIC, anon, authenticated;
GRANT SELECT,INSERT ON public.zurvan_achievement_activity TO service_role;
UPDATE public.zurvan_badge_catalog SET required_xp=0,required_streak=0,
description_fa=CASE badge_key
 WHEN 'first_step' THEN 'نخستین بار بازکردن زروان'
 WHEN 'xp_100' THEN 'ثبت اولین یادداشت یا رویداد'
 WHEN 'xp_500' THEN 'بازدید از سه بخش متفاوت برنامه'
 WHEN 'xp_1500' THEN 'مرور هر ۱۲ ماه یک سال شمسی در تقویم'
 WHEN 'streak_7' THEN 'بازکردن برنامه در ۷ روز متوالی'
 WHEN 'streak_30' THEN 'بازکردن برنامه در ۳۰ روز متوالی'
 ELSE description_fa END
WHERE badge_key IN ('first_step','xp_100','xp_500','xp_1500','streak_7','streak_30');
CREATE OR REPLACE FUNCTION public.zurvan_record_achievement_activity(p_account uuid, p_kind text, p_section text DEFAULT NULL::text, p_year integer DEFAULT NULL::integer, p_month integer DEFAULT NULL::integer)
 RETURNS jsonb
 LANGUAGE plpgsql
 SECURITY DEFINER
 SET search_path TO ''
AS $function$
DECLARE
  v_key text;
  v_today date := (now() at time zone 'UTC')::date;
  v_explored integer;
  v_months integer;
  v_new text[] := array[]::text[];
  v_badge text;
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM public.zurvan_accounts a
    JOIN auth.users u ON u.id = a.id
    WHERE a.id = p_account AND a.status = 'active'
      AND u.email_confirmed_at IS NOT NULL
  ) THEN
    RAISE EXCEPTION 'verified_active_account_required' USING ERRCODE = '42501';
  END IF;

  IF p_kind = 'app_open' THEN
    v_key := 'open:' || v_today::text;
  ELSIF p_kind = 'content_saved' THEN
    v_key := 'first_content';
  ELSIF p_kind = 'explore' AND
      p_section IN ('calendar','plans','occasions','reminders','desk','settings') THEN
    v_key := 'explore:' || p_section;
  ELSIF p_kind = 'calendar_month' AND p_year BETWEEN 1200 AND 1700
      AND p_month BETWEEN 1 AND 12 THEN
    v_key := 'month:' || p_year::text || ':' || lpad(p_month::text, 2, '0');
  ELSE
    RAISE EXCEPTION 'invalid_achievement_activity' USING ERRCODE = '22023';
  END IF;

  -- Serialize concurrent navigation events per account for deterministic badge thresholds.
  PERFORM pg_advisory_xact_lock(612480, hashtext(p_account::text));

  INSERT INTO public.zurvan_achievement_activity(account_id,event_key)
  VALUES (p_account, v_key) ON CONFLICT DO NOTHING;

  IF p_kind = 'app_open' THEN
    v_badge := 'first_step';
  ELSIF p_kind = 'content_saved' THEN
    v_badge := 'xp_100';
  ELSIF p_kind = 'explore' THEN
    SELECT count(*) INTO v_explored FROM public.zurvan_achievement_activity
    WHERE account_id = p_account AND event_key LIKE 'explore:%';
    IF v_explored >= 3 THEN v_badge := 'xp_500'; END IF;
  ELSE
    SELECT count(*) INTO v_months FROM public.zurvan_achievement_activity
    WHERE account_id = p_account AND event_key LIKE ('month:' || p_year::text || ':%');
    IF v_months >= 12 THEN v_badge := 'xp_1500'; END IF;
  END IF;

  IF v_badge IS NOT NULL THEN
    INSERT INTO public.zurvan_user_badges(account_id,badge_key)
    VALUES(p_account,v_badge) ON CONFLICT DO NOTHING
    RETURNING badge_key INTO v_badge;
    IF v_badge IS NOT NULL THEN v_new := array_append(v_new,v_badge); END IF;
  END IF;

  IF p_kind = 'app_open' THEN
    IF (SELECT count(*) = 7 FROM generate_series(0,6) d
        JOIN public.zurvan_achievement_activity a
          ON a.account_id = p_account
         AND a.event_key = 'open:' || (v_today - d)::text) THEN
      INSERT INTO public.zurvan_user_badges(account_id,badge_key)
      VALUES(p_account,'streak_7') ON CONFLICT DO NOTHING
      RETURNING badge_key INTO v_badge;
      IF v_badge IS NOT NULL THEN v_new := array_append(v_new,v_badge); END IF;
    END IF;
    IF (SELECT count(*) = 30 FROM generate_series(0,29) d
        JOIN public.zurvan_achievement_activity a
          ON a.account_id = p_account
         AND a.event_key = 'open:' || (v_today - d)::text) THEN
      INSERT INTO public.zurvan_user_badges(account_id,badge_key)
      VALUES(p_account,'streak_30') ON CONFLICT DO NOTHING
      RETURNING badge_key INTO v_badge;
      IF v_badge IS NOT NULL THEN v_new := array_append(v_new,v_badge); END IF;
    END IF;
  END IF;

  RETURN jsonb_build_object('accepted',true,'event_key',v_key,'new_badges',v_new);
END;
$function$
;
REVOKE ALL ON FUNCTION public.zurvan_record_achievement_activity(uuid,text,text,integer,integer)
 FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.zurvan_record_achievement_activity(uuid,text,text,integer,integer)
 TO service_role;
