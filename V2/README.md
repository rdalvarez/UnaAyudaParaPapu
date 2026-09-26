# PapaPersonas V2

V2 currently contains an independent WPF shell and the first bounded behavior for **Preparar archivo de Hernán**: selecting an Excel workbook reads only its headers, creates the default `rech` folder, and exposes independently selectable dynamic columns. Cleaning, export, Sergio, DuckDB, and persistence remain disabled.

## Verification

From the repository root:

```powershell
dotnet restore V2/PapaPersonas.sln
dotnet build V2/PapaPersonas.sln -c Release --no-restore
dotnet test V2/PapaPersonas.sln -c Release --no-build
```

## Next step

The next step is a visual/manual review with a workbook selected by the user before implementing any cleaning behavior.
