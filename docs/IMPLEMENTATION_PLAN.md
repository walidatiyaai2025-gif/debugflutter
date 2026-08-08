# Flutter Build Doctor — Implementation Plan

## 1. Product Goal

Flutter Build Doctor is a Windows desktop application built with .NET 8 and WPF that accepts a Git repository URL and branch, prepares the local Flutter/Android toolchain, diagnoses environment and project issues, applies safe automated repairs, and supports build/run/release workflows from one UI.

Primary target: Windows 10/11 developers building Flutter Android applications.

## 2. Product Principles

1. Diagnose before changing anything.
2. Never apply destructive fixes without backup/rollback capability.
3. Every operation must emit structured logs and a user-readable status.
4. Environment checks must distinguish: Installed, Missing, Outdated, Incompatible, Broken, Ready.
5. Project-specific requirements override generic defaults.
6. Long-running commands must be cancellable.
7. Build/release output must be reproducible and traceable to Git branch + commit + toolchain versions.
8. Safe auto-repair is preferred; risky repairs require explicit confirmation.
9. UI must remain responsive while external processes are running.
10. Every feature requires unit/integration coverage where practical.

## 3. Proposed Architecture

Solution: `FlutterBuildDoctor.sln`

Projects:

- `src/FlutterBuildDoctor.App` — WPF shell, MVVM, views, resources.
- `src/FlutterBuildDoctor.Application` — use cases, orchestration, DTOs, interfaces.
- `src/FlutterBuildDoctor.Domain` — models, enums, rules, diagnostics, repair contracts.
- `src/FlutterBuildDoctor.Infrastructure` — Git, process runner, file system, registry, downloads, shell integration.
- `src/FlutterBuildDoctor.Flutter` — Flutter/Dart discovery, doctor parsing, pub/analyze/test/build/run.
- `src/FlutterBuildDoctor.Android` — Android SDK, Java/JDK, Gradle/AGP/Kotlin, ADB, emulator, Android Studio.
- `src/FlutterBuildDoctor.Repair` — repair catalog, backup/rollback, verification pipeline.
- `src/FlutterBuildDoctor.Persistence` — SQLite settings, histories, known issues, execution receipts.
- `tests/FlutterBuildDoctor.UnitTests`
- `tests/FlutterBuildDoctor.IntegrationTests`

Recommended stack:

- .NET 8
- WPF + MVVM
- CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting / DI / Configuration / Logging
- Serilog
- SQLite
- YamlDotNet where YAML parsing is required
- System.Text.Json
- HttpClientFactory
- xUnit

## 4. Major User Workflows

### Workflow A — Import Project
1. Enter Git URL.
2. Select/enter branch.
3. Choose local workspace.
4. Clone or update repository.
5. Detect Flutter project root.
6. Read project requirements.
7. Run environment diagnosis.
8. Show readiness score and blockers.

### Workflow B — Diagnose & Repair
1. Run system checks.
2. Run Flutter doctor.
3. Parse project compatibility requirements.
4. Build issue list.
5. Suggest safe repairs.
6. Backup affected files/configuration.
7. Apply repairs.
8. Re-run checks.
9. Report verified result.

### Workflow C — Build
1. Select Debug/Profile/Release/App Bundle.
2. Select flavor/target when available.
3. Validate prerequisites.
4. Run clean/pub get as configured.
5. Analyze/test optional gates.
6. Build.
7. Parse errors.
8. Suggest/apply fixes.
9. Retry when policy allows.
10. Store artifact + execution receipt.

### Workflow D — Emulator / Device
1. Detect ADB devices and AVDs.
2. Start selected emulator.
3. Wait for boot readiness.
4. Install/run Flutter application.
5. Stream logs.
6. Stop/restart/reinstall as required.

### Workflow E — Release
1. Validate package ID/version/signing.
2. Validate release toolchain.
3. Build APK/AAB.
4. Verify artifact location/hash/size.
5. Store release receipt.
6. Open output folder.

## 5. Phase Plan

### Phase 0 — Foundation & Team Operating Model
Goal: establish solution, conventions, CI, documentation and ownership.

Deliverables:
- solution skeleton
- coding standards
- branch/PR rules
- logging baseline
- dependency injection baseline
- test projects
- CI build

### Phase 1 — Repository Workspace Manager
Goal: reliably clone/update/switch Flutter repositories.

