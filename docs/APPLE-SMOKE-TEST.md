# Apple smoke test — 2026-09-10

Offline prototype, bundle ID `pl.gkk.naszemiasteczko`.

## macOS

- Unity 6000.3.23f1, ARM64, IL2CPP, Metal, development build.
- `scripts/unity.sh prepare` and `scripts/unity.sh macos` passed.
- Town.app launched on an Apple M1 Pro Mac. The town and Polish UI rendered.
- A local `.town` save and `.town.bak` backup were created.
- App: `builds/macos.2BqVsu0q/Town.app`.
- Build log: `logs/macos.2BqVsu0q.log`.

## iPhone

- iPhone 15 Pro Max, iOS 26.6.1; Xcode 26.6.
- Unity iOS export passed: `builds/ios.Eug9PiTY/Xcode`.
- Xcode Release build succeeded with automatic development signing.
- Installation on the paired physical iPhone succeeded.
- Initial launch was denied because the phone was locked. Runtime, rendering,
  local save/reload, touch, audio and haptics remain unverified on iPhone.
- Signed app: `builds/ios-derived.OiQDLQWO/Build/Products/Release-iphoneos/Town.app`.
- Build/install log: `logs/ios-device-build.log`.
- The embedded development provisioning profile expires on 2026-09-17.
  Rebuild and reinstall with Xcode when it expires.

## Configuration and fixes

- `.env.local` configures the installed Unity Editor, bundle ID and existing
  Apple development team. All three API settings remain empty; GCP is not connected.
- Qualified `UnityEngine.ShadowQuality` to fix the starter's ambiguous type name.
- Package integrity checks now skip generated Unity cache and build directories;
  the checks passed after the desktop build.
- Backend/cloud and full device interaction tests are outside this smoke test.

## Resume

```bash
set -a
source .env.local
set +a
./scripts/unity.sh ios
./scripts/ios-device.sh list
./scripts/ios-device.sh build-and-install DEVICE_UDID
```

Keep the intended iPhone unlocked for launch. Existing local worlds are stored
separately on each device; offline mode does not synchronize them.
