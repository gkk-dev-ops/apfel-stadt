#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
require_macos
need xcrun
project="${UNITY_PROJECT:-$TOWN_ROOT/apps/game}"
source_file="$project/Assets/Town/Plugins/iOS/TownApple.mm"
out_dir="$project/Assets/Town/Plugins/macOS"
[[ -f "$source_file" ]] || die 'Missing native Apple source.'
mkdir -p "$out_dir"
xcrun clang++ -dynamiclib -arch arm64 -mmacosx-version-min=13.0 -fobjc-arc \
  -framework Foundation -framework Cocoa -framework Security \
  "$source_file" -o "$out_dir/libTownApple.dylib"
[[ -s "$out_dir/libTownApple.dylib" ]] || die 'Compiler returned without creating the native bridge.'
printf 'Built native bridge: %s\n' "$out_dir/libTownApple.dylib"
