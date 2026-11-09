-- Finalize Kanban ordering, default columns and versioned planning-item edits.
begin;
create or replace function public.collab_seed_kanban_columns()
returns trigger language plpgsql security definer set search_path=''
as $$
begin
 if new.kanban_enabled then
  insert into public.collab_kanban_columns(group_id,name,position,color)
  values(new.id,'برای انجام',0,'slate'),(new.id,'در حال انجام',1,'blue'),(new.id,'انجام‌شده',2,'green')
  on conflict do nothing;
 end if;
 return new;
end; $$;
drop trigger if exists collab_seed_kanban on public.collab_groups;
create trigger collab_seed_kanban after insert on public.collab_groups
for each row execute function public.collab_seed_kanban_columns();
insert into public.collab_kanban_columns(group_id,name,position,color)
select g.id,v.name,v.position,v.color
from public.collab_groups g
cross join (values ('برای انجام',0,'slate'),('در حال انجام',1,'blue'),('انجام‌شده',2,'green')) v(name,position,color)
where g.kanban_enabled and not exists(select 1 from public.collab_kanban_columns c where c.group_id=g.id)
on conflict do nothing;
revoke all on function public.collab_seed_kanban_columns() from public,anon,authenticated;

create or replace function public.collab_reorder_kanban_columns(
 p_actor uuid,p_group uuid,p_ids uuid[]
) returns boolean language plpgsql security definer set search_path=''
as $$
declare actual_count integer; i integer;
begin
 if not exists(select 1 from public.collab_members m where m.group_id=p_group and m.account_id=p_actor
   and (m.can_edit or m.role in ('owner','assistant'))) then
  raise exception 'Edit denied' using errcode='42501';
 end if;
 select count(*) into actual_count from public.collab_kanban_columns where group_id=p_group;
 if coalesce(array_length(p_ids,1),0)<>actual_count or
   (select count(distinct x) from unnest(p_ids) x)<>actual_count or
   exists(select 1 from unnest(p_ids) x left join public.collab_kanban_columns c
      on c.id=x and c.group_id=p_group where c.id is null) then
  raise exception 'Column set mismatch' using errcode='22023';
 end if;
 update public.collab_kanban_columns set position=position+100000 where group_id=p_group;
 for i in 1..actual_count loop
  update public.collab_kanban_columns set position=i-1
   where group_id=p_group and id=p_ids[i];
 end loop;
 return true;
end; $$;
revoke all on function public.collab_reorder_kanban_columns(uuid,uuid,uuid[]) from public,anon,authenticated;
grant execute on function public.collab_reorder_kanban_columns(uuid,uuid,uuid[]) to service_role;

create or replace function public.collab_update_planning_item(
 p_actor uuid,p_group uuid,p_item uuid,p_expected_version int,
 p_title text,p_details text,p_state text,p_due_at timestamptz,
 p_assigned_to uuid,p_column_id uuid,p_starts_at timestamptz,
 p_recurrence_rule text,p_recurrence_until timestamptz,p_timezone text
) returns public.collab_items language plpgsql security definer set search_path=''
as $$
declare old_item public.collab_items%rowtype; result public.collab_items;
begin
 if not exists(select 1 from public.collab_members m where m.group_id=p_group and m.account_id=p_actor
   and (m.can_edit or m.role in ('owner','assistant'))) then
  raise exception 'Edit denied' using errcode='42501';
 end if;
 select * into old_item from public.collab_items where id=p_item and group_id=p_group for update;
 if not found then raise exception 'Item not found' using errcode='P0002'; end if;
 if old_item.version<>p_expected_version then raise exception 'Version conflict' using errcode='40001'; end if;
 if char_length(trim(coalesce(p_title,''))) not between 1 and 240 or p_state not in ('todo','doing','done') then
  raise exception 'Invalid item' using errcode='22023'; end if;
 if p_assigned_to is not null and not exists(select 1 from public.collab_members where group_id=p_group and account_id=p_assigned_to) then
  raise exception 'Assignee not a member' using errcode='22023'; end if;
 if p_column_id is not null and (old_item.mode<>'kanban' or not exists(
   select 1 from public.collab_kanban_columns where id=p_column_id and group_id=p_group)) then
  raise exception 'Invalid Kanban column' using errcode='22023'; end if;
 if p_recurrence_rule is not null and (old_item.mode<>'agenda' or p_recurrence_rule not in ('daily','weekdays','weekly','monthly')) then
  raise exception 'Invalid recurrence' using errcode='22023'; end if;
 if not exists(select 1 from pg_timezone_names where name=p_timezone) then
  raise exception 'Invalid timezone' using errcode='22023'; end if;
 update public.collab_items set title=trim(p_title),details=left(coalesce(p_details,''),12000),
  state=p_state,due_at=p_due_at,assigned_to=p_assigned_to,column_id=p_column_id,
  starts_at=p_starts_at,recurrence_rule=p_recurrence_rule,recurrence_until=p_recurrence_until,
  timezone=p_timezone,version=version+1,updated_by=p_actor,updated_at=now()
  where id=p_item returning * into result;
 return result;
