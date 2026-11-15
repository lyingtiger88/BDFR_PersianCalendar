begin;

create table if not exists public.zurvan_manual_payments (
 id uuid primary key default gen_random_uuid(),
 auth_user_id uuid not null references auth.users(id) on delete restrict,
 full_name text not null check (char_length(btrim(full_name)) between 3 and 120),
 contact_email text not null check (char_length(contact_email) between 5 and 254),
 phone text not null check (phone ~ '^\+?[0-9]{9,15}$'),
 receipt_path text not null check (char_length(receipt_path) between 8 and 500),
 receipt_mime text not null check (receipt_mime in ('image/jpeg','image/png','application/pdf')),
 receipt_size integer not null check (receipt_size between 1 and 5242880),
 status text not null default 'pending' check (status in ('pending','approved','rejected')),
 submitted_at timestamptz not null default now(),
 reviewed_at timestamptz,
 reviewed_by uuid references auth.users(id),
 admin_note text,
 constraint review_consistency check
   ((status='pending' and reviewed_at is null and reviewed_by is null) or
    (status<>'pending' and reviewed_at is not null and reviewed_by is not null))
);
create index if not exists zurvan_manual_payments_user_idx on public.zurvan_manual_payments(auth_user_id,submitted_at desc);
create unique index if not exists zurvan_manual_payments_one_pending
 on public.zurvan_manual_payments(auth_user_id) where status='pending';

alter table public.zurvan_manual_payments enable row level security;
revoke all on public.zurvan_manual_payments from public,anon,authenticated;
grant all on public.zurvan_manual_payments to service_role;

create or replace function public.zurvan_manual_review_payment(
 p_payment_id uuid, p_admin_id uuid, p_approve boolean, p_note text default null
)
returns jsonb language plpgsql security invoker set search_path=''
as $$
declare
 req public.zurvan_manual_payments%rowtype;
 current_license public.zurvan_licenses%rowtype;
 next_expiry timestamptz;
begin
 if not exists(
   select 1 from public.zurvan_admin_users
   where auth_user_id=p_admin_id and active=true and role='superadmin'
 ) then raise exception 'not_authorized' using errcode='42501'; end if;

 select * into req from public.zurvan_manual_payments
   where id=p_payment_id for update;
 if not found then raise exception 'request_missing' using errcode='22023'; end if;
 if req.status<>'pending' then raise exception 'already_reviewed' using errcode='23505'; end if;
 if not exists(select 1 from public.zurvan_accounts
   where id=req.auth_user_id and status='active') then
   raise exception 'license_account_missing' using errcode='22023'; end if;

 if p_approve then
   select * into current_license from public.zurvan_licenses
     where account_id=req.auth_user_id and product_id='zurvan' for update;
   if found and current_license.type='lifetime' and current_license.status='active' then
     raise exception 'lifetime_license_unchanged' using errcode='22023';
   end if;
   next_expiry := greatest(now(),coalesce(current_license.expires_at,now())) + interval '1 year';
   insert into public.zurvan_licenses(account_id,product_id,type,status,expires_at,order_id)
     values(req.auth_user_id,'zurvan','subscription','active',next_expiry,null)
     on conflict(account_id,product_id) do update set
       type='subscription',status='active',expires_at=excluded.expires_at,
       generation=public.zurvan_licenses.generation+1;
 end if;

 update public.zurvan_manual_payments set
   status=case when p_approve then 'approved' else 'rejected' end,
   reviewed_at=now(), reviewed_by=p_admin_id,
   admin_note=nullif(left(btrim(coalesce(p_note,'')),500),'')
 where id=p_payment_id;
 return jsonb_build_object('id',p_payment_id,
   'status',case when p_approve then 'approved' else 'rejected' end,
   'license_expires_at',case when p_approve then next_expiry else null end);
end;
$$;

revoke all on function public.zurvan_manual_review_payment(uuid,uuid,boolean,text)
 from public,anon,authenticated;
grant execute on function public.zurvan_manual_review_payment(uuid,uuid,boolean,text)
 to service_role;
commit;