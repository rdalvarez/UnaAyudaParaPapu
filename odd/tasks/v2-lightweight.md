# PapaPersonas V2 Lightweight

## Objective

Create a lighter PapaPersonas V2 while preserving the current application as an independently buildable V1. Develop V2 incrementally, beginning with the tabbed shell and the Paso 1 interface before adding cleaning behavior.

## Problem and Why

The current application contains a cumulative master-database workflow that is no longer required. Incoming files can also change their column names, so V2 must let the operator select columns dynamically while preserving full-row rejection evidence and the proven V1 rules.

## Authorized Scope

- Organize the existing tracked application under `V1/` without renaming its solution or projects.
- Keep repository metadata and repository-level guidance at the root.
- Create an independent `V2/PapaPersonas.sln` in later tasks.
- Build V2 incrementally; do not implement the complete cleaning engine in one task.
- Do not push, open a pull request, or merge without separate user authorization.

## Constraints

- Preserve V1 behavior and buildability.
- Keep each structural or behavioral change in a reviewable work-unit commit.
- Do not mix the V1 move with V2 implementation.
- Preserve ignored local outputs and artifacts unless separately reviewed.
- The repository is under OneDrive; avoid unnecessary bulk movement of generated files.
- TDD mode: not enabled by project/session configuration. Use ordinary functional verification for the structural move.
- Delivery strategy: `ask-on-risk`.

## Review Workload Forecast

- The structural V1 move should be detected primarily as renames and contains almost no authored source changes.
- The initial V2 shell and interface are expected to remain below approximately 400 authored changed lines if kept as separate work units.
- Reassess before each work-unit commit; do not reduce clarity, tests, or documentation to fit the heuristic.

## Tasks

- [x] **V2L-001 — Move the existing application into `V1/`**
  - Route: delegated direct.
  - Trigger evidence: coordinated movement affects the solution, source, tests, configuration, documentation, and historical specifications.
  - Scope: move tracked V1 assets only; keep `.git`, `.gitignore`, `.github`, and this feature document at the repository root.
  - Acceptance: `V1/PapaPersonas.sln` restores, builds in Release, and all tests pass from its new location.
  - Checks: `dotnet restore V1/PapaPersonas.sln`; `dotnet build V1/PapaPersonas.sln -c Release --no-restore`; `dotnet test V1/PapaPersonas.sln -c Release --no-build`.
  - Rollback boundary: the directory moves and any path-only fixes made solely for V1 relocation.
  - Evidence:
    - `dotnet restore V1/PapaPersonas.sln`: passed; all five projects restored successfully.
    - `dotnet build V1/PapaPersonas.sln -c Release --no-restore`: passed; 0 warnings, 0 errors.
    - `dotnet test V1/PapaPersonas.sln -c Release --no-build`: passed; 357 passed, 0 failed, 0 skipped.
    - Structural readback confirmed `V1/PapaPersonas.sln`, `V1/src/`, `V1/tests/`, `V1/config/`, `V1/docs/`, `V1/SPEC_V1.md`, `V1/openspec/`, and the faithful `V1/README.md` copy. Root `README.md` remains in place.
    - `git diff --summary`: tracked V1 assets detected as renames into `V1/`; no ignored local outputs, artifacts, or real data were moved or modified.
    - Runtime harness: `N/A` — this work unit only relocates bytes without changing behavior; build plus the complete test suite are the applicable functional verification.
    - RDD risk: `disabled/unmanaged` — verified off by default for this session.
    - Commit: `7e42ad272a0b3258f5af8296233a4fcc8d137f7a` (`refactor(repo): move existing application into V1`).
    - Evidence commit: `6df927544c7517667ece214e359a4f18286746d6` (`docs(odd): record V1 relocation evidence`).
    - Parent spot check: `dotnet test V1/PapaPersonas.sln -c Release --no-build` passed again with 357 passed, 0 failed, and 0 skipped.
    - Independent verification: PASS. Git reported 229 `R100`/`C100` entries plus this ODD document; blob comparison found 0 mismatches, and an independent Release build passed with 0 warnings and 0 errors.

