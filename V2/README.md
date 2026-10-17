# PapaPersonas V2

V2 now connects **Preparar archivo de Hernán** end to end. The Sergio tab performs accepted-header preflight, stages every workbook row in DuckDB, and reports the selected PK plus imported, valid, empty, duplicate-group, duplicate-row, and rejected counts. `CLAVE_VACIA` covers null/blank trimmed keys; `CLAVE_DUPLICADA` covers every row in a non-empty duplicate group. Mixed results require explicit **Continuar**/**Cancelar**; cancellation performs no official mutation while retaining the candidate and export action. Zero valid rows block preview/promotion but still permit export.

**Exportar rechazados** writes `PP_MOTIVO` followed by source columns to the sibling `rech` directory, asynchronously, and logs only count and full path. **Vista previa y cargar** shows only valid rows and states imported, valid-to-load, rejected-excluded, and PK values before **Cargar**/**Reemplazar**. Promotion atomically replaces `sergio_current`, stores valid-row count in metadata, and retains the complete import as `sergio_last_import` so rejected export remains available. Failures retain a retryable candidate. Storage is `%LOCALAPPDATA%\PapaPersonas\V2\papapersonas-v2.duckdb`.

The footer now reports dynamic DuckDB lifecycle status: initializing, ready without official Sergio load, ready with official load (safe source filename + official row count), inconsistent, and unavailable. `Reiniciar base` requires explicit Spanish confirmation of permanent data loss and no backup; cancellation is the default. A confirmed reset permanently deletes only the configured DuckDB file and its `.wal` sidecar, creates no backup, and initializes a fresh DB without official Sergio load. If deletion fails before the database file is removed, the previous database stays. If prior data was deleted and fresh initialization fails, that data is not restored and is not recoverable. If no database or `.wal` existed and creation fails, the failure does not claim that a previous database was deleted or preserved. Reset is disabled while any operation or status refresh is busy.

## Stock (Generación y consulta de stock)

One current operational Stock, built from the official Sergio load. **Consultas** stays disabled.

### Operator flow

1. Load the official Sergio file first (see above).
2. In the Stock tab, pick the **Código** and **Descripción** columns. The primary key is the one used at Sergio import and is shown read-only.
3. **Generar stock** and confirm the destructive prompt (cancellation is the default).
4. Review per-code totals (total, sold, reserved, available), edit names, then **Guardar nombres**.
5. Export summary or detail, or enter quantities and **Extraer selección** (optional sale reference).
6. If an extraction stays pending, use **Reintentar**, **Guardar en otra ruta**, or **Cancelar pendiente**. Completed extractions can be reexported.

### Tables

Created only by an explicit generation, never by Sergio promotion or startup.

| Table | Role |
|-------|------|
| `stock_data` | Literal copy of `sergio_current`: same business columns, same order, VARCHAR primary key. |
| `stock_status` | `member_id`, `sold`, `extraction_id` (membership of each extraction). |
| `stock_extractions` | Extraction history: state, dates, optional uppercase reference, export/recovery configuration. |
| `stock_headers` | Singleton origin context: source file, load time, row count, selected PK/code/description aliases. |

### Behavior

| Topic | Behavior |
|-------|----------|
| Independence | The snapshot does not change when Sergio is replaced. A different official source only shows an informational Spanish warning; Stock operations continue. |
| Regeneration | Needs destructive confirmation. Clears stock data, sold rows, extraction history and pending references. Preserves the names catalog. No backup. |
| Pending guard | Regeneration is blocked until a pending extraction is retried or cancelled. |
| Field selection | Code and description are chosen on every generation; no mapping is remembered. Aliases are validated before anything is replaced. |
| Grouping | Code is `Trim` + `ToUpperInvariant`. Empty codes stay visible as one group labelled `Sin código`. |
| Names | Stored in a separate V2 JSON catalog: `%LOCALAPPDATA%\PapaPersonas\V2\config\stock-obras-sociales.json`. Not shared with V1, not deleted by **Reiniciar base**. Any nonempty description seeds a missing code; existing names are never overridden. Invalid JSON is left untouched and suggestions stay in memory. A catalog save failure after a committed generation returns Completed with a warning. |
| Reference | Optional free text, one per extraction, stored uppercase. |
| Detail CSV | Summary plus detail; detail has dynamic columns with mandatory PK, code and description exactly once. Only-available excludes sold rows. |
| Safe export | Each attempt writes a unique owned temp file. An existing different destination is never overwritten and the extraction stays pending; use **Guardar en otra ruta** (the actual path is persisted). A matching already-published file is finalized without overwrite. No double sale. |
| Busy guard | Stock actions register global busy; Stock mutations and **Reiniciar base** are disabled while busy. The window does not close while an operation or DuckDB status refresh is busy. |
| Origin comparison | The Stock header compares the file and load timestamp used to generate stock against current Sergio official metadata. Same file with different load time is treated as a separate load. Non-blocking amber text clarifies stock is unchanged. |

Not in scope: customers, collections, financial model, ledgers kept after regeneration.

### Stock verification status

| Check | Result |
|-------|--------|
| STK001 focused / full | 21/21 / 135/135 |
| STK002 focused / full | 16/16 / 151/151 |
| STK003 focused / full | 26/26 / 159/159 |
| STK005 focused / full | 38/38 / 173/173 |
| Final Release build | 0 warnings, 0 errors |
| Final full suite (parent rerun) | 173/173 |

One independent full-suite run had a transient environmental `UnauthorizedAccessException` on `CleaningCycleProcessorTests.Cycles_are_sequential_and_rejections_are_consolidated_once` (`File.Move` on a temp file; 158 passed, 1 failed, outside Stock). The root cause was not established. The isolated test passed 1/1 on rerun, followed by the passing build and full suite above.

Tests use temporary directories only; the real DuckDB file was never touched. No GUI visual smoke or app launch was run. Work is uncommitted.

### Manual smoke checklist (outstanding, not automated)

- [ ] Stock tab opens; Consultas stays disabled.
- [ ] Generate with code/description selection; confirm cancel is the default.
- [ ] Regenerate is blocked with a pending extraction; retry/cancel unblocks it.
- [ ] Replace Sergio, then confirm the newer-source warning does not disable actions.
- [ ] Extract with and without a reference; retry, save elsewhere, reexport.
- [ ] Window close is refused while an operation is running.

## Verification

From the repository root:

```powershell
dotnet restore V2/PapaPersonas.sln
dotnet build V2/PapaPersonas.sln -c Release --no-restore
dotnet test V2/PapaPersonas.sln -c Release --no-build
```

## V2L-008

Header discovery configures ExcelDataReader 3.9.0 with `SinglePassMode = true`, then calls `Read()` once and reads the observed `FieldCount` values in their original order and positions. `RowCount` is not used. The same workbook should be compared manually by the user for timing; this change records no invented before/after measurement.

The pure cleaning execution preflight now compares the re-read raw headers with the planning snapshot, validates column positions, and creates a deduplicated executable copy without mutating the visible plan. It is not connected to execution yet; no rows are read and no output is generated.

V2L-011 adds reusable fixed-workspace and streaming CSV infrastructure. The production workspace is `%LOCALAPPDATA%\PapaPersonas\V2\temp`, while tests use explicit temporary-root overrides. The infrastructure remains disconnected from UI and execution; V2L-012 is next.

## Current verification boundary

DDS-002 verification uses focused footer presentation tests, full Release tests, a Release build without restore, and diff check. WPF runtime smoke is N/A in automated checks because this work unit verifies state mapping and async wiring through tests/build only; no deterministic GUI harness is available in CI-like execution here.
