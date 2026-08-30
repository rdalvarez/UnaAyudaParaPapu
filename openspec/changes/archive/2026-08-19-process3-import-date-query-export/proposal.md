# Proposal: Process 3 Import Date Query Export

## Intent

Add import-date semantics to Process 2 and Process 3 query/export so Papa can know currentness, filter, and export CSVs without raw SQL or third-party grids.

## Scope

### In Scope
- Add `fecha_importacion DATE` to `personas` and `import_runs`; keep `fecha_actualizacion` as automatic apply timestamp.
- Process 2 extracts one bounded valid `dd_MM_yyyy` filename token, prefills editable day/month/year TextBoxes, and requires manual entry when missing/ambiguous/invalid.
- Analyze requires and persists a valid import date; file/date edits after Analyze invalidate Apply ownership.
- Apply writes system timestamp now plus run import date to every valid row touched/inserted.
- Idempotent backfill sets 819,530 rows to `2026-08-10` from verified file `3 - Devolucón_Sergio_10_08_2026__HERNAN_Procesado.xlsx`.
- Process 3 adds counts, latest import date KPI, exact CUIL search, any-column AND/date filters, paginated preview, selectable columns, and streaming CSV export with temp-file/rename/cancel/progress.
- Privacy: no PII in console/logs beyond the preview grid; aggregate console messages only.

### Out of Scope
- Raw SQL, third-party grids, created/material-change dates, and guessing from other filename patterns.

## Capabilities

### New Capabilities
- `process3-query-export`: Process 3 read/query/preview/column selection/CSV export behavior.

### Modified Capabilities
- `process2-import-apply`: import date capture, validation, ownership invalidation, run persistence, row timestamp writes.
- `database-schema`: date columns and explicit historical backfill migration.

## Approach

Use built-in WPF controls/DataGrid over DuckDB services. Build parameterized filters from allowed `personas` columns only. Stream exports to temp file, rename on success, support cancel/progress, and delete partials. Date parts validate strictly: incomplete/impossible/future block; old dates warn to later load the newer file.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Core/Import/Paso2` | Modified | Date request/ownership. |
| `Infrastructure/Import/Paso2` | Modified | Persistence. |
| `Infrastructure/Database` | Modified | Migration/backfill. |
| `App/Processes` | New/Modified | Process 2 UI; Process 3 tab. |
| `tests`, `docs` | Modified/New | Coverage and operator semantics. |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Backfill misread as history | Med | Explicit migration and docs. |
| Unsafe/slow filters | Med | Allowlist, parameters, pagination, streaming. |
| Stale apply after date edit | Low | Require Analyze again. |

## Rollback Plan

Disable Process 3, revert Process 2 UI/contracts, and restore pre-migration backup. If schema applied, run reviewed down migration or leave nullable columns unused.

## Dependencies

- DuckDB, WPF tab shell, Process 2 staging/apply.

## Success Criteria

- [ ] Invalid/future/incomplete dates block; old dates warn; valid token prefills fields.
- [ ] Analyze owns file + date; edits force re-analyze.
- [ ] Apply/backfill produce correct `fecha_importacion` and `fecha_actualizacion`.
- [ ] Process 3 filters, preview, counts, KPI, CSV, cancel/progress, and PII-safe logging work.
