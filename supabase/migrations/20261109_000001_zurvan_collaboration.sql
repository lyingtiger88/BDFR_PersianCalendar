-- Zurvan Desk collaboration MVP — isolated from the license and Desk Vault schemas.
-- Identity is public.zurvan_desk_accounts, NOT auth.users or unverified local IDs.
-- Access is exclusively via the authenticated Edge Function using service_role.
-- New tables do not have anon/authenticated grants even when default grants exist.
begin;
create extension if not exists pgcrypto;

create table if not exists public.collab_groups (
 id uuid primary key default gen_random_uuid(),
 owner_id uuid not null references public.zurvan_desk_accounts(id),
 name text not null check (char_length(trim(name)) between 1 and 120),
 description text not null default '',
 agenda_enabled boolean not null default true,
 kanban_enabled boolean not null default false,
 school_enabled boolean not null default false,
 created_at timestamptz not null default now(),
 check (agenda_enabled or kanban_enabled or school_enabled)
);

create table if not exists public.collab_members (
 group_id uuid not null references public.collab_groups(id) on delete cascade,
 account_id uuid not null references public.zurvan_desk_accounts(id),
 role text not null check (role in ('owner','assistant','member')),
 can_chat boolean not null default true,
 can_report boolean not null default true,
 can_edit boolean not null default false,
 joined_at timestamptz not null default now(),
 primary key (group_id,account_id)
);
create index if not exists collab_members_account on public.collab_members(account_id);

create table if not exists public.collab_invites (
 id uuid primary key default gen_random_uuid(),
 group_id uuid not null references public.collab_groups(id) on delete cascade,
 invited_by uuid not null references public.zurvan_desk_accounts(id),
 recipient_id uuid not null references public.zurvan_desk_accounts(id),
 role text not null check(role in ('assistant','member')),
 status text not null default 'pending' check(status in ('pending','accepted','revoked')),
 expires_at timestamptz not null default (now() + interval '7 days'),
 created_at timestamptz not null default now()
);
create unique index if not exists collab_invite_open
 on public.collab_invites(group_id,recipient_id) where status='pending';

create table if not exists public.collab_items (
 id uuid primary key default gen_random_uuid(),
 group_id uuid not null references public.collab_groups(id) on delete cascade,
 created_by uuid not null references public.zurvan_desk_accounts(id),
 updated_by uuid references public.zurvan_desk_accounts(id),
 title text not null check(char_length(trim(title)) between 1 and 240),
 details text not null default '',
 mode text not null check(mode in ('agenda','kanban','school')),
 state text not null default 'todo',
 assigned_to uuid references public.zurvan_desk_accounts(id),
 starts_at timestamptz,
 due_at timestamptz,
 version integer not null default 1 check(version>0),
 created_at timestamptz not null default now(),
 updated_at timestamptz not null default now()
);
create index if not exists collab_items_group_due on public.collab_items(group_id,due_at);

create table if not exists public.collab_messages (
 id uuid primary key default gen_random_uuid(),
 group_id uuid not null references public.collab_groups(id) on delete cascade,
 sender_id uuid not null references public.zurvan_desk_accounts(id),
 body text not null check(char_length(trim(body)) between 1 and 4000),
 created_at timestamptz not null default now()
);
create index if not exists collab_messages_group_created on public.collab_messages(group_id,created_at desc);

create table if not exists public.collab_reports (
 id uuid primary key default gen_random_uuid(),
 group_id uuid not null references public.collab_groups(id) on delete cascade,
 author_id uuid not null references public.zurvan_desk_accounts(id),
 title text not null check(char_length(trim(title)) between 1 and 160),
 body text not null check(char_length(trim(body)) between 1 and 12000),
 status text not null default 'submitted',
 created_at timestamptz not null default now()
);

-- Dedicated classroom entities, only within a School-enabled group.
create table if not exists public.collab_subjects (
 id uuid primary key default gen_random_uuid(),
 group_id uuid not null references public.collab_groups(id) on delete cascade,
 name text not null check(char_length(trim(name)) between 1 and 120),
 created_at timestamptz not null default now()
);
create table if not exists public.collab_timetable (
 id uuid primary key default gen_random_uuid(),
 group_id uuid not null references public.collab_groups(id) on delete cascade,
 subject_id uuid not null references public.collab_subjects(id) on delete cascade,
 weekday integer not null check (weekday between 0 and 6),
 start_time time not null,
 end_time time not null,
 room text,
 teacher_id uuid references public.zurvan_desk_accounts(id),
 check(end_time>start_time)
);
create table if not exists public.collab_homework (
 id uuid primary key default gen_random_uuid(),
 group_id uuid not null references public.collab_groups(id) on delete cascade,
 subject_id uuid not null references public.collab_subjects(id) on delete cascade,
 created_by uuid not null references public.zurvan_desk_accounts(id),
 title text not null check(char_length(trim(title)) between 1 and 240),
 instructions text not null default '',
 due_at timestamptz,
 created_at timestamptz not null default now()
);
create table if not exists public.collab_homework_submissions (
 id uuid primary key default gen_random_uuid(),
 homework_id uuid not null references public.collab_homework(id) on delete cascade,
 student_id uuid not null references public.zurvan_desk_accounts(id),
 body text not null default '',
 grade numeric(5,2),
 feedback text,
 submitted_at timestamptz not null default now(),
 unique(homework_id,student_id)
);

