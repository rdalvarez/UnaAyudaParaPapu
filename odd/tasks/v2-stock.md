# V2 Stock

Build one current operational Stock from the official Sergio load, without letting a later Sergio replacement change sold rows, exports, history, or the names catalog.

## Quick path

1. Implement STK001 in Shell plus focused tests: snapshot, summary, and the separate names catalog.
2. Implement STK002 in the same backend: CSV export and extraction recovery.
3. Implement STK003 in WPF: enable Stock, wire selection and confirmation, and keep Sergio/reset busy guards.
4. Close STK004 with README and this task evidence. Do not stage, commit, push, or open a PR.
5. Implement STK005 in Shell, WPF, and tests: compact Stock header comparison block, read-only official metadata exposure, non-blocking origin status, and collapsed warning layout.

## Approved scope

One current Stock. Four DuckDB tables, created only by an explicit generation, never by Sergio promotion or startup inspection:

| Table | Contract |
|-------|----------|
| `stock_data` | Literal complete copy of dynamic `sergio_current`: same business columns, same order, same VARCHAR primary key. No CUIL requirement. |
| `stock_status` | Minimal operational state: `member_id` plus `sold` plus `extraction_id`. |
| `stock_extractions` | Metadata, state, dates, optional uppercase sale reference, and export/recovery configuration. |
| `stock_headers` | Singleton origin and mapping context for the current Stock. |

Approved behavior:

- The snapshot is independent. Replacing Sergio must not alter stock, exports, sold rows, or history. A newer official source is an informational warning only and must not block Stock.
- Regeneration requires explicit destructive confirmation. It replaces all operational stock, sold rows, history, pending state, and references. It preserves the separate names catalog.
- Preserve the V1 guard: a pending extraction must be resolved before regeneration. Do not silently erase reservations. No real resets or regenerations for testing.
- Each generation asks the user for the code column and the description column. Do not save or preselect those mappings across generations.
- The primary key is the same column selected at Sergio import. Keys stay VARCHAR. Do not require CUIL.
- Normalize the OS code group as V1: `Trim` plus `ToUpperInvariant`. Names do not split groups. Empty codes remain visible and keep V1 extraction behavior.
- Display label for an empty code is `Sin código`, by the latest approved wording. V1 currently displays `(Vacío)` in `Paso4ObraSocialPresentation.EmptyDisplayName`. This is a documented display-label decision, not a grouping or extraction change.
- Description seed: any nonempty description for a code is acceptable. Do not require first row, file order, or frequency. Persist by normalized code only when no saved name exists. Never override an existing name automatically. Names stay editable and save as in V1.
- Use a separate V2 JSON catalog. Do not share the V1 catalog. It survives regeneration. Do not add a schema ordinal only to choose a name.
- Summary counts are total, sold, and available. CSV has summary plus detail. Detail columns are dynamic, with mandatory PK, code, and description, deduplicated when those columns overlap. The only-available option stays consistent with V1.
- Extraction is by quantity and group. Prevent oversale and double sale. Keep pending reservations and output-safe finalization. Pending retry/cancel and completed reexport must work after reload, with stable membership and configuration.
- The optional sale reference is free text, one per extraction, may be blank, and is stored uppercase.
- No customers, financial system, or retained ledgers after regeneration.
- Keep the feature simple. Do not add speculative extras.

### Stock header origin comparison (STK005)

Compact comparison block directly below 'Generación y consulta de stock' informing whether stock is generated or absent, comparing source files with load timestamps, displaying stock generation timestamp separately, and providing non-blocking origin status:

- Two source files compared WITH load dates/times:
  1. File used for current Stock (`StockHeader.SourceFileName` and `StockHeader.LoadedAtUtc`).
  2. Current official Sergio metadata (`SourceFileName` and `LoadedAtUtc`).
