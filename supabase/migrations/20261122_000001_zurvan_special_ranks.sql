begin;

-- Special Zurvan ranks are separate from the ordinary XP/level system.
create table if not exists public.zurvan_special_ranks (
  code text primary key,
  display_name text not null,
  display_name_fa text not null,
  description text not null,
  badge_key text not null unique,
  access_level text not null check (access_level in ('owner', 'admin', 'advisor')),
  permissions jsonb not null default '{}'::jsonb,
  sort_order integer not null,
  is_assignable boolean not null default true,
  created_at timestamptz not null default now(),
  constraint zurvan_special_ranks_code_check
    check (code in ('emperor', 'empress', 'special_advisor'))
);

insert into public.zurvan_special_ranks
  (code, display_name, display_name_fa, description, badge_key, access_level, permissions, sort_order, is_assignable)
values
  (
    'emperor', 'Emperor', 'امپراتور',
    'Founder and ultimate owner of Zurvan.',
    'crown-emperor', 'owner',
    '{"can_write":true,"can_manage_users":true,"can_manage_roles":true,"can_manage_licenses":true,"can_manage_billing":true,"can_manage_security":true,"can_view_advisory_workspace":true}'::jsonb,
    1, false
  ),
  (
    'empress', 'Empress', 'امپراتریس',
    'Special senior administrator. Cannot transfer ownership or manage the Emperor account.',
    'crown-empress', 'admin',
    '{"can_write":true,"can_manage_users":true,"can_manage_roles":false,"can_manage_licenses":false,"can_manage_billing":false,"can_manage_security":false,"can_view_advisory_workspace":true}'::jsonb,
    2, true
  ),
  (
    'special_advisor', 'Special Advisor', 'مشاور ویژه',
    'Special advisory appointment without owner or administrator privileges.',
    'shield-advisor', 'advisor',
    '{"can_write":false,"can_manage_users":false,"can_manage_roles":false,"can_manage_licenses":false,"can_manage_billing":false,"can_manage_security":false,"can_view_advisory_workspace":true}'::jsonb,
    3, true
  )
on conflict (code) do update set
  display_name = excluded.display_name,
  display_name_fa = excluded.display_name_fa,
  description = excluded.description,
  badge_key = excluded.badge_key,
  access_level = excluded.access_level,
  permissions = excluded.permissions,
  sort_order = excluded.sort_order,
  is_assignable = excluded.is_assignable;

alter table public.zurvan_special_ranks enable row level security;
revoke all on public.zurvan_special_ranks from public, anon;
grant select on public.zurvan_special_ranks to authenticated, service_role;

drop policy if exists zurvan_special_ranks_read_authenticated
  on public.zurvan_special_ranks;
create policy zurvan_special_ranks_read_authenticated
  on public.zurvan_special_ranks
  for select to authenticated
  using (true);

-- Convert only the explicitly identified, already-active owner account.
-- No other account or license record is changed.
update public.zurvan_accounts
set super_type = 'emperor',
    super_title = 'Emperor'
where id = '5fcfb06c-7344-4c1f-9063-589daf145038'::uuid
  and status = 'active'
  and super_type in ('creator', 'emperor');

do $$
begin
  if not exists (
    select 1
    from public.zurvan_accounts
    where id = '5fcfb06c-7344-4c1f-9063-589daf145038'::uuid
      and status = 'active'
      and super_type = 'emperor'
      and super_title = 'Emperor'
  ) then
    raise exception 'Owner account was not found as an active creator/emperor; migration aborted.';
  end if;
end
$$;

commit;
