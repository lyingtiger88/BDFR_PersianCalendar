-- Mobile free-trial device claims start unverified. Do not grant writable
-- entitlement until Apple's DeviceCheck or Google's Play Integrity device
-- recall has been successfully updated by the trusted Edge Function.
begin;
alter table public.zurvan_trial_devices
  add column if not exists platform text not null default 'windows';
alter table public.zurvan_trial_devices
  add column if not exists verified boolean not null default true;
alter table public.zurvan_trial_devices
  drop constraint if exists zurvan_trial_devices_platform_chk;
alter table public.zurvan_trial_devices
  add constraint zurvan_trial_devices_platform_chk
    check(platform in ('windows','android','ios'));
grant select, insert, update on public.zurvan_trial_devices to service_role;
commit;
