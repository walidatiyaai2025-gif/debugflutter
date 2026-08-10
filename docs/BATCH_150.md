# BATCH-150 — Agent Engineering Ownership

Status: IN PROGRESS

This plan extends the existing 50-task ownership in `docs/BATCH_050.md` by **100 additional engineering tasks**, for **150 total claimed tasks**.

## Coordination rules

- Preserve work already in PR #119.
- Do not modify the active FBD-613 / `local.properties` scope while PR #117 remains open.
- Deliver in small reviewable slices with Release build + full tests as merge gates.
- Prefer backend capability before enabling UI actions.
- No secret leakage, silent destructive edits, or unverified repair claims.

## Additional 100 tasks

### Flutter Doctor & parsing — B150-051..060

51. **B150-051** — Flutter Doctor status-marker tokenizer; recognize `[✓]`, `[!]`, `[✗]`, `[-]` and equivalent Unicode markers.
52. **B150-052** — Flutter Doctor section-boundary parser; split top-level components without losing source text.
53. **B150-053** — Doctor evidence-line parser; attach indented bullets/evidence to the correct section.
54. **B150-054** — Known component classifier; normalize Flutter, Android toolchain, Chrome, Visual Studio, Android Studio, connected device and network resources.
55. **B150-055** — Unknown-section preservation; retain future Flutter sections verbatim instead of dropping them.
56. **B150-056** — Raw evidence retention contract; each parsed section keeps original source lines.
57. **B150-057** — Normalized Doctor status model; map success/warning/error/unavailable/unknown safely.
58. **B150-058** — Malformed/truncated Doctor output handling; parser returns partial structured results without throwing.
59. **B150-059** — Doctor summary projection; totals for ready/warning/error/unknown sections.
60. **B150-060** — Multi-version Doctor parser fixture suite covering representative stable output variants.

### Compatibility engine — B150-061..070

61. **B150-061** — Semantic version value/parser shared by compatibility rules.
62. **B150-062** — Version range/constraint parser for exact, minimum and bounded requirements.
63. **B150-063** — Java ↔ Gradle compatibility rule implementation.
64. **B150-064** — Gradle ↔ Android Gradle Plugin compatibility rule implementation.
65. **B150-065** — AGP ↔ compileSdk compatibility rule implementation.
66. **B150-066** — Kotlin ↔ Gradle/AGP compatibility rule implementation.
67. **B150-067** — Flutter ↔ Dart project constraint validation.
68. **B150-068** — Required Android platform/build-tools availability rule.
69. **B150-069** — Compatibility blocker/severity scoring.
70. **B150-070** — Compatibility matrix aggregation with current/required/recommended/evidence fields.

### Flutter Command Center — B150-071..080

71. **B150-071** — Safe Flutter command builder using typed arguments rather than shell concatenation.
72. **B150-072** — `flutter pub get` execution service.
73. **B150-073** — `flutter clean` execution service with explicit operation state.
74. **B150-074** — `flutter analyze` execution + summary contract.
75. **B150-075** — `flutter test` execution + pass/fail/cancel result contract.
76. **B150-076** — `flutter pub outdated` execution service.
77. **B150-077** — `flutter devices` execution service.
78. **B150-078** — `flutter emulators` execution service.
79. **B150-079** — typed `flutter run` request supporting device/flavor/target.
80. **B150-080** — command progress/cancellation integration across Flutter commands.

### Build orchestration — B150-081..090

81. **B150-081** — Debug APK pipeline.
82. **B150-082** — Profile APK pipeline.
83. **B150-083** — Release APK pipeline.
84. **B150-084** — Release AAB pipeline.
85. **B150-085** — Flavor argument support.
86. **B150-086** — Custom Dart target/entrypoint support.
87. **B150-087** — Build artifact discovery with expected-path and fallback scan logic.
88. **B150-088** — SHA-256 artifact hashing service.
89. **B150-089** — Build duration + execution receipt projection.
90. **B150-090** — bounded build retry policy with explicit reason and no infinite retry.

### Devices & emulators — B150-091..100

