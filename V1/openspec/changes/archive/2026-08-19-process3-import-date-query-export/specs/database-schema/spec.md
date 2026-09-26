# database-schema Specification

## Requirements

### Requirement: Date Columns and Semantics

Schema MUST add nullable `fecha_importacion DATE` to `personas` and `import_runs` and SHALL preserve `fecha_actualizacion` as apply-time timestamp.

#### Scenario: Post-migration schema contract

- GIVEN schema migrations are applied
- WHEN tables are inspected
- THEN both tables expose `fecha_importacion`
- AND `fecha_actualizacion` behavior is unchanged

### Requirement: Explicit Historical Backfill

The system MUST run an idempotent migration setting `personas.fecha_importacion = 2026-08-10` for existing 819,530 rows sourced from `3 - Devolucón_Sergio_10_08_2026__HERNAN_Procesado.xlsx`.

#### Scenario: Initial backfill

- GIVEN backfill has not run
- WHEN migration executes
- THEN exactly 819,530 target rows are set to `2026-08-10`

#### Scenario: Re-run backfill

- GIVEN backfill already ran
- WHEN migration executes again
- THEN row counts and values remain unchanged
