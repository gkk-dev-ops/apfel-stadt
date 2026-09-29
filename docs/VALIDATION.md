# Validation — 2026-09-10, implementation 0.2

## Apple hardware follow-up

Real macOS and iPhone build/install results are recorded in
[APPLE-SMOKE-TEST.md](APPLE-SMOKE-TEST.md). The sections below describe the original
source-package validation environment; its lack of Unity/Xcode no longer describes
the target Mac.

## Passed in this workspace

- Pure C# domain/persistence compiled with the official .NET 8.0.425 SDK compiler and ran on
  .NET runtime 8.0.31: **24 assertions passed**. Building/costs, collisions/bounds, roads,
  move/demolition/undo, families, reward replay prevention, deep copies, validation,
  atomic save/load ordering, corruption backup, import identity, durable pending upload and gzip.
- Unity sources: **8 C# files parsed in 3 preprocessor configurations, 0 syntax errors**
  (Editor+iOS, iOS player, macOS player). This is syntax validation, not Unity API linking/compilation.
- TypeScript strict compilation passed; **12 backend tests passed**. Concurrent commits/replays,
  storage and transaction failures, delete races, restore CAS, payload validation/gzip bomb,
  quotas, ownership, pagination, HTTP contract/auth, download checksum and ETag.
- A snapshot actually emitted by the C# domain test was accepted by the TypeScript server validator.
- Production GCP adapter imports successfully with installed firebase-admin 14.3.0 dependencies.
- `npm audit --omit=dev` reported **0 vulnerabilities** after pinning the uuid transitive override.
  This is a dependency-advisory result at the time of validation, not a security certification.
- All shell scripts pass bash syntax checks. Internal Markdown links and JSON documents parse.
- Overlay installer guards: missing project, repeated install, modified file preservation and spaces in paths.
- Mock Apple orchestration checks: native compile invoked before Mac build, Mac/iOS arguments,
  failed Unity export preserves last successful pointer, failed Xcode never installs stale app,
  explicit target device, matching bundle ID. These mocks do not compile native code.
- Terraform 1.9.8 fmt/check passes. Google provider 7.46.1 initialization and lockfile were available.

## Environment limits and checks not passed here

- The combined `scripts/check.sh` cannot complete in this sandbox because the .NET CLI's process
  inspection fails in `dotnet run` with `Win32Exception: Unable to retrieve ... process or thread`.
  The same domain and syntax sources were compiled using the SDK's `csc.dll` directly and executed
  successfully. The normal CLI path still needs to run on the target Mac/CI.
- Terraform full schema validation is **not passed**: the Google provider cannot open its local Unix
  socket here (`socket: operation not permitted`). Run terraform init/validate on Mac/CI and then
  an authenticated plan in the dedicated project. No GCP plan or apply was executed.
- No Unity Editor, Apple SDK or Xcode is present. No real Unity compilation, scene generation,
  rendering, ObjC++ compile, IL2CPP linking, signing, device installation or performance benchmark.
- No Docker/Cloud Build image was built. TypeScript compilation and dependency imports do not replace it.
- GCP production adapters are not cloud integration tested. Memory transactions model expected behavior;
  real Firestore retries, Storage generations, IAM, revocation, org policy and project configuration remain to verify.
- Unity client state during real asynchronous network operations, JsonUtility/IL2CPP behavior,
  device termination and Keychain failures require actual application integration tests.
- Native share/import, iOS keyboard, touch scrolling, haptic patterns, thermal policy and audio behavior
  need physical-device checks. The UI is prototype IMGUI and has not been visually rendered here.
- Metadata backups/PITR, GC, migrations beyond v1 and public account lifecycle are future work.

No cloud resources, Apple signing identities, repositories, accounts, messages or store releases were created.
