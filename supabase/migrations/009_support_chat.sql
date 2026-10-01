-- Dedicated Support Chat tables (completely independent from Test Center)
create table if not exists public.support_threads (
  id uuid primary key default gen_random_uuid(),
  user_id uuid references auth.users(id) on delete set null,
  guest_id text,
  display_name text not null default 'Пользователь Luma',
  user_email text,
  app_version text,
  telegram_message_id bigint,
  status text not null default 'active',
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists support_threads_user_idx on public.support_threads(user_id, updated_at desc);
create index if not exists support_threads_guest_idx on public.support_threads(guest_id, updated_at desc);

create table if not exists public.support_messages (
  id uuid primary key default gen_random_uuid(),
  thread_id uuid not null references public.support_threads(id) on delete cascade,
  sender_type text not null check (sender_type in ('user','admin','system')),
  sender_name text not null default '',
  body text not null default '' check (char_length(body) <= 6000),
  telegram_message_id bigint,
  created_at timestamptz not null default now()
);
create index if not exists support_messages_thread_idx on public.support_messages(thread_id, created_at);

create table if not exists public.support_active_sessions (
  admin_id text primary key,
  thread_id uuid not null references public.support_threads(id) on delete cascade,
  is_active boolean not null default true,
  updated_at timestamptz not null default now()
);

create table if not exists public.telegram_support_messages (
  telegram_message_id bigint primary key,
  thread_id uuid not null references public.support_threads(id) on delete cascade,
  created_at timestamptz not null default now()
);
create index if not exists telegram_support_messages_thread_idx on public.telegram_support_messages(thread_id);

alter table public.support_threads enable row level security;
alter table public.support_messages enable row level security;
alter table public.support_active_sessions enable row level security;
alter table public.telegram_support_messages enable row level security;

-- Policies: users can read their own threads & messages; anon can read if matching guest_id
drop policy if exists support_threads_read_own on public.support_threads;
create policy support_threads_read_own on public.support_threads for select using (
  (auth.uid() is not null and auth.uid() = user_id)
);

drop policy if exists support_messages_read_own on public.support_messages;
create policy support_messages_read_own on public.support_messages for select using (
  exists (select 1 from public.support_threads t where t.id = thread_id and t.user_id = auth.uid())
);

grant select on public.support_threads, public.support_messages to authenticated, anon;
