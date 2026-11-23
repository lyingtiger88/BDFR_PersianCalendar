-- Make XP visible as zero for every account, including future sign-ups,
-- rather than showing an empty table until the first daily claim.
begin;
insert into public.zurvan_user_scores(account_id)
select a.id from public.zurvan_accounts a
on conflict(account_id) do nothing;

create or replace function public.zurvan_init_user_score()
returns trigger language plpgsql security definer set search_path = '' as $$
begin
  insert into public.zurvan_user_scores(account_id) values(new.id)
  on conflict(account_id) do nothing;
  return new;
end;
$$;
revoke all on function public.zurvan_init_user_score()
  from public,anon,authenticated;
drop trigger if exists zurvan_init_user_score on public.zurvan_accounts;
create trigger zurvan_init_user_score
  after insert on public.zurvan_accounts
  for each row execute function public.zurvan_init_user_score();
commit;
