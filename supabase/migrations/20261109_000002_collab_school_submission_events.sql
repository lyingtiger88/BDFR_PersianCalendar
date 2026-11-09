-- Metadata-only event for school submissions; never publish body, grade or feedback.
begin;
create or replace function public.collab_record_change()
returns trigger language plpgsql security definer set search_path = ''
as $$
declare ref_group uuid; ref_id uuid; actor uuid; kind text;
begin
 if tg_table_name='collab_homework_submissions' then
  ref_id:=new.id; actor:=new.student_id; kind:='school.submission';
  select h.group_id into ref_group from public.collab_homework h where h.id=new.homework_id;
 elsif tg_op='DELETE' then ref_group:=old.group_id;
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
 elsif tg_table_name='collab_homework_submissions' then null;
 else ref_id:=new.id;kind:='school';actor:=null; end if;
 insert into public.collab_events(group_id,kind,resource_id,actor_id)
 values(ref_group,kind,ref_id,actor);
 if tg_op='DELETE' then return old;end if;
 return new;
end; $$;
drop trigger if exists collab_homework_submissions_change on public.collab_homework_submissions;
create trigger collab_homework_submissions_change after insert or update
on public.collab_homework_submissions for each row execute function public.collab_record_change();
revoke all on function public.collab_record_change() from public,anon,authenticated;
commit;