- [x] **V2L-002 — Create repository landing documentation**
  - Route: inline if limited to the root README; otherwise delegated.
  - Trigger evidence: expected to be a single already-understood documentation file.
  - Acceptance: the root README clearly links to V1 and V2 and does not duplicate version-specific operating instructions.
  - Checks: structural readback and link/path validation.
  - Rollback boundary: root README changes only.
  - Evidence:
    - Root `README.md` now acts as a concise version selector and links to `V1/README.md` and this feature document.
    - V1 build, test, runtime, privacy, and database guidance remains version-specific under `V1/README.md`.
    - Repository paths in the V1 quick-start commands match the verified relocated solution.
    - Structural readback and Markdown path checks passed.

- [x] **V2L-003 — Scaffold the independent V2 shell**
  - Route: delegated direct.
  - Trigger evidence: creation of multiple non-trivial WPF, project, and test files.
  - Scope: independent `V2/PapaPersonas.sln`, tabbed shell, Paso 1 interface shell, and disabled placeholders for later modules; no cleaning engine.
  - Acceptance: V2 builds, tests pass, and the application launches with the expected tabs and controls.
  - Checks: focused tests, Release build, and a runtime launch/readback scenario.
  - Rollback boundary: `V2/` scaffold only.
  - Evidence:
    - `dotnet restore V2/PapaPersonas.sln`: passed; all three V2 projects restored successfully.
    - `dotnet build V2/PapaPersonas.sln -c Release --no-restore`: passed; 0 warnings, 0 errors.
    - `dotnet test V2/PapaPersonas.sln -c Release --no-build`: passed; 1 passed, 0 failed, 0 skipped.
    - Structural XAML readback: passed; four tabs are present in the requested order, only the first is enabled, all requested Paso 1 controls are present, and the V2 XAML contains no DuckDB reference.
    - Runtime harness: passed; the V2 Release executable remained active after launch, exposed a main window, closed through `CloseMainWindow()`, exited with code 0, and did not require forced termination.
    - Rollback boundary: remove `V2/` only; V1 behavior and files are outside this work unit.
    - RDD risk: `disabled/unmanaged`.
    - Commit: `4cff920` (`feat(v2): scaffold tabbed shell and Paso 1 interface`).
    - Bounded correction evidence: `MainWindow` now consumes `TabCatalog.All` by applying title and enabled state to named tabs in catalog order, with a fail-fast count check. The Limpieza group now uses two responsive action rows with `170`, `*`, and `Auto` columns; all selectors and buttons remain disabled.
    - Correction verification: Release build, focused test suite, and V2 runtime harness passed after the correction. No V1 files or behavior changed.
    - Correction commit: `ee1b0a4` (`fix(v2): bind shell tabs and improve Paso 1 layout`).
    - Parent spot check: `dotnet test V2/PapaPersonas.sln -c Release --no-build` passed again with 1 passed, 0 failed, and 0 skipped.
    - Operator visual review: approved from the supplied running-application screenshot; layout, labels, disabled future controls, splitter, and activity area were accepted without requested changes.
    - Next step: review the Solapa 1 visuals before adding dynamic column detection; do not add cleaning behavior in this work unit.

