-- Five-character uppercase alphanumeric inviter codes (36^5 possibilities).
-- Retain old 8- and 16-character identifiers as hidden backward-compatible aliases.
begin;
alter table public.zurvan_invite_codes
  add column if not exists legacy_short_code text unique;
update public.zurvan_invite_codes
set legacy_short_code=code where legacy_short_code is null;
do $$
begin
 if exists (
  select 1 from (select left(code,5) as prefix from public.zurvan_invite_codes group by 1 having count(*)>1) t
 ) then raise exception 'legacy_code_prefix_collision_manual_resolution_required'; end if;
end $$;

alter table public.zurvan_invite_codes drop constraint if exists zurvan_invite_code_syntax;
update public.zurvan_invite_codes set code=left(code,5);
alter table public.zurvan_invite_codes
  add constraint zurvan_invite_code_syntax check(code ~ '^[A-Z0-9]{5}$');

-- No predictable sequence: each character is derived from pgcrypto random bytes.
-- Rejection sampling avoids modulo bias (252 is divisible by 36).
create or replace function public.zurvan_generate_invite_identifier()
returns text language plpgsql volatile security invoker set search_path='' as $$
declare
 alphabet constant text := 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
 code text:=''; candidate integer;
begin
 while length(code)<5 loop
  candidate := get_byte(gen_random_bytes(1),0);
  if candidate < 252 then
    code:=code||substr(alphabet,(candidate % 36)+1,1);
  end if;
 end loop;
 return code;
end;
$$;
revoke all on function public.zurvan_generate_invite_identifier()
 from public,anon,authenticated;
grant execute on function public.zurvan_generate_invite_identifier() to service_role;

alter table public.zurvan_invite_codes
  alter column code set default public.zurvan_generate_invite_identifier();

create or replace function public.zurvan_issue_invite_code_for_account()
returns trigger language plpgsql security definer set search_path='' as $$
declare attempt integer;
begin
 for attempt in 1..30 loop
  begin
   insert into public.zurvan_invite_codes(account_id,code)
   values(new.id,public.zurvan_generate_invite_identifier())
   on conflict(account_id) do nothing;
   return new;
  exception when unique_violation then
   -- Extremely rare random-code collision; retry without affecting other rows.
  end;
 end loop;
 raise exception 'referral_code_generation_unavailable';
end;
$$;
revoke all on function public.zurvan_issue_invite_code_for_account()
 from public,anon,authenticated;

create or replace function public.zurvan_capture_signup_inviter()
returns trigger language plpgsql security definer set search_path='' as $$
declare v_code text;v_inviter uuid;
begin
 v_code:=upper(btrim(coalesce(new.raw_user_meta_data->>'zurvan_referral_code','')));
 if v_code !~ '^([A-Z0-9]{5}|[0-9A-F]{8}|[0-9A-F]{16})$' then return new;end if;
 select c.account_id into v_inviter
 from public.zurvan_invite_codes c
 join public.zurvan_accounts a on a.id=c.account_id and a.status='active'
 where (c.code=v_code or c.legacy_short_code=v_code or c.legacy_code=v_code)
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

create or replace function public.zurvan_link_invite(p_invited uuid,p_code text)
returns jsonb language plpgsql security invoker set search_path='' as $$
declare v_inviter uuid;v_created timestamptz;v_created_user timestamptz;v_code text;
begin
 v_code:=upper(btrim(coalesce(p_code,'')));
 if p_invited is null or v_code !~ '^([A-Z0-9]{5}|[0-9A-F]{8}|[0-9A-F]{16})$'
 then raise exception 'invalid_invite_code' using errcode='22023';end if;
 select c.account_id,c.created_at into v_inviter,v_created
 from public.zurvan_invite_codes c
 join public.zurvan_accounts a on a.id=c.account_id and a.status='active'
 where c.code=v_code or c.legacy_short_code=v_code or c.legacy_code=v_code;
 if v_inviter is null then raise exception 'invite_code_not_found' using errcode='22023';end if;
 if v_inviter=p_invited then raise exception 'self_referral_forbidden' using errcode='22023';end if;
 select u.created_at into v_created_user from auth.users u
 join public.zurvan_accounts a on a.id=u.id
 where u.id=p_invited and u.email_confirmed_at is not null and a.status='active';
 if v_created_user is null then raise exception 'verified_account_required' using errcode='42501';end if;
 if v_created_user<v_created or now()>v_created_user+interval '7 days'
 then raise exception 'referral_registration_window_closed' using errcode='22023';end if;
 if exists(select 1 from public.zurvan_referrals r where r.invited_account_id=p_invited)
 then raise exception 'referral_already_linked' using errcode='23505';end if;
 insert into public.zurvan_referrals(invited_account_id,inviter_account_id)
 values(p_invited,v_inviter);
 return jsonb_build_object('linked',true,'qualified',false);
end;
$$;
revoke all on function public.zurvan_link_invite(uuid,text)
 from public,anon,authenticated;
grant execute on function public.zurvan_link_invite(uuid,text) to service_role;
commit;