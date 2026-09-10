#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
require_macos
need xcrun
need xcodebuild
case "${1:-}" in
  list) exec xcrun devicectl list devices ;;
  build-and-install) ;;
  *) die 'Usage: scripts/ios-device.sh list | build-and-install DEVICE_UDID' ;;
esac
[[ -n "${2:-}" ]] || die 'Pass the intended physical device UDID explicitly.'
device="$2"
[[ -n "${APPLE_TEAM_ID:-}" && "$APPLE_TEAM_ID" != REPLACE* ]] || die 'Set APPLE_TEAM_ID.'
[[ -n "${BUNDLE_ID:-}" && "$BUNDLE_ID" != com.example.* ]] || die 'Set your own BUNDLE_ID before signing.'
[[ -f "$TOWN_ROOT/builds/latest-ios.txt" ]] || die 'Export iOS with scripts/unity.sh ios first.'
IFS= read -r xcode_dir < "$TOWN_ROOT/builds/latest-ios.txt"
[[ -d "$xcode_dir/Unity-iPhone.xcodeproj" ]] || die 'Missing exported Xcode project.'
# Ensure signing/install settings match the bundle identifier configured by Unity.
[[ -f "$xcode_dir/town-build.json" ]] || die 'Missing Unity build manifest.'
need python3
export TOWN_MANIFEST="$xcode_dir/town-build.json"
python3 - <<'CHECK'
import json, os, sys
with open(os.environ['TOWN_MANIFEST']) as f: m=json.load(f)
if m['bundleId'] != os.environ['BUNDLE_ID']:
 sys.exit('BUNDLE_ID changed since export. Export iOS again.')
CHECK
build_args=(-project "$xcode_dir/Unity-iPhone.xcodeproj")
# Prefer a workspace when native dependencies generate one (e.g. CocoaPods).
if [[ -d "$xcode_dir/Unity-iPhone.xcworkspace" ]]; then
  build_args=(-workspace "$xcode_dir/Unity-iPhone.xcworkspace")
fi
# Fresh DerivedData avoids installing a stale .app after a failed build.
derived="$(mktemp -d "$TOWN_ROOT/builds/ios-derived.XXXXXXXX")"
xcodebuild "${build_args[@]}" -scheme Unity-iPhone -configuration Release \
  -destination "id=$device" -derivedDataPath "$derived" \
  -allowProvisioningUpdates -allowProvisioningDeviceRegistration \
  "DEVELOPMENT_TEAM=$APPLE_TEAM_ID" CODE_SIGN_STYLE=Automatic build
# Release here is the Xcode configuration; Unity development flag was chosen at export.
app="$derived/Build/Products/Release-iphoneos/Town.app"
if [[ ! -d "$app" ]]; then
  candidates=("$derived/Build/Products/Release-iphoneos/"*.app)
  [[ "${#candidates[@]}" == 1 && -d "${candidates[0]}" ]] || die 'Expected exactly one built .app.'
  app="${candidates[0]}"
fi
xcrun devicectl device install app --device "$device" "$app"
xcrun devicectl device process launch --device "$device" "$BUNDLE_ID"
printf 'Installed: %s\n' "$app"
