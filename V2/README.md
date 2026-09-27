# PapaPersonas V2

V2 now connects **Preparar archivo de Hernán** end to end: selecting an Excel workbook preserves its raw headers, exposes normalized dynamic columns, builds an ordered plan, executes it asynchronously through the verified coordinator, publishes paired CSV outputs, and reports evidence in the newest-first activity log. The Sergio tab now provides a lightweight header-only preflight: it selects `.xlsx`/`.xls`, reads only the first header row, proposes deterministic aliases for empty/exact duplicate headers, and requires Continue/Cancel when anomalies exist. It does not read rows, create DuckDB, stage data, or write outputs. Progress/status and the activity log are global shell surfaces shared by all tabs; the activity log remains newest-first. Current limits are Excel input only, no cancellation, no per-row Sergio UI, no TXT log export, no CSV/TXT source, and no Sergio DuckDB processing; Stock and Consultas remain disabled.

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

The next step is a user-performed manual smoke with a representative workbook; visual acceptance and execution timing are not claimed here. Only explicitly authorized follow-up fixes should be made. TXT export and CSV/TXT input remain future work.
