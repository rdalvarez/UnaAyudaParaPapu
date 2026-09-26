# PapaPersonas V2

V2 currently contains an independent WPF shell and the first bounded behavior for **Preparar archivo de Hernán**: selecting an Excel workbook reads only its headers, creates the default `rech` folder, exposes independently selectable dynamic columns, and shows visible indeterminate feedback while headers are being read. Cleaning, export, Sergio, DuckDB, and persistence remain disabled.

## Verification

From the repository root:

```powershell
dotnet restore V2/PapaPersonas.sln
dotnet build V2/PapaPersonas.sln -c Release --no-restore
dotnet test V2/PapaPersonas.sln -c Release --no-build
```

## Next step

The next step is a visual/manual review of the header-reading indicator before implementing cleaning or TXT export.