- Stock generation timestamp displayed separately (`StockHeader.GeneratedUtc`).
- Status values:
  - No stock present: `Sin generar`.
  - Same load: `Generado con la carga actual`.
  - Differing source: `Generado con una carga anterior`, accompanied by amber non-blocking explanatory text indicating stock is unchanged and can be used or regenerated when the user decides. Do not call it invalid, error, or desactualizado.
  - Official source absent: `Sin archivo cargado`; stock remains fully usable.
- Comparison rules:
  - Identity metadata only: compare `SourceFileName`, `LoadedAtUtc`, `RowCount`, and `PrimaryKeyAlias`.
  - Same file name with different `LoadedAtUtc` is treated as a distinct load.
  - Do not compare filename only; do not read XLSX files or perform content/hash comparisons.
  - Read-only DB metadata constraints: read existing `sergio_load_metadata` (via `ReadMetadata`). No new tables, schema changes, DDL, or DML.
  - Honest refresh: failure to retrieve current metadata clears or reports state honestly without showing stale file data.
- Layout and presentation:
  - Separate transient operation in-progress text and catalog warnings from persistent stock-origin status.
  - Collapse absent warning blocks (`Visibility="Collapsed"`) to eliminate empty vertical gaps.
  - Grid counts remain by grid; manual column selectors for each generation remain unchanged.

## Constraints

| Topic | Decision |
|-------|----------|
| Branch | `feat/v2-lightweight` at `47ff04a6721369cafaf6545669cdaa2d3b01b350` |
| Product | V2 only. V1 is reference and must not be edited. |
| SDD | Not used. No `sdd-init`. |
| TDD | Off. Source: `odd/tasks/v2-duckdb-dynamic-status.md` line 32, "TDD mode: off; ordinary behavior-first integration tests and Release build are required." Red-green TDD is not required. |
| Runner | `dotnet test V2/PapaPersonas.sln -c Release --no-build` after `dotnet build V2/PapaPersonas.sln -c Release --no-restore`. Focused filters use `V2/tests/PapaPersonas.V2.Tests/PapaPersonas.V2.Tests.csproj`. |
| Language | Spanish for new comments and user-facing alerts, warnings, confirmations, and errors. English identifiers and technical docs. |
| Tests | Temporary directories only. Never touch `%LOCALAPPDATA%\PapaPersonas\V2\papapersonas-v2.duckdb`. |
| Dirty baseline | Preserve and do not revert: `V2/README.md`, `V2/src/PapaPersonas.V2.App/MainWindow.xaml.cs`, `V2/src/PapaPersonas.V2.Shell/SergioDuckDbLifecycle.cs`, `V2/tests/PapaPersonas.V2.Tests/SergioDuckDbFooterPresenterTests.cs`, `V2/tests/PapaPersonas.V2.Tests/SergioDuckDbLifecycleTests.cs`, `odd/tasks/v2-duckdb-dynamic-status.md`, untracked `AGENTS.md`. Shared files may only be extended while preserving those changes. |
| Delivery | `ask-on-risk`. Forecast is above 400 authored lines. No staging, commit, push, or PR is authorized. |
| Review budget | 400 lines is advisory per task. Do not omit tests or compress code to fit it. |

## Design decisions

