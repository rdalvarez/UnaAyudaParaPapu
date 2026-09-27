# V2 Sergio Import

## Objective

Build the second V2 tab incrementally: inspect Sergio workbook headers first, require explicit consent for empty or duplicate headers, and only then prepare a safe future DuckDB load.

## Problem and Why

Sergio workbooks can contain empty or repeated headers. Persisting those headers directly would make SQL identifiers ambiguous. The operator must see the issue and decide whether deterministic aliases are acceptable before any row scan or database mutation begins.

## Authorized Scope

- Work only in PapaPersonas V2.
- Enable the `Ingresar archivo de Sergio` tab.
- Select `.xlsx`/`.xls`, read only the first row with the existing single-pass header reader, and detect exact case-sensitive empty/duplicate headers.
- If anomalies exist, show a blocking Continue/Cancel confirmation with the proposed aliases.
- Continue accepts the workbook and exposes aliased headers plus primary-key selection; Cancel leaves no accepted Sergio file/header state.
- Default the primary-key selector to `CUIL` when that exact aliased header exists.
- Use global progress and activity with `[Paso 2]`.
- Do not read data rows, create DuckDB, stage data, validate key values, or write outputs in this work unit.

## Constraints

- Empty headers: `Columna Vacia`, `Columna Vacia (1)`, `Columna Vacia (2)`, ...
- Exact repeated headers: first unchanged, then `(1)`, `(2)`, ...
- Every displayed/SQL candidate alias must be deterministically unique, including collisions with raw headers that already contain suffix-like text.
- Header analysis must remain lightweight and must not scan workbook rows.
- TDD mode: off; ordinary focused tests and Release build are required.
- Delivery strategy: `ask-on-risk`; forecast is below 400 authored changed lines for this work unit.

## Tasks

- [x] **SER-001 — Add Sergio header preflight**
  - Route: delegated direct because UI, dialog, models, tests, and documentation span multiple non-trivial files.
  - Acceptance: second tab enabled; header-only asynchronous read; clean headers accepted directly; anomalies require Continue/Cancel; aliases shown before acceptance; cancel clears pending Sergio state; exact `CUIL` is selected by default; logs use `[Paso 2]`; no row or DuckDB work.
  - Checks: focused unit tests, Release build, complete V2 tests, bounded launch smoke, and structural readback.
  - Rollback: revert only Sergio tab/preflight UI, model/tests, tab enablement, and this feature document.
  - Evidence: `SergioHeaderPreflight` is pure and preserves raw names/positions while creating ordinal, globally unique aliases with ordinal comparison. The WPF Sergio tab uses the existing `IHeaderReader` asynchronously, shows one owner-centered scrollable Continue/Cancel dialog for anomalies, clears accepted state on Cancel, defaults exact `CUIL`, and logs through `[Paso 2]`. Stock and Consultas remain disabled. Tests cover clean, duplicate, case-sensitive, empty, suffix-collision, immutability, CUIL, and catalog behavior. Verification: `dotnet build V2/PapaPersonas.sln -c Release --no-restore`; `dotnet test V2/PapaPersonas.sln -c Release --no-build`; `git diff --check`; structural readback confirmed no Sergio row iteration, DuckDB, staging, or outputs. Bounded launch/close smoke: not run in this environment; visual acceptance is not claimed.
  - RDD: disabled/unmanaged.
  - Commit: `87acc93` (`feat(v2): add sergio header preflight`).

- [x] **SER-002 — Prioritize primary-key candidates**
  - Route: delegated direct because selector policy, UI wiring, tests, and task evidence span multiple files.
  - Acceptance: available exact aliases `CUIL`, `CUIT`, and `DNI` appear first in that order; all remaining aliases preserve source order; exact `CUIL` remains the only automatic default; the operator can select any available alias; duplicate suffixed aliases remain later independent choices.
  - Checks: focused selector tests, Release build, complete V2 tests, and structural readback.
  - Rollback: revert only primary-key option ordering and this task evidence.
  - Evidence: `SergioPrimaryKeySelection.Options` returns only detected aliases, promotes available exact case-sensitive `CUIL`, `CUIT`, and `DNI` in that order, then preserves all other aliases in source order without duplicates. Suffix aliases such as `CUIL (1)` remain in the source-order remainder. `DefaultAlias` selects only exact `CUIL`; when absent, the ComboBox remains unselected and the existing warning is preserved. `MainWindow` binds the ComboBox to the pure options function and performs no row, value, DuckDB, or anomaly-preflight work. Tests cover arbitrary ordering, absent preferred aliases, case sensitivity, suffixed aliases, deduplication, alias preservation, and exact-CUIL-only defaulting. Verification: `dotnet build V2/PapaPersonas.sln -c Release --no-restore`; `dotnet test V2/PapaPersonas.sln -c Release --no-build`; `git diff --check`; structural readback confirmed exact priority/default policy and no row/DB work. Bounded launch/close smoke: not run in this environment; visual acceptance is not claimed.
  - RDD: disabled/unmanaged.
  - Commit: `31bc336` (`feat(v2): prioritize sergio primary keys`).

