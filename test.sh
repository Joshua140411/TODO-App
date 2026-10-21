#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/.."
dotnet build FocusFlow.slnx --configuration Release
dotnet run --project tests/FocusFlow.Tests --configuration Release --no-build
