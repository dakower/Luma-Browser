-- Read-only quota snapshot for the desktop UI. Unlike consume_my_assistant_quota(),
-- this function never increments usage.
create or replace function public.get_my_assistant_quota()
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_user uuid := auth.uid();
  v_email text;
  v_date date := (now() at time zone 'utc')::date;
  v_used integer := 0;
  v_limit constant integer := 15;
begin
  if v_user is null then
    raise exception 'Authentication required' using errcode = '42501';
  end if;

  select lower(email) into v_email from auth.users where id = v_user;
  if v_email = 'dakowerr@gmail.com' then
    return jsonb_build_object('unlimited', true, 'limit', 0, 'used', 0, 'remaining', -1, 'usage_date', v_date);
  end if;

  select coalesce(request_count, 0) into v_used
  from public.assistant_daily_usage
  where user_id = v_user and usage_date = v_date;

  return jsonb_build_object(
    'unlimited', false,
    'limit', v_limit,
    'used', coalesce(v_used, 0),
    'remaining', greatest(v_limit - coalesce(v_used, 0), 0),
    'usage_date', v_date
  );
end;
$$;

revoke all on function public.get_my_assistant_quota() from public;
grant execute on function public.get_my_assistant_quota() to authenticated;
