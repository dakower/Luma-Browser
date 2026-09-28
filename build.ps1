param(
  [string]$CodeSigningThumbprint = $env:LUMA_CODESIGN_THUMBPRINT,
  [string]$TimestampUrl = 'http://timestamp.digicert.com',
  [switch]$NoOpen
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$browserProject = Join-Path $root "src\Luma\Luma.csproj"
$setupProject = Join-Path $root "src\Luma.Setup\Luma.Setup.csproj"
$setupDir = Split-Path $setupProject -Parent
$fontDir = Join-Path $setupDir "Fonts"
$fontPath = Join-Path $fontDir "Inter.ttf"
$browserFontDir = Join-Path $root "src\Luma\Fonts"
$browserFontPath = Join-Path $browserFontDir "Inter.ttf"
$publish = Join-Path $root "publish"
$dist = Join-Path $root "dist"
$payload = Join-Path $setupDir "Payload.zip"
$payloadHash = Join-Path $setupDir "Payload.sha256"
$webViewBootstrapper = Join-Path $setupDir "Assets\MicrosoftEdgeWebview2Setup.exe"
$webViewBootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
[xml]$browserProjectXml = Get-Content $browserProject
$version = [string]$browserProjectXml.Project.PropertyGroup.Version
$parsedVersion = $null
if (-not [Version]::TryParse($version, [ref]$parsedVersion)) { throw "Invalid browser version: $version" }

function Find-SignTool {
  $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
  if ($command) { return $command.Source }
  $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
  if (Test-Path $kits) {
    $candidate = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
      Sort-Object FullName -Descending | Select-Object -First 1
    if ($candidate) { return $candidate.FullName }
  }
  throw 'signtool.exe not found. Install Windows SDK or clear CodeSigningThumbprint for an unsigned local build.'
}

function Sign-ReleaseFile([string]$Path) {
  if ([string]::IsNullOrWhiteSpace($CodeSigningThumbprint)) { return }
  $thumbprint = $CodeSigningThumbprint.Replace(' ', '')
  $signTool = Find-SignTool
  & $signTool sign /sha1 $thumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $Path
  if ($LASTEXITCODE -ne 0) { throw "Code signing failed: $Path" }
  $signed = Get-AuthenticodeSignature $Path
  if ($signed.Status -ne 'Valid') { throw "Invalid Authenticode signature on ${Path}: $($signed.Status)" }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw ".NET 8 SDK not found" }

# Catch malformed #RGB/#RRGGBB/#AARRGGBB literals before WPF BAML compilation.
Get-ChildItem (Join-Path $root "src\Luma") -Recurse -Filter *.xaml | ForEach-Object {
  $text = Get-Content $_.FullName -Raw
  foreach ($match in [regex]::Matches($text, '#[0-9A-Fa-f]+')) {
    if ($match.Value.Length -notin 4, 7, 9) { throw "Invalid XAML color $($match.Value) in $($_.FullName)" }
  }
}

Write-Host "Running Luma.Core tests..." -ForegroundColor Cyan
dotnet test (Join-Path $root "tests\Luma.Core.Tests\Luma.Core.Tests.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "Core tests failed" }

New-Item $fontDir, $browserFontDir -ItemType Directory -Force | Out-Null
if (-not (Test-Path $fontPath)) {
    Write-Host "Downloading Inter Variable font..." -ForegroundColor Cyan
    Invoke-WebRequest "https://raw.githubusercontent.com/google/fonts/main/ofl/inter/Inter%5Bopsz%2Cwght%5D.ttf" -OutFile $fontPath -UseBasicParsing
}
if (-not (Test-Path $fontPath)) { throw "Inter font download failed" }
Copy-Item $fontPath $browserFontPath -Force

New-Item (Split-Path $webViewBootstrapper -Parent) -ItemType Directory -Force | Out-Null
if (Test-Path $webViewBootstrapper) {
    $cachedSignature = Get-AuthenticodeSignature $webViewBootstrapper
    if ($cachedSignature.Status -ne 'Valid' -or $cachedSignature.SignerCertificate.Subject -notmatch 'Microsoft') {
        Remove-Item $webViewBootstrapper -Force
    }
}
if (-not (Test-Path $webViewBootstrapper)) {
    Write-Host "Downloading the official Microsoft Edge WebView2 Evergreen bootstrapper..." -ForegroundColor Cyan
    Invoke-WebRequest $webViewBootstrapperUrl -OutFile $webViewBootstrapper -UseBasicParsing
}
if (-not (Test-Path $webViewBootstrapper)) { throw "WebView2 bootstrapper download failed" }
$signature = Get-AuthenticodeSignature $webViewBootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
    Remove-Item $webViewBootstrapper -Force -ErrorAction SilentlyContinue
    throw "WebView2 bootstrapper signature is not a valid Microsoft signature"
}

Get-Process Luma -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item $publish, $dist, $payload, $payloadHash -Recurse -Force -ErrorAction SilentlyContinue
New-Item $publish, $dist -ItemType Directory -Force | Out-Null

Write-Host "Publishing compact self-contained browser..." -ForegroundColor Cyan
dotnet restore $browserProject
if ($LASTEXITCODE -ne 0) { throw "Browser restore failed" }
dotnet publish $browserProject -c Release -r win-x64 --self-contained true --no-restore -o $publish `
  -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Browser build failed" }
if (-not (Test-Path (Join-Path $publish "Luma.exe"))) { throw "Luma.exe was not produced" }
Copy-Item $webViewBootstrapper (Join-Path $publish "MicrosoftEdgeWebview2Setup.exe") -Force
if (-not (Test-Path (Join-Path $publish "MicrosoftEdgeWebview2Setup.exe"))) { throw "WebView2 bootstrapper was not added to the browser payload" }
Get-ChildItem $publish -Recurse -Include *.pdb,*.xml | Remove-Item -Force -ErrorAction SilentlyContinue
Sign-ReleaseFile (Join-Path $publish 'Luma.exe')

Write-Host "Compressing browser payload..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $payload -CompressionLevel Optimal
if (-not (Test-Path $payload)) { throw "Payload.zip was not produced" }
$hash = (Get-FileHash $payload -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path $payloadHash -Value $hash -NoNewline -Encoding ascii

Write-Host "Publishing compressed single-file installer..." -ForegroundColor Cyan
dotnet restore $setupProject
if ($LASTEXITCODE -ne 0) { throw "Installer restore failed" }
dotnet publish $setupProject -c Release -r win-x64 --self-contained true --no-restore -o $dist `
  -p:Version=$version `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false `
  -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }

$built = Join-Path $dist "LumaSetup.exe"
$final = Join-Path $dist "LumaSetup-$version-x64.exe"
if (-not (Test-Path $built)) { throw "LumaSetup.exe was not produced" }
Move-Item $built $final -Force
Get-ChildItem $dist -File | Where-Object { $_.FullName -ne $final } | Remove-Item -Force
Sign-ReleaseFile $final
$sizeMb = [Math]::Round((Get-Item $final).Length / 1MB, 1)
$installerHash = (Get-FileHash $final -Algorithm SHA256).Hash.ToLowerInvariant()
"$installerHash *$(Split-Path $final -Leaf)" | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
Write-Host "SUCCESS: $final ($sizeMb MB)" -ForegroundColor Green
Write-Host "SHA256: $installerHash" -ForegroundColor DarkGray
if (-not $NoOpen) { Start-Process explorer.exe $dist }
