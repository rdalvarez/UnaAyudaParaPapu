# Design: Process 3 Import Date Query Export

## Technical Approach

Implement this change by extending the existing Process 2 preview/apply contracts and adding a new Process 3 query/export vertical slice (Core contracts + Infrastructure DuckDB service + WPF tab control). We keep current architecture: Core defines requests/results/state; Infrastructure executes parameterized DuckDB SQL; App coordinates UI and busy/activity behavior.

## Architecture Decisions

### Decision 1 — Migration + backfill strategy

| Option | Tradeoff | Decision |
|---|---|---|
| One schema migration v4 with nullable columns + backfill `WHERE fecha_importacion IS NULL` | Simple, transactional, idempotent; relies on known legacy dataset shape | ✅ Chosen |
| Separate manual script outside migration runner | More operator control, but drifts from bootstrap consistency | Rejected |

**Choice**: Add migration version **4** in `SchemaMigrations.All` to:
1) `ALTER TABLE ... ADD COLUMN IF NOT EXISTS personas.fecha_importacion DATE` and `import_runs.fecha_importacion DATE`; 2) backfill `personas.fecha_importacion = DATE '2026-08-10' WHERE fecha_importacion IS NULL`.

**Rationale**: Existing runner is transactional/versioned. First run on legacy DB sets expected 819,530 rows; reruns are no-op (version gate + NULL predicate). `fecha_actualizacion` semantics stay unchanged.

### Decision 2 — Import date ownership and persistence

| Option | Tradeoff | Decision |
|---|---|---|
| Extend analyze/apply ownership to `(normalized source path, import date)` | Slightly more state/validation code; prevents stale apply | ✅ Chosen |
| Keep ownership by source path only | Allows wrong-date apply after date edit | Rejected |

**Choice**: Add `DateOnly ImportDate` to preview request and run metadata; update `Process2ImportSessionState` to own `AnalyzedSourcePath + AnalyzedImportDate`.

**Rationale**: Enforces spec requirement that edits after Analyze invalidate Apply intent.

## Data Flow

```text
Paso2 UI (path + D/M/Y)
  -> validate (bounded dd_MM_yyyy prefill, strict date, no future)
  -> PreviewProcessor.Analyze(request{path, importDate})
  -> import_runs(fecha_importacion, status=ready_for_confirmation)
  -> SessionState owns (normalizedPath, importDate)
  -> ApplyProcessor.Apply(importId)
  -> read import_runs.fecha_importacion
  -> upsert personas + set fecha_actualizacion=now + fecha_importacion=run date

Paso3 UI filters/columns/page
  -> QueryService.QueryPreview(request)
  -> parameterized WHERE from allowlist/operator map
  -> returns rows + total_count + latest_fecha_importacion
  -> ExportService.ExportCsv(request, token, progress)
  -> stream reader -> temp CSV -> atomic rename
```

## File Changes

| File | Action | Description |
|---|---|---|
| `src/PapaPersonas.Infrastructure/Database/SchemaMigrations.cs` | Modify | Add migration v4 columns + idempotent backfill. |
| `src/PapaPersonas.Core/Import/Paso2/SergioStagePreviewRequest.cs` | Modify | Include effective import date contract. |
| `src/PapaPersonas.Core/Import/Paso2/Process2ImportSessionState.cs` | Modify | Ownership key includes normalized path + date. |
| `src/PapaPersonas.Infrastructure/Import/Paso2/SergioPaso2PreviewProcessor.cs` | Modify | Persist `import_runs.fecha_importacion`. |
| `src/PapaPersonas.Infrastructure/Import/Paso2/SergioPaso2ApplyProcessor.cs` | Modify | Read run import date; write `personas.fecha_importacion` on update/insert. |
| `src/PapaPersonas.App/Processes/Paso2ProcessControl.xaml(.cs)` | Modify | D/M/Y TextBoxes, filename token prefill, old-date warning, invalid/future blocking, ownership invalidation on date edit. |
| `src/PapaPersonas.Core/Query/Paso3/*.cs` | Create | Query/export request-result contracts, filter DTOs, progress/cancel contracts. |
| `src/PapaPersonas.Infrastructure/Query/Paso3/DuckDbPaso3QueryService.cs` | Create | Allowlist-driven paginated preview + KPI + count. |
| `src/PapaPersonas.Infrastructure/Query/Paso3/DuckDbPaso3ExportService.cs` | Create | Streaming export with temp/rename/cancel/cleanup. |
| `src/PapaPersonas.App/Processes/Paso3ProcessControl.xaml(.cs)` | Create | Process 3 tab UI, selected columns, pagination, export controls. |
| `src/PapaPersonas.App/MainWindow.xaml(.cs)` | Modify | Enable tab 3 and wire coordinator/db path provider. |
| `tests/PapaPersonas.Tests/Database/DuckDbBootstrapperTests.cs` | Modify | Target schema version 4 and new column assertions. |
| `tests/PapaPersonas.Tests/Import/Paso2/*.cs` | Modify | Date validation, ownership invalidation, dual-date write coverage. |
| `tests/PapaPersonas.Tests/Query/Paso3/*.cs` | Create | Filter SQL mapping, ordering, pagination, export cancel/progress/cleanup tests. |
| `README.md`, `docs/DIAGRAMA_DATOS_Y_FLUJO.md`, `docs/PROCESO_2_SERGIO.md`, `docs/PROCESO_3_CONSULTA.md` | Modify/Create | Operator-facing date semantics and Process 3 behavior. |

## Interfaces / Contracts

```csharp
public sealed record SergioStagePreviewRequest(string SergioXlsxPath, string DatabasePath, DateOnly ImportDate);

public sealed record Paso3Filter(string Column, string Operator, string Value);
public sealed record Paso3PreviewRequest(
    string DatabasePath,
    string? ExactCuil,
    IReadOnlyList<Paso3Filter> Filters,
    DateOnly? ImportDateFrom,
    DateOnly? ImportDateTo,
    IReadOnlyList<string> SelectedColumns,
    int PageNumber,
    int PageSize);
```

Allowlist/operator map (no user SQL):
- Text columns: `eq`, `contains`, `starts_with`.
- Date columns (`fecha_importacion`, `fecha_actualizacion`): `eq`, `gte`, `lte`, `between`.
- CUIL filter is exact equality only.

Deterministic ordering for preview/export: `ORDER BY fecha_importacion DESC NULLS LAST, cuil ASC`.

## Testing Strategy

| Layer | What to Test | Approach |
|---|---|---|
| Unit | Filename date extraction (`(?<!\d)dd_MM_yyyy(?!\d)`), date validation, ownership invalidation by date/path | New tests for parser + `Process2ImportSessionStateTests` |
| Integration | Migration v4 idempotency, apply writes both dates, query filters/ordering/pagination, streaming export cleanup | DuckDB-backed tests mirroring current style |
| UI/Flow | Process 2 blocks invalid/future, warns old; Process 3 busy/cancel/progress and aggregate logging only | Control-level tests where feasible + manual verification checklist |

## Migration / Rollout

1. Ship migration v4 with nullable columns and idempotent legacy backfill.
2. Update Process 2 contracts/UI and apply semantics.
3. Introduce Process 3 services + tab.
4. Update docs and regression tests.

No third-party grid dependency. No loading full result sets into memory. Export is streamed row-by-row.

## Open Questions

- [ ] None blocking. Old-date warning threshold will use latest existing `personas.fecha_importacion` in DB.
