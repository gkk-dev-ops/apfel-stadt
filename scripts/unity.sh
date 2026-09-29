#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
action="${1:-}"
case "$action" in
  prepare|smoke) method='Town.Editor.TownBuild.PrepareGame'; target='StandaloneOSX' ;;
  macos) method='Town.Editor.TownBuild.MacOS'; target='StandaloneOSX' ;;
  ios) method='Town.Editor.TownBuild.IOS'; target='iOS' ;;
  *) die 'Usage: scripts/unity.sh prepare|macos|ios' ;;
esac
require_macos
[[ -n "${UNITY_EDITOR:-}" && -x "$UNITY_EDITOR" ]] || die 'Set UNITY_EDITOR to the exact Unity executable installed by Hub.'
project="${UNITY_PROJECT:-$TOWN_ROOT/apps/game}"
[[ -f "$project/ProjectSettings/ProjectVersion.txt" ]] || die 'Unity project not initialized.'
[[ -f "$project/Assets/Editor/TownBuild.cs" ]] || die 'Run scripts/install-unity-overlay.sh first.'
case "${BUILD_CONFIGURATION:-development}" in development|release) ;; *) die 'BUILD_CONFIGURATION must be development or release.' ;; esac
if [[ "$action" == macos ]]; then "$TOWN_ROOT/scripts/build-native-macos.sh"; fi
# No automatic sourcing: .env.local is executable shell code; source it intentionally.
mkdir -p "$TOWN_ROOT/builds" "$TOWN_ROOT/logs"
run_dir="$(mktemp -d "$TOWN_ROOT/builds/${action}.XXXXXXXX")"
export TOWN_BUILD_OUTPUT="$run_dir"
export BUILD_CONFIGURATION="${BUILD_CONFIGURATION:-development}"
export TOWN_EXPECTED_UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "$project/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
log_file="$TOWN_ROOT/logs/$(basename "$run_dir").log"
"$UNITY_EDITOR" -batchmode -quit -projectPath "$project" -buildTarget "$target" \
  -executeMethod "$method" -logFile "$log_file"
if [[ "$action" == ios ]]; then
  [[ -d "$run_dir/Xcode/Unity-iPhone.xcodeproj" ]] || die "Unity reported success but Xcode export is missing. See $log_file"
  printf '%s\n' "$run_dir/Xcode" > "$TOWN_ROOT/builds/latest-ios.txt"
elif [[ "$action" == macos ]]; then
  [[ -d "$run_dir/Apfel Stadt.app" ]] || die "Unity reported success but app is missing. See $log_file"
  printf '%s\n' "$run_dir/Apfel Stadt.app" > "$TOWN_ROOT/builds/latest-macos.txt"
fi
printf 'Output: %s\nLog: %s\n' "$run_dir" "$log_file"
