# V2 DuckDB Dynamic Status

## Objective

Initialize the local V2 DuckDB database when the application starts, report its real state in the shell footer, and provide a simple operator-controlled reset that preserves the previous database as a dated backup.

## Problem and Why

V2 currently creates DuckDB only when a Sergio workbook is analyzed, while the footer shows a static and misleading relationship between the two tabs. The operator needs an honest startup status and a safe recovery action when the local database cannot be used.

## Authorized Scope

- Work only in PapaPersonas V2 on `feat/v2-lightweight`.
- Create/open the V2 DuckDB file during application startup without blocking the UI.
- Create the fixed `sergio_load_metadata` table at startup; keep it empty until the first successful Sergio promotion.
- Create the dynamic `sergio_current` and `sergio_last_import` tables only after a successful load because their business columns and selected primary key come from the workbook.
- Keep `sergio_staging_<id>` tables transient during candidate preparation.
- Replace the static footer with dynamic states for initializing, ready without an official load, ready with an official load, inconsistent, and unavailable.
- Add a `Reiniciar base` action to the footer. Require explicit in-app confirmation, move the current database to a dated backup, and create a fresh initialized database.
- Never silently delete or overwrite a database. A corrupt, locked, permission-denied, or disk-error database must remain available for diagnosis or restoration.
- Disable reset while a Sergio operation is active and serialize initialization, status inspection, promotion, and reset access.

## Constraints

- The status probe must not mutate business data or repair schemas implicitly.
- Display only the source file name, never its original directory.
- The footer may expose the canonical local DuckDB path through an ellipsized label and tooltip.
- Reloading a workbook may rebuild missing dynamic tables when DuckDB remains writable; it cannot repair a corrupt, locked, permission-denied, or storage-failed database.
- TDD mode: off; ordinary behavior-first integration tests and Release build are required.
- Delivery strategy: `stacked-to-main`; each task is an ordered local commit. No push or PR creation is authorized.
- Forecast: under 400 authored changed lines, excluding tests only when generated; no PR split is expected.

## Tasks

- [x] **DDS-001 — Initialize, inspect, and safely reset DuckDB**
  - Route: delegated direct because startup initialization, consistency inspection, backup/reset semantics, synchronization, and integration tests span multiple non-trivial files.
  - Acceptance: startup creates the DuckDB file and empty fixed metadata table; inspection distinguishes ready-empty, ready-loaded, inconsistent, and unavailable states; reset requires caller confirmation, preserves the old database as a collision-safe dated backup, initializes a fresh database, and restores the prior file if fresh initialization fails; no source path leakage occurs.
  - Checks: temporary-directory integration tests cover first initialization, idempotency, complete metadata, partial canonical state, count mismatch, transient candidates, corrupt input, reset backup, reset collision, and reset rollback. Release build, full V2 tests, and `git diff --check` pass.
  - Rollback: revert only the lifecycle service, its focused tests, and this task evidence; existing Sergio tables remain untouched.

- [ ] **DDS-002 — Integrate dynamic status and reset into the WPF footer**
  - Route: delegated direct because WPF async lifecycle, stale-update protection, operation-state coordination, presentation tests, and documentation span multiple non-trivial files.
  - Acceptance: startup shows initializing then the observed DuckDB state; the footer reports safe metadata after a successful promotion; failures remain actionable; reset is disabled during Sergio work, asks for confirmation, refreshes status after completion, and never claims success after a failed reset; the old static tab-connection claim is removed.
  - Checks: presentation/state tests cover every status and safe source-name formatting; structural readback confirms one startup hook and no fire-and-forget exception leaks. Release build, full V2 tests, and `git diff --check` pass.
  - Rollback: revert only footer wiring, presentation helpers/tests, README updates, and this task evidence while preserving the lifecycle backend.

## Progress

Feature authorized. Exploration confirmed that V2 has no startup database bootstrap and that the existing three-table contract consists of fixed metadata plus two workbook-shaped tables created by promotion.

DDS-001 implemented in `feat/v2-lightweight` with a dedicated lifecycle backend service and focused integration tests. Metadata initialization is idempotent, inspection reports ready/loaded/inconsistent/unavailable states without mutating business tables, and reset requires explicit confirmation while preserving collision-safe backups and rollback restoration on initialization failure.

## Evidence

- Focused lifecycle tests: `dotnet test V2/tests/PapaPersonas.V2.Tests/PapaPersonas.V2.Tests.csproj -c Release --filter "FullyQualifiedName~SergioDuckDbLifecycleTests"` → Passed (`Total: 13, Failed: 0`).
- Full V2 test suite: `dotnet test V2/PapaPersonas.sln -c Release --no-build` → Passed (`Total: 104, Failed: 0`).
- V2 Release build: first run failed due to locked `PapaPersonas.V2.Shell.dll` by running process `PapaPersonas.V2.App (39260)`; after stopping that process, `dotnet build V2/PapaPersonas.sln -c Release --no-restore` → Passed (`0 errors`).
- Diff whitespace check: `git diff --check` → Passed (line-ending warning only, no whitespace errors).
