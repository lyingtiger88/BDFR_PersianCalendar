-- Atomic, idempotent replay of a small, explicit set of offline collaboration commands.
begin;
create or replace function public.collab_replay_command(
 p_actor uuid,p_group uuid,p_command uuid,p_kind text,p_payload jsonb
) returns jsonb language plpgsql security definer set search_path=''
as $$
declare existing jsonb; inserted integer; g public.collab_groups%rowtype; m public.collab_members%rowtype;
 result jsonb; item public.collab_items%rowtype; message public.collab_messages%rowtype;
 mode_value text; title_value text;
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
  if char_length(title_value) not between 1 and 240 or mode_value not in ('agenda','kanban','school') then
   raise exception 'Invalid item' using errcode='22023'; end if;
  if (mode_value='agenda' and not g.agenda_enabled) or (mode_value='kanban' and not g.kanban_enabled) or (mode_value='school' and not g.school_enabled) then
   raise exception 'Mode disabled' using errcode='22023'; end if;
  insert into public.collab_items(group_id,created_by,updated_by,title,details,mode,state,starts_at,due_at,timezone,recurrence_rule,recurrence_until)
  values(p_group,p_actor,p_actor,title_value,left(coalesce(p_payload->>'details',''),12000),mode_value,
   coalesce(p_payload->>'state','todo'),nullif(p_payload->>'startsAt','')::timestamptz,
   nullif(p_payload->>'dueAt','')::timestamptz,coalesce(nullif(p_payload->>'timezone',''),'Asia/Tehran'),
   nullif(p_payload->>'recurrenceRule',''),nullif(p_payload->>'recurrenceUntil','')::timestamptz)
  returning * into item;
  result:=to_jsonb(item);
 elsif p_kind='send_message' then
  if not m.can_chat then raise exception 'Chat denied' using errcode='42501'; end if;
  if char_length(trim(coalesce(p_payload->>'body',''))) not between 1 and 4000 then
   raise exception 'Invalid message' using errcode='22023'; end if;
  insert into public.collab_messages(group_id,sender_id,body)
  values(p_group,p_actor,trim(p_payload->>'body')) returning * into message;
  result:=to_jsonb(message);
 else
  raise exception 'Unsupported offline command' using errcode='22023';
 end if;
 update public.collab_command_receipts set response=result where account_id=p_actor and command_id=p_command;
 return result;
end; $$;
revoke all on function public.collab_replay_command(uuid,uuid,uuid,text,jsonb) from public,anon,authenticated;
grant execute on function public.collab_replay_command(uuid,uuid,uuid,text,jsonb) to service_role;
commit;