end; $$;
revoke all on function public.collab_update_planning_item(uuid,uuid,uuid,int,text,text,text,timestamptz,uuid,uuid,timestamptz,text,timestamptz,text) from public,anon,authenticated;
grant execute on function public.collab_update_planning_item(uuid,uuid,uuid,int,text,text,text,timestamptz,uuid,uuid,timestamptz,text,timestamptz,text) to service_role;

create or replace function public.collab_replay_command(
 p_actor uuid,p_group uuid,p_command uuid,p_kind text,p_payload jsonb
) returns jsonb language plpgsql security definer set search_path=''
as $$
declare existing jsonb; inserted integer; g public.collab_groups%rowtype; m public.collab_members%rowtype;
 result jsonb; item public.collab_items%rowtype; message public.collab_messages%rowtype;
 mode_value text; title_value text; col_id uuid; repeat_rule text; tz text;
begin
 select * into m from public.collab_members where group_id=p_group and account_id=p_actor;
 if not found then raise exception 'Membership required' using errcode='42501'; end if;
 insert into public.collab_command_receipts(account_id,command_id,group_id,response)
 values(p_actor,p_command,p_group,'{}'::jsonb) on conflict do nothing;
 get diagnostics inserted = row_count;
 if inserted=0 then
  select response into existing from public.collab_command_receipts
   where account_id=p_actor and command_id=p_command and group_id=p_group;
  return existing;
 end if;
 select * into g from public.collab_groups where id=p_group;
 if p_kind='create_item' then
  if not (m.can_edit or m.role in ('owner','assistant')) then raise exception 'Edit denied' using errcode='42501'; end if;
  title_value:=trim(coalesce(p_payload->>'title',''));
  mode_value:=coalesce(p_payload->>'mode','agenda');
  col_id:=nullif(p_payload->>'columnId','')::uuid;
  repeat_rule:=nullif(p_payload->>'recurrenceRule','');
  tz:=coalesce(nullif(p_payload->>'timezone',''),'Asia/Tehran');
  if char_length(title_value) not between 1 and 240 or mode_value not in ('agenda','kanban','school') then
   raise exception 'Invalid item' using errcode='22023'; end if;
  if (mode_value='agenda' and not g.agenda_enabled) or (mode_value='kanban' and not g.kanban_enabled) or (mode_value='school' and not g.school_enabled) then
   raise exception 'Mode disabled' using errcode='22023'; end if;
  if col_id is not null and (mode_value<>'kanban' or not exists(select 1 from public.collab_kanban_columns where id=col_id and group_id=p_group)) then
   raise exception 'Invalid Kanban column' using errcode='22023'; end if;
  if repeat_rule is not null and (mode_value<>'agenda' or repeat_rule not in ('daily','weekdays','weekly','monthly')) then
   raise exception 'Invalid recurrence' using errcode='22023'; end if;
  if not exists(select 1 from pg_timezone_names where name=tz) then raise exception 'Invalid timezone' using errcode='22023'; end if;
  insert into public.collab_items(group_id,created_by,updated_by,title,details,mode,state,starts_at,due_at,timezone,recurrence_rule,recurrence_until,column_id)
  values(p_group,p_actor,p_actor,title_value,left(coalesce(p_payload->>'details',''),12000),mode_value,
   coalesce(p_payload->>'state','todo'),nullif(p_payload->>'startsAt','')::timestamptz,
   nullif(p_payload->>'dueAt','')::timestamptz,tz,repeat_rule,
   nullif(p_payload->>'recurrenceUntil','')::timestamptz,col_id)
  returning * into item;
  result:=to_jsonb(item);
 elsif p_kind='send_message' then
  if not m.can_chat then raise exception 'Chat denied' using errcode='42501'; end if;
  if char_length(trim(coalesce(p_payload->>'body',''))) not between 1 and 4000 then
   raise exception 'Invalid message' using errcode='22023'; end if;
  insert into public.collab_messages(group_id,sender_id,body)
  values(p_group,p_actor,trim(p_payload->>'body')) returning * into message;
  result:=to_jsonb(message);
 else raise exception 'Unsupported offline command' using errcode='22023';
 end if;
 update public.collab_command_receipts set response=result where account_id=p_actor and command_id=p_command;
 return result;
end; $$;
revoke all on function public.collab_replay_command(uuid,uuid,uuid,text,jsonb) from public,anon,authenticated;
grant execute on function public.collab_replay_command(uuid,uuid,uuid,text,jsonb) to service_role;
commit;