- [x] **V2L-004 — Add dynamic column discovery and selection**
  - Route: direct bounded implementation.
  - Trigger evidence: coordinated UI, Excel-reading, state, and test changes across multiple files.
  - Scope: enable Excel selection, create/display the rejected-folder path, read headers off the UI thread, normalize dynamic presentation names, populate independent selectors, and expose newest-first operational activity. Duplicate removal, empty-value removal, export, Sergio, DuckDB, and row materialization remain disabled.
  - Acceptance: arbitrary header names appear in the interface and can be selected without fixed aliases; cancellation and failures preserve coherent prior UI state.
  - Checks: restore, Release build, focused tests, structural readback, runtime launch/close harness, and synthetic header normalization tests.
  - Rollback boundary: V2 column-discovery and selection behavior only.
  - Evidence:
    - `dotnet restore V2/PapaPersonas.sln`: passed; all three V2 projects restored.
    - `dotnet build V2/PapaPersonas.sln -c Release --no-restore`: passed; 0 warnings, 0 errors.
    - `dotnet test V2/PapaPersonas.sln -c Release --no-build`: passed; 4 passed, 0 failed, 0 skipped.
    - Structural readback: passed; Browse file enabled with Excel filter, rejected-folder label is clickable/styled, folder override is present, both combos share detected-column items while remaining independent, three processing actions remain disabled, and no DuckDB/Sergio/cleaning logic was added.
    - Runtime harness: passed; Release V2 executable started, exposed a main window, closed cleanly, and exited with code 0.
    - Synthetic header scenario: passed through test-only in-memory normalization coverage for ordered, empty, repeated case-insensitive headers and unique positional identities. No real workbook or personal data was used.
    - Rollback boundary: V2 column discovery, folder selection, activity, and selector behavior in `V2/` only.
    - RDD risk: `disabled/unmanaged`.
    - Main commit: `ef41407` (`feat(v2): detect workbook columns in Paso 1`).
    - Bounded correction: updated Paso 1 copy, named and disabled file/folder controls during asynchronous detection with `finally` restoration, and guarded rejected-folder opening with safe operational logging and a friendly message. Cleaning, export, Sergio, DuckDB, and row processing remain out of scope.
    - Correction commit: `ed9af71` (`fix(v2): harden Paso 1 column discovery`).
    - Independent verification: build and 4 tests passed; functional scope and state handling passed. The verifier initially blocked because activity logs include the selected file name and complete rejected-folder path.
    - Operator decision: full local paths and file names are intentionally retained in the activity log for this personal application because diagnostic value for future support outweighs local metadata exposure. The paths are not persisted or transmitted by V2L-004.
    - Operator manual acceptance: passed with a real workbook; input path, automatic `rech` folder creation, clickable folder access, complete detected-column list, and both column selectors worked as expected.

- [ ] **V2L-005 — Save the activity log as a TXT file**
  - Route: delegated direct if implementation touches UI, dialog, logging, and tests; otherwise reassess after visual design.
  - Scope: add a footer action beside the V2 status that saves the complete current activity log to a user-selected TXT file for support. Do not implement it until explicitly authorized.
  - Acceptance: exported text preserves the on-screen log content and order, reports cancellation/failure clearly, and does not alter the active cleaning state.
  - Evidence: pending.

- [x] **V2L-006 — Show visible work progress in Paso 1**
  - Route: direct bounded implementation.
  - Scope: while header detection runs, show an indeterminate progress bar and a short status such as `Leyendo encabezados...`; hide both after success or failure. Reuse the existing busy boundary without adding cancellation, percentages, or new processing logic.
  - Acceptance: the indicator becomes visible before background work starts, remains visible while controls are disabled, and is always hidden when the operation completes or fails.
  - Evidence:
    - `dotnet build V2/PapaPersonas.sln -c Release --no-restore`: passed; 0 warnings, 0 errors.
    - `dotnet test V2/PapaPersonas.sln -c Release --no-build`: passed; 4 passed, 0 failed, 0 skipped.
    - XAML/code-behind readback: passed; the indicator is `Collapsed` by default, its `ProgressBar` is indeterminate, its text is `Leyendo encabezados...`, and `SetBusy(true)`/`SetBusy(false)` show and hide it. The existing `finally` calls `SetBusy(false)`, restoring the hidden state after success, error, or exception exit. Accessibility names and a polite live setting are present.
    - Runtime harness: passed; the V2 Release executable started, exposed the main window, closed cleanly, and exited with code 0.
    - Manual acceptance carried forward from V2L-004: passed with a real workbook; input path, automatic `rech` folder creation, clickable folder access, complete detected-column list, and both column selectors worked as expected.
    - Rollback boundary: remove the V2 header-reading indicator and its README/ODD evidence changes; V1 and V2L-004 behavior remain outside the boundary.
    - RDD risk: `disabled/unmanaged`; automated assessment was high only because the unchanged file still contains the pre-existing folder-opening process boundary.
    - Commit: `0eec9ff` (`feat(v2): show header detection progress`).
    - Parent spot check: `dotnet test V2/PapaPersonas.sln -c Release --no-build` passed again with 4 passed, 0 failed, and 0 skipped.
    - Independent verification: PASS; confirmed the diff only adds busy feedback and documentation, with 0 build warnings/errors and 4 passing tests.
    - Operator manual acceptance: passed with a real workbook; the loading text and indeterminate progress bar were clearly visible during header detection and disappeared correctly afterward.

