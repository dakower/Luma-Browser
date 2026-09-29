param(
  [switch]$SourceOnly,
  [string]$InstallerPath = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$browserProject = Join-Path $root 'src\Luma\Luma.csproj'
$setupProject = Join-Path $root 'src\Luma.Setup\Luma.Setup.csproj'
[xml]$browser = Get-Content $browserProject
[xml]$setup = Get-Content $setupProject
$browserVersion = [string]$browser.Project.PropertyGroup.Version
$setupVersion = [string]$setup.Project.PropertyGroup.Version
$parsed = $null
if (-not [Version]::TryParse($browserVersion, [ref]$parsed)) { throw "Invalid browser version: $browserVersion" }
if ($browserVersion -ne $setupVersion) { throw "Version mismatch: browser=$browserVersion setup=$setupVersion" }

$required = @(
  'src\Luma\updates.json', 'src\Luma\update-public.pem', 'src\Luma\Updates\apply-update.ps1',
  'PRIVACY.md', 'LICENSE.txt', 'RELEASE-CHECKLIST.md', 'tools\publish-update.ps1'
)
foreach ($relative in $required) {
  if (-not (Test-Path (Join-Path $root $relative))) { throw "Required release file missing: $relative" }
}

$config = Get-Content (Join-Path $root 'src\Luma\updates.json') -Raw | ConvertFrom-Json
if (-not [Uri]::IsWellFormedUriString([string]$config.endpointUrl, [UriKind]::Absolute)) { throw 'Invalid update endpoint' }
if ([string]$config.channel -ne 'stable') { throw 'Release update channel must be stable' }
$publicKey = Get-Content (Join-Path $root 'src\Luma\update-public.pem') -Raw
if ($publicKey -notmatch 'BEGIN PUBLIC KEY') { throw 'Update public key is invalid' }

$bad = @()
Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'supabase') -Recurse -File |
  Where-Object { $_.Extension -in '.cs', '.xaml', '.html', '.json', '.ts', '.ps1' } |
  ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    if ($text.Contains([char]0xfffd)) { $bad += $_.FullName }
  }
if ($bad.Count) { throw "Replacement characters found: $($bad -join ', ')" }

if (-not $SourceOnly) {
  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 8 SDK not found' }
  & dotnet test (Join-Path $root 'tests\Luma.Core.Tests\Luma.Core.Tests.csproj') -c Release
  if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
}

if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
  $installer = [IO.Path]::GetFullPath($InstallerPath)
  if (-not (Test-Path $installer)) { throw "Installer missing: $installer" }
  $signature = Get-AuthenticodeSignature $installer
  if ($signature.Status -ne 'Valid') { throw "Installer signature is not valid: $($signature.Status)" }
}
Write-Host "Release preflight passed for Luma $browserVersion" -ForegroundColor Green
