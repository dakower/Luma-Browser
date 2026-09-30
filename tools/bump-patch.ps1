$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$browserProject = Join-Path $root 'src\Luma\Luma.csproj'
$setupProject = Join-Path $root 'src\Luma.Setup\Luma.Setup.csproj'

[xml]$browser = Get-Content $browserProject
$current = [Version][string]$browser.Project.PropertyGroup.Version
$next = [Version]::new($current.Major, $current.Minor, $current.Build + 1)
$oldText = "$($current.Major).$($current.Minor).$($current.Build)"
$newText = "$($next.Major).$($next.Minor).$($next.Build)"

foreach ($file in @(
  $browserProject,
  $setupProject,
  (Join-Path $root 'src\Luma\MainWindow.Events.cs'),
  (Join-Path $root 'src\Luma.Setup\SetupWindow.xaml.cs'),
  (Join-Path $root 'src\Luma\Authentication\SupabaseAuthService.cs'),
  (Join-Path $root 'src\Luma\Updates\UpdateSystem.cs')
)) {
  $content = Get-Content $file -Raw
  [System.IO.File]::WriteAllText($file, $content, (New-Object System.Text.UTF8Encoding($false)))
}

Write-Host "Luma patch version: $oldText -> $newText" -ForegroundColor Green
Write-Host 'Run build.ps1 or release.ps1 to produce files with the new version.' -ForegroundColor DarkGray
