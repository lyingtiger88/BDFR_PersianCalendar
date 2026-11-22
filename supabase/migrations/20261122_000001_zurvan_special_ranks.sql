begin;

alter table public.zurvan_accounts
  add column if not exists super_type text,
  add column if not exists super_title text;

create table if not exists public.zurvan_special_ranks (
  code text primary key,
  display_name text not null,
  display_name_fa text not null,
  description text not null default '',
  badge_key text not null,
  access_level text not null check (access_level in ('admin','advisory')),
  permissions jsonb not null default '{}'::jsonb,
  sort_order integer not null default 100,
  created_at timestamptz not null default now()
);

alter table public.zurvan_special_ranks enable row level security;
revoke all on public.zurvan_special_ranks from public, anon, authenticated;
grant select on public.zurvan_special_ranks to service_role;

insert into public.zurvan_special_ranks
  (code, display_name, display_name_fa, description, badge_key, access_level, permissions, sort_order)
values
  ('emperor', 'Emperor', 'امپراتور', 'Founder of Zurvan with full administrative authority.', 'crown-gold', 'admin',
   '{"admin":true,"manage_accounts":true,"manage_ranks":true,"manage_licenses":true,"manage_settings":true}'::jsonb, 1),
  ('empress', 'Empress', 'امپراتریس', 'Special executive administrator of Zurvan.', 'crown-rose', 'admin',
   '{"admin":true,"manage_accounts":true,"manage_ranks":false,"manage_licenses":true,"manage_settings":true}'::jsonb, 2),
  ('special_advisor', 'Special Advisor', 'مشاور ویژه', 'Honorary advisory rank; does not bypass license entitlement.', 'star-teal', 'advisory',
   '{"admin":false,"advisory":true,"manage_accounts":false,"manage_licenses":false}'::jsonb, 3)
on conflict (code) do update set
  display_name = excluded.display_name,
  display_name_fa = excluded.display_name_fa,
  description = excluded.description,
  badge_key = excluded.badge_key,
  access_level = excluded.access_level,
  permissions = excluded.permissions,
  sort_order = excluded.sort_order;

do $$
declare
  creator_id uuid;
begin
  select u.id into creator_id
  from auth.users u
  join public.accounts a on a.auth_user_id = u.id
  where lower(u.email) = lower('behzaddesec@gmail.com')
    and u.email_confirmed_at is not null
    and a.state = 'active'
  limit 1;

  if creator_id is null then
    raise exception 'Verified active Zurvan creator account behzaddesec@gmail.com was not found; Emperor rank was not assigned.';
  end if;

  insert into public.zurvan_accounts
    (id, status, recovery_email, recovery_verified_at, super_type, super_title)
  select creator_id, 'active', u.email, coalesce(u.email_confirmed_at, now()),
         'emperor', 'Founder / Emperor'
  from auth.users u
  where u.id = creator_id
  on conflict (id) do update set
    status = 'active',
    super_type = 'emperor',
    super_title = 'Founder / Emperor';
end;
$$;

commit;
