-- Automatically show every Zurvan account in the special-feature table.
-- Existing grants and timestamps are preserved; a missing grant is OFF.
begin;

insert into public.zurvan_account_features (account_id, pahlavi_occasions)
select za.id, false
from public.zurvan_accounts as za
on conflict (account_id) do nothing;

create or replace function public.zurvan_auto_enroll_account_features()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  insert into public.zurvan_account_features (account_id, pahlavi_occasions)
  values (new.id, false)
  on conflict (account_id) do nothing;
  return new;
end;
$$;

revoke all on function public.zurvan_auto_enroll_account_features()
  from public, anon, authenticated;

drop trigger if exists zurvan_auto_enroll_account_features
  on public.zurvan_accounts;
create trigger zurvan_auto_enroll_account_features
  after insert on public.zurvan_accounts
  for each row
  execute function public.zurvan_auto_enroll_account_features();

commit;