# Development Stop Point

Recorded: 2026-08-09
Live branch: `main`
Integration branch: `agent/fbd-foundation`

## Live application status

The production-facing WPF shell on `main` contains the Git Repository Manager and Environment Doctor integrations that have passed live-main CI. Do not reimplement these capabilities on parallel feature branches; promote validated backend work into the existing UI incrementally.

### Git Repository Manager — live

- clone/import and safe refresh/reclone
- branch selection/switch support from the completed Git layer
- pull current branch with fast-forward-only safety
- dirty working tree protections
- repository identity header (remote / branch / commit)
- Windows workspace lock recovery

### Environment Doctor — live through FBD-413

- Git executable/version
- Flutter SDK/version/channel
- Java/JDK detection
- environment variables and Android SDK root validation
- command-line tools / `sdkmanager`
- `avdmanager` availability/effective path
- platform-tools / ADB
- installed Android platforms
- installed Android build-tools
- Android emulator binary/version

FBD-413 live UI promotion was merged to `main` after exact PR CI passed. The Environment Doctor XAML now visibly renders both emulator and avdmanager readiness.

## Current task

`FBD-415` — Detect Android license status — LIVE UI PROMOTION IN PROGRESS

Branch: `agent/fbd-415-main-ui`

Scope:
- port the already validated FBD-415 backend implementation onto current `main`
- register `IAndroidLicenseDetector`
- run a bounded status-only `sdkmanager --licenses` probe with stdin forced closed using `< NUL`
- classify Accepted, Pending, sdkmanager unavailable, timeout, failure, cancellation and indeterminate states
- show sdkmanager path/revision and local license-file evidence in Environment Doctor
- never send acceptance input and never modify license files

Safety boundary: this promotion is diagnostic only. It does not accept licenses. Any future acceptance operation must remain an explicit user-driven repair action with appropriate confirmation.

## Next promotion

`FBD-414` Android Studio detection remains on the team feature/draft path and should be promoted only after that backend PR is finalized and validated. Otherwise continue with the next already validated backend capability whose dependencies are satisfied.

## Team coordination rule

Before starting another live UI promotion, re-read `docs/TASK_BOARD.md`, `docs/STOP_POINT.md`, and the relevant `docs/work/FBD-xxx.md`. Preserve all completed main-line UI integrations. Use one main-based branch per promotion, require Release Build + full Tests to pass, open a PR to `main`, and require exact PR merged-tree CI before merging.

The task board contains historical stale TODO/READY states for several already completed environment tasks; treat validated merged code and work receipts as source of truth until a documentation-only reconciliation is completed.
