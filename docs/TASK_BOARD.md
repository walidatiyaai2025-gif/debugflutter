# Flutter Build Doctor — Team Task Board

Statuses: `TODO` / `READY` / `IN PROGRESS` / `BLOCKED` / `REVIEW` / `DONE`

Priorities: `P0 Critical` / `P1 High` / `P2 Medium` / `P3 Low`

---

## EPIC FBD-000 — Foundation

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-001 | Create .NET 8 solution and project structure | Tech Lead | P0 | — | DONE | Solution builds with all planned projects and tests |
| FBD-002 | Add CommunityToolkit.Mvvm + Generic Host + DI | Tech Lead | P0 | FBD-001 | READY | App starts through DI and ViewModels resolve from container |
| FBD-003 | Implement Serilog logging baseline | Systems Engineer | P0 | FBD-001 | TODO | File + in-app structured logs with timestamps and levels |
| FBD-004 | Add global exception handling | Tech Lead | P0 | FBD-002,FBD-003 | TODO | UI/thread/task exceptions captured and surfaced safely |
| FBD-005 | Define Domain enums/status contracts | Tech Lead | P0 | FBD-001 | TODO | Shared Ready/Missing/Broken/Incompatible/etc. contracts compile |
| FBD-006 | Add unit + integration test projects | QA | P0 | FBD-001 | TODO | Tests execute from CLI and CI |
| FBD-007 | Add editorconfig/analyzers/code conventions | Tech Lead | P1 | FBD-001 | TODO | Build uses agreed style/analyzer configuration |
| FBD-008 | Add GitHub Actions CI for restore/build/test | DevOps | P0 | FBD-006 | TODO | PR CI builds solution and runs tests |
| FBD-009 | Write contributor/branch/PR guide | Tech Lead | P1 | FBD-001 | TODO | CONTRIBUTING.md exists and defines workflow |
| FBD-010 | Add application version/build identity service | DevOps | P1 | FBD-001 | TODO | UI/logs can display product version + commit/build identity |

---

## EPIC FBD-100 — WPF Application Shell

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-101 | Create MainWindow shell and navigation | WPF Developer | P0 | FBD-002 | TODO | Navigation works without recreating state incorrectly |
| FBD-102 | Implement Home dashboard | WPF Developer | P1 | FBD-101 | TODO | Shows project/environment/build summary |
| FBD-103 | Create reusable status badge component | WPF Developer | P1 | FBD-005,FBD-101 | TODO | Supports Ready/Warning/Error/Running/etc. states |
| FBD-104 | Create operation progress/timeline component | WPF Developer | P1 | FBD-101 | TODO | Long tasks show stages, duration and current status |
| FBD-105 | Create searchable log viewer | WPF Developer | P1 | FBD-003,FBD-101 | TODO | Live append, search, filter, copy, clear |
| FBD-106 | Implement global notification/toast service | WPF Developer | P1 | FBD-101 | TODO | Info/success/warning/error messages available globally |
| FBD-107 | Add dark/light themes | WPF Developer | P2 | FBD-101 | TODO | Theme switches at runtime and persists |
| FBD-108 | Add settings screen | WPF Developer | P1 | FBD-101 | TODO | Core paths/preferences editable |
| FBD-109 | Implement confirmation dialog for risky actions | WPF Developer | P0 | FBD-101 | TODO | Risk summary shown before destructive operations |
| FBD-110 | Add cancellation UI for active operations | WPF Developer | P0 | FBD-104 | TODO | User can cancel supported long-running tasks |

---

## EPIC FBD-200 — Process Execution Engine

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-201 | Define process request/result contracts | Systems Engineer | P0 | FBD-005 | TODO | Contracts support executable,args,cwd,env,timeout |
| FBD-202 | Implement async process runner | Systems Engineer | P0 | FBD-201 | TODO | Runs command without blocking UI |
| FBD-203 | Stream stdout/stderr events | Systems Engineer | P0 | FBD-202 | TODO | Output visible while command executes |
| FBD-204 | Implement cancellation/kill process tree | Systems Engineer | P0 | FBD-202 | TODO | Cancellation terminates child process tree safely |
| FBD-205 | Add timeout support | Systems Engineer | P1 | FBD-202 | TODO | Timed out operation returns explicit status |
| FBD-206 | Add environment overrides | Systems Engineer | P1 | FBD-202 | TODO | Per-command PATH/JAVA_HOME/etc. supported |
| FBD-207 | Implement secret/redaction filter | Systems Engineer | P0 | FBD-203 | TODO | Password/token/keystore secrets never logged |
| FBD-208 | Create execution receipt model | Systems Engineer | P1 | FBD-202 | TODO | Records command identity, exit code, duration, result |
| FBD-209 | Persist command history | Persistence Developer | P2 | FBD-208 | TODO | Previous executions are queryable |
| FBD-210 | Add process runner tests with fake commands | QA | P0 | FBD-202 | TODO | Success/failure/cancel/timeout covered |

