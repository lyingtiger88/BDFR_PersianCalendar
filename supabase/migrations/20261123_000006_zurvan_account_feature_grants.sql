-- Server-authoritative account feature grants; never trust local settings to grant access.
begin;
create table if not exists public.zurvan_account_features (
  account_id uuid primary key references auth.users(id) on delete cascade,
  pahlavi_occasions boolean not null default false,
  updated_by uuid references auth.users(id),
  updated_at timestamptz not null default now()
);
alter table public.zurvan_account_features enable row level security;
revoke all on public.zurvan_account_features from public,anon,authenticated;
grant select,insert,update on public.zurvan_account_features to service_role;
create table if not exists public.zurvan_feature_audit (
  id bigint generated always as identity primary key,
  account_id uuid not null references auth.users(id) on delete cascade,
  actor_id uuid not null references auth.users(id),
  feature text not null check (feature = 'pahlavi-occasions'),
  enabled boolean not null,
  changed_at timestamptz not null default now()
);
alter table public.zurvan_feature_audit enable row level security;
revoke all on public.zurvan_feature_audit from public,anon,authenticated;
grant select,insert on public.zurvan_feature_audit to service_role;

create or replace function public.zurvan_admin_set_pahlavi_feature(
  p_actor uuid, p_account uuid, p_enabled boolean
) returns jsonb language plpgsql security invoker set search_path = '' as $$
begin
  if p_actor is null or p_account is null or p_enabled is null then
    raise exception 'invalid_feature_request' using errcode='22023';
  end if;
  if not exists (
    select 1 from public.zurvan_admin_users
    where auth_user_id=p_actor and active=true and role='superadmin'
  ) then raise exception 'admin_required' using errcode='42501'; end if;
  if not exists (select 1 from auth.users where id=p_account) then
    raise exception 'account_not_found' using errcode='22023';
  end if;
  insert into public.zurvan_account_features(account_id,pahlavi_occasions,updated_by)
    values (p_account,p_enabled,p_actor)
    on conflict(account_id) do update set pahlavi_occasions=excluded.pahlavi_occasions,
      updated_by=excluded.updated_by, updated_at=now();
  insert into public.zurvan_feature_audit(account_id,actor_id,feature,enabled)
    values(p_account,p_actor,'pahlavi-occasions',p_enabled);
  return jsonb_build_object('account_id',p_account,'pahlavi_occasions',p_enabled);
end; $$;
revoke all on function public.zurvan_admin_set_pahlavi_feature(uuid,uuid,boolean)
  from public,anon,authenticated;
grant execute on function public.zurvan_admin_set_pahlavi_feature(uuid,uuid,boolean) to service_role;
commit;
