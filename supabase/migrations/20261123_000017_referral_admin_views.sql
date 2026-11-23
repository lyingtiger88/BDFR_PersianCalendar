-- Private-to-admin read-only consolidated referral views.
-- These are public-schema views for convenient Supabase Table Editor browsing,
-- but have NO privileges for anon/authenticated users. Never expose emails
-- through the user-progress API or the public leaderboard.
begin;

create or replace view public.zurvan_referral_admin_summary
with (security_barrier = true)
as
select
  a.id as account_id,
  coalesce(nullif(a.username,''),'—') as username,
  u.email as email,
  a.status as account_status,
  c.code as referral_code,
  coalesce(r.total_invited,0)::integer as total_invited,
  coalesce(r.successful_invites,0)::integer as successful_invites,
  coalesce(r.pending_invites,0)::integer as pending_invites,
  coalesce(s.total_xp,0)::bigint as total_xp,
  coalesce(w.silver_rewards,0)::integer as silver_rewards_issued,
  w.latest_silver_expires_at,
  greatest(0,(coalesce(w.silver_rewards,0)+1)*15-
    coalesce(r.successful_invites,0))::integer as invites_until_next_silver,
  a.created_at as account_created_at
from public.zurvan_accounts a
left join auth.users u on u.id=a.id
left join public.zurvan_invite_codes c on c.account_id=a.id
left join public.zurvan_user_scores s on s.account_id=a.id
left join lateral (
 select
   count(*) as total_invited,
   count(*) filter (where qualified_at is not null) as successful_invites,
   count(*) filter (where qualified_at is null) as pending_invites
 from public.zurvan_referrals r
 where r.inviter_account_id=a.id
) r on true
left join lateral (
 select
   count(*) as silver_rewards,
   max(expires_at) filter (where status='active') as latest_silver_expires_at
 from public.zurvan_referral_silver_rewards w
 where w.account_id=a.id
) w on true;

create or replace view public.zurvan_referral_admin_details
with (security_barrier = true)
as
select
  r.inviter_account_id,
  coalesce(nullif(inviter.username,''),'—') as inviter_username,
  inviter_auth.email as inviter_email,
  c.code as inviter_referral_code,
  r.invited_account_id,
  coalesce(nullif(invited.username,''),'—') as invited_username,
  invited_auth.email as invited_email,
  case when r.qualified_at is null then 'pending' else 'successful' end
    as referral_status,
  r.linked_at,
  r.qualified_at
from public.zurvan_referrals r
join public.zurvan_accounts inviter on inviter.id=r.inviter_account_id
join public.zurvan_accounts invited on invited.id=r.invited_account_id
left join auth.users inviter_auth on inviter_auth.id=inviter.id
left join auth.users invited_auth on invited_auth.id=invited.id
left join public.zurvan_invite_codes c on c.account_id=inviter.id;

revoke all on public.zurvan_referral_admin_summary,
 public.zurvan_referral_admin_details from public,anon,authenticated;
grant select on public.zurvan_referral_admin_summary,
 public.zurvan_referral_admin_details to service_role;
comment on view public.zurvan_referral_admin_summary is
 'Admin only: email, username, code, successful/pending referrals, XP and Silver rewards.';
comment on view public.zurvan_referral_admin_details is
 'Admin only: inviter and invitee email/username with individual referral status.';
commit;