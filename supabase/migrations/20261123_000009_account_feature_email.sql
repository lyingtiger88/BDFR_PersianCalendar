-- Keep an admin-friendly email alongside account UUID in the
-- service-role-only feature grant table. Email is identification/display
-- metadata, never an authorization key; Auth UUID remains canonical.
begin;

alter table public.zurvan_account_features
  add column if not exists email text;

update public.zurvan_account_features as f
set email = u.email
from auth.users as u
where f.account_id = u.id
  and f.email is distinct from u.email;

create or replace function public.zurvan_feature_fill_email()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  select u.email into new.email
  from auth.users as u
  where u.id = new.account_id;
  return new;
end;
$$;
revoke all on function public.zurvan_feature_fill_email()
  from public, anon, authenticated;

drop trigger if exists zurvan_feature_fill_email on public.zurvan_account_features;
create trigger zurvan_feature_fill_email
  before insert or update of account_id on public.zurvan_account_features
  for each row execute function public.zurvan_feature_fill_email();

create or replace function public.zurvan_feature_sync_auth_email()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  update public.zurvan_account_features
    set email = new.email
    where account_id = new.id
      and email is distinct from new.email;
  return new;
end;
$$;
revoke all on function public.zurvan_feature_sync_auth_email()
  from public, anon, authenticated;

drop trigger if exists zurvan_feature_sync_auth_email on auth.users;
create trigger zurvan_feature_sync_auth_email
  after update of email on auth.users
  for each row
  when (old.email is distinct from new.email)
  execute function public.zurvan_feature_sync_auth_email();

commit;