| Topic | Decision |
|-------|----------|
| Placement | Stock services live in `PapaPersonas.V2.Shell`, beside Sergio. Tests stay in `PapaPersonas.V2.Tests`. WPF stays in `PapaPersonas.V2.App`. Do not add a Core/Infrastructure split for this feature. |
| Reuse | Copy the V1 guarantees, not the V1 architecture: pending-before-regenerate, normalized-code grouping, atomic reservation, output-safe finalize, retry/cancel, and completed reexport. |
| Copy | `stock_data` is `CREATE TABLE ... AS SELECT` of every business column from `sergio_current`, then the same table-level VARCHAR primary key. Do not project into a fixed person schema. |
| Member identity | `stock_status.member_id` stores the copied VARCHAR primary-key value. It is not a new surrogate and not CUIL. |
| Header | One `stock_headers` row stores source file name, loaded-at timestamp, official row count, selected PK alias, code alias, description alias, and generated timestamp. It does not store a reusable column mapping for the next generation. |
| Pending | Pending metadata lives on the single `stock_extractions` row in `pending` state: token, started time, output path, expected rows, selected detail columns, and optional reference. Regeneration reads that state inside the write transaction and fails closed, matching `DuckDbPaso4StockService.GenerateOrRegenerateStock`. |
| Sale | A completed extraction sets `stock_status.sold` and `extraction_id` only after the output is safely finalized. Cancel releases unsold reservations. A failed finalize leaves the pending reservation recoverable. |
| History | `stock_extractions` is the only extraction history. Regeneration deletes operational stock tables and their extraction rows. It does not keep a ledger. |
| Catalog | `%LOCALAPPDATA%\PapaPersonas\V2\config\stock-obras-sociales.json`, overridable in tests. Atomic temp-file replace, as in `Paso4ObraSocialCatalogStore`. Invalid JSON uses suggestions in memory and does not overwrite the file. |
| Warning | Stock compares `stock_headers` source identity with `sergio_load_metadata`. A difference returns a Spanish informational warning. Stock operations continue. |
| Lock | Mutating Stock operations take `SergioStagingAnalyzer.AcquirePromotionLock`, the same gate used by promotion, export, inspection, and reset. |
| UI tests | `PapaPersonas.V2.Tests` does not reference the WPF app. Put dialog decisions and button-state rules in Shell types so they are testable without launching the app. |
| Reset | Existing confirmed DuckDB reset already deletes the whole configured database. Do not add a second Stock reset. The names JSON is outside that file and is not deleted by database reset. |

## Tasks

- [x] **STK001 — Snapshot, summary, and names catalog**
  - Route: delegated direct. The snapshot, status, header, summary, catalog store, and integration tests span multiple non-trivial files.
  - Start: official `sergio_current` and `sergio_load_metadata` exist; Stock tables do not.
  - Finished: an explicit generation copies the full official table, records the selected code and description aliases, summarizes by normalized code, and seeds only missing catalog names.
  - Acceptance: Sergio replacement after generation leaves `stock_data`, sold/status rows, and extraction rows unchanged while the overview warning changes. Regeneration without the pending resolved returns the V1-equivalent validation failure and changes nothing. Confirmed regeneration replaces operational rows and preserves the JSON catalog. Empty codes remain one extractable group. Any nonempty description may seed a missing name; an existing catalog name is never replaced. No mapping is persisted for the next generation.
  - Checks: `dotnet test V2/tests/PapaPersonas.V2.Tests/PapaPersonas.V2.Tests.csproj -c Release --filter "FullyQualifiedName~StockSnapshotTests|FullyQualifiedName~StockCatalogTests"`; `dotnet build V2/PapaPersonas.sln -c Release --no-restore`; `dotnet test V2/PapaPersonas.sln -c Release --no-build`; `git diff --check`.
  - Rollback: revert only the new Stock snapshot/catalog files, their tests, and this task's evidence. Do not revert the preexisting dirty baseline.
  - Evidence: implemented in `V2/src/PapaPersonas.V2.Shell/StockSnapshot.cs`, `V2/tests/PapaPersonas.V2.Tests/StockSnapshotTests.cs`, and `V2/tests/PapaPersonas.V2.Tests/StockCatalogTests.cs`. Initial focused filter passed 11/11, Release build passed with 0 warnings and 0 errors, and the full V2 suite passed 125/125.
  - Correction: code and description aliases are validated against the official schema before any stock table is dropped. A missing, blank, or unknown alias fails validation and preserves an existing snapshot; the first generation creates no stock tables. A catalog save failure after commit returns `Completed` with a Spanish warning and a usable summary. An unconfirmed retry does not regenerate. Invalid catalog JSON stays untouched, and the summary still shows one grouped in-memory suggestion for a missing code. `MIN(TRIM(description))` remains the approved any-nonempty seed. `stock_status.extraction_id` remains the membership column; no fifth table was added. The generation holds `AcquirePromotionLock` across the commit, catalog seed, and summary read without calling `GetSummary`.
  - Correction checks: focused filter passed 21/21. Release build passed with 0 warnings and 0 errors. Full V2 suite passed 135/135. `git diff --check` passed with preexisting line-ending warnings only. Runtime harness: N/A, because this slice has no UI and app launch is not authorized. Not committed: staging, commit, and push were not requested.

