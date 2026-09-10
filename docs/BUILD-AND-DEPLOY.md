# Build, signing and GCP deployment runbook

This package contains the game/client/backend implementation. Unity, Xcode signing, device installation and GCP apply have not been run here.
Execute Apple steps on the target Mac. Execute Terraform only after reviewing its resource plan.

## 1. Toolchain

- Mac with current supported Xcode for the installed iOS version; open Xcode once to accept
  licenses and install platform support. `xcode-select -p` must select full Xcode, not only CLT.
- Unity Hub, licensed Unity 6000.3.23f1 Editor, iOS + macOS IL2CPP build support.
- The existing project pins Editor 6000.3.23f1 and URP 17.3.0. Unity generates its package lock and
  remaining ProjectSettings on first import; commit them after setup.
- Python 3, bash, Node 22+, .NET SDK 8 for domain tests, Terraform >=1.9 <2, gcloud CLI. Git and Git LFS for the final repo.
- Internet for initial dependencies/auth, then local gameplay remains possible.

## 2. Unity project and builds

Add the existing project `apps/game` to Hub. Do not create a new project over these sources. Close Editor before batch build.
Commit all Assets `.meta`, package lockfiles and ProjectSettings. In Editor use Force Text serialization
and Visible Meta Files. `.gitignore` excludes the Unity `Library` cache, not game source.

```bash
cp .env.example .env.local
# Edit UNITY_EDITOR, BUNDLE_ID, APPLE_TEAM_ID.
set -a
source .env.local
set +a
./scripts/unity.sh prepare
./scripts/unity.sh macos
./scripts/unity.sh ios
```

`prepare` creates the Town scene, materials and URP assets if missing. Existing Town scene content
is preserved; it becomes the enabled build scene. The same command is available in Editor under
Town → Prepare playable project. The source scene starts a populated playable demo at runtime.

The helper configures IL2CPP, Metal and ARM64, checks build outcome and emits a manifest.
It temporarily embeds public API settings into `Resources/TownClientConfig.json`, then removes
that generated asset. TownApp loads this resource. Empty API fields give an offline build.
For cloud tests inside Editor, create that JSON resource manually using ClientConfig's three fields;
remove it before scripted builds, which reserve the path to avoid shipping stale configuration.
`PlayerSettings` changes are saved: review/commit intended configuration after first setup.

The macos command compiles TownApple.mm to an ARM64 dylib before Unity import. The iOS export
compiles the same source in UnityFramework; the post-build hook links Foundation, UIKit, Security,
CoreHaptics and UniformTypeIdentifiers and enables ARC for the plugin. No CocoaPods dependency.

Output paths are unique beneath `builds/`; `latest-ios.txt` and `latest-macos.txt` are updated
only after success. A failed Unity export does not become the latest export. Logs are in `logs/`.
Fresh output is intentional; no destructive `rm -rf` of the Unity project or arbitrary path.
Xcode plugins/native dependencies must be ARM64 and linked for their actual platform.

For profiling a release-like build:

```bash
export BUILD_CONFIGURATION=release
./scripts/unity.sh macos
./scripts/unity.sh ios
```

This disables the Unity Development flag. Apple development signing still works. Measure this
build on the physical iPhone; do not compare Editor FPS with device FPS as if equivalent.
macOS .app is a local development artifact, not a notarized downloadable public release.
Use `open` on the app path printed by the script. Notarization is a later distribution task.

## 3. Install on physical iPhone

Sign in with the intended Apple developer account in Xcode. Connect/unlock iPhone, trust the Mac,
enable Developer Mode under Settings → Privacy & Security when offered, restart/confirm as needed.
Xcode must recognize the device and support its iOS version. First signing may require interaction
in Xcode; no credentials are passed through scripts or repository secrets.

```bash
./scripts/ios-device.sh list
./scripts/ios-device.sh build-and-install DEVICE_UDID
```

The script uses Xcode Release configuration, automatic development signing and a fresh DerivedData
folder. It installs only the newly built app via `devicectl` and launches its configured bundle ID.
If native plugins create an Xcode workspace, the script selects it. This project does not require pod install.

With free Personal Team, provisioning expires after 7 days. Rebuild/reinstall the same bundle ID
without uninstalling first; export important worlds before account/signing experiments.
Do not rely on reinstall keeping data if team/bundle changes. No App Store review is needed for
this cable workflow. Server sync and ordinary Core Haptics do not depend on iCloud entitlements.

## 4. Prepare GCP project and Terraform state

Use a dedicated, existing GCP project linked to billing. Project/organization creation and billing
attachment are deliberately outside these Terraform roots. Log in locally:

```bash
gcloud auth login
gcloud auth application-default login
# If required by your setup:
gcloud auth application-default set-quota-project YOUR_PROJECT
```

The bootstrap operator needs permission to enable APIs and create/manage the state bucket;
the infra operator additionally needs the resource-specific administration rights for Run,
Firestore, IAM, Artifact Registry, Identity Platform/API keys, Firebase Rules and budgets.
Do not give the runtime API these administrative roles. Organization policy can block allUsers
invoker; then an approved mobile ingress design is needed instead of bypassing that policy.

Bootstrap is separate because a remote state bucket must exist before it can store state:

