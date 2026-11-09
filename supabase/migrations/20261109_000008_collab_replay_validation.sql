-- Tighten offline replay validation to match the direct planning API.
begin;

create or replace function public.collab_replay_command(
 p_actor uuid,p_group uuid,p_command uuid,p_kind text,p_payload jsonb
) returns jsonb language plpgsql security definer set search_path=''
as $$
declare
 existing jsonb; inserted integer; g public.collab_groups%rowtype; m public.collab_members%rowtype;
 result jsonb; item public.collab_items%rowtype; message public.collab_messages%rowtype;
 mode_value text; title_value text; state_value text; col_id uuid; assignee_id uuid;
 repeat_rule text; tz text; starts_value timestamptz; due_value timestamptz; repeat_end timestamptz;
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
  if not (m.can_edit or m.role in ('owner','assistant')) then
   raise exception 'Edit denied' using errcode='42501';
  end if;

  title_value:=trim(coalesce(p_payload->>'title',''));
  mode_value:=coalesce(nullif(p_payload->>'mode',''),'agenda');
  state_value:=coalesce(nullif(p_payload->>'state',''),'todo');
  col_id:=nullif(p_payload->>'columnId','')::uuid;
  assignee_id:=nullif(p_payload->>'assignedTo','')::uuid;
  repeat_rule:=nullif(p_payload->>'recurrenceRule','');
  tz:=coalesce(nullif(p_payload->>'timezone',''),'Asia/Tehran');
  starts_value:=nullif(p_payload->>'startsAt','')::timestamptz;
  due_value:=nullif(p_payload->>'dueAt','')::timestamptz;
  repeat_end:=nullif(p_payload->>'recurrenceUntil','')::timestamptz;

  if char_length(title_value) not between 1 and 240
     or mode_value not in ('agenda','kanban','school')
     or state_value not in ('todo','doing','done') then
   raise exception 'Invalid item' using errcode='22023';
  end if;
  if (mode_value='agenda' and not g.agenda_enabled)
     or (mode_value='kanban' and not g.kanban_enabled)
     or (mode_value='school' and not g.school_enabled) then
   raise exception 'Mode disabled' using errcode='22023';
  end if;
  if assignee_id is not null and not exists(
    select 1 from public.collab_members where group_id=p_group and account_id=assignee_id
  ) then
   raise exception 'Assignee not a member' using errcode='22023';
  end if;
  if col_id is not null and (mode_value<>'kanban' or not exists(
    select 1 from public.collab_kanban_columns where id=col_id and group_id=p_group
  )) then
   raise exception 'Invalid Kanban column' using errcode='22023';
  end if;
  if repeat_rule is not null and (
    mode_value<>'agenda' or repeat_rule not in ('daily','weekdays','weekly','monthly')
    or coalesce(starts_value,due_value) is null
  ) then
   raise exception 'Invalid recurrence' using errcode='22023';
  end if;
  if repeat_end is not null and (
    repeat_rule is null or coalesce(starts_value,due_value) is null
    or repeat_end < coalesce(starts_value,due_value)
  ) then
   raise exception 'Invalid recurrence end' using errcode='22023';
  end if;
  if not exists(select 1 from pg_timezone_names where name=tz) then
   raise exception 'Invalid timezone' using errcode='22023';
  end if;

  insert into public.collab_items(
    group_id,created_by,updated_by,title,details,mode,state,assigned_to,
    starts_at,due_at,timezone,recurrence_rule,recurrence_until,column_id
  ) values(
    p_group,p_actor,p_actor,title_value,left(coalesce(p_payload->>'details',''),12000),
    mode_value,state_value,assignee_id,starts_value,due_value,tz,repeat_rule,repeat_end,col_id
  ) returning * into item;
  result:=to_jsonb(item);

 elsif p_kind='update_item' then
  if not (m.can_edit or m.role in ('owner','assistant')) then
   raise exception 'Edit denied' using errcode='42501';
  end if;
  perform 1 from public.collab_items
   where id=(p_payload->>'itemId')::uuid and group_id=p_group;
  if not found then raise exception 'Item not found' using errcode='P0002'; end if;

  select * into item from public.collab_update_planning_item(
    p_actor,p_group,(p_payload->>'itemId')::uuid,(p_payload->>'expectedVersion')::int,
    coalesce(p_payload->>'title',''),coalesce(p_payload->>'details',''),
    coalesce(p_payload->>'state','todo'),
    nullif(p_payload->>'dueAt','')::timestamptz,
    nullif(p_payload->>'assignedTo','')::uuid,
    nullif(p_payload->>'columnId','')::uuid,
    nullif(p_payload->>'startsAt','')::timestamptz,
    nullif(p_payload->>'recurrenceRule',''),
    nullif(p_payload->>'recurrenceUntil','')::timestamptz,
    coalesce(nullif(p_payload->>'timezone',''),'Asia/Tehran')
  );
  result:=to_jsonb(item);

 elsif p_kind='send_message' then
  if not m.can_chat then raise exception 'Chat denied' using errcode='42501'; end if;
  if char_length(trim(coalesce(p_payload->>'body',''))) not between 1 and 4000 then
   raise exception 'Invalid message' using errcode='22023';
  end if;
  insert into public.collab_messages(group_id,sender_id,body)
  values(p_group,p_actor,trim(p_payload->>'body')) returning * into message;
  result:=to_jsonb(message);
 else
  raise exception 'Unsupported offline command' using errcode='22023';
 end if;

 -- Keep replay receipts free of message bodies and private planning details.
 update public.collab_command_receipts set response=jsonb_build_object(
  'id',result->>'id','version',result->'version','kind',p_kind)
 where account_id=p_actor and command_id=p_command;
 return result;