---

## EPIC FBD-300 — Git Repository Manager

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-301 | Detect Git executable/version | Systems Engineer | P0 | FBD-202 | TODO | Git status shown with path/version |
| FBD-302 | Validate repository URL | Systems Engineer | P0 | FBD-301 | TODO | Invalid URL blocked with clear reason |
| FBD-303 | Clone repository | Systems Engineer | P0 | FBD-301 | TODO | Clone supports target workspace and progress logs |
| FBD-304 | Fetch branches | Systems Engineer | P0 | FBD-303 | TODO | Remote/local branch list available |
| FBD-305 | Checkout/switch branch | Systems Engineer | P0 | FBD-304 | TODO | Selected branch checked out safely |
| FBD-306 | Pull current branch | Systems Engineer | P1 | FBD-305 | TODO | Fast-forward/update result clearly reported |
| FBD-307 | Detect dirty working tree | Systems Engineer | P0 | FBD-303 | TODO | App never overwrites dirty repo silently |
| FBD-308 | Display current commit/branch/remote | WPF Developer | P1 | FBD-305 | TODO | Project header shows exact Git identity |
| FBD-309 | Implement safe refresh/reclone workflow | Systems Engineer | P1 | FBD-307 | TODO | Existing folder can be backed up/recreated safely |
| FBD-310 | Repository Manager UI | WPF Developer | P0 | FBD-303,FBD-305 | TODO | User enters Git URL, branch, workspace and imports project |

---

## EPIC FBD-400 — Windows Environment Discovery

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-401 | Detect Windows version/architecture | Systems Engineer | P1 | FBD-005 | TODO | OS info represented in environment snapshot |
| FBD-402 | Implement PATH executable discovery utility | Systems Engineer | P0 | FBD-201 | TODO | Finds all matches and identifies conflicts |
| FBD-403 | Read relevant environment variables | Systems Engineer | P0 | FBD-402 | TODO | PATH,JAVA_HOME,ANDROID_HOME,ANDROID_SDK_ROOT captured |
| FBD-404 | Detect Flutter SDK + version | Flutter Engineer | P0 | FBD-402 | TODO | Flutter path/version/channel detected |
| FBD-405 | Detect Dart SDK + version | Flutter Engineer | P0 | FBD-404 | TODO | Dart version/path detected and linked to Flutter where applicable |
| FBD-406 | Detect Java installations | Android Engineer | P0 | FBD-402 | TODO | All likely JDKs discovered with version/vendor/path |
| FBD-407 | Detect Android SDK roots | Android Engineer | P0 | FBD-403 | TODO | SDK root candidates validated |
| FBD-408 | Detect sdkmanager/cmdline-tools | Android Engineer | P0 | FBD-407 | TODO | Installed cmdline-tools versions reported |
| FBD-409 | Detect platform-tools/ADB | Android Engineer | P0 | FBD-407 | TODO | ADB path/version/status reported |
| FBD-410 | Detect installed platforms | Android Engineer | P0 | FBD-408 | TODO | android-XX platforms enumerated |
| FBD-411 | Detect installed build-tools | Android Engineer | P0 | FBD-408 | TODO | Build-tools versions enumerated |
| FBD-412 | Detect emulator binary | Android Engineer | P1 | FBD-407 | TODO | Emulator path/version reported |
| FBD-413 | Detect avdmanager | Android Engineer | P1 | FBD-407 | TODO | AVD manager availability reported |
| FBD-414 | Detect Android Studio installations | Systems Engineer | P1 | FBD-401 | TODO | Studio executable/version/paths detected |
| FBD-415 | Detect Android license status | Android Engineer | P0 | FBD-408 | TODO | License readiness shown without hanging UI |
| FBD-416 | Build immutable EnvironmentSnapshot | Tech Lead | P0 | FBD-404:FBD-415 | TODO | One object represents complete current environment |
| FBD-417 | Environment Doctor dashboard UI | WPF Developer | P0 | FBD-416 | TODO | Each component shows state/path/version/action |
| FBD-418 | Refresh environment action | WPF Developer | P1 | FBD-416,FBD-417 | TODO | Re-scan updates dashboard without restarting app |

---

## EPIC FBD-500 — Flutter Doctor & Command Parsing

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-501 | Execute `flutter doctor -v` | Flutter Engineer | P0 | FBD-404,FBD-202 | TODO | Raw command executed and captured |
| FBD-502 | Parse Flutter Doctor sections | Flutter Engineer | P0 | FBD-501 | TODO | Flutter/Android/Studio/device statuses become structured records |
| FBD-503 | Preserve unknown doctor output | Flutter Engineer | P1 | FBD-502 | TODO | Parser degrades gracefully after Flutter output changes |
| FBD-504 | Run `flutter --version` structured probe | Flutter Engineer | P0 | FBD-404 | TODO | Flutter/Dart/channel/framework revision parsed |
| FBD-505 | Doctor UI detail panel | WPF Developer | P1 | FBD-502 | TODO | User sees raw evidence plus actionable summary |
| FBD-506 | Flutter doctor parser fixture tests | QA | P0 | FBD-502 | TODO | Multiple sample outputs/regressions covered |

