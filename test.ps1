$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet build FocusFlow.slnx --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run --project tests/FocusFlow.Tests --configuration Release --no-build
exit $LASTEXITCODE
