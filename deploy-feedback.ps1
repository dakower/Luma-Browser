param([string]$ProjectRef = "ejwjlifyfnhgsbvsjefd")
$ErrorActionPreference = "Stop"
if (-not (Get-Command supabase -ErrorAction SilentlyContinue)) { throw "Install Supabase CLI first: https://supabase.com/docs/guides/local-development/cli/getting-started" }
supabase link --project-ref $ProjectRef
if ($LASTEXITCODE -ne 0) { throw "Supabase link failed" }
supabase db push
if ($LASTEXITCODE -ne 0) { throw "Database migration failed" }
supabase functions deploy luma-feedback --project-ref $ProjectRef
if ($LASTEXITCODE -ne 0) { throw "luma-feedback deploy failed" }
supabase functions deploy luma-feedback-telegram --project-ref $ProjectRef --no-verify-jwt
if ($LASTEXITCODE -ne 0) { throw "Telegram webhook deploy failed" }
Write-Host "Functions deployed. Configure TELEGRAM_BOT_TOKEN, TELEGRAM_ADMIN_CHAT_ID, TELEGRAM_ADMIN_USER_ID and TELEGRAM_WEBHOOK_SECRET as Supabase secrets, then register the webhook described in supabase/README-FEEDBACK.md." -ForegroundColor Green
