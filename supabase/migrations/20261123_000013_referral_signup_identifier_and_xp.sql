-- Capture the referral identifier only during the FIRST signup transaction.
-- Auth metadata is user supplied, but the one-time database capture is immutable
-- and is never subsequently accepted from mutable profile data.
begin;
insert into public.zurvan_invite_codes(account_id)
  select id from public.zurvan_accounts
  on conflict(account_id) do nothing;

create or replace function public.zurvan_issue_invite_code_for_account()
returns trigger language plpgsql security definer set search_path='' as $$
begin
  insert into public.zurvan_invite_codes(account_id) values(new.id)
  on conflict(account_id) do nothing;
  return new;
end;
$$;
revoke all on function public.zurvan_issue_invite_code_for_account() from public,anon,authenticated;
drop trigger if exists zurvan_issue_invite_code on public.zurvan_accounts;
create trigger zurvan_issue_invite_code
  after insert on public.zurvan_accounts
  for each row execute function public.zurvan_issue_invite_code_for_account();

-- The 'zurvan_on_auth_user_created' trigger creates the Zurvan account first.
-- PostgreSQL fires same-event triggers in alphabetical name order: this trigger
-- is intentionally named 'zurvan_z_...' so the foreign key target exists.
create or replace function public.zurvan_capture_signup_inviter()
returns trigger language plpgsql security definer set search_path='' as $$
declare v_code text; v_inviter uuid;
begin
  v_code:=upper(btrim(coalesce(new.raw_user_meta_data->>'zurvan_referral_code','')));
  if v_code !~ '^[0-9A-F]{16}$' then return new; end if;
  select c.account_id into v_inviter
  from public.zurvan_invite_codes c
  join public.zurvan_accounts a on a.id=c.account_id and a.status='active'
  where c.code=v_code and c.created_at <= new.created_at;
  if v_inviter is not null and v_inviter<>new.id then
    insert into public.zurvan_referrals(invited_account_id,inviter_account_id)
      values(new.id,v_inviter)
      on conflict(invited_account_id) do nothing;
  end if;
  return new;
end;
$$;
revoke all on function public.zurvan_capture_signup_inviter() from public,anon,authenticated;
drop trigger if exists zurvan_z_capture_signup_inviter on auth.users;
create trigger zurvan_z_capture_signup_inviter
  after insert on auth.users
  for each row execute function public.zurvan_capture_signup_inviter();

-- Reward the inviter with 100 XP ONCE per independently qualified invite.
-- An inviter cannot choose recipients or set their own award amount.
alter table public.zurvan_xp_events
  drop constraint if exists zurvan_xp_events_event_type_check;
alter table public.zurvan_xp_events
  add constraint zurvan_xp_events_event_type_check
  check (event_type in ('daily_checkin','referral_qualified'));
alter table public.zurvan_xp_events
  drop constraint if exists zurvan_xp_events_points_check;
alter table public.zurvan_xp_events
  add constraint zurvan_xp_events_points_check
  check (points between 1 and 100);

create or replace function public.zurvan_refresh_referrals(
  p_inviter uuid
) returns integer language plpgsql security invoker set search_path='' as $$
declare v_count integer; v_record record; v_total bigint; v_best integer;
begin
  -- Serialized alongside daily claims and reward redemption for this account.
  perform 1 from public.zurvan_user_scores
  where account_id=p_inviter for update;
  if not found then return 0; end if;

  for v_record in
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
      )
    returning r.invited_account_id
  loop
    insert into public.zurvan_xp_events(account_id,event_key,event_type,points)
    values(p_inviter,'referral:'||v_record.invited_account_id::text,'referral_qualified',100)
    on conflict(account_id,event_key) do nothing;
    if found then
      update public.zurvan_user_scores
      set total_xp=total_xp+100,updated_at=now()
      where account_id=p_inviter returning total_xp,best_streak into v_total,v_best;
      insert into public.zurvan_user_badges(account_id,badge_key)
      select p_inviter,c.badge_key from public.zurvan_badge_catalog c
      where v_total>=c.required_xp and v_best>=c.required_streak
      on conflict(account_id,badge_key) do nothing;
    end if;
  end loop;
  select count(*) into v_count from public.zurvan_referrals
  where inviter_account_id=p_inviter and qualified_at is not null;
  return v_count;
end;
$$;
revoke all on function public.zurvan_refresh_referrals(uuid) from public,anon,authenticated;
grant execute on function public.zurvan_refresh_referrals(uuid) to service_role;
commit;