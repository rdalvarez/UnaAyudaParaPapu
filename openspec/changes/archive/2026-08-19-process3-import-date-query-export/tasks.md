# Tasks: Process 3 Import Date Query Export

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 900-1400 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR1 Foundation/date semantics -> PR2 Process 3 backend query/export -> PR3 Process 3 UI/docs/verification |
| Delivery strategy | ask-on-risk |
| Chain strategy | stacked-to-main |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | Migration v4 + Process 2 date semantics/ownership | PR 1 | Autonomous foundation with RED/GREEN/REFACTOR tests included |
| 2 | Process 3 Core+Infrastructure query/filters/export streaming | PR 2 | Depends on PR 1 schema; include service/integration tests |
| 3 | Process 3 WPF tab wiring + docs + final verification | PR 3 | Depends on PR 2; includes manual checklist and operator docs |

## Phase 1: Foundation (Schema + Process 2 Date Semantics)

- [x] 1.1 RED: Extend `tests/PapaPersonas.Tests/Database/DuckDbBootstrapperTests.cs` for schema v4 columns and idempotent backfill count/value scenarios.
- [x] 1.2 GREEN: Update `src/PapaPersonas.Infrastructure/Database/SchemaMigrations.cs` with migration v4 (`fecha_importacion` columns + `2026-08-10` backfill on NULL).
- [x] 1.3 RED: Add/extend `tests/PapaPersonas.Tests/Import/Paso2/*` for filename token parse, invalid/future blocking, old-date warning, and analyze invalidation on file/date edit.
- [x] 1.4 GREEN: Update `src/PapaPersonas.Core/Import/Paso2/SergioStagePreviewRequest.cs` and `Process2ImportSessionState.cs` to bind ownership to normalized path + import date.
- [x] 1.5 GREEN: Update `src/PapaPersonas.Infrastructure/Import/Paso2/SergioPaso2PreviewProcessor.cs` and `SergioPaso2ApplyProcessor.cs` to persist/read run import date and write both persona dates.
- [x] 1.6 REFACTOR: Keep Process 2 validation helpers cohesive in `src/PapaPersonas.App/Processes/Paso2ProcessControl.xaml.cs` without changing behavior.

## Phase 2: Process 3 Query/Export Backend

- [x] 2.1 RED: Create `tests/PapaPersonas.Tests/Query/Paso3/*` for exact CUIL, AND text filters, date predicates, deterministic ordering, counts, and latest-import KPI.
- [x] 2.2 GREEN: Add contracts in `src/PapaPersonas.Core/Query/Paso3/*.cs` (request/filters/results/export progress-cancel).
- [x] 2.3 GREEN: Implement `src/PapaPersonas.Infrastructure/Query/Paso3/DuckDbPaso3QueryService.cs` with allowlist-only parameterized SQL and pagination.
- [x] 2.4 RED: Add export tests for temp-file streaming, atomic rename, cancel, and partial cleanup.
- [x] 2.5 GREEN: Implement `src/PapaPersonas.Infrastructure/Query/Paso3/DuckDbPaso3ExportService.cs` to satisfy streaming/cancel/progress/privacy requirements.

## Phase 3: Process 3 UI Integration

- [x] 3.1 RED: Add/extend control-level tests for Process 3 busy state, pagination actions, selected-column behavior, and aggregate-only logging expectations.
- [x] 3.2 GREEN: Create `src/PapaPersonas.App/Processes/Paso3ProcessControl.xaml` and `.xaml.cs` with guided filters (no raw SQL), preview grid, KPI, and export controls.
- [x] 3.3 GREEN: Update `src/PapaPersonas.App/MainWindow.xaml` and `.xaml.cs` to enable Tab 3 and wire Process 3 services with current DB path provider.

## Phase 4: Documentation and Operator Guidance

- [x] 4.1 Update `docs/PROCESO_2_SERGIO.md` and `docs/DIAGRAMA_DATOS_Y_FLUJO.md` with import-date semantics, analyze ownership rules, and backfill note.
- [x] 4.2 Create/update `docs/PROCESO_3_CONSULTA.md` stating Process 3 V1 is simple guided extraction (not raw SQL) and DBeaver is approved fallback for advanced/complex queries.
- [x] 4.3 Update `README.md` with Process 3 scope, privacy logging rule, and export workflow summary.

## Phase 5: Explicit Verification Gates

- [x] 5.1 Build verification: `dotnet build PapaPersonas.sln -c Release`.
- [x] 5.2 Test verification (strict TDD gate): `dotnet test PapaPersonas.sln -c Release`.
- [x] 5.3 Migration/backfill validation: verify both new columns exist and backfill rows remain `819530` with `fecha_importacion='2026-08-10'` after rerun.
- [x] 5.4 UI manual checklist: Process 2 prefill/edit/block/warn/re-analyze flows; Process 3 filter semantics, column selection, pagination, export success/cancel cleanup, and no PII in logs. Validated by user on 2026-08-19.
- [x] 5.5 Incident-prevention hardening: add testable startup DB path resolver with `PAPAPERSONAS_DB_PATH` override, wire app startup to resolver, and document disposable smoke DB + graceful shutdown policy.
