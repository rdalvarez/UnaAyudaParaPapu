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
     - RDD risk: `disabled/unmanaged`.
    - Commit: pending until verification and staging are complete.

## Confirmed Future Cleaning Semantics

- Each cleaning cycle operates on one selected column and on the previous cycle's result.
- Duplicate cleaning rejects every full row whose selected-column value is repeated in that column.
- Empty cleaning trims the selected-column value and rejects the full row when the result is empty.
- Rejected output preserves the complete source row, rejected value, reason, and cycle evidence.
- Every cycle emits operator-visible activity logs.

## Progress and Next Step

`V2L-001` through `V2L-006` are complete, with V2L-004 manually accepted on a real workbook. The next step is visual/manual review of the V2L-006 indicator before any cleaning or TXT export work. TXT log export and cleaning remain future tasks.
