$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host ""
Write-Host "Luma services deployment" -ForegroundColor Cyan
Write-Host "Supabase project: ejwjlifyfnhgsbvsjefd" -ForegroundColor DarkGray
Write-Host ""

if (-not (Get-Command npx -ErrorAction SilentlyContinue)) {
    Write-Host "Node.js is not installed or npx is unavailable." -ForegroundColor Red
    Write-Host "Install the LTS version from https://nodejs.org/ and run this file again."
    Read-Host "Press Enter to close"
    exit 1
}

Write-Host "Step 1 of 3: sign in to Supabase." -ForegroundColor Yellow
& npx --yes supabase@latest login
if ($LASTEXITCODE -ne 0) { throw "Supabase login failed" }

Write-Host ""
Write-Host "Step 2 of 3: deploy luma-assistant..." -ForegroundColor Yellow
& npx --yes supabase@latest functions deploy luma-assistant --project-ref ejwjlifyfnhgsbvsjefd
if ($LASTEXITCODE -ne 0) { throw "luma-assistant deployment failed" }

Write-Host ""
Write-Host "Step 3 of 3: deploy luma-search..." -ForegroundColor Yellow
& npx --yes supabase@latest functions deploy luma-search --no-verify-jwt --project-ref ejwjlifyfnhgsbvsjefd
if ($LASTEXITCODE -ne 0) { throw "luma-search deployment failed" }

Write-Host ""
Write-Host "SUCCESS: LumaAI and Luma Search are deployed." -ForegroundColor Green
Write-Host "Fully close Luma Browser and start it again."
Read-Host "Press Enter to close"
