-- Friendly 8-character referral identifiers. Preserve the original 16-character
-- codes as legacy aliases so already-shared identifiers keep working.
begin;
alter table public.zurvan_invite_codes add column legacy_code text unique;
update public.zurvan_invite_codes set legacy_code=code where legacy_code is null;
alter table public.zurvan_invite_codes drop constraint zurvan_invite_code_syntax;
update public.zurvan_invite_codes set code=left(code,8);
alter table public.zurvan_invite_codes
  add constraint zurvan_invite_code_syntax check(code ~ '^[0-9A-F]{8}$');
alter table public.zurvan_invite_codes
  alter column code set default upper(encode(gen_random_bytes(4),'hex'));

-- All existing codes are random; the UNIQUE constraint guards new codes.
-- In the unlikely event of a collision, the insert itself will retry.
create or replace function public.zurvan_issue_invite_code_for_account()
returns trigger language plpgsql security definer set search_path='' as $$
declare v_code text; v_attempt integer;
begin
  for v_attempt in 1..20 loop
    v_code:=upper(encode(gen_random_bytes(4),'hex'));
    begin
      insert into public.zurvan_invite_codes(account_id,code)
      values(new.id,v_code) on conflict(account_id) do nothing;
      return new;
    exception when unique_violation then
      -- Retry only if another account happens to have the random code.
    end;
  end loop;
  raise exception 'referral_code_generation_unavailable';
end;
$$;
revoke all on function public.zurvan_issue_invite_code_for_account()
  from public,anon,authenticated;

create or replace function public.zurvan_capture_signup_inviter()
returns trigger language plpgsql security definer set search_path='' as $$
declare v_code text; v_inviter uuid;
begin
  v_code:=upper(btrim(coalesce(new.raw_user_meta_data->>'zurvan_referral_code','')));
  if v_code !~ '^([0-9A-F]{8}|[0-9A-F]{16})$' then return new; end if;
  select c.account_id into v_inviter from public.zurvan_invite_codes c
  join public.zurvan_accounts a on a.id=c.account_id and a.status='active'
  where (c.code=v_code or c.legacy_code=v_code)
    and c.created_at<=new.created_at;
  if v_inviter is not null and v_inviter<>new.id then
    insert into public.zurvan_referrals(invited_account_id,inviter_account_id)
      values(new.id,v_inviter)
      on conflict(invited_account_id) do nothing;
  end if;
  return new;
end;
$$;
revoke all on function public.zurvan_capture_signup_inviter()
 from public,anon,authenticated;

create or replace function public.zurvan_link_invite(
  p_invited uuid,p_code text
) returns jsonb language plpgsql security invoker set search_path='' as $$
declare v_inviter uuid;v_created timestamptz;v_user_created timestamptz;v_code text;
begin
  v_code:=upper(btrim(coalesce(p_code,'')));
  if p_invited is null or v_code !~ '^([0-9A-F]{8}|[0-9A-F]{16})$'
  then raise exception 'invalid_invite_code' using errcode='22023';end if;
  select c.account_id,c.created_at into v_inviter,v_created
  from public.zurvan_invite_codes c
  join public.zurvan_accounts a on a.id=c.account_id and a.status='active'
  where c.code=v_code or c.legacy_code=v_code;
  if v_inviter is null then raise exception 'invite_code_not_found' using errcode='22023';end if;
  if v_inviter=p_invited then raise exception 'self_referral_forbidden' using errcode='22023';end if;
  select u.created_at into v_user_created from auth.users u
  join public.zurvan_accounts a on a.id=u.id
  where u.id=p_invited and u.email_confirmed_at is not null and a.status='active';
  if v_user_created is null then raise exception 'verified_account_required' using errcode='42501';end if;
  if v_user_created<v_created or now()>v_user_created+interval '7 days'
  then raise exception 'referral_registration_window_closed' using errcode='22023';end if;
  if exists(select 1 from public.zurvan_referrals r where r.invited_account_id=p_invited)
  then raise exception 'referral_already_linked' using errcode='23505';end if;
  insert into public.zurvan_referrals(invited_account_id,inviter_account_id)
  values(p_invited,v_inviter);
  return jsonb_build_object('linked',true,'qualified',false);
end;
$$;
revoke all on function public.zurvan_link_invite(uuid,text) from public,anon,authenticated;
grant execute on function public.zurvan_link_invite(uuid,text) to service_role;
commit;