# Paso 1 — Proceso de datos de Hernán (implemented)

This step is a preparatory helper flow. It cleans/validates Hernán input and produces the file for Sergio.

It does **not** update DuckDB `personas`.

## Distinción operativa: pedido y devolución

El archivo generado en este paso es el **pedido enviado a Sergio**. No es el archivo que vuelve de Sergio y, por lo tanto, **nunca debe cargarse en el Proceso 2**.

| Archivo | Propósito | Nombre típico | Encabezados para reconocerlo | Dónde se usa | Nunca hacer |
|---|---|---|---|---|---|
| Pedido a Sergio | Solicitar el enriquecimiento de datos. | `2 - PEDIDO_Sergio_Pegar_Domic_tel_O.S._10_08_2026.xlsx` | `CUIL`, `APELLIDO_NOMBRE`, `CD_OS`, `DESCRIPCION O_S`, `FECHA_NAC`, `EDAD` | Se envía a Sergio al finalizar Paso 1. | No cargarlo en Proceso 2 ni confirmarlo como devolución. |
| Devolución de Sergio | Incorporar los datos enriquecidos en la base maestra. | `3 - Devolucón_Sergio_10_08_2026__HERNAN_Procesado.xlsx` | `APELLIDO` y `NOMBRE` separados, más datos de domicilio y contacto, por ejemplo `DIRECCION`, `TELPART1`, `CELULAR1`, `EMAIL1`, `CODIGOOS` y `OBRASOCIAL`. | Se selecciona en Proceso 2 para analizar y, luego de la confirmación, sincronizar la base. | No confundirla con el pedido ni reemplazar el pedido por una carga de Proceso 2. |

### Encabezados observados en la devolución de Sergio

La devolución inspeccionada tiene 28 encabezados reconocidos: `CUIL`, `APELLIDO`, `NOMBRE`, `DIRECCION`, `CP`, `LOCALIDAD`, `PARTIDO`, `PROVINCIA`, `NACIONALIDAD`, `TELPART1`, `TELPART2`, `CELULAR1`, `WSP1`, `CELULAR2`, `WSP2`, `CELULAR3`, `WSP3`, `CELULAR4`, `WSP4`, `CELULAR5`, `WSP5`, `EMAIL1`, `EMAIL2`, `EMAIL3`, `CODIGOOS`, `OBRASOCIAL`, `FECNANAC`, `EDAD`.

Para la acción ante un archivo equivocado en Proceso 2, consultar [Solución de problemas: se seleccionó el pedido de Paso 1](PROCESO_2_SERGIO.md#solución-de-problemas-se-seleccionó-el-pedido-de-paso-1).

## Quick path

1. Build a `HernanPreparationRequest` with input file path and output folder.
2. Call `HernanPaso1Processor.Process(request)`.
3. Read summary/errors/notices and consume generated CSV files.

## Input and outputs

Input:

- Hernán XLSX file (`1 - PedidoOOSS_...xlsx` style).

Outputs:

- Sergio request CSV:
  - `CUIL`
  - `APELLIDO_NOMBRE`
  - `CD_OS`
  - `DESCRIPCION O_S`
  - `FECHA_NAC`
  - `EDAD`
- Rejected rows CSV:
  - `source_row_number`
  - `reason_code`
  - `raw_cuil`
  - `normalized_cuil`
  - `message`

## Editable mapping config (`config/PARA_HERNAN.json`)

Default file:

```json
{
  "outputColumns": [
    { "outputName": "CUIL", "sourceName": "CUIL" },
    { "outputName": "APELLIDO_NOMBRE", "sourceName": "CUIL_APENOM" },
    { "outputName": "CD_OS", "sourceName": "CUIL_CODOS" },
    { "outputName": "DESCRIPCION O_S", "sourceName": "CUIL_DESCRIPOS" },
    { "outputName": "FECHA_NAC", "sourceName": "CUIL_FECHANAC" },
    { "outputName": "EDAD", "sourceName": "CUIL_EDAD" }
  ]
}
```

What father/user can edit safely:

- `outputName`: column title in Sergio CSV
- `sourceName`: input header name from Hernán workbook

Validation rules:

- JSON must be valid.
- `outputName` and `sourceName` cannot be empty.
- duplicate `outputName` is blocked.
- config must include `CUIL <- CUIL` mapping.
- if configured `sourceName` is not present in current input headers, processing is blocked.

## CLI entrypoint (implemented)

```bash
dotnet run --project src/PapaPersonas.Cli -- paso1-hernan --input "<archivo-hernan.xlsx>" --output "<carpeta-de-salida>" --config "config/PARA_HERNAN.json"
```

## Header contract for Paso 1

Required prep headers:

- `CUIL`
- `CUIL_APENOM`
- `CUIL_CODOS`
- `CUIL_DESCRIPOS`
- `CUIL_FECHANAC`
- `CUIL_EDAD`

Rules:

- Missing any required prep header => blocking validation error.
- Extra/unknown Hernán headers => non-blocking notice.
- Trim + case-insensitive matching.

## CUIL + duplicates behavior

- Structural CUIL validator (ASCII digits only after removing spaces/hyphens).
- Missing/malformed CUIL => rejected CSV.
- Duplicate normalized CUIL in same batch => **all** rows for that CUIL rejected.
- No checksum validation yet (intentional boundary).

## Processing model

Current implementation uses a two-pass row iteration:

- Pass 1: collect row number + raw CUIL, run validation and duplicate classification.
- Pass 2: iterate rows again and write Sergio/rejected CSV by classification map.

This keeps behavior deterministic while avoiding full-row payload retention.

## Programmatic call

```csharp
var processor = new HernanPaso1Processor();
var result = processor.Process(new HernanPreparationRequest(
    inputFilePath: @"<archivo-hernan.xlsx>",
    outputDirectory: @"<carpeta-de-salida>"));
```

## Notes

- Excel reading: `ExcelDataReader`.
- Legacy code pages support is enabled by registering `CodePagesEncodingProvider.Instance`.
- WPF UI integration is available through separate tabs for Process 1, Process 2, and the guided Process 3 query/export flow.
- Generated CSV files under `outputs/` may contain personal data; folder is git-ignored and files should be moved/shared carefully.

## WPF usage (implemented)

In the desktop app:

1. Open **Process 1 - Paso 1** section.
2. Select **Input XLSX**.
3. Confirm or change **Output Folder**.
4. Confirm or change **Config JSON**.
5. Use **Open/Edit Config** if mapping changes are needed.
6. Click **Process Paso 1**.

Tab location:

- This flow lives in tab **1. Preparar datos de Hernán**.
- Only one top-level process tab is visible at a time.

Behavior guarantees:

- Processing runs off the UI thread.
- Input/config controls are disabled during execution.
- Double-start is prevented.
- Window close is blocked while processing is active.
- Summary shows aggregate counts only (no row-level personal data).
- Output paths and elapsed time are displayed, and output folder can be opened directly.
- Process 1 activity console is session-only (no persisted log files), bounded to latest 500 entries, and clears when a new Process 1 run starts.
- Switching to another tab and back preserves the same Process 1 console history while app remains open.