end;
$$;

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
  raise exception 'Invalid item' using errcode='22023';
 end if;
 if p_assigned_to is not null and not exists(
   select 1 from public.collab_members where group_id=p_group and account_id=p_assigned_to
 ) then
  raise exception 'Assignee not a member' using errcode='22023';
 end if;
 if p_column_id is not null and (old_item.mode<>'kanban' or not exists(
   select 1 from public.collab_kanban_columns where id=p_column_id and group_id=p_group
 )) then
  raise exception 'Invalid Kanban column' using errcode='22023';
 end if;
 if p_recurrence_rule is not null and (
   old_item.mode<>'agenda' or p_recurrence_rule not in ('daily','weekdays','weekly','monthly')
   or coalesce(p_starts_at,p_due_at) is null
 ) then
  raise exception 'Invalid recurrence' using errcode='22023';
 end if;
 if p_recurrence_until is not null and (
   p_recurrence_rule is null or coalesce(p_starts_at,p_due_at) is null
   or p_recurrence_until < coalesce(p_starts_at,p_due_at)
 ) then
  raise exception 'Invalid recurrence end' using errcode='22023';
 end if;
 if not exists(select 1 from pg_timezone_names where name=p_timezone) then
  raise exception 'Invalid timezone' using errcode='22023';
 end if;
 update public.collab_items set title=trim(p_title),details=left(coalesce(p_details,''),12000),
  state=p_state,due_at=p_due_at,assigned_to=p_assigned_to,column_id=p_column_id,
  starts_at=p_starts_at,recurrence_rule=p_recurrence_rule,recurrence_until=p_recurrence_until,
  timezone=p_timezone,version=version+1,updated_by=p_actor,updated_at=now()
  where id=p_item returning * into result;
 return result;
end;
$$;

revoke all on function public.collab_replay_command(uuid,uuid,uuid,text,jsonb) from public,anon,authenticated;
grant execute on function public.collab_replay_command(uuid,uuid,uuid,text,jsonb) to service_role;
revoke all on function public.collab_update_planning_item(uuid,uuid,uuid,int,text,text,text,timestamptz,uuid,uuid,timestamptz,text,timestamptz,text) from public,anon,authenticated;
grant execute on function public.collab_update_planning_item(uuid,uuid,uuid,int,text,text,text,timestamptz,uuid,uuid,timestamptz,text,timestamptz,text) to service_role;

commit;
