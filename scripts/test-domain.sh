#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
need dotnet
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
dotnet run --project "$TOWN_ROOT/tests/domain/Town.Domain.Tests.csproj" -p:UseSharedCompilation=false -- "$TOWN_ROOT/tests/fixtures/csharp-world.json"