- [x] **STK002 — Export and extraction recovery**
  - Route: delegated direct. CSV writers, reservation, finalize, retry/cancel, and reexport span multiple non-trivial files and must ship with their tests.
  - Start: STK001 can create one current Stock and report available quantities.
  - Finished: summary/detail export and quantity extraction are recoverable after process reload without changing membership or configuration.
  - Acceptance: detail CSV includes dynamic `stock_data` columns plus mandatory PK, code, and description exactly once when aliases overlap. Only-available excludes sold rows. Quantity above availability, a second pending extraction, and a double sale are rejected with no partial sold rows. Pending retry finalizes the same reserved members; pending cancel releases them. Completed reexport repeats the stored configuration. A blank reference stays blank; a supplied reference is stored uppercase, one per extraction.
  - Checks: `dotnet test V2/tests/PapaPersonas.V2.Tests/PapaPersonas.V2.Tests.csproj -c Release --filter "FullyQualifiedName~StockExportTests|FullyQualifiedName~StockExtractionTests"`; `dotnet build V2/PapaPersonas.sln -c Release --no-restore`; `dotnet test V2/PapaPersonas.sln -c Release --no-build`; `git diff --check`.
  - Rollback: revert only the export/extraction service files, their tests, and this task's evidence. Leave the STK001 snapshot intact.
  - Evidence: implemented in `V2/src/PapaPersonas.V2.Shell/StockSnapshot.cs`, `V2/tests/PapaPersonas.V2.Tests/StockExportTests.cs`, and `V2/tests/PapaPersonas.V2.Tests/StockExtractionTests.cs`. The service file is above the advisory 400-line budget because export, reservation, retry, cancel, and reexport share the same four-table contract. It was not split or compressed. Initial focused filter passed 11/11. Release build passed with 0 warnings and 0 errors. Full V2 suite passed 146/146.
  - Correction: each export attempt owns a unique temp file and deletes only that file. A published destination that matches the regenerated pending output is finalized without overwrite. A different existing file stays untouched and pending; `RetryPending(alternateOutputPath)` can save it elsewhere and persist that path. Cancel does not delete unrelated temp files. The technical sold header is unique even when `VENDIDO` and `ESTADO_VENDIDO` are business columns. `beforePublish` runs once per publication.
  - Correction checks: focused filter passed 16/16. Release build passed with 0 warnings and 0 errors. Full V2 suite passed 151/151. `git diff --check` passed with preexisting line-ending warnings only. Runtime harness: N/A, because this slice has no UI and app launch is not authorized. Not committed: staging, commit, and push were not requested.

