# Flutter Build Doctor

Flutter Build Doctor is a Windows desktop developer tool for importing Flutter repositories, diagnosing the local Flutter/Android toolchain, applying safe repairs, and running reproducible build/release workflows.

## Current status

The repository is in the foundation phase. The .NET 8 solution, shared domain/process contracts, unit/integration test projects, and Windows CI baseline are available on the active foundation branch.

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK
- Visual Studio 2022 or newer with the .NET desktop development workload, or another environment capable of building .NET 8 WPF projects

Flutter, Android SDK, Java/JDK, Android Studio, ADB, and emulator installations are runtime dependencies that Flutter Build Doctor will diagnose and manage in later phases; they are not required for the foundation contract tests.

## Solution

Open `FlutterBuildDoctor.sln`.

Main projects:

- `src/FlutterBuildDoctor.App` — WPF application shell
- `src/FlutterBuildDoctor.Application` — use cases, orchestration, and interfaces
- `src/FlutterBuildDoctor.Domain` — dependency-free domain/status contracts
- `src/FlutterBuildDoctor.Infrastructure` — process/system implementations
- `src/FlutterBuildDoctor.Git` — Git-specific logic
- `src/FlutterBuildDoctor.Flutter` — Flutter/Dart integration
- `src/FlutterBuildDoctor.Android` — Android/JDK/SDK integration
- `src/FlutterBuildDoctor.Repair` — repair workflows
- `src/FlutterBuildDoctor.Persistence` — persistence layer
- `tests/FlutterBuildDoctor.UnitTests` — unit tests
- `tests/FlutterBuildDoctor.IntegrationTests` — integration tests

## Restore, build, and test

From the repository root:

```powershell
dotnet restore FlutterBuildDoctor.sln
dotnet build FlutterBuildDoctor.sln --configuration Release --no-restore
dotnet test FlutterBuildDoctor.sln --configuration Release --no-build
```

The same restore/build/test gates run in GitHub Actions on a Windows runner.

## Run the desktop application

From the repository root:

```powershell
dotnet run --project src/FlutterBuildDoctor.App/FlutterBuildDoctor.App.csproj
```

The application currently launches the foundation WPF shell. Product workflows are implemented incrementally according to `docs/IMPLEMENTATION_PLAN.md` and `docs/TASK_BOARD.md`.

## Architecture rules

- Domain code must not depend on WPF or concrete infrastructure.
- UI code depends on application abstractions rather than concrete infrastructure wherever practical.
- External processes must be asynchronous and cancellable.
- Machine-specific paths and secrets must never be committed.
- Long-running operations must expose structured status and sanitized logs.
- Work is delivered through task branches, validation, and pull requests.

## Project planning

- Product/architecture plan: `docs/IMPLEMENTATION_PLAN.md`
- Team task board: `docs/TASK_BOARD.md`
- Per-task execution notes: `docs/work/`

See the relevant GitHub Issue and work note for the authoritative execution state of an active task.