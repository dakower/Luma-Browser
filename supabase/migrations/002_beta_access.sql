create extension if not exists pgcrypto;

alter table public.profiles add column if not exists role text not null default 'user';
alter table public.profiles drop constraint if exists profiles_role_check;
alter table public.profiles add constraint profiles_role_check check (role in ('user','admin'));

-- The administrator role is assigned server-side to the confirmed account below.
update public.profiles p set role = 'admin'
where p.id in (select id from auth.users where lower(email) = lower('dakowerr@gmail.com'));

create or replace function public.handle_new_user() returns trigger language plpgsql security definer set search_path = public as $$
begin
  insert into public.profiles(id, display_name, role)
  values(new.id, coalesce(new.raw_user_meta_data->>'display_name','Luma User'), case when lower(new.email) = lower('dakowerr@gmail.com') then 'admin' else 'user' end)
  on conflict(id) do update set display_name = excluded.display_name, role = case when lower(new.email) = lower('dakowerr@gmail.com') then 'admin' else public.profiles.role end;
  return new;
end; $$;

create table if not exists public.beta_access (
  user_id uuid primary key references auth.users(id) on delete cascade,
  status text not null default 'approved' check (status in ('approved','blocked')),
  granted_at timestamptz not null default now(),
  granted_by uuid references auth.users(id)
);

create table if not exists public.beta_codes (
  id uuid primary key default gen_random_uuid(),
  code_hash text not null unique,
  code_hint text not null,
  label text not null default 'Beta access',
  max_uses integer not null default 1 check (max_uses between 1 and 10000),
  uses integer not null default 0 check (uses >= 0),
  expires_at timestamptz not null,
  active boolean not null default true,
  created_by uuid not null references auth.users(id),
  created_at timestamptz not null default now()
);

alter table public.beta_access enable row level security;
alter table public.beta_codes enable row level security;

drop policy if exists beta_access_read_own on public.beta_access;
create policy beta_access_read_own on public.beta_access for select using (auth.uid() = user_id);

create or replace function public.is_luma_admin()
returns boolean language sql stable security definer set search_path = public as $$
  select exists(select 1 from public.profiles where id = auth.uid() and role = 'admin');
$$;

create or replace function public.get_my_beta_access()
returns jsonb language sql stable security definer set search_path = public as $$
  select jsonb_build_object(
    'role', coalesce((select role from public.profiles where id = auth.uid()), 'user'),
    'is_admin', public.is_luma_admin(),
    'has_beta', public.is_luma_admin() or exists(select 1 from public.beta_access where user_id = auth.uid() and status = 'approved'),
    'beta_status', case when public.is_luma_admin() then 'admin' when exists(select 1 from public.beta_access where user_id = auth.uid() and status = 'approved') then 'approved' else 'none' end
  );
$$;

create or replace function public.create_beta_code(p_label text default 'Beta access', p_max_uses integer default 1, p_valid_days integer default 30)
returns text language plpgsql security definer set search_path = public as $$
declare raw_code text;
begin
  if not public.is_luma_admin() then raise exception 'Administrator access required' using errcode = '42501'; end if;
  if p_max_uses < 1 or p_max_uses > 10000 then raise exception 'Invalid max uses'; end if;
  if p_valid_days < 1 or p_valid_days > 365 then raise exception 'Invalid validity period'; end if;
  raw_code := 'LUMA-' || upper(substr(encode(gen_random_bytes(4), 'hex'), 1, 4)) || '-' || upper(substr(encode(gen_random_bytes(4), 'hex'), 1, 4));
  insert into public.beta_codes(code_hash, code_hint, label, max_uses, expires_at, created_by)
  values(encode(digest(upper(raw_code), 'sha256'), 'hex'), right(raw_code, 4), coalesce(nullif(trim(p_label),''),'Beta access'), p_max_uses, now() + make_interval(days => p_valid_days), auth.uid());
  return raw_code;
end;
$$;

create or replace function public.redeem_beta_code(p_code text)
returns boolean language plpgsql security definer set search_path = public as $$
declare selected_id uuid;
begin
  if auth.uid() is null then raise exception 'Authentication required' using errcode = '42501'; end if;
  select id into selected_id from public.beta_codes
  where code_hash = encode(digest(upper(trim(p_code)), 'sha256'), 'hex') and active and expires_at > now() and uses < max_uses
  for update;
  if selected_id is null then return false; end if;
  update public.beta_codes set uses = uses + 1, active = case when uses + 1 >= max_uses then false else active end where id = selected_id;
  insert into public.beta_access(user_id, status, granted_by) values(auth.uid(), 'approved', (select created_by from public.beta_codes where id = selected_id))
  on conflict(user_id) do update set status = 'approved', granted_at = now(), granted_by = excluded.granted_by;
  return true;
end;
$$;

create or replace function public.list_beta_codes()
returns jsonb language sql stable security definer set search_path = public as $$
  select case when public.is_luma_admin() then coalesce(jsonb_agg(jsonb_build_object('id',id,'hint',code_hint,'label',label,'uses',uses,'max_uses',max_uses,'expires_at',expires_at,'active',active) order by created_at desc),'[]'::jsonb) else '[]'::jsonb end from public.beta_codes;
$$;

grant execute on function public.is_luma_admin() to authenticated;
grant execute on function public.get_my_beta_access() to authenticated;
grant execute on function public.create_beta_code(text,integer,integer) to authenticated;
grant execute on function public.redeem_beta_code(text) to authenticated;
grant execute on function public.list_beta_codes() to authenticated;

insert into storage.buckets(id,name,public) values('beta-installers','beta-installers',false) on conflict(id) do update set public=false;
