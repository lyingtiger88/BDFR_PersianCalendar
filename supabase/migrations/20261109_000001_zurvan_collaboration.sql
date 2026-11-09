-- Zurvan collaboration foundation for the existing Supabase project.
-- Deploy manually with reviewed credentials. DO NOT paste a service-role key into clients.
-- Uses Supabase Auth identities (auth.uid), NOT unauthenticated local profile IDs.
begin;

create extension if not exists pgcrypto;

create table if not exists public.collab_groups (
  id uuid primary key default gen_random_uuid(),
  owner_id uuid not null references auth.users(id),
  name text not null check (char_length(trim(name)) between 1 and 120),
  description text not null default '',
  avatar_path text,
  agenda_enabled boolean not null default true,
  kanban_enabled boolean not null default false,
  school_enabled boolean not null default false,
  created_at timestamptz not null default now(),
  check (agenda_enabled or kanban_enabled or school_enabled)
);

create table if not exists public.collab_members (
  group_id uuid not null references public.collab_groups(id) on delete cascade,
  user_id uuid not null references auth.users(id) on delete cascade,
  role text not null check (role in ('owner','assistant','member')),
  can_chat boolean not null default true,
  can_report boolean not null default true,
  can_edit boolean not null default false,
  joined_at timestamptz not null default now(),
  primary key (group_id,user_id)
);

create table if not exists public.collab_invitations (
  id uuid primary key default gen_random_uuid(),
  group_id uuid not null references public.collab_groups(id) on delete cascade,
  invited_by uuid not null references auth.users(id),
  invited_user_id uuid not null references auth.users(id),
  role text not null check (role in ('assistant','member')),
  status text not null default 'pending' check (status in ('pending','accepted','revoked')),
  created_at timestamptz not null default now(),
  expires_at timestamptz not null default (now() + interval '7 days')
);

create unique index if not exists collab_one_pending_invite
  on public.collab_invitations(group_id,invited_user_id) where status='pending';

