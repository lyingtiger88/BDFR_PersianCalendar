-- pgcrypto gen_random_bytes is installed in the extensions schema on Supabase.
-- Explicitly qualify it because the generator uses a hardened empty search_path.
create or replace function public.zurvan_generate_invite_identifier()
returns text language plpgsql volatile security invoker set search_path='' as $$
declare
 alphabet constant text := 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
 code text:=''; candidate integer;
begin
 while length(code)<5 loop
  candidate := get_byte(extensions.gen_random_bytes(1),0);
  if candidate < 252 then
   code:=code||substr(alphabet,(candidate % 36)+1,1);
  end if;
 end loop;
 return code;
end;
$$;
revoke all on function public.zurvan_generate_invite_identifier()
 from public,anon,authenticated;
grant execute on function public.zurvan_generate_invite_identifier() to service_role;