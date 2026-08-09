# Development Stop Point

Recorded: 2026-08-09
Live branch: `main`
Integration branch: `agent/fbd-foundation`

## Live application status

The production-facing WPF shell on `main` contains the Git Repository Manager and Environment Doctor integrations that have passed live-main CI. Preserve them and promote validated backend work incrementally.

### Git Repository Manager — live

- clone/import and safe refresh/reclone
- branch selection/switch support
- pull current branch with fast-forward-only safety
- dirty working tree protection
- repository identity header
- Windows workspace lock recovery

### Environment Doctor — live through FBD-401 / FBD-415

- Windows version/build, OS architecture, process architecture and bitness
- Git executable/version
- Flutter SDK/version/channel
- Java/JDK detection
- environment variables and Android SDK root validation
- command-line tools / `sdkmanager`
- `avdmanager` availability/effective path
- Android SDK license readiness status-only probe
- platform-tools / ADB
- installed Android platforms and build-tools
- Android emulator binary/version

FBD-401 and FBD-415 live UI promotions were merged to `main` after branch and exact PR CI passed. License detection remains diagnostic only and never sends acceptance input.

## Current task

`FBD-414` — Detect Android Studio installations — LIVE UI PROMOTION IN PROGRESS

Branch: `agent/fbd-414-main-ui`

Scope:
- port the validated FBD-414 Android Studio detector onto current `main`
- consume the already-live FBD-401 Windows evidence
- search bounded known Windows installation roots in Program Files, Program Files (x86), LocalAppData Programs and JetBrains Toolbox
- discover `studio64.exe` / `studio.exe`
- parse `product-info.json` first with `build.txt` and executable-version fallbacks
- preserve multiple installations and metadata/source evidence
- show Android Studio readiness, version/build and executable paths in Environment Doctor

Safety boundary: FBD-414 performs bounded file-system inspection only. It does not launch Android Studio, execute Studio binaries, install plugins, mutate SDK configuration, or alter Toolbox state.

## Next promotion

After FBD-414 passes branch CI, exact PR merged-tree CI and live merge, select the next already validated backend capability whose dependencies are satisfied. Keep Compatibility, Build Center, Devices & Emulators, Problems, Auto Repair and Release Center disabled until their complete backend workflows are genuinely available.

## Team coordination rule

Use one main-based branch per live promotion. Preserve all completed main-line UI integrations. Require Release Build + full Tests on branch, open a PR to `main`, and require exact PR merged-tree CI before merging. Treat validated merged code/work receipts as source of truth where the task board is stale.
