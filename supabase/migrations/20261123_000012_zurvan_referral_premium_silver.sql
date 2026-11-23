-- Invite 15 independently verified new users => redeem one 12-month Premium Silver
-- award. A referral Silver entitlement never overwrites paid/lifetime licenses.
begin;

create table public.zurvan_invite_codes (
  account_id uuid primary key references public.zurvan_accounts(id) on delete cascade,
  code text not null unique default upper(encode(gen_random_bytes(8),'hex')),
  created_at timestamptz not null default now(),
  constraint zurvan_invite_code_syntax check (code ~ '^[0-9A-F]{16}$')
);

create table public.zurvan_referrals (
  invited_account_id uuid primary key references public.zurvan_accounts(id) on delete cascade,
  inviter_account_id uuid not null references public.zurvan_accounts(id) on delete cascade,
  linked_at timestamptz not null default now(),
  qualified_at timestamptz,
  constraint zurvan_referral_no_self check (invited_account_id <> inviter_account_id)
);
create index zurvan_referrals_inviter_idx
  on public.zurvan_referrals(inviter_account_id, qualified_at);

create table public.zurvan_referral_silver_rewards (
  id uuid primary key default gen_random_uuid(),
  account_id uuid not null references public.zurvan_accounts(id) on delete cascade,
  reward_number integer not null check (reward_number > 0),
  required_referrals integer not null default 15 check (required_referrals = 15),
  plan_code text not null default 'premium_silver' check (plan_code = 'premium_silver'),
  awarded_at timestamptz not null default now(),
  starts_at timestamptz not null,
  expires_at timestamptz not null,
  status text not null default 'active' check (status in ('active','revoked')),
  unique(account_id,reward_number),
  constraint zurvan_referral_silver_exact_year check (expires_at = starts_at + interval '1 year')
);
create index zurvan_referral_silver_lookup_idx
  on public.zurvan_referral_silver_rewards(account_id, starts_at, expires_at)
  where status='active';

alter table public.zurvan_invite_codes enable row level security;
alter table public.zurvan_referrals enable row level security;
alter table public.zurvan_referral_silver_rewards enable row level security;
revoke all on public.zurvan_invite_codes,public.zurvan_referrals,
  public.zurvan_referral_silver_rewards from public,anon,authenticated;
grant select,insert on public.zurvan_invite_codes to service_role;
grant select,insert,update on public.zurvan_referrals to service_role;
grant select,insert on public.zurvan_referral_silver_rewards to service_role;

-- Called only by the authenticated, service-role-backed Edge Function.
-- A code must predate the invitee's registration; the referral must be
-- submitted within seven days of signup. One invitee can have one inviter.
create or replace function public.zurvan_link_invite(
  p_invited uuid,p_code text
) returns jsonb language plpgsql security invoker set search_path='' as $$
declare v_inviter uuid; v_created timestamptz; v_user_created timestamptz;
begin
  if p_invited is null or p_code is null or p_code !~ '^[0-9A-Fa-f]{16}$'
  then raise exception 'invalid_invite_code' using errcode='22023'; end if;
  select c.account_id,c.created_at into v_inviter,v_created
  from public.zurvan_invite_codes c
  join public.zurvan_accounts a on a.id=c.account_id and a.status='active'
  where c.code=upper(p_code);
  if v_inviter is null then
    raise exception 'invite_code_not_found' using errcode='22023'; end if;
  if v_inviter=p_invited then
    raise exception 'self_referral_forbidden' using errcode='22023'; end if;
  select u.created_at into v_user_created
  from auth.users u join public.zurvan_accounts a on a.id=u.id
  where u.id=p_invited and u.email_confirmed_at is not null and a.status='active';
  if v_user_created is null then
    raise exception 'verified_account_required' using errcode='42501'; end if;
  if v_user_created < v_created or now() > v_user_created + interval '7 days'
  then raise exception 'referral_registration_window_closed' using errcode='22023'; end if;
  if exists(select 1 from public.zurvan_referrals r
       where r.invited_account_id=p_invited) then
    raise exception 'referral_already_linked' using errcode='23505'; end if;
  insert into public.zurvan_referrals(invited_account_id,inviter_account_id)
    values(p_invited,v_inviter);
  return jsonb_build_object('linked',true,'qualified',false);
