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
  device_row record;
  device_id_value uuid;
  license_id_value uuid;
  trial_expiry timestamptz;
begin
  if p_auth_user_id is null or
     (p_key_fingerprint is not null and p_key_fingerprint !~ '^[0-9a-f]{64}$') then
    raise exception 'invalid_trial_request' using errcode='22023';
  end if;
  select a.id into account_id_value from public.accounts a
    where a.auth_user_id=p_auth_user_id and a.state='active' for update;
  if not found then raise exception 'account_not_registered' using errcode='42501'; end if;
  if not exists(select 1 from public.zurvan_accounts za
    where za.id=p_auth_user_id and za.status='active') then
    raise exception 'license_account_inactive' using errcode='42501';
  end if;
  if not exists(select 1 from public.zurvan_products p where p.id='zurvan' and p.enabled) then
    raise exception 'product_unavailable' using errcode='55000';
  end if;
  if p_key_fingerprint is not null then
    select d.id,d.public_key,d.key_fingerprint,d.platform,d.device_label into device_row
      from public.devices d where d.account_id=account_id_value
      and d.key_fingerprint=p_key_fingerprint and d.revoked_at is null for update;
    if not found then raise exception 'device_not_registered' using errcode='42501'; end if;
  end if;
  select * into license_row from public.zurvan_licenses
    where account_id=p_auth_user_id and product_id='zurvan' for update;
  if found then
    return jsonb_build_object('status',case when license_row.status<>'active' then license_row.status
      when license_row.expires_at is not null and license_row.expires_at<=now() then 'expired'
      else 'active' end,'license_type',license_row.type,'expires_at',license_row.expires_at,
      'trial_created',false,'can_write',license_row.status='active' and
      (license_row.expires_at is null or license_row.expires_at>now()));
  end if;
  if p_key_fingerprint is not null then
    insert into public.zurvan_devices(account_id,key_fingerprint,public_key,platform,label)
      values(p_auth_user_id,device_row.key_fingerprint,device_row.public_key,
        device_row.platform,coalesce(device_row.device_label,'Zurvan device'))
      on conflict(key_fingerprint) do nothing;
    select zd.id into device_id_value from public.zurvan_devices zd
      where zd.key_fingerprint=p_key_fingerprint and zd.account_id=p_auth_user_id for update;
    if not found then raise exception 'device_mapping_conflict' using errcode='23505'; end if;
  end if;
  trial_expiry:=now()+interval '45 days';
  insert into public.zurvan_licenses(account_id,product_id,type,status,generation,expires_at)
    values(p_auth_user_id,'zurvan','trial','active',1,trial_expiry) returning id into license_id_value;
  if device_id_value is not null then
    insert into public.zurvan_activations(license_id,device_id)
      values(license_id_value,device_id_value) on conflict do nothing;
  end if;
  return jsonb_build_object('status','active','license_type','trial',
    'expires_at',trial_expiry,'trial_created',true,'can_write',true);
end;
$$;
revoke all on function public.zurvan_start_trial(uuid,text) from public,anon,authenticated;
grant execute on function public.zurvan_start_trial(uuid,text) to service_role;