- [x] **STK003 — Stock WPF integration**
  - Route: delegated direct. Enabling the tab, dialogs, async commands, and busy coordination touch shared WPF files plus new Shell presentation types.
  - Start: STK001 and STK002 services are callable without UI.
  - Finished: the Stock tab can generate, warn, export, extract, retry, cancel, and reexport through explicit Spanish confirmation.
  - Acceptance: `TabCatalog` enables `Generación y consulta de stock` and leaves Consultas disabled. Generation requires both column selections in the current session and a destructive confirmation whose default is cancellation. A pending extraction blocks regeneration until retry or cancel. The newer-source warning does not disable operations. Stock mutations and DuckDB reset stay disabled while either operation is busy. User-facing text is Spanish. No app launch is required for acceptance.
  - Checks: `dotnet test V2/tests/PapaPersonas.V2.Tests/PapaPersonas.V2.Tests.csproj -c Release --filter "FullyQualifiedName~StockPresentationTests|FullyQualifiedName~TabCatalogTests"`; structural readback of `MainWindow.xaml`, `MainWindow.xaml.cs`, and `TabCatalog.cs`; `dotnet build V2/PapaPersonas.sln -c Release --no-restore`; `dotnet test V2/PapaPersonas.sln -c Release --no-build`; `git diff --check`.
  - Runtime harness: N/A for automated acceptance. The test project does not reference the WPF app, and app launch is not authorized. Presentation rules must be pure Shell tests.
  - Rollback: revert only Stock tab wiring, new dialogs, presentation helpers/tests, and this task's evidence. Preserve preexisting `MainWindow.xaml.cs` DuckDB status changes.
  - Evidence: `TabCatalog` enables Stock and leaves Consultas disabled. `StockProcessControl` owns the tab UI. `StockPresentation` holds the pure button, alias, quantity, and confirmation rules. Initial focused filter passed 24/24. Release build passed with 0 warnings and 0 errors. Full V2 suite passed 157/157.
  - Correction: Stock operations call `SetBusy` through `Attach(..., setGlobalBusy)` before the first await and clear it in `finally`. A local busy guard blocks double-clicks and refreshes during an edit. WPF values are captured before `Task.Run`. Export columns rebuild when the snapshot schema changes and mandatory columns stay selected. Save names is guarded and does not claim success on failure. Result text uses `Completed` only. Focused filter passed 26/26. Release build passed with 0 warnings and 0 errors. Full V2 suite passed 159/159. `git diff --check` passed with preexisting line-ending warnings only. Runtime harness: N/A. No app launch and no visual smoke were run. Not committed: staging, commit, and push were not requested.
  - Final evidence: the final independent check found no close guard. `MainWindow` `Closing` now cancels when `_isBusy || _isDuckDbStatusBusy`, logs a Spanish message, and returns before `CleanupAll`. Final parent state: isolated flaky-run test 1/1, Release build 0/0, full suite 159/159, `git diff --check` passed with line-ending warnings. See STK004 for the transient environmental failure.

- [x] **STK004 — Verification docs**
  - Route: docs-general (documentation agent), not inline. Update `V2/README.md` and this task record only.
  - Start: STK001-STK003 have recorded commands and results.
  - Finished: the README states the Stock contract, catalog path, warning behavior, and verification commands without claiming an unrun GUI smoke.
  - Acceptance: README mentions the four tables, separate V2 catalog, pending-before-regenerate guard, and informational newer-source warning. This document records exact command results. No source behavior changes in this task.
  - Checks: structural readback of `V2/README.md` and this file; `git diff --check`.
  - Runtime harness: N/A. Documentation has no runtime boundary.
  - Rollback: revert only the README Stock section and STK004 evidence.
  - Evidence: `V2/README.md` gained a Stock section (operator flow, four-table roles, behavior table, verification status, manual smoke checklist) placed before `## Verification`; the earlier no-backup reset text is preserved. Claims were checked against `StockSnapshot.cs` (catalog `config/stock-obras-sociales.json`, `Sin código`, `Trim`+`ToUpperInvariant`, uppercase reference, four `CREATE TABLE`, source warning), `StockProcessControl.xaml`, `TabCatalog.cs`, and `MainWindow.xaml.cs` (Stock attach with global busy; `Closing` sets `args.Cancel = true` when `_isBusy || _isDuckDbStatusBusy`, logs in Spanish and returns before `CleanupAll`). Documentation readback only; no code changes, no tests run by this task. Not committed: staging, commit, and push were not requested.
  - Final state (supersedes partial snapshots): independent verification corrections for STK001-STK003 passed; the missing close guard was added inline in `MainWindow` by the parent. One independent full-suite run showed an environmental `UnauthorizedAccessException` on `CleaningCycleProcessorTests.Cycles_are_sequential_and_rejections_are_consolidated_once` (`File.Move` temp; 158 pass, 1 fail, outside Stock). Root cause not proven and not hidden. Parent reran that test in isolation (1/1 pass), then Release build 0 warnings/0 errors, full suite 159/159, and `git diff --check` passed with line-ending warnings only.
  - Limits: RDD is off by default with no native lifecycle; native assessment could not classify because the untracked scope was undeclared, so it was treated as unassessable/high and covered by independent functional verification. No real database touched. No GUI smoke or app launch. Manual smoke checklist is outstanding.

