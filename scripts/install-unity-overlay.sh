#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
project="${UNITY_PROJECT:-$TOWN_ROOT/apps/game}"
[[ -f "$project/ProjectSettings/ProjectVersion.txt" ]] || die 'Create a Universal 3D project in apps/game first.'
dest="$project/Assets/Editor/TownBuild.cs"
mkdir -p "$(dirname "$dest")"
if [[ -e "$dest" ]]; then
  cmp -s "$TOWN_ROOT/unity-overlay/Assets/Editor/TownBuild.cs" "$dest" || die 'TownBuild.cs already exists and differs; review before replacing it.'
else
  cp "$TOWN_ROOT/unity-overlay/Assets/Editor/TownBuild.cs" "$dest"
fi
printf 'Unity build overlay ready: %s\n' "$dest"