---

## EPIC FBD-600 — Flutter Project Analyzer

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-601 | Locate Flutter project root | Flutter Engineer | P0 | FBD-303 | TODO | Finds pubspec.yaml and validates Flutter project |
| FBD-602 | Parse pubspec.yaml | Flutter Engineer | P0 | FBD-601 | TODO | SDK constraints, package name and dependencies read |
| FBD-603 | Parse pubspec.lock | Flutter Engineer | P1 | FBD-601 | TODO | Locked package versions available |
| FBD-604 | Detect Groovy vs Kotlin Gradle DSL | Android Engineer | P0 | FBD-601 | TODO | Analyzer supports `.gradle` and `.gradle.kts` |
| FBD-605 | Parse Gradle wrapper version | Android Engineer | P0 | FBD-601 | TODO | distributionUrl/version parsed |
| FBD-606 | Parse AGP version | Android Engineer | P0 | FBD-604 | TODO | Modern/legacy plugin declaration patterns supported |
| FBD-607 | Parse Kotlin plugin version | Android Engineer | P0 | FBD-604 | TODO | Kotlin version detected when explicit |
| FBD-608 | Parse compileSdk/minSdk/targetSdk | Android Engineer | P0 | FBD-604 | TODO | Static values and common Flutter references handled |
| FBD-609 | Parse applicationId/namespace | Android Engineer | P0 | FBD-604 | TODO | Android identifiers exposed |
| FBD-610 | Parse versionName/versionCode | Flutter Engineer | P1 | FBD-602,FBD-604 | TODO | Effective release version represented |
| FBD-611 | Detect flavors | Flutter Engineer | P1 | FBD-604 | TODO | Product flavors enumerated where configured |
| FBD-612 | Detect common Dart entry targets | Flutter Engineer | P1 | FBD-601 | TODO | main.dart and likely flavor entrypoints shown |
| FBD-613 | Parse local.properties SDK/Flutter paths | Android Engineer | P0 | FBD-601 | TODO | Missing/invalid local paths detected |
| FBD-614 | Detect signing configuration readiness | Android Engineer | P1 | FBD-604 | TODO | Reports readiness without reading/logging secret values |
| FBD-615 | Build ProjectRequirements model | Tech Lead | P0 | FBD-602:FBD-614 | TODO | Unified immutable requirements model |
| FBD-616 | Project Requirements UI | WPF Developer | P1 | FBD-615 | TODO | Requirements displayed with source/evidence |
| FBD-617 | Analyzer fixture test repository set | QA | P0 | FBD-615 | TODO | Groovy/KTS/legacy/modern cases covered |

---

## EPIC FBD-700 — Compatibility Engine

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-701 | Define compatibility rule interface | Tech Lead | P0 | FBD-416,FBD-615 | TODO | Rules accept environment + project requirements |
| FBD-702 | Java ↔ Gradle compatibility rules | Android Engineer | P0 | FBD-701 | TODO | Known incompatible combinations flagged |
| FBD-703 | Gradle ↔ AGP compatibility rules | Android Engineer | P0 | FBD-701 | TODO | Blockers/recommended versions returned |
| FBD-704 | AGP ↔ compileSdk rules | Android Engineer | P0 | FBD-701 | TODO | Insufficient AGP/SDK combinations flagged |
| FBD-705 | Kotlin ↔ Gradle/AGP rules | Android Engineer | P1 | FBD-701 | TODO | Major compatibility conflicts detected |
| FBD-706 | Flutter ↔ Dart constraint validation | Flutter Engineer | P0 | FBD-701 | TODO | Project Dart SDK constraint validated |
| FBD-707 | Required Android platform/build-tools rules | Android Engineer | P0 | FBD-701 | TODO | Missing project-required SDK packages identified |
| FBD-708 | Java selection recommendation engine | Android Engineer | P0 | FBD-702 | TODO | Best installed JDK chosen or missing version recommended |
| FBD-709 | Severity/blocker scoring | Tech Lead | P0 | FBD-701 | TODO | Results distinguish blocking errors/warnings/info |
| FBD-710 | Environment readiness score | Tech Lead | P1 | FBD-709 | TODO | Readiness summary based on explicit rules |
| FBD-711 | Compatibility Matrix UI | WPF Developer | P0 | FBD-702:FBD-710 | TODO | Current/required/recommended versions visible |
| FBD-712 | Compatibility rule test matrix | QA | P0 | FBD-702:FBD-708 | TODO | Representative Java/Gradle/AGP/SDK cases covered |

