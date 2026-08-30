# Proceso 2 — Sergio (2A + 2B backend + WPF flow implemented)

This document describes implemented backend behavior for Process 2A and 2B.

## Quick path

1. Read Sergio XLSX headers.
2. Validate headers with the conservative `SergioReturn` contract.
3. If unknown headers exist without structural errors, list them and ask **Continuar** or **Cancelar** before opening DuckDB.
4. On Continue, create `import_runs` row (`stage_type=sergio_return`, `status=analyzing`) and process only recognized columns.
5. Stream rows into `personas_staging` with row-level validation outcomes.
6. Mark duplicate CUIL groups rejected (all rows in each duplicate group).
7. Compute preview counts and update `import_runs` to `ready_for_confirmation`.
8. On explicit confirmation, apply valid staging rows to `personas` transactionally.

## Why this design exists

| Decision | Intent |
|---|---|
| Sergio unknown headers require explicit operator choice | Unknown columns are listed and ignored only after Continue; Cancel leaves database and import state untouched. |
| Staging first, never `personas` directly | Preview/confirmation flow requires a safe intermediate snapshot and reversible failure boundaries. |
| CUIL stored as text | Preserve leading zeros and source formatting fidelity for identifiers. |
| Duplicate CUIL rejects all rows in group | Prevent arbitrary winner selection and force explicit human reconciliation. |
| `source_columns_present_json` per import | Preserves absent-column vs present-empty semantics for future merge logic. |
| Apply is blocked unless run is `ready_for_confirmation` | Prevents accidental/double apply and preserves explicit confirmation semantics. |

## Header governance

- Matching is conservative: trim and case-insensitive comparison only.
- Unknown headers are returned in source order and never become DuckDB columns.
- Continue processes only recognized fields; unknown fields do not enter staging, `source_columns_present_json`, Apply SQL, or `personas`.
- Missing CUIL, duplicate raw headers, and aliases colliding on one canonical field are structural blockers and never offer Continue.
- The confirmation is bound to the exact ordered source-header shape. If the file changes, Analyze must be repeated.

## Process 2B apply semantics (implemented backend)

- Runs only when `import_runs.stage_type=sergio_return` and `status=ready_for_confirmation`.
- Transition to `applying` is atomic (`WHERE status='ready_for_confirmation'`) to prevent concurrent double-apply races.
- Applies only `personas_staging.validation_outcome='valid'` rows for that import.
- Uses `cuil` as immutable conflict key (primary key in `personas`).
- Uses `source_columns_present_json` to decide update behavior:
  - field absent in JSON => preserve existing value,
  - field present and staging value null => clear value,
  - field present and staging value non-null => replace value.
- Always sets `fecha_actualizacion` for inserted/updated rows.
- Rejected staging rows never mutate `personas`.
- Apply is transactional; any SQL failure rolls back to avoid half-applied imports.
- Unsupported canonical fields in `source_columns_present_json` fail apply (schema-drift protection).
- Zero-valid imports are allowed to complete with 0 applied rows (and optional rejected CSV).

## Import-date semantics and Analyze ownership

- Process 2 now distinguishes two dates with different meaning:
  - `fecha_importacion` = fecha efectiva del archivo Sergio que se está aplicando.
  - `fecha_actualizacion` = timestamp técnico del momento de Apply.
- Analyze/Apply ownership is bound to **(source path + import date)**.
- If the user edits file path or import date after Analyze, Apply ownership is invalidated and Analyze must run again.
- Invalid/incomplete/future import date blocks Analyze and Apply.
- Older-but-valid import date only warns (does not block) to support controlled backfills/replays.
- Apply writes both dates for affected personas and persists run date in `import_runs.fecha_importacion`.

### Backfill note (legacy cohort)

- Migration v4 adds `fecha_importacion` columns and includes an idempotent backfill for legacy cohort rows that had NULL.
- Expected legacy value is `2026-08-10` for the known historical baseline.
- Reruns must keep the backfill stable (no double mutation and no overwrite of non-NULL values).

Rejected CSV in Process 2B:

- Optional export for rejected staging rows.
- Columns: `source_row_number`, `reason_code`, `normalized_cuil`.
- Limitation: raw CUIL is not persisted in staging, so export does not include raw CUIL.

## Process 2A output

- `import_id`
- status (`ready_for_confirmation` on success)
- totals: total/valid/rejected/missing/malformed/duplicate
- preview impact: `rows_to_insert`, `rows_to_update`
- elapsed time
- non-PII failure message on error

## Process 2B output

- apply status (`completed` on success)
- counts: applied/inserted/updated/rejected-exported
- optional rejected CSV path
- elapsed time
- non-PII failure message on error

## Implemented boundaries

Implemented in this slice:

- Conservative Sergio XLSX header validation with typed unknown-header confirmation.
- Streaming load into `personas_staging`.
- CUIL structural validation and duplicate-group rejection.
- Row-level typed validation for `fecha_nacimiento`, `edad`, `anio`.
- `import_runs` lifecycle for preview analysis.
- Process 2B transactional apply from staging to personas.
- Process 2B optional rejected CSV export.

## WPF integration (implemented)

- Process 2 is in tab **2. Actualizar base de Sergio**.
- Analyze button executes Process 2A asynchronously and keeps the UI responsive.
- Preview result stores only the current `import_id` as ownership token for apply.
- Confirm-and-apply button shows explicit warning that backup is recommended and that apply updates definitive `personas`.
- Apply executes Process 2B asynchronously for the owned `import_id` only.
- After successful apply, UI clears pending ownership to avoid user-triggered repeat apply.
- UI summaries are aggregate-only; row-level PII values are intentionally never displayed.
- Process 2 has its own session-only activity console (no persisted log files), capped to latest 500 entries.
- Process 2 console lifecycle:
  - `Analyze` starts a new execution and clears this console.
  - `Apply` appends to the same console generated by that Analyze.
  - running another Analyze clears it again.

## Mantenimiento relacionado

Restore reemplaza el snapshot operativo completo y luego refresca los procesos. Si el snapshot restaurado contiene una extracción pendiente, Proceso 4 la muestra inmediatamente para resolverla con Reintentar o Cancelar, incluso si el CSV externo ya no existe.
