-- Luma beta Test Center: private reports, two-way messages, attachments and tester tasks.
create table if not exists public.feedback_reports (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references auth.users(id) on delete cascade,
  category text not null default 'bug' check (category in ('bug','crash','search','idea','other')),
  subject text not null check (char_length(subject) between 2 and 160),
  status text not null default 'sent' check (status in ('sent','viewed','in_progress','resolved','closed')),
  diagnostics jsonb not null default '{}'::jsonb,
  context jsonb not null default '{}'::jsonb,
  telegram_message_id bigint,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists feedback_reports_user_updated_idx on public.feedback_reports(user_id, updated_at desc);
create unique index if not exists feedback_reports_telegram_message_idx on public.feedback_reports(telegram_message_id) where telegram_message_id is not null;

create table if not exists public.feedback_messages (
  id uuid primary key default gen_random_uuid(),
  report_id uuid not null references public.feedback_reports(id) on delete cascade,
  sender_type text not null check (sender_type in ('tester','admin','system')),
  sender_name text not null default '',
  body text not null default '' check (char_length(body) <= 6000),
  attachment_path text,
  attachment_name text,
  attachment_mime text,
  created_at timestamptz not null default now(),
  check (char_length(body) > 0 or attachment_path is not null)
);
create index if not exists feedback_messages_report_created_idx on public.feedback_messages(report_id, created_at);

create table if not exists public.tester_tasks (
  id uuid primary key default gen_random_uuid(),
  title text not null,
  description text not null default '',
  sort_order integer not null default 0,
  active boolean not null default true,
  created_at timestamptz not null default now()
);
create table if not exists public.tester_task_progress (
  user_id uuid not null references auth.users(id) on delete cascade,
  task_id uuid not null references public.tester_tasks(id) on delete cascade,
  completed boolean not null default false,
  completed_at timestamptz,
  primary key(user_id, task_id)
);

alter table public.feedback_reports enable row level security;
alter table public.feedback_messages enable row level security;
alter table public.tester_tasks enable row level security;
alter table public.tester_task_progress enable row level security;

drop policy if exists feedback_reports_read_own on public.feedback_reports;
create policy feedback_reports_read_own on public.feedback_reports for select using (auth.uid() = user_id);
drop policy if exists feedback_messages_read_own on public.feedback_messages;
create policy feedback_messages_read_own on public.feedback_messages for select using (exists(select 1 from public.feedback_reports r where r.id=report_id and r.user_id=auth.uid()));
drop policy if exists tester_tasks_read_beta on public.tester_tasks;
create policy tester_tasks_read_beta on public.tester_tasks for select using (active and (public.is_luma_admin() or exists(select 1 from public.beta_access b where b.user_id=auth.uid() and b.status='approved')));
drop policy if exists tester_progress_read_own on public.tester_task_progress;
create policy tester_progress_read_own on public.tester_task_progress for select using (auth.uid()=user_id);

insert into storage.buckets(id,name,public,file_size_limit,allowed_mime_types)
values('feedback-files','feedback-files',false,8388608,array['image/png','image/jpeg','image/webp','text/plain','application/json','application/zip','application/pdf'])
on conflict(id) do update set public=false,file_size_limit=8388608,allowed_mime_types=excluded.allowed_mime_types;

insert into public.tester_tasks(title,description,sort_order)
select 'Проверить менеджер загрузок','Скачайте изображение и обычный файл, проверьте прогресс, открытие и папку.',10
where not exists(select 1 from public.tester_tasks where title='Проверить менеджер загрузок');
insert into public.tester_tasks(title,description,sort_order)
select 'Оценить Luma Search','Выполните три разных запроса и отметьте плохую выдачу, если она встретится.',20
where not exists(select 1 from public.tester_tasks where title='Оценить Luma Search');

grant select on public.feedback_reports, public.feedback_messages, public.tester_tasks, public.tester_task_progress to authenticated;
