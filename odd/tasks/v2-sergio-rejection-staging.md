# V2 Sergio Rejection Staging

## Objective

Keep every Sergio workbook row in DuckDB staging, classify unusable primary-key rows in the database, let the operator export those rejections, and atomically build the final official table from valid rows with a real primary-key constraint.

## Problem and Why

The current Sergio flow drops the entire candidate when the selected primary key contains empty or duplicate values. That forces manual source cleanup and loses useful evidence. Staging should represent the imported file faithfully; only the final official table should enforce clean primary-key semantics.

## Authorized Scope

- Work only in PapaPersonas V2 on `feat/v2-lightweight`.
- Preserve every imported row in the candidate staging table.
- Add a collision-safe technical rejection-reason column.
- Classify `NULL` or `TRIM(key) = ''` as `CLAVE_VACIA`.
- Classify every row in a non-empty duplicate `TRIM(key)` group as `CLAVE_DUPLICADA`; never choose an arbitrary survivor.
- Show the selected key column and counts for total, valid, empty-key, duplicate-group, and duplicate rows.
- Let the operator continue or cancel without requiring workbook cleanup.
- Export rejected rows on demand to `<workbook-directory>/rech`.
- Preview only valid rows.
- Atomically build the final official table without technical columns and with a real primary-key constraint.
- Preserve the previous official table and metadata on any failure.
- Retain only the latest complete marked import as `sergio_last_import` after successful promotion so rejected rows remain exportable.

## Constraints

- All source/business staging columns remain `VARCHAR`; the technical reason column is nullable `VARCHAR`.
- Technical identifiers must be generated collision-safe against accepted aliases.
- Rejection matching remains case-sensitive and applies only `TRIM()`, consistent with the current contract.
- Empty keys are classified before duplicate detection and receive one reason.
- If no valid rows remain, promotion is disabled; export and cancel remain available.
- Rejection export uses UTF-8 BOM, RFC-4180 quoting, atomic `.partial` publication, and non-overwriting timestamp/suffix names.
- Exported rows contain `PP_MOTIVO` followed by all original columns; no technical database identifier leaks into the public header.
- No row values are written to activity logs.
- TDD mode: off; ordinary behavior-first integration tests and Release build are required.
- Delivery strategy: `stacked-to-main`; each task is an ordered local commit. No push or PR creation is authorized.

## Tasks

- [x] **SRJ-001 — Retain and classify every staging row**
  - Route: direct implementation because staging SQL, candidate ownership, metrics, tests, and task evidence span multiple non-trivial files.
  - Acceptance: candidate retains every row; technical reason alias is collision-safe; empty keys and every member of duplicate groups are classified; metrics include valid rows; rejected candidates are retained rather than dropped; zero-valid state is explicit.
  - Checks: real temporary DuckDB tests cover mixed empty/duplicate/valid rows, duplicate-group membership, candidate retention, collision-safe reason alias, and unrelated-table preservation. Release build and full V2 tests pass (73/73); `git diff --check` passes. Structural readback confirms V2-only changes, all rows retained, parameterized `CLAVE_VACIA` then `CLAVE_DUPLICADA` classification, and promoter rejection guard.
  - Rollback: revert only staging classification/result changes, focused tests, and this task evidence.

- [ ] **SRJ-002 — Export rejected rows safely**
  - Route: delegated direct because DuckDB streaming, CSV publication, filesystem collision safety, tests, and evidence span multiple files.
  - Acceptance: analyzer-owned candidate exports only rejected rows to `<input>/rech`; public CSV header is `PP_MOTIVO` plus source aliases; UTF-8 BOM and quoting are correct; publication is atomic and never overwrites; no rejected rows produces an explicit no-output result.
  - Checks: focused exporter integration tests, full V2 tests, Release build, diff check, filesystem cleanup readback.
  - Rollback: remove only the Sergio rejection exporter, focused tests, and this task evidence.

- [ ] **SRJ-003 — Promote valid rows with a real primary key**
  - Route: delegated direct because dynamic schema DDL, transaction/rollback, retained import audit, preview filtering, tests, and evidence span multiple files.
  - Acceptance: preview contains only valid business columns; promotion creates a clean official table with a real PK; rejected rows and technical columns never enter official data; complete marked import becomes `sergio_last_import`; previous official/import/metadata survive any failure; zero-valid candidates cannot promote.
  - Checks: DuckDB catalog PK assertion, real rollback integration test, full V2 tests, Release build, diff check, structural readback.
  - Rollback: revert promotion/preview/audit changes, focused tests, and this task evidence; no migration is required because replacement is transactional.

- [ ] **SRJ-004 — Integrate rejection decisions and export in WPF**
  - Route: delegated direct because WPF state, warning/confirmation text, export action, preview, documentation, tests, and evidence span multiple files.
  - Acceptance: UI names the selected key and all counts; rejected rows trigger Continue/Cancel messaging; export remains available before and after promotion; preview shows valid rows only; promotion is disabled at zero valid rows; logs contain counts/paths but no row values; README and feature evidence match behavior.
  - Checks: focused text/state tests, full V2 tests, Release build, diff check, independent structural verification; GUI smoke reported honestly.
  - Rollback: revert WPF/export wiring, documentation, focused tests, and this task evidence while preserving backend tasks.

## Progress

SRJ-001 is complete. Candidate staging retains all rows, adds a nullable collision-safe technical reason column, classifies empty keys before non-empty duplicate groups, and exposes internally consistent metrics including valid rows. `IsValid` retains the compatibility meaning “no rejected rows”; `HasRejections` and `CanPromote` make the safety state explicit, and the current promoter rejects marked candidates. Zero-row candidates are retained but are not valid/promotable. Failures and cancellation still remove the current attempt; `CleanupAll` remains the close-time ownership safety net. RDD remains disabled/unmanaged. The actual implementation commit hash is recorded after commit creation; SRJ-002, SRJ-003, and SRJ-004 remain pending.
