-- Enforce one automatic free Zurvan trial per device identity.
-- Apply this migration BEFORE deploying the updated Edge Function and Windows client.
begin;

create table if not exists public.zurvan_trial_devices (
    fingerprint text primary key check (fingerprint ~ '^[0-9a-f]{64}$'),
    first_account_id uuid not null references auth.users(id) on delete restrict,
    claimed_at timestamptz not null default now()
);
create index if not exists zurvan_trial_devices_first_account_idx
    on public.zurvan_trial_devices(first_account_id);
alter table public.zurvan_trial_devices enable row level security;
revoke all on public.zurvan_trial_devices from public, anon, authenticated;
grant select, insert on public.zurvan_trial_devices to service_role;

create or replace function public.zurvan_start_trial(
  p_auth_user_id uuid,
  p_key_fingerprint text default null
)
returns jsonb
language plpgsql
security invoker
set search_path = ''
as $$
declare
  account_id_value uuid;
  license_row public.zurvan_licenses%rowtype;
  license_id_value uuid;
  trial_expiry timestamptz;
  existing_owner uuid;
begin
  if p_auth_user_id is null then
    raise exception 'invalid_trial_request' using errcode = '22023';
  end if;

  select a.id into account_id_value
    from public.accounts a
    where a.auth_user_id = p_auth_user_id and a.state = 'active'
    for update;
  if not found then
    raise exception 'account_not_registered' using errcode = '42501';
  end if;

  if not exists (
    select 1 from public.zurvan_accounts za
    where za.id = p_auth_user_id and za.status = 'active'
  ) then
    raise exception 'license_account_inactive' using errcode = '42501';
  end if;

  if not exists (
    select 1 from public.zurvan_products p
    where p.id = 'zurvan' and p.enabled
  ) then
    raise exception 'product_unavailable' using errcode = '55000';
  end if;

  -- Existing paid and trial licenses remain untouched. In particular,
  -- don't require a new device identifier just to read an existing license.
  select * into license_row from public.zurvan_licenses
    where account_id = p_auth_user_id and product_id = 'zurvan'
    for update;
  if found then
    return jsonb_build_object(
      'status', case
        when license_row.status <> 'active' then license_row.status
        when license_row.expires_at is not null and license_row.expires_at <= now() then 'expired'
        else 'active' end,
      'license_type', license_row.type,
      'expires_at', license_row.expires_at,
      'trial_created', false,
      'can_write', license_row.status = 'active' and
        (license_row.expires_at is null or license_row.expires_at > now())
    );
  end if;

  -- New free trials MUST have a device identity. Never silently fall back
  -- to an account-only free trial.
  if p_key_fingerprint is null or
     p_key_fingerprint !~ '^[0-9a-f]{64}$' then
    raise exception 'device_identity_required' using errcode = '22023';
  end if;

  -- Unique primary key + transaction lock serialize claims across accounts.
  perform pg_catalog.pg_advisory_xact_lock(
    pg_catalog.hashtextextended(p_key_fingerprint, 0)
  );
  select d.first_account_id into existing_owner
    from public.zurvan_trial_devices d
    where d.fingerprint = p_key_fingerprint
    for update;
  if found and existing_owner <> p_auth_user_id then
    raise exception 'device_trial_already_used' using errcode = '23505';
  end if;
  if not found then
    insert into public.zurvan_trial_devices(fingerprint, first_account_id)
      values (p_key_fingerprint, p_auth_user_id);
  end if;

  trial_expiry := now() + interval '45 days';
  insert into public.zurvan_licenses(
    account_id, product_id, type, status, generation, expires_at
  ) values (
    p_auth_user_id, 'zurvan', 'trial', 'active', 1, trial_expiry
  ) returning id into license_id_value;

  return jsonb_build_object(
    'status', 'active', 'license_type', 'trial',
    'expires_at', trial_expiry, 'trial_created', true,
    'can_write', true
  );
end;
$$;
revoke all on function public.zurvan_start_trial(uuid,text)
  from public, anon, authenticated;
grant execute on function public.zurvan_start_trial(uuid,text)
  to service_role;

commit;
