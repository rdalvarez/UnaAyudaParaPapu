# PapaPersonas

Initial skeleton for a local Windows desktop application built with **.NET 10 + WPF + DuckDB**.

## Prerequisites

- Windows 11
- .NET SDK 10.0.x

## Build, run, test

From `PapaPersonas` root:

```bash
dotnet restore PapaPersonas.sln
dotnet build PapaPersonas.sln -c Release
dotnet test PapaPersonas.sln -c Release
dotnet run --project src/PapaPersonas.App/PapaPersonas.App.csproj
```

## Incident-prevention smoke guidance (mandatory)

- For automated smoke, ALWAYS set `PAPAPERSONAS_DB_PATH` to a disposable temp database path.
- Never force-stop the app while footer DB status is still initializing.
- Do not run smoke against `%LOCALAPPDATA%\PapaPersonas\data\PapaPersonas.duckdb`.
- If DBeaver is connected for inspection, use read-only mode and disconnect before Process 2 writes/apply.

Example (PowerShell):

```powershell
$env:PAPAPERSONAS_DB_PATH = "$env:TEMP\PapaPersonas\smoke\PapaPersonas.duckdb"
dotnet run --project src/PapaPersonas.App/PapaPersonas.App.csproj -c Release
```

## WPF UI structure (current)

- Main window is a thin shell with:
  - fixed header (`PapaPersonas`),
  - top `TabControl` navigation,
  - fixed DB bootstrap status footer.
- Tabs:
  1. `Preparar datos de Hernán` (enabled)
  2. `Actualizar base de Sergio` (enabled)
  3. `Consultar datos` (enabled, V1 guided query/export)
  4. `Stock y extracciones` (enabled, V1 stock operativo)
- Process 1, 2, 3 y 4 están implementados como `UserControl`s separados.
- Each enabled tab has:
  - independent scrollable process form,
  - resizable activity console (`GridSplitter`),
  - independent console state preserved while app stays open.

### Activity console behavior

- Session-only memory (no persisted log files).
- Switching tabs preserves each process console history.
- Starting a new Process 1 run clears only Process 1 console.
- Process 2 `Analyze` starts a new execution and clears Process 2 console.
- Process 2 `Apply` appends to the same Process 2 console created by the latest Analyze.
- Message cap: last 500 entries per process console.
- Console entries include timestamp + severity + phase and avoid row-level PII.

### Process 2 Sergio headers

- Unknown source headers are listed before any database or staging mutation.
- The operator can **Continuar** to ignore every listed unknown header, or **Cancelar** without creating import state.
- Missing CUIL, duplicate raw headers, and aliases colliding on one canonical field remain structural blockers with no continue option.
- Only recognized columns enter staging, presence metadata, Apply SQL, personas, or the DuckDB schema.

## Process 3 scope (V1)

- Guided UI only (no raw SQL editor).
- Supports:
  - exact CUIL filter,
  - AND filters by allowed columns/operators,
  - import-date range filters,
  - selected columns,
  - paginated preview,
  - CSV export with progress and cancel.

Advanced/complex queries are intentionally out of V1 scope. Approved fallback for those cases is DBeaver under controlled read-only usage.

## Proceso 4 scope (V1): Stock y extracciones

- Guided UI only (no SQL editor) over current stock tables.
- Fecha selector limitado a las últimas 5 `fecha_importacion` disponibles.
- `Generar stock` crea stock operativo para una fecha.
- `Regenerar stock` es explícitamente destructivo para el stock vigente:
  - reemplaza miembros del stock,
  - limpia pendiente,
  - reinicia historial operativo de extracción/reexportación del stock anterior.
- Detección de stock desactualizado (stale) respecto del último import exitoso.
- Exportaciones CSV:
  - resumen (agregado por grupo),
  - completo (detalle por persona, con columnas seleccionables y opción sólo disponibles),
  - extracción por selección (por cantidades por grupo).
- Extracción multi-grupo con semántica todo-o-nada y recuperación por token:
  - Retry reintenta pendiente,
  - Cancel libera reservas no vendidas.
- Reexportación restringida a extracciones completadas del stock actual.

### Proceso 4: formato y privacidad

- CSV únicamente.
- UTF-8 con BOM.
- Delimitador fijo coma (`,`); delimitador configurable fuera de alcance V1.
- Consola y logs agregados: sin PII de fila (sin volcar payload de persona).
- `personas` (base madre) no se borra desde este proceso.

Más detalle operativo: `docs/PROCESO_4_STOCK.md`.

## Mantenimiento de base

La pestaña **Mantenimiento de base** cubre tres operaciones sobre la DuckDB activa:

