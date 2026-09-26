# process3-query-export Specification

## Requirements

### Requirement: Query Results with Counts and Latest Import KPI

The system MUST return paginated preview rows, counts for active filters, and latest `fecha_importacion`.

#### Scenario: Filtered query metrics

- GIVEN personas data exists
- WHEN operator executes query filters
- THEN preview shows current page rows and total matches
- AND KPI shows latest `fecha_importacion` in filtered results

### Requirement: Filter Semantics

The system MUST support exact CUIL equality, any-column text predicates with AND semantics, and date filters.

#### Scenario: Exact CUIL filter

- GIVEN a known CUIL
- WHEN CUIL filter is applied
- THEN only exact-equality CUIL rows are returned

#### Scenario: Any-column AND plus date predicates

- GIVEN multiple text filters and import-date bounds
- WHEN query runs
- THEN rows must satisfy all predicates

### Requirement: Column Selection and Privacy Logging

The system MUST allow column selection for preview/CSV and MUST NOT log row-level PII outside preview grid; logs SHALL be aggregate.

#### Scenario: Selected columns govern output

- GIVEN a subset of columns is selected
- WHEN preview or export executes
- THEN only selected columns appear

#### Scenario: Aggregate logs only

- GIVEN query/export runs
- WHEN logs are emitted
- THEN logs contain no row-level PII values

### Requirement: Streaming Export with Cancel and Progress

CSV export MUST stream to temp file, report progress, support cancelation, rename on success, and delete partial files on cancel/failure.

#### Scenario: Successful export

- GIVEN valid filters and destination path
- WHEN export completes
- THEN final CSV exists at destination
- AND temp artifacts are removed

#### Scenario: Canceled export

- GIVEN export is running
- WHEN operator cancels
- THEN export stops with canceled status
- AND partial files are removed