end;
$$;

-- Qualification permanently records verified independent devices, or
-- bona fide activated paid/lifetime account licenses.
-- An email-only account, or an unverified mobile claim, never counts.
create or replace function public.zurvan_refresh_referrals(
  p_inviter uuid
) returns integer language plpgsql security invoker set search_path='' as $$
declare v_count integer;
begin
  update public.zurvan_referrals r
  set qualified_at=now()
  where r.inviter_account_id=p_inviter and r.qualified_at is null
    and exists (
      select 1 from auth.users u
      join public.zurvan_accounts a on a.id=u.id
      where u.id=r.invited_account_id and a.status='active'
        and u.email_confirmed_at is not null
    )
    and (
      exists (
        select 1 from public.zurvan_trial_devices d
        where d.first_account_id=r.invited_account_id and d.verified=true
      )
      or exists (
        select 1 from public.zurvan_licenses l
        where l.account_id=r.invited_account_id and l.product_id='zurvan'
          and l.type in ('subscription','lifetime') and l.status='active'
      )
    );
  select count(*) into v_count from public.zurvan_referrals r
  where r.inviter_account_id=p_inviter and r.qualified_at is not null;
  return v_count;
end;
$$;

create or replace function public.zurvan_claim_referral_silver(
  p_account uuid
) returns jsonb language plpgsql security invoker set search_path='' as $$
declare v_count integer; v_issued integer; v_start timestamptz;
        v_reward public.zurvan_referral_silver_rewards%rowtype;
begin
  if p_account is null or not exists (
    select 1 from public.zurvan_accounts a
    join auth.users u on u.id=a.id
    where a.id=p_account and a.status='active' and u.email_confirmed_at is not null
  ) then raise exception 'verified_active_account_required' using errcode='42501'; end if;
  -- Serialize multiple concurrent reward requests from the same account.
  perform 1 from public.zurvan_user_scores where account_id=p_account for update;
  if not found then raise exception 'score_profile_required' using errcode='42501'; end if;
  v_count:=public.zurvan_refresh_referrals(p_account);
  select count(*) into v_issued from public.zurvan_referral_silver_rewards
    where account_id=p_account;
  if v_count < (v_issued+1)*15 then
    return jsonb_build_object('claimed',false,'successful_invites',v_count,
      'rewarded_count',v_issued,'remaining',greatest(0,(v_issued+1)*15-v_count));
  end if;
  select greatest(now(),coalesce(max(expires_at),now())) into v_start
  from public.zurvan_referral_silver_rewards
  where account_id=p_account and status='active';
  insert into public.zurvan_referral_silver_rewards
    (account_id,reward_number,starts_at,expires_at)
    values(p_account,v_issued+1,v_start,v_start+interval '1 year')
    returning * into v_reward;
  return jsonb_build_object('claimed',true,'successful_invites',v_count,
    'rewarded_count',v_issued+1,'remaining',greatest(0,(v_issued+2)*15-v_count),
    'plan_code','premium_silver','starts_at',v_reward.starts_at,
    'expires_at',v_reward.expires_at,'reward_id',v_reward.id);
end;
$$;

revoke all on function public.zurvan_link_invite(uuid,text),
 public.zurvan_refresh_referrals(uuid),
 public.zurvan_claim_referral_silver(uuid) from public,anon,authenticated;
grant execute on function public.zurvan_link_invite(uuid,text),
 public.zurvan_refresh_referrals(uuid),
 public.zurvan_claim_referral_silver(uuid) to service_role;
commit;