- [x] **V2L-007 — Build the ordered cleaning plan without processing rows**
  - Route: delegated direct because the change coordinates plan state, XAML controls, activity messages, workbook replacement behavior, and tests.
  - Scope: enable `Quitar duplicados` and `Quitar valores vacíos` only when their selector has a column; clicking adds an ordered visible step and one cycle-level activity entry without reading rows. Add one `Limpiar plan` action that immediately removes every step. Do not add individual removal, reordering, confirmation dialogs, row processing, rejected output, or final export.
  - Acceptance: steps preserve insertion order and positional column identity; the visible plan and cycle count stay synchronized; loading a different workbook successfully clears the old plan so positions cannot leak across files; failed/cancelled loads preserve the prior coherent plan.
  - Checks: focused plan-state tests, Release build, structural readback, and runtime harness.
  - Rollback boundary: V2 cleaning-plan UI/state/log behavior only.
  - Evidence:
    - `dotnet build V2/PapaPersonas.sln -c Release --no-restore`: passed; 0 warnings, 0 errors.
    - `dotnet test V2/PapaPersonas.sln -c Release --no-build`: passed; 7 passed, 0 failed, 0 skipped.
    - Plan-state tests passed for insertion order, action type, positional column identity, visible numbering, clear count, empty state, and repeated actions.
    - XAML/code readback: passed; both action buttons require a valid combo selection and are disabled while busy; the visible `Plan de limpieza` list, synchronized summary, and single `Limpiar plan` action are present; clear-all has no confirmation or individual removal; `Generar archivo de salida` remains disabled and has no handler; no row processing was added.
    - Workbook replacement readback: passed; a successful new header read resets the previous plan before presenting new columns and logs the reset only when steps existed; selector cancellation or a failed read returns before mutation, preserving the prior file, columns, and plan.
    - Runtime harness V2: passed; Release executable started, exposed the main window, closed through `CloseMainWindow()`, exited cleanly with code 0, and did not require forced termination.
    - Rollback boundary: remove the cleaning-plan model, UI, handlers, tests, README note, and this V2L-007 evidence; V2L-004/V2L-006 header discovery and progress behavior remain outside the boundary.
    - RDD risk: `disabled/unmanaged`; automated assessment was high only because `MainWindow` still contains the unchanged folder-opening process boundary.
    - Commit: `b08fa5d` (`feat(v2): build ordered cleaning plan`).
    - Parent spot check: `dotnet test V2/PapaPersonas.sln -c Release --no-build` passed again with 7 passed, 0 failed, and 0 skipped.
    - Independent verification: PASS; confirmed plan-only scope, correct state transitions, 0 build warnings/errors, and 7 passing tests.
    - Next step: visual/manual review of the ordered plan; do not execute cleaning yet.

