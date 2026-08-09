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

### Environment Doctor — live through FBD-405 / FBD-415

- Windows version/build, OS architecture, process architecture and bitness
- Android Studio installation discovery/version/build/path evidence
- Git executable/version
- Flutter SDK/version/channel
- Dart SDK version/source/PATH conflict evidence
- Java/JDK detection
- environment variables and Android SDK root validation
- command-line tools / `sdkmanager`
- `avdmanager` availability/effective path
- Android SDK license readiness status-only probe
- platform-tools / ADB
- installed Android platforms and build-tools
- Android emulator binary/version

FBD-405, FBD-414 and FBD-415 are live on `main` after branch and exact PR CI passed. Dart detection is read-only and does not change PATH. Android Studio detection does not launch Studio. Android license detection never sends acceptance input.

## Current task

`FBD-416` — Build immutable EnvironmentSnapshot — LIVE UI PROMOTION IN PROGRESS

Branch: `agent/fbd-416-main-ui`

Scope:
- port the validated FBD-416 immutable snapshot service onto current `main`
- capture environment variables once and reuse one effective PATH for Flutter, Dart and Java
- reuse the exact Android SDK root and command-line-tools results for all dependent Android detectors
- collect Windows, Android Studio, Flutter, Dart, Java and Android toolchain evidence into one immutable result
- make Environment Doctor `Run / Refresh Scan` project all non-Git cards from that one snapshot in production
- keep Git detection separate because Git is outside the FBD-416 snapshot contract
- preserve the direct-detector fallback for existing isolated ViewModel tests/construction

Safety boundary: FBD-416 only orchestrates already validated read-only/status detectors. It adds no repair, installation, license acceptance, PATH mutation, Android Studio launch or emulator/AVD lifecycle action.

## Next promotion

After FBD-416 passes branch CI, exact PR merged-tree CI and live merge, evaluate FBD-417 Environment Doctor dashboard work against the now-unified snapshot. Keep Compatibility, Build Center, Devices & Emulators, Problems, Auto Repair and Release Center disabled until their complete backend workflows are genuinely available.

## Team coordination rule

Use one main-based branch per live promotion. Preserve all completed main-line UI integrations. Require Release Build + full Tests on branch, open a PR to `main`, and require exact PR merged-tree CI before merging. Treat validated merged code/work receipts as source of truth where the task board is stale.