---

## EPIC FBD-800 — Flutter Command Center

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-801 | Command builder abstraction | Flutter Engineer | P0 | FBD-202,FBD-615 | TODO | Safe argument construction without shell-string bugs |
| FBD-802 | Implement flutter pub get | Flutter Engineer | P0 | FBD-801 | TODO | Exit/result/log captured |
| FBD-803 | Implement flutter clean | Flutter Engineer | P0 | FBD-801 | TODO | Command available with warning/progress |
| FBD-804 | Implement flutter analyze | Flutter Engineer | P0 | FBD-801 | TODO | Diagnostics parsed and summarized |
| FBD-805 | Implement flutter test | Flutter Engineer | P1 | FBD-801 | TODO | Test result status captured |
| FBD-806 | Implement flutter pub outdated | Flutter Engineer | P2 | FBD-801 | TODO | Dependency update information surfaced |
| FBD-807 | Implement Debug APK build | Flutter Engineer | P0 | FBD-801 | TODO | APK built and artifact discovered |
| FBD-808 | Implement Profile APK build | Flutter Engineer | P1 | FBD-801 | TODO | Profile APK pipeline works |
| FBD-809 | Implement Release APK build | Flutter Engineer | P0 | FBD-801 | TODO | Release APK artifact found |
| FBD-810 | Implement Release AAB build | Flutter Engineer | P0 | FBD-801 | TODO | Release AAB artifact found |
| FBD-811 | Add flavor/target arguments | Flutter Engineer | P1 | FBD-611,FBD-612,FBD-801 | TODO | Selected flavor/target included correctly |
| FBD-812 | Build Center UI | WPF Developer | P0 | FBD-807:FBD-811 | TODO | User can select mode/options and build |
| FBD-813 | Artifact result panel | WPF Developer | P1 | FBD-807 | TODO | Path,size,type,duration and open-folder actions displayed |

---

## EPIC FBD-900 — Android SDK & JDK Provisioning

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-901 | Define downloadable tool/package contracts | Systems Engineer | P0 | FBD-005 | TODO | Package install requests are declarative/auditable |
| FBD-902 | Implement resilient HTTP download service | Systems Engineer | P0 | FBD-901 | TODO | Progress,cancel,temp file,checksum supported |
| FBD-903 | Android SDK package install wrapper | Android Engineer | P0 | FBD-408,FBD-902 | TODO | sdkmanager install command structured and verified |
| FBD-904 | Install missing Android platform | Android Engineer | P0 | FBD-707,FBD-903 | TODO | Required android-XX package installed and re-detected |
| FBD-905 | Install missing build-tools | Android Engineer | P0 | FBD-707,FBD-903 | TODO | Required build-tools installed and verified |
| FBD-906 | Install/update platform-tools | Android Engineer | P1 | FBD-903 | TODO | ADB package repaired/updated |
| FBD-907 | Install/update cmdline-tools | Android Engineer | P1 | FBD-903 | TODO | Latest required cmdline-tools installed safely |
| FBD-908 | Android license acceptance flow | Android Engineer | P0 | FBD-415 | TODO | User can accept licenses and app verifies completion |
| FBD-909 | JDK install/provider abstraction | Systems Engineer | P0 | FBD-708,FBD-902 | TODO | Supports managed JDK installation without overwriting unrelated JDKs |
| FBD-910 | Managed JDK 17 installation | Systems Engineer | P0 | FBD-909 | TODO | JDK 17 can be installed into app-managed tools folder |
| FBD-911 | Managed JDK 21 installation | Systems Engineer | P1 | FBD-909 | TODO | JDK 21 can be installed into app-managed tools folder |
| FBD-912 | Per-build Java selection | Android Engineer | P0 | FBD-708,FBD-910 | TODO | Build uses selected JAVA_HOME without global mutation |
| FBD-913 | Environment variable repair planner | Systems Engineer | P1 | FBD-403 | TODO | Suggests changes and separates process/user/system scope |
| FBD-914 | Tool installation UI/progress | WPF Developer | P1 | FBD-902,FBD-903,FBD-909 | TODO | Downloads/installs show progress and final verification |

---

