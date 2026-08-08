# Flutter Build Doctor Runtime Detection

Detection pipeline:

UI -> EnvironmentScanService -> Tool Detectors -> Process Engine -> Windows Tools

Supported detectors:

- Git
- Flutter SDK
- Dart SDK
- Java/JDK
- Android SDK
- ADB
- Emulator
- Android Studio

Each detector returns normalized status information:

- Installed
- Version
- Path
- Health message
- Repair availability

Next phase: connect detectors to ProcessRunner and parse real command output.
