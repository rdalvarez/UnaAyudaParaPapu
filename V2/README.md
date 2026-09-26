# PapaPersonas V2

V2 currently contains only an independent WPF shell and the visual interface for **Preparar archivo de Hernán**. The first tab is intentionally inert: it shows the future workflow without reading files, detecting columns, cleaning rows, exporting output, using Sergio data, DuckDB, or persistence.

## Verification

From the repository root:

```powershell
dotnet restore V2/PapaPersonas.sln
dotnet build V2/PapaPersonas.sln -c Release --no-restore
dotnet test V2/PapaPersonas.sln -c Release --no-build
```

## Next step

Review the first tab visually before adding dynamic column detection. Later cleaning and persistence work is intentionally outside this work unit.
