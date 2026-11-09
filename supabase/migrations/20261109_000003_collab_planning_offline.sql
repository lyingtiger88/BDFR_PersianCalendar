-- Complete collaboration planning: ordered Kanban columns, recurring Agenda metadata,
-- and durable idempotency receipts for offline command replay.
begin;
create table if not exists public.collab_kanban_columns (
 id uuid primary key default gen_random_uuid(),
 group_id uuid not null references public.collab_groups(id) on delete cascade,
 name text not null check(char_length(trim(name)) between 1 and 80),
 position integer not null check(position>=0),
 color text not null default 'slate' check(color in ('slate','blue','green','amber','rose','violet')),
 created_at timestamptz not null default now(),
 unique(group_id,position),
 unique(group_id,name)
);
alter table public.collab_items add column if not exists column_id uuid references public.collab_kanban_columns(id) on delete set null;
alter table public.collab_items add column if not exists recurrence_rule text;
alter table public.collab_items add column if not exists recurrence_until timestamptz;
alter table public.collab_items add column if not exists timezone text not null default 'Asia/Tehran';
alter table public.collab_items add constraint collab_items_recurrence_rule_check
 check(recurrence_rule is null or recurrence_rule in ('daily','weekly','monthly','weekdays')) not valid;
alter table public.collab_items validate constraint collab_items_recurrence_rule_check;
create index if not exists collab_items_column_position on public.collab_items(group_id,column_id,created_at);
create index if not exists collab_items_recurrence on public.collab_items(group_id,due_at) where recurrence_rule is not null;
create table if not exists public.collab_command_receipts (
 account_id uuid not null references public.zurvan_desk_accounts(id) on delete cascade,
 command_id uuid not null,
 group_id uuid not null references public.collab_groups(id) on delete cascade,
 response jsonb not null,
 created_at timestamptz not null default now(),
 primary key(account_id,command_id)
);
create index if not exists collab_command_receipts_created on public.collab_command_receipts(created_at);
alter table public.collab_kanban_columns enable row level security;
alter table public.collab_command_receipts enable row level security;
revoke all on public.collab_kanban_columns,public.collab_command_receipts from anon,authenticated;
drop trigger if exists collab_kanban_columns_change on public.collab_kanban_columns;
create trigger collab_kanban_columns_change after insert or update or delete
on public.collab_kanban_columns for each row execute function public.collab_record_change();
revoke all on function public.collab_record_change() from public,anon,authenticated;
commit;
