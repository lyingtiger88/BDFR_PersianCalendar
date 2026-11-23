-- Server-authoritative, privacy-conscious Zurvan XP, earned badges and ranking.
-- Founder/editorial ranks in zurvan_special_ranks remain fully independent.
begin;

create table if not exists public.zurvan_user_scores (
  account_id uuid primary key references public.zurvan_accounts(id) on delete cascade,
  total_xp bigint not null default 0 check (total_xp >= 0),
  daily_streak integer not null default 0 check (daily_streak >= 0),
  best_streak integer not null default 0 check (best_streak >= 0),
  last_claim_date date,
  leaderboard_opt_in boolean not null default false,
  updated_at timestamptz not null default now()
);
create index if not exists zurvan_user_scores_top_idx
  on public.zurvan_user_scores(total_xp desc, account_id)
  where leaderboard_opt_in = true;

create table if not exists public.zurvan_xp_events (
  id bigint generated always as identity primary key,
  account_id uuid not null references public.zurvan_accounts(id) on delete cascade,
  event_key text not null check (length(event_key) between 1 and 120),
  event_type text not null check (event_type in ('daily_checkin')),
  points integer not null check (points between 1 and 100),
  created_at timestamptz not null default now(),
  unique(account_id,event_key)
);
create index if not exists zurvan_xp_events_history_idx
  on public.zurvan_xp_events(account_id,created_at desc);

create table if not exists public.zurvan_badge_catalog (
  badge_key text primary key,
  name_fa text not null,
  description_fa text not null,
  icon text not null,
  required_xp bigint not null default 0 check (required_xp >= 0),
  required_streak integer not null default 0 check (required_streak >= 0),
  display_order integer not null default 0
);
insert into public.zurvan_badge_catalog
  (badge_key,name_fa,description_fa,icon,required_xp,required_streak,display_order)
values
  ('first_step','قدم اول','اولین حضور روزانه در زروان','🌱',20,1,10),
  ('xp_100','ستاره آغاز','کسب ۱۰۰ امتیاز','⭐',100,0,20),
  ('xp_500','کوشای زروان','کسب ۵۰۰ امتیاز','🌟',500,0,30),
  ('xp_1500','استاد زمان','کسب ۱۵۰۰ امتیاز','🏆',1500,0,40),
  ('streak_7','هفت روز پیوسته','۷ روز فعالیت متوالی','🔥',0,7,50),
  ('streak_30','یک ماه پایدار','۳۰ روز فعالیت متوالی','💎',0,30,60)
on conflict(badge_key) do nothing;

create table if not exists public.zurvan_user_badges (
  account_id uuid not null references public.zurvan_accounts(id) on delete cascade,
  badge_key text not null references public.zurvan_badge_catalog(badge_key),
  earned_at timestamptz not null default now(),
  primary key(account_id,badge_key)
);

alter table public.zurvan_user_scores enable row level security;
alter table public.zurvan_xp_events enable row level security;
alter table public.zurvan_badge_catalog enable row level security;
alter table public.zurvan_user_badges enable row level security;
revoke all on public.zurvan_user_scores,public.zurvan_xp_events,
  public.zurvan_badge_catalog,public.zurvan_user_badges from public,anon,authenticated;
grant select,insert,update on public.zurvan_user_scores to service_role;
grant select,insert on public.zurvan_xp_events to service_role;
grant select on public.zurvan_badge_catalog,public.zurvan_user_badges to service_role;
grant insert on public.zurvan_user_badges to service_role;
grant usage,select on sequence public.zurvan_xp_events_id_seq to service_role;

create or replace function public.zurvan_claim_daily_xp(p_account uuid)
returns jsonb language plpgsql security invoker set search_path = '' as $$
declare
  current_day date := (now() at time zone 'UTC')::date;
  prior_day date;
  next_streak integer;
  awarded integer;
  total bigint;
  already_claimed boolean := false;
begin
  if p_account is null or not exists (
    select 1 from auth.users u
    join public.zurvan_accounts za on za.id=u.id
    where u.id=p_account and u.email_confirmed_at is not null and za.status='active'
  ) then raise exception 'verified_active_account_required' using errcode='42501'; end if;

  insert into public.zurvan_user_scores(account_id) values (p_account)
    on conflict(account_id) do nothing;

  select s.last_claim_date, s.daily_streak, s.total_xp
    into prior_day,next_streak,total
    from public.zurvan_user_scores s where s.account_id=p_account for update;
  if prior_day = current_day then
    return jsonb_build_object('claimed',false,'awarded_xp',0,
      'total_xp',total,'daily_streak',next_streak,'date',current_day);
  end if;

  next_streak := case when prior_day = current_day-1 then next_streak+1 else 1 end;
  awarded := 20 + 5 * least(next_streak-1,6);
  -- Unique event key is a second idempotency barrier.
  insert into public.zurvan_xp_events(account_id,event_key,event_type,points)
    values(p_account,'daily:'||current_day::text,'daily_checkin',awarded)
    on conflict(account_id,event_key) do nothing;
  if not found then
    select s.total_xp,s.daily_streak into total,next_streak
    from public.zurvan_user_scores s where s.account_id=p_account;
    return jsonb_build_object('claimed',false,'awarded_xp',0,
      'total_xp',total,'daily_streak',next_streak,'date',current_day);
  end if;

  update public.zurvan_user_scores
    set total_xp=total_xp+awarded,
        daily_streak=next_streak,
        best_streak=greatest(best_streak,next_streak),
        last_claim_date=current_day,
        updated_at=now()
    where account_id=p_account
    returning total_xp into total;

  insert into public.zurvan_user_badges(account_id,badge_key)
    select p_account,c.badge_key
    from public.zurvan_badge_catalog c
    where total>=c.required_xp and next_streak>=c.required_streak
    on conflict(account_id,badge_key) do nothing;

  return jsonb_build_object('claimed',true,'awarded_xp',awarded,
    'total_xp',total,'daily_streak',next_streak,'date',current_day);
end;
$$;

create or replace function public.zurvan_set_score_visibility(
  p_account uuid,p_visible boolean
) returns jsonb language plpgsql security invoker set search_path = '' as $$
begin
  if p_account is null or p_visible is null or not exists (
    select 1 from auth.users u
    join public.zurvan_accounts a on a.id=u.id
    where u.id=p_account and a.status='active' and u.email_confirmed_at is not null
  ) then raise exception 'verified_active_account_required' using errcode='42501'; end if;
  insert into public.zurvan_user_scores(account_id,leaderboard_opt_in)
    values(p_account,p_visible)
    on conflict(account_id) do update
    set leaderboard_opt_in=excluded.leaderboard_opt_in,updated_at=now();
  return jsonb_build_object('leaderboard_opt_in',p_visible);
end;
$$;
revoke all on function public.zurvan_claim_daily_xp(uuid),
  public.zurvan_set_score_visibility(uuid,boolean) from public,anon,authenticated;
grant execute on function public.zurvan_claim_daily_xp(uuid),
  public.zurvan_set_score_visibility(uuid,boolean) to service_role;
commit;
