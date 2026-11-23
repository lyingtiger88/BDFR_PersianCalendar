-- A free trial may have only one physical-device claim per account.
-- Combined with the fingerprint primary key this protects both dimensions
-- even if two mobile verification requests race concurrently.
create unique index if not exists zurvan_trial_devices_one_account
  on public.zurvan_trial_devices(first_account_id);
