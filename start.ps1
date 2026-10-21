$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'Bitte zuerst das .NET 10 SDK installieren: https://dotnet.microsoft.com/download/dotnet/10.0'
    exit 1
}
Write-Host 'FocusFlow startet unter http://localhost:5080. Mit Strg+C beenden.'
dotnet run --project src/FocusFlow.Web --no-launch-profile
exit $LASTEXITCODE
