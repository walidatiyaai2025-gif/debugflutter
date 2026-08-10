# Contributing to Flutter Build Doctor

Flutter Build Doctor is developed as a set of small, verifiable capabilities. Preserve that operating model: one narrow change, explicit evidence, and a green Windows CI gate before merge.

## Branch workflow

- Start production work from the latest `main` unless an issue explicitly targets an integration branch.
- Use `agent/<task-or-scope>` for agent/developer branches.
- Do not mix unrelated fixes into the same branch.
- Before editing, check open pull requests and active issues so two contributors do not implement the same capability concurrently.
- Never overwrite another contributor's in-progress branch or force-push shared work.

## Task coordination

- Every material change must map to a task/issue or a documented work receipt under `docs/work/`.
- If implementation reveals a new dependency, blocker, safety constraint, or follow-up task, record it in GitHub before or with the code change.
- Treat validated merged code and work receipts as the source of truth when the legacy task board is stale.
- Do not mark a UI action ready until the backend capability it invokes is implemented, tested, and safe.

## Coding rules

- Target .NET 8 and preserve nullable reference type annotations.
- Keep application/domain contracts independent from WPF and concrete infrastructure where practical.
- Use dependency injection for production services rather than service locators or hidden global state.
- Long-running Git/Flutter/Gradle/ADB work must be asynchronous and cancellable when the underlying operation supports cancellation.
- Never shell-concatenate untrusted arguments. Use the existing process request/runner abstractions.
- Do not log passwords, tokens, signing secrets, keystore passwords, private keys, or raw secret-bearing configuration files.
- Destructive repair actions require an explicit plan, backup/rollback semantics where applicable, and user confirmation in the UI.

## Formatting and analyzers

The repository root `.editorconfig` is authoritative for whitespace, naming, and code-style guidance. `Directory.Build.props` enables the repository-wide analyzer baseline. New warnings introduced by a pull request should be fixed rather than suppressed unless the PR explains the exception.

## Validation

Run the equivalent of the CI pipeline before requesting merge when a local Windows .NET environment is available:

```powershell
dotnet restore FlutterBuildDoctor.sln
dotnet build FlutterBuildDoctor.sln --configuration Release --no-restore
dotnet test FlutterBuildDoctor.sln --configuration Release --no-build --verbosity normal
```

GitHub Actions is the authoritative merge gate when local execution is unavailable. A change is not considered verified until the exact branch/PR head has a successful Release build and full test run.

## Pull requests

A pull request should state:

- task/issue IDs covered;
- what changed and why;
- user/developer impact;
- security or destructive-operation boundary, when relevant;
- tests added or updated;
- exact validation evidence.

Keep PRs reviewable. Prefer multiple small PRs over a single large PR that crosses unrelated epics.

## Review and merge

- Resolve review comments with code/tests or a documented rationale.
- Re-run failed CI only after understanding whether the failure is deterministic, environmental, or introduced by the change.
- Prefer squash merge for focused task PRs so the main history retains one auditable task-level commit.
- After merge, update the corresponding issue/work receipt and continue from the next unblocked task.
