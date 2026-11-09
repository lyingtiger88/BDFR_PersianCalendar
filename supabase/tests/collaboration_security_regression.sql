-- Run against a disposable Supabase project or with an operator role.
-- All rows are rolled back; this exercises database invariants without creating test users.
begin;

do $$
declare
  owner_id uuid;
  member_id uuid;
  group_id uuid;
  command_id uuid := gen_random_uuid();
  message_command_id uuid := gen_random_uuid();
  first_result jsonb;
  replay_result jsonb;
  message_result jsonb;
  item_id uuid;
  item_version integer;
  subject_id uuid;
  homework_id uuid;
  submission_id uuid;
  event_count integer;
  column_count integer;
begin
  insert into public.zurvan_desk_accounts(identifier,password_salt,password_hash,vault_id)
  values ('reg-owner-'||gen_random_uuid()::text,repeat('s',24),repeat('h',64),
          'reg-vault-owner-'||gen_random_uuid()::text)
  returning id into owner_id;

  insert into public.zurvan_desk_accounts(identifier,password_salt,password_hash,vault_id)
  values ('reg-member-'||gen_random_uuid()::text,repeat('s',24),repeat('h',64),
          'reg-vault-member-'||gen_random_uuid()::text)
  returning id into member_id;

  insert into public.collab_groups(owner_id,name,agenda_enabled,kanban_enabled,school_enabled)
  values(owner_id,'__collaboration_security_regression__',true,true,true)
  returning id into group_id;

  insert into public.collab_members(group_id,account_id,role,can_chat,can_report,can_edit)
  values(group_id,member_id,'member',true,true,false);

  select count(*) into column_count
  from public.collab_kanban_columns where group_id=group_id;
  if column_count <> 3 then raise exception 'Default Kanban columns were not created'; end if;

  first_result := public.collab_replay_command(owner_id,group_id,command_id,'create_item',
    jsonb_build_object('title','private regression title','details','private regression detail',
      'mode','kanban','state','todo','timezone','Europe/Berlin'));
  replay_result := public.collab_replay_command(owner_id,group_id,command_id,'create_item',
    jsonb_build_object('title','private regression title','details','private regression detail',
      'mode','kanban','state','todo','timezone','Europe/Berlin'));

  if first_result->>'id' is distinct from replay_result->>'id' then
    raise exception 'Idempotent create returned different item IDs';
  end if;
  item_id := (first_result->>'id')::uuid;
  item_version := (first_result->>'version')::integer;

  if (select count(*) from public.collab_items where id=item_id) <> 1 then
    raise exception 'Idempotent create inserted duplicate rows';
  end if;
  if (select response ? 'title' or response ? 'details'
      from public.collab_command_receipts
      where account_id=owner_id and command_id=command_id) then
    raise exception 'Command receipt retained private item text';
  end if;

  begin
    perform public.collab_replay_command(member_id,group_id,gen_random_uuid(),'create_item',
      jsonb_build_object('title','unauthorized','mode','agenda','state','todo'));
    raise exception 'Member without edit permission created an item';
  exception when insufficient_privilege then null;
  end;

  message_result := public.collab_replay_command(member_id,group_id,message_command_id,'send_message',
    jsonb_build_object('body','private regression message'));
  if not exists(select 1 from public.collab_messages
      where id=(message_result->>'id')::uuid and sender_id=member_id) then
    raise exception 'Member with chat permission could not send a message';
  end if;
  if (select response ? 'body' from public.collab_command_receipts
      where account_id=member_id and command_id=message_command_id) then
    raise exception 'Command receipt retained message body';
  end if;

  begin
    perform public.collab_replay_command(member_id,group_id,gen_random_uuid(),'update_item',
      jsonb_build_object('itemId',item_id,'expectedVersion',item_version,
        'title','unauthorized edit','details','','state','doing','timezone','Europe/Berlin'));
    raise exception 'Member without edit permission updated an item';
  exception when insufficient_privilege then null;
  end;

  insert into public.collab_subjects(group_id,name)
  values(group_id,'Regression subject') returning id into subject_id;
  insert into public.collab_homework(group_id,subject_id,created_by,title,instructions)
  values(group_id,subject_id,owner_id,'Regression homework','test')
  returning id into homework_id;
  insert into public.collab_homework_submissions(homework_id,student_id,body)
  values(homework_id,member_id,'private student submission')
  returning id into submission_id;

  select count(*) into event_count
  from public.collab_events
  where group_id=group_id and kind='school.submission'
    and resource_id=submission_id and actor_id=member_id;
  if event_count <> 1 then raise exception 'School submission event was not emitted exactly once'; end if;
end $$;

rollback;
