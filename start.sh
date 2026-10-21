#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/.."
if ! command -v dotnet >/dev/null 2>&1; then
  echo 'Bitte zuerst das .NET 10 SDK installieren: https://dotnet.microsoft.com/download/dotnet/10.0'
  exit 1
fi
echo 'FocusFlow startet unter http://localhost:5080. Mit Strg+C beenden.'
exec dotnet run --project src/FocusFlow.Web --no-launch-profile
