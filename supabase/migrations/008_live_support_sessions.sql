-- Live Support sessions & message mapping for Telegram bot relay
create table if not exists public.telegram_report_messages (
  telegram_message_id bigint primary key,
  report_id uuid not null references public.feedback_reports(id) on delete cascade,
  created_at timestamptz not null default now()
);
create index if not exists telegram_report_messages_report_idx on public.telegram_report_messages(report_id);

create table if not exists public.feedback_chat_sessions (
  admin_id text primary key,
  report_id uuid not null references public.feedback_reports(id) on delete cascade,
  is_active boolean not null default true,
  updated_at timestamptz not null default now()
);
create index if not exists feedback_chat_sessions_report_idx on public.feedback_chat_sessions(report_id);

-- Backfill from existing reports
insert into public.telegram_report_messages (telegram_message_id, report_id)
select telegram_message_id, id from public.feedback_reports
where telegram_message_id is not null
on conflict (telegram_message_id) do nothing;
