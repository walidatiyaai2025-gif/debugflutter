# Windows Smoke-Test Matrix

This matrix defines **required release-candidate evidence**. A row is not considered passed until a human/CI run records the date, OS build, artifact/commit and evidence link. The presence of this document is not a claim that a physical smoke test has run.

| Scenario | Windows 10 | Windows 11 | Required evidence |
|---|---|---|---|
| App cold start | PENDING | PENDING | OS build, app commit/version, screenshot/log |
| Import existing Flutter repo | PENDING | PENDING | repo fixture, selected branch, import result |
| Environment Doctor | PENDING | PENDING | detected Git/Flutter/Dart/Java/Android status |
| Flutter Doctor parser | PENDING | PENDING | raw output + parsed section summary |
| `flutter analyze` | PENDING | PENDING | sanitized command receipt + exit/result summary |
| Unit/widget tests | PENDING | PENDING | sanitized command receipt + pass/fail count |
| Debug APK build | PENDING | PENDING | artifact path, size, SHA-256 |
| Release APK preflight/build | PENDING | PENDING | preflight report + artifact receipt; no secrets |
| Device list / emulator launch | PENDING | PENDING | device IDs/states and bounded readiness result |
| Cancellation | PENDING | PENDING | operation status shows Cancelled, child process terminated |
| Timeout | PENDING | PENDING | operation status shows TimedOut, not Cancelled |
| Logcat bounded capture | PENDING | PENDING | retained line count within configured bound |
| Repair preview | PENDING | PENDING | risk, affected paths, consequences, verification steps |
| Dirty repository safety | PENDING | PENDING | replacement blocked without explicit approval |

## Evidence record template

- Date/time (UTC):
- Windows edition/build:
- Machine type (physical/VM):
- App commit:
- App version/build identity:
- Flutter version:
- Android SDK/AGP/Gradle/JDK versions:
- Scenario:
- Result: PASS / FAIL / BLOCKED
- Evidence link/path:
- Notes / follow-up issue:
