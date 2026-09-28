$ErrorActionPreference = "Stop"
dotnet test "$PSScriptRoot\tests\Luma.Core.Tests\Luma.Core.Tests.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