## EPIC FBD-1000 — Device & Emulator Manager

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-1001 | Parse `adb devices -l` | Android Engineer | P0 | FBD-409,FBD-202 | TODO | Physical/emulated device records generated |
| FBD-1002 | Detect unauthorized/offline devices | Android Engineer | P0 | FBD-1001 | TODO | User gets specific remediation state |
| FBD-1003 | Enumerate Flutter devices | Flutter Engineer | P1 | FBD-801 | TODO | Flutter device identifiers available |
| FBD-1004 | Enumerate AVDs | Android Engineer | P0 | FBD-412,FBD-413 | TODO | Configured AVD names listed |
| FBD-1005 | Launch selected emulator | Android Engineer | P0 | FBD-1004 | TODO | Selected AVD launches non-blocking |
| FBD-1006 | Wait for ADB readiness | Android Engineer | P0 | FBD-1005 | TODO | Boot process waits with timeout/cancel |
| FBD-1007 | Wait for Android boot completion | Android Engineer | P0 | FBD-1006 | TODO | Does not run app before system boot completes |
| FBD-1008 | Stop/restart emulator | Android Engineer | P1 | FBD-1005 | TODO | Selected emulator can be stopped/restarted |
| FBD-1009 | Implement `flutter run -d` | Flutter Engineer | P0 | FBD-1001,FBD-801 | TODO | App runs on selected target |
| FBD-1010 | APK install via ADB | Android Engineer | P1 | FBD-1001,FBD-807 | TODO | Built APK can be installed/reinstalled |
| FBD-1011 | Launch Android package via ADB | Android Engineer | P1 | FBD-1001,FBD-609 | TODO | Installed app can be launched |
| FBD-1012 | Capture/filter logcat | Android Engineer | P1 | FBD-1001 | TODO | App/device runtime errors can be streamed |
| FBD-1013 | Devices & Emulators UI | WPF Developer | P0 | FBD-1001:FBD-1012 | TODO | Device state/start/run actions available |

---

## EPIC FBD-1100 — Error Intelligence

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-1101 | Define ProblemRecord and ErrorSignature contracts | Repair Engineer | P0 | FBD-005 | TODO | Structured error model supports category/evidence/fix |
| FBD-1102 | Build line/block normalization pipeline | Repair Engineer | P0 | FBD-203,FBD-1101 | TODO | Noisy paths/timestamps normalized for matching |
| FBD-1103 | Java/JDK error signatures | Repair Engineer | P0 | FBD-1102 | TODO | Common Java-version/JAVA_HOME failures recognized |
| FBD-1104 | Gradle error signatures | Repair Engineer | P0 | FBD-1102 | TODO | Wrapper/cache/daemon/dependency failures recognized |
| FBD-1105 | AGP compatibility signatures | Repair Engineer | P0 | FBD-1102 | TODO | Unsupported AGP/SDK/Gradle errors recognized |
| FBD-1106 | Kotlin error signatures | Repair Engineer | P1 | FBD-1102 | TODO | Common Kotlin compiler/cache compatibility errors recognized |
| FBD-1107 | Android SDK signatures | Repair Engineer | P0 | FBD-1102 | TODO | Missing platforms/build tools/licenses recognized |
| FBD-1108 | Flutter/Dart signatures | Repair Engineer | P0 | FBD-1102 | TODO | Flutter SDK/pub/analyzer errors categorized |
| FBD-1109 | Pub/network/TLS signatures | Repair Engineer | P1 | FBD-1102 | TODO | Network/resolution/proxy failures separated |
| FBD-1110 | Manifest/resource merge signatures | Repair Engineer | P1 | FBD-1102 | TODO | Android manifest/resource failures categorized |
| FBD-1111 | Native libs/Jetifier signatures | Repair Engineer | P1 | FBD-1102 | TODO | Native merge/transform issues recognized |
| FBD-1112 | Signing signatures | Repair Engineer | P1 | FBD-1102 | TODO | Missing/invalid release signing errors recognized safely |
| FBD-1113 | ADB/emulator signatures | Repair Engineer | P1 | FBD-1102 | TODO | Offline/unauthorized/boot/storage issues categorized |
| FBD-1114 | File lock/cache corruption signatures | Repair Engineer | P0 | FBD-1102 | TODO | Known Windows lock/cache cases recognized |
| FBD-1115 | Problem aggregation/deduplication | Repair Engineer | P0 | FBD-1103:FBD-1114 | TODO | Repeated raw errors become one actionable issue |
| FBD-1116 | Problems UI | WPF Developer | P0 | FBD-1115 | TODO | Severity/category/evidence/recommendation/filter visible |
| FBD-1117 | Signature regression fixtures | QA | P0 | FBD-1103:FBD-1114 | TODO | Known captured errors remain recognized after changes |

---

