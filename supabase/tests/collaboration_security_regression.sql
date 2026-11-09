-- Run with an operator role against a disposable Supabase project.
-- All fixture rows are rolled back; no test account remains in the database.
begin;

do $$
declare
  v_owner_id uuid;
  v_member_id uuid;
  v_group_id uuid;
  v_command_id uuid := gen_random_uuid();
  v_message_command_id uuid := gen_random_uuid();
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

  insert into public.collab_groups(owner_id,name,agenda_enabled,kanban_enabled,school_enabled)
  values(v_owner_id,'__collaboration_security_regression__',true,true,true)
  returning id into v_group_id;

  insert into public.collab_members(group_id,account_id,role,can_chat,can_report,can_edit)
  values(v_group_id,v_member_id,'member',true,true,false);

  select count(*) into v_column_count
  from public.collab_kanban_columns c where c.group_id=v_group_id;
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

  if (select count(*) from public.collab_items i where i.id=v_item_id) <> 1 then
    raise exception 'Idempotent create inserted duplicate rows';
  end if;
  if (select r.response ? 'title' or r.response ? 'details'
      from public.collab_command_receipts r
      where r.account_id=v_owner_id and r.command_id=v_command_id) then
    raise exception 'Command receipt retained private item text';
  end if;

  begin
    perform public.collab_replay_command(v_member_id,v_group_id,gen_random_uuid(),'create_item',
      jsonb_build_object('title','unauthorized','mode','agenda','state','todo'));
    raise exception 'Member without edit permission created an item';
  exception when insufficient_privilege then null;
  end;

  v_message_result := public.collab_replay_command(v_member_id,v_group_id,v_message_command_id,'send_message',
    jsonb_build_object('body','private regression message'));
  if not exists(select 1 from public.collab_messages m
      where m.id=(v_message_result->>'id')::uuid and m.sender_id=v_member_id) then
    raise exception 'Member with chat permission could not send a message';
  end if;
  if (select r.response ? 'body' from public.collab_command_receipts r
      where r.account_id=v_member_id and r.command_id=v_message_command_id) then
    raise exception 'Command receipt retained message body';
  end if;

  begin
    perform public.collab_replay_command(v_member_id,v_group_id,gen_random_uuid(),'update_item',
      jsonb_build_object('itemId',v_item_id,'expectedVersion',v_item_version,
        'title','unauthorized edit','details','','state','doing','timezone','Europe/Berlin'));
    raise exception 'Member without edit permission updated an item';
  exception when insufficient_privilege then null;
  end;

  insert into public.collab_subjects(group_id,name)
  values(v_group_id,'Regression subject') returning id into v_subject_id;
  insert into public.collab_homework(group_id,subject_id,created_by,title,instructions)
  values(v_group_id,v_subject_id,v_owner_id,'Regression homework','test')
  returning id into v_homework_id;
  insert into public.collab_homework_submissions(homework_id,student_id,body)
  values(v_homework_id,v_member_id,'private student submission')
  returning id into v_submission_id;

  select count(*) into v_event_count
  from public.collab_events e
  where e.group_id=v_group_id and e.kind='school.submission'
    and e.resource_id=v_submission_id and e.actor_id=v_member_id;
  if v_event_count <> 1 then raise exception 'School submission event was not emitted exactly once'; end if;
end $$;

rollback;