Deliverables:
- Git URL validation
- branch discovery/checkout
- workspace manager
- clone/fetch/pull/reset-safe workflows
- commit identity display
- repository health status

### Phase 2 — Environment Discovery & Doctor Dashboard
Goal: inventory the Windows Flutter development environment.

Checks:
- OS architecture/version
- Git
- Flutter SDK
- Dart SDK
- Java/JDK
- JAVA_HOME/PATH conflicts
- Android SDK
- ANDROID_HOME/ANDROID_SDK_ROOT
- cmdline-tools
- platform-tools/ADB
- build-tools
- installed Android platforms
- sdkmanager
- avdmanager
- emulator
- Android Studio
- Gradle availability where applicable
- licenses

### Phase 3 — Flutter Project Analyzer
Goal: determine what the selected project actually requires.

Inputs:
- pubspec.yaml
- pubspec.lock
- android/settings.gradle / settings.gradle.kts
- android/build.gradle / build.gradle.kts
- android/app/build.gradle / build.gradle.kts
- gradle-wrapper.properties
- gradle.properties
- local.properties
- AndroidManifest.xml
- Flutter metadata/version files where available

Outputs:
- min/target/compile SDK requirements
- Gradle wrapper version
- AGP version
- Kotlin version
- Java compatibility requirement
- Flutter/Dart constraints
- flavors
- targets
- applicationId
- versionName/versionCode
- signing readiness

### Phase 4 — Compatibility Engine
Goal: explain cross-tool incompatibilities before build time.

Rules cover:
- Flutter ↔ Dart
- Flutter ↔ Java
- Java ↔ Gradle
- Gradle ↔ AGP
- AGP ↔ compileSdk
- Kotlin ↔ AGP/Gradle
- plugin-specific Android requirements

Outputs:
- compatibility matrix
- blocking vs warning severity
- recommended version/action
- evidence/source from local project files

### Phase 5 — Process Execution Console
Goal: run all external tooling safely and visibly.

Capabilities:
- stdout/stderr streaming
- timestamps
- cancellation
- timeout policy
- exit codes
- working directory/environment overrides
- command history
- redaction of secrets
- structured execution receipt

### Phase 6 — Flutter Command Center
Goal: expose common Flutter operations.

Commands:
- flutter doctor -v
- flutter --version
- flutter pub get
- flutter pub outdated
- flutter clean
- flutter analyze
- flutter test
- flutter devices
- flutter emulators
- flutter run
- flutter build apk --debug
- flutter build apk --profile
- flutter build apk --release
- flutter build appbundle --release

### Phase 7 — Android SDK & Java Provisioning
Goal: detect and install missing Android/Java prerequisites.

Capabilities:
- JDK discovery
- recommended JDK selection
- local application-managed JDK option
- Android SDK package inventory
- install required platform/build-tools/platform-tools/cmdline-tools
- license acceptance workflow
- environment variable validation
- post-install verification

### Phase 8 — Emulator & Device Manager
Goal: build/run directly on Android targets.

Capabilities:
- list physical devices
- list AVDs
- detect offline/unauthorized devices
- launch emulator
- wait-for-device/boot completion
- stop emulator
- install APK
- launch application
- collect logcat
- open AVD manager / Android Studio when needed

### Phase 9 — Error Intelligence Engine
Goal: convert raw command output into actionable problems.

Categories:
- Flutter
- Dart
- Pub dependencies
- Java/JDK
- Gradle
- Android Gradle Plugin
- Kotlin
- Android SDK
- Manifest
- Resource merge
- Native libraries
- Signing
- ADB/device
- Emulator
- Network/proxy/TLS
- File locks/cache corruption
- Permissions/path issues

Each problem record contains:
- unique signature
- title
- category
- severity
- detected evidence
- probable root cause
- recommended action
- repair availability
- verification command

### Phase 10 — Auto-Repair Engine
Goal: safely fix known issues and prove the fix worked.

Repair pipeline:
1. Detect issue.
2. Check prerequisites.
3. Classify safe/risky/destructive.
4. Create backup/restore point.
5. Preview planned actions.
6. Execute repair.
7. Verify.
8. Roll back on failed verification when supported.
9. Store receipt.