## EPIC FBD-1200 — Auto Repair & Rollback

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-1201 | Define repair recipe/action contracts | Repair Engineer | P0 | FBD-1101 | TODO | Repairs declare risk,changes,verify,rollback |
| FBD-1202 | Implement backup workspace service | Repair Engineer | P0 | FBD-1201 | TODO | Files to be edited/deleted can be restored |
| FBD-1203 | Implement repair preview | Repair Engineer | P0 | FBD-1201 | TODO | User sees planned changes before risky repair |
| FBD-1204 | Implement repair executor | Repair Engineer | P0 | FBD-1202,FBD-1203 | TODO | Ordered actions execute with logs/cancel support |
| FBD-1205 | Implement verification pipeline | Repair Engineer | P0 | FBD-1204 | TODO | Repair cannot report success without verification |
| FBD-1206 | Implement rollback pipeline | Repair Engineer | P0 | FBD-1202,FBD-1205 | TODO | Supported failed repairs restore prior state |
| FBD-1207 | Repair: flutter clean + build folder reset | Repair Engineer | P0 | FBD-1204 | TODO | Safe cleanup repair verified by next probe |
| FBD-1208 | Repair: project `.gradle` cleanup | Repair Engineer | P0 | FBD-1204 | TODO | Project-local cache cleanup avoids global collateral damage |
| FBD-1209 | Repair: Gradle user cache targeted cleanup | Repair Engineer | P1 | FBD-1204 | TODO | Specific corrupt caches handled with explicit risk level |
| FBD-1210 | Repair: regenerate local.properties | Repair Engineer | P0 | FBD-613,FBD-1204 | TODO | Correct sdk.dir/flutter.sdk written with backup |
| FBD-1211 | Repair: select compatible managed JDK | Repair Engineer | P0 | FBD-912,FBD-1204 | TODO | Build can rerun with recommended JDK |
| FBD-1212 | Repair: install missing SDK packages | Repair Engineer | P0 | FBD-904:FBD-907,FBD-1204 | TODO | Missing requirements installed and re-detected |
| FBD-1213 | Repair: Android licenses | Repair Engineer | P0 | FBD-908,FBD-1204 | TODO | License blocker resolved and verified |
| FBD-1214 | Repair: ADB restart | Repair Engineer | P1 | FBD-409,FBD-1204 | TODO | kill-server/start-server then verify devices |
| FBD-1215 | Repair: emulator restart | Repair Engineer | P1 | FBD-1008,FBD-1204 | TODO | Emulator reset and readiness rechecked |
| FBD-1216 | Repair: dependency refresh/pub get | Repair Engineer | P0 | FBD-802,FBD-1204 | TODO | Package state refreshed and exit verified |
| FBD-1217 | Repair: Gradle wrapper compatibility update | Repair Engineer | P1 | FBD-703,FBD-1202 | TODO | Explicit supported update with file backup and verification |
| FBD-1218 | Repair history/receipts | Persistence Developer | P1 | FBD-1204 | TODO | Before/action/after result persisted |
| FBD-1219 | Auto Repair UI | WPF Developer | P0 | FBD-1203:FBD-1218 | TODO | Fix Selected/Fix Safe Issues/rollback status available |

---

## EPIC FBD-1300 — Build Orchestration

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-1301 | Define build profile/pipeline contracts | Tech Lead | P0 | FBD-801 | TODO | Pipeline consists of ordered, cancellable steps |
| FBD-1302 | Preflight environment gate | Flutter Engineer | P0 | FBD-711,FBD-1301 | TODO | Known blockers prevent pointless build |
| FBD-1303 | Quick Build pipeline | Flutter Engineer | P0 | FBD-1302,FBD-807 | TODO | pub get + requested build |
| FBD-1304 | Clean Build pipeline | Flutter Engineer | P0 | FBD-1302,FBD-803,FBD-807 | TODO | clean + pub get + build |
| FBD-1305 | Analyze + Build pipeline | Flutter Engineer | P1 | FBD-804,FBD-1301 | TODO | Analyze gate configurable |
| FBD-1306 | Test + Build pipeline | Flutter Engineer | P1 | FBD-805,FBD-1301 | TODO | Test gate configurable |
| FBD-1307 | Build & Run pipeline | Flutter Engineer | P0 | FBD-1009,FBD-1301 | TODO | Build/run on selected emulator/device |
| FBD-1308 | Release APK pipeline | Flutter Engineer | P0 | FBD-809,FBD-1301 | TODO | Release APK pipeline records artifact |
| FBD-1309 | Release AAB pipeline | Flutter Engineer | P0 | FBD-810,FBD-1301 | TODO | Release AAB pipeline records artifact |
| FBD-1310 | Failure → Problem Intelligence integration | Repair Engineer | P0 | FBD-1115,FBD-1301 | TODO | Failed step automatically creates structured problems |
| FBD-1311 | Auto-fix/retry policy | Repair Engineer | P0 | FBD-1205,FBD-1310 | TODO | Only eligible safe fixes trigger bounded retry |
| FBD-1312 | Prevent retry loops | Tech Lead | P0 | FBD-1311 | TODO | Signature/action retry limits enforced |
| FBD-1313 | Build execution receipt | Persistence Developer | P0 | FBD-208,FBD-1301 | TODO | Branch,commit,versions,steps,duration,result stored |
| FBD-1314 | Build history UI | WPF Developer | P1 | FBD-1313 | TODO | Previous builds/artifacts/results searchable |

---

