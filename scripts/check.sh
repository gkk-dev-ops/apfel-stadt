#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
need python3
need node
need npm
while IFS= read -r -d '' file; do bash -n "$file"; done < <(find "$TOWN_ROOT/scripts" -name '*.sh' -print0)
python3 "$TOWN_ROOT/tests/check_bundle.py"
"$TOWN_ROOT/scripts/test-domain.sh"
dotnet run --project "$TOWN_ROOT/tests/syntax/Town.Syntax.csproj" -p:UseSharedCompilation=false -- "$TOWN_ROOT"
npm --prefix "$TOWN_ROOT/apps/sync-api" test
if command -v terraform >/dev/null 2>&1; then
  CHECKPOINT_DISABLE=1 terraform -chdir="$TOWN_ROOT/infra" fmt -check -recursive
else
  printf '%s\n' 'Terraform CLI unavailable: run fmt and validate on the target machine.'
fi
