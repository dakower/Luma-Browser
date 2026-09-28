create table if not exists public.assistant_daily_usage (
  user_id uuid not null references auth.users(id) on delete cascade,
  usage_date date not null default (now() at time zone 'utc')::date,
  request_count integer not null default 0 check (request_count >= 0),
  updated_at timestamptz not null default now(),
  primary key(user_id, usage_date)
);

alter table public.assistant_daily_usage enable row level security;

create or replace function public.consume_my_assistant_quota()
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_user uuid := auth.uid();
  v_email text;
  v_date date := (now() at time zone 'utc')::date;
  v_used integer;
  v_limit constant integer := 15;
begin
  if v_user is null then
    raise exception 'Authentication required' using errcode = '42501';
  end if;

  select lower(email) into v_email from auth.users where id = v_user;
  if v_email = 'dakowerr@gmail.com' then
    return jsonb_build_object('allowed', true, 'unlimited', true, 'limit', 0, 'used', 0, 'remaining', -1, 'usage_date', v_date);
  end if;

  insert into public.assistant_daily_usage(user_id, usage_date, request_count, updated_at)
  values(v_user, v_date, 1, now())
  on conflict(user_id, usage_date) do update
    set request_count = public.assistant_daily_usage.request_count + 1,
        updated_at = now()
    where public.assistant_daily_usage.request_count < v_limit
  returning request_count into v_used;

  if v_used is null then
    select request_count into v_used
    from public.assistant_daily_usage
    where user_id = v_user and usage_date = v_date;
    return jsonb_build_object('allowed', false, 'unlimited', false, 'limit', v_limit, 'used', coalesce(v_used, v_limit), 'remaining', 0, 'usage_date', v_date);
  end if;

  return jsonb_build_object('allowed', true, 'unlimited', false, 'limit', v_limit, 'used', v_used, 'remaining', greatest(v_limit - v_used, 0), 'usage_date', v_date);
end;
$$;

create or replace function public.refund_assistant_quota(p_user_id uuid, p_usage_date date)
returns void
language sql
security definer
set search_path = public
as $$
  update public.assistant_daily_usage
  set request_count = greatest(request_count - 1, 0), updated_at = now()
  where user_id = p_user_id and usage_date = p_usage_date;
$$;

revoke all on function public.consume_my_assistant_quota() from public;
grant execute on function public.consume_my_assistant_quota() to authenticated;
revoke all on function public.refund_assistant_quota(uuid, date) from public, anon, authenticated;
grant execute on function public.refund_assistant_quota(uuid, date) to service_role;