## EPIC FBD-1400 — Release Center

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-1401 | Release readiness checklist service | Android Engineer | P0 | FBD-614,FBD-711 | TODO | Signing/version/ID/toolchain checks unified |
| FBD-1402 | Validate application ID | Android Engineer | P1 | FBD-609 | TODO | Empty/default/invalid release IDs warned |
| FBD-1403 | Validate versionCode/versionName | Flutter Engineer | P1 | FBD-610 | TODO | Release version presented and checked |
| FBD-1404 | Validate signing files/config references | Android Engineer | P0 | FBD-614 | TODO | Readiness checked without secret disclosure |
| FBD-1405 | Artifact metadata calculator | Systems Engineer | P1 | FBD-809,FBD-810 | TODO | SHA-256,size,path,time calculated |
| FBD-1406 | Release receipt persistence | Persistence Developer | P1 | FBD-1405 | TODO | Release metadata tied to Git/toolchain identity |
| FBD-1407 | Release Center UI | WPF Developer | P0 | FBD-1401:FBD-1406 | TODO | Checklist + APK/AAB build + artifact details available |

---

## EPIC FBD-1500 — Android Studio / Windows Shell Integration

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-1501 | Open project in Android Studio | Systems Engineer | P0 | FBD-414,FBD-601 | TODO | Selected project opens in detected Studio |
| FBD-1502 | Open Android module in Studio | Systems Engineer | P1 | FBD-1501 | TODO | `/android` opens directly |
| FBD-1503 | Open project folder | Systems Engineer | P1 | FBD-601 | TODO | Explorer opens project root |
| FBD-1504 | Open artifact folder | Systems Engineer | P1 | FBD-813 | TODO | Explorer opens output location |
| FBD-1505 | Open terminal at project | Systems Engineer | P2 | FBD-601 | TODO | Configured terminal opens at cwd |
| FBD-1506 | Copy diagnostic/build command | WPF Developer | P2 | FBD-801 | TODO | User can copy equivalent CLI command |

---

## EPIC FBD-1600 — Persistence & Profiles

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-1601 | Add SQLite database bootstrap/migrations | Persistence Developer | P0 | FBD-001 | TODO | DB initializes and upgrades safely |
| FBD-1602 | Persist application settings | Persistence Developer | P0 | FBD-1601 | TODO | Settings survive restart |
| FBD-1603 | Persist project/workspace profiles | Persistence Developer | P0 | FBD-1601 | TODO | Recent projects reload correctly |
| FBD-1604 | Persist preferred tools/JDK | Persistence Developer | P1 | FBD-1601 | TODO | Per-project/global preferred JDK supported |
| FBD-1605 | Persist build profiles | Persistence Developer | P1 | FBD-1601,FBD-1301 | TODO | Reusable debug/release profiles saved |
| FBD-1606 | Persist last device/emulator selection | Persistence Developer | P2 | FBD-1601,FBD-1013 | TODO | Last target restored when still available |
| FBD-1607 | Persist diagnostic snapshots | Persistence Developer | P1 | FBD-416,FBD-1601 | TODO | Changes between scans can be compared |
| FBD-1608 | Data retention/cleanup settings | Persistence Developer | P2 | FBD-1601 | TODO | Old logs/history can be pruned safely |

---

## EPIC FBD-1700 — Installation & Self-Update

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-1701 | Configure Release build publishing | DevOps | P1 | FBD-001 | TODO | Reproducible win-x64 release output generated |
| FBD-1702 | Create Windows installer | DevOps | P1 | FBD-1701 | TODO | Install/uninstall works on clean machine |
| FBD-1703 | Create portable package | DevOps | P2 | FBD-1701 | TODO | Zip/portable launch works without installer |
| FBD-1704 | Implement update-check service | DevOps | P2 | FBD-010 | TODO | Newer app release can be detected |
| FBD-1705 | Add About/version/update UI | WPF Developer | P2 | FBD-1704 | TODO | User can view version and update availability |
| FBD-1706 | Define code-signing strategy | DevOps | P1 | FBD-1702 | TODO | Signing process documented/configurable securely |

---

## EPIC FBD-1800 — QA & Hardening

