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

### Environment Doctor — live through FBD-414 / FBD-415

- Windows version/build, OS architecture, process architecture and bitness
- Android Studio installation discovery/version/build/path evidence
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

FBD-414 was merged to `main` after branch and exact PR CI passed. Android Studio detection is bounded file-system inspection only and does not launch Studio. Android license detection remains diagnostic only and never sends acceptance input.

## Current task

`FBD-405` — Detect Dart SDK + version — LIVE UI PROMOTION IN PROGRESS

Branch: `agent/fbd-405-main-ui`

Scope:
- port the validated FBD-405 Dart SDK detector onto current `main`
- consume the already-live Flutter SDK result
- discover Flutter-bundled Dart plus PATH/standalone candidates
- read SDK version metadata without executing Dart
- preserve PATH preferred/shadowed candidates and Flutter/PATH mismatch evidence
- show Dart version/source/path/conflict evidence in Environment Doctor
- never change PATH or mutate Flutter/Dart installations

## Next promotion

After FBD-405 passes branch CI, exact PR merged-tree CI and live merge, select the next already validated backend capability whose dependencies are satisfied. Keep Compatibility, Build Center, Devices & Emulators, Problems, Auto Repair and Release Center disabled until their complete backend workflows are genuinely available.

## Team coordination rule

Use one main-based branch per live promotion. Preserve all completed main-line UI integrations. Require Release Build + full Tests on branch, open a PR to `main`, and require exact PR merged-tree CI before merging. Treat validated merged code/work receipts as source of truth where the task board is stale.
