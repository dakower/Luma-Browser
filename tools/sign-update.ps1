param(
    [Parameter(Mandatory=$true)][string]$ManifestPath,
    [Parameter(Mandatory=$true)][string]$PrivateKeyPath
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$signer=Join-Path $root 'tools\Luma.UpdateSigner\Luma.UpdateSigner.csproj'
if(-not(Test-Path $ManifestPath)){throw "Manifest missing: $ManifestPath"}
if(-not(Test-Path $PrivateKeyPath)){throw "Private key missing: $PrivateKeyPath"}
dotnet run --project $signer -c Release -- $ManifestPath $PrivateKeyPath
if($LASTEXITCODE-ne 0){throw 'Signer failed'}
