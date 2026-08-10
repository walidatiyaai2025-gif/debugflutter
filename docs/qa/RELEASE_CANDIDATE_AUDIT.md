# Release-Candidate Audit Checklist

A release candidate is not VERIFIED until every required item has evidence attached. `N/A` requires a written reason.

## Source and CI
- [ ] Candidate commit SHA recorded.
- [ ] Repository clean/dirty state recorded; dirty replacement was not implicit.
- [ ] Restore succeeded.
- [ ] Release build succeeded.
- [ ] Full automated test suite succeeded.
- [ ] No unresolved blocker-level compatibility finding.

## Security and privacy
- [ ] Secret-redaction regression suite passed.
- [ ] Signing readiness output contains no password/key material.
- [ ] Persisted history contains sanitized metadata rather than raw secret-bearing logs.
- [ ] Destructive/risky repair actions require explicit confirmation.
- [ ] Backup/rollback evidence captured where the selected repair supports it.

## Environment scenarios
- [ ] Clean-machine fixture/harness passed.
- [ ] Partial-toolchain fixture/harness passed.
- [ ] Windows 10 smoke matrix completed with evidence.
- [ ] Windows 11 smoke matrix completed with evidence.

## Runtime workflows
- [ ] Flutter Doctor golden fixtures passed, including unknown-section preservation.
- [ ] Process cancellation regression passed.
- [ ] Process timeout regression passed.
- [ ] Device/emulator readiness workflow passed.
- [ ] Logcat bounded-retention behavior passed.

## Build/release evidence
- [ ] Release preflight passed.
- [ ] APK/AAB artifact type and path recorded.
- [ ] Artifact size recorded.
- [ ] SHA-256 recorded and independently rechecked if distributing externally.
- [ ] Version name/code and package/application ID recorded.
- [ ] Release history record written.

## Sign-off record
- Candidate SHA:
- CI run:
- Artifact path:
- SHA-256:
- Windows 10 evidence:
- Windows 11 evidence:
- Known warnings:
- Rollback/restore notes:
- Decision: APPROVE / REJECT
- Reviewer/date:
