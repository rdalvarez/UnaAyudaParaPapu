# PapaPersonas V2

V2 currently contains an independent WPF shell and the first bounded behavior for **Preparar archivo de Hernán**: selecting an Excel workbook reads only its headers in ExcelDataReader single-pass mode, creates the default `rech` folder, exposes independently selectable dynamic columns, and shows visible indeterminate feedback while headers are being read. Cleaning selections now build an ordered visible plan only; rows are not read or modified. Export, Sergio, DuckDB, and persistence remain disabled.

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

## Next step

The next step is V2L-012: execute cleaning through the fixed temporary files. V2L-011 infrastructure is not connected to execution yet. Do not execute cleaning yet.
