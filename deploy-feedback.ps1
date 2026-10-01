param([string]$ProjectRef = "ejwjlifyfnhgsbvsjefd")
$ErrorActionPreference = "Stop"

function Invoke-Supabase {
    if (Get-Command supabase -ErrorAction SilentlyContinue) {
        & supabase @args
    } else {
        & npx supabase @args
    }
}

Invoke-Supabase link --project-ref $ProjectRef
if ($LASTEXITCODE -ne 0) { throw "Supabase link failed" }
Invoke-Supabase db push
if ($LASTEXITCODE -ne 0) { throw "Database migration failed" }
Invoke-Supabase functions deploy luma-feedback --project-ref $ProjectRef
if ($LASTEXITCODE -ne 0) { throw "luma-feedback deploy failed" }
Invoke-Supabase functions deploy luma-support --project-ref $ProjectRef --no-verify-jwt
if ($LASTEXITCODE -ne 0) { throw "luma-support deploy failed" }
Invoke-Supabase functions deploy luma-feedback-telegram --project-ref $ProjectRef --no-verify-jwt
if ($LASTEXITCODE -ne 0) { throw "Telegram webhook deploy failed" }
Write-Host "Functions deployed. Configure TELEGRAM_BOT_TOKEN, TELEGRAM_ADMIN_CHAT_ID, TELEGRAM_ADMIN_USER_ID and TELEGRAM_WEBHOOK_SECRET as Supabase secrets, then register the webhook described in supabase/README-FEEDBACK.md." -ForegroundColor Green