```bash
cp infra/bootstrap/terraform.tfvars.example infra/bootstrap/terraform.tfvars
# Edit project_id, state_bucket_name and region.
./scripts/terraform.sh bootstrap init
./scripts/terraform.sh bootstrap plan
terraform -chdir=infra/bootstrap show review.tfplan
# This creates billable-capable resources; run only when ready:
terraform -chdir=infra/bootstrap apply review.tfplan
```

Bootstrap initially has local state. Immediately back it up in the new private bucket, and
repeat after any bootstrap changes. Do not commit it. A later migration of bootstrap state
into its own prefix is possible; environment state must use a different prefix.

```bash
export TF_STATE_BUCKET='YOUR_STATE_BUCKET'
gcloud storage cp infra/bootstrap/terraform.tfstate "gs://$TF_STATE_BUCKET/bootstrap-backup/terraform.tfstate"
export TF_STATE_PREFIX='town/dev'
cp infra/environment/terraform.tfvars.example infra/environment/terraform.tfvars
# Set project/region, correct billing currency and account for budget alerts if desired.
./scripts/terraform.sh environment init
./scripts/terraform.sh environment plan
terraform -chdir=infra/environment show review.tfplan
terraform -chdir=infra/environment apply review.tfplan
```

Initial `enable_api=false` provisions only the foundation. Do not change region after storing data.
If default Firestore/Identity Platform/rules already exist, import them into this state before apply;
prefer a clean dedicated project. `prevent_destroy` and service deletion protection intentionally
make an accidental destructive plan fail. The runbook does not disable these guards.
Commit generated `.terraform.lock.hcl` files. On the target Mac use `terraform providers lock`
for `darwin_arm64` and CI `linux_amd64` checksums if not already present.

## 5. Auth and backend configuration

Identity Platform is configured for email/password with end-user signup disabled. An admin
creates tester users in the console/admin API, records their UIDs, and adds them to
`allowed_user_uids` in tfvars. Do not put passwords into Terraform state or send welcome mail
from a provisioning script. The client has a login screen, no public signup.

Get the public client config:

```bash
terraform -chdir=infra/environment output -raw auth_api_key
```

Use that value as AUTH_API_KEY, project as AUTH_PROJECT_ID, and eventual api_url as API_BASE_URL.
Do not confuse this public API key with service-account credentials or refresh tokens.
Security depends on token validation and world ownership, not hiding this key.

The backend is implemented in TypeScript with Firebase Admin production adapters. Startup requires
GOOGLE_CLOUD_PROJECT, WORLDS_BUCKET and a nonempty JSON ALLOWED_USER_UIDS list; Terraform supplies them.
It refuses emulator environment variables in the production entry point. There is no insecure debug auth switch.
Read SYNC-PROTOCOL.md for limits and conflict behavior. The cloud-side IAM/auth integration test remains required.

## 6. Build API image and deploy by digest

After foundation apply and a clean committed git working tree (initialize a private/local git repository
and commit the unpacked sources first; a hosted repository is not required):

```bash
./scripts/build-api.sh
```

This submits source from `apps/sync-api` to Cloud Build; it does not deploy Cloud Run. It uses a
specified user-managed builder service account rather than guessing the project's default builder.
The human caller needs `cloudbuild.builds.create`, access to the staging bucket, and
`iam.serviceAccounts.actAs` on that builder. The optional WIF principal impersonates the builder.
The source contains no `.env.local`, Unity assets or Terraform state because of `.gcloudignore`.

Copy the returned `.../sync-api@sha256:...` into api_image, set tester UIDs and `enable_api=true`:

```bash
./scripts/terraform.sh environment plan
terraform -chdir=infra/environment show review.tfplan
terraform -chdir=infra/environment apply review.tfplan
terraform -chdir=infra/environment output -raw api_url
```

Runtime uses its own service account. Cloud Run IAM permits network invocation; endpoints
enforce Identity Platform authentication. Validate 401/403 and cross-user 404 in deployed dev.
Pin a previous digest to roll back API, but do not roll back save schema blindly. Keep the
current and previous supported container images until compatibility/restore is verified.

## 7. CI and verification

`checks.yml`: no cloud credentials; C# domain tests, Unity C# syntax checks, TypeScript API tests, and Terraform schema validation.
`unity-mac.yml`: manual, main branch only, own trusted macOS ARM64 runner labeled `town-unity`,
preinstalled Unity/Xcode and license. Configure repo variables UNITY_EDITOR, BUNDLE_ID, APPLE_TEAM_ID; optionally API_BASE_URL, AUTH_API_KEY, AUTH_PROJECT_ID.
Artifacts contain builds and logs; keep repository private. A runner must not execute untrusted
PRs while holding signing credentials. There is no automatic App Store or Terraform apply workflow.

Optional WIF root creates a GitHub trust constrained by numeric owner/repo IDs and main ref.
It grants only builder permissions, not IAM/infra admin. Wiring `google-github-actions/auth` with
its provider/service-account outputs is a later repo-specific step. Never check in a JSON SA key.
For stronger trust, use a protected GitHub environment and narrow OIDC conditions to its subject.

Run local checks:

```bash
npm ci --prefix apps/sync-api --ignore-scripts
./scripts/check.sh
terraform -chdir=infra/bootstrap init -backend=false -input=false
terraform -chdir=infra/bootstrap validate
terraform -chdir=infra/environment init -backend=false -input=false
terraform -chdir=infra/environment validate
```

No emulator/cloud test substitutes for Mac → iPhone → offline conflict → resume on real devices.