- [x] **STK005 — Stock header comparison and UI refinement**
  - Route: delegated direct. Multi-file scope spanning Shell metadata exposure and presentation helpers, WPF XAML/codebehind header block, and focused unit/integration tests.
  - Start: STK001-STK004 operational; Stock displays a single origin line and basic warnings; `StockOfficialSource` exposes only PK/columns/CanGenerate; warning blocks leave empty vertical gaps when absent.
  - Finished: a compact header block below 'Generación y consulta de stock' displays stock status ('Sin generar', 'Generado con la carga actual', 'Generado con una carga anterior' with amber explanatory text, or 'Sin archivo cargado' if official source is missing), compares source files with load timestamps, shows generation timestamp separately, separates in-progress/catalog messages from origin status, collapses empty warning blocks, and preserves counts and manual selectors.
  - Acceptance:
    - Status evaluation:
      - When no stock exists: status is 'Sin generar', file and generation timestamp show empty/absent state.
      - When stock exists and official load matches (same file name and loaded_at timestamp): status is 'Generado con la carga actual'.
      - When stock exists and official load differs (different file name OR different loaded_at timestamp, even if file name matches): status is 'Generado con una carga anterior', accompanied by amber non-blocking explanatory text clarifying stock is unchanged and can be used or regenerated when the user decides. The status must not be described as an error, invalid, or desactualizado.
      - When stock exists but official load is missing/unloaded: official status indicates 'Sin archivo cargado' while stock remains usable.
    - Identity metadata comparison only: comparison relies strictly on stored identity metadata (`SourceFileName`, `LoadedAtUtc`, `RowCount`, `PrimaryKeyAlias`). No XLSX reading, no content hashing, and no file-name-only comparisons.
    - Read-only database constraints: new metadata exposure is strictly read-only from existing `sergio_load_metadata`. No new tables, schema changes, DDL, or DML.
    - UI and layout: in-progress operation text and catalog warnings are distinct from origin status. Absent warning blocks are collapsed (`Visibility="Collapsed"`) to eliminate empty gaps. Grid counts and manual generation selectors remain unchanged.
    - Honest refresh: failure to refresh current metadata clears or reports state honestly without displaying stale file data.
  - Checks:
    - Focused tests: `dotnet test V2/tests/PapaPersonas.V2.Tests/PapaPersonas.V2.Tests.csproj -c Release --filter "FullyQualifiedName~StockPresentationTests|FullyQualifiedName~StockSnapshotTests"`
    - Build: `dotnet build V2/PapaPersonas.sln -c Release --no-restore`
    - Full suite: `dotnet test V2/PapaPersonas.sln -c Release --no-build`
    - Diff check: `git diff --check`
  - Runtime harness: N/A for automated acceptance. Presentation rules tested as pure Shell types in `PapaPersonas.V2.Tests`; no app launch authorized.
  - Rollback: revert only the STK005 header changes in `StockSnapshot.cs`, `StockProcessControl.xaml`, `StockProcessControl.xaml.cs`, their tests in `StockPresentationTests.cs` and `StockSnapshotTests.cs`, and this task entry. Preserve preexisting dirty baseline and prior STK001-STK004 implementations.
  - Evidence: implemented in `V2/src/PapaPersonas.V2.Shell/StockSnapshot.cs`, `V2/src/PapaPersonas.V2.App/StockProcessControl.xaml`, `V2/src/PapaPersonas.V2.App/StockProcessControl.xaml.cs`, `V2/tests/PapaPersonas.V2.Tests/StockPresentationTests.cs`, and `V2/tests/PapaPersonas.V2.Tests/StockSnapshotTests.cs`. `StockOfficialSource` exposes `StockSourceIdentity? Identity` read-only from `sergio_load_metadata`. `StockPresentation.EvaluateOrigin` provides pure presentation logic comparing identity metadata without reading XLSX. `StockProcessControl` binds the compact origin container with collapsible warning blocks, preserving grid counts and manual selectors. Initial focused filter passed 36/36, Release build passed with 0 warnings and 0 errors, and full V2 suite passed 171/171.
  - Correction: `StockSummary.SourceWarning` guards its read-only probe; metadata read errors or corrupted schema return an informational Spanish warning without crashing `GetSummary()`. Empty metadata (0 rows) reports absent rather than newer. Source identity comparison reuses `StockPresentation.SourcesMatch`. When `GetOfficialSource` fails in UI refresh, `officialUnavailable: true` marks official status unavailable while existing Stock remains usable. Focused filter passed 38/38 (24 prior + 14 new), Release build passed with 0 warnings and 0 errors, full V2 suite passed 173/173 (159 prior + 14 new), and `git diff --check` passed with preexisting line-ending warnings only. Visual smoke was not run. Work is uncommitted.