create table if not exists public.collab_events (
 seq bigint generated always as identity primary key,
 group_id uuid not null references public.collab_groups(id) on delete cascade,
 kind text not null,
 resource_id uuid,
 actor_id uuid,
 created_at timestamptz not null default now()
);
create index if not exists collab_events_group_seq on public.collab_events(group_id,seq);

create or replace function public.collab_group_owner_insert()
returns trigger language plpgsql security definer set search_path = ''
as $$
begin
 insert into public.collab_members(group_id,account_id,role,can_chat,can_report,can_edit)
 values(new.id,new.owner_id,'owner',true,true,true);
 return new;
end; $$;
drop trigger if exists collab_owner_seed on public.collab_groups;
create trigger collab_owner_seed after insert on public.collab_groups
for each row execute function public.collab_group_owner_insert();

create or replace function public.collab_guard_group_owner()
returns trigger language plpgsql set search_path = ''
as $$
begin
 if new.owner_id <> old.owner_id then
  raise exception 'Changing owner is not supported' using errcode='42501';
 end if;
 return new;
end; $$;
drop trigger if exists collab_owner_guard on public.collab_groups;
create trigger collab_owner_guard before update on public.collab_groups
for each row execute function public.collab_guard_group_owner();

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
  raise exception 'Workspace mode not enabled' using errcode='22023';
 end if;
 return new;
end; $$;
drop trigger if exists collab_mode_guard on public.collab_items;
create trigger collab_mode_guard before insert or update on public.collab_items
for each row execute function public.collab_guard_item_mode();

-- Atomic optimistic-concurrency updates, called only with a *verified* Desk account.
create or replace function public.collab_update_work_item(
 p_actor uuid, p_group uuid, p_item uuid, p_expected_version int,
 p_title text, p_details text, p_state text, p_due_at timestamptz,
 p_assigned_to uuid default null)
returns public.collab_items language plpgsql security definer set search_path = ''
as $$
declare old_item public.collab_items%rowtype; new_item public.collab_items;
begin
 if p_actor is null or not exists (
  select 1 from public.collab_members m
  where m.group_id=p_group and m.account_id=p_actor and
   (m.can_edit or m.role in ('owner','assistant'))) then
  raise exception 'Edit denied' using errcode='42501';
 end if;
 select * into old_item from public.collab_items
  where id=p_item and group_id=p_group for update;
 if not found then raise exception 'Item not found' using errcode='P0002'; end if;
 if old_item.version<>p_expected_version then
  raise exception 'Version conflict' using errcode='40001'; end if;
 if char_length(trim(coalesce(p_title,''))) not between 1 and 240 then
  raise exception 'Invalid title' using errcode='22023'; end if;
 if p_assigned_to is not null and not exists(select 1 from public.collab_members
  where group_id=p_group and account_id=p_assigned_to) then
  raise exception 'Assignee not a member' using errcode='22023'; end if;
 update public.collab_items set title=trim(p_title),details=left(coalesce(p_details,''),12000),
  state=left(coalesce(p_state,'todo'),60),due_at=p_due_at,assigned_to=p_assigned_to,
  version=version+1,updated_by=p_actor,updated_at=now()
  where id=p_item returning * into new_item;
 return new_item;
end; $$;

-- Minimal metadata change log; no chat body or sensitive report content.
create or replace function public.collab_record_change()
returns trigger language plpgsql security definer set search_path = ''
as $$
declare ref_group uuid; ref_id uuid; actor uuid; kind text;
begin
 if tg_op='DELETE' then
  ref_group:=old.group_id;
 else ref_group:=new.group_id; end if;
 if tg_table_name='collab_items' then
  ref_id:=new.id; actor:=coalesce(new.updated_by,new.created_by);kind:='item';
 elsif tg_table_name='collab_messages' then
  ref_id:=new.id; actor:=new.sender_id;kind:='message';
 elsif tg_table_name='collab_reports' then
  ref_id:=new.id; actor:=new.author_id;kind:='report';
 elsif tg_table_name='collab_members' then
  if tg_op='DELETE' then ref_id:=old.account_id; else ref_id:=new.account_id;end if;
  kind:='member';actor:=null;
 else
  ref_id:=new.id;kind:='school';actor:=null;
 end if;
 insert into public.collab_events(group_id,kind,resource_id,actor_id)
 values(ref_group,kind,ref_id,actor);
 if tg_op='DELETE' then return old;end if;
 return new;