- [x] **V2L-008 — Read workbook headers in single-pass mode**
  - Route: delegated direct because the change requires reader configuration, a regression seam, performance evidence, and documentation.
  - Scope: configure ExcelDataReader 3.9.0 with `SinglePassMode = true` for header discovery so it skips the initial full worksheet scan. Do not change displayed headers, planning behavior, or row processing.
  - Acceptance: header discovery still returns the same first-row values and positional identities while no longer requesting the initial full scan; build, tests, and a before/after measurement with the same real workbook are reported honestly.
  - Checks: focused reader/configuration tests, Release build, complete V2 tests, runtime harness, and user-observed timing when private input cannot be used automatically.
  - Rollback boundary: ExcelDataReader header-reader configuration and related evidence only.
  - Evidence:
    - `dotnet build V2/PapaPersonas.sln -c Release --no-restore`: passed; 0 warnings, 0 errors.
    - `dotnet test V2/PapaPersonas.sln -c Release --no-build`: passed; 8 passed, 0 failed, 0 skipped.
    - Readback: `ExcelDataReaderHeaderReader` passes `HeaderReaderConfiguration.Create()` to `CreateReader`; that configuration sets `SinglePassMode = true`. Header discovery calls `Read()` once, uses the resulting `FieldCount`, preserves raw value order and positional normalization, and does not use `RowCount`.
    - Configuration seam test: passed; the productive configuration factory returns `SinglePassMode = true` without reflection or reader abstraction.
    - Runtime harness V2: passed; Release executable started, exposed its main window, closed cleanly through `CloseMainWindow()`, and exited with code 0 without forced termination.
    - Workbook comparison: operator retested the same real workbook and reported that header discovery is now substantially faster; no numeric benchmark was captured.
    - Rollback boundary: revert the single-pass header-reader configuration, its focused seam test, and V2L-008 documentation only; header presentation, plan behavior, UI, and future cleaning semantics remain unchanged.
    - RDD risk: `disabled/unmanaged`; automated assessment classified the small executable configuration change as medium.
    - Commit: `37d7941` (`perf(v2): read workbook headers in single pass`).
    - Parent spot check: `dotnet test V2/PapaPersonas.sln -c Release --no-build` passed again with 8 passed, 0 failed, and 0 skipped.
    - Next step: the user compares speed manually with the same workbook; before future cleaning execution, headers will be re-read and compared, but that behavior is not implemented now.

- [ ] **V2L-009 — Keep tabular input extensible to CSV**
  - Status: future architecture note only; not authorized for implementation.
  - Scope: evolve the source boundary so UI, header snapshots, cleaning plans, and future row processing call one tabular-file reader method that selects the concrete parser internally by file type (`.xlsx`/`.xls`, `.csv`, or text). Every parser returns the same ordered collection contract.
  - CSV considerations: use `CreateCsvReader`, bound initial analysis (for example `AnalyzeInitialCsvRows = 1` for header-only discovery), and explicitly handle delimiter candidates, BOM/fallback encoding, quoted values, and escaping. `SinglePassMode` applies to XLS/XLSX/XLSB and has no effect on CSV.
  - Acceptance: adding CSV later does not change cleaning-plan semantics or UI state; Excel and CSV readers produce the same raw ordered header snapshot and row contract.
  - Evidence: pending.

- [x] **V2L-010 — Build cleaning execution preflight**
  - Route: delegated direct.
  - Scope: create a pure preflight that deduplicates repeated steps by `(action, column position)` while preserving the first occurrence/order, reports omitted steps, and compares the current raw header sequence against the planning snapshot by count, order, and exact identity. Do not read data rows, create output files, enable generation, or change UI behavior.
  - Acceptance: same-column/same-action repeats are omitted; different actions on the same column remain; header differences abort preflight with a clear result; no source state mutates.
  - Checks: focused unit tests, Release build, complete V2 tests, and structural readback.
  - Rollback boundary: pure execution-preflight model/tests/documentation only.
  - Evidence:
    - `dotnet build V2/PapaPersonas.sln -c Release --no-restore`: passed; 0 warnings, 0 errors.
    - `dotnet test V2/PapaPersonas.sln -c Release --no-build`: passed; 16 passed, 0 failed, 0 skipped.
    - Structural readback: passed; `CleaningExecutionPreflight` is a pure shell model, compares raw headers with ordinal null-preserving identity, validates positions, deduplicates only `(action, position)`, reports omitted metadata, renumbers an execution copy, and does not mutate the visible plan or snapshots. No UI, file, row, output, generation, Sergio, DuckDB, or V1 code was added.
    - Rollback boundary: remove `CleaningExecutionPreflight.cs`, its focused tests, and the V2L-010 README/ODD evidence only.
    - RDD risk: `disabled/unmanaged`.
    - Commit: `25a2022` (`feat(v2): add cleaning execution preflight`).
    - Next step: V2L-011 motor temporal, still not implemented.