create table if not exists public.collab_items (
  id uuid primary key default gen_random_uuid(),
  group_id uuid not null references public.collab_groups(id) on delete cascade,
  created_by uuid not null references auth.users(id),
  title text not null check (char_length(trim(title)) between 1 and 240),
  body text not null default '',
  mode text not null check (mode in ('agenda','kanban','school')),
  lane text not null default 'todo',
  assigned_to uuid references auth.users(id),
  start_at timestamptz,
  due_at timestamptz,
  version integer not null default 1 check (version > 0),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists collab_items_group_index on public.collab_items(group_id,due_at);

create table if not exists public.collab_messages (
  id uuid primary key default gen_random_uuid(),
  group_id uuid not null references public.collab_groups(id) on delete cascade,
  sender_id uuid not null references auth.users(id),
  body text not null check (char_length(trim(body)) between 1 and 4000),
  created_at timestamptz not null default now()
);
create index if not exists collab_messages_group_index on public.collab_messages(group_id,created_at desc);

create table if not exists public.collab_reports (
  id uuid primary key default gen_random_uuid(),
  group_id uuid not null references public.collab_groups(id) on delete cascade,
  author_id uuid not null references auth.users(id),
  title text not null check (char_length(trim(title)) between 1 and 160),
  body text not null check (char_length(trim(body)) between 1 and 12000),
  status text not null default 'submitted',
  created_at timestamptz not null default now()
);

-- Metadata-only event feed. No chat bodies, reports or student records here.
create table if not exists public.collab_events (
  seq bigint generated always as identity primary key,
  group_id uuid not null references public.collab_groups(id) on delete cascade,
  kind text not null,
  resource_id uuid,
  actor_id uuid,
  created_at timestamptz not null default now()
);
create index if not exists collab_events_group_seq on public.collab_events(group_id,seq);

-- SECURITY DEFINER helpers avoid infinite RLS recursion when reading memberships.
create or replace function public.collab_is_member(g uuid)
returns boolean language sql stable security definer set search_path = ''
as $$
  select exists(select 1 from public.collab_members
  where group_id=g and user_id=auth.uid());
$$;

create or replace function public.collab_role(g uuid)
returns text language sql stable security definer set search_path = ''
as $$
  select role from public.collab_members
  where group_id=g and user_id=auth.uid() limit 1;
$$;

create or replace function public.collab_allowed(g uuid,p text)
returns boolean language sql stable security definer set search_path = ''
as $$
  select exists(select 1 from public.collab_members
  where group_id=g and user_id=auth.uid()
    and case p
      when 'chat' then can_chat
      when 'report' then can_report
      when 'edit' then can_edit or role in ('owner','assistant')
      else false end);
$$;

create or replace function public.collab_owner_after_group_insert()
returns trigger language plpgsql security definer set search_path = ''
as $$
begin
  insert into public.collab_members(group_id,user_id,role,can_chat,can_report,can_edit)
  values(new.id,new.owner_id,'owner',true,true,true);
  return new;
end;
$$;
drop trigger if exists collab_group_owner_insert on public.collab_groups;
create trigger collab_group_owner_insert after insert on public.collab_groups
for each row execute function public.collab_owner_after_group_insert();

-- Group ownership must remain stable. Assistants can edit display fields only.
create or replace function public.collab_protect_group_owner()
returns trigger language plpgsql set search_path = ''
as $
begin
  if new.owner_id <> old.owner_id then
    raise exception 'Group ownership cannot be reassigned here' using errcode='42501';
  end if;
  return new;
end;
$;
drop trigger if exists collab_group_owner_guard on public.collab_groups;
create trigger collab_group_owner_guard before update on public.collab_groups
for each row execute function public.collab_protect_group_owner();

-- Invitation must target an authenticated Supabase user ID.
-- Converting a public Zurvan display identifier to this UUID requires a verified identity mapping.
create or replace function public.collab_invite_member(g uuid, target uuid, requested_role text default 'member')
returns uuid language plpgsql security definer set search_path = ''
as $$
declare current_role text; invitation_id uuid;
begin
  if auth.uid() is null then raise exception 'Authentication required' using errcode='42501'; end if;
  current_role := public.collab_role(g);
  if coalesce(current_role,'') not in ('owner','assistant') then raise exception 'Not authorized' using errcode='42501'; end if;
  if requested_role not in ('member','assistant') or
     (requested_role='assistant' and current_role<>'owner')
  then raise exception 'Role not authorized' using errcode='42501'; end if;
  if target=auth.uid() or not exists(select 1 from auth.users where id=target)
    then raise exception 'Invalid recipient' using errcode='22023'; end if;
  if exists(select 1 from public.collab_members where group_id=g and user_id=target)
    then raise exception 'Already a member'; end if;
  -- Pending expired invitations are revoked before granting a new one.
  update public.collab_invitations set status='revoked'
  where group_id=g and invited_user_id=target and status='pending' and expires_at<=now();
  insert into public.collab_invitations(group_id,invited_by,invited_user_id,role)
  values(g,auth.uid(),target,requested_role) returning id into invitation_id;
  return invitation_id;
end;
$$;

create or replace function public.collab_accept_invitation(inv uuid)
returns uuid language plpgsql security definer set search_path = ''
as $$
declare i public.collab_invitations%rowtype; issuer_role text;
begin
  if auth.uid() is null then raise exception 'Authentication required' using errcode='42501'; end if;
  select * into i from public.collab_invitations where id=inv for update;
  if not found or i.invited_user_id<>auth.uid() or
     i.status<>'pending' or i.expires_at<=now() then
    raise exception 'Invitation not valid' using errcode='42501';
  end if;
  select role into issuer_role from public.collab_members
    where group_id=i.group_id and user_id=i.invited_by;
  if coalesce(issuer_role,'') not in ('owner','assistant') or
     (i.role='assistant' and issuer_role<>'owner') then
    raise exception 'Inviter no longer authorized' using errcode='42501';
  end if;
  insert into public.collab_members(group_id,user_id,role,can_chat,can_report,can_edit)
  values(i.group_id,auth.uid(),i.role,true,true,false);
  update public.collab_invitations set status='accepted' where id=inv;
  return i.group_id;
end;
$$;

create or replace function public.collab_set_member_permissions(
  g uuid, target uuid, chat boolean, report boolean, edit boolean)
returns void language plpgsql security definer set search_path = ''
as $$
declare manager_role text; target_role text;
begin
  if auth.uid() is null then raise exception 'Authentication required' using errcode='42501'; end if;
  manager_role := public.collab_role(g);
  select role into target_role from public.collab_members
  where group_id=g and user_id=target for update;
  if coalesce(manager_role,'') not in ('owner','assistant')
    or target_role is null or target_role='owner'
    or (target_role='assistant' and manager_role<>'owner')
  then raise exception 'Permission change not authorized' using errcode='42501'; end if;
  update public.collab_members
    set can_chat=chat, can_report=report, can_edit=edit
    where group_id=g and user_id=target;
end;
$$;

create or replace function public.collab_remove_member(g uuid, target uuid)
returns void language plpgsql security definer set search_path = ''
as $
declare manager_role text; target_role text;
begin
  if auth.uid() is null then raise exception 'Authentication required' using errcode='42501'; end if;
  manager_role := public.collab_role(g);
  select role into target_role from public.collab_members
  where group_id=g and user_id=target for update;
  if target_role is null or target_role='owner' or
     (auth.uid()<>target and (coalesce(manager_role,'') not in ('owner','assistant')
      or (target_role='assistant' and manager_role<>'owner')))
  then raise exception 'Member removal not authorized' using errcode='42501'; end if;
  delete from public.collab_members where group_id=g and user_id=target;
end;
$$;

-- Optimistic concurrency for card movement and task updates.
create or replace function public.collab_update_item(
  item_id uuid, expected_version integer, new_title text, new_lane text,
  new_due_at timestamptz default null)
returns public.collab_items language plpgsql security definer set search_path = ''
as $$
declare original public.collab_items%rowtype; updated public.collab_items;
begin
  if auth.uid() is null then raise exception 'Authentication required' using errcode='42501'; end if;
  select * into original from public.collab_items where id=item_id for update;
  if not found then raise exception 'Item missing'; end if;
  if not public.collab_allowed(original.group_id,'edit')
    then raise exception 'Not authorized' using errcode='42501'; end if;
  if original.version<>expected_version then
    raise exception 'Version conflict' using errcode='40001'; end if;
  if char_length(trim(new_title)) not between 1 and 240
    then raise exception 'Invalid title' using errcode='22023'; end if;
  update public.collab_items
    set title=trim(new_title), lane=left(new_lane,60),
        due_at=new_due_at, version=version+1, updated_at=now()
    where id=item_id returning * into updated;
  return updated;
end;
$$;

create or replace function public.collab_guard_item_mode()
returns trigger language plpgsql security definer set search_path = ''
as $$
declare g public.collab_groups%rowtype;
begin
  select * into g from public.collab_groups where id=new.group_id;
  if not found or
    (new.mode='agenda' and not g.agenda_enabled) or
    (new.mode='kanban' and not g.kanban_enabled) or
    (new.mode='school' and not g.school_enabled) then
    raise exception 'Workspace mode disabled' using errcode='22023';
  end if;
  return new;
end;
$$;
drop trigger if exists collab_item_mode_guard on public.collab_items;
create trigger collab_item_mode_guard before insert or update on public.collab_items
for each row execute function public.collab_guard_item_mode();

-- Events are inserted by trusted database triggers, not client requests.
create or replace function public.collab_log_change()
returns trigger language plpgsql security definer set search_path = ''
as $$
declare group_ref uuid; row_id uuid; kind text;
begin
  if tg_table_name='collab_items' then
    group_ref := new.group_id; row_id := new.id; kind:='item';
  elsif tg_table_name='collab_messages' then
    group_ref := new.group_id; row_id := new.id; kind:='message';
  elsif tg_table_name='collab_reports' then
    group_ref := new.group_id; row_id := new.id; kind:='report';
  else
    group_ref := coalesce(new.group_id,old.group_id);
    row_id := coalesce(new.user_id,old.user_id); kind:='membership';
  end if;
  insert into public.collab_events(group_id,kind,resource_id,actor_id)
  values(group_ref,kind,row_id,auth.uid());
  return coalesce(new,old);
end;
$$;
drop trigger if exists collab_item_event on public.collab_items;
create trigger collab_item_event after insert or update on public.collab_items
for each row execute function public.collab_log_change();
drop trigger if exists collab_message_event on public.collab_messages;
create trigger collab_message_event after insert on public.collab_messages
for each row execute function public.collab_log_change();
drop trigger if exists collab_report_event on public.collab_reports;
create trigger collab_report_event after insert on public.collab_reports
for each row execute function public.collab_log_change();
drop trigger if exists collab_membership_event on public.collab_members;
create trigger collab_membership_event after insert or update or delete on public.collab_members
for each row execute function public.collab_log_change();

alter table public.collab_groups enable row level security;
alter table public.collab_members enable row level security;
alter table public.collab_invitations enable row level security;
alter table public.collab_items enable row level security;
alter table public.collab_messages enable row level security;
alter table public.collab_reports enable row level security;
alter table public.collab_events enable row level security;

drop policy if exists collab_group_read on public.collab_groups;
create policy collab_group_read on public.collab_groups for select to authenticated
using (public.collab_is_member(id));
drop policy if exists collab_group_create on public.collab_groups;
create policy collab_group_create on public.collab_groups for insert to authenticated
with check (owner_id=auth.uid());
drop policy if exists collab_group_update on public.collab_groups;
create policy collab_group_update on public.collab_groups for update to authenticated
using (public.collab_role(id) in ('owner','assistant'))
with check (public.collab_role(id) in ('owner','assistant'));

drop policy if exists collab_member_read on public.collab_members;
create policy collab_member_read on public.collab_members for select to authenticated
using (public.collab_is_member(group_id));
drop policy if exists collab_invitation_read on public.collab_invitations;
create policy collab_invitation_read on public.collab_invitations for select to authenticated
using (invited_user_id=auth.uid() or public.collab_role(group_id) in ('owner','assistant'));

drop policy if exists collab_item_read on public.collab_items;
create policy collab_item_read on public.collab_items for select to authenticated
using (public.collab_is_member(group_id));
drop policy if exists collab_item_insert on public.collab_items;
create policy collab_item_insert on public.collab_items for insert to authenticated
with check (created_by=auth.uid() and version=1 and public.collab_allowed(group_id,'edit')
  and (assigned_to is null or exists(select 1 from public.collab_members
    where group_id=collab_items.group_id and user_id=assigned_to)));

drop policy if exists collab_message_read on public.collab_messages;
create policy collab_message_read on public.collab_messages for select to authenticated
using (public.collab_is_member(group_id));
drop policy if exists collab_message_insert on public.collab_messages;
create policy collab_message_insert on public.collab_messages for insert to authenticated
with check (sender_id=auth.uid() and public.collab_allowed(group_id,'chat'));

drop policy if exists collab_report_read on public.collab_reports;
create policy collab_report_read on public.collab_reports for select to authenticated
using (public.collab_is_member(group_id) and
  (public.collab_role(group_id) in ('owner','assistant') or author_id=auth.uid()));
drop policy if exists collab_report_insert on public.collab_reports;
create policy collab_report_insert on public.collab_reports for insert to authenticated
with check (author_id=auth.uid() and status='submitted'
  and public.collab_allowed(group_id,'report'));

drop policy if exists collab_event_read on public.collab_events;
create policy collab_event_read on public.collab_events for select to authenticated
using (public.collab_is_member(group_id) and (
 kind <> 'report' or public.collab_role(group_id) in ('owner','assistant')
 or exists(select 1 from public.collab_reports r
    where r.id=resource_id and r.author_id=auth.uid())));

-- Restrict direct mutation paths; security-definer RPCs validate sensitive operations.
revoke all on public.collab_groups,public.collab_members,public.collab_invitations,
  public.collab_items,public.collab_messages,public.collab_reports,
  public.collab_events from anon,authenticated;

grant select,insert,update on public.collab_groups to authenticated;
grant select on public.collab_members,public.collab_invitations to authenticated;
grant select,insert on public.collab_items,public.collab_messages,public.collab_reports to authenticated;
grant select on public.collab_events to authenticated;
grant usage,select on sequence public.collab_events_seq_seq to authenticated;

revoke all on function public.collab_is_member(uuid),
  public.collab_role(uuid), public.collab_allowed(uuid,text),
  public.collab_invite_member(uuid,uuid,text),
  public.collab_accept_invitation(uuid),
  public.collab_set_member_permissions(uuid,uuid,boolean,boolean,boolean),
  public.collab_remove_member(uuid,uuid),
  public.collab_update_item(uuid,integer,text,text,timestamptz)
  from public,anon;
grant execute on function public.collab_is_member(uuid), public.collab_role(uuid),
  public.collab_allowed(uuid,text), public.collab_invite_member(uuid,uuid,text),
  public.collab_accept_invitation(uuid),
  public.collab_set_member_permissions(uuid,uuid,boolean,boolean,boolean),
  public.collab_remove_member(uuid,uuid),
  public.collab_update_item(uuid,integer,text,text,timestamptz)
  to authenticated;

-- Supabase Realtime Postgres Changes: subscribe to collab_events only.
do $$
begin
  if exists(select 1 from pg_publication where pubname='supabase_realtime')
    and not exists(select 1 from pg_publication_tables
      where pubname='supabase_realtime' and schemaname='public' and tablename='collab_events')
  then
    alter publication supabase_realtime add table public.collab_events;
  end if;
end $$;

commit;
