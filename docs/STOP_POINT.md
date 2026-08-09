# Development Stop Point

Recorded: 2026-08-09
Live branch: `main`
Integration branch: `agent/fbd-foundation`

## Live application status

The production-facing WPF shell on `main` now contains the Git Repository Manager and Environment Doctor integrations that have passed live-main CI. Do not reimplement those capabilities on parallel feature branches; promote validated backend work into the existing UI incrementally.

### Git Repository Manager — live

- clone/import and safe refresh/reclone
- branch selection/switch support from the completed Git layer
- pull current branch with fast-forward-only safety
- dirty working tree protections
- repository identity header (remote / branch / commit)
- Windows workspace lock recovery

### Environment Doctor — live through FBD-412

- Git executable/version
- Flutter SDK/version/channel
- Java/JDK detection
- environment variables and Android SDK root validation
- command-line tools / `sdkmanager`
- platform-tools / ADB
- installed Android platforms
- installed Android build-tools
- Android emulator binary/version

`FBD-413` avdmanager detection is currently being promoted to the live UI on branch `agent/fbd-413-main-ui`. The branch also corrects the Environment Doctor XAML so the already-integrated emulator result is visibly rendered as a readiness card.

## Current task

`FBD-413` — Detect avdmanager — LIVE UI PROMOTION IN PROGRESS

Branch: `agent/fbd-413-main-ui`

Scope:
- port the already validated FBD-413 backend implementation onto current `main`
- register `IAndroidAvdManagerDetector`
- execute the read-only file-presence detector from Environment Doctor after command-line-tools discovery
- show effective path/revision and alternate-installation evidence
- keep `Devices & Emulators` disabled because AVD inventory, creation/deletion and launch/lifecycle management are not implemented yet

Safety boundary: FBD-413 does not execute avdmanager and does not create, delete, list, modify or launch AVDs.

## Next promotion

`FBD-415` — Android license readiness.

The backend is already validated on the integration line. Promote it to Environment Doctor only after FBD-413 live-main CI/merge is complete. The license detector is status-only: it runs a bounded `sdkmanager --licenses` probe with stdin closed and must never send acceptance input or modify license files.

`FBD-414` Android Studio detection currently remains on its team feature/draft path and should be promoted only after that backend PR is finalized and validated.

## Team coordination rule

Before starting another live UI promotion, re-read `docs/TASK_BOARD.md`, `docs/STOP_POINT.md`, and the relevant `docs/work/FBD-xxx.md`. Preserve all completed main-line UI integrations. Use one main-based branch per promotion, require Release Build + full Tests to pass, open a PR to `main`, and require exact PR merged-tree CI before merging.

The task board contains historical stale TODO/READY states for several already completed environment tasks; treat validated merged code and work receipts as source of truth until a documentation-only reconciliation is completed.