| ID | Task | Owner Role | Priority | Depends On | Status | Acceptance Criteria |
|---|---|---|---|---|---|---|
| FBD-1801 | Create clean Windows VM test baseline | QA | P0 | FBD-008 | TODO | Repeatable clean-machine scenario documented |
| FBD-1802 | Test missing Flutter scenario | QA | P0 | FBD-417 | TODO | Missing state/action shown correctly |
| FBD-1803 | Test multiple Flutter installations | QA | P1 | FBD-404 | TODO | PATH conflicts detected and selectable |
| FBD-1804 | Test Java 11/17/21 conflict matrix | QA | P0 | FBD-708,FBD-912 | TODO | Correct compatibility decisions verified |
| FBD-1805 | Test missing SDK platform/build-tools | QA | P0 | FBD-904,FBD-905 | TODO | Detection/install/recheck passes |
| FBD-1806 | Test Android license blocker | QA | P0 | FBD-908 | TODO | Blocker resolved with clear verification |
| FBD-1807 | Test corrupted project Gradle cache | QA | P0 | FBD-1208 | TODO | Recognized/repaired/verified |
| FBD-1808 | Test selected global Gradle corruption case | QA | P1 | FBD-1209 | TODO | Targeted repair avoids unnecessary deletion |
| FBD-1809 | Test emulator unavailable/offline | QA | P0 | FBD-1013 | TODO | Clear status and recovery path |
| FBD-1810 | Test physical unauthorized device | QA | P1 | FBD-1002 | TODO | App provides correct authorization guidance |
| FBD-1811 | Test Groovy Gradle project | QA | P0 | FBD-617 | TODO | Requirements/build workflow passes |
| FBD-1812 | Test Kotlin DSL project | QA | P0 | FBD-617 | TODO | Requirements/build workflow passes |
| FBD-1813 | Test debug APK end-to-end | QA | P0 | FBD-1303 | TODO | Import→doctor→build artifact passes |
| FBD-1814 | Test build & run end-to-end | QA | P0 | FBD-1307 | TODO | Import→repair→emulator→run passes |
| FBD-1815 | Test release APK end-to-end | QA | P0 | FBD-1308,FBD-1407 | TODO | Signed release workflow passes on fixture project |
| FBD-1816 | Test release AAB end-to-end | QA | P0 | FBD-1309,FBD-1407 | TODO | AAB workflow passes on fixture project |
| FBD-1817 | Test cancellation during clone/download/build | QA | P0 | FBD-204,FBD-902,FBD-1301 | TODO | No hung process/corrupt state after cancel |
| FBD-1818 | Security/log redaction test | QA | P0 | FBD-207 | TODO | Known secret forms never appear in logs |
| FBD-1819 | Backup/rollback destructive test suite | QA | P0 | FBD-1206 | TODO | Controlled failures restore original files |
| FBD-1820 | RC smoke/regression checklist | QA | P0 | All P0 | TODO | Release candidate checklist fully passes |

---

# Recommended Parallel Workstreams

## Developer A — Tech Lead / Core
Start: FBD-001 → 002 → 005 → 201 → 701 → 1301.
Own integration contracts and review cross-module changes.

## Developer B — WPF/MVVM
Start after FBD-002: FBD-101 → 103 → 104 → 105 → 310 → 417 → 711 → 812.

## Developer C — Flutter/Android Toolchain
Start after process runner: FBD-404 → 406 → 407-415 → 501 → 601-615 → 702-708 → 801-811 → 1001-1012.

## Developer D — Repair/Automation
Start after error contracts/process output: FBD-1101 → 1102 → signature families → 1201-1217 → 1310-1312.

## Developer E — QA/DevOps/Persistence
Start immediately: FBD-006 → 008 → 1601; then test fixtures, integration matrix and packaging.

---

# Critical Path to First Usable Build (M1)

`FBD-001 → 002 → 005 → 201 → 202 → 203 → 301 → 303 → 305 → 310 → 404 → 406 → 407 → 408 → 409 → 416 → 417 → 501 → 502 → 601 → 615 → 801 → 802 → 804 → 807 → 812`

M1 acceptance:
1. User enters Git repository + branch + folder.
2. Repository is cloned/updated.
3. Flutter project is detected.
4. Flutter/Java/Android environment status is shown.
5. `flutter doctor -v` is visible and parsed.
6. `pub get`, `analyze`, and Debug APK build can run.
7. Logs stream live.
8. Failures are captured clearly.
9. APK path is shown on success.
10. UI never freezes during commands.

---

# Critical Path to Auto-Repair Build (M4)

`M1 → FBD-602-615 → FBD-701-712 → FBD-1101-1117 → FBD-1201-1219 → FBD-1301-1313`

M4 acceptance:
1. App detects a known toolchain/build issue.
2. App identifies evidence and root cause.
3. App proposes a repair with risk level.
4. Affected files are backed up.
5. Repair executes.
6. Verification reruns.
7. Build retries at most within bounded policy.
8. Repair/build receipt is persisted.
9. Rollback is available where supported.

---

# Team Rules

1. One task ID per PR unless tasks are inseparable.
2. PR title starts with Task ID: `[FBD-xxx] ...`.
3. Do not directly mutate global PATH/JAVA_HOME if a per-process setting can solve the problem.
4. Do not delete global Gradle/Flutter caches broadly without a targeted reason and confirmation.
5. Never log secrets, signing passwords, tokens, or private keys.
6. Every automated fix must have a verification step.
7. Every risky file mutation must have backup/rollback metadata.
8. Parsers must preserve raw evidence when parsing fails.
9. External commands must support cancellation where possible.
10. New error signatures require regression fixture tests.
11. P0 tasks block RC until passing.
12. Update this board status when work starts/completes.