end; $$;

drop trigger if exists collab_items_change on public.collab_items;
create trigger collab_items_change after insert or update on public.collab_items
for each row execute function public.collab_record_change();
drop trigger if exists collab_messages_change on public.collab_messages;
create trigger collab_messages_change after insert on public.collab_messages
for each row execute function public.collab_record_change();
drop trigger if exists collab_reports_change on public.collab_reports;
create trigger collab_reports_change after insert on public.collab_reports
for each row execute function public.collab_record_change();
drop trigger if exists collab_members_change on public.collab_members;
create trigger collab_members_change after insert or update or delete on public.collab_members
for each row execute function public.collab_record_change();
drop trigger if exists collab_subjects_change on public.collab_subjects;
create trigger collab_subjects_change after insert or update on public.collab_subjects
for each row execute function public.collab_record_change();
drop trigger if exists collab_timetable_change on public.collab_timetable;
create trigger collab_timetable_change after insert or update on public.collab_timetable
for each row execute function public.collab_record_change();
drop trigger if exists collab_homework_change on public.collab_homework;
create trigger collab_homework_change after insert or update on public.collab_homework
for each row execute function public.collab_record_change();

-- No direct client access: every collaboration request passes Desk session verification
-- and membership/permission checks in the Edge API. Service-role access only.
alter table public.collab_groups enable row level security;
alter table public.collab_members enable row level security;
alter table public.collab_invites enable row level security;
alter table public.collab_items enable row level security;
alter table public.collab_messages enable row level security;
alter table public.collab_reports enable row level security;
alter table public.collab_subjects enable row level security;
alter table public.collab_timetable enable row level security;
alter table public.collab_homework enable row level security;
alter table public.collab_homework_submissions enable row level security;
alter table public.collab_events enable row level security;
revoke all on public.collab_groups,public.collab_members,public.collab_invites,
 public.collab_items,public.collab_messages,public.collab_reports,
 public.collab_subjects,public.collab_timetable,public.collab_homework,
 public.collab_homework_submissions,public.collab_events from anon,authenticated;
revoke all on sequence public.collab_events_seq_seq from anon,authenticated;
revoke all on function public.collab_update_work_item(uuid,uuid,uuid,int,text,text,text,timestamptz,uuid)
 from public,anon,authenticated;
grant execute on function public.collab_update_work_item(uuid,uuid,uuid,int,text,text,text,timestamptz,uuid)
 to service_role;
revoke all on function public.collab_group_owner_insert() from public,anon,authenticated;
revoke all on function public.collab_guard_item_mode() from public,anon,authenticated;
revoke all on function public.collab_record_change() from public,anon,authenticated;
create index if not exists collab_groups_owner_idx on public.collab_groups(owner_id);
create index if not exists collab_invites_inviter_idx on public.collab_invites(invited_by);
create index if not exists collab_invites_recipient_idx on public.collab_invites(recipient_id);
create index if not exists collab_items_created_by_idx on public.collab_items(created_by);
create index if not exists collab_items_updated_by_idx on public.collab_items(updated_by);
create index if not exists collab_items_assignee_idx on public.collab_items(assigned_to);
create index if not exists collab_messages_sender_idx on public.collab_messages(sender_id);
create index if not exists collab_reports_author_idx on public.collab_reports(author_id);
create index if not exists collab_reports_group_idx on public.collab_reports(group_id);
create index if not exists collab_subjects_group_idx on public.collab_subjects(group_id);
create index if not exists collab_timetable_group_idx on public.collab_timetable(group_id);
create index if not exists collab_timetable_subject_idx on public.collab_timetable(subject_id);
create index if not exists collab_timetable_teacher_idx on public.collab_timetable(teacher_id);
create index if not exists collab_homework_group_idx on public.collab_homework(group_id);
create index if not exists collab_homework_subject_idx on public.collab_homework(subject_id);
create index if not exists collab_homework_created_by_idx on public.collab_homework(created_by);
create index if not exists collab_homework_submissions_student_idx on public.collab_homework_submissions(student_id);

-- Realtime Postgres Changes is intentionally not exposed to clients: Desk sessions
-- are not Supabase Auth JWTs. Edge authenticated change-feed / streaming is required.
commit;
