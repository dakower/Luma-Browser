param(
  [Parameter(Mandatory=$true)][string]$PrivateKeyPath,
  [string]$Title='',
  [string]$Notes='Исправлены страницы ошибок, навигация Luma Search, контекстные меню и музыкальный виджет.',
  [string]$MinimumVersion='1.9.14',
  [string]$CodeSigningThumbprint=$env:LUMA_CODESIGN_THUMBPRINT,
  [switch]$SkipBuild,
  [switch]$Upload,
  [switch]$AllowUnsigned,
  [switch]$NoOpen
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'tools\release-preflight.ps1') -SourceOnly
if($LASTEXITCODE-ne 0){throw 'Release source preflight failed'}
[xml]$projectXml=Get-Content (Join-Path $root 'src\Luma\Luma.csproj')
$version=[string]$projectXml.Project.PropertyGroup.Version
if([string]::IsNullOrWhiteSpace($Title)){$Title="Доступно обновление Luma $version"}
$installer=Join-Path $root "dist\LumaSetup-$version-x64.exe"
if(-not $SkipBuild){
  & (Join-Path $root 'build.ps1') -CodeSigningThumbprint $CodeSigningThumbprint -NoOpen
  if($LASTEXITCODE-ne 0){throw 'Installer build failed'}
}elseif(-not(Test-Path $installer)){
  throw "Existing installer not found: $installer"
}
$signature=Get-AuthenticodeSignature $installer
if(-not $AllowUnsigned -and $signature.Status-ne 'Valid'){
  throw "Stable release requires a valid Authenticode signature. Status: $($signature.Status). Set LUMA_CODESIGN_THUMBPRINT or use -AllowUnsigned only for private testing."
}
$sourceDirectory=Join-Path $root 'publish'
if(-not(Test-Path (Join-Path $sourceDirectory 'Luma.exe'))){throw "Published browser payload not found: $sourceDirectory"}
& (Join-Path $root 'tools\publish-update.ps1') -PrivateKeyPath $PrivateKeyPath -Title $Title -Notes $Notes -MinimumVersion $MinimumVersion -Upload:$Upload -SourceDirectory $sourceDirectory
if($LASTEXITCODE-ne 0){throw 'Update build failed'}
$release=Join-Path $root 'dist\release';$installerOut=Join-Path $release 'installer';$updateOut=Join-Path $release 'update'
Remove-Item $release -Recurse -Force -ErrorAction SilentlyContinue
New-Item $installerOut,$updateOut -ItemType Directory -Force|Out-Null
Copy-Item $installer $installerOut -Force
if(Test-Path (Join-Path $root 'dist\SHA256SUMS.txt')){Copy-Item (Join-Path $root 'dist\SHA256SUMS.txt') $installerOut -Force}
Copy-Item (Join-Path $root "dist\updates\Luma-$version-x64.zip") $updateOut -Force
Copy-Item (Join-Path $root 'dist\updates\manifest.json') $updateOut -Force
Write-Host 'RELEASE READY' -ForegroundColor Green
Write-Host "Installer: $installerOut"
Write-Host "R2 update files: $updateOut"
if(-not $NoOpen){Start-Process explorer.exe $release}
