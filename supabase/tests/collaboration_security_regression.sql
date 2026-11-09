-- Run against a disposable Supabase project or with an operator role.
-- All rows are rolled back; this exercises database invariants without creating test users.
begin;

do $$
declare
  v_owner_id uuid;
  v_member_id uuid;
  v_group_id uuid;
  v_command_id uuid := gen_random_uuid();
  message_v_command_id uuid := gen_random_uuid();
  v_first_result jsonb;
  v_replay_result jsonb;
  v_message_result jsonb;
  v_item_id uuid;
  v_item_version integer;
  v_subject_id uuid;
  v_homework_id uuid;
  v_submission_id uuid;
  v_event_count integer;
  v_column_count integer;
begin
  insert into public.zurvan_desk_accounts(identifier,password_salt,password_hash,vault_id)
  values ('reg-owner-'||gen_random_uuid()::text,repeat('s',24),repeat('h',64),
          'reg-vault-owner-'||gen_random_uuid()::text)
  returning id into v_owner_id;

  insert into public.zurvan_desk_accounts(identifier,password_salt,password_hash,vault_id)
  values ('reg-member-'||gen_random_uuid()::text,repeat('s',24),repeat('h',64),
          'reg-vault-member-'||gen_random_uuid()::text)
  returning id into v_member_id;

  insert into public.collab_groups(v_owner_id,name,agenda_enabled,kanban_enabled,school_enabled)
  values(v_owner_id,'__collaboration_security_regression__',true,true,true)
  returning id into v_group_id;

  insert into public.collab_members(v_group_id,account_id,role,can_chat,can_report,can_edit)
  values(v_group_id,v_member_id,'member',true,true,false);

  select count(*) into v_column_count
  from public.collab_kanban_columns where v_group_id=v_group_id;
  if v_column_count <> 3 then raise exception 'Default Kanban columns were not created'; end if;

  v_first_result := public.collab_replay_command(v_owner_id,v_group_id,v_command_id,'create_item',
    jsonb_build_object('title','private regression title','details','private regression detail',
      'mode','kanban','state','todo','timezone','Europe/Berlin'));
  v_replay_result := public.collab_replay_command(v_owner_id,v_group_id,v_command_id,'create_item',
    jsonb_build_object('title','private regression title','details','private regression detail',
      'mode','kanban','state','todo','timezone','Europe/Berlin'));

  if v_first_result->>'id' is distinct from v_replay_result->>'id' then
    raise exception 'Idempotent create returned different item IDs';
  end if;
  v_item_id := (v_first_result->>'id')::uuid;
  v_item_version := (v_first_result->>'version')::integer;

  if (select count(*) from public.collab_items where id=v_item_id) <> 1 then
    raise exception 'Idempotent create inserted duplicate rows';
  end if;
  if (select response ? 'title' or response ? 'details'
      from public.collab_command_receipts
      where account_id=v_owner_id and v_command_id=v_command_id) then
    raise exception 'Command receipt retained private item text';
  end if;

  begin
    perform public.collab_replay_command(v_member_id,v_group_id,gen_random_uuid(),'create_item',
      jsonb_build_object('title','unauthorized','mode','agenda','state','todo'));
    raise exception 'Member without edit permission created an item';
  exception when insufficient_privilege then null;
  end;

  v_message_result := public.collab_replay_command(v_member_id,v_group_id,message_v_command_id,'send_message',
    jsonb_build_object('body','private regression message'));
  if not exists(select 1 from public.collab_messages
      where id=(v_message_result->>'id')::uuid and sender_id=v_member_id) then
    raise exception 'Member with chat permission could not send a message';
  end if;
  if (select response ? 'body' from public.collab_command_receipts
      where account_id=v_member_id and v_command_id=message_v_command_id) then
    raise exception 'Command receipt retained message body';
  end if;

  begin
    perform public.collab_replay_command(v_member_id,v_group_id,gen_random_uuid(),'update_item',
      jsonb_build_object('itemId',v_item_id,'expectedVersion',v_item_version,
        'title','unauthorized edit','details','','state','doing','timezone','Europe/Berlin'));
    raise exception 'Member without edit permission updated an item';
  exception when insufficient_privilege then null;
  end;

  insert into public.collab_subjects(v_group_id,name)
  values(v_group_id,'Regression subject') returning id into v_subject_id;
  insert into public.collab_homework(v_group_id,v_subject_id,created_by,title,instructions)
  values(v_group_id,v_subject_id,v_owner_id,'Regression homework','test')
  returning id into v_homework_id;
  insert into public.collab_homework_submissions(v_homework_id,student_id,body)
  values(v_homework_id,v_member_id,'private student submission')
  returning id into v_submission_id;

  select count(*) into v_event_count
  from public.collab_events
  where v_group_id=v_group_id and kind='school.submission'
    and resource_id=v_submission_id and actor_id=v_member_id;
  if v_event_count <> 1 then raise exception 'School submission event was not emitted exactly once'; end if;
end $$;

rollback;
