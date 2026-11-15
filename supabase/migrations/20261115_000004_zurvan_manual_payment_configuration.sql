begin;
create table if not exists public.zurvan_manual_payment_config (
 singleton boolean primary key default true check (singleton),
 merchant_card text check (merchant_card is null or merchant_card ~ '^[0-9]{16}$'),
 merchant_name text check (merchant_name is null or char_length(btrim(merchant_name)) between 3 and 120),
 updated_at timestamptz not null default now(),
 updated_by uuid references auth.users(id)
);
insert into public.zurvan_manual_payment_config(singleton) values(true)
on conflict(singleton) do nothing;
alter table public.zurvan_manual_payment_config enable row level security;
revoke all on public.zurvan_manual_payment_config from public,anon,authenticated;
grant all on public.zurvan_manual_payment_config to service_role;
commit;