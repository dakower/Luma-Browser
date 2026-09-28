-- Rich beta workflow: forum-style statuses and structured tester task results.
alter table public.feedback_reports drop constraint if exists feedback_reports_status_check;
update public.feedback_reports set status = case status
  when 'sent' then 'new'
  when 'viewed' then 'checking'
  when 'in_progress' then 'fixing'
  when 'resolved' then 'fixed'
  when 'closed' then 'fixed'
  else status end;
alter table public.feedback_reports alter column status set default 'new';
alter table public.feedback_reports add constraint feedback_reports_status_check
  check (status in ('new','checking','need_info','fixing','fixed','cannot_reproduce','duplicate'));

alter table public.tester_task_progress add column if not exists result text;
alter table public.tester_task_progress add column if not exists started_at timestamptz;
alter table public.tester_task_progress add column if not exists updated_at timestamptz not null default now();
alter table public.tester_task_progress drop constraint if exists tester_task_progress_result_check;
alter table public.tester_task_progress add constraint tester_task_progress_result_check
  check (result is null or result in ('started','works','bug'));
update public.tester_task_progress set result = case when completed then 'works' else null end where result is null;

-- A focused, idempotent starter checklist for systematic beta testing.
insert into public.tester_tasks(title,description,sort_order)
select item.title,item.description,item.sort_order
from (values
  ('Найти малоизвестного YouTube-блогера','Проверьте обычный и видеопоиск, превью и переход к найденному каналу.',30),
  ('Восстановить 20 вкладок','Откройте 20 вкладок, полностью перезапустите Luma и проверьте порядок и выбранную вкладку.',40),
  ('Импортировать данные Chrome','Проверьте импорт истории, закладок и доступных данных профиля Chrome.',50),
  ('Проверить приватное окно','Откройте приватное окно, посетите несколько сайтов и убедитесь, что данные сессии удаляются.',60),
  ('Скачать большой файл','Проверьте прогресс, паузу интерфейса, завершение и открытие большого файла.',70),
  ('Видео в полноэкранном режиме','Запустите видео, войдите и выйдите из полноэкранного режима, переключите вкладку.',80),
  ('Сменить тему и масштаб','Переключите тему Luma и проверьте интерфейс при системном масштабе 125% или выше.',90),
  ('Проверить установку обновления','Установите доступное тестовое обновление и проверьте запуск, профиль и версию.',100)
) as item(title,description,sort_order)
where not exists(select 1 from public.tester_tasks task where task.title=item.title);