91. **B150-091** — `adb devices -l` parser.
92. **B150-092** — Device state mapping for online/offline/unauthorized/unknown.
93. **B150-093** — Physical-device metadata projection.
94. **B150-094** — AVD list parser/model.
95. **B150-095** — Emulator launch service.
96. **B150-096** — Wait-for-device orchestration with cancellation/timeout.
97. **B150-097** — Android boot-completed readiness polling.
98. **B150-098** — Safe emulator stop action.
99. **B150-099** — APK install service with explicit replace/downgrade policy.
100. **B150-100** — cancellable logcat streaming service with bounded buffering.

### Repair engine — B150-101..110

101. **B150-101** — Stable issue-signature model for known failures.
102. **B150-102** — Repair safety classification: safe/risky/destructive.
103. **B150-103** — Repair-plan preview contract showing every planned action.
104. **B150-104** — Backup/restore-point contract.
105. **B150-105** — Rollback execution contract.
106. **B150-106** — Post-repair verification contract.
107. **B150-107** — `flutter clean` repair recipe.
108. **B150-108** — dependency refresh / `flutter pub get` repair recipe.
109. **B150-109** — ADB restart repair recipe.
110. **B150-110** — stale Flutter build-directory cleanup recipe with safe-path guards.

### Release center — B150-111..120

111. **B150-111** — Release preflight orchestration.
112. **B150-112** — Signing readiness check without exposing passwords or key material.
113. **B150-113** — Package/application ID validation.
114. **B150-114** — versionName/versionCode readiness validation.
115. **B150-115** — Release manifest checks.
116. **B150-116** — Release artifact size/hash receipt.
117. **B150-117** — Safe output-directory/open-artifact action contract.
118. **B150-118** — Release history model.
119. **B150-119** — Release APK end-to-end orchestration.
120. **B150-120** — Release AAB end-to-end orchestration.

### Persistence & profiles — B150-121..130

121. **B150-121** — SQLite database bootstrap/version table.
122. **B150-122** — Repository/workspace history schema.
123. **B150-123** — Command execution history schema.
124. **B150-124** — Diagnostics history schema.
125. **B150-125** — Repair history schema.
126. **B150-126** — Build history schema.
127. **B150-127** — Release receipt persistence schema.
128. **B150-128** — Application settings persistence.
129. **B150-129** — Preferred JDK/device/build-profile persistence.
130. **B150-130** — Persistence migration/retention regression tests.

### UX & accessibility — B150-131..140

131. **B150-131** — Accessible names/status semantics for status badges.
132. **B150-132** — Keyboard navigation through primary application workflows.
133. **B150-133** — Visible keyboard focus states.
134. **B150-134** — Runtime theme service contract.
135. **B150-135** — High-contrast-safe design tokens.
136. **B150-136** — Search/filter behavior for live logs.
137. **B150-137** — Copyable problem/error evidence UX.
138. **B150-138** — Standard empty/loading/error/no-project states.
139. **B150-139** — Consistent cancellation affordance for supported long operations.
140. **B150-140** — Destructive/risky confirmation UX including consequences and rollback availability.

### QA & hardening — B150-141..150

141. **B150-141** — Golden Doctor parser fixture regression suite.
142. **B150-142** — Process cancellation regression suite.
143. **B150-143** — Process timeout regression suite.
144. **B150-144** — Secret-redaction regression suite.
145. **B150-145** — Dirty-repository safety regression suite.
146. **B150-146** — Windows 10 smoke-test matrix.
147. **B150-147** — Windows 11 smoke-test matrix.
148. **B150-148** — Clean-machine scenario harness/documented fixture.
149. **B150-149** — Partial-toolchain scenario harness/documented fixture.
150. **B150-150** — Release-candidate audit checklist and evidence template.

## Current execution order

1. Finish/review PR #119 foundation slice.
2. Implement FBD-502 / FBD-503 / FBD-504 / FBD-506 plus B150-051..060 on stacked branch `agent/batch-150-flutter-doctor`.
3. Move to compatibility engine and command center only after parser contracts are stable.
4. Keep FBD-613 isolated until PR #117 is resolved.

## Definition of done

A task is complete only when its code/docs/tests (as applicable) are committed, relevant Release build and full tests pass, safety constraints are preserved, and the work is represented in the repository plan/receipt.