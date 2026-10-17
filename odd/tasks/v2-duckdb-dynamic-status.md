# V2 DuckDB Dynamic Status

## Objective

Initialize the local V2 DuckDB database when the application starts, report its real state in the shell footer, and provide an operator-controlled reset.

Current reset scope, authorized by the user, supersedes dated backup and restoration: confirmed reset permanently deletes the configured DuckDB file and its `.wal` sidecar, creates no backup, and cannot recover the deleted data. Historical DDS-001 and DDS-002 backup/restore evidence remains below and is explicitly superseded for current behavior.

## Problem and Why

V2 currently creates DuckDB only when a Sergio workbook is analyzed, while the footer shows a static and misleading relationship between the two tabs. The operator needs an honest startup status and a safe recovery action when the local database cannot be used.

## Authorized Scope

- Work only in PapaPersonas V2 on `feat/v2-lightweight`.
- Create/open the V2 DuckDB file during application startup without blocking the UI.
- Create the fixed `sergio_load_metadata` table at startup; keep it empty until the first successful Sergio promotion.
- Create the dynamic `sergio_current` and `sergio_last_import` tables only after a successful load because their business columns and selected primary key come from the workbook.
- Keep `sergio_staging_<id>` tables transient during candidate preparation.
- Replace the static footer with dynamic states for initializing, ready without an official load, ready with an official load, inconsistent, and unavailable.
- Add a `Reiniciar base` action to the footer. Require explicit Spanish confirmation of permanent data loss and no backup. The default remains cancellation. Confirmed reset deletes only the configured database and its `.wal` sidecar, then creates a fresh initialized database.
- Never silently delete or overwrite a database. Delete failure before any file is removed must leave the previous database in place. Initialization failure after a successful delete must not restore a backup or claim that the data is recoverable.
- Superseded historical requirement: move the current database to a dated backup and restore it if fresh initialization fails. That behavior was completed in DDS-001/DDS-002 and is not current.
- Disable reset while a Sergio operation is active and serialize initialization, status inspection, promotion, and reset access.

## Constraints

- The status probe must not mutate business data or repair schemas implicitly.
- Display only the source file name, never its original directory.
- The footer may expose the canonical local DuckDB path through an ellipsized label and tooltip.
- Reloading a workbook may rebuild missing dynamic tables when DuckDB remains writable; it cannot repair a corrupt, locked, permission-denied, or storage-failed database.
- TDD mode: off; ordinary behavior-first integration tests and Release build are required.
- Delivery strategy: `stacked-to-main`; each task is an ordered local commit. No push or PR creation is authorized.
- Initial forecast: under 400 authored changed lines; actual scope is recorded below without code-golfing or omitted tests.

## Tasks

- [x] **DDS-001 — Initialize, inspect, and safely reset DuckDB**
  - Route: delegated direct because startup initialization, consistency inspection, backup/reset semantics, synchronization, and integration tests span multiple non-trivial files.
  - Acceptance: startup creates the DuckDB file and empty fixed metadata table; inspection distinguishes ready-empty, ready-loaded, inconsistent, and unavailable states; reset requires caller confirmation, preserves the old database as a collision-safe dated backup, initializes a fresh database, and restores the prior file if fresh initialization fails; no source path leakage occurs.
  - Checks: temporary-directory integration tests cover first initialization, idempotency, complete metadata, partial canonical state, count mismatch, transient candidates, corrupt input, reset backup, reset collision, and reset rollback. Release build, full V2 tests, and `git diff --check` pass.
  - Rollback: revert only the lifecycle service, its focused tests, and this task evidence; existing Sergio tables remain untouched.

- [x] **DDS-002 — Integrate dynamic status and reset into the WPF footer**
  - Route: delegated direct because WPF async lifecycle, stale-update protection, operation-state coordination, presentation tests, and documentation span multiple non-trivial files.
  - Acceptance: startup shows initializing then the observed DuckDB state; the footer reports safe metadata after a successful promotion; failures remain actionable; reset is disabled during Sergio work, asks for confirmation, refreshes status after completion, and never claims success after a failed reset; the old static tab-connection claim is removed.
  - Checks: presentation/state tests cover every status and safe source-name formatting; structural readback confirms one startup hook and no fire-and-forget exception leaks. Release build, full V2 tests, and `git diff --check` pass.
  - Rollback: revert only footer wiring, presentation helpers/tests, README updates, and this task evidence while preserving the lifecycle backend.