Initial repair catalog:
- PATH/JAVA_HOME corrections
- missing SDK package installation
- Android license acceptance
- flutter pub get refresh
- flutter clean
- Gradle cache cleanup
- project `.gradle` cleanup
- stale build directory cleanup
- incompatible Gradle wrapper recommendation/update
- compatible Java selection
- missing local.properties generation
- SDK path correction
- ADB restart
- emulator restart
- dependency refresh

### Phase 11 — Build Orchestrator
Goal: implement one-click deterministic build pipelines.

Pipelines:
- Quick Build
- Clean Build
- Analyze + Build
- Test + Build
- Build & Run
- Release APK
- Release AAB

Features:
- preflight checks
- selectable gates
- retry policy
- auto-fix between retries
- artifact discovery
- build duration
- build history

### Phase 12 — Release Center
Goal: validate production release readiness.

Checks:
- release signing
- keystore presence without exposing secrets
- version name/code
- application ID
- release manifest
- SDK compatibility
- artifact creation
- artifact SHA-256
- file size
- output path

### Phase 13 — Android Studio / Shell Integration
Goal: bridge to existing developer tools.

Actions:
- open project in Android Studio
- open android module
- open project directory
- open output directory
- open terminal in project
- open logs
- copy command

### Phase 14 — Persistence, History & Profiles
Goal: make repeated work fast and auditable.

Persist:
- repositories
- branches/workspaces
- tool locations
- preferred JDK
- build profiles
- last device/emulator
- diagnostics history
- repair history
- build history
- release receipts

### Phase 15 — UX Polish & Accessibility
Goal: production-quality desktop UX.

Screens:
- Home
- Projects
- Environment Doctor
- Project Requirements
- Compatibility Matrix
- Build Center
- Devices & Emulators
- Problems
- Auto Repair
- Release Center
- History
- Settings

UX requirements:
- responsive WPF layout
- dark/light themes
- severity icons
- progress/status timeline
- copyable errors
- searchable logs
- filtering
- keyboard accessibility
- clear destructive-action confirmation

### Phase 16 — Update & Distribution
Goal: install and update Flutter Build Doctor itself.

Deliverables:
- Windows installer
- versioning
- portable build option
- update check
- release notes
- signed package strategy

### Phase 17 — QA, Hardening & RC
Goal: release candidate quality.

Test matrix:
- Windows 10/11
- clean machine
- partial Flutter installation
- Java 11/17/21 conflict cases
- missing Android SDK packages
- corrupted Gradle cache
- no emulator
- physical device
- multiple Flutter projects
- Gradle Groovy and Kotlin DSL
- debug/profile/release/AAB
- failure recovery and rollback

## 6. Milestones

- M0: Foundation ready
- M1: Git + Environment Doctor + command console
- M2: Project Analyzer + Compatibility Engine
- M3: Flutter build center + emulator/device manager
- M4: Error Intelligence + Auto Repair
- M5: Release Center + history + Android Studio integration
- M6: Installer + QA hardening + RC

## 7. Team Roles

Suggested team:

- Tech Lead / Architect — architecture, integration, code review, complex compatibility rules.
- WPF/MVVM Developer — UI shell, dashboards, build center, state/progress UX.
- Systems Engineer — process execution, Windows environment, registry, PATH, downloads.
- Flutter/Android Toolchain Engineer — Flutter, Gradle, AGP, Kotlin, SDK, emulator logic.
- Automation/Repair Engineer — diagnostic signatures, safe fix recipes, rollback/verification.
- QA/Automation Engineer — test matrix, regression suite, machine-state scenarios.
- DevOps/Release Engineer — CI, packaging, versioning, installer/release pipeline.

A 4-person team can combine roles:
1. Lead + backend/application architecture
2. WPF/MVVM
3. Flutter/Android + repairs
4. QA/DevOps

## 8. Definition of Done for Every Task

A task is Done only when:
- implementation compiles
- relevant tests pass
- errors are logged with actionable context
- cancellation/failure behavior is handled where relevant
- user-facing state is represented in UI or DTO contract
- no secrets are logged
- documentation is updated if behavior/configuration changes
- code is merged through review

## 9. Out of Scope for Initial RC

- iOS build provisioning on macOS
- cloud build farm
- direct Play Store publishing
- remote device farms
- arbitrary AI-generated code modification
- automatic destructive edits without backup/verification

These can be later roadmap items after the Windows Android RC is stable.