- [x] **SER-003 — Stream Sergio into VARCHAR staging and validate the selected key**
  - Route: delegated direct because DuckDB infrastructure, streaming workbook conversion, UI state, tests, and documentation span multiple non-trivial files.
  - Scope: create/open the V2 DuckDB file under `%LOCALAPPDATA%\PapaPersonas\V2`; stream the accepted Sergio workbook once into a staging table whose aliased columns are all `VARCHAR`; preserve source values with deterministic text conversion; validate the selected key using `TRIM()` for empty and duplicate values; retain a valid staging candidate for a later confirmation/promote task; remove the candidate on validation or load failure. Do not replace the official table yet.
  - Conversion: strings remain unchanged; null stays null; integral numbers have no decimal suffix; non-integral numbers use invariant round-trip text; dates/times use invariant ISO text; booleans use lowercase invariant text.
  - Acceptance: analysis requires an accepted workbook and selected PK; global progress/logs use Paso 2; one workbook row pass; all aliases become quoted DuckDB `VARCHAR` columns; exact header snapshot is revalidated before rows; result reports total rows, empty-key rows, duplicate-key groups/rows, and validity without exposing values; invalid keys leave no candidate table; a valid candidate remains isolated and the existing official table is untouched.
  - Checks: focused conversion/SQL tests, real XLSX-to-DuckDB integration tests, Release build, complete V2 tests, structural readback, and bounded smoke.
  - Rollback: remove only V2 DuckDB staging/validation infrastructure, Sergio analyze UI/result state, focused tests, package reference, and this task evidence.
   - Evidence: Initial implementation independently blocked on accepted anomaly handling: `ValidateRequest` incorrectly treated raw `HasIssues` as invalid after Continue. The correction validates non-empty columns, non-empty aliases, exact Ordinal alias uniqueness, complete unique positions, and exactly one selected alias; accepted duplicate/empty raw headers now stage all-VARCHAR aliases, including independently selectable duplicate aliases. Candidate cleanup is instance-owned only. At first `Analyze` after application restart, a thread-safe one-time sweep removes only exact lowercase `sergio_staging_<32 hex>` names; near matches, arbitrary prefix tables, and current ownership are preserved. The crash/restart cleanup is intentionally before the first Analyze because no pending candidate survives an application restart yet. Primary and cleanup failures are both retained in `SergioStagingException.CleanupException`; cancellation remains a domain exception. Tests cover formatter behavior, accepted anomalies, duplicate-key alias selection, retained-candidate cleanup, ownership isolation, exact stale cleanup, and explicit non-production temp DuckDB paths. No official Sergio table or metadata mutation exists. Verification: `dotnet build V2/PapaPersonas.sln -c Release --no-restore`; `dotnet test V2/PapaPersonas.sln -c Release --no-build`; `git diff --check`. Bounded application launch/close smoke: not run; visual acceptance is not claimed. Structural readback: one workbook cursor pass, quoted aliases/all VARCHAR, exact header revalidation, instance-owned cleanup, exact stale cleanup, no official/metadata table, and no data values in logs.
  - RDD: disabled/unmanaged.
   - Commit: `c764557` (`feat(v2): validate sergio staging load`), `140b319` (`docs(v2): document sergio staging readiness`), correction commit `NEW` (`fix(v2): honor accepted sergio aliases`).

- [ ] **SER-004 — Preview and atomically promote the valid Sergio candidate**
  - Status: future work after SER-003 verification and product review.
  - Scope: show bounded preview and replacement warning, then atomically replace the single official Sergio table and update last-file/load-time metadata. Preserve the previous official execution on any failure.
  - Evidence: pending.

## Progress

SER-001 and SER-002 are complete. SER-003 implementation was initially independently blocked, then corrected and tested; verification checks pass and SER-003 is complete. RDD remains disabled/unmanaged. SER-004 will preview and atomically replace the prior official execution only after separate confirmation.