## Forecast and delivery

Forecast: above 400 authored lines cumulatively for STK001-STK004. For STK005 alone, the forecast is honest and bounded under 400 lines (~180-250 authored lines across Shell metadata exposure/presentation helpers, WPF XAML/codebehind, and focused unit/integration tests). Do not omit tests or compress code to fit the advisory budget.

Delivery: `ask-on-risk`. Implementation may proceed because the parent explicitly requested it, but no staging, commit, push, or PR is authorized. If a later review asks for a PR, stop and ask before creating one.

Route trigger: delegated direct for STK001-STK003 and STK005 because each one spans multiple non-trivial production and test files. STK004 was routed to docs-general (documentation agent), not inline.

## Rollback limits

| Boundary | What revert can undo | What it cannot undo |
|----------|----------------------|---------------------|
| Code | New Stock files, Stock tests, Stock README text, and this task evidence. | Preexisting dirty DuckDB/AGENTS work. Those files may only be extended. |
| Real data | Nothing in this task may mutate the real V2 DuckDB file. | A later confirmed Stock regeneration or the existing confirmed DuckDB reset deletes operational data with no backup. Reverting code does not restore it. |
| Catalog | Tests must use an overridden directory. | A later real save can change the V2 JSON catalog. Reverting code does not restore that file. |

## Progress

- [x] Task document created before source edits.
- [x] Parent read `odd/tasks/v2-stock.md` and mirror `#3389` before STK001.
- [x] STK001 source and tests. Initial 11/11 and 125/125. Correction 21/21, build 0/0, full suite 135/135. Commit not created.
- [x] STK002 source and tests. Initial 11/11 and 146/146. Correction 16/16, build 0/0, full suite 151/151. Commit not created.
- [x] STK003 WPF integration. Initial 24/24 and 157/157. Correction 26/26, build 0/0, full suite 159/159. Visual smoke not run. Commit not created.
- [x] STK004 README and evidence. Documentation readback done; see STK004 evidence. Not committed.
- [x] STK005 — Stock header comparison and UI refinement. Initial 36/36 and 171/171; correction 38/38, build 0/0, full suite 173/173. Visual smoke not run. Commit not created.
- [x] Delivery: STK001-STK005 authorized for local commit. Committed alongside no-backup DuckDB reset due to shared MainWindow.xaml.cs and V2/README.md integration. All 173 tests pass in Release mode with 0 build warnings.
- [ ] Manual GUI smoke checklist (in `V2/README.md`) outstanding. Not an automated pass.

Historical context: the task document was created in a preparation step that had no source edits, test runs, app launches, or git writes. Those restrictions no longer describe the current state; implementation exists as uncommitted work. The Stock source files (`StockSnapshot.cs`, `StockProcessControl.xaml/.cs`, tests) are untracked and must be retained.
