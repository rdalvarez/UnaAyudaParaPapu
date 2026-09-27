# PapaPersonas V2

V2 currently contains an independent WPF shell and the first bounded behavior for **Preparar archivo de Hernán**: selecting an Excel workbook reads only its headers, creates the default `rech` folder, exposes independently selectable dynamic columns, and shows visible indeterminate feedback while headers are being read. Cleaning selections now build an ordered visible plan only; rows are not read or modified. Export, Sergio, DuckDB, and persistence remain disabled.

## Verification

From the repository root:

```powershell
dotnet restore V2/PapaPersonas.sln
dotnet build V2/PapaPersonas.sln -c Release --no-restore
dotnet test V2/PapaPersonas.sln -c Release --no-build
```

## Next step

The next step is a visual/manual review of the ordered cleaning plan. Do not execute cleaning yet.