- [x] **DDS-003 — Reset DuckDB without backup**
  - Route: delegated direct because lifecycle reset semantics, distinct failure reporting, WPF confirmation, tests, and README span multiple non-trivial files.
  - TDD: off. Source: Constraints in this document ("TDD mode: off; ordinary behavior-first integration tests and Release build are required."). Red-green TDD is not required.
  - Acceptance: confirmed reset permanently deletes the configured database and its `.wal` sidecar, creates a fresh database with empty metadata, and creates no backup. Unconfirmed reset leaves the database intact. Prior backup files are not touched. Failures are typed, not inferred from message text: an existing database is preserved when deletion fails before removal; prior data that was actually deleted is reported as irreversible when initialization then fails; a missing database plus failed creation does not claim that a previous database was deleted or preserved. Spanish confirmation states permanent loss and no backup; default remains cancellation. Busy guards remain. No real database is reset or deleted; tests use temporary directories only.
  - Checks: temporary-directory lifecycle tests, Release lifecycle filter, Release build, full V2 tests, and `git diff --check`. No operation against the real `%LOCALAPPDATA%` database.
  - Forecast: under 400 authored lines.
  - Delivery: prior `stacked-to-main` strategy remains recorded. This task does not stage, commit, push, or open a PR.
  - Rollback: reverting the DDS-003 code change removes the irreversible reset behavior only. It cannot recover data already deleted by a confirmed reset, because no backup is created.

## Progress

Feature authorized. Exploration confirmed that V2 has no startup database bootstrap and that the existing three-table contract consists of fixed metadata plus two workbook-shaped tables created by promotion.

DDS-001 implemented in `feat/v2-lightweight` with a dedicated lifecycle backend service and focused integration tests. Metadata initialization is idempotent, inspection reports ready/loaded/inconsistent/unavailable states without mutating business tables, and reset requires explicit confirmation while preserving collision-safe backups and rollback restoration on initialization failure.

DDS-001 work-unit commit: `aa23b06` (`feat(v2): initialize and inspect duckdb lifecycle`). RDD was disabled/unmanaged; parent structural readback and focused test rerun passed.

DDS-002 implemented by wiring asynchronous lifecycle initialization/inspection into WPF startup, replacing the static footer text with a compact dynamic DuckDB status area, adding guarded reset confirmation/action, and refreshing footer state after Sergio promotion completion (success and failure) and after reset.

DDS-002 work-unit commit: `e811cfe` (`feat(v2): show dynamic duckdb status in footer`). Native assessment was high due to a conservative process-boundary signal; independent read-only verification found no process mutation and passed all required checks. RDD remained disabled/unmanaged.

DDS-003 implemented an irreversible confirmed reset. It deletes only the configured DuckDB file and its `.wal` sidecar and creates no backup. A verification correction replaced message parsing with `SergioDuckDbResetFailureKind`: preserved previous database, irreversibly deleted previous data, or no previous database when creation fails. Backup creation and restoration from DDS-001/DDS-002 are superseded for current behavior, but their completed evidence is retained. No real database was reset or deleted.

Delivery note: DDS-003 and DDS-004 authorized for local commit. Root AGENTS.md committed standalone; DDS-003 no-backup reset committed alongside Stock feature in feat(v2).

## Evidence

- Focused lifecycle tests: `dotnet test V2/tests/PapaPersonas.V2.Tests/PapaPersonas.V2.Tests.csproj -c Release --filter "FullyQualifiedName~SergioDuckDbLifecycleTests"` → Passed (`Total: 13, Failed: 0`).
- Full V2 test suite: `dotnet test V2/PapaPersonas.sln -c Release --no-build` → Passed (`Total: 104, Failed: 0`).
- V2 Release build: first run failed due to locked `PapaPersonas.V2.Shell.dll` by running process `PapaPersonas.V2.App (39260)`; after stopping that process, `dotnet build V2/PapaPersonas.sln -c Release --no-restore` → Passed (`0 errors`).
- Diff whitespace check: `git diff --check` → Passed (line-ending warning only, no whitespace errors).
- Focused footer presentation tests: `dotnet test V2/tests/PapaPersonas.V2.Tests/PapaPersonas.V2.Tests.csproj -c Release --filter "FullyQualifiedName~SergioDuckDbFooterPresenterTests"` → Passed (`Total: 7, Failed: 0`).
- Full V2 test suite: `dotnet test V2/PapaPersonas.sln -c Release --no-build` → Passed (`Total: 111, Failed: 0`).
- V2 Release build: `dotnet build V2/PapaPersonas.sln -c Release --no-restore` → Passed (`0 errors`).
- Diff whitespace check: `git diff --check` → Passed (line-ending warnings only, no whitespace errors).
- DDS-003 focused lifecycle tests: `dotnet test V2/tests/PapaPersonas.V2.Tests/PapaPersonas.V2.Tests.csproj -c Release --filter "FullyQualifiedName~SergioDuckDbLifecycleTests"` → Passed (`Total: 14, Failed: 0`).
- DDS-003 V2 Release build: `dotnet build V2/PapaPersonas.sln -c Release --no-restore` → Passed (`0 Warning(s), 0 Errores`).
- DDS-003 full V2 test suite: `dotnet test V2/PapaPersonas.sln -c Release --no-build` → Passed (`Total: 112, Failed: 0`).
- DDS-003 diff whitespace check: `git diff --check` → Passed (line-ending warnings only, no whitespace errors).
- DDS-003 initial diff stat, before the failure-kind correction: `5 files changed, 139 insertions(+), 81 deletions(-)`.
- DDS-003 correction: independent verification found that a missing database plus failed fresh initialization always reported deleted previous data because `hadPreviousDatabase` was ignored. Typed failure kinds now distinguish preserved data, irreversible deletion, and no previous database.
- DDS-003 correction lifecycle tests: `dotnet test V2/tests/PapaPersonas.V2.Tests/PapaPersonas.V2.Tests.csproj -c Release --filter "FullyQualifiedName~SergioDuckDbLifecycleTests"` → Passed (`Total: 15, Failed: 0`). The first rerun failed 1/15 because the missing-database assertion rejected the truthful negation `No se eliminó ningún dato previo`; the assertion was narrowed and this rerun passed.
- DDS-003 correction Release build: `dotnet build V2/PapaPersonas.sln -c Release --no-restore` → Passed (`0 Advertencia(s), 0 Errores`).
- DDS-003 correction full V2 suite: `dotnet test V2/PapaPersonas.sln -c Release --no-build` → Passed (`Total: 114, Failed: 0`).
- DDS-003 correction diff check: `git diff --check` → Passed (line-ending warnings only, no whitespace errors).
- DDS-003 correction diff stat after evidence update: `6 files changed, 233 insertions(+), 81 deletions(-)`.
- DDS-004 root AGENTS.md authored with repository rules: Spanish comments/alerts, 95% confidence before writes, no gratuitous renames, no fabricated metrics, read-only inspection on ambiguity, no unrelated rewrites, no assumed destructive or remote permissions.

- [x] **DDS-004 — Repo policy for AGENTS instructions**
  - Route: direct documentation because the task is to author the root repo policy and update the existing task record only; the request is limited to repository instructions, not product code or operational change.
  - Acceptance: a concise root `AGENTS.md` exists at the repo root with repo-scoped rules limited to the requested policy. The file states that all new or modified source comments and user-facing alerts/warnings/confirmations/errors must be in Spanish; identifiers and APIs are not renamed gratuitously; before creating or modifying any file, the agent must obtain at least 95% evidence-based confidence about the intended change, scope, context, impact, and checks; no fabricated numerical/statistical measurement is allowed; if clarity is insufficient, the agent must inspect read-only and ask a focused question before writing; no unrelated rewrites are allowed; and no implicit destructive or remote permission is assumed.
  - Checks: read the existing root `AGENTS.md` if present before making changes; perform a structural readback after writing to confirm the repo root file exists and the DDS-004 task block matches the corrected request without disturbing the DDS-003 evidence section. No unrelated files are modified.
  - Delivery: documentation-only task record update; no unrelated code or operational edits are added by this policy task itself.
  - Rollback: remove the DDS-004 task block and the root `AGENTS.md` if the repo policy should be retracted; preserve all prior DDS-001–DDS-003 evidence and unrelated work.

## Forecast update

Original forecast (<400 authored lines) was exceeded: DDS-001 authored `+886/-2` and DDS-002 authored `+367/-4` (combined `+1253/-6`, including tests and task evidence). The work was kept as two cohesive ODD work-unit commits instead of code-golfing or omitting tests. No push or pull request was created.
