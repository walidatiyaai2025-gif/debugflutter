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

### Environment Doctor — live through FBD-415

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

FBD-415 live UI promotion was merged to `main` after branch and exact PR CI passed. License detection is diagnostic only and never sends acceptance input.

## Current task

`FBD-401` — Detect Windows version/architecture — LIVE UI PROMOTION IN PROGRESS

Branch: `agent/fbd-401-main-ui`

Scope:
- port the validated FBD-401 managed Windows detector onto current `main`
- register `IWindowsRuntimeInfoSource` and `IWindowsEnvironmentDetector`
- show Windows description/version/build, OS architecture, process architecture and bitness in Environment Doctor
- use managed runtime APIs only; no WMI, PowerShell, registry mutation or external process execution

## Next promotion

`FBD-414` — Android Studio installations. Its backend PR is now merged/validated on the integration line and depends on FBD-401 Windows evidence, so promote it only after this FBD-401 main-line promotion passes exact PR CI and merges.

## Team coordination rule

Use one main-based branch per live promotion. Preserve all completed main-line UI integrations. Require Release Build + full Tests on branch, open a PR to `main`, and require exact PR merged-tree CI before merging. Treat validated merged code/work receipts as source of truth where the task board is stale.