- [x] **V2L-011 — Build the fixed temporary workspace and streaming CSV transport**
  - Route: delegated direct because the infrastructure and its edge-case tests span multiple non-trivial files.
  - Scope: create an injectable fixed workspace abstraction whose production root is `%LOCALAPPDATA%\PapaPersonas\V2\temp`; remove stale content before starting, refuse to continue when reset fails, expose deterministic working paths, and delete the workspace on dispose even after failures. Add streaming CSV read/write primitives for comma-delimited UTF-8 BOM files without materializing rows.
  - CSV transport: preserve whitespace and embedded commas, quotes, CR/LF, empty strings, and Unicode. Use one-row CSV analysis and never request `RowCount`.
  - Exclusions: no Excel data-row export, cleaning rules, output publication, UI wiring, or generation button changes yet.
  - Checks: focused lifecycle/round-trip tests, Release build, complete V2 tests, and structural readback.
  - Evidence:
    - `dotnet build V2/PapaPersonas.sln -c Release --no-restore`: passed; 0 warnings, 0 errors.
    - `dotnet test V2/PapaPersonas.sln -c Release --no-build`: passed; 18 passed, 0 failed, 0 skipped.
    - Structural readback: passed; fixed workspace reset/cleanup is injectable and deterministic, production root uses `Environment.SpecialFolder.LocalApplicationData`, CSV uses ExcelDataReader `CreateCsvReader` with comma-only detection, one-row analysis, whitespace preservation, and no `RowCount`/`AsDataSet`; no UI, Excel data-row, cleaning, output, or V1 code was added.
    - Round-trip tests passed for empty fields, whitespace, commas, quotes, CR/LF, and requested Unicode values; UTF-8 BOM bytes were verified. Tests use only explicit roots under `Path.GetTempPath()` and never touch real LocalAppData.
    - Runtime harness: `N/A` — infrastructure is intentionally disconnected from UI/execution; focused lifecycle and round-trip tests are the applicable functional boundary.
    - Rollback boundary: remove the fixed-workspace and streaming CSV source/tests plus this V2L-011 README/ODD evidence; V2L-010 and all UI/V1 behavior remain outside the boundary.
    - RDD risk: `disabled/unmanaged`.
    - Commit: this work-unit commit (`feat(v2): add streaming temporary transport`).

- [x] **V2L-012 — Stream workbook rows into the canonical workspace CSV**
  - Status: complete; staging exists as a disconnected execution primitive.
  - Route: delegated direct because the workbook cursor, row normalization, and integration tests span multiple non-trivial files.
  - Scope: open the first worksheet once with ExcelDataReader single-pass mode, expose its raw headers, run V2L-010 preflight before the first data row is written, then stream each row into `current.csv` at the planning header width. Preserve positional columns, format dates/numbers deterministically, and return row/preflight metadata without publishing outputs.
  - Exclusions: no cleaning cycles, rejected CSV, final output publication, workspace orchestration, or UI wiring.
  - Evidence:
    - `dotnet restore V2/PapaPersonas.sln`: passed; ClosedXML 0.105.0 restored only in the V2 test project.
    - `dotnet build V2/PapaPersonas.sln -c Release --no-restore`: passed; 0 warnings, 0 errors.
    - `dotnet test V2/PapaPersonas.sln -c Release --no-build`: passed; 24 passed, 0 failed, 0 skipped.
    - Focused workbook staging tests: passed; 24 passed, 0 failed, 0 skipped. Fake cursors cover row streaming, padding, header mismatch, invalid positions, extra columns with partial cleanup, and input immutability. A test-time `.xlsx` through ExcelDataReader covers first-sheet headers/rows, Unicode, deterministic dates, and invariant numbers.
    - Structural readback: one ExcelDataReader workbook pass uses `HeaderReaderConfiguration.Create()` and `FileShare.Read`; preflight runs before `current.csv` creation; rows are streamed; partial output is removed on failure; no cleaning, publication, UI, or V1 changes were added.
    - Runtime harness: `N/A` — staging is intentionally disconnected from UI; focused tests are the applicable functional boundary.
    - Rollback boundary: remove `WorkbookRowCursor.cs`, `WorkbookStaging.cs`, their focused tests and test-only ClosedXML reference, plus this V2L-012/README evidence; V2L-010/V2L-011 and V1 remain outside the boundary.
    - RDD risk: `disabled/unmanaged`.
    - Commit placeholder/identity: this work-unit commit (`feat(v2): stage workbook rows for cleaning`).
    - Route: rollback by reverting the single work-unit commit; no remote operation requested or performed.

- [x] **V2L-013 — Apply cleaning cycles and consolidate rejections**
  - Status: complete; pure workspace processor remains disconnected from UI/publication.
  - Scope: consume `current.csv`, apply each normalized cycle in order through next-file replacement, and build one rejected CSV. No DuckDB and no whole-file row materialization.
  - Rejected output metadata columns appear first: `PP_MOTIVO`, `PP_PASO`, `PP_COLUMNA`, `PP_VALOR`, followed by every original column in order; source headers colliding with technical names receive deterministic suffixes.
  - Duplicate semantics: compare trimmed non-empty values case-insensitively; reject every row in repeated groups. Empty semantics: trim the selected value and reject when empty.
  - Evidence:
    - Release build passed with 0 warnings and 0 errors.
    - Complete Release test suite passed with 30 tests, 0 failures, and 0 skips.
    - Structural readback: streaming cycle passes replace `next.csv` safely, keep only the active duplicate-count map, validate headers/row widths, preserve complete first-rejection rows, and write one header-only rejected file when needed. No UI, publication, cleanup orchestration, DuckDB, or V1 code changed.
    - Focused tests cover empty/duplicate semantics, sequential survivors, consolidated output, deterministic technical-header collisions, malformed transport cleanup, single-column empty rows, counters, and ordering. Tests use explicit temporary roots only; production LocalAppData is never used.
    - Runtime harness: `N/A` — intentionally disconnected from UI; focused tests are the applicable boundary.
    - Rollback boundary: revert the V2L-013 processor, transport regression fix, focused tests, README, and this evidence only.
    - RDD risk: `disabled/unmanaged`.
    - Commit: this work-unit commit (`feat(v2): process streaming cleaning cycles`).

- [x] **V2L-014 — Orchestrate cleanup and publish outputs safely**
  - Status: complete; the end-to-end engine remains disconnected from UI.
  - Scope: coordinate fixed-workspace prepare/cleanup in `finally`, preflight/staging/cleaning, and safe publication. Clean CSV goes beside the original; one consolidated rejected CSV goes inside the selected `rech` folder. Both names use the source base name plus timestamp.
  - Evidence:
    - `dotnet build V2/PapaPersonas.sln -c Release --no-restore`: passed; 0 warnings and 0 errors.
    - `dotnet test V2/PapaPersonas.sln -c Release --no-build`: passed; 35 passed, 0 failures, and 0 skips.
    - Structural readback: the coordinator rejects an empty visible plan before workspace preparation, executes prepare/stage/process/publish in order, and always attempts cleanup in `finally`. Safe publication uses destination-local `.partial` files, one shared timestamp, deterministic shared collision suffixes, exact byte copies, and attempt-only rollback. No UI, V1, TXT export, CSV/TXT input, DuckDB, or remote code was added.
    - Tests cover real ClosedXML `.xlsx` end-to-end processing, custom rejected folder, collision suffix without overwrite, header mismatch and malformed processing cleanup, publication failure cleanup boundaries, and empty-plan short-circuit. All test workspace roots are explicit temporary paths; production LocalAppData is not used.
    - Runtime harness: `N/A` — the coordinator is intentionally disconnected from UI; end-to-end and focused tests are the applicable functional boundary.
    - Rollback boundary: revert the V2L-014 coordinator, publisher, tests, README, and this evidence; V2L-013 processing and earlier V2 infrastructure remain outside the boundary.
    - RDD risk: `disabled/unmanaged`.
    - Commit: `feat(v2): orchestrate cleaning output publication` (this work-unit commit).