- **Crear copia**: abre un `SaveFileDialog`, hace `CHECKPOINT`, cierra la conexión y recién después copia la base a destino. El nombre sugerido usa la última `fecha_importacion` disponible:
  - `<yyyy-MM-dd>_BaseMaestra_BK.duckdb`
  - fallback: `SinImportaciones_BaseMaestra_BK.duckdb`
- **Restaurar copia**: valida la copia seleccionada en una copia temporal antes de reemplazar la base activa; rechaza archivos vacíos, arbitrarios, no reconocidos o de una versión futura.
- **Reiniciar base**: preserva automáticamente el estado actual y recrea una base vacía con el esquema vigente.

Backup y restore requieren confirmar que DBeaver y toda herramienta externa DuckDB estén cerrados. Si no se obtiene acceso exclusivo, la operación falla de forma segura y se puede reintentar después; la aplicación nunca fuerza el cierre de otro programa.
Restore es un rollback operativo total: reemplaza personas, importaciones, stock, ventas y extracciones. El backup contiene únicamente el archivo DuckDB.
Después de restaurar, la aplicación refresca los Procesos 2, 3 y 4 y muestra de inmediato cualquier extracción pendiente restaurada para resolverla con **Reintentar** o **Cancelar**. Si falta el CSV externo, se usa el flujo existente de reintento/cancelación; esos archivos no forman parte del backup. Reiniciar refresca el estado sin mostrar ese diálogo.

Las operaciones destructivas guardan un resguardo automático en:

- `%LOCALAPPDATA%\PapaPersonas\backups`

Si la base actual está corrupta y no puede hacer `CHECKPOINT`, igual se preserva el archivo crudo y su `.wal` cuando existe.

### Process 3 privacy logging rule

- Preview grid may show row data to the operator.
- Activity console/logging must stay aggregate-only.
- Do not emit row-level PII values (CUIL, names, row payload values) in logs.

### Export workflow summary

1. Build query from guided filters and selected columns.
2. Preview with pagination (no full result materialization).
3. Export streams rows to a temporary CSV file.
4. On success, temp file is atomically renamed to target path.
5. On cancel/failure, partial temp files are cleaned up.

## Project structure

```text
PapaPersonas/
├─ SPEC_V1.md
├─ PapaPersonas.sln
├─ src/
│  ├─ PapaPersonas.App/            # WPF executable
│  ├─ PapaPersonas.Core/           # Core contracts
│  └─ PapaPersonas.Infrastructure/ # DuckDB bootstrap + persistence details
└─ tests/
   └─ PapaPersonas.Tests/          # xUnit tests
```

## Current bootstrap behavior

- On startup, the main window is shown immediately with an initializing status.
- Database initialization runs in the background (off the UI thread).
- The app initializes DuckDB at:
  - `%LOCALAPPDATA%/PapaPersonas/data/PapaPersonas.duckdb`
- Parent directory is created if missing.
- A transactional, idempotent migration runner is used:
  - `schema_metadata` stores applied schema versions.
  - migrations run in order.
  - schema version advances only after each migration succeeds.
  - concurrent/repeated `Initialize` calls for the same DB path are serialized inside the process.
  - schema version insertion is conflict-safe (`ON CONFLICT DO NOTHING`) as defense in depth.
- Main window is updated with either “database ready” or a friendly error after initialization finishes.

> Pre-release note: migration **v2** is still in bootstrap development and can be corrected in place before first user release.

## Schema overview (current)

- `personas`
  - Canonical V1 base table.
  - `cuil` is `VARCHAR` primary key.
  - Identifier/contact fields (DNI, CUIT, postal code, phones, etc.) are `VARCHAR` to preserve leading zeros and source formatting.
  - `fecha_nacimiento` uses `DATE`, `edad` uses `SMALLINT`.
  - `whatsapp_1..5` currently stored as `VARCHAR` to avoid losing source fidelity before profiling.
  - `fecha_actualizacion` is required (`TIMESTAMP NOT NULL`) for whole-row update tracking.

- `import_runs`
  - Tracks each import execution (`UUID` import id, stage/source type, file metadata, status, timestamps, and row-count summary fields for preview/logging flow).
  - Includes `source_columns_present_json` (`VARCHAR`, JSON text) to persist the exact source columns present in that import.

- `personas_staging`
  - Persistent staging table for canonical nullable values plus import metadata.
  - Includes `import_id`, `source_row_number`, validation outcome/error.
  - Includes nullable `anio` (`SMALLINT`).
  - Uses composite primary key `(import_id, source_row_number)`.
  - Uses foreign key `import_id -> import_runs(import_id)`.
  - `cuil` is nullable in staging to retain rows that will later be rejected by validations.

## Canonical column union and update contract

- Current canonical schema follows the widest known union of source columns.
- Observed source shapes:
  - workbook **0 template**: 36 headers (includes `ANIO`)
  - workbook **3 actual return sample**: 28 headers
- `anio` is implemented in both `personas` and `personas_staging` (migration v3).

Authoritative row-update semantics for existing CUIL (implemented in Process 2B):

- source column **absent** in imported file -> preserve existing value
- source column **present with non-empty value** -> replace existing value
- source column **present but empty** -> clear existing value
- update `fecha_actualizacion` when an import actually applies that row

This contract metadata is persisted through `import_runs.source_columns_present_json` so Apply can distinguish absent vs present-empty.

### Schema-evolution policy (unknown columns)

> Status: implemented end to end in Process 2.

- **Known optional column absent** from an import: preserve existing value (see update contract above). This is expected and silent.
- **Unknown/new column** found in a **Hernán** import: non-blocking **notice**. This stage never writes to `personas`, so it can proceed with a warning.
- **Unknown/new column** found in a **Sergio** import: the operator receives a deterministic list and may Continue to ignore it or Cancel before any import state is created. It is never auto-created or persisted.
- Missing CUIL, duplicate raw headers, and aliases colliding on one canonical field remain structural blockers and never offer Continue.
- Unknown headers are excluded from staging, canonical presence metadata, Apply SQL, `personas`, and the DuckDB schema.

## Indexing choices

Only a small set of indexes is created now:

- `personas(obra_social)`, `personas(codigo_postal)`, `personas(edad)` for the first priority filters from SPEC.
- `import_runs(started_utc)` for operational listing by latest run.
- `personas_staging(import_id, validation_outcome)` for per-run validation review.

We intentionally avoid indexing every filterable column until query patterns are measured.

## Migration status note

- Migrations are still pre-release in this bootstrap phase.
- v3 adds `anio` and source-column-presence metadata, but does **not** implement XLSX parsing or merge orchestration yet.

## Validation layer status (implemented in Core)

The validation contracts are implemented in `PapaPersonas.Core` and are now used by the Paso 1 non-UI processor for real XLSX reading and CSV generation.

- Header contract validation by import stage:
  - `HernanRaw` (preparation stage, feeds "Proceso de datos de Hernán"): requires only headers needed to generate the Sergio request file (`CUIL`, `CUIL_APENOM`, `CUIL_CODOS`, `CUIL_DESCRIPOS`, `CUIL_FECHANAC`, `CUIL_EDAD`). No fixed total column count is enforced or expected.
  - `HernanRaw`: known optional headers are accepted; extra/unknown headers do **not** invalidate this stage — they are returned as **notices** (non-blocking).
  - `SergioReturn` (persistence stage, feeds "Actualización de base de Sergio"): widest known union (36-template headers + accepted aliases from the 28-header sample); unknown/new headers are listed for explicit **Continuar/Cancelar** before any database state is created.
- Conservative normalization only (trim + case-insensitive compare).
- Unknown headers are stage-specific and asymmetric on purpose:
  - `HernanRaw`: notice (non-blocking) — this stage never writes to `personas`, so the risk of an unreviewed column is low.
  - `SergioReturn`: typed confirmation (non-structural) — an unknown column is ignored only after explicit Continue and never enters schema or import state.
- Duplicate source headers or alias collisions to the same canonical field are errors.
- Output includes source-header -> canonical-field mapping and present canonical field set.
- CUIL structural validation (implemented, both stages):
  - accepts ASCII 11 digits plain or formatted with spaces/hyphens,
  - rejects empty, invalid characters, or non-11-digit normalized values.

Intentional boundaries (not implemented yet):

- Argentine CUIL checksum validation is deferred.

## Paso 1 implemented: Proceso de datos de Hernán

## Proceso 2A implemented: Sergio staging preview with unknown-header confirmation

Process 2A is now implemented as application-facing infrastructure service:

- conservative Sergio header validation (`ImportStage.SergioReturn`) with typed unknown-header confirmation
- `import_runs` creation (`stage_type=sergio_return`, `status=analyzing`)
- streaming row load into `personas_staging`
- CUIL structural validation + duplicate-group rejection (all rows in duplicate groups)
- set-based preview counts (`valid/rejected/missing/malformed/duplicate`, `rows_to_insert`, `rows_to_update`)
- run status transition to `ready_for_confirmation`

See details in `docs/PROCESO_2_SERGIO.md`.

## Proceso 2B implemented: Sergio confirmation apply backend

Process 2B backend service is now implemented (library/API):

- validates import run exists and is `sergio_return + ready_for_confirmation`
- applies only valid staging rows for that import
- uses `source_columns_present_json` to preserve/clear/replace per field
- updates `fecha_actualizacion` for rows applied
- rejects double-apply by status guard
- optional rejected CSV export (`source_row_number`, `reason_code`, `normalized_cuil`)

## Proceso 2 WPF UI (implemented)

The desktop app now includes Process 2 analyze + confirmation apply flow:

- Process 2 card enabled; Process 3 is available in its own guided V1 tab.
- Analyze step (Process 2A) runs async and shows aggregate-only preview counts:
  - total/valid/rejected/missing/malformed/duplicate/invalid-typed,
  - rows to insert/update,
  - elapsed,
  - issues/notices,
  - technical `import_id` reference.
- Apply step (Process 2B) is gated by explicit confirmation warning:
  - warns that backup is recommended,
  - does not auto-create backup,
  - applies only the currently analyzed `import_id`.
- Pending apply ownership is invalidated and requires re-analyze when:
  - source path changes after analyze,
  - analyze fails,
  - apply fails,
  - apply succeeds.
- Process 2 summary is aggregate-only (no row data/PII values in UI text).
- Optional rejected CSV output folder with default path:
  - `%USERPROFILE%\Documents\PapaPersonas\Proceso2`
  - fallback: `%LOCALAPPDATA%\PapaPersonas\Proceso2`
- Busy-state lock prevents Process 1 and Process 2 from running simultaneously.
- Window close is blocked while analyze/apply is running.
- Successful apply clears current import ownership in UI state to avoid user-triggered double apply.

## Paso 1 WPF UI (implemented)

The desktop app now includes a usable Paso 1 screen for non-technical users:

- 3 process cards are visible; Process 1 and Process 2 are enabled.
- Input fields with browse actions:
  - Hernán XLSX file
  - Output folder
  - Config JSON (`config/PARA_HERNAN.json`)
- `Open/Edit Config` opens the JSON in the default editor.
- `Process Paso 1` runs asynchronously and blocks conflicting controls while processing.
- Closing the window is blocked during an active run with a friendly message.
- Result area shows only aggregate counts, elapsed time, and output paths.
- `Open Output Folder` opens the generated files location.

Defaults at startup:

- Config path: `AppContext.BaseDirectory\config\PARA_HERNAN.json`
- Output folder: `%USERPROFILE%\Documents\PapaPersonas\outputs\paso1`
  - fallback: `%LOCALAPPDATA%\PapaPersonas\outputs\paso1`

Config packaging:

- `config/PARA_HERNAN.json` is copied into app output as `config\PARA_HERNAN.json` using `CopyToOutputDirectory=PreserveNewest`.

What is implemented now:

- Programmatic non-UI pipeline to process a Hernán XLSX and generate:
  - Sergio request CSV with headers: `CUIL`, `APELLIDO_NOMBRE`, `CD_OS`, `DESCRIPCION O_S`, `FECHA_NAC`, `EDAD`
  - Rejected rows CSV with: `source_row_number`, `reason_code`, `raw_cuil`, `normalized_cuil`, `message`
- Hernán stage is preparatory and does **not** update DuckDB `personas`.
- Uses ExcelDataReader (`CreateReader` + row iteration) and UTF-8 CSV writer with explicit escaping.
- Supports two-pass flow:
  - pass 1: validate CUIL + classify duplicates from row-level CUIL values
  - pass 2: stream rows once to write both Sergio/rejected CSV outputs using classification map

Programmatic usage:

```csharp
var processor = new HernanPaso1Processor();
var result = processor.Process(new HernanPreparationRequest(
    inputFilePath: @"D:\OneDrive\papa\HERNAN y SERGIO\1 - PedidoOOSS_...xlsx",
    outputDirectory: @"D:\OneDrive\papa\PapaPersonas\out"));

if (!result.IsSuccess)
{
    // Check result.IsValidationFailure, result.Errors, result.Notices
}
```

CLI usage (implemented):

```bash
dotnet run --project src/PapaPersonas.Cli -- paso1-hernan --input "D:\OneDrive\papa\HERNAN y SERGIO\1 - PedidoOOSS_10_08_2026__HERNAN.xlsx" --output "D:\OneDrive\papa\PapaPersonas\outputs\paso1" --config "config/PARA_HERNAN.json"
```

CLI output safety:

- prints only aggregate summary, file paths, and notices/errors counts
- never prints row-level values

Generated outputs safety:

- `outputs/` CSV files may contain personal data.
- `outputs/` is git-ignored intentionally.
- Move/share generated files carefully and only to approved destinations.

Editable config for father/user:

- `config/PARA_HERNAN.json`
- user can edit output columns (`outputName`) and source headers (`sourceName`) for Paso 1 Sergio CSV generation
- validator blocks malformed/unsafe config (duplicate outputs, empty names, missing `CUIL <- CUIL` mapping)
- if a configured source header is missing in the input file, Paso 1 fails with clear validation error

Out of scope for this step:

- Base update/upsert merge logic from Sergio.
- Sergio persistence into `personas` (Proceso 2).

Performance note:

- `BatchCuilValidator` currently returns deterministic per-row results in memory.
- Keep this contract when wiring the importer; optimize/stream in that future integration step if needed for very large batches.
