param(
    [Parameter(Mandatory=$true)][string]$PrivateKeyPath,
    [string]$Title='Luma update is ready',
    [string]$Notes='Stability and interface improvements.',
    [string]$Channel='stable',
    [string]$MinimumVersion='1.9.14',
    [switch]$Mandatory,
    [switch]$Upload,
    [string]$Bucket='luma-beta-installers',
    [string]$SourceDirectory=''
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$project=Join-Path $root 'src\Luma\Luma.csproj'
$tests=Join-Path $root 'tests\Luma.Core.Tests\Luma.Core.Tests.csproj'
$work=Join-Path $root 'update-publish'
$out=Join-Path $root 'dist\updates'
[xml]$projectXml=Get-Content $project
$version=[string]$projectXml.Project.PropertyGroup.Version
if(-not(Test-Path $PrivateKeyPath)){throw "Private key missing: $PrivateKeyPath"}

dotnet test $tests -c Release
if($LASTEXITCODE-ne 0){throw 'Tests failed'}
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item $work,$out -ItemType Directory -Force|Out-Null

if(-not[string]::IsNullOrWhiteSpace($SourceDirectory)){
    $source=[IO.Path]::GetFullPath($SourceDirectory)
    if(-not(Test-Path (Join-Path $source 'Luma.exe'))){throw "Release source missing Luma.exe: $source"}
    Copy-Item (Join-Path $source '*') $work -Recurse -Force
}else{
    dotnet restore $project
    if($LASTEXITCODE-ne 0){throw 'Restore failed'}
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $work -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false
    if($LASTEXITCODE-ne 0){throw 'Publish failed'}
}
Get-ChildItem $work -Recurse -Include *.pdb,*.xml|Remove-Item -Force -ErrorAction SilentlyContinue

$name="Luma-$version-x64.zip"
$package=Join-Path $out $name
Remove-Item $package -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $work '*') -DestinationPath $package -CompressionLevel Optimal
$hash=(Get-FileHash $package -Algorithm SHA256).Hash.ToLowerInvariant()
$size=(Get-Item $package).Length
$packageKey="updates/$Channel/$name"
$manifest=Join-Path $out 'manifest.json'
[ordered]@{
    version=$version
    channel=$Channel
    packageKey=$packageKey
    sha256=$hash
    packageSize=$size
    mandatory=[bool]$Mandatory
    minimumVersion=$MinimumVersion
    title=$Title
    notes=$Notes
    signature=''
}|ConvertTo-Json -Depth 10|Set-Content $manifest -Encoding utf8

& (Join-Path $root 'tools\sign-update.ps1') -ManifestPath $manifest -PrivateKeyPath $PrivateKeyPath
if($LASTEXITCODE-ne 0){throw 'Manifest signing failed'}

if($Upload){
    $npx=Get-Command npx.cmd -ErrorAction SilentlyContinue
    if(-not$npx){$npx=Get-Command npx -ErrorAction Stop}
    & $npx.Source wrangler r2 object put "$Bucket/$packageKey" --file $package --remote
    if($LASTEXITCODE-ne 0){throw 'Package upload failed'}
    & $npx.Source wrangler r2 object put "$Bucket/updates/$Channel/manifest.json" --file $manifest --remote
    if($LASTEXITCODE-ne 0){throw 'Manifest upload failed'}
}
Write-Host "Ready: $package" -ForegroundColor Green
Write-Host "SHA256: $hash"
Write-Host "Manifest: $manifest"