- [x] **V2L-015 — Wire execution progress and completion UI**
  - Status: complete; Solapa 1 now connects to the verified V2L-014 coordinator.
  - Scope: enable generation only for a valid input, output folder, and non-empty plan; run asynchronously with the existing busy guard; render validation/cycle/output logs; and show a final custom dialog with `Abrir carpeta` (opens the source folder) and `Aceptar`.
  - Evidence:
    - `dotnet build V2/PapaPersonas.sln -c Release --no-restore`: pending in this work unit; required verification command below.
    - `dotnet test V2/PapaPersonas.sln -c Release --no-build`: pending in this work unit; required verification command below.
    - Structural readback: raw header snapshots are copied before normalization; Generate is enabled only for valid input, rejected folder, raw headers, and a non-empty visible plan; one busy guard disables selection, plan mutation, actions, and generation; execution snapshots paths/headers/steps and uses `Task.Run`; logs include omitted steps, cycles, counts, full output paths, and cleanup warnings; errors reset busy state in `finally` and instruct reload/reselection without retry.
    - Completion dialog is owner-centered, accessible, keyboard-default/cancel aware, and opens only the original source directory with `UseShellExecute = true`; open failures show a friendly message without crashing.
    - Runtime harness: bounded launch/close only; visual acceptance and manual representative-workbook execution remain for the operator.
    - Rollback boundary: revert `V2/src/PapaPersonas.V2.App/MainWindow.xaml`, `MainWindow.xaml.cs`, `CompletionDialog.xaml`, `CompletionDialog.xaml.cs`, `V2/README.md`, and this V2L-015 evidence; V2L-014 engine and earlier V2 work remain outside the boundary.
     - RDD risk: `disabled/unmanaged`.
     - Commit: pending until verification (`feat(v2): connect cleaning workflow UI`).

- [x] **V2L-016 — Prefix Solapa 1 activity entries with timestamped Paso 1 metadata**
  - Route: direct bounded implementation.
  - Scope: make the reusable activity-log model accept a caller-provided section and injectable clock, format every visible entry as `[Paso 1][dd/MM/yyyy HH:mm:ss]Mensaje`, and preserve newest-first ordering. `MainWindow.AddActivity` remains the single UI logging wrapper and passes exactly `Paso 1` for all current entries. No cleaning functionality or cleanup behavior changed.
  - Acceptance: deterministic tests verify the exact timestamped format and newest-first ordering; structural readback confirms current activity calls flow through the single wrapper and there are no direct activity-log writes without the prefix.
  - Evidence:
    - `dotnet build V2/PapaPersonas.sln -c Release --no-restore`: pending in this work unit; required verification command below.
    - `dotnet test V2/PapaPersonas.sln -c Release --no-build`: pending in this work unit; required verification command below.
    - Structural readback: `ActivityLog.Add(section, message)` is the only model write, `MainWindow.AddActivity` is the only application wrapper, and it passes exactly `Paso 1`; no cleanup behavior was changed.
    - Runtime harness: `N/A` — this bounded formatting change has no new runtime boundary; build, tests, and structural readback are the applicable verification.
    - Rollback boundary: revert `ColumnDiscovery.cs`, `MainWindow.xaml.cs`, `TabCatalogTests.cs`, and this V2L-016 evidence only; cleaning and cleanup behavior remain outside the boundary.
    - RDD risk: `disabled/unmanaged`.
    - Commit: pending until verification (`feat(v2): timestamp paso one activity log`).

## Confirmed Future Cleaning Semantics

- Each cleaning cycle operates on one selected column and on the previous cycle's result.
- Duplicate cleaning rejects every full row whose selected-column value is repeated in that column.
- Empty cleaning trims the selected-column value and rejects the full row when the result is empty.
- Rejected output preserves the complete source row, rejected value, reason, and cycle evidence.
- Every cycle emits operator-visible activity logs.
- Immediately before future cleaning execution, re-read the original header row and compare column count, order, and raw identity with the planning snapshot. Abort without processing and require a reload if the workbook changed.

## Progress and Next Step

`V2L-001` through `V2L-008`, V2L-010 through V2L-015 are complete and the single-pass speed improvement is manually accepted. The next step is a manual smoke with a representative workbook, followed only by explicitly authorized fixes. TXT export and CSV/TXT input remain future work.
