# PapaPersonas V2

V2 now connects **Preparar archivo de Hernán** end to end. The Sergio tab performs accepted-header preflight, stages every workbook row in DuckDB, and reports the selected PK plus imported, valid, empty, duplicate-group, duplicate-row, and rejected counts. `CLAVE_VACIA` covers null/blank trimmed keys; `CLAVE_DUPLICADA` covers every row in a non-empty duplicate group. Mixed results require explicit **Continuar**/**Cancelar**; cancellation performs no official mutation while retaining the candidate and export action. Zero valid rows block preview/promotion but still permit export.

**Exportar rechazados** writes `PP_MOTIVO` followed by source columns to the sibling `rech` directory, asynchronously, and logs only count and full path. **Vista previa y cargar** shows only valid rows and states imported, valid-to-load, rejected-excluded, and PK values before **Cargar**/**Reemplazar**. Promotion atomically replaces `sergio_current`, stores valid-row count in metadata, and retains the complete import as `sergio_last_import` so rejected export remains available. Failures retain a retryable candidate. Storage is `%LOCALAPPDATA%\PapaPersonas\V2\papapersonas-v2.duckdb`.

The footer now reports dynamic DuckDB lifecycle status: initializing, ready without official Sergio load, ready with official load (safe source filename + official row count), inconsistent, and unavailable. `Reiniciar base` requires explicit confirmation, moves the current DB to a dated backup, and initializes a fresh DB without official Sergio load. Reset is disabled while any operation or status refresh is busy.

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